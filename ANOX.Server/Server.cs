using System;
using System.Text;
using ANOX.Core;

namespace ANOX.Server;

class Server
{
    static void Main(string[] args)
    {
        Console.ReadLine();
    }
}


// HOW TO SERIALIZE MESSAGE !

/*

Message msg = new Message(MessageType.UserMessage,
    "mersedel",
    "sztucer",
    "Hello sztucer!",
    DateTime.Now.ToString("HH:mm:ss"));

Console.WriteLine(MessageParser.SerializeMessage(msg));*/