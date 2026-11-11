
using Veyron.Core.Enums;
using Veyron.Core.Interfaces;
using Veyron.Core.Models;
using Veyron.Network.Diagnostics;

namespace Veyron.Network.Providers;

public sealed class Socks5Provider : IConnectionProvider
{
    private readonly Socks5Diagnostics diagnostics = new();

    private ConnectionProfile? currentProfile;

    public string Name => "SOCKS5";

    public async Task ConnectAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken)
    {
        if (profile.Type != ConnectionType.SOCKS5)
            throw new NotSupportedException(
                "Socks5Provider supports SOCKS5 only.");

        bool available = await diagnostics.TestAsync(
            profile.Host,
            profile.Port,
            cancellationToken);

        if (!available)
            throw new InvalidOperationException(
                "SOCKS5 server is unavailable or does not support the required authentication method.");

        throw new NotSupportedException(
            "SOCKS5 endpoint diagnostics succeeded, but application routing is not implemented yet.");
    }

    public Task DisconnectAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        currentProfile = null;

        return Task.CompletedTask;
    }

    public Task<bool> IsConnectedAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Diagnostics alone do not mean an active connection.
        return Task.FromResult(false);
    }
}
