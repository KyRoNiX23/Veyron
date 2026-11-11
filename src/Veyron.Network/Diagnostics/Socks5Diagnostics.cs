
using System.Net.Sockets;

namespace Veyron.Network.Diagnostics;

public sealed class Socks5Diagnostics
{
    public async Task<bool> TestAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        if (port < 1 || port > 65535)
            return false;

        try
        {
            using var client = new TcpClient();

            await client.ConnectAsync(host, port, cancellationToken);

            using NetworkStream stream = client.GetStream();

            // SOCKS5 greeting: version 5, one method (no authentication).
            byte[] greeting = { 0x05, 0x01, 0x00 };

            await stream.WriteAsync(
                greeting.AsMemory(), cancellationToken);

            byte[] response = new byte[2];

            int received = await stream.ReadAsync(
                response.AsMemory(), cancellationToken);

            // Version must be 5; 0xFF means no acceptable method.
            return received == 2 &&
                   response[0] == 0x05 &&
                   response[1] != 0xFF;
        }
        catch (Exception ex) when (
            ex is SocketException ||
            ex is IOException ||
            ex is OperationCanceledException)
        {
            return false;
        }
    }
}
