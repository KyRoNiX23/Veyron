using System.Net.NetworkInformation;

namespace Veyron.Network.Diagnostics;

public class NetworkDiagnostics
{
    public async Task<long?> PingAsync(
        string host,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host is empty.");

        using var ping = new Ping();

        try
        {
            var reply = await ping.SendPingAsync(
                host, 4000).WaitAsync(cancellationToken);

            if (reply.Status != IPStatus.Success)
                return null;

            return reply.RoundtripTime;
        }
        catch (PingException)
        {
            return null;
        }
    }
}