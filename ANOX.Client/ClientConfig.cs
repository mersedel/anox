using System;
using System.IO;
using System.Text.Json;

namespace ANOX.Client;

/// <summary>client.json next to the executable: remembers the last server address.</summary>
public class ClientConfig
{
    public const int DefaultPort = 5656;

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = DefaultPort;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "client.json");

    /// <summary>Returns an empty config if the file is missing or broken.</summary>
    public static ClientConfig Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<ClientConfig>(File.ReadAllText(path), _jsonOptions) ?? new ClientConfig();
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Can't read {path}: {e.Message}");
        }

        return new ClientConfig();
    }

    public void Save(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, _jsonOptions));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Can't save {path}: {e.Message}");
        }
    }

    /// <summary>Parses "host" or "host:port" (IPv6 as "[::1]:5656").</summary>
    public static bool TryParseAddress(string input, out string host, out int port)
    {
        input = input.Trim();
        host = input;
        port = DefaultPort;

        if (input.Length == 0) return false;

        int colon = input.LastIndexOf(':');
        bool isBracketedIpv6 = input.StartsWith('[');
        bool hasPort = colon > 0 && (isBracketedIpv6 ? input.IndexOf(']') < colon : input.IndexOf(':') == colon);

        if (hasPort)
        {
            if (!int.TryParse(input.AsSpan(colon + 1), out port) || port is < 1 or > 65535) return false;
            host = input.Substring(0, colon);
        }

        host = host.Trim('[', ']');
        return host.Length > 0;
    }
}
