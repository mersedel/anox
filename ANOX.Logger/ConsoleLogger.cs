using System;

namespace ANOX.Logger;

public class ConsoleLogger : ILogger
{
    private readonly object _lock = new();

    public void Log(LogType logType, string message)
    {
        string formatted =
            $"[{DateTime.Now:HH:mm:ss}][{logType}] {message}";

        lock (_lock)
        {
            try
            {
                Console.WriteLine(formatted);
            }
            catch
            {
                // In a console logger, it's usually enough to just ignore
                // because the output to the console almost never crashes,
                // but try-catch makes the behavior stable.
            }
        }
    }
}