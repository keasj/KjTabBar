using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace KjTabBar.Helpers
{
    internal static class AppLogger
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, DateTime> ThrottledLogTimes = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly Regex WindowsPathRegex = new Regex("(?i)(?:[a-z]:\\\\|\\\\\\\\)[^\\r\\n\\\"'<>|]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex SidRegex = new Regex(@"\bS-\d-(?:\d+-){1,14}\d+\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private const int MaxLoggedTextLength = 2048;

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

        internal static string SanitizeForLog(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            try
            {
                string sanitized = text;
                sanitized = ReplaceKnownPath(sanitized, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "<user-profile>");
                sanitized = ReplaceKnownPath(sanitized, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "<appdata>");
                sanitized = ReplaceKnownPath(sanitized, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "<localappdata>");
                sanitized = ReplaceKnownPath(sanitized, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "<desktop>");
                sanitized = ReplaceKnownPath(sanitized, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "<common-desktop>");
                sanitized = ReplaceKnownPath(sanitized, Path.GetTempPath(), "<temp>");
                sanitized = SidRegex.Replace(sanitized, "<sid>");
                sanitized = WindowsPathRegex.Replace(sanitized, "<path>");

                if (sanitized.Length > MaxLoggedTextLength)
                {
                    sanitized = sanitized.Substring(0, MaxLoggedTextLength) + "...";
                }

                return sanitized;
            }
            catch
            {
                if (text.Length > MaxLoggedTextLength)
                {
                    return text.Substring(0, MaxLoggedTextLength) + "...";
                }

                return text;
            }
        }

        private static string ReplaceKnownPath(string text, string knownPath, string replacement)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(knownPath))
            {
                return text;
            }

            string trimmedPath = knownPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(trimmedPath))
            {
                return text;
            }

            return Regex.Replace(text, Regex.Escape(trimmedPath), replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
                builder.Append(SanitizeForLog(message ?? string.Empty));

                if (exception != null)
                {
                    builder.Append(" | ");
                    builder.Append(exception.GetType().FullName);
                    builder.Append(": ");
                    builder.Append(SanitizeForLog(exception.Message));

                    string stackTrace = SanitizeForLog(exception.StackTrace ?? string.Empty);
                    if (!string.IsNullOrEmpty(stackTrace))
                    {
                        builder.AppendLine();
                        builder.Append(stackTrace);
                    }
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