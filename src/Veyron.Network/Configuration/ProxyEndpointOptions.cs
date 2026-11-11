
namespace Veyron.Network.Configuration;

public sealed class ProxyEndpointOptions
{
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 1080;

    public bool AllowRemoteConnections { get; set; } = false;

    public int ConnectionTimeoutMs { get; set; } = 5000;
}
