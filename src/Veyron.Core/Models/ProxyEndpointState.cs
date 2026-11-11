
using Veyron.Core.Enums;

namespace Veyron.Core.Models;

public sealed class ProxyEndpointState
{
    public ProxyConnectionStatus Status { get; set; }

    public string LocalAddress { get; set; } = "127.0.0.1";

    public int LocalPort { get; set; } = 1080;

    public string? RemoteHost { get; set; }

    public int RemotePort { get; set; }

    public string? Error { get; set; }

    public DateTime? StartedAt { get; set; }
}
