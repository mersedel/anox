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
    
    private bool _isRegistered;
    
    private byte[] _receiveBuffer = new byte[1024];
    
    private TcpClient _tcpClient;
    private NetworkStream _stream;
    
    private bool _isReceiving = true;

    private string _clientUsername;
    public string Username => _clientUsername;

    public void SetUsername(string username)
    {
        _clientUsername = username;
    }
    
    public ClientSession(TcpClient tcpClient, ILogger logger)
    {
        _tcpClient = tcpClient;
        _stream = tcpClient.GetStream();
        _logger = logger;
    }

    public void Start()
    {
        Thread thread = new Thread(ReceiveLoop);

        thread.IsBackground = true;
        
        thread.Start();
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
            
                string receivedMessage = Encoding.UTF8.GetString(_receiveBuffer, 0, bytesRead);

                if (!_isRegistered)
                {
                    SetUsername(receivedMessage);
                    ClientManager.AddClient(receivedMessage, this);
                    _isRegistered = true;
                    _logger.Log(LogType.Info, $"Client registered with name: [{Username}]");
                }
                else
                {
                    Message message = MessageParser.DeserializeClientMessage(receivedMessage);
                    _logger.Log(LogType.Info,$"Received broadcast message [{message.GetMessageText()}] from: [{Username}]");
                    
                    ClientManager.Broadcast(receivedMessage, Username);
                }
            }
        }
        catch (Exception e)
        {
            _logger.Log(LogType.Error, $"Error in Receive(): {e.Message}");
        }
        finally
        {
            Disconnect();
        }
    }

    public void Send(string message)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            
            _stream.Write(bytes);
        }
        catch (Exception e)
        {
            Disconnect();
        }
    }

    private void Disconnect()
    {
        _isReceiving = false;
        _stream.Close();
        _tcpClient.Close();
        
        if (_isRegistered)
        {
            ClientManager.RemoveClient(Username);
        }
        
        _logger.Log(LogType.Info, $"Client [{Username}] disconnected!");
    }
}