using ProfileDeck.Core.Persistence;

namespace ProfileDeck.Core.Logging;

public sealed class FileLogSink : ILogSink
{
    private readonly string _logFile;
    private readonly object _lock = new();

    public event Action<LogEntry>? EntryLogged;

    public FileLogSink()
    {
        Directory.CreateDirectory(AppPaths.LogsDir);
        _logFile = Path.Combine(AppPaths.LogsDir, $"log_{DateTime.Now:yyyyMMdd}.txt");
    }

    public void Info(string message) => Write(LogLevel.Info, message);
    public void Warn(string message) => Write(LogLevel.Warn, message);
    public void Error(string message, Exception? ex = null) => Write(LogLevel.Error, ex != null ? $"{message} :: {ex}" : message);

    private void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.UtcNow, level, message);
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFile, $"[{entry.TimestampUtc:HH:mm:ss}] {level.ToString().ToUpperInvariant()} {message}{Environment.NewLine}");
            }
            catch { /* logging must never crash the app */ }
        }
        EntryLogged?.Invoke(entry);
    }
}
