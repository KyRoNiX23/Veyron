
using Veyron.Core.Models;

namespace Veyron.Core.Interfaces;

public interface IConnectionProvider
{
    string Name { get; }

    Task ConnectAsync(ConnectionProfile profile,
        CancellationToken cancellationToken);

    Task DisconnectAsync(
        CancellationToken cancellationToken);

    Task<bool> IsConnectedAsync(
        CancellationToken cancellationToken);
}