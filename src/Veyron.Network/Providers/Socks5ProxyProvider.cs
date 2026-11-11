using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Veyron.Core.Enums;
using Veyron.Core.Interfaces;
using Veyron.Core.Models;

namespace Veyron.Network.Providers;

public sealed class Socks5ProxyProvider : IConnectionProvider
{
    private TcpClient? client;

    public string Name => "SOCKS5";

    public async Task ConnectAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken)
    {
        if (profile.Type != ConnectionType.SOCKS5)
            throw new ArgumentException("Invalid profile type.");

        if (string.IsNullOrWhiteSpace(profile.Host))
            throw new ArgumentException("Proxy host is empty.");

        if (profile.Port < 1 || profile.Port > 65535)
            throw new ArgumentException("Invalid proxy port.");

        await DisconnectAsync(CancellationToken.None);

        var newClient = new TcpClient();

        try
        {
            await newClient.ConnectAsync(
                profile.Host,
                profile.Port,
                cancellationToken);

            NetworkStream stream = newClient.GetStream();

            // SOCKS5 greeting: version 5, one method, no authentication.
            byte[] greeting = { 0x05, 0x01, 0x00 };

            await stream.WriteAsync(
                greeting,
                cancellationToken);

            byte[] response = new byte[2];

            await stream.ReadExactlyAsync(
                response,
                cancellationToken);

            if (response[0] != 0x05)
                throw new IOException("Invalid SOCKS5 response.");

            if (response[1] == 0xFF)
                throw new IOException(
                    "SOCKS5 proxy does not accept available authentication methods.");

            if (response[1] != 0x00)
                throw new NotSupportedException(
                    "This provider currently supports SOCKS5 without authentication.");

            client = newClient;
        }
        catch
        {
            newClient.Dispose();
            throw;
        }
    }

    public Task<bool> IsConnectedAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool connected =
            client?.Connected == true;

        return Task.FromResult(connected);
    }

    public Task DisconnectAsync(
        CancellationToken cancellationToken)
    {
        client?.Dispose();
        client = null;

        return Task.CompletedTask;
    }
}