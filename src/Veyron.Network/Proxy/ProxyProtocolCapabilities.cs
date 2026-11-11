using Veyron.Core.Enums;

namespace Veyron.Network.Proxy;

public static class ProxyProtocolCapabilities
{
    public static bool IsSupported(ConnectionType type)
    {
        return type switch
        {
            ConnectionType.HTTP => true,
            ConnectionType.SOCKS4 => true,
            ConnectionType.SOCKS5 => true,
            _ => false
        };
    }

    public static string GetStatus(ConnectionType type)
    {
        return type switch
        {
            ConnectionType.HTTP => "Available",
            ConnectionType.SOCKS4 => "Not implemented",
            ConnectionType.SOCKS5 => "Available",
            _ => "Unsupported"
        };
    }
}