using System;
using System.IO;
using System.Text;

namespace ANOX.Logger;

public class FileLogger : ILogger
{
    private readonly string _filePath;
    private readonly object _lock = new();

    public FileLogger(string filePath)
    {
        _filePath = filePath;
        
        if (!File.Exists(_filePath))
        {
            using (File.Create(_filePath)) { }
        }
    }

    public void Log(LogType logType, string message)
    {
        string formatted = $"[{DateTime.Now:HH:mm:ss}][{logType}] {message}{Environment.NewLine}";
        
        lock (_lock)
        {
            File.AppendAllText(_filePath, formatted, Encoding.UTF8);
        }
    }
}