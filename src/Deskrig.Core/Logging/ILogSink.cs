namespace Deskrig.Core.Logging;

public enum LogLevel { Info, Warn, Error }

public sealed record LogEntry(DateTime TimestampUtc, LogLevel Level, string Message);

public interface ILogSink
{
    event Action<LogEntry>? EntryLogged;
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}
