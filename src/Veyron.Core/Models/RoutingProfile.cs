using System;
using System.Collections.Generic;
using Veyron.Core.Enums;

namespace Veyron.Core.Models;

public sealed class RoutingProfile
{
    public string Id { get; set; } =
        Guid.NewGuid().ToString();

    public string Name { get; set; } = "Default";

    public RoutingMode Mode { get; set; } =
        RoutingMode.RuleBased;

    public RoutingAction DefaultAction { get; set; } =
        RoutingAction.Direct;

    public List<RoutingRule> Rules { get; set; } = new();
}