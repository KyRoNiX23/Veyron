
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Veyron.Core.Models;

namespace Veyron.Infrastructure.Storage;

public sealed class ProfileRepository
{
    private readonly JsonStorage<ConnectionProfile> storage =
        new("profiles.json");

    private readonly SemaphoreSlim semaphore = new(1, 1);

    public async Task<List<ConnectionProfile>> GetAllAsync()
    {
        await semaphore.WaitAsync();

        try
        {
            return await storage.LoadAsync();
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task AddAsync(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await semaphore.WaitAsync();

        try
        {
            var items = await storage.LoadAsync();
            items.Add(profile);

            await storage.SaveAsync(items);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task RemoveAsync(string id)
    {
        await semaphore.WaitAsync();

        try
        {
            var items = await storage.LoadAsync();
            items.RemoveAll(x => x.Id == id);

            await storage.SaveAsync(items);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task UpdateAsync(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await semaphore.WaitAsync();

        try
        {
            var items = await storage.LoadAsync();

            int index = items.FindIndex(x => x.Id == profile.Id);

            if (index < 0)
                return;

            items[index] = profile;

            await storage.SaveAsync(items);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task RemoveRangeAsync(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var idSet = ids.ToHashSet();

        if (idSet.Count == 0)
            return;

        await semaphore.WaitAsync();

        try
        {
            var items = await storage.LoadAsync();

            items.RemoveAll(x => idSet.Contains(x.Id));

            await storage.SaveAsync(items);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task RemoveAllAsync()
    {
        await semaphore.WaitAsync();

        try
        {
            await storage.SaveAsync(new List<ConnectionProfile>());
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<int> AddRangeAsync(
        IEnumerable<ConnectionProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var incoming = profiles.ToList();

        if (incoming.Count == 0)
            return 0;

        await semaphore.WaitAsync();

        try
        {
            var items = await storage.LoadAsync();

            var known = new HashSet<string>(
                items.Select(CreateKey),
                StringComparer.OrdinalIgnoreCase);

            int added = 0;

            foreach (var profile in incoming)
            {
                if (profile == null)
                    continue;

                if (!known.Add(CreateKey(profile)))
                    continue;

                items.Add(profile);
                added++;
            }

            if (added > 0)
                await storage.SaveAsync(items);

            return added;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static string CreateKey(ConnectionProfile profile)
    {
        return $"{profile.Type}|{profile.Host.Trim()}|{profile.Port}";
    }
}
