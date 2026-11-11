
using Veyron.Core.Enums;

namespace Veyron.Core.Models;

public class ProviderInfo
{
    public string Id { get; set; } =
        Guid.NewGuid().ToString();

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public ConnectionType Type { get; set; }

    public string Endpoint { get; set; } = "";

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } =
        DateTime.UtcNow;
}