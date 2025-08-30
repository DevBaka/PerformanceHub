using System;
using System.IO;
using DJWinOptimizer.Core.Interfaces;
using DJWinOptimizer.Utils;

namespace DJWinOptimizer.Services
{
    public class FileLogger : ILogger
    {
        private readonly string _logFile;
        private readonly object _lock = new();

        public FileLogger()
        {
            var name = $"log_{DateTime.Now:yyyyMMdd}.txt";
            try
            {
                Directory.CreateDirectory(PortablePaths.LogsDir);
            }
            catch { }
            _logFile = Path.Combine(PortablePaths.LogsDir, name);
        }

        public void Info(string message) => Write("INFO", message);
        public void Warn(string message) => Write("WARN", message);
        public void Error(string message, Exception? ex = null) => Write("ERROR", message + (ex != null ? $" :: {ex}" : string.Empty));

        private void Write(string level, string message)
        {
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logFile, $"[{DateTime.Now:HH:mm:ss}] {level} {message}{Environment.NewLine}");
                }
                catch { /* never crash on logging */ }
            }
        }
    }
}
