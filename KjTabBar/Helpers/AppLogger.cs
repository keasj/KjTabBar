using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KjTabBar.Helpers
{
    internal static class AppLogger
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, DateTime> ThrottledLogTimes = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        public static void LogInfo(string source, string message)
        {
            Write("INFO", source, message, null);
        }

        public static void LogError(string source, string message, Exception exception)
        {
            Write("ERROR", source, message, exception);
        }

        public static void LogErrorThrottled(string source, string key, string message, Exception exception, TimeSpan interval)
        {
            if (string.IsNullOrEmpty(key))
            {
                Write("ERROR", source, message, exception);
                return;
            }

            lock (SyncRoot)
            {
                DateTime lastLoggedUtc;
                if (ThrottledLogTimes.TryGetValue(key, out lastLoggedUtc))
                {
                    if ((DateTime.UtcNow - lastLoggedUtc) < interval)
                    {
                        return;
                    }
                }

                ThrottledLogTimes[key] = DateTime.UtcNow;
            }

            Write("ERROR", source, message, exception);
        }

        private static void Write(string level, string source, string message, Exception exception)
        {
            try
            {
                string logDirectory = GetLogDirectory();
                if (string.IsNullOrEmpty(logDirectory))
                {
                    return;
                }

                Directory.CreateDirectory(logDirectory);
                string logPath = Path.Combine(logDirectory, "KjTabBar.log");
                StringBuilder builder = new StringBuilder();
                builder.Append(DateTime.UtcNow.ToString("o"));
                builder.Append(" [");
                builder.Append(level);
                builder.Append("] ");
                builder.Append(source ?? "Unknown");
                builder.Append(": ");
                builder.Append(message ?? string.Empty);

                if (exception != null)
                {
                    builder.Append(" | ");
                    builder.Append(exception.GetType().FullName);
                    builder.Append(": ");
                    builder.Append(exception.Message);
                    builder.AppendLine();
                    builder.Append(exception.StackTrace ?? string.Empty);
                }

                lock (SyncRoot)
                {
                    File.AppendAllText(logPath, builder.ToString() + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        private static string GetLogDirectory()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(appData))
                {
                    return Path.Combine(appData, "KjTabBar", "Logs");
                }
            }
            catch
            {
            }

            try
            {
                string tempPath = Path.GetTempPath();
                if (!string.IsNullOrEmpty(tempPath))
                {
                    return Path.Combine(tempPath, "KjTabBar", "Logs");
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
