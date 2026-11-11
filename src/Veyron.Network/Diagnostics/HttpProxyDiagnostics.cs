
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Veyron.Core.Models;

namespace Veyron.Network.Diagnostics;

public sealed class HttpProxyDiagnostics
{
    public async Task<HttpProxyTestResult> TestAsync(
        ConnectionProfile profile,
        string targetHost = "example.com",
        int targetPort = 443,
        CancellationToken cancellationToken = default)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        if (string.IsNullOrWhiteSpace(profile.Host))
            return HttpProxyTestResult.Failed("Proxy host is empty.");

        if (profile.Port is < 1 or > 65535)
            return HttpProxyTestResult.Failed("Invalid proxy port.");

        using var timeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 1. TCP connection
            using var tcp = new TcpClient();

            await tcp.ConnectAsync(
                profile.Host,
                profile.Port,
                timeout.Token);

            using NetworkStream network = tcp.GetStream();

            // 2. HTTP CONNECT
            string connectRequest =
                $"CONNECT {targetHost}:{targetPort} HTTP/1.1\r\n" +
                $"Host: {targetHost}:{targetPort}\r\n" +
                "Proxy-Connection: Keep-Alive\r\n" +
                "User-Agent: Veyron/1.0\r\n\r\n";

            byte[] connectBytes = Encoding.ASCII.GetBytes(connectRequest);

            await network.WriteAsync(connectBytes, timeout.Token);

            using var proxyReader = new StreamReader(
                network,
                Encoding.ASCII,
                false,
                1024,
                true);

            string? proxyStatus = await proxyReader.ReadLineAsync(
                timeout.Token);

            if (string.IsNullOrWhiteSpace(proxyStatus))
                return HttpProxyTestResult.Failed(
                    "Proxy returned an empty CONNECT response.");

            string[] statusParts = proxyStatus.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            if (statusParts.Length < 2 ||
                !int.TryParse(statusParts[1], out int proxyCode))
            {
                return HttpProxyTestResult.Failed(
                    $"Invalid CONNECT response: {proxyStatus}");
            }

            // Read remaining CONNECT headers.
            string? line;

            while (!string.IsNullOrEmpty(
                line = await proxyReader.ReadLineAsync(timeout.Token)))
            {
            }

            if (proxyCode != 200)
            {
                return new HttpProxyTestResult
                {
                    Success = false,
                    StatusCode = proxyCode,
                    StatusDescription = proxyStatus,
                    Error = $"CONNECT rejected: {proxyStatus}",
                    ResponseTime = -1
                };
            }

            // 3. TLS handshake
            using var ssl = new SslStream(
                network,
                leaveInnerStreamOpen: true);

            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = targetHost,
                    EnabledSslProtocols =
                        SslProtocols.Tls12 | SslProtocols.Tls13
                },
                timeout.Token);

            // 4. HTTPS GET
            string getRequest =
                $"GET / HTTP/1.1\r\n" +
                $"Host: {targetHost}\r\n" +
                "Connection: close\r\n" +
                "User-Agent: Veyron/1.0\r\n\r\n";

            byte[] getBytes = Encoding.ASCII.GetBytes(getRequest);

            await ssl.WriteAsync(getBytes, timeout.Token);

            using var httpsReader = new StreamReader(
                ssl,
                Encoding.ASCII,
                false,
                1024,
                true);

            string? httpsStatus = await httpsReader.ReadLineAsync(
                timeout.Token);

            if (string.IsNullOrWhiteSpace(httpsStatus))
            {
                return HttpProxyTestResult.Failed(
                    "TLS established, but the website returned no HTTP response.");
            }

            string[] httpsParts = httpsStatus.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            if (httpsParts.Length < 2 ||
                !int.TryParse(httpsParts[1], out int httpsCode))
            {
                return HttpProxyTestResult.Failed(
                    $"Invalid HTTPS response: {httpsStatus}");
            }

            stopwatch.Stop();

            return new HttpProxyTestResult
            {
                Success = true,
                ResponseTime = stopwatch.ElapsedMilliseconds,
                StatusCode = httpsCode,
                StatusDescription = httpsStatus,
                ProxyResponse = proxyStatus,
                Error = null
            };
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return HttpProxyTestResult.Failed(
                "Connection timed out after 15 seconds.");
        }
        catch (AuthenticationException ex)
        {
            return HttpProxyTestResult.Failed(
                $"TLS handshake failed: {ex.Message}");
        }
        catch (SocketException ex)
        {
            return HttpProxyTestResult.Failed(
                $"TCP error: {ex.SocketErrorCode} - {ex.Message}");
        }
        catch (IOException ex)
        {
            return HttpProxyTestResult.Failed(
                $"Network stream error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return HttpProxyTestResult.Failed(ex.Message);
        }
    }
}

public sealed class HttpProxyTestResult
{
    public bool Success { get; set; }
    public long ResponseTime { get; set; }
    public int? StatusCode { get; set; }
    public string StatusDescription { get; set; } = "";
    public string ProxyResponse { get; set; } = "";
    public string? Error { get; set; }

    public static HttpProxyTestResult Failed(string error)
    {
        return new HttpProxyTestResult
        {
            Success = false,
            ResponseTime = -1,
            Error = error
        };
    }
}
