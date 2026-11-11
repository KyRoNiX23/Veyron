
namespace Veyron.Core.Models;

public sealed class ProxyEndpoint
{
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 1080;

    public bool IsRunning { get; set; }

    public string Protocol { get; set; } = "SOCKS5";

    public DateTime StartedAt { get; set; }
}
