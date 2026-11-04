using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ANOX.Core;
using ANOX.Logger;

namespace ANOX.Client;

public class Chat
{
    private ILogger _logger = new ConsoleLogger();
    
    private byte[] _receiveBuffer = new byte[1024];
    
    private TcpClient _tcpClient;
    private NetworkStream _stream;

    private bool _isConnected = false;
    
    private bool _isRegistered = false;

    private bool _isInSession = false;
    private bool _isReceiving = true;
    private bool _isSending = true;
    
    private string _username;
    private string _connectedToUsername;
    
    private string _serverIp;
    private int _port;

    public Chat(string serverIp, int port)
    {
        _serverIp = serverIp;
        _port = port;
    }

    public void Start()
    {
        _tcpClient = new TcpClient();
        _tcpClient.Connect(_serverIp, _port);
        _isConnected = true;

        _stream = _tcpClient.GetStream();
        
        if (!_isRegistered)
        {
            RegisterClient();
        }
        
        
        Thread receiveThread = new Thread(ReceiveLoop);
        receiveThread.IsBackground = true;
        receiveThread.Start();

        Thread sendThread = new Thread(SendLoop);
        sendThread.IsBackground = true;
        sendThread.Start();
        
        Thread.Sleep(Timeout.Infinite);
    }

    private void RegisterClient()
    {
        Console.WriteLine("Enter Username: ");

        _username = Console.ReadLine();
        
        SendMessage(MessageFactory.CreateRegisterMessage(_username));
        
        Console.Clear();
    }

    private void ConnectToUser()
    {
        if (_isInSession) return;
    
        Console.WriteLine("\nEnter username to connect to: ");
        _connectedToUsername = Console.ReadLine();
        SendMessage(MessageFactory.CreateSessionConnectRequest(_username, _connectedToUsername));
    
        while (!_isInSession)
        {
            Thread.Sleep(50);
        }
    }

    private void SendMessage(Message message)
    {
        string serializedMessage = MessageParser.SerializeMessage(message);
        
        _stream.Write(Encoding.UTF8.GetBytes(serializedMessage));
    }

    private void ReceiveLoop()
    {
        try
        {
            while (_isReceiving)
            {
                int bytesRead = _stream.Read(_receiveBuffer);

                if (bytesRead == 0)
                {
                    break;
                }

                string serializedMessage = Encoding.UTF8.GetString(_receiveBuffer, 0, bytesRead);

                Message message = MessageParser.DeserializeClientMessage(serializedMessage);

                if (message.GetMessageType() == MessageType.ServerError)
                {
                    _logger.Log(LogType.Error, $"Error from server: {message.GetMessageText()}");
                    break;
                }

                if (message.GetMessageType() == MessageType.SystemInfo && !_isRegistered)
                {
                    _isRegistered = true;
                }
                
                if (message.GetMessageType() == MessageType.SessionConnectResponse)
                {
                    _connectedToUsername = message.GetReceiverName();
                    _isInSession = true;
                    _logger.Log(LogType.Info, $"Peer session! Connected with user: {_connectedToUsername}");
                    continue;
                }

                _logger.Log(LogType.Info, $"Message from [{message.GetSenderName()}]: {message.GetMessageText()}");
            }
        }
        catch (Exception e)
        {
            _logger.Log(LogType.Error, $"Error in ReceiveLoop(): {e.Message}");
        }
        finally
        {
            Disconnect();
        }
    }

    private void SendLoop()
    {
        while (!_isRegistered) 
        {
            Thread.Sleep(50);
        }
        
        if (!_isInSession)
        {
            ConnectToUser();
        }
    
        while (!_isInSession)
        {
            Thread.Sleep(50);
        }
        
        try
        {
            while (_isSending)
            {
                Console.WriteLine("\nEnter message: ");

                string messageText = Console.ReadLine();
                
                if (!_isSending) break;
                
                if (string.IsNullOrWhiteSpace(messageText)) continue;

                SendMessage(MessageFactory.CreateUserMessage(_username, _connectedToUsername, messageText));
            }
        }
        catch (Exception e)
        {
            _logger.Log(LogType.Error, $"Error in SendLoop(): {e.Message}");
        }
        finally
        {
            Disconnect();
        }
    }

    private void Disconnect()
    {
        if (!_isConnected) return;
        _isConnected = false;
        
        _isReceiving = false;
        _isSending = false;
        
        _stream.Close();
        _tcpClient.Close();
        
        _logger.Log(LogType.Info, $"Disconnected from server!");
    }
}