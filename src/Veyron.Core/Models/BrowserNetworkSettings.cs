
namespace Veyron.Core.Models;

public sealed class BrowserNetworkSettings
{
    public bool Enabled { get; set; }

    public bool ProcessTcpStreams { get; set; } = true;

    public bool ProcessTlsConnections { get; set; } = true;

    public bool EnableDnsConfiguration { get; set; }

    public string DnsServer { get; set; } = "";

    public bool UseLocalProxy { get; set; }

    public int LocalProxyPort { get; set; } = 2080;

    public string ActiveProfile { get; set; } = "Default";

    public List<string> IncludedDomains { get; set; } = new();

    public List<string> ExcludedDomains { get; set; } = new();
}
