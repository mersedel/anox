using System;
using System.IO;
using System.Net;
using System.Text.Json;

namespace ANOX.Server;

/// <summary>
/// Settings from server.json (next to the executable, or the path given with --config).
/// A file with defaults is created on first start.
/// </summary>
public class ServerConfig
{
    /// <summary>Address to listen on. "0.0.0.0" = all network interfaces.</summary>
    public string Host { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 5656;

    /// <summary>Hash for /login. Empty = admin login disabled. Generate with: Server --hash-password</summary>
    public string AdminPasswordHash { get; set; } = string.Empty;

    /// <summary>Failed /login attempts allowed before a temporary lockout.</summary>
    public int MaxLoginAttempts { get; set; } = 5;
    public int LoginLockoutSeconds { get; set; } = 60;

    public int InviteTimeoutSeconds { get; set; } = 60;
    public int InviteSameUserCooldownSeconds { get; set; } = 15;
    public int InviteRateWindowSeconds { get; set; } = 60;
    public int MaxInvitesPerWindow { get; set; } = 5;

    /// <summary>Longest chat message in characters.</summary>
    public int MaxMessageLength { get; set; } = 2000;

    /// <summary>Log file path (relative to the executable). Empty = console only.</summary>
    public string LogFilePath { get; set; } = "logs/server.log";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <param name="created">True if the file didn't exist and was created with defaults.</param>
    /// <exception cref="InvalidDataException">The file can't be parsed or has invalid values.</exception>
    public static ServerConfig LoadOrCreate(string path, out bool created)
    {
        if (!File.Exists(path))
        {
            ServerConfig defaults = new ServerConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, _jsonOptions));
            created = true;
            return defaults;
        }

        created = false;

        ServerConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(path), _jsonOptions);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Can't parse {path}: {e.Message}");
        }

        if (config == null) throw new InvalidDataException($"{path} is empty.");

        config.Validate();
        return config;
    }

    public string ResolvePath(string relativeOrAbsolute) =>
        Path.IsPathRooted(relativeOrAbsolute)
            ? relativeOrAbsolute
            : Path.Combine(AppContext.BaseDirectory, relativeOrAbsolute);

    private void Validate()
    {
        if (!IPAddress.TryParse(Host, out _))
            throw new InvalidDataException($"Host \"{Host}\" is not a valid IP address.");
        if (Port is < 1 or > 65535)
            throw new InvalidDataException("Port must be between 1 and 65535.");

        RequirePositive(MaxLoginAttempts, nameof(MaxLoginAttempts));
        RequirePositive(LoginLockoutSeconds, nameof(LoginLockoutSeconds));
        RequirePositive(InviteTimeoutSeconds, nameof(InviteTimeoutSeconds));
        RequirePositive(InviteRateWindowSeconds, nameof(InviteRateWindowSeconds));
        RequirePositive(MaxInvitesPerWindow, nameof(MaxInvitesPerWindow));
        RequirePositive(MaxMessageLength, nameof(MaxMessageLength));

        if (InviteSameUserCooldownSeconds < 0)
            throw new InvalidDataException($"{nameof(InviteSameUserCooldownSeconds)} can't be negative.");
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0) throw new InvalidDataException($"{name} must be greater than 0.");
    }
}
