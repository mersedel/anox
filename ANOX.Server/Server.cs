using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using ANOX.Core;
using ANOX.Logger;

namespace ANOX.Server;

class Server
{
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--hash-password")
        {
            return PrintPasswordHash();
        }

        string configPath = Path.Combine(AppContext.BaseDirectory, "server.json");
        int configIndex = Array.IndexOf(args, "--config");
        if (configIndex >= 0 && configIndex + 1 < args.Length)
        {
            configPath = Path.GetFullPath(args[configIndex + 1]);
        }

        ILogger consoleLogger = new ConsoleLogger();
        ServerConfig config;
        bool configCreated;

        try
        {
            config = ServerConfig.LoadOrCreate(configPath, out configCreated);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            consoleLogger.Log(LogType.Error, $"Config error: {e.Message}");
            return 1;
        }

        ILogger logger = string.IsNullOrWhiteSpace(config.LogFilePath)
            ? consoleLogger
            : new CombinedLogger(consoleLogger, new FileLogger(config.ResolvePath(config.LogFilePath)));

        string version = ProtocolInfo.GetAppVersion(Assembly.GetExecutingAssembly());
        logger.Log(LogType.Info, $"ANOX Server v{version} (protocol {ProtocolInfo.Version})");
        logger.Log(LogType.Info, configCreated
            ? $"Created default config: {configPath}"
            : $"Loaded config: {configPath}");

        if (string.IsNullOrEmpty(config.AdminPasswordHash))
        {
            logger.Log(LogType.Warning, "Admin login is disabled. Generate a hash with \"Server --hash-password\" and put it into AdminPasswordHash.");
        }
        else if (!PasswordHasher.IsValidHash(config.AdminPasswordHash))
        {
            logger.Log(LogType.Warning, "AdminPasswordHash has an unknown format, admin login will always fail.");
        }

        InviteManager.Configure(config);
        CommandManager.Configure(config, logger);
        InviteManager.Expired += Client.OnInviteExpired;

        TcpListener tcpListener = new TcpListener(IPAddress.Parse(config.Host), config.Port);

        try
        {
            tcpListener.Start();
        }
        catch (SocketException e)
        {
            logger.Log(LogType.Error, $"Can't listen on {config.Host}:{config.Port}: {e.Message}");
            return 1;
        }

        logger.Log(LogType.Info, $"Server started on {config.Host}:{config.Port}");
        logger.Log(LogType.Info, "Waiting for clients...");

        while (true)
        {
            TcpClient tcpClient = tcpListener.AcceptTcpClient();
            logger.Log(LogType.Info, $"Client connected from {tcpClient.Client.RemoteEndPoint}");

            Client client = new Client(tcpClient, logger, config);

            client.Start();
        }
    }

    /// <summary>Asks for a password without echoing it and prints its hash for server.json.</summary>
    private static int PrintPasswordHash()
    {
        Console.Write("Admin password: ");
        string password = ReadHidden();
        Console.Write("Repeat password: ");
        string repeat = ReadHidden();

        if (password.Length == 0 || password != repeat)
        {
            Console.WriteLine("Passwords are empty or don't match.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Put this into AdminPasswordHash in server.json:");
        Console.WriteLine(PasswordHasher.Hash(password));
        return 0;
    }

    private static string ReadHidden()
    {
        // Piped input (scripts): ReadKey isn't available, read a plain line.
        if (Console.IsInputRedirected) return Console.ReadLine() ?? string.Empty;

        StringBuilder input = new StringBuilder();

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter) break;

            if (key.Key == ConsoleKey.Backspace)
            {
                if (input.Length > 0) input.Length--;
                continue;
            }

            if (!char.IsControl(key.KeyChar)) input.Append(key.KeyChar);
        }

        Console.WriteLine();
        return input.ToString();
    }
}
