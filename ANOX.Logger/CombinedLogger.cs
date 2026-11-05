using System;
using System.Collections.Generic;

namespace ANOX.Logger;

public class CombinedLogger : ILogger
{
    private readonly List<ILogger> _loggers = new();
    private readonly object _lock = new();

    public CombinedLogger(params ILogger[] loggers)
    {
        foreach (var logger in loggers)
        {
            if (logger != null)
                _loggers.Add(logger);
        }
    }

    public void Log(LogType logType, string message)
    {
        lock (_lock)
        {
            foreach (var logger in _loggers)
            {
                try
                {
                    logger.Log(logType, message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[LOGGER ERROR] Failed to write log in {logger.GetType().Name}: {ex.Message}");
                }
            }
        }
    }
}