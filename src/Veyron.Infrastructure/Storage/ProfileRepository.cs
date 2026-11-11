
using Veyron.Core.Models;

namespace Veyron.Infrastructure.Storage;

public class ProfileRepository
{
    private readonly JsonStorage<ConnectionProfile> storage =
        new("profiles.json");

    public Task<List<ConnectionProfile>> GetAllAsync()
        => storage.LoadAsync();

    public async Task AddAsync(ConnectionProfile profile)
    {
        var items = await storage.LoadAsync();

        items.Add(profile);

        await storage.SaveAsync(items);
    }

    public async Task RemoveAsync(string id)
    {
        var items = await storage.LoadAsync();

        items.RemoveAll(x => x.Id == id);

        await storage.SaveAsync(items);
    }

    public async Task UpdateAsync(ConnectionProfile profile)
    {
        var items = await storage.LoadAsync();

        int index = items.FindIndex(x => x.Id == profile.Id);

        if (index < 0)
            return;

        items[index] = profile;

        await storage.SaveAsync(items);
    }

    public async Task RemoveRangeAsync(IEnumerable<string> ids)
    {
        var idSet = ids.ToHashSet();

        if (idSet.Count == 0)
            return;

        var items = await storage.LoadAsync();

        items.RemoveAll(x => idSet.Contains(x.Id));

        await storage.SaveAsync(items);
    }

    public async Task RemoveAllAsync()
    {
        await storage.SaveAsync(new List<ConnectionProfile>());
    }

    public async Task<int> AddRangeAsync(
    IEnumerable<ConnectionProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var incoming = profiles.ToList();

        if (incoming.Count == 0)
            return 0;

        var items = await storage.LoadAsync();

        var known = new HashSet<string>(
            items.Select(p => $"{p.Type}|{p.Host}|{p.Port}"),
            StringComparer.OrdinalIgnoreCase);

        int added = 0;

        foreach (var profile in incoming)
        {
            string key = $"{profile.Type}|{profile.Host}|{profile.Port}";

            if (!known.Add(key))
                continue;

            items.Add(profile);
            added++;
        }

        if (added > 0)
            await storage.SaveAsync(items);

        return added;
    }
}