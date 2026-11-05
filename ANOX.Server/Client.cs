using System;
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

    /// <summary>Empty until the client is registered.</summary>
    public string Username => _clientUsername ?? string.Empty;
    public bool IsRegistered => _isRegistered;

    private string DisplayName => _clientUsername ?? "unregistered";

    public Client(TcpClient tcpClient, ILogger logger)
    {
        _tcpClient = tcpClient;
        _stream = tcpClient.GetStream();
        _channel = new MessageChannel(_stream);
        _logger = logger;
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
        string username = message.GetMessageText().Trim();

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

    private void HandleSessionConnectRequest(Message message)
    {
        string targetName = message.GetReceiverName();
        Client? clientB = ClientManager.GetClient(targetName);

        if (clientB == null)
        {
            SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.ReceiverNotFound, Username, targetName));
            return;
        }

        if (!SessionManager.TryAddSession(this, clientB, out MessageErrorType error))
        {
            SendMessage(MessageFactory.CreateErrorMessage(error, Username, targetName));
            return;
        }

        SendMessage(MessageFactory.CreateSessionConnectResponse(Username, clientB.Username,
            $"Created session with {clientB.Username}"));
        clientB.SendMessage(MessageFactory.CreateSessionConnectResponse(clientB.Username, Username,
            $"Created session with {Username}"));

        _logger.Log(LogType.Info, $"Session created: [{Username}] <-> [{clientB.Username}]");
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
            while (true)
            {
                string? receivedSerializedMessage = _channel.ReadSerialized();

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
                        ChatSession? session = SessionManager.GetSession(Username);
                        if (session != null)
                            session.Route(message, Username);
                        else
                            _logger.Log(LogType.Warning, $"[{Username}] sent UserMessage but has no active session");
                        break;

                    case MessageType.SessionLeave:
                        HandleSessionLeave();
                        break;

                    case MessageType.Ping:
                        SendMessage(MessageFactory.CreatePong(Username, message.GetMessageText()));
                        break;

                    case MessageType.CommandRequest:
                        _logger.Log(LogType.Info, $"[{Username}] executed command: /{message.GetMessageText()}");
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

            ChatSession? session = SessionManager.RemoveSession(Username);
            Client? peer = session?.GetPeer(this);
            peer?.SendMessage(MessageFactory.CreateSessionEnded(peer.Username, Username));
        }

        _logger.Log(LogType.Info, $"Client [{DisplayName}] disconnected!");
    }
}
