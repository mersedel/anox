using System;

namespace ANOX.Client;

class Program
{
    /// <summary>
    /// Server address, first match wins:
    /// 1. arguments: AnoxClient &lt;host&gt; [port]  or  AnoxClient &lt;host:port&gt;
    /// 2. client.json (saved after the first successful prompt)
    /// 3. asked at start
    /// </summary>
    static void Main(string[] args)
    {
        ClientConfig config = ClientConfig.Load(ClientConfig.DefaultPath);

        string host;
        int port;

        if (args.Length > 0)
        {
            if (!ClientConfig.TryParseAddress(args[0], out host, out port))
            {
                Console.WriteLine($"Invalid server address: {args[0]}");
                return;
            }

            if (args.Length > 1 && (!int.TryParse(args[1], out port) || port is < 1 or > 65535))
            {
                Console.WriteLine($"Invalid port: {args[1]}");
                return;
            }
        }
        else if (!string.IsNullOrWhiteSpace(config.Host))
        {
            host = config.Host;
            port = config.Port;
        }
        else
        {
            (host, port) = AskAddress();

            config.Host = host;
            config.Port = port;
            config.Save(ClientConfig.DefaultPath);
        }

        Chat chat = new Chat(host, port);
        chat.Start();
    }

    private static (string Host, int Port) AskAddress()
    {
        while (true)
        {
            Console.WriteLine($"Server address (host or host:port, default port {ClientConfig.DefaultPort}): ");
            string? input = Console.ReadLine();

            if (input == null) Environment.Exit(0);

            if (ClientConfig.TryParseAddress(input, out string host, out int port)) return (host, port);

            Console.WriteLine("Invalid address.");
        }
    }
}
