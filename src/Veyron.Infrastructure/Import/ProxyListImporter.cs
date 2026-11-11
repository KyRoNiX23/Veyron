
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Veyron.Core.Enums;
using Veyron.Core.Models;

namespace Veyron.Infrastructure.Import;

public sealed class ProxyListImporter
{
    public async Task<List<ConnectionProfile>> ImportAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is empty.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException("CSV file not found.", filePath);

        string[] lines = await File.ReadAllLinesAsync(filePath);

        if (lines.Length == 0)
            throw new InvalidDataException("CSV file is empty.");

        lines[0] = lines[0].TrimStart('\uFEFF');

        var profiles = new List<ConnectionProfile>();
        string fullPath = Path.GetFullPath(filePath);

        List<string> firstFields = ParseCsvLine(lines[0]);

        bool hasHeaders =
            firstFields.Any(x => x.Trim().Equals("ip", StringComparison.OrdinalIgnoreCase)) &&
            firstFields.Any(x => x.Trim().Equals("port", StringComparison.OrdinalIgnoreCase)) &&
            firstFields.Any(x => x.Trim().Equals("protocol", StringComparison.OrdinalIgnoreCase));

        Dictionary<string, int>? headers = null;

        if (hasHeaders)
        {
            headers = firstFields
                .Select((name, index) => new
                {
                    Name = name.Trim().ToLowerInvariant(),
                    Index = index
                })
                .GroupBy(x => x.Name)
                .ToDictionary(g => g.Key, g => g.First().Index);
        }

        int startIndex = hasHeaders ? 1 : 0;

        for (int i = startIndex; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            try
            {
                List<string> fields = ParseCsvLine(lines[i]);

                ConnectionProfile? profile;

                if (hasHeaders)
                {
                    profile = ParseStandardFormat(fields, headers!);
                }
                else
                {
                    profile = ParseUriFormat(fields);
                }

                if (profile == null)
                    continue;

                profile.SourcePath = fullPath;

                profiles.Add(profile);
            }
            catch
            {
                // Пропускаем некорректные строки.
            }
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        profiles.RemoveAll(p => !unique.Add($"{p.Type}|{p.Host}|{p.Port}"));

        if (profiles.Count == 0)
        {
            throw new InvalidDataException(
                "No valid proxy addresses were found in the CSV file.");
        }

        return profiles;
    }

    private static ConnectionProfile? ParseStandardFormat(
        List<string> fields,
        Dictionary<string, int> headers)
    {
        string host = GetField(fields, headers, "ip");
        string portText = GetField(fields, headers, "port");
        string protocolText = GetField(fields, headers, "protocol");

        string country = GetField(fields, headers, "country");
        string city = GetField(fields, headers, "city");
        string latencyText = GetField(fields, headers, "latency_ms");

        if (string.IsNullOrWhiteSpace(host))
            return null;

        if (!int.TryParse(portText, out int port) ||
            port < 1 || port > 65535)
            return null;

        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            return null;

        if (!TryParseProtocol(protocolText, out ConnectionType type))
            return null;

        int ping = int.TryParse(latencyText, out int latency)
            ? Math.Max(-1, latency)
            : -1;

        string name = $"{country} {city}".Trim();

        if (string.IsNullOrWhiteSpace(name))
            name = $"{host}:{port}";

        return new ConnectionProfile
        {
            Name = $"{name} ({type})",
            Host = host,
            Port = port,
            Type = type,
            Country = country,
            Ping = ping,
            IsAvailable = false
        };
    }

    private static ConnectionProfile? ParseUriFormat(
        List<string> fields)
    {
        if (fields.Count == 0)
            return null;

        string address = fields[0].Trim();

        if (!Uri.TryCreate(
            address,
            UriKind.Absolute,
            out Uri? uri))
            return null;

        if (!TryParseProtocol(uri.Scheme, out ConnectionType type))
            return null;

        if (string.IsNullOrWhiteSpace(uri.Host) ||
            uri.Port < 1 ||
            uri.Port > 65535)
            return null;

        string country = fields.Count > 1 ? fields[1].Trim() : "";
        string city = fields.Count > 2 ? fields[2].Trim() : "";

        string location = $"{country} {city}".Trim();

        string name = string.IsNullOrWhiteSpace(location)
            ? $"{uri.Host}:{uri.Port}"
            : location;

        return new ConnectionProfile
        {
            Name = $"{name} ({type})",
            Host = uri.Host,
            Port = uri.Port,
            Type = type,
            Country = country,
            Ping = -1,
            IsAvailable = false
        };
    }

    private static bool TryParseProtocol(
        string protocol,
        out ConnectionType type)
    {
        switch (protocol.Trim().ToLowerInvariant())
        {
            case "http":
                type = ConnectionType.HTTP;
                return true;

            case "https":
                type = ConnectionType.HTTPS;
                return true;

            case "socks4":
            case "socks4a":
                type = ConnectionType.SOCKS4;
                return true;

            case "socks5":
                type = ConnectionType.SOCKS5;
                return true;

            default:
                type = default;
                return false;
        }
    }

    private static string GetField(
        List<string> fields,
        Dictionary<string, int> headers,
        string name)
    {
        if (!headers.TryGetValue(name, out int index))
            return "";

        return index < fields.Count
            ? fields[index].Trim()
            : "";
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var value = new System.Text.StringBuilder();

        bool insideQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (insideQuotes &&
                    i + 1 < line.Length &&
                    line[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (c == ',' && !insideQuotes)
            {
                result.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(c);
            }
        }

        result.Add(value.ToString());

        return result;
    }
}
