using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ANOX.Server;

public class ClientSession
{
    private byte[] _receiveBuffer = new byte[1024];
    
    private TcpClient _tcpClient;
    private NetworkStream _stream;
    
    private bool _isReceiving = true;

    private string _clientUsername;
    
    public ClientSession(TcpClient tcpClient)
    {
        _tcpClient = tcpClient;
        _stream = tcpClient.GetStream();
    }

    public void Start()
    {
        Thread thread = new Thread(Receive);

        thread.IsBackground = true;
        
        thread.Start();
    }

    private void Receive()
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
            }
        }
        catch (Exception e)
        {

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
    }
}