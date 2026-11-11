
using Microsoft.Win32;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Veyron.Core.Enums;
using Veyron.Core.Interfaces;
using Veyron.Core.Models;

namespace Veyron.Network.Providers;

public sealed class HttpProxyProvider : IConnectionProvider
{
    private const string RegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionRefresh = 37;

    private object? previousProxyEnable;
    private object? previousProxyServer;
    private RegistryValueKind previousEnableKind;
    private RegistryValueKind previousServerKind;
    private bool hadProxyEnable;
    private bool hadProxyServer;
    private bool settingsSaved;

    private string? activeProxy;

    public string Name => "HTTP Proxy";

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(
        IntPtr hInternet,
        int dwOption,
        IntPtr lpBuffer,
        int dwBufferLength);

    public async Task ConnectAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Type != ConnectionType.HTTP)
            throw new NotSupportedException(
                "HTTP provider supports HTTP profiles only.");

        if (string.IsNullOrWhiteSpace(profile.Host))
            throw new ArgumentException("Proxy host is empty.");

        if (profile.Port is < 1 or > 65535)
            throw new ArgumentException("Invalid proxy port.");

        // Check that the proxy endpoint accepts TCP connections.
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(
                profile.Host,
                profile.Port,
                cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        string proxy = $"{profile.Host}:{profile.Port}";

        using var key = Registry.CurrentUser.OpenSubKey(
            RegistryPath, writable: true)
            ?? throw new InvalidOperationException(
                "Cannot open Windows Internet Settings.");

        SavePreviousSettings(key);

        try
        {
            key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            key.SetValue("ProxyServer", proxy, RegistryValueKind.String);

            RefreshWindowsProxySettings();

            activeProxy = proxy;
        }
        catch
        {
            RestorePreviousSettings(key);
            RefreshWindowsProxySettings();
            throw;
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!settingsSaved)
            return Task.CompletedTask;

        using var key = Registry.CurrentUser.OpenSubKey(
            RegistryPath, writable: true)
            ?? throw new InvalidOperationException(
                "Cannot open Windows Internet Settings.");

        RestorePreviousSettings(key);
        RefreshWindowsProxySettings();

        activeProxy = null;
        settingsSaved = false;

        return Task.CompletedTask;
    }

    public Task<bool> IsConnectedAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(activeProxy))
            return Task.FromResult(false);

        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);

        if (key == null)
            return Task.FromResult(false);

        bool enabled = Convert.ToInt32(
            key.GetValue("ProxyEnable", 0)) == 1;

        string? server = key.GetValue("ProxyServer") as string;

        return Task.FromResult(
            enabled &&
            string.Equals(
                server,
                activeProxy,
                StringComparison.OrdinalIgnoreCase));
    }

    private void SavePreviousSettings(RegistryKey key)
    {
        if (settingsSaved)
            return;

        hadProxyEnable = key.GetValueNames()
            .Contains("ProxyEnable", StringComparer.OrdinalIgnoreCase);

        hadProxyServer = key.GetValueNames()
            .Contains("ProxyServer", StringComparer.OrdinalIgnoreCase);

        if (hadProxyEnable)
        {
            previousProxyEnable = key.GetValue("ProxyEnable");
            previousEnableKind = key.GetValueKind("ProxyEnable");
        }

        if (hadProxyServer)
        {
            previousProxyServer = key.GetValue("ProxyServer");
            previousServerKind = key.GetValueKind("ProxyServer");
        }

        settingsSaved = true;
    }

    private void RestorePreviousSettings(RegistryKey key)
    {
        if (hadProxyEnable)
        {
            key.SetValue(
                "ProxyEnable",
                previousProxyEnable!,
                previousEnableKind);
        }
        else
        {
            key.DeleteValue("ProxyEnable", false);
        }

        if (hadProxyServer)
        {
            key.SetValue(
                "ProxyServer",
                previousProxyServer!,
                previousServerKind);
        }
        else
        {
            key.DeleteValue("ProxyServer", false);
        }
    }

    private static void RefreshWindowsProxySettings()
    {
        InternetSetOption(
            IntPtr.Zero,
            InternetOptionSettingsChanged,
            IntPtr.Zero,
            0);

        InternetSetOption(
            IntPtr.Zero,
            InternetOptionRefresh,
            IntPtr.Zero,
            0);
    }
}
