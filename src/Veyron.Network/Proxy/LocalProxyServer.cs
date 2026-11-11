
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Veyron.Core.Enums;
using Veyron.Core.Models;
using Veyron.Network.Routing;

namespace Veyron.Network.Proxy;

public sealed class LocalProxyServer : IAsyncDisposable
{
    private readonly ConnectionProfile profile;
    private readonly TcpListener listener;

    private CancellationTokenSource? cancellation;
    private Task? acceptTask;

    public int Port { get; }

    public bool IsRunning => cancellation != null;

    private readonly ConcurrentDictionary<Guid, ProxyConnectionInfo>
    connections = new();

    public event Action<ProxyConnectionInfo>? ConnectionUpdated;

    public IReadOnlyCollection<ProxyConnectionInfo> Connections =>
        connections.Values.ToArray();

    private long totalBytesSent;
    private long totalBytesReceived;

    public long TotalBytesSent =>
        Interlocked.Read(ref totalBytesSent);

    public long TotalBytesReceived =>
        Interlocked.Read(ref totalBytesReceived);

    public int ActiveConnections =>
        connections.Count;

    private readonly TrafficRouter trafficRouter = new();
    private readonly RoutingProfile routingProfile;

    private void NotifyConnection(ProxyConnectionInfo info)
    {
        ConnectionUpdated?.Invoke(info);
    }

    public LocalProxyServer(
    ConnectionProfile profile,
    int localPort = 2080,
    RoutingProfile? routingProfile = null)
    {
        this.profile = profile ??
            throw new ArgumentNullException(nameof(profile));

        if (localPort < 1 || localPort > 65535)
            throw new ArgumentOutOfRangeException(nameof(localPort));

        if (profile.Type is not (
            ConnectionType.HTTP or
            ConnectionType.SOCKS5))
        {
            throw new NotSupportedException(
                $"Local proxy does not support {profile.Type} yet.");
        }

        if (string.IsNullOrWhiteSpace(profile.Host) ||
            profile.Port < 1 ||
            profile.Port > 65535)
        {
            throw new ArgumentException("Invalid upstream proxy.");
        }

        this.routingProfile = routingProfile ?? new RoutingProfile
        {
            Name = "Default",
            Mode = RoutingMode.GlobalProxy
        };

        Port = localPort;

        listener = new TcpListener(
            IPAddress.Loopback,
            Port);
    }

    public Task StartAsync()
    {
        if (IsRunning)
            return Task.CompletedTask;

        cancellation = new CancellationTokenSource();

        listener.Start();

        acceptTask = AcceptLoopAsync(cancellation.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var cts = cancellation;

        if (cts == null)
            return;

        cancellation = null;

        cts.Cancel();
        listener.Stop();

        try
        {
            if (acceptTask != null)
                await acceptTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        cts.Dispose();
        acceptTask = null;
    }

    private async Task AcceptLoopAsync(
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException) when (token.IsCancellationRequested)
            {
                break;
            }

            _ = HandleClientAsync(client, token);
        }
    }


    private async Task HandleClientAsync(
        TcpClient localClient,
        CancellationToken token)
    {
        ProxyConnectionInfo? connectionInfo = null;
        bool connected = false;

        using (localClient)
        {
            try
            {
                localClient.NoDelay = true;

                NetworkStream localStream = localClient.GetStream();

                string headers = await ReadHeadersAsync(
                    localStream,
                    token);

                string firstLine = headers.Split(
                    new[] { "\r\n" },
                    StringSplitOptions.None)[0];

                string[] parts = firstLine.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 3 ||
                    !parts[0].Equals(
                        "CONNECT",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await SendResponseAsync(
                        localStream,
                        "HTTP/1.1 501 Not Implemented\r\n" +
                        "Connection: close\r\n\r\n",
                        token);

                    return;
                }

                if (!Uri.TryCreate(
                    "tcp://" + parts[1],
                    UriKind.Absolute,
                    out Uri? target))
                {
                    await SendResponseAsync(
                        localStream,
                        "HTTP/1.1 400 Bad Request\r\n" +
                        "Connection: close\r\n\r\n",
                        token);

                    return;
                }

                string targetHost = target.Host;
                int targetPort = target.Port;

                if (targetPort < 1 || targetPort > 65535)
                {
                    await SendResponseAsync(
                        localStream,
                        "HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n",
                        token);

                    return;
                }

                // Определяем маршрут
                RoutingAction route = trafficRouter.Resolve(
                    targetHost,
                    routingProfile);

                // Создаём информацию о подключении
                connectionInfo = new ProxyConnectionInfo
                {
                    Destination = $"{targetHost}:{targetPort}",
                    Status = "Connecting"
                };

                connections.TryAdd(connectionInfo.Id, connectionInfo);
                NotifyConnection(connectionInfo);

                // Блокировка
                if (route == RoutingAction.Block)
                {
                    connectionInfo.Status = "Blocked";
                    NotifyConnection(connectionInfo);

                    await SendResponseAsync(
                        localStream,
                        "HTTP/1.1 403 Forbidden\r\n" +
                        "Connection: close\r\n\r\n",
                        token);

                    return;
                }

                using var upstream = new TcpClient
                {
                    NoDelay = true
                };

                if (route == RoutingAction.Direct)
                {
                    // Прямое подключение к целевому серверу
                    await upstream.ConnectAsync(
                        targetHost,
                        targetPort,
                        token);
                }
                else if (route == RoutingAction.Proxy)
                {
                    // Подключение через выбранный прокси
                    await upstream.ConnectAsync(
                        profile.Host,
                        profile.Port,
                        token);

                    NetworkStream upstreamStream = upstream.GetStream();

                    if (profile.Type == ConnectionType.HTTP)
                    {
                        await EstablishHttpTunnelAsync(
                            upstreamStream,
                            targetHost,
                            targetPort,
                            token);
                    }
                    else if (profile.Type == ConnectionType.SOCKS5)
                    {
                        await EstablishSocks5TunnelAsync(
                            upstreamStream,
                            targetHost,
                            targetPort,
                            token);
                    }
                }

                await SendResponseAsync(
                    localStream,
                    "HTTP/1.1 200 Connection Established\r\n" +
                    "Proxy-Agent: Veyron\r\n\r\n",
                    token);

                connected = true;
                connectionInfo.Status = "Connected";

                NotifyConnection(connectionInfo);

                await RelayAsync(
                    localClient,
                    upstream,
                    connectionInfo,
                    token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                if (connectionInfo != null)
                    connectionInfo.Status = "Failed";

                try
                {
                    if (localClient.Connected)
                    {
                        await SendResponseAsync(
                            localClient.GetStream(),
                            "HTTP/1.1 502 Bad Gateway\r\n" +
                            "Connection: close\r\n\r\n",
                            CancellationToken.None);
                    }
                }
                catch
                {
                }
            }
            finally
            {
                if (connectionInfo != null)
                {
                    if (connected)
                        connectionInfo.Status = "Disconnected";

                    NotifyConnection(connectionInfo);

                    connections.TryRemove(
                        connectionInfo.Id,
                        out _);
                }
            }
        }
    }


    private async Task EstablishHttpTunnelAsync(
        NetworkStream stream,
        string host,
        int port,
        CancellationToken token)
    {
        string request =
            $"CONNECT {host}:{port} HTTP/1.1\r\n" +
            $"Host: {host}:{port}\r\n" +
            "Proxy-Connection: Keep-Alive\r\n\r\n";

        byte[] bytes = Encoding.ASCII.GetBytes(request);

        await stream.WriteAsync(bytes, token);

        string response = await ReadHeadersAsync(
            stream,
            token);

        string statusLine = response.Split(
            new[] { "\r\n" },
            StringSplitOptions.None)[0];

        string[] parts = statusLine.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 ||
            !int.TryParse(parts[1], out int statusCode) ||
            statusCode != 200)
        {
            throw new IOException(
                $"Upstream HTTP proxy rejected CONNECT: {statusLine}");
        }
    }

    private async Task EstablishSocks5TunnelAsync(
        NetworkStream stream,
        string host,
        int port,
        CancellationToken token)
    {
        // SOCKS5 greeting: no authentication.
        await stream.WriteAsync(
            new byte[] { 0x05, 0x01, 0x00 },
            token);

        byte[] greeting = await ReadExactlyAsync(
            stream,
            2,
            token);

        if (greeting[0] != 0x05 || greeting[1] != 0x00)
            throw new IOException("SOCKS5 authentication method rejected.");

        byte[] domain = Encoding.ASCII.GetBytes(host);

        if (domain.Length == 0 || domain.Length > 255)
            throw new IOException("Invalid SOCKS5 destination hostname.");

        byte[] request = new byte[7 + domain.Length];

        request[0] = 0x05;
        request[1] = 0x01;
        request[2] = 0x00;
        request[3] = 0x03;
        request[4] = (byte)domain.Length;

        Buffer.BlockCopy(
            domain,
            0,
            request,
            5,
            domain.Length);

        request[^2] = (byte)(port >> 8);
        request[^1] = (byte)(port & 0xFF);

        await stream.WriteAsync(request, token);

        byte[] response = await ReadExactlyAsync(
            stream,
            4,
            token);

        if (response[0] != 0x05 || response[1] != 0x00)
        {
            throw new IOException(
                $"SOCKS5 connection rejected. Code: {response[1]}");
        }

        int addressLength = response[3] switch
        {
            0x01 => 4,
            0x04 => 16,
            0x03 => (await ReadExactlyAsync(
                stream, 1, token))[0],
            _ => throw new IOException(
                "Invalid SOCKS5 address type.")
        };

        await ReadExactlyAsync(
            stream,
            addressLength + 2,
            token);
    }


    private async Task RelayAsync(
        TcpClient local,
        TcpClient upstream,
        ProxyConnectionInfo info,
        CancellationToken token)
    {
        using var relayCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(token);

        NetworkStream localStream = local.GetStream();
        NetworkStream upstreamStream = upstream.GetStream();

        Task upload = TransferAsync(
            localStream,
            upstreamStream,
            info,
            true,
            relayCancellation.Token);

        Task download = TransferAsync(
            upstreamStream,
            localStream,
            info,
            false,
            relayCancellation.Token);

        await Task.WhenAny(upload, download);

        relayCancellation.Cancel();

        local.Close();
        upstream.Close();

        try
        {
            await Task.WhenAll(upload, download);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }


    private async Task TransferAsync(
        Stream source,
        Stream destination,
        ProxyConnectionInfo info,
        bool isUpload,
        CancellationToken token)
    {
        byte[] buffer = new byte[81920];

        while (!token.IsCancellationRequested)
        {
            int count = await source.ReadAsync(
                buffer.AsMemory(0, buffer.Length),
                token);

            if (count == 0)
                break;

            await destination.WriteAsync(
                buffer.AsMemory(0, count),
                token);

            if (isUpload)
            {
                Interlocked.Add(ref totalBytesSent, count);
                info.BytesSent += count;
            }
            else
            {
                Interlocked.Add(ref totalBytesReceived, count);
                info.BytesReceived += count;
            }

            NotifyConnection(info);
        }
    }


    private static async Task<byte[]> ReadExactlyAsync(
        Stream stream,
        int length,
        CancellationToken token)
    {
        byte[] result = new byte[length];

        int offset = 0;

        while (offset < length)
        {
            int read = await stream.ReadAsync(
                result.AsMemory(offset, length - offset),
                token);

            if (read == 0)
                throw new IOException("Unexpected end of stream.");

            offset += read;
        }

        return result;
    }

    private static Task SendResponseAsync(
        Stream stream,
        string response,
        CancellationToken token)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(response);

        return stream.WriteAsync(bytes, token).AsTask();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private static async Task<string> ReadHeadersAsync(
    NetworkStream stream,
    CancellationToken token)
    {
        const int maxHeaderSize = 32768;

        using var memory = new MemoryStream();

        byte[] buffer = new byte[1];

        while (memory.Length < maxHeaderSize)
        {
            int read = await stream.ReadAsync(buffer, token);

            if (read == 0)
                throw new IOException("Connection closed while reading headers.");

            memory.WriteByte(buffer[0]);

            byte[] data = memory.GetBuffer();
            int length = (int)memory.Length;

            if (length >= 4 &&
                data[length - 4] == '\r' &&
                data[length - 3] == '\n' &&
                data[length - 2] == '\r' &&
                data[length - 1] == '\n')
            {
                return Encoding.ASCII.GetString(
                    data,
                    0,
                    length);
            }
        }

        throw new IOException("HTTP headers are too large.");
    }
}
