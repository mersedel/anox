using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using ANOX.Core;
using ANOX.Logger;

namespace ANOX.Server;

/// <summary>
/// Executes CommandRequest messages sent by clients and answers with a CommandResponse.
/// The command list lives only here: clients forward any unknown '/command' to the server.
/// </summary>
public static class CommandManager
{
    /// <param name="Hidden">Not listed in /help (e.g. /login).</param>
    private record ServerCommand(string Name, string Description, bool AdminOnly, bool Hidden, Func<Client, string, string> Execute);

    private static readonly Dictionary<string, ServerCommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    // Failed /login attempts per IP address, so reconnecting doesn't reset the counter.
    private static readonly object _loginLock = new();
    private static readonly Dictionary<string, (int Failures, DateTime LastFailure, DateTime LockedUntil)> _loginAttempts = new();
    private static DateTime _lastLoginSweep = DateTime.MinValue;

    // Process.GetCurrentProcess() returns a new IDisposable with an OS handle each call; read the start time once.
    private static readonly DateTime _processStartTime = GetProcessStartTime();

    private static string _adminPasswordHash = string.Empty;
    private static int _maxLoginAttempts = 5;
    private static TimeSpan _loginLockout = TimeSpan.FromSeconds(60);
    private static ILogger? _logger;

    static CommandManager()
    {
        Register("help",              "Show server commands",          false, (client, _) => GetHelp(client), hidden: true);
        Register("login",             "Log in as admin",               false, Login, hidden: true);
        Register("getServerStatus",   "Server status and uptime",      false, (_, _) => GetServerStatus());
        Register("getServerInfo",     "Server version and platform",   false, (_, _) => GetServerInfo());
        Register("getServerTime",     "Current server time",           false, (_, _) => $"Server time: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Register("getAllUsersNames",  "Names of all online users",     true,  (_, _) => GetAllUsersNames());
        Register("getAllUsersAmount", "Number of online users",        true,  (_, _) => $"Users online: {ClientManager.ClientCount}");
    }

    public static void Configure(ServerConfig config, ILogger logger)
    {
        _adminPasswordHash = config.AdminPasswordHash;
        _maxLoginAttempts = config.MaxLoginAttempts;
        _loginLockout = TimeSpan.FromSeconds(config.LoginLockoutSeconds);
        _logger = logger;
    }

    private static void Register(string name, string description, bool adminOnly, Func<Client, string, string> execute, bool hidden = false)
    {
        _commands[name] = new ServerCommand(name, description, adminOnly, hidden, execute);
    }

    /// <param name="commandText">Command name without '/', followed by optional arguments.</param>
    public static void Execute(Client client, string commandText)
    {
        string[] parts = commandText.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            client.SendMessage(MessageFactory.CreateCommandResponse(client.Username, "Empty command."));
            return;
        }

        string name = parts[0];
        string args = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        if (!_commands.TryGetValue(name, out ServerCommand? command))
        {
            client.SendMessage(MessageFactory.CreateCommandResponse(client.Username,
                $"Unknown command: /{name}. Type /help for a list of commands."));
            return;
        }

        if (command.AdminOnly && !client.IsAdmin)
        {
            client.SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.Unauthorized, client.Username));
            return;
        }

        string response;
        try
        {
            response = command.Execute(client, args);
        }
        catch (Exception e)
        {
            _logger?.Log(LogType.Error, $"Command /{command.Name} from [{client.Username}] failed: {e.Message}");
            client.SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.ServerInternalError, client.Username));
            return;
        }

        client.SendMessage(MessageFactory.CreateCommandResponse(client.Username, response));
    }

    private static string Login(Client client, string password)
    {
        if (client.IsAdmin) return "You are already logged in as admin.";

        if (string.IsNullOrEmpty(_adminPasswordHash)) return "Admin login is disabled on this server.";

        if (password.Length == 0) return "Usage: /login <password>";

        string ip = client.RemoteAddress;
        DateTime now = DateTime.UtcNow;

        // PBKDF2 runs under the lock on purpose: parallel connections from one address
        // can't race past the failure counter. The per-address lockout bounds the CPU cost.
        lock (_loginLock)
        {
            SweepLoginAttempts(now);

            _loginAttempts.TryGetValue(ip, out var attempts);

            // Old failures expire: a typo last week shouldn't count towards today's lockout.
            if (attempts.LockedUntil <= now && now - attempts.LastFailure >= _loginLockout)
            {
                attempts = default;
            }

            if (attempts.LockedUntil > now)
            {
                return $"Too many failed attempts. Try again in {Math.Ceiling((attempts.LockedUntil - now).TotalSeconds)}s.";
            }

            if (PasswordHasher.Verify(password, _adminPasswordHash))
            {
                _loginAttempts.Remove(ip);
                client.IsAdmin = true;
                _logger?.Log(LogType.Info, $"[{client.Username}] logged in as admin from {ip}");
                return "Logged in as admin.";
            }

            int failures = attempts.Failures + 1;
            _logger?.Log(LogType.Warning, $"Failed admin login by [{client.Username}] from {ip} ({failures}/{_maxLoginAttempts})");

            if (failures >= _maxLoginAttempts)
            {
                _loginAttempts[ip] = (0, now, now + _loginLockout);
                return $"Wrong password. Login is locked for {(int)_loginLockout.TotalSeconds}s.";
            }

            _loginAttempts[ip] = (failures, now, DateTime.MinValue);
            return "Wrong password.";
        }
    }

    /// <summary>
    /// Forgets addresses with no recent failures and no active lockout, so the table doesn't grow
    /// with every address that ever mistyped. At most once per lockout period. Call under <see cref="_loginLock"/>.
    /// </summary>
    private static void SweepLoginAttempts(DateTime now)
    {
        if (now - _lastLoginSweep < _loginLockout) return;
        _lastLoginSweep = now;

        List<string> stale = _loginAttempts
            .Where(e => e.Value.LockedUntil <= now && now - e.Value.LastFailure >= _loginLockout)
            .Select(e => e.Key)
            .ToList();

        foreach (string key in stale) _loginAttempts.Remove(key);
    }

    private static DateTime GetProcessStartTime()
    {
        using Process process = Process.GetCurrentProcess();
        return process.StartTime;
    }

    private static string GetHelp(Client client)
    {
        IEnumerable<string> lines = _commands.Values
            .Where(c => !c.Hidden)
            .Where(c => !c.AdminOnly || client.IsAdmin)
            .Select(c => $"  /{c.Name,-21} {c.Description}{(c.AdminOnly ? " (admin)" : string.Empty)}");

        return "Server commands:" + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private static string GetServerStatus()
    {
        TimeSpan uptime = DateTime.Now - _processStartTime;

        return $"Status: online | Uptime: {(int)uptime.TotalDays}d {uptime:hh\\:mm\\:ss} | " +
               $"Users online: {ClientManager.ClientCount} | Active sessions: {SessionManager.SessionCount}";
    }

    private static string GetServerInfo()
    {
        string version = ProtocolInfo.GetAppVersion(Assembly.GetExecutingAssembly());

        return $"Server: ANOX v{version}, protocol {ProtocolInfo.Version} | " +
               $"{RuntimeInformation.FrameworkDescription} | {RuntimeInformation.OSDescription}";
    }

    private static string GetAllUsersNames()
    {
        List<string> names = ClientManager.GetAllUsernames();

        return names.Count == 0
            ? "No users online."
            : $"Users online ({names.Count}): {string.Join(", ", names)}";
    }
}
