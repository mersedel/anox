using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using ANOX.Core;
using ANOX.Logger;

namespace ANOX.Client;

public class Chat
{
    private ILogger _logger = new ConsoleLogger();

    private readonly object _connectionLock = new();
    private readonly ManualResetEvent _exitEvent = new(false);

    private readonly ClientCommandManager _commandManager;

    private TcpClient? _tcpClient;
    private MessageChannel? _channel;

    private volatile bool _isConnected = false;
    private volatile bool _isRegistered = false;
    private volatile bool _isInSession = false;
    private volatile bool _isExiting = false;

    // Set when the server rejected our name: the next input line is treated as a new username.
    private volatile bool _isAwaitingUsername = false;

    // Users who sent us a chat request that we haven't answered yet.
    private readonly List<string> _incomingInvites = new();

    private string _username = string.Empty;
    private string? _connectedToUsername;

    private string _serverIp;
    private int _port;

    public static string VersionText =>
        $"ANOX Client v{ProtocolInfo.GetAppVersion(Assembly.GetExecutingAssembly())}, protocol {ProtocolInfo.Version}";

    public bool IsConnected => _isConnected;
    public bool IsInSession => _isInSession;
    public string Username => _username;
    public string? ConnectedToUsername => _connectedToUsername;

    public Chat(string serverIp, int port)
    {
        _serverIp = serverIp;
        _port = port;

        _commandManager = new ClientCommandManager(this);
    }

    public void Start()
    {
        AskUsername();

        Console.WriteLine($"{VersionText} | server {_serverIp}:{_port}");

        Connect();

        Thread inputThread = new Thread(InputLoop);
        inputThread.IsBackground = true;
        inputThread.Start();

        _exitEvent.WaitOne();
    }

    private void AskUsername()
    {
        while (true)
        {
            Console.WriteLine("Enter Username: ");
            string? input = Console.ReadLine();

            if (input == null) Environment.Exit(0);

            if (TrySetUsername(input)) break;
        }

        ClearConsole();
        SetTitle($"ANOX - {_username}");
    }

    private void Connect()
    {
        TcpClient tcpClient = new TcpClient();

        try
        {
            tcpClient.Connect(_serverIp, _port);
        }
        catch (Exception e)
        {
            tcpClient.Dispose();
            _logger.Log(LogType.Error, $"Can't connect to server: {e.Message}. Use /reconnect to try again.");
            return;
        }

        lock (_connectionLock)
        {
            _tcpClient = tcpClient;
            _channel = new MessageChannel(tcpClient.GetStream());

            _isConnected = true;
            _isRegistered = false;
            _isInSession = false;
            _isAwaitingUsername = false;
            _connectedToUsername = null;
        }

        MessageChannel channel = _channel;

        Thread receiveThread = new Thread(() => ReceiveLoop(tcpClient, channel));
        receiveThread.IsBackground = true;
        receiveThread.Start();

        SendMessage(MessageFactory.CreateRegisterMessage(_username));
    }

    public void Reconnect()
    {
        Disconnect(showHint: false);
        _logger.Log(LogType.Info, "Reconnecting...");
        Connect();
    }

    public void Exit()
    {
        _isExiting = true;
        Disconnect(showHint: false);
        _exitEvent.Set();
    }

    public void RequestSession(string username)
    {
        SendMessage(MessageFactory.CreateSessionConnectRequest(_username, username));
    }

    public List<string> GetIncomingInvites()
    {
        lock (_incomingInvites)
        {
            return new List<string>(_incomingInvites);
        }
    }

    public void AnswerInvite(string requester, bool accept)
    {
        lock (_incomingInvites)
        {
            _incomingInvites.Remove(requester);
        }

        SendMessage(MessageFactory.CreateSessionInviteResponse(_username, requester, accept));

        if (!accept)
        {
            Console.WriteLine($"Declined the chat request from {requester}.");
        }
    }

    private void ClearIncomingInvites()
    {
        lock (_incomingInvites)
        {
            _incomingInvites.Clear();
        }
    }

    private void OnInviteReceived(string requester)
    {
        lock (_incomingInvites)
        {
            if (!_incomingInvites.Contains(requester)) _incomingInvites.Add(requester);
        }

        Console.WriteLine();
        Console.WriteLine($">>> {requester} wants to chat with you. Type /accept or /decline.");
    }

    private void OnInviteCancelled(string otherUser, string reasonText)
    {
        if (!Enum.TryParse(reasonText, out InviteCancelReason reason)) return;

        bool wasIncoming;
        lock (_incomingInvites)
        {
            wasIncoming = _incomingInvites.Remove(otherUser);
        }

        string text = (wasIncoming, reason) switch
        {
            (_, InviteCancelReason.Offline) => $"{otherUser} went offline.",
            (_, InviteCancelReason.Busy)    => $"{otherUser} is now in another chat.",
            (true, InviteCancelReason.Expired)   => $"Chat request from {otherUser} expired.",
            (true, InviteCancelReason.Cancelled) => $"{otherUser} cancelled the chat request.",
            (false, InviteCancelReason.Declined) => $"{otherUser} declined your chat request.",
            (false, InviteCancelReason.Expired)  => $"Your chat request to {otherUser} expired.",
            _ => $"Chat request with {otherUser} cancelled."
        };

        Console.WriteLine(text);
    }

    public void LeaveSession()
    {
        SendMessage(MessageFactory.CreateSessionLeave(_username));
    }

    /// <summary>Sends the current timestamp; the server echoes it back, so the round trip needs no stored state.</summary>
    public void SendPing()
    {
        SendMessage(MessageFactory.CreatePing(_username, Stopwatch.GetTimestamp().ToString()));
    }

    public void SendCommandRequest(string commandText)
    {
        SendMessage(MessageFactory.CreateCommandRequest(_username, commandText));
    }

    private void SendMessage(Message message)
    {
        MessageChannel? channel = _channel;

        if (!_isConnected || channel == null)
        {
            _logger.Log(LogType.Warning, "Not connected to the server. Use /reconnect.");
            return;
        }

        try
        {
            channel.Send(message);
        }
        catch (Exception e)
        {
            _logger.Log(LogType.Error, $"Failed to send message: {e.Message}");
            Disconnect();
        }
    }

    private void InputLoop()
    {
        while (!_isExiting)
        {
            string? input = Console.ReadLine();

            if (input == null)
            {
                // stdin closed (e.g. Ctrl+Z / Ctrl+D)
                Exit();
                break;
            }

            if (string.IsNullOrWhiteSpace(input)) continue;

            try
            {
                HandleInput(input);
            }
            catch (Exception e)
            {
                _logger.Log(LogType.Error, $"Error while handling input: {e.Message}");
            }
        }
    }

    private void HandleInput(string input)
    {
        // "//text" sends a regular message that starts with '/'
        if (input.StartsWith("//"))
        {
            SendUserMessage(input.Substring(1), input);
            return;
        }

        if (input.StartsWith('/'))
        {
            _commandManager.Handle(input);
            return;
        }

        if (_isAwaitingUsername)
        {
            SubmitUsername(input);
            return;
        }

        SendUserMessage(input, input);
    }

    private void SubmitUsername(string input)
    {
        if (!TrySetUsername(input))
        {
            Console.WriteLine("Enter another username: ");
            return;
        }

        _isAwaitingUsername = false;
        SendMessage(MessageFactory.CreateRegisterMessage(_username));
    }

    /// <summary>Checks the name locally first; the server validates it again.</summary>
    private bool TrySetUsername(string input)
    {
        string name = input.Trim();

        if (!MessageValidator.IsValidUsername(name))
        {
            Console.WriteLine($"Invalid username. Use 1-{MessageValidator.MaxUsernameLength} letters, digits, '_', '-' or '.'.");
            return false;
        }

        _username = name;
        return true;
    }

    /// <param name="text">Message to send.</param>
    /// <param name="rawInput">The line as the user typed it (to erase its echo).</param>
    private void SendUserMessage(string text, string rawInput)
    {
        if (!_isConnected)
        {
            Console.WriteLine("Not connected to the server. Use /reconnect.");
            return;
        }

        if (!_isInSession || _connectedToUsername == null)
        {
            Console.WriteLine("No active session. Use /connect <username>.");
            return;
        }

        SendMessage(MessageFactory.CreateUserMessage(_username, _connectedToUsername, text));
        ShowOwnMessage(text, rawInput);
    }

    /// <summary>
    /// Replaces the line the console echoed while typing with "[time] me: text",
    /// so own messages look the same as incoming ones.
    /// </summary>
    private void ShowOwnMessage(string text, string rawInput)
    {
        EraseInputLine(rawInput);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {_username}: {text}");
    }

    /// <summary>Removes the line the console echoed while the user typed it (also used to hide /login passwords).</summary>
    public void EraseInputLine(string rawInput)
    {
        try
        {
            int width = Console.BufferWidth;
            int lines = Math.Max(1, (rawInput.Length + width - 1) / width);
            int top = Math.Max(0, Console.CursorTop - lines);

            Console.SetCursorPosition(0, top);
            Console.Write(new string(' ', width * lines));
            Console.SetCursorPosition(0, top);
        }
        catch
        {
            // Output is redirected or the terminal doesn't support cursor moves.
        }
    }

    private void ReceiveLoop(TcpClient tcpClient, MessageChannel channel)
    {
        try
        {
            while (true)
            {
                string? serializedMessage = channel.ReadSerialized();

                if (serializedMessage == null)
                {
                    break;
                }

                if (!MessageValidator.IsValid(serializedMessage))
                {
                    _logger.Log(LogType.Warning, "Invalid message received from server, ignored.");
                    continue;
                }

                Message message = MessageParser.DeserializeClientMessage(serializedMessage);

                switch (message.GetMessageType())
                {
                    case MessageType.ServerError:
                        var (errorType, errorText) = MessageParser.ParseError(message);
                        _logger.Log(LogType.Error, $"Error from server: {errorText}");

                        // The name was rejected: ask for another one (the connection stays open).
                        if (!_isRegistered && errorType is MessageErrorType.NameAlreadyTaken or MessageErrorType.InvalidUsername)
                        {
                            _isAwaitingUsername = true;
                            Console.WriteLine("Enter another username: ");
                        }
                        break;

                    case MessageType.SystemInfo:
                        _logger.Log(LogType.Info, message.GetMessageText());

                        if (!_isRegistered)
                        {
                            _isRegistered = true;
                            Console.WriteLine("Type /connect <username> to start chatting or /help for a list of commands.");
                        }
                        break;

                    case MessageType.SessionConnectRequest:
                        OnInviteReceived(message.GetSenderName());
                        break;

                    case MessageType.SessionInviteCancelled:
                        OnInviteCancelled(message.GetSenderName(), message.GetMessageText());
                        break;

                    case MessageType.SessionConnectResponse:
                        ClearIncomingInvites();
                        _connectedToUsername = message.GetReceiverName();
                        _isInSession = true;
                        ShowSessionStarted(_connectedToUsername);
                        break;

                    case MessageType.CommandResponse:
                        Console.WriteLine(message.GetMessageText());
                        break;

                    case MessageType.SessionEnded:
                        string? peer = _connectedToUsername;
                        _isInSession = false;
                        _connectedToUsername = null;
                        if (peer != null) ShowSessionEnded(peer);

                        string leaver = message.GetMessageText();
                        Console.WriteLine(leaver == _username
                            ? "You left the chat. Use /connect <username> to start a new one."
                            : $"{leaver} left the chat. Use /connect <username> to start a new one.");
                        break;

                    case MessageType.Pong:
                        if (long.TryParse(message.GetMessageText(), out long sentAt))
                        {
                            TimeSpan rtt = Stopwatch.GetElapsedTime(sentAt);
                            Console.WriteLine($"Pong from server: {rtt.TotalMilliseconds:0.#} ms");
                        }
                        break;

                    case MessageType.UserMessage:
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message.GetSenderName()}: {message.GetMessageText()}");
                        break;

                    default:
                        _logger.Log(LogType.Info, $"Message from [{message.GetSenderName()}]: {message.GetMessageText()}");
                        break;
                }
            }
        }
        catch (Exception e)
        {
            // Reading from a socket we closed ourselves (Disconnect/Reconnect) is expected.
            if (IsCurrentConnection(tcpClient))
            {
                _logger.Log(LogType.Error, $"Error in ReceiveLoop(): {e.Message}");
            }
        }
        finally
        {
            Disconnect(tcpClient);
        }
    }

    private bool IsCurrentConnection(TcpClient tcpClient)
    {
        lock (_connectionLock)
        {
            return _isConnected && ReferenceEquals(_tcpClient, tcpClient);
        }
    }

    private void ShowSessionStarted(string peer)
    {
        SetTitle($"ANOX - {_username} <-> {peer}");

        Console.WriteLine();
        Console.WriteLine($"===== Chat with {peer} =====");
        Console.WriteLine("Type a message and press Enter. /help for commands.");
    }

    private void ShowSessionEnded(string peer)
    {
        SetTitle($"ANOX - {_username}");

        Console.WriteLine($"===== Chat with {peer} ended =====");
    }

    /// <summary>Console.Clear throws when output is redirected (pipes, IDE consoles).</summary>
    public static void ClearConsole()
    {
        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
        }
    }

    private static void SetTitle(string title)
    {
        try
        {
            Console.Title = title;
        }
        catch
        {
            // Not every terminal supports changing the title.
        }
    }

    private void Disconnect(bool showHint = true)
    {
        TcpClient? current;

        lock (_connectionLock)
        {
            current = _tcpClient;
        }

        if (current != null)
        {
            Disconnect(current, showHint);
        }
    }

    /// <summary>
    /// Closes the given connection only if it is still the current one,
    /// so a receive thread of an old connection can't close a new one after /reconnect.
    /// </summary>
    private void Disconnect(TcpClient tcpClient, bool showHint = true)
    {
        string? endedSessionWith;

        lock (_connectionLock)
        {
            if (!_isConnected || !ReferenceEquals(_tcpClient, tcpClient)) return;

            endedSessionWith = _isInSession ? _connectedToUsername : null;

            _isConnected = false;
            _isRegistered = false;
            _isInSession = false;
            _isAwaitingUsername = false;
            _connectedToUsername = null;
            ClearIncomingInvites();

            // Closing the socket unblocks the receive thread's ReadLine; the channel itself holds nothing else.
            _tcpClient?.Close();

            _channel = null;
            _tcpClient = null;
        }

        if (endedSessionWith != null)
        {
            ShowSessionEnded(endedSessionWith);
        }

        _logger.Log(LogType.Info, showHint && !_isExiting
            ? "Disconnected from server! Use /reconnect to connect again or /exit to quit."
            : "Disconnected from server!");
    }
}
