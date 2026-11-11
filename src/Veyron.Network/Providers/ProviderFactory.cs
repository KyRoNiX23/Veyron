using Veyron.Core.Enums;
using Veyron.Core.Interfaces;

namespace Veyron.Network.Providers;

public sealed class ProviderFactory
{
    private readonly Dictionary<ConnectionType, Func<IConnectionProvider>> providers = new();

    public ProviderFactory()
    {
        Register(ConnectionType.WireGuard, () => new WireGuardProvider());
        Register(ConnectionType.HTTP, () => new HttpProxyProvider());
        Register(ConnectionType.SOCKS5, () => new Socks5ProxyProvider());
    }

    public void Register(
        ConnectionType type,
        Func<IConnectionProvider> factory)
    {
        providers[type] = factory
            ?? throw new ArgumentNullException(nameof(factory));
    }

    public IConnectionProvider Create(ConnectionType type)
    {
        if (!providers.TryGetValue(type, out var factory))
            throw new NotSupportedException(
                $"Provider {type} is not implemented.");

        return factory();
    }
}
/*
 var factory = new ProviderFactory();

// Позже добавим реальные реализации:
factory.Register(
    ConnectionType.WireGuard,
    () => new WireGuardProvider());

factory.Register(
    ConnectionType.OpenVPN,
    () => new OpenVpnProvider());

var manager = new ConnectionManager(factory.Create);
 */