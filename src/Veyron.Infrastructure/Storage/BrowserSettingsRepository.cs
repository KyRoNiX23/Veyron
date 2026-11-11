
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Veyron.Core.Models;

namespace Veyron.Infrastructure.Storage;

public sealed class BrowserSettingsRepository
{
    private readonly string filePath =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Veyron",
            "browser-settings.json");

    private readonly SemaphoreSlim semaphore = new(1, 1);

    public async Task<BrowserNetworkSettings> LoadAsync()
    {
        await semaphore.WaitAsync();

        try
        {
            if (!File.Exists(filePath))
                return new BrowserNetworkSettings();

            string json = await File.ReadAllTextAsync(filePath);

            return JsonSerializer.Deserialize<BrowserNetworkSettings>(json)
                   ?? new BrowserNetworkSettings();
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task SaveAsync(BrowserNetworkSettings settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        await semaphore.WaitAsync();

        try
        {
            string? directory = Path.GetDirectoryName(filePath);

            if (directory == null)
                throw new InvalidOperationException("Invalid settings path.");

            Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            string tempPath = filePath + ".tmp";

            await File.WriteAllTextAsync(tempPath, json);

            File.Move(tempPath, filePath, true);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
