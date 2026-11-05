using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using ANOX.Core;

namespace ANOX.Server;

/// <summary>
/// Executes CommandRequest messages sent by clients and answers with a CommandResponse.
/// The command list lives only here: clients forward any unknown '/command' to the server.
/// </summary>
public static class CommandManager
{
    private record ServerCommand(string Name, string Description, bool AdminOnly, Func<Client, string, string> Execute);

    // TODO: there is no authentication yet, so anyone can register with an admin name.
    // Replace with a real check (password/token) before relying on this.
    private static readonly HashSet<string> _adminUsernames = new(StringComparer.Ordinal)
    {
        "admin"
    };

    private static readonly Dictionary<string, ServerCommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    static CommandManager()
    {
        Register("help",              "Show server commands",          false, (client, _) => GetHelp(client));
        Register("getServerStatus",   "Server status and uptime",      false, (_, _) => GetServerStatus());
        Register("getServerInfo",     "Server version and platform",   false, (_, _) => GetServerInfo());
        Register("getServerTime",     "Current server time",           false, (_, _) => $"Server time: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Register("getAllUsersNames",  "Names of all online users",     true,  (_, _) => GetAllUsersNames());
        Register("getAllUsersAmount", "Number of online users",        true,  (_, _) => $"Users online: {ClientManager.ClientCount}");
    }

    private static void Register(string name, string description, bool adminOnly, Func<Client, string, string> execute)
    {
        _commands[name] = new ServerCommand(name, description, adminOnly, execute);
    }

    public static bool IsAdmin(Client client) =>
        client.IsRegistered && _adminUsernames.Contains(client.Username);

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

        if (command.AdminOnly && !IsAdmin(client))
        {
            client.SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.Unauthorized, client.Username));
            return;
        }

        string response;
        try
        {
            response = command.Execute(client, args);
        }
        catch (Exception)
        {
            client.SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.ServerInternalError, client.Username));
            return;
        }

        client.SendMessage(MessageFactory.CreateCommandResponse(client.Username, response));
    }

    private static string GetHelp(Client client)
    {
        bool isAdmin = IsAdmin(client);

        IEnumerable<string> lines = _commands.Values
            // The client shows its own /help line, so don't list "help" twice.
            .Where(c => c.Name != "help")
            .Where(c => !c.AdminOnly || isAdmin)
            .Select(c => $"  /{c.Name,-21} {c.Description}{(c.AdminOnly ? " (admin)" : string.Empty)}");

        return "Server commands:" + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private static string GetServerStatus()
    {
        TimeSpan uptime = DateTime.Now - Process.GetCurrentProcess().StartTime;

        return $"Status: online | Uptime: {(int)uptime.TotalDays}d {uptime:hh\\:mm\\:ss} | " +
               $"Users online: {ClientManager.ClientCount} | Active sessions: {SessionManager.SessionCount}";
    }

    private static string GetServerInfo()
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

        return $"ANOX Server v{version} | {RuntimeInformation.FrameworkDescription} | {RuntimeInformation.OSDescription}";
    }

    private static string GetAllUsersNames()
    {
        List<string> names = ClientManager.GetAllUsernames();

        return names.Count == 0
            ? "No users online."
            : $"Users online ({names.Count}): {string.Join(", ", names)}";
    }
}
