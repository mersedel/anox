using System;
using System.Collections.Generic;

namespace ANOX.Client;

/// <summary>
/// Handles chat input that starts with '/'.
/// Commands registered here run locally; any unknown command is forwarded
/// to the server as a CommandRequest, so new server commands need no client changes.
/// </summary>
public class ClientCommandManager
{
    private record ClientCommand(string Name, string Usage, string Description, Action<string> Execute);

    private readonly Chat _chat;

    private readonly Dictionary<string, ClientCommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    public ClientCommandManager(Chat chat)
    {
        _chat = chat;

        Register("help",      "/help",               "Show available commands",            _ => ShowHelp());
        Register("clear",     "/clear",              "Clear the console",                  _ => Console.Clear());
        Register("connect",   "/connect <username>", "Send a chat request to a user",      Connect);
        Register("accept",    "/accept [username]",  "Accept a chat request",              args => Answer(args, accept: true));
        Register("decline",   "/decline [username]", "Decline a chat request",             args => Answer(args, accept: false));
        Register("leave",     "/leave",              "Leave the current chat session",     _ => Leave());
        Register("ping",      "/ping",               "Measure latency to the server",      _ => Ping());
        Register("reconnect", "/reconnect",          "Reconnect to the server",            _ => _chat.Reconnect());
        Register("exit",      "/exit",               "Disconnect and close the client",    _ => _chat.Exit());
    }

    private void Register(string name, string usage, string description, Action<string> execute)
    {
        _commands[name] = new ClientCommand(name, usage, description, execute);
    }

    /// <param name="input">Raw input line, including the leading '/'.</param>
    public void Handle(string input)
    {
        string[] parts = input.Substring(1).Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            Console.WriteLine("Empty command. Type /help for a list of commands.");
            return;
        }

        string name = parts[0];
        string args = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        if (_commands.TryGetValue(name, out ClientCommand? command))
        {
            command.Execute(args);
            return;
        }

        ForwardToServer(name, args);
    }

    private void ForwardToServer(string name, string args)
    {
        if (!_chat.IsConnected)
        {
            Console.WriteLine("Not connected to the server. Use /reconnect.");
            return;
        }

        string commandText = args.Length > 0 ? $"{name} {args}" : name;
        _chat.SendCommandRequest(commandText);
    }

    private void ShowHelp()
    {
        Console.WriteLine("Client commands:");
        foreach (ClientCommand command in _commands.Values)
        {
            Console.WriteLine($"  {command.Usage,-22} {command.Description}");
        }
        Console.WriteLine("  //text                 Send a message that starts with '/'");

        // Server commands are known only to the server, so ask it for its own list.
        if (_chat.IsConnected)
        {
            _chat.SendCommandRequest("help");
        }
    }

    /// <summary>Without a name answers the only pending request; with several, the name is required.</summary>
    private void Answer(string args, bool accept)
    {
        if (!_chat.IsConnected)
        {
            Console.WriteLine("Not connected to the server. Use /reconnect.");
            return;
        }

        List<string> invites = _chat.GetIncomingInvites();
        string name = args.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } words ? words[0] : string.Empty;
        string verb = accept ? "/accept" : "/decline";

        if (invites.Count == 0)
        {
            Console.WriteLine("No pending chat requests.");
            return;
        }

        if (name.Length == 0)
        {
            if (invites.Count > 1)
            {
                Console.WriteLine($"Several chat requests: {string.Join(", ", invites)}. Use {verb} <username>.");
                return;
            }

            name = invites[0];
        }

        string? requester = invites.Find(i => string.Equals(i, name, StringComparison.OrdinalIgnoreCase));

        if (requester == null)
        {
            Console.WriteLine($"No pending chat request from {name}.");
            return;
        }

        if (accept && _chat.IsInSession)
        {
            Console.WriteLine($"You are already chatting with {_chat.ConnectedToUsername}. Use /leave first.");
            return;
        }

        _chat.AnswerInvite(requester, accept);
    }

    private void Leave()
    {
        if (!_chat.IsInSession)
        {
            Console.WriteLine("You are not in a chat session.");
            return;
        }

        _chat.LeaveSession();
    }

    private void Ping()
    {
        if (!_chat.IsConnected)
        {
            Console.WriteLine("Not connected to the server. Use /reconnect.");
            return;
        }

        _chat.SendPing();
    }

    private void Connect(string args)
    {
        string target = args.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } words
            ? words[0]
            : string.Empty;

        if (target.Length == 0)
        {
            Console.WriteLine("Usage: /connect <username>");
            return;
        }

        if (!_chat.IsConnected)
        {
            Console.WriteLine("Not connected to the server. Use /reconnect.");
            return;
        }

        if (_chat.IsInSession)
        {
            Console.WriteLine($"Already in a session with {_chat.ConnectedToUsername}.");
            return;
        }

        if (string.Equals(target, _chat.Username, StringComparison.Ordinal))
        {
            Console.WriteLine("You can't connect to yourself.");
            return;
        }

        _chat.RequestSession(target);
    }
}
