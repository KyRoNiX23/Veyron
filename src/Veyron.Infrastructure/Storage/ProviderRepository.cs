
using Veyron.Core.Models;

namespace Veyron.Infrastructure.Storage;

public class ProviderRepository
{
    private readonly JsonStorage<ProviderInfo> storage =
        new("providers.json");

    public Task<List<ProviderInfo>> GetAllAsync()
        => storage.LoadAsync();

    public async Task AddAsync(ProviderInfo provider)
    {
        var items = await storage.LoadAsync();

        items.Add(provider);

        await storage.SaveAsync(items);
    }

    public async Task RemoveAsync(string id)
    {
        var items = await storage.LoadAsync();

        items.RemoveAll(x => x.Id == id);

        await storage.SaveAsync(items);
    }

    public async Task UpdateAsync(ProviderInfo provider)
    {
        var items = await storage.LoadAsync();

        int index = items.FindIndex(
            x => x.Id == provider.Id);

        if (index < 0)
            return;

        items[index] = provider;

        await storage.SaveAsync(items);
    }
}