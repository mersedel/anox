using System;

namespace ANOX.Client;

class Program
{
    private const int DefaultPort = 5656;
    private const string DefaultHost = "anox.ddns.net";

    /// <summary>
    /// Server address, first match wins:
    /// 1. arguments: AnoxClient &lt;host&gt; [port]  or  AnoxClient &lt;host:port&gt;
    /// 2. default address
    /// </summary>
    static void Main(string[] args)
    {
        string host;
        int port;

        if (args.Length > 0)
        {
            if (!TryParseAddress(args[0], out host, out port))
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
        else
        {
            host = DefaultHost;
            port = DefaultPort;
        }

        Chat chat = new Chat(host, port);
        chat.Start();
    }

    /// <summary>Parses "host" or "host:port" (IPv6 as "[::1]:5656").</summary>
    private static bool TryParseAddress(string input, out string host, out int port)
    {
        input = input.Trim();
        host = input;
        port = DefaultPort;

        if (input.Length == 0) return false;

        int colon = input.LastIndexOf(':');
        bool isBracketedIpv6 = input.StartsWith('[');
        bool hasPort = colon > 0 && (isBracketedIpv6 ? input.IndexOf(']') < colon : input.IndexOf(':') == colon);

        if (hasPort)
        {
            if (!int.TryParse(input.AsSpan(colon + 1), out port) || port is < 1 or > 65535) return false;
            host = input.Substring(0, colon);
        }

        host = host.Trim('[', ']');
        return Uri.CheckHostName(host) != UriHostNameType.Unknown;
    }
}
