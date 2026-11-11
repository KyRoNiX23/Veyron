using System;
using Veyron.Core.Models;

namespace Veyron.Network.Routing;

public static class DomainMatcher
{
    public static bool Matches(
        string host,
        RoutingRule rule)
    {
        if (!rule.Enabled ||
            string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(rule.Pattern))
            return false;

        string domain = host.Trim().TrimEnd('.').ToLowerInvariant();
        string pattern = rule.Pattern.Trim().TrimEnd('.').ToLowerInvariant();

        if (pattern.StartsWith("*."))
        {
            string suffix = pattern[2..];

            return domain.Length > suffix.Length &&
                   domain.EndsWith("." + suffix, StringComparison.Ordinal);
        }

        return string.Equals(
            domain,
            pattern,
            StringComparison.Ordinal);
    }
}