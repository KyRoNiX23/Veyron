
using Veyron.Core.Enums;
using Veyron.Core.Interfaces;
using Veyron.Core.Models;
using Veyron.Network.Providers;

namespace Veyron.Network;

public sealed class ConnectionManager : IConnectionManager
{
    private readonly SemaphoreSlim semaphore = new(1, 1);
    private readonly ProviderFactory providerFactory;

    private IConnectionProvider? activeProvider;

    public ConnectionStatus Status { get; private set; }
        = ConnectionStatus.Disconnected;

    public ConnectionProfile? CurrentProfile { get; private set; }

    public event Action<ConnectionStatus>? StatusChanged;

    public ConnectionManager(ProviderFactory providerFactory)
    {
        this.providerFactory = providerFactory
            ?? throw new ArgumentNullException(nameof(providerFactory));
    }

    public async Task ConnectAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await semaphore.WaitAsync(cancellationToken);

        try
        {
            if (activeProvider != null)
                throw new InvalidOperationException(
                    "Disconnect the current connection first.");

            SetStatus(ConnectionStatus.Connecting);

            IConnectionProvider? provider = null;

            try
            {
                provider = providerFactory.Create(profile.Type);

                await provider.ConnectAsync(profile, cancellationToken);

                bool connected = await provider.IsConnectedAsync(
                    cancellationToken);

                if (!connected)
                    throw new InvalidOperationException(
                        "WireGuard tunnel could not be verified.");

                activeProvider = provider;
                CurrentProfile = profile;

                SetStatus(ConnectionStatus.Connected);
            }
            catch
            {
                if (provider != null)
                {
                    try
                    {
                        await provider.DisconnectAsync(
                            CancellationToken.None);
                    }
                    catch
                    {
                        // Preserve the original error.
                    }
                }

                SetStatus(ConnectionStatus.Failed);
                throw;
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task DisconnectAsync(
        CancellationToken cancellationToken = default)
    {
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            if (activeProvider == null)
            {
                CurrentProfile = null;
                SetStatus(ConnectionStatus.Disconnected);
                return;
            }

            SetStatus(ConnectionStatus.Disconnecting);

            await activeProvider.DisconnectAsync(cancellationToken);

            activeProvider = null;
            CurrentProfile = null;

            SetStatus(ConnectionStatus.Disconnected);
        }
        catch
        {
            SetStatus(ConnectionStatus.Failed);
            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private void SetStatus(ConnectionStatus status)
    {
        Status = status;

        try
        {
            StatusChanged?.Invoke(status);
        }
        catch
        {
            // UI event errors must not interrupt connection management.
        }
    }
}
