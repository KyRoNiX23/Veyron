
using Veyron.Core.Models;

namespace Veyron.Core.Interfaces;

public interface IProxyEndpointProvider
{
    ProxyEndpoint? CurrentEndpoint { get; }

    Task StartAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default);

    Task StopAsync(
        CancellationToken cancellationToken = default);
}
