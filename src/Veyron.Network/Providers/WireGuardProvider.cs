
using System.Diagnostics;
using System.ComponentModel;
using Veyron.Core.Enums;
using Veyron.Core.Interfaces;
using Veyron.Core.Models;

namespace Veyron.Network.Providers;

public sealed class WireGuardProvider : IConnectionProvider
{
    private readonly string wireGuardExe;
    private readonly string wgExe;

    private string? activeTunnel;

    public string Name => "WireGuard";

    public WireGuardProvider(string? installDirectory = null)
    {
        string directory = installDirectory ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles),
            "WireGuard");

        wireGuardExe = Path.Combine(directory, "wireguard.exe");
        wgExe = Path.Combine(directory, "wg.exe");

        if (!File.Exists(wireGuardExe))
            throw new FileNotFoundException(
                "WireGuard executable was not found.",
                wireGuardExe);

        if (!File.Exists(wgExe))
            throw new FileNotFoundException(
                "WireGuard command-line utility was not found.",
                wgExe);
    }

    public async Task ConnectAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Type != ConnectionType.WireGuard)
            throw new ArgumentException(
                "Profile is not WireGuard.");

        if (string.IsNullOrWhiteSpace(profile.SourcePath))
            throw new ArgumentException(
                "Configuration path is empty.");

        string configPath = Path.GetFullPath(profile.SourcePath);

        if (!File.Exists(configPath))
            throw new FileNotFoundException(
                "WireGuard configuration was not found.",
                configPath);

        if (!string.Equals(
            Path.GetExtension(configPath),
            ".conf",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Only .conf WireGuard configurations are supported.");
        }

        string tunnelName =
            Path.GetFileNameWithoutExtension(configPath);

        if (string.IsNullOrWhiteSpace(tunnelName) ||
            tunnelName.Any(c =>
                !char.IsAsciiLetterOrDigit(c) &&
                c != '_' && c != '-'))
        {
            throw new InvalidDataException(
                "Invalid tunnel name.");
        }

        var result = await RunAsync(
            wireGuardExe,
            ["/installtunnelservice", configPath],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"WireGuard installation failed:\n{result.Output}");
        }

        activeTunnel = tunnelName;
    }

    public async Task DisconnectAsync(
        CancellationToken cancellationToken)
    {
        if (activeTunnel == null)
            return;

        string tunnel = activeTunnel;

        var result = await RunAsync(
            wireGuardExe,
            ["/uninstalltunnelservice", tunnel],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"WireGuard disconnect failed:\n{result.Output}");
        }

        activeTunnel = null;
    }

    public async Task<bool> IsConnectedAsync(
        CancellationToken cancellationToken)
    {
        if (activeTunnel == null)
            return false;

        var result = await RunAsync(
            wgExe,
            ["show", activeTunnel],
            cancellationToken);

        return result.ExitCode == 0 &&
               !string.IsNullOrWhiteSpace(result.Output);
    }

    private static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    "Could not start WireGuard process.");
            }

            Task<string> stdout =
                process.StandardOutput.ReadToEndAsync();

            Task<string> stderr =
                process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken);

            string output = await stdout;
            string error = await stderr;

            string combinedOutput =
                string.Join(
                    Environment.NewLine,
                    new[] { output, error }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));

            return new ProcessResult(
                process.ExitCode,
                combinedOutput);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            throw;
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                "Unable to start WireGuard. Check installation and permissions.",
                ex);
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string Output);
}
