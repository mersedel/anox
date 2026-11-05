using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ANOX.Core;
using ANOX.Logger;

namespace ANOX.Server;

public class Client
{
    private ILogger _logger;

    private readonly object _disconnectLock = new();

    private bool _isConnected;

    private volatile bool _isRegistered;

    private TcpClient _tcpClient;
    private NetworkStream _stream;
    private MessageChannel _channel;

    private string? _clientUsername;

    private readonly int _maxMessageLength;

    /// <summary>Empty until the client is registered.</summary>
    public string Username => _clientUsername ?? string.Empty;
    public bool IsRegistered => _isRegistered;

    /// <summary>Set by a successful /login; lasts until disconnect.</summary>
    public bool IsAdmin { get; set; }

    public string RemoteAddress { get; }

    private string DisplayName => _clientUsername ?? "unregistered";

    public Client(TcpClient tcpClient, ILogger logger, ServerConfig config)
    {
        _tcpClient = tcpClient;
        _stream = tcpClient.GetStream();
        _maxMessageLength = config.MaxMessageLength;

        // Base64 of a UTF-8 message is at most ~5.4 chars per character, plus header fields.
        _channel = new MessageChannel(_stream, maxLineLength: config.MaxMessageLength * 6 + 256);
        _logger = logger;

        RemoteAddress = (tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
    }

    public void Start()
    {
        _isConnected = true;

        Thread thread = new Thread(ReceiveLoop);

        thread.IsBackground = true;

        thread.Start();
    }

    private void HandleRegister(Message message)
    {
        // Old or foreign clients: tell them to update and close the connection.
        if (!int.TryParse(message.GetMessageText(), out int clientProtocol) || clientProtocol != ProtocolInfo.Version)
        {
            SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.ProtocolMismatch, string.Empty,
                ProtocolInfo.Version.ToString()));
            _logger.Log(LogType.Warning, $"Rejected client with protocol \"{message.GetMessageText()}\" (server uses {ProtocolInfo.Version})");
            Disconnect();
            return;
        }

        string username = message.GetSenderName().Trim();

        // On failure keep the connection open: the client can retry with another name.
        if (!MessageValidator.IsValidUsername(username))
        {
            // Don't echo the rejected name back: it may contain '|' and break the protocol.
            SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.InvalidUsername, string.Empty));
            _logger.Log(LogType.Warning, $"Rejected invalid username [{username}]");
            return;
        }

        if (!ClientManager.TryAddClient(username, this))
        {
            SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.NameAlreadyTaken, username));
            _logger.Log(LogType.Warning, $"Username [{username}] already taken, waiting for another name");
            return;
        }

        _clientUsername = username;
        _isRegistered = true;
        _logger.Log(LogType.Info, $"Client registered with name [{Username}]");

        SendMessage(MessageFactory.CreateSystemMessage(Username, $"Registered successfully! Welcome, {Username} "));
    }

    /// <summary>/connect: doesn't create a session right away, but forwards a chat request to the target.</summary>
    private void HandleSessionConnectRequest(Message message)
    {
        string targetName = message.GetReceiverName();
        Client? target = ClientManager.GetClient(targetName);

        if (target == null)
        {
            SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.ReceiverNotFound, Username, targetName));
            return;
        }

        if (ReferenceEquals(target, this))
        {
            SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.CannotConnectToSelf, Username));
            return;
        }

        if (SessionManager.ContainsSession(Username) || SessionManager.ContainsSession(target.Username))
        {
            SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.AlreadyInSession, Username));
            return;
        }

        // Both asked each other: treat it as accepted.
        if (InviteManager.TryRemove(target.Username, Username))
        {
            StartSession(target, this);
            return;
        }

        if (InviteManager.Exists(Username, target.Username))
        {
            SendMessage(MessageFactory.CreateSystemMessage(Username,
                $"Your chat request to {target.Username} is still pending."));
            return;
        }

        if (!InviteManager.TryRegisterAttempt(Username, target.Username, out TimeSpan retryAfter))
        {
            SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.TooManyRequests, Username,
                Math.Ceiling(retryAfter.TotalSeconds).ToString()));
            _logger.Log(LogType.Warning, $"[{Username}] is rate-limited for chat requests");
            return;
        }

        InviteManager.Invite? replaced = InviteManager.Create(Username, target.Username);

        if (replaced != null && replaced.To != target.Username)
        {
            ClientManager.GetClient(replaced.To)?.SendMessage(
                MessageFactory.CreateSessionInviteCancelled(Username, replaced.To, InviteCancelReason.Cancelled));
        }

        target.SendMessage(MessageFactory.CreateSessionConnectRequest(Username, target.Username));
        SendMessage(MessageFactory.CreateSystemMessage(Username,
            $"Chat request sent to {target.Username}. Waiting for an answer ({(int)InviteManager.Timeout.TotalSeconds}s)..."));

        _logger.Log(LogType.Info, $"[{Username}] sent a chat request to [{target.Username}]");
    }

    /// <summary>/accept or /decline from the user who received a chat request.</summary>
    private void HandleSessionInviteResponse(Message message)
    {
        string requesterName = message.GetReceiverName();

        if (!InviteManager.TryRemove(requesterName, Username))
        {
            SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.InviteNotFound, Username, requesterName));
            return;
        }

        Client? requester = ClientManager.GetClient(requesterName);

        if (requester == null)
        {
            SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.ReceiverNotFound, Username, requesterName));
            return;
        }

        if (message.GetMessageText() == MessageFactory.InviteAccept)
        {
            StartSession(requester, this);
        }
        else
        {
            requester.SendMessage(MessageFactory.CreateSessionInviteCancelled(Username, requester.Username, InviteCancelReason.Declined));
            _logger.Log(LogType.Info, $"[{Username}] declined the chat request from [{requester.Username}]");
        }
    }

    private void StartSession(Client requester, Client acceptor)
    {
        if (!SessionManager.TryAddSession(requester, acceptor, out MessageErrorType error))
        {
            acceptor.SendMessage(MessageFactory.CreateErrorMessage(error, acceptor.Username, requester.Username));
            requester.SendMessage(MessageFactory.CreateErrorMessage(error, requester.Username, acceptor.Username));
            return;
        }

        // Other pending requests of both users are now pointless: they are busy.
        CancelInvitesOf(requester.Username, InviteCancelReason.Busy);
        CancelInvitesOf(acceptor.Username, InviteCancelReason.Busy);

        requester.SendMessage(MessageFactory.CreateSessionConnectResponse(requester.Username, acceptor.Username,
            $"Created session with {acceptor.Username}"));
        acceptor.SendMessage(MessageFactory.CreateSessionConnectResponse(acceptor.Username, requester.Username,
            $"Created session with {requester.Username}"));

        _logger.Log(LogType.Info, $"Session created: [{requester.Username}] <-> [{acceptor.Username}]");
    }

    /// <summary>Removes all requests the user sent or received and tells the other side why.</summary>
    private static void CancelInvitesOf(string username, InviteCancelReason reason)
    {
        foreach (InviteManager.Invite invite in InviteManager.RemoveAllFor(username))
        {
            string otherUser = invite.From == username ? invite.To : invite.From;

            ClientManager.GetClient(otherUser)?.SendMessage(
                MessageFactory.CreateSessionInviteCancelled(username, otherUser, reason));
        }
    }

    /// <summary>Subscribed to <see cref="InviteManager.Expired"/> in Server.Main.</summary>
    public static void OnInviteExpired(InviteManager.Invite invite)
    {
        ClientManager.GetClient(invite.From)?.SendMessage(
            MessageFactory.CreateSessionInviteCancelled(invite.To, invite.From, InviteCancelReason.Expired));
        ClientManager.GetClient(invite.To)?.SendMessage(
            MessageFactory.CreateSessionInviteCancelled(invite.From, invite.To, InviteCancelReason.Expired));
    }

    private void HandleSessionLeave()
    {
        ChatSession? session = SessionManager.RemoveSession(Username);

        if (session == null)
        {
            SendMessage(MessageFactory.CreateCommandResponse(Username, "You are not in a session."));
            return;
        }

        Client peer = session.GetPeer(this);

        // Both sides get SessionEnded with the leaver's name; the leaver recognises its own name.
        peer.SendMessage(MessageFactory.CreateSessionEnded(peer.Username, Username));
        SendMessage(MessageFactory.CreateSessionEnded(Username, Username));

        _logger.Log(LogType.Info, $"[{Username}] left the session with [{peer.Username}]");
    }

    private void ReceiveLoop()
    {
        try
        {
            while (_isConnected)
            {
                string? receivedSerializedMessage;

                try
                {
                    receivedSerializedMessage = _channel.ReadSerialized();
                }
                catch (MessageTooLongException)
                {
                    // The oversized line was skipped, the connection is still in sync.
                    SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.InvalidLength, Username));
                    _logger.Log(LogType.Warning, $"Oversized message from [{DisplayName}] dropped");
                    continue;
                }

                if (receivedSerializedMessage == null)
                {
                    break;
                }

                if (!MessageValidator.IsValid(receivedSerializedMessage))
                {
                    _logger.Log(LogType.Warning, $"Invalid message received from [{DisplayName}]");
                    continue;
                }

                Message message = MessageParser.DeserializeClientMessage(receivedSerializedMessage);

                if (!_isRegistered)
                {
                    if (message.GetMessageType() == MessageType.Register)
                    {
                        HandleRegister(message);
                    }
                    else
                    {
                        _logger.Log(LogType.Warning, $"[{message.GetMessageType()}] from unregistered client ignored");
                    }
                    continue;
                }

                switch (message.GetMessageType())
                {
                    case MessageType.SessionConnectRequest:
                        HandleSessionConnectRequest(message);
                        break;

                    case MessageType.UserMessage:
                        if (message.GetMessageText().Length > _maxMessageLength)
                        {
                            SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.InvalidLength, Username));
                            break;
                        }

                        ChatSession? session = SessionManager.GetSession(Username);
                        if (session != null)
                            session.Route(message, Username);
                        else
                            _logger.Log(LogType.Warning, $"[{Username}] sent UserMessage but has no active session");
                        break;

                    case MessageType.SessionInviteResponse:
                        HandleSessionInviteResponse(message);
                        break;

                    case MessageType.SessionLeave:
                        HandleSessionLeave();
                        break;

                    case MessageType.Ping:
                        SendMessage(MessageFactory.CreatePong(Username, message.GetMessageText()));
                        break;

                    case MessageType.CommandRequest:
                        // Log only the name: arguments may be secret (e.g. /login <password>).
                        string commandName = message.GetMessageText().Trim().Split(' ', 2)[0];
                        _logger.Log(LogType.Info, $"[{Username}] executed command: /{commandName}");
                        CommandManager.Execute(this, message.GetMessageText());
                        break;

                    case MessageType.Broadcast:
                        // nothing for now
                        break;

                    default:
                        _logger.Log(LogType.Warning, $"Unexpected message type [{message.GetMessageType()}] from [{Username}]");
                        break;
                }
            }
        }
        catch (Exception e)
        {
            if (_isConnected)
            {
                _logger.Log(LogType.Error, $"Error in Receive() from [{DisplayName}] -> {e.Message}");
            }
        }
        finally
        {
            Disconnect();
        }
    }

    /// <summary>Thread-safe: other clients' threads send to this client too.</summary>
    public void SendMessage(Message message)
    {
        try
        {
            _channel.Send(message);
        }
        catch (Exception)
        {
            Disconnect();
        }
    }

    private void Disconnect()
    {
        lock (_disconnectLock)
        {
            if (!_isConnected) return;
            _isConnected = false;
        }

        _stream.Close();
        _tcpClient.Close();

        if (_isRegistered)
        {
            ClientManager.RemoveClient(Username);
            CancelInvitesOf(Username, InviteCancelReason.Offline);

            ChatSession? session = SessionManager.RemoveSession(Username);
            Client? peer = session?.GetPeer(this);
            peer?.SendMessage(MessageFactory.CreateSessionEnded(peer.Username, Username));
        }

        _logger.Log(LogType.Info, $"Client [{DisplayName}] disconnected!");
    }
}
