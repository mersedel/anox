using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ANOX.Core;
using ANOX.Logger;

namespace ANOX.Server;

public class ClientSession
{
    private ILogger _logger;

    private bool _isConnected;
    
    private bool _isRegistered;
    
    private byte[] _receiveBuffer = new byte[1024];
    
    private TcpClient _tcpClient;
    private NetworkStream _stream;
    
    private bool _isReceiving = true;

    private string _clientUsername;
    public string Username => _clientUsername;
    
    public ClientSession(TcpClient tcpClient, ILogger logger)
    {
        _tcpClient = tcpClient;
        _stream = tcpClient.GetStream();
        _logger = logger;
    }

    public void Start()
    {
        _isConnected = true;
        
        Thread thread = new Thread(ReceiveLoop);

        thread.IsBackground = true;
        
        thread.Start();
    }

    private void RegisterClient(string username)
    {
        _clientUsername = username;
        
        ClientManager.AddClient(_clientUsername, this);
        _isRegistered = true;
        _logger.Log(LogType.Info, $"Client registered with name [{Username}]");
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
                
                string receivedSerializedMessage = Encoding.UTF8.GetString(_receiveBuffer, 0, bytesRead);
                
                Message message = MessageParser.DeserializeClientMessage(receivedSerializedMessage);

                if (!_isRegistered && message.GetMessageType() == MessageType.Register)
                {
                    if (ClientManager.ContainsClient(message.GetSenderName()))
                    {
                        SendMessage(MessageFactory.CreateErrorMessage(MessageErrorType.NameAlreadyTaken, Username));
                        
                        _logger.Log(LogType.Error, $"Username [{receivedSerializedMessage}] already taken, sending ErrorMessage to client");
                        return;
                    }
                    RegisterClient(message.GetMessageText());
                }
                else
                {
                    _logger.Log(LogType.Info,$"Received broadcast message [{message.GetMessageText()}] from [{Username}]");
                    
                    ClientManager.Broadcast(message, Username); // broadcasting serialized message to receiver
                }
            }
        }
        catch (Exception e)
        {
            _logger.Log(LogType.Error, $"Error in Receive() -> {e.Message}");
        }
        finally
        {
            Disconnect();
        }
    }

    public void SendMessage(Message message)
    {
        try
        {
            string serializedMessage = MessageParser.SerializeMessage(message);
            byte[] bytes = Encoding.UTF8.GetBytes(serializedMessage);
            
            _stream.Write(bytes);
        }
        catch (Exception e)
        {
            Disconnect();
        }
    }

    private void Disconnect()
    {
        if (!_isConnected) return;
        _isConnected = false;
        
        _isReceiving = false;
        _stream.Close();
        _tcpClient.Close();
        
        if (_isRegistered)
        {
            ClientManager.RemoveClient(Username);
        }
        
        _logger.Log(LogType.Info, $"Client [{Username ?? "unregistered"}] disconnected!");
    }
}