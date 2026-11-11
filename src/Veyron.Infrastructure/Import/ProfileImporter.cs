
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Veyron.Core.Enums;
using Veyron.Core.Models;

namespace Veyron.Infrastructure.Import;

public sealed class ProxyImportResult
{
    public List<ConnectionProfile> Profiles { get; } = new();
    public List<string> Errors { get; } = new();
}

public class ProfileImporter
{
    public async Task<ConnectionProfile> ImportAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is empty.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException(
                "Configuration file not found.", filePath);

        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        string content = await File.ReadAllTextAsync(filePath);

        var profile = new ConnectionProfile
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            SourcePath = Path.GetFullPath(filePath)
        };

        switch (extension)
        {
            case ".conf":
                ParseWireGuard(content, profile);
                break;

            case ".ovpn":
                ParseOpenVpn(content, profile);
                break;

            case ".txt":
                ParseProxy(content, profile);
                break;

            default:
                throw new NotSupportedException(
                    $"Unsupported configuration format: {extension}");
        }

        ValidateProfile(profile);
        return profile;
    }

    public async Task<ProxyImportResult> ImportProxyListAsync(
        string filePath,
        ConnectionType defaultType = ConnectionType.SOCKS5)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException(
                "Proxy list not found.", filePath);

        if (defaultType != ConnectionType.SOCKS5 &&
            defaultType != ConnectionType.HTTP &&
            defaultType != ConnectionType.HTTPS)
            throw new ArgumentException("Invalid default proxy type.");

        string[] lines = await File.ReadAllLinesAsync(filePath);
        var result = new ProxyImportResult();

        string sourcePath = Path.GetFullPath(filePath);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("#") ||
                line.StartsWith("//"))
                continue;

            // Заголовок CSV.
            if (i == 0 &&
                line.Contains("host", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("port", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var profile = ParseProxyListEntry(
                    line,
                    defaultType,
                    sourcePath);

                result.Profiles.Add(profile);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Line {i + 1}: {ex.Message}");
            }
        }

        if (result.Profiles.Count == 0)
            throw new InvalidDataException(
                "No valid proxy addresses were found.\n" +
                string.Join("\n", result.Errors.Take(10)));

        return result;
    }

    private ConnectionProfile ParseProxyListEntry(
        string line,
        ConnectionType defaultType,
        string sourcePath)
    {
        ConnectionType type = defaultType;
        string host;
        string portText;

        if (line.Contains("://"))
        {
            if (!Uri.TryCreate(line, UriKind.Absolute, out Uri? uri))
                throw new InvalidDataException("Invalid proxy URI.");

            type = ParseProxyType(uri.Scheme);

            if (!string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidDataException(
                    "Proxy credentials are not supported yet.");

            host = uri.Host;
            portText = uri.IsDefaultPort
                ? GetDefaultPort(uri.Scheme).ToString()
                : uri.Port.ToString();
        }
        else
        {
            char delimiter = line.Contains(';') ? ';' : ',';
            string[] parts = line.Split(delimiter)
                .Select(x => x.Trim())
                .ToArray();

            if (parts.Length == 3)
            {
                // Формат: type,host,port
                if (TryParseProxyType(parts[0], out var firstType))
                {
                    type = firstType;
                    host = parts[1];
                    portText = parts[2];
                }
                // Формат: host,port,type
                else if (TryParseProxyType(parts[2], out var lastType))
                {
                    host = parts[0];
                    portText = parts[1];
                    type = lastType;
                }
                else
                {
                    throw new InvalidDataException(
                        "CSV type must be socks5, http or https.");
                }
            }
            else if (parts.Length == 2)
            {
                host = parts[0];
                portText = parts[1];
            }
            else
            {
                // Формат: host:port или [IPv6]:port
                var temporary = new ConnectionProfile();
                ParseHostPort(line, temporary);

                host = temporary.Host;
                portText = temporary.Port.ToString();
            }
        }

        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new InvalidDataException("Invalid proxy host.");

        if (!int.TryParse(portText, out int port) ||
            port < 1 || port > 65535)
            throw new InvalidDataException("Invalid proxy port.");

        return new ConnectionProfile
        {
            Name = $"{type} - {host}:{port}",
            Host = host,
            Port = port,
            Type = type,
            SourcePath = sourcePath,
            Country = "",
            Ping = -1,
            IsAvailable = false
        };
    }

    private void ParseWireGuard(
        string content,
        ConnectionProfile profile)
    {
        profile.Type = ConnectionType.WireGuard;

        foreach (string line in content.Split('\n'))
        {
            string value = line.Trim();

            if (!value.StartsWith(
                "Endpoint",
                StringComparison.OrdinalIgnoreCase))
                continue;

            string[] parts = value.Split(new[] { '=' }, 2);

            if (parts.Length != 2)
                continue;

            ParseHostPort(parts[1].Trim(), profile);
            return;
        }

        throw new InvalidDataException(
            "WireGuard Endpoint was not found.");
    }

    private void ParseOpenVpn(
        string content,
        ConnectionProfile profile)
    {
        profile.Type = ConnectionType.OpenVPN;

        foreach (string line in content.Split('\n'))
        {
            string value = line.Trim();

            if (value.StartsWith("#") || value.StartsWith(";"))
                continue;

            string[] parts = value.Split(
                new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2 ||
                !parts[0].Equals(
                    "remote",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            string port = parts.Length > 2 ? parts[2] : "1194";

            ParseHostPort(parts[1] + ":" + port, profile);
            return;
        }

        throw new InvalidDataException(
            "OpenVPN remote address was not found.");
    }

    private void ParseProxy(
        string content,
        ConnectionProfile profile)
    {
        string value = content.Trim();

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            throw new InvalidDataException("Invalid proxy URI.");

        profile.Type = ParseProxyType(uri.Scheme);

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException(
                "Proxy credentials are not supported yet.");

        profile.Host = uri.Host;
        profile.Port = uri.IsDefaultPort
            ? GetDefaultPort(uri.Scheme)
            : uri.Port;
    }

    private ConnectionType ParseProxyType(string scheme)
    {
        return scheme.ToLowerInvariant() switch
        {
            "socks5" => ConnectionType.SOCKS5,
            "http" => ConnectionType.HTTP,
            "https" => ConnectionType.HTTPS,
            _ => throw new NotSupportedException(
                $"Unsupported proxy protocol: {scheme}")
        };
    }

    private bool TryParseProxyType(
        string value,
        out ConnectionType type)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "socks5":
                type = ConnectionType.SOCKS5;
                return true;

            case "http":
                type = ConnectionType.HTTP;
                return true;

            case "https":
                type = ConnectionType.HTTPS;
                return true;

            default:
                type = default;
                return false;
        }
    }

    private int GetDefaultPort(string scheme)
    {
        return scheme.ToLowerInvariant() switch
        {
            "http" => 80,
            "https" => 443,
            "socks5" => 1080,
            _ => throw new NotSupportedException()
        };
    }

    private void ValidateProfile(ConnectionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Host))
            throw new InvalidDataException(
                "Could not determine server address.");

        if (profile.Port < 1 || profile.Port > 65535)
            throw new InvalidDataException("Invalid server port.");
    }

    private void ParseHostPort(
        string endpoint,
        ConnectionProfile profile)
    {
        string host;
        string portText;

        if (endpoint.StartsWith("["))
        {
            int closingBracket = endpoint.IndexOf(']');

            if (closingBracket < 0 ||
                closingBracket + 1 >= endpoint.Length ||
                endpoint[closingBracket + 1] != ':')
                throw new InvalidDataException(
                    "Invalid IPv6 endpoint.");

            host = endpoint.Substring(1, closingBracket - 1);
            portText = endpoint.Substring(closingBracket + 2);

            if (!IPAddress.TryParse(host, out IPAddress? address) ||
                address.AddressFamily !=
                System.Net.Sockets.AddressFamily.InterNetworkV6)
                throw new InvalidDataException("Invalid IPv6 address.");
        }
        else
        {
            int separator = endpoint.LastIndexOf(':');

            if (separator <= 0)
                throw new InvalidDataException(
                    "Invalid server endpoint.");

            host = endpoint.Substring(0, separator).Trim();
            portText = endpoint.Substring(separator + 1).Trim();

            if (host.Contains(':'))
                throw new InvalidDataException(
                    "IPv6 addresses must use brackets.");
        }

        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new InvalidDataException("Invalid server address.");

        if (!int.TryParse(portText, out int port) ||
            port < 1 || port > 65535)
            throw new InvalidDataException("Invalid server port.");

        profile.Host = host;
        profile.Port = port;
    }
}
