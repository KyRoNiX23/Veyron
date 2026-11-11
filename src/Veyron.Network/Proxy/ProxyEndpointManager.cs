
using Veyron.Core.Enums;
using Veyron.Core.Models;
using Veyron.Network.Configuration;

namespace Veyron.Network.Proxy;

public sealed class ProxyEndpointManager
{
    private readonly LocalProxyEndpoint endpoint;

    public ProxyEndpointState State { get; private set; } = new();

    public event Action<ProxyEndpointState>? StateChanged;

    public ProxyEndpointManager(ProxyEndpointOptions? options = null)
    {
        endpoint = new LocalProxyEndpoint(options);

        State.LocalAddress = endpoint.Options.Host;
        State.LocalPort = endpoint.Options.Port;
    }

    public async Task StartAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (profile.Type != ConnectionType.SOCKS5)
            throw new NotSupportedException("Only SOCKS5 is supported.");

        if (endpoint.IsRunning)
            throw new InvalidOperationException(
                "Proxy endpoint is already running.");

        State.Status = ProxyConnectionStatus.Starting;
        State.RemoteHost = profile.Host;
        State.RemotePort = profile.Port;
        State.Error = null;

        Notify();

        try
        {
            await endpoint.StartAsync(cancellationToken);

            State.Status = ProxyConnectionStatus.Listening;
            State.StartedAt = DateTime.UtcNow;

            Notify();
        }
        catch (Exception ex)
        {
            State.Status = ProxyConnectionStatus.Failed;
            State.Error = ex.Message;

            Notify();

            throw;
        }
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        State.Status = ProxyConnectionStatus.Stopping;
        Notify();

        try
        {
            await endpoint.StopAsync();

            State.Status = ProxyConnectionStatus.Stopped;
            State.StartedAt = null;
            State.RemoteHost = null;
            State.RemotePort = 0;
        }
        catch (Exception ex)
        {
            State.Status = ProxyConnectionStatus.Failed;
            State.Error = ex.Message;
            throw;
        }
        finally
        {
            Notify();
        }
    }

    private void Notify()
    {
        StateChanged?.Invoke(State);
    }
}
