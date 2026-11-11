
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Veyron.Infrastructure.Storage;

public sealed class JsonStorage<T>
{
    private readonly string filePath;

    private readonly JsonSerializerOptions options = new()
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
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return await JsonSerializer.DeserializeAsync<List<T>>(
                stream,
                options) ?? new List<T>();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Invalid JSON storage: {filePath}\n" +
                $"Line: {ex.LineNumber}\n" +
                $"Byte position: {ex.BytePositionInLine}\n" +
                $"Details: {ex.Message}",
                ex);
        }
    }

    public async Task SaveAsync(List<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        string temporaryPath =
            filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    items,
                    options);

                await stream.FlushAsync();
            }

            if (File.Exists(filePath))
            {
                File.Replace(
                    temporaryPath,
                    filePath,
                    null);
            }
            else
            {
                File.Move(
                    temporaryPath,
                    filePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }


    public string GetFileDiagnostic()
    {
        if (!File.Exists(filePath))
            return "File not found";

        byte[] bytes = File.ReadAllBytes(filePath);

        string hex = Convert.ToHexString(
            bytes.AsSpan(0, Math.Min(bytes.Length, 32)));

        return $"File: {filePath}\n" +
               $"Size: {bytes.Length} bytes\n" +
               $"First bytes: {hex}";
    }


    public string AnalyzeFile()
    {
        if (!File.Exists(filePath))
            return "File not found";

        byte[] bytes = File.ReadAllBytes(filePath);

        int zeroCount = 0;
        int nonZeroCount = 0;
        int firstNonZero = -1;

        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 0)
                zeroCount++;
            else
            {
                nonZeroCount++;

                if (firstNonZero < 0)
                    firstNonZero = i;
            }
        }

        string sample = firstNonZero >= 0
            ? Convert.ToHexString(
                bytes.AsSpan(
                    firstNonZero,
                    Math.Min(64, bytes.Length - firstNonZero)))
            : "No non-zero bytes";

        return
            $"Size: {bytes.Length}\n" +
            $"Zero bytes: {zeroCount}\n" +
            $"Non-zero bytes: {nonZeroCount}\n" +
            $"First non-zero offset: {firstNonZero}\n" +
            $"First non-zero bytes: {sample}";
    }


}
