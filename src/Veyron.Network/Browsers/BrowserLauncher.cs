
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Veyron.Core.Enums;
using Veyron.Core.Models;

namespace Veyron.Network.Browsers;

public enum BrowserType
{
    Chrome,
    Edge
}

public sealed class BrowserLauncher
{
    public Process Start(
        BrowserType browser,
        ConnectionProfile profile,
        string? url = null,
        string? proxyAddress = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(profile.Host))
            throw new ArgumentException("Profile host is empty.");

        string executable = FindBrowser(browser);

        string userDataDirectory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Veyron",
            "BrowserProfiles",
            browser.ToString(),
            profile.Id);

        Directory.CreateDirectory(userDataDirectory);

        string effectiveProxy = proxyAddress ??
            $"{GetProxyScheme(profile.Type)}://{profile.Host}:{profile.Port}";

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(
            $"--proxy-server={effectiveProxy}");

        startInfo.ArgumentList.Add(
            $"--user-data-dir={userDataDirectory}");

        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--new-window");

        if (!string.IsNullOrWhiteSpace(url))
            startInfo.ArgumentList.Add(url);

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException(
                "Browser process could not be started.");
        }

        return process;
    }

    private static string FindBrowser(BrowserType browser)
    {
        string programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);

        string programFilesX86 = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86);

        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        string[] paths = browser switch
        {
            BrowserType.Chrome =>
            [
                Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe")
            ],

            BrowserType.Edge =>
            [
                Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe")
            ],

            _ => throw new NotSupportedException(
                $"Browser {browser} is not supported.")
        };

        return paths.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                $"{browser} executable was not found.");
    }

    private static string GetProxyScheme(ConnectionType type)
    {
        return type switch
        {
            ConnectionType.HTTP => "http",
            ConnectionType.HTTPS => "https",
            ConnectionType.SOCKS4 => "socks4",
            ConnectionType.SOCKS5 => "socks5",

            _ => throw new NotSupportedException(
                $"Browser does not support {type}.")
        };
    }
}
