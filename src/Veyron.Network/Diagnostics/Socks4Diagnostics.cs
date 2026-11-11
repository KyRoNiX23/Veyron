using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Veyron.Core.Models;

namespace Veyron.Network.Diagnostics;

public sealed class Socks4Diagnostics
{
    public async Task<bool> CheckTcpAsync(
        ConnectionProfile profile,
        CancellationToken token = default)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        if (string.IsNullOrWhiteSpace(profile.Host))
            return false;

        if (profile.Port is < 1 or > 65535)
            return false;

        using var client = new TcpClient();

        using var timeout = CancellationTokenSource
            .CreateLinkedTokenSource(token);

        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            await client.ConnectAsync(
                profile.Host,
                profile.Port,
                timeout.Token);

            return client.Connected;
        }
        catch (OperationCanceledException)
            when (!token.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public static bool IsValidIpv4(string address)
    {
        return IPAddress.TryParse(
                   address,
                   out IPAddress? ip) &&
               ip.AddressFamily == AddressFamily.InterNetwork;
    }
}