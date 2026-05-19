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
    
    private bool _isReceiving = true;
    private bool _isSending = true;
    
    private string _username;
    
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
        _logger.Log(LogType.Info, $"Enter username: ");

        _username = Console.ReadLine();
        
        SendMessage(MessageFactory.CreateRegisterMessage(_username));
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
        try
        {
            while (_isSending)
            {
                _logger.Log(LogType.Info, $"Enter message to broadcast: ");

                string messageText = Console.ReadLine();
                
                if (!_isSending) break;
                
                if (string.IsNullOrWhiteSpace(messageText)) continue;

                SendMessage(MessageFactory.CreateBroadcast(_username, messageText));
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