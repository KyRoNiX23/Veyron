using System;
using Veyron.Core.Enums;

namespace Veyron.Network.Proxy;

public static class ProxyTunnelFactory
{
    public static string GetProtocolName(ConnectionType type)
    {
        return type switch
        {
            ConnectionType.HTTP => "HTTP CONNECT",
            ConnectionType.SOCKS4 => "SOCKS4",
            ConnectionType.SOCKS5 => "SOCKS5",
            _ => throw new NotSupportedException(
                $"Unsupported proxy protocol: {type}")
        };
    }

    public static bool IsSupported(ConnectionType type)
    {
        return type is
            ConnectionType.HTTP or
            ConnectionType.SOCKS4 or
            ConnectionType.SOCKS5;
    }
}