using System;
using Veyron.Core.Enums;

namespace Veyron.Core.Models;

public sealed class RoutingRule
{
    public string Id { get; set; } =
        Guid.NewGuid().ToString();

    public string Pattern { get; set; } = "";

    public RoutingAction Action { get; set; } =
        RoutingAction.Proxy;

    public bool Enabled { get; set; } = true;

    public string Description { get; set; } = "";
}