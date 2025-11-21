using System;

namespace ANOX.Logger;

public class ConsoleLogger : ILogger
{
    public void Log(LogType logType, string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}][{logType}] {message}");
    }
}