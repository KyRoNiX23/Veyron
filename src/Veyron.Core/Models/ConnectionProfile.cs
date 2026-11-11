
using Veyron.Core.Enums;

namespace Veyron.Core.Models;

public class ConnectionProfile
{
    public string Id { get; set; } =
        Guid.NewGuid().ToString();

    public string Name { get; set; } = "";

    public string Country { get; set; } = "";

    public string Host { get; set; } = "";

    public int Port { get; set; }

    public ConnectionType Type { get; set; }

    public string SourcePath { get; set; } = "";

    public int Ping { get; set; } = -1;

    public bool IsAvailable { get; set; }

    public DateTime ImportedAt { get; set; } =
        DateTime.UtcNow;

    public bool IsFavorite { get; set; }
}