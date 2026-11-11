
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Veyron.Core.Enums;
using Veyron.Core.Models;

namespace Veyron.Network.Diagnostics;

public sealed class SocksProxyDiagnostics
{
    public async Task<SocksProxyTestResult> TestAsync(
        ConnectionProfile profile,
        string targetHost = "example.com",
        int targetPort = 443,
        CancellationToken cancellationToken = default)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        if (profile.Type != ConnectionType.SOCKS4 &&
            profile.Type != ConnectionType.SOCKS5)
        {
            return SocksProxyTestResult.Failed(
                "Profile must use SOCKS4 or SOCKS5.",
                "Validation");
        }

        if (string.IsNullOrWhiteSpace(profile.Host))
        {
            return SocksProxyTestResult.Failed(
                "Proxy host is empty.",
                "Validation");
        }

        if (profile.Port < 1 || profile.Port > 65535)
        {
            return SocksProxyTestResult.Failed(
                "Invalid proxy port.",
                "Validation");
        }

        if (string.IsNullOrWhiteSpace(targetHost) ||
            targetPort < 1 || targetPort > 65535)
        {
            return SocksProxyTestResult.Failed(
                "Invalid target address.",
                "Validation");
        }

        using var timeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var tcp = new TcpClient();

            // 1. TCP connection
            await tcp.ConnectAsync(
                profile.Host,
                profile.Port,
                timeout.Token);

            using NetworkStream stream = tcp.GetStream();

            // 2. SOCKS handshake and target connection
            string handshakeResult;

            if (profile.Type == ConnectionType.SOCKS5)
            {
                handshakeResult = await ConnectSocks5Async(
                    stream,
                    targetHost,
                    targetPort,
                    timeout.Token);
            }
            else
            {
                handshakeResult = await ConnectSocks4Async(
                    stream,
                    targetHost,
                    targetPort,
                    timeout.Token);
            }

            if (handshakeResult != "OK")
            {
                stopwatch.Stop();

                return new SocksProxyTestResult
                {
                    Success = false,
                    ResponseTime = stopwatch.ElapsedMilliseconds,
                    Stage = "SOCKS handshake",
                    Error = handshakeResult
                };
            }

            // 3. TLS handshake
            using var ssl = new SslStream(
                stream,
                leaveInnerStreamOpen: true);

            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = targetHost,
                    EnabledSslProtocols =
                        SslProtocols.Tls12 | SslProtocols.Tls13
                },
                timeout.Token);

            // 4. HTTPS request
            string request =
                $"GET / HTTP/1.1\r\n" +
                $"Host: {targetHost}\r\n" +
                "User-Agent: Veyron-Diagnostics/1.0\r\n" +
                "Connection: close\r\n\r\n";

            byte[] requestBytes = Encoding.ASCII.GetBytes(request);

            await ssl.WriteAsync(
                requestBytes,
                timeout.Token);

            using var reader = new StreamReader(
                ssl,
                Encoding.ASCII,
                false,
                1024,
                true);

            string? responseLine = await reader.ReadLineAsync(
                timeout.Token);

            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return SocksProxyTestResult.Failed(
                    "TLS established, but the website returned no HTTP response.",
                    "HTTPS");
            }

            string[] parts = responseLine.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2 ||
                !int.TryParse(parts[1], out int httpStatus))
            {
                return SocksProxyTestResult.Failed(
                    $"Invalid HTTP response: {responseLine}",
                    "HTTPS");
            }

            stopwatch.Stop();

            return new SocksProxyTestResult
            {
                Success = true,
                ResponseTime = stopwatch.ElapsedMilliseconds,
                Stage = "Completed",
                HttpStatusCode = httpStatus,
                HttpStatusDescription = responseLine
            };
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return SocksProxyTestResult.Failed(
                "Connection timed out after 15 seconds.",
                "Timeout");
        }
        catch (AuthenticationException ex)
        {
            return SocksProxyTestResult.Failed(
                $"TLS handshake failed: {ex.Message}",
                "TLS");
        }
        catch (SocketException ex)
        {
            return SocksProxyTestResult.Failed(
                $"{ex.SocketErrorCode}: {ex.Message}",
                "TCP");
        }
        catch (IOException ex)
        {
            return SocksProxyTestResult.Failed(
                ex.Message,
                "Network");
        }
        catch (Exception ex)
        {
            return SocksProxyTestResult.Failed(
                ex.Message,
                "Unknown");
        }
    }

    private static async Task<string> ConnectSocks5Async(
        NetworkStream stream,
        string targetHost,
        int targetPort,
        CancellationToken token)
    {
        // SOCKS5 greeting: version 5, one method, no authentication.
        byte[] greeting = { 0x05, 0x01, 0x00 };

        await stream.WriteAsync(greeting, token);

        byte[] greetingResponse = new byte[2];

        await ReadExactAsync(
            stream,
            greetingResponse,
            token);

        if (greetingResponse[0] != 0x05)
            return "Invalid SOCKS5 version.";

        if (greetingResponse[1] == 0xFF)
            return "Proxy does not accept unauthenticated connections.";

        if (greetingResponse[1] != 0x00)
            return $"Unsupported authentication method: {greetingResponse[1]}.";

        // SOCKS5 CONNECT using a domain name.
        byte[] hostBytes = Encoding.ASCII.GetBytes(targetHost);

        if (hostBytes.Length == 0 || hostBytes.Length > 255)
            return "Invalid target hostname.";

        byte[] request = new byte[7 + hostBytes.Length];

        request[0] = 0x05; // Version
        request[1] = 0x01; // CONNECT
        request[2] = 0x00; // Reserved
        request[3] = 0x03; // Domain name
        request[4] = (byte)hostBytes.Length;

        Buffer.BlockCopy(
            hostBytes, 0,
            request, 5,
            hostBytes.Length);

        request[5 + hostBytes.Length] =
            (byte)(targetPort >> 8);

        request[6 + hostBytes.Length] =
            (byte)(targetPort & 0xFF);

        await stream.WriteAsync(request, token);

        byte[] responseHeader = new byte[4];

        await ReadExactAsync(
            stream,
            responseHeader,
            token);

        if (responseHeader[0] != 0x05)
            return "Invalid SOCKS5 CONNECT response.";

        if (responseHeader[1] != 0x00)
        {
            string reason = responseHeader[1] switch
            {
                0x01 => "General SOCKS server failure.",
                0x02 => "Connection not allowed.",
                0x03 => "Network unreachable.",
                0x04 => "Host unreachable.",
                0x05 => "Connection refused.",
                0x06 => "TTL expired.",
                0x07 => "Command not supported.",
                0x08 => "Address type not supported.",
                _ => $"Unknown SOCKS5 error: {responseHeader[1]}."
            };

            return reason;
        }

        int addressLength = responseHeader[3] switch
        {
            0x01 => 4,
            0x04 => 16,
            0x03 => await ReadDomainLengthAsync(stream, token),
            _ => -1
        };

        if (addressLength < 0)
            return "Invalid SOCKS5 address type.";

        // Bound address and port.
        byte[] remaining = new byte[addressLength + 2];

        await ReadExactAsync(stream, remaining, token);

        return "OK";
    }

    private static async Task<string> ConnectSocks4Async(
        NetworkStream stream,
        string targetHost,
        int targetPort,
        CancellationToken token)
    {
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(
            targetHost,
            token);

        IPAddress? ipv4 = addresses.FirstOrDefault(
            x => x.AddressFamily == AddressFamily.InterNetwork);

        if (ipv4 == null)
            return "SOCKS4 requires an IPv4 address for the target.";

        byte[] ipBytes = ipv4.GetAddressBytes();

        // SOCKS4 CONNECT request:
        // VN, CD, DSTPORT, DSTIP, USERID, NULL
        byte[] request = new byte[9];

        request[0] = 0x04;
        request[1] = 0x01;
        request[2] = (byte)(targetPort >> 8);
        request[3] = (byte)(targetPort & 0xFF);

        Buffer.BlockCopy(ipBytes, 0, request, 4, 4);

        request[8] = 0x00;

        await stream.WriteAsync(request, token);

        byte[] response = new byte[8];

        await ReadExactAsync(stream, response, token);

        if (response[0] != 0x00)
            return "Invalid SOCKS4 response version.";

        return response[1] switch
        {
            0x5A => "OK",
            0x5B => "SOCKS4: Request rejected or failed.",
            0x5C => "SOCKS4: Ident server unavailable.",
            0x5D => "SOCKS4: User ID mismatch.",
            _ => $"Unknown SOCKS4 response: {response[1]}."
        };
    }

    private static async Task<int> ReadDomainLengthAsync(
        NetworkStream stream,
        CancellationToken token)
    {
        byte[] length = new byte[1];

        await ReadExactAsync(stream, length, token);

        return length[0];
    }

    private static async Task ReadExactAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken token)
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer.AsMemory(offset),
                token);

            if (read == 0)
                throw new IOException("Connection closed unexpectedly.");

            offset += read;
        }
    }
}

public sealed class SocksProxyTestResult
{
    public bool Success { get; set; }

    public long ResponseTime { get; set; }

    public string Stage { get; set; } = "";

    public int? HttpStatusCode { get; set; }

    public string HttpStatusDescription { get; set; } = "";

    public string? Error { get; set; }

    public static SocksProxyTestResult Failed(
        string error,
        string stage)
    {
        return new SocksProxyTestResult
        {
            Success = false,
            ResponseTime = -1,
            Stage = stage,
            Error = error
        };
    }
}
