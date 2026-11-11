
using System.Diagnostics;
using System.Globalization;

namespace Veyron.Network.Diagnostics;

public sealed class WireGuardDiagnostics
{
    private readonly string wgExe;

    public WireGuardDiagnostics(string? installDirectory = null)
    {
        string directory = installDirectory ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles),
            "WireGuard");

        wgExe = Path.Combine(directory, "wg.exe");

        if (!File.Exists(wgExe))
            throw new FileNotFoundException(
                "wg.exe was not found.", wgExe);
    }

    public async Task<bool> IsInterfaceActiveAsync(
        string tunnelName,
        CancellationToken cancellationToken = default)
    {
        string output = await ExecuteAsync(
            tunnelName,
            "show",
            cancellationToken);

        return !string.IsNullOrWhiteSpace(output);
    }

    public async Task<DateTime?> GetLatestHandshakeAsync(
        string tunnelName,
        CancellationToken cancellationToken = default)
    {
        string output = await ExecuteAsync(
            tunnelName,
            "latest-handshakes",
            cancellationToken);

        DateTime? latest = null;

        foreach (string line in output.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split(
                '\t',
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
                continue;

            if (!long.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long timestamp))
                continue;

            if (timestamp <= 0)
                continue;

            DateTime handshake = DateTimeOffset
                .FromUnixTimeSeconds(timestamp)
                .UtcDateTime;

            if (latest == null || handshake > latest)
                latest = handshake;
        }

        return latest;
    }

    private async Task<string> ExecuteAsync(
        string tunnelName,
        string command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tunnelName))
            throw new ArgumentException("Tunnel name is empty.");

        var startInfo = new ProcessStartInfo
        {
            FileName = wgExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("show");
        startInfo.ArgumentList.Add(tunnelName);

        if (command != "show")
            startInfo.ArgumentList.Add(command);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
            throw new InvalidOperationException(
                "Could not start wg.exe.");

        Task<string> stdout =
            process.StandardOutput.ReadToEndAsync();

        Task<string> stderr =
            process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        string output = await stdout;
        string error = await stderr;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? "WireGuard diagnostics failed."
                    : error);
        }

        return output;
    }
}
