namespace ANOX.Logger;

public interface ILogger
{
    public void Log(LogType logType, string message);
}