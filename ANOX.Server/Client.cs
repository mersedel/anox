using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ANOX.Core;
using ANOX.Logger;

namespace ANOX.Server;

public class Client
{
    private readonly ILogger _logger;

    // Guards the connected/registered transition: registration and disconnect must not interleave,
    // otherwise a client could be added to ClientManager after Disconnect already ran (a "ghost" name).
    private readonly object _disconnectLock = new();

    // Read by other threads (ReceiveLoop condition, SendMessage callers), so volatile.
    private volatile bool _isConnected;
    private volatile bool _isRegistered;

    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private readonly MessageChannel _channel;

    private Timer? _registerTimer;
    private readonly int _registerTimeoutSeconds;

    // Set before the client becomes visible in ClientManager, read by other clients' threads.
    private volatile string? _clientUsername;

    private readonly int _maxMessageLength;

    // Flood protection; touched only by the receive thread.
    private readonly RateLimiter _rateLimiter;
    private bool _rateLimitNotified;
    private readonly int _maxProtocolViolations;
    private int _protocolViolations;

    /// <summary>Empty until the client is registered.</summary>
    public string Username => _clientUsername ?? string.Empty;
    public bool IsRegistered => _isRegistered;

    /// <summary>Set by a successful /login; lasts until disconnect.</summary>
    public bool IsAdmin { get; set; }

    public string RemoteAddress { get; }

    /// <summary>Key for per-address limits, see <see cref="ConnectionLimiter.GetAddressKey"/>.</summary>
    public string AddressKey { get; }

    private string DisplayName => _clientUsername ?? "unregistered";

    /// <param name="addressKey">Slot already taken with <see cref="ConnectionLimiter.TryAcquire"/>; released on disconnect.</param>
    public Client(TcpClient tcpClient, string addressKey, ILogger logger, ServerConfig config)
    {
        _tcpClient = tcpClient;
        _stream = tcpClient.GetStream();

        // A peer that stops reading would block Write forever (and with it the thread of whoever sends to it,
        // e.g. its chat partner or the invite timer). With a timeout the write fails and the peer is dropped.
        _stream.WriteTimeout = config.SendTimeoutSeconds * 1000;

        _maxMessageLength = config.MaxMessageLength;
        _registerTimeoutSeconds = config.RegisterTimeoutSeconds;
        _rateLimiter = new RateLimiter(config.MaxMessagesPerSecond, config.MessageBurst);
        _maxProtocolViolations = config.MaxProtocolViolations;

        // Base64 of a UTF-8 message is at most ~5.4 chars per character, plus header fields.
        _channel = new MessageChannel(_stream, maxLineLength: config.MaxMessageLength * 6 + 256);
        _logger = logger;

        RemoteAddress = (tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
        AddressKey = addressKey;

        // Connected from construction on, so Close() always releases the socket and the limiter slot.
        _isConnected = true;
    }

    public void Start()
    {
        // Close the connection if the client doesn't register in time (bots, port scanners).
        _registerTimer = new Timer(_ => OnRegisterTimeout(), null,
            TimeSpan.FromSeconds(_registerTimeoutSeconds), Timeout.InfiniteTimeSpan);

        Thread thread = new Thread(ReceiveLoop);

        thread.IsBackground = true;

        thread.Start();
    }

    /// <summary>Closes the connection (used by the server when setting up the client fails).</summary>
    public void Close() => Disconnect();

    private void HandleRegister(Message message)
    {
        // Old or foreign clients: tell them to update and close the connection.
        if (!int.TryParse(message.GetMessageText(), out int clientProtocol) || clientProtocol != ProtocolInfo.Version)
        {
            SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.ProtocolMismatch, string.Empty,
                ProtocolInfo.Version.ToString()));
            _logger.Log(LogType.Warning, $"Rejected client with protocol \"{LogText.Safe(message.GetMessageText(), 16)}\" (server uses {ProtocolInfo.Version})");
            Disconnect();
            return;
        }

        string username = message.GetSenderName().Trim();

        // On failure keep the connection open: the client can retry with another name.
        if (!MessageValidator.IsValidUsername(username))
        {
            // Don't echo the rejected name back: it may contain '|' and break the protocol.
            SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.InvalidUsername, string.Empty));
            _logger.Log(LogType.Warning, $"Rejected invalid username [{LogText.Safe(username)}]");
            return;
        }

        bool added;
        lock (_disconnectLock)
        {
            // The register timeout may have closed the connection meanwhile: don't add a dead client.
            if (!_isConnected) return;

            // The name must be set before other threads can find this client in ClientManager,
            // otherwise they would briefly see an empty Username.
            _clientUsername = username;
            added = ClientManager.TryAddClient(username, this);

            if (added) _isRegistered = true;
            else _clientUsername = null;
        }

        if (!added)
        {
            SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.NameAlreadyTaken, username));
            _logger.Log(LogType.Warning, $"Username [{username}] already taken, waiting for another name");
            return;
        }

        // Not needed anymore; disposing also frees the reference the timer keeps to this client.
        Interlocked.Exchange(ref _registerTimer, null)?.Dispose();

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

        // The target may have disconnected after GetClient above; its Disconnect already cleaned up its invites,
        // so this one would hang until it expires (or be accepted by someone who re-registered the name).
        if (!ReferenceEquals(ClientManager.GetClient(target.Username), target))
        {
            if (InviteManager.TryRemove(Username, target.Username))
            {
                SendMessage(MessageFactory.CreateSessionInviteCancelled(target.Username, Username, InviteCancelReason.Offline));
            }
            return;
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

        // Names are case-insensitive in ClientManager but invites store the exact registered name.
        Client? requester = ClientManager.GetClient(requesterName);
        if (requester != null) requesterName = requester.Username;

        if (!InviteManager.TryRemove(requesterName, Username))
        {
            SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.InviteNotFound, Username, requesterName));
            return;
        }

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

    /// <summary>
    /// Counts malformed or unexpected input. A real client never produces it, so after a few
    /// the connection is closed: otherwise a bot could fill the log (and the disk) with warnings.
    /// </summary>
    private void ReportViolation(string reason)
    {
        _protocolViolations++;
        _logger.Log(LogType.Warning, $"{reason} from [{DisplayName}] ({_protocolViolations}/{_maxProtocolViolations})");

        if (_protocolViolations >= _maxProtocolViolations)
        {
            _logger.Log(LogType.Warning, $"Too many invalid messages from [{DisplayName}] ({RemoteAddress}), disconnecting");
            Disconnect();
        }
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
                    ReportViolation("Oversized message");
                    continue;
                }

                if (receivedSerializedMessage == null)
                {
                    break;
                }

                // Flood protection: drop what's over the limit and tell the client once per burst.
                if (!_rateLimiter.TryTake())
                {
                    if (!_rateLimitNotified)
                    {
                        _rateLimitNotified = true;
                        SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.RateLimited, Username));
                        _logger.Log(LogType.Warning, $"[{DisplayName}] is sending too fast, messages dropped");
                    }
                    continue;
                }

                _rateLimitNotified = false;

                if (!MessageValidator.IsValid(receivedSerializedMessage))
                {
                    ReportViolation("Invalid message");
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
                        ReportViolation($"[{message.GetMessageType()}] before registration");
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
                            session.Route(this, message.GetMessageText());
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
                        _logger.Log(LogType.Info, $"[{Username}] executed command: /{LogText.Safe(commandName, 32)}");
                        CommandManager.Execute(this, message.GetMessageText());
                        break;

                    default:
                        ReportViolation($"Unexpected message type [{message.GetMessageType()}]");
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
        if (!_isConnected) return;

        try
        {
            _channel.Send(message);
        }
        catch (Exception)
        {
            // Includes the write timeout: a client that doesn't read is dropped.
            Disconnect();
        }
    }

    /// <summary>Disconnects the client if it didn't register within RegisterTimeoutSeconds.</summary>
    private void OnRegisterTimeout()
    {
        if (!_isRegistered)
        {
            _logger.Log(LogType.Info, $"Client from {RemoteAddress} didn't register in time");
            Disconnect();
        }
    }

    private void Disconnect()
    {
        bool wasRegistered;

        lock (_disconnectLock)
        {
            if (!_isConnected) return;
            _isConnected = false;

            // Read under the lock: HandleRegister can't flip it anymore, so the cleanup below is complete.
            wasRegistered = _isRegistered;
        }

        Interlocked.Exchange(ref _registerTimer, null)?.Dispose();

        try
        {
            _stream.Close();
            _tcpClient.Close();
        }
        catch (Exception)
        {
            // The socket is already broken; nothing else to release.
        }

        ConnectionLimiter.Release(AddressKey);

        if (wasRegistered)
        {
            // Order matters. Invites are keyed by name, so cancel them while the name is still ours:
            // after RemoveClient someone else may register it and must not lose their own invites.
            CancelInvitesOf(Username, InviteCancelReason.Offline);
            ClientManager.RemoveClient(Username, this);

            // By reference, for the same reason; TryAddSession refuses clients no longer in ClientManager.
            ChatSession? session = SessionManager.RemoveSession(this);
            Client? peer = session?.GetPeer(this);
            peer?.SendMessage(MessageFactory.CreateSessionEnded(peer.Username, Username));
        }

        _logger.Log(LogType.Info, $"Client [{DisplayName}] disconnected!");
    }
}
