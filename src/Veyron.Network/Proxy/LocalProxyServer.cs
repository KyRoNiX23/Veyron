
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    private const int MaxHeaderSize = 32768;
    private const int BufferSize = 81920;

    private readonly ConnectionProfile profile;
    private readonly TcpListener listener;
    private readonly TrafficRouter trafficRouter = new();
    private readonly RoutingProfile routingProfile;

    private readonly ConcurrentDictionary<Guid, ProxyConnectionInfo>
        connections = new();

    private readonly ConcurrentDictionary<Guid, TcpClient>
        clients = new();

    private readonly ConcurrentDictionary<Guid, Task>
        connectionTasks = new();

    private readonly SemaphoreSlim lifecycleLock = new(1, 1);

    private CancellationTokenSource? cancellation;
    private Task? acceptTask;

    private long totalBytesSent;
    private long totalBytesReceived;

    private bool disposed;

    public int Port { get; }

    public bool IsRunning =>
        cancellation != null &&
        !cancellation.IsCancellationRequested;

    public long TotalBytesSent =>
        Interlocked.Read(ref totalBytesSent);

    public long TotalBytesReceived =>
        Interlocked.Read(ref totalBytesReceived);

    public int ActiveConnections =>
        connections.Count;

    public IReadOnlyCollection<ProxyConnectionInfo> Connections =>
        connections.Values.ToArray();

    public event Action<ProxyConnectionInfo>? ConnectionUpdated;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    public LocalProxyServer(
        ConnectionProfile profile,
        int localPort = 2080,
        RoutingProfile? routingProfile = null)
    {
        this.profile = profile ??
            throw new ArgumentNullException(nameof(profile));

        if (localPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(localPort));

        if (profile.Type is not (
            ConnectionType.HTTP or
            ConnectionType.SOCKS5))
        {
            throw new NotSupportedException(
                $"Unsupported upstream proxy: {profile.Type}");
        }

        if (string.IsNullOrWhiteSpace(profile.Host) ||
            profile.Port is < 1 or > 65535)
        {
            throw new ArgumentException(
                "Invalid upstream proxy configuration.");
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

    public async Task StartAsync()
    {
        await lifecycleLock.WaitAsync();

        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (IsRunning)
                return;

            var cts = new CancellationTokenSource();

            try
            {
                listener.Start();

                Debug.WriteLine(
                    $"[Veyron Proxy] STARTED: 127.0.0.1:{Port}");

                Debug.WriteLine(
                    $"[Veyron Proxy] IsListening: {listener.Server.IsBound}");

                cancellation = cts;

                acceptTask = AcceptLoopAsync(cts.Token);

                Debug.WriteLine(
                    $"[Veyron Proxy] Listening on 127.0.0.1:{Port}");
            }
            catch
            {
                Debug.WriteLine($"[Veyron Proxy] STOPPING: 127.0.0.1:{Port}");
                listener.Stop();
                cancellation = null;
                acceptTask = null;
                cts.Dispose();

                throw;
            }
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await lifecycleLock.WaitAsync();

        try
        {
            var cts = cancellation;

            if (cts == null)
                return;

            cancellation = null;

            cts.Cancel();
            listener.Stop();

            foreach (var client in clients.Values)
            {
                try
                {
                    client.Close();
                }
                catch
                {
                }
            }

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

            Task[] activeTasks = connectionTasks.Values.ToArray();

            try
            {
                await Task.WhenAll(activeTasks);
            }
            catch
            {
            }

            cts.Dispose();
            acceptTask = null;

            Debug.WriteLine("[Veyron Proxy] Stopped.");
        }
        finally
        {
            lifecycleLock.Release();
        }
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
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[Veyron Proxy] Accept error: {ex.Message}");

                break;
            }

            Guid id = Guid.NewGuid();

            clients.TryAdd(id, client);

            Task task = HandleClientTrackedAsync(
                id,
                client,
                token);

            connectionTasks.TryAdd(id, task);
        }
    }

    private async Task HandleClientAsync(
        Guid clientId,
        TcpClient localClient,
        CancellationToken token)
    {
        ProxyConnectionInfo? info = null;
        bool connected = false;

        using (localClient)
        {
            try
            {
                localClient.NoDelay = true;

                NetworkStream localStream =
                    localClient.GetStream();

                string headers = await ReadHeadersAsync(
                    localStream,
                    token);

                string[] lines = headers.Split(
                    new[] { "\r\n" },
                    StringSplitOptions.None);

                string firstLine = lines[0];

                Debug.WriteLine(
                    $"[Veyron Proxy] Request: {firstLine}");

                string[] parts = firstLine.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 3)
                {
                    await SendStatusAsync(
                        localStream,
                        400,
                        "Bad Request",
                        token);

                    return;
                }

                if (!parts[0].Equals(
                    "CONNECT",
                    StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine(
                        $"[Veyron Proxy] Unsupported method: {parts[0]}");

                    await SendStatusAsync(
                        localStream,
                        405,
                        "Method Not Allowed",
                        token,
                        "Allow: CONNECT\r\n");

                    return;
                }

                if (!TryParseAuthority(
                    parts[1],
                    out string targetHost,
                    out int targetPort))
                {
                    await SendStatusAsync(
                        localStream,
                        400,
                        "Bad Request",
                        token);

                    return;
                }

                info = new ProxyConnectionInfo
                {
                    Destination = $"{targetHost}:{targetPort}",
                    Status = "Connecting"
                };

                connections.TryAdd(info.Id, info);
                NotifyConnection(info);

                RoutingAction route = trafficRouter.Resolve(
                    targetHost,
                    routingProfile);

                Debug.WriteLine($"========== VEYRON ROUTING ==========");
                Debug.WriteLine($"Destination: {targetHost}:{targetPort}");
                Debug.WriteLine($"Action: {route}");
                Debug.WriteLine($"Upstream type: {profile.Type}");
                Debug.WriteLine($"Upstream host: {profile.Host}:{profile.Port}");
                Debug.WriteLine($"====================================");

                if (route == RoutingAction.Block)
                {
                    info.Status = "Blocked";
                    NotifyConnection(info);

                    await SendStatusAsync(
                        localStream,
                        403,
                        "Forbidden",
                        token);

                    return;
                }

                using var upstream = new TcpClient
                {
                    NoDelay = true
                };

                if (route == RoutingAction.Direct)
                {
                    await ConnectTcpAsync(
                        upstream,
                        targetHost,
                        targetPort,
                        token);
                }
                else if (route == RoutingAction.Proxy)
                {
                    await ConnectTcpAsync(
                        upstream,
                        profile.Host,
                        profile.Port,
                        token);

                    NetworkStream upstreamStream =
                        upstream.GetStream();

                    if (profile.Type == ConnectionType.HTTP)
                    {
                        await EstablishHttpTunnelAsync(
                            upstreamStream,
                            targetHost,
                            targetPort,
                            token);
                    }
                    else
                    {
                        await EstablishSocks5TunnelAsync(
                            upstreamStream,
                            targetHost,
                            targetPort,
                            token);
                    }
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Unknown routing action: {route}");
                }

                await SendResponseAsync(
                    localStream,
                    "HTTP/1.1 200 Connection Established\r\n" +
                    "Proxy-Agent: Veyron\r\n" +
                    "Connection: keep-alive\r\n\r\n",
                    token);

                connected = true;

                info.Status = "Connected";
                NotifyConnection(info);

                Debug.WriteLine(
                    $"[Veyron Proxy] Connected: {info.Destination}");

                await RelayAsync(
                    localClient,
                    upstream,
                    info,
                    token);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine(
                    $"[Veyron Proxy] Connection cancelled: {clientId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[Veyron Proxy] Connection failed: {ex}");

                if (info != null)
                    info.Status = "Failed";

                try
                {
                    if (localClient.Connected)
                    {
                        await SendStatusAsync(
                            localClient.GetStream(),
                            502,
                            "Bad Gateway",
                            CancellationToken.None);
                    }
                }
                catch
                {
                }
            }
            finally
            {
                if (info != null)
                {
                    if (connected)
                        info.Status = "Disconnected";

                    NotifyConnection(info);

                    connections.TryRemove(
                        info.Id,
                        out _);
                }

                clients.TryRemove(clientId, out _);
            }
        }
    }

    private static bool TryParseAuthority(
        string authority,
        out string host,
        out int port)
    {
        host = string.Empty;
        port = 0;

        if (string.IsNullOrWhiteSpace(authority))
            return false;

        if (!Uri.TryCreate(
            "http://" + authority,
            UriKind.Absolute,
            out Uri? uri))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
            return false;

        if (uri.Port is < 1 or > 65535)
            return false;

        host = uri.Host;
        port = uri.Port;

        return true;
    }

    private async Task EstablishHttpTunnelAsync(
        NetworkStream stream,
        string host,
        int port,
        CancellationToken token)
    {
        string request =
            $"CONNECT {FormatAuthority(host, port)} HTTP/1.1\r\n" +
            $"Host: {FormatAuthority(host, port)}\r\n" +
            "Proxy-Connection: Keep-Alive\r\n\r\n";

        await SendResponseAsync(
            stream,
            request,
            token);

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
        await stream.WriteAsync(
            new byte[] { 0x05, 0x01, 0x00 },
            token);

        byte[] greeting = await ReadExactlyAsync(
            stream,
            2,
            token);

        if (greeting[0] != 0x05 || greeting[1] != 0x00)
        {
            throw new IOException(
                "SOCKS5 proxy does not accept no-authentication mode.");
        }

        byte[] addressBytes;
        byte addressType;

        if (IPAddress.TryParse(host, out IPAddress? ip))
        {
            addressBytes = ip.GetAddressBytes();

            addressType = ip.AddressFamily == AddressFamily.InterNetwork
                ? (byte)0x01
                : (byte)0x04;
        }
        else
        {
            addressBytes = Encoding.ASCII.GetBytes(host);

            if (addressBytes.Length is < 1 or > 255)
                throw new IOException("Invalid SOCKS5 hostname.");

            addressType = 0x03;
        }

        int domainLength = addressType == 0x03 ? 1 : 0;

        byte[] request = new byte[
            4 + domainLength + addressBytes.Length + 2];

        request[0] = 0x05;
        request[1] = 0x01;
        request[2] = 0x00;
        request[3] = addressType;

        int offset = 4;

        if (addressType == 0x03)
            request[offset++] = (byte)addressBytes.Length;

        Buffer.BlockCopy(
            addressBytes,
            0,
            request,
            offset,
            addressBytes.Length);

        offset += addressBytes.Length;

        request[offset++] = (byte)(port >> 8);
        request[offset] = (byte)(port & 0xFF);

        await stream.WriteAsync(request, token);

        byte[] response = await ReadExactlyAsync(
            stream,
            4,
            token);

        if (response[0] != 0x05 || response[1] != 0x00)
        {
            throw new IOException(
    $"SOCKS5 rejected connection: " +
    GetSocks5Error(response[1]) +
    $" (0x{response[1]:X2})");
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
        NetworkStream localStream = local.GetStream();
        NetworkStream upstreamStream = upstream.GetStream();

        Task upload = TransferAsync(
            localStream,
            upstreamStream,
            info,
            true,
            token);

        Task download = TransferAsync(
            upstreamStream,
            localStream,
            info,
            false,
            token);

        await Task.WhenAll(upload, download);
    }

    private async Task TransferAsync(
        Stream source,
        Stream destination,
        ProxyConnectionInfo info,
        bool upload,
        CancellationToken token)
    {
        byte[] buffer = new byte[BufferSize];

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

            if (upload)
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

    private static async Task<string> ReadHeadersAsync(
        NetworkStream stream,
        CancellationToken token)
    {
        using var memory = new MemoryStream();

        byte[] buffer = new byte[1];

        while (memory.Length < MaxHeaderSize)
        {
            int read = await stream.ReadAsync(
                buffer.AsMemory(0, 1),
                token);

            if (read == 0)
            {
                throw new IOException(
                    "Connection closed while reading HTTP headers.");
            }

            memory.WriteByte(buffer[0]);

            if (memory.Length >= 4)
            {
                byte[] data = memory.GetBuffer();
                int length = (int)memory.Length;

                if (data[length - 4] == 13 &&
                    data[length - 3] == 10 &&
                    data[length - 2] == 13 &&
                    data[length - 1] == 10)
                {
                    return Encoding.ASCII.GetString(
                        data,
                        0,
                        length);
                }
            }
        }

        throw new IOException(
            "HTTP headers exceeded the maximum allowed size.");
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

    private static Task SendStatusAsync(
        Stream stream,
        int statusCode,
        string reason,
        CancellationToken token,
        string additionalHeaders = "")
    {
        string response =
            $"HTTP/1.1 {statusCode} {reason}\r\n" +
            additionalHeaders +
            "Connection: close\r\n\r\n";

        return SendResponseAsync(
            stream,
            response,
            token);
    }

    private static Task SendResponseAsync(
        Stream stream,
        string response,
        CancellationToken token)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(response);

        return stream.WriteAsync(
            bytes.AsMemory(),
            token).AsTask();
    }

    private static string FormatAuthority(
        string host,
        int port)
    {
        string formattedHost = host.Contains(':') &&
                               !host.StartsWith("[")
            ? $"[{host}]"
            : host;

        return $"{formattedHost}:{port}";
    }

    private void NotifyConnection(ProxyConnectionInfo info)
    {
        try
        {
            ConnectionUpdated?.Invoke(info);
        }
        catch (IOException ex)
        {
            Debug.WriteLine(
                $"[Veyron Proxy] I/O error ({info.Id}): {ex.Message}");

            if (info != null)
                info.Status = "Failed";
        }
        catch (SocketException ex)
        {
            Debug.WriteLine(
                $"[Veyron Proxy] Socket error ({info.Id}): {ex.Message}");

            if (info != null)
                info.Status = "Failed";
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[Veyron Proxy] Unexpected error ({info.Id}): {ex}");

            if (info != null)
                info.Status = "Failed";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;

        await StopAsync();

        disposed = true;

        lifecycleLock.Dispose();
    }

    private static string GetSocks5Error(byte code)
    {
        return code switch
        {
            0x01 => "General SOCKS server failure",
            0x02 => "Connection not allowed by ruleset",
            0x03 => "Network unreachable",
            0x04 => "Host unreachable",
            0x05 => "Connection refused",
            0x06 => "TTL expired",
            0x07 => "Command not supported",
            0x08 => "Address type not supported",
            _ => $"Unknown SOCKS5 error: 0x{code:X2}"
        };
    }

    private static async Task ConnectTcpAsync(
    TcpClient client,
    string host,
    int port,
    CancellationToken token)
    {
        using var timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(token);

        timeoutCts.CancelAfter(ConnectTimeout);

        try
        {
            await client.ConnectAsync(
                host,
                port,
                timeoutCts.Token);
        }
        catch (OperationCanceledException)
            when (!token.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Connection to {host}:{port} timed out.");
        }
    }

    private async Task HandleClientTrackedAsync(
    Guid id,
    TcpClient client,
    CancellationToken token)
    {
        try
        {
            await HandleClientAsync(id, client, token);
        }
        finally
        {
            connectionTasks.TryRemove(id, out _);
            clients.TryRemove(id, out _);
        }
    }
}
