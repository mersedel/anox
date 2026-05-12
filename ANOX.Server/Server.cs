using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ANOX.Core;
using ANOX.Logger;

namespace ANOX.Server;

class Server
{
    static void Main(string[] args)
    {
        bool isListening = true;
        
        IPAddress ipAddress = IPAddress.Parse("192.168.0.55");
        int port = 5656;
        
        TcpListener tcpListener = new TcpListener(ipAddress, port);
        tcpListener.Start();
        Console.WriteLine("Server started! Waiting for connections...");

        while (isListening)
        {
            TcpClient tcpClient = tcpListener.AcceptTcpClient();

            ClientSession clientSession = new ClientSession(tcpClient);
            
            clientSession.Start();
        }
    }
}