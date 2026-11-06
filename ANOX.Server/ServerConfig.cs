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

    /// <summary>Seconds a new connection has to register before it is closed.</summary>
    public int RegisterTimeoutSeconds { get; set; } = 10;

    /// <summary>Open connections allowed in total and from one IP address (IPv6: per /64).</summary>
    public int MaxConnections { get; set; } = 500;
    public int MaxConnectionsPerIp { get; set; } = 5;

    /// <summary>A client that doesn't read its data for this long is disconnected.</summary>
    public int SendTimeoutSeconds { get; set; } = 10;

    /// <summary>Per-connection flood limit: average messages per second and allowed burst.</summary>
    public int MaxMessagesPerSecond { get; set; } = 5;
    public int MessageBurst { get; set; } = 20;

    /// <summary>Malformed/unexpected messages tolerated before the connection is closed.</summary>
    public int MaxProtocolViolations { get; set; } = 10;

    /// <summary>Hash for /login. Empty = admin login disabled. Generate with: AnoxServer --hash-password</summary>
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
        RequirePositive(RegisterTimeoutSeconds, nameof(RegisterTimeoutSeconds));
        RequirePositive(MaxConnections, nameof(MaxConnections));
        RequirePositive(MaxConnectionsPerIp, nameof(MaxConnectionsPerIp));
        RequirePositive(SendTimeoutSeconds, nameof(SendTimeoutSeconds));
        RequirePositive(MaxMessagesPerSecond, nameof(MaxMessagesPerSecond));
        RequirePositive(MessageBurst, nameof(MessageBurst));
        RequirePositive(MaxProtocolViolations, nameof(MaxProtocolViolations));

        // MaxMessageLength * 6 is the line limit; larger values would overflow int.
        if (MaxMessageLength > 100_000)
            throw new InvalidDataException($"{nameof(MaxMessageLength)} can't be greater than 100000.");

        if (InviteSameUserCooldownSeconds < 0)
            throw new InvalidDataException($"{nameof(InviteSameUserCooldownSeconds)} can't be negative.");

        // The send history is kept only for the rate window; a longer cooldown would silently stop working.
        if (InviteSameUserCooldownSeconds > InviteRateWindowSeconds)
            throw new InvalidDataException($"{nameof(InviteSameUserCooldownSeconds)} can't be greater than {nameof(InviteRateWindowSeconds)}.");

        // Timer/stream timeouts are in milliseconds (int).
        if (SendTimeoutSeconds > 3600 || RegisterTimeoutSeconds > 3600)
            throw new InvalidDataException("Timeouts can't be longer than 3600 seconds.");
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0) throw new InvalidDataException($"{name} must be greater than 0.");
    }
}
