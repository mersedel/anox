using System;
using System.IO;
using System.Text;

namespace ANOX.Logger;

public class FileLogger : ILogger, IDisposable
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly StreamWriter _writer;

    public FileLogger(string filePath)
    {
        _filePath = filePath;
        
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        
        _writer = new StreamWriter(_filePath, true, Encoding.UTF8)
        {
            AutoFlush = true
        };
    }

    public void Log(LogType logType, string message)
    {
        string formatted =
            $"[{DateTime.Now:HH:mm:ss}][{logType}] {message}{Environment.NewLine}";

        lock (_lock)
        {
            try
            {
                _writer.Write(formatted);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[LOGGER ERROR] Failed to write log: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer.Dispose();
        }
    }
}