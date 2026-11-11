
using Veyron.Core.Interfaces;
using Veyron.Core.Models;
using Veyron.Network.Configuration;

namespace Veyron.Network.Proxy;

public sealed class LocalProxyEndpointProvider : IProxyEndpointProvider
{
    private readonly LocalProxyEndpoint endpoint;

    private ProxyEndpoint? currentEndpoint;

    public ProxyEndpoint? CurrentEndpoint => currentEndpoint;

    public LocalProxyEndpointProvider(
        ProxyEndpointOptions? options = null)
    {
        endpoint = new LocalProxyEndpoint(options);
    }

    public async Task StartAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (profile.Type != Veyron.Core.Enums.ConnectionType.SOCKS5)
            throw new NotSupportedException("Only SOCKS5 profiles are supported.");

        if (endpoint.IsRunning)
            throw new InvalidOperationException(
                "Local proxy endpoint is already running.");

        // Пока это только локальный listener.
        // Он не пересылает трафик на удалённый SOCKS5-сервер.
        await endpoint.StartAsync(cancellationToken);

        currentEndpoint = new ProxyEndpoint
        {
            Host = endpoint.Options.Host,
            Port = endpoint.Options.Port,
            Protocol = "SOCKS5",
            IsRunning = true,
            StartedAt = DateTime.UtcNow
        };
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await endpoint.StopAsync();

        currentEndpoint = null;
    }
}
