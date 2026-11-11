using System;
using System.Linq;
using Veyron.Core.Enums;
using Veyron.Core.Models;

namespace Veyron.Network.Routing;

public sealed class TrafficRouter
{
    public RoutingAction Resolve(
        string host,
        RoutingProfile profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        if (string.IsNullOrWhiteSpace(host))
            return RoutingAction.Block;

        switch (profile.Mode)
        {
            case RoutingMode.GlobalProxy:
                return RoutingAction.Proxy;

            case RoutingMode.GlobalDirect:
                return RoutingAction.Direct;

            case RoutingMode.RuleBased:
                return ResolveByRules(host, profile);

            default:
                return RoutingAction.Block;
        }
    }

    private static RoutingAction ResolveByRules(
        string host,
        RoutingProfile profile)
    {
        if (profile.Rules == null)
            return profile.DefaultAction;

        RoutingRule? matchedRule = profile.Rules.FirstOrDefault(
            rule => DomainMatcher.Matches(host, rule));

        return matchedRule?.Action ?? profile.DefaultAction;
    }
}