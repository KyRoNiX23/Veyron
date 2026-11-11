
using System.Text.Json;

namespace Veyron.Infrastructure.Storage;

public class JsonStorage<T>
{
    private readonly string filePath;

    private readonly JsonSerializerOptions options =
        new()
        {
            WriteIndented = true
        };

    public JsonStorage(string fileName)
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "Veyron");

        Directory.CreateDirectory(directory);

        filePath = Path.Combine(directory, fileName);
    }

    public async Task<List<T>> LoadAsync()
    {
        if (!File.Exists(filePath))
            return new List<T>();

        try
        {
            string json = await File.ReadAllTextAsync(filePath);

            return JsonSerializer.Deserialize<List<T>>(
                json, options) ?? new List<T>();
        }
        catch (JsonException)
        {
            throw new InvalidDataException(
                $"Invalid JSON storage: {filePath}");
        }
    }

    public async Task SaveAsync(List<T> items)
    {
        string json = JsonSerializer.Serialize(items, options);

        string temporaryPath = filePath + ".tmp";

        await File.WriteAllTextAsync(temporaryPath, json);

        File.Move(temporaryPath, filePath, true);
    }
}