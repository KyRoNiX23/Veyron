
using Veyron.Core.Enums;
using Veyron.Core.Models;

namespace Veyron.Core.Interfaces;

public interface IConnectionManager
{
    ConnectionStatus Status { get; }

    ConnectionProfile? CurrentProfile { get; }

    event Action<ConnectionStatus>? StatusChanged;

    Task ConnectAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(
        CancellationToken cancellationToken = default);


}