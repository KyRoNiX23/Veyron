
using Veyron.Core.Models;
using Veyron.Network.Diagnostics;

namespace Veyron.Network.Selection;

public sealed class ServerSelectionService
{
    private readonly NetworkDiagnostics diagnostics = new();
    private const int MaxConcurrentChecks = 4;

    public async Task<List<ConnectionProfile>> MeasureAsync(
        IEnumerable<ConnectionProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        var result = profiles.ToList();

        using var semaphore = new SemaphoreSlim(MaxConcurrentChecks);

        var tasks = result.Select(async profile =>
        {
            await semaphore.WaitAsync(cancellationToken);

            try
            {
                if (string.IsNullOrWhiteSpace(profile.Host))
                {
                    profile.Ping = -1;
                    return;
                }

                long? ping = await diagnostics.PingAsync(
                    profile.Host, cancellationToken);

                profile.Ping = ping.HasValue
                    ? (int)ping.Value
                    : -1;
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        return result
            .OrderBy(p => p.Ping < 0 ? int.MaxValue : p.Ping)
            .ToList();
    }

    public ConnectionProfile? FindBest(
        IEnumerable<ConnectionProfile> profiles)
    {
        return profiles
            .Where(p =>
                p.Type == Veyron.Core.Enums.ConnectionType.WireGuard &&
                p.Ping >= 0 &&
                File.Exists(p.SourcePath))
            .OrderBy(p => p.Ping)
            .FirstOrDefault();
    }
}