using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

        ILogger logger = consoleLogger;

        if (!string.IsNullOrWhiteSpace(config.LogFilePath))
        {
            try
            {
                FileLogger fileLogger = new FileLogger(config.ResolvePath(config.LogFilePath));

                // Flush and close the file on normal exit / Ctrl+C.
                AppDomain.CurrentDomain.ProcessExit += (_, _) => fileLogger.Dispose();

                logger = new CombinedLogger(consoleLogger, fileLogger);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // E.g. the file is locked by another server instance: keep running with console logging.
                consoleLogger.Log(LogType.Warning, $"Can't open log file, logging to console only: {e.Message}");
            }
        }

        string version = ProtocolInfo.GetAppVersion(Assembly.GetExecutingAssembly());
        logger.Log(LogType.Info, $"ANOX Server v{version} (protocol {ProtocolInfo.Version})");
        logger.Log(LogType.Info, configCreated
            ? $"Created default config: {configPath}"
            : $"Loaded config: {configPath}");

        if (string.IsNullOrEmpty(config.AdminPasswordHash))
        {
            logger.Log(LogType.Warning, "Admin login is disabled. Generate a hash with \"AnoxServer --hash-password\" and put it into AdminPasswordHash.");
        }
        else if (!PasswordHasher.IsValidHash(config.AdminPasswordHash))
        {
            logger.Log(LogType.Warning, "AdminPasswordHash has an unknown format, admin login will always fail.");
        }

        InviteManager.Configure(config);
        CommandManager.Configure(config, logger);
        ConnectionLimiter.Configure(config);
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
            TcpClient tcpClient;

            try
            {
                tcpClient = tcpListener.AcceptTcpClient();
            }
            catch (SocketException e)
            {
                // E.g. a connection reset before accept, or out of sockets: keep serving the others.
                logger.Log(LogType.Warning, $"Accept failed: {e.Message}");
                Thread.Sleep(100); // don't spin if the error repeats
                continue;
            }

            AcceptClient(tcpClient, logger, config);
        }
    }

    private static DateTime _lastRejectLog = DateTime.MinValue;
    private static int _rejectedSinceLog;

    /// <summary>
    /// Sets up one connection. Nothing thrown here may reach the accept loop: one broken socket
    /// (e.g. reset right after accept) would otherwise crash the whole server.
    /// </summary>
    private static void AcceptClient(TcpClient tcpClient, ILogger logger, ServerConfig config)
    {
        Client? client = null;
        string? addressKey = null;

        try
        {
            IPAddress? address = (tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address;
            string key = ConnectionLimiter.GetAddressKey(address);

            if (!ConnectionLimiter.TryAcquire(key, out MessageErrorType rejection))
            {
                _ = RejectAsync(tcpClient, rejection);
                LogRejected(logger, key);
                return;
            }

            addressKey = key;
            logger.Log(LogType.Info, $"Client connected from {address}");

            client = new Client(tcpClient, key, logger, config);
            client.Start();
        }
        catch (Exception e)
        {
            logger.Log(LogType.Warning, $"Failed to set up a connection: {e.Message}");

            if (client != null)
            {
                client.Close(); // also releases the limiter slot
            }
            else
            {
                tcpClient.Close();
                if (addressKey != null) ConnectionLimiter.Release(addressKey);
            }
        }
    }

    /// <summary>
    /// Tells a rejected client why before closing, so it doesn't just see "Disconnected".
    /// Async on purpose: the accept loop must not wait, and under a connection flood
    /// blocking pool threads would stall the server's timers.
    /// </summary>
    private static async Task RejectAsync(TcpClient tcpClient, MessageErrorType reason)
    {
        using (tcpClient)
        {
            try
            {
                Socket socket = tcpClient.Client;
                byte[] line = Encoding.UTF8.GetBytes(
                    MessageParser.SerializeMessage(MessageFactory.CreateErrorMessage(reason, string.Empty)) + "\n");

                // Bounds the whole exchange: a peer that never closes can't hold the socket.
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));

                await socket.SendAsync(line, SocketFlags.None, timeout.Token);
                socket.Shutdown(SocketShutdown.Send);

                // Discard what the client already sent (its Register) until it closes. Closing with
                // unread data sends an RST, and the client could lose the error line before reading it.
                byte[] buffer = new byte[1024];
                while (await socket.ReceiveAsync(buffer, SocketFlags.None, timeout.Token) > 0) { }
            }
            catch (Exception)
            {
                // Best effort: timeout, reset or a peer that's already gone.
            }
        }
    }

    /// <summary>At most one line per 10 s, so a connection flood can't flood the log too.</summary>
    private static void LogRejected(ILogger logger, string addressKey)
    {
        _rejectedSinceLog++;

        DateTime now = DateTime.UtcNow;
        if (now - _lastRejectLog < TimeSpan.FromSeconds(10)) return;

        logger.Log(LogType.Warning, $"Connection limit reached, rejected {_rejectedSinceLog} connection(s) (last from {addressKey})");
        _lastRejectLog = now;
        _rejectedSinceLog = 0;
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
