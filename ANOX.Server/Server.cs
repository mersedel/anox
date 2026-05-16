using System;
using System.Net;
using System.Net.Sockets;
using ANOX.Logger;

namespace ANOX.Server;

class Server
{
    static void Main(string[] args)
    {
        ILogger logger = new ConsoleLogger();
        
        bool isListening = true;
        
        IPAddress ipAddress = IPAddress.Parse("192.168.0.55");
        int port = 5656;
        
        TcpListener tcpListener = new TcpListener(ipAddress, port);
        tcpListener.Start();
        logger.Log(LogType.Info, "Server started succesfully!");

        while (isListening)
        {
            TcpClient tcpClient = tcpListener.AcceptTcpClient();
            logger.Log(LogType.Info, "Client connected!");

            ClientSession clientSession = new ClientSession(tcpClient, logger);
            
            clientSession.Start();
        }
    }
}