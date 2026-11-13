using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace KjTabBar.Helpers
{
    internal static class AppLogger
    {
        private static readonly object SyncRoot = new object();
        private static readonly object PendingLogSync = new object();
        private static long _droppedLogEntries;
        internal const int MaxPendingLogEntries = 256;
        internal const int MaxPendingLogEntryChars = 8192;
        private static readonly Dictionary<string, DateTime> ThrottledLogTimes = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly Regex WindowsPathRegex = new Regex("(?i)(?:[a-z]:\\\\|\\\\\\\\)[^\\r\\n\\\"'<>|]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex SidRegex = new Regex(@"\bS-\d-(?:\d+-){1,14}\d+\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private sealed class PendingLogEntry
        {
            internal string Text;
            internal string Directory;
        }

        private static readonly ConcurrentQueue<PendingLogEntry> PendingLogEntries = new ConcurrentQueue<PendingLogEntry>();
        private static string _logDirectoryOverrideForTests;
        private static readonly AutoResetEvent PendingLogSignal = new AutoResetEvent(false);
        private static readonly Thread LogWriterThread = new Thread(ProcessPendingLogEntries);
        private const int MaxLoggedTextLength = 2048;
        private const long MaxLogFileBytes = 1024L * 1024L;
        private const int MaxLogArchiveFiles = 3;
        internal static void SetLogDirectoryOverrideForTests(string directory)
        {
            lock (SyncRoot) _logDirectoryOverrideForTests = directory;
        }

        internal static void ResetThrottledLogTimesForTests()
        {
            lock (SyncRoot) ThrottledLogTimes.Clear();
        }

        static AppLogger()
        {
            LogWriterThread.IsBackground = true;
            LogWriterThread.Name = "KjTabBar_LogWriter";
            LogWriterThread.Start();
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogInfo(string source, string message)
        {
#if DEBUG
            Write("INFO", source, message, null);
#endif
        }

        // Enable only for a diagnostic run; keep normal Release logging unchanged.
        private static readonly bool DiagnosticsEnabled =
            string.Equals(Environment.GetEnvironmentVariable("KJTB_DIAGNOSTICS"), "1", StringComparison.Ordinal);

        internal static System.Diagnostics.Stopwatch StartDiagnosticTiming()
        {
            return DiagnosticsEnabled ? System.Diagnostics.Stopwatch.StartNew() : null;
        }

        internal static void LogDiagnosticTiming(string stage, IntPtr hwnd, System.Diagnostics.Stopwatch timer)
        {
            if (timer == null) return;
            LogDiagnostic("ReopenTiming", string.Format(CultureInfo.InvariantCulture,
                "stage={0} hwnd={1} elapsedMs={2:0.0}", stage, hwnd, timer.Elapsed.TotalMilliseconds));
        }

        internal static void LogDiagnosticPlacement(string stage, IntPtr hwnd)
        {
            if (!DiagnosticsEnabled) return;
            NativeMethods.WINDOWPLACEMENT placement = new NativeMethods.WINDOWPLACEMENT();
            placement.length = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.WINDOWPLACEMENT));
            bool hasPlacement = NativeMethods.GetWindowPlacement(hwnd, ref placement);
            NativeMethods.RECT rect = placement.rcNormalPosition;
            NativeMethods.RECT currentRect;
            bool hasRect = NativeMethods.GetWindowRect(hwnd, out currentRect);
            LogDiagnostic("WindowPlacement", string.Format(CultureInfo.InvariantCulture,
                "stage={0} hwnd={1} alive={2} visible={3} iconic={4} hasPlacement={5} show={6} normal={7},{8},{9},{10} hasRect={11} rect={12},{13},{14},{15}",
                stage, hwnd, NativeMethods.IsWindow(hwnd), NativeMethods.IsWindowVisible(hwnd), NativeMethods.IsIconic(hwnd),
                hasPlacement, placement.showCmd, rect.Left, rect.Top, rect.Right, rect.Bottom,
                hasRect, currentRect.Left, currentRect.Top, currentRect.Right, currentRect.Bottom));
        }

        public static void LogDiagnostic(string source, string message)
        {
            if (DiagnosticsEnabled) Write("DIAG", source, message, null);
#if DEBUG
            else Write("INFO", source, message, null);
#endif
        }

        public static void LogError(string source, string message, Exception exception)
        {
            Write("ERROR", source, message, exception);
        }

        public static void LogErrorThrottled(string source, string key, string message, Exception exception, TimeSpan interval)
        {
            WriteThrottled("ERROR", source, key, message, exception, interval);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogSlowOperation(string source, string key, string operation, TimeSpan elapsed, TimeSpan threshold, TimeSpan interval)
        {
#if DEBUG
            if (elapsed < threshold)
            {
                return;
            }

            string message = string.Format(
                CultureInfo.InvariantCulture,
                "{0} took {1:0} ms.",
                string.IsNullOrEmpty(operation) ? "Operation" : operation,
                elapsed.TotalMilliseconds);
            WriteThrottled("INFO", source, key, message, null, interval);
#endif
        }

        public static void Flush()
        {
            try
            {
                FlushPendingLogEntries();
            }
            catch
            {
            }
        }

        internal static string SanitizeForLog(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            try
            {
                // Mask complete paths before replacing known roots, or their descendants leak.
                string sanitized = SidRegex.Replace(text, "<sid>");
                sanitized = WindowsPathRegex.Replace(sanitized, "<path>");
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
                return "<log-text-unavailable>";
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

        internal static bool ShouldRotateLogFile(long currentFileBytes, int pendingByteCount, long maxLogFileBytes)
        {
            if (currentFileBytes <= 0 || maxLogFileBytes <= 0)
            {
                return false;
            }

            long pendingBytes = pendingByteCount > 0 ? pendingByteCount : 0;
            if (pendingBytes >= maxLogFileBytes)
            {
                return true;
            }

            return currentFileBytes > maxLogFileBytes - pendingBytes;
        }

        private static void WriteThrottled(string level, string source, string key, string message, Exception exception, TimeSpan interval)
        {
            if (string.IsNullOrEmpty(key))
            {
                Write(level, source, message, exception);
                return;
            }

            string testLogDirectory;
            lock (SyncRoot) testLogDirectory = _logDirectoryOverrideForTests;
            if (!string.IsNullOrEmpty(testLogDirectory))
            {
                Write(level, source, message, exception);
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

            Write(level, source, message, exception);
        }
        private static void Write(string level, string source, string message, Exception exception)
        {
            try
            {
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
                    Queue<Exception> causes = new Queue<Exception>();
                    causes.Enqueue(exception);
                    while (causes.Count > 0 && builder.Length < MaxPendingLogEntryChars)
                    {
                        Exception cause = causes.Dequeue();
                        builder.Append(" | ");
                        builder.Append(cause.GetType().FullName);
                        builder.Append(": ");
                        builder.Append(SanitizeForLog(cause.Message));

                        string stackTrace = SanitizeForLog(cause.StackTrace ?? string.Empty);
                        if (!string.IsNullOrEmpty(stackTrace))
                        {
                            builder.AppendLine();
                            builder.Append(stackTrace);
                        }
                        AggregateException aggregate = cause as AggregateException;
                        if (aggregate != null)
                        {
                            foreach (Exception inner in aggregate.InnerExceptions) causes.Enqueue(inner);
                        }
                        else if (cause.InnerException != null) causes.Enqueue(cause.InnerException);
                    }
                }

                string testLogDirectory;
                lock (SyncRoot) testLogDirectory = _logDirectoryOverrideForTests;
                if (!string.IsNullOrEmpty(testLogDirectory))
                {
                    lock (SyncRoot)
                    {
                        Directory.CreateDirectory(testLogDirectory);
                        File.AppendAllText(Path.Combine(testLogDirectory, "KjTabBar.log"), builder.ToString() + Environment.NewLine, Encoding.UTF8);
                    }
                    return;
                }
                lock (PendingLogSync)
                {
                    if (PendingLogEntries.Count < MaxPendingLogEntries)
                    {
                        PendingLogEntries.Enqueue(new PendingLogEntry
                        {
                            Text = builder.ToString() + Environment.NewLine,
                            Directory = testLogDirectory
                        });
                        PendingLogSignal.Set();
                    }
                    else if (_droppedLogEntries < long.MaxValue)
                    {
                        _droppedLogEntries++;
                    }
                }
            }
            catch
            {
            }
        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            try
            {
                FlushPendingLogEntries();
            }
            catch
            {
            }
        }

        private static void ProcessPendingLogEntries()
        {
            while (true)
            {
                try
                {
                    PendingLogSignal.WaitOne(TimeSpan.FromSeconds(5));
                    FlushPendingLogEntries();
                }
                catch
                {
                }
            }
        }

        internal static bool EnqueueLogEntry(ConcurrentQueue<string> entries, string entry, ref long dropped)
        {
            if (entry.Length > MaxPendingLogEntryChars)
                entry = entry.Substring(0, MaxPendingLogEntryChars - 5) + "...\r\n";
            lock (PendingLogSync)
            {
                if (entries.Count >= MaxPendingLogEntries)
                {
                    if (dropped < long.MaxValue) dropped++;
                    return false;
                }
                entries.Enqueue(entry);
                return true;
            }
        }

        private static void FlushPendingLogEntries()
        {
            if (PendingLogEntries.IsEmpty && Interlocked.Read(ref _droppedLogEntries) == 0) return;
            lock (SyncRoot)
            {
                PendingLogEntry[] batch;
                long droppedBatch;
                lock (PendingLogSync)
                {
                    batch = PendingLogEntries.ToArray();
                    droppedBatch = _droppedLogEntries;
                    if (batch.Length == 0 && droppedBatch == 0) return;
                    string defaultDirectory = GetDefaultLogDirectory();
                    Dictionary<string, StringBuilder> grouped = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
                    foreach (PendingLogEntry entry in batch)
                    {
                        string directory = string.IsNullOrEmpty(entry.Directory) ? defaultDirectory : entry.Directory;
                        if (string.IsNullOrEmpty(directory)) continue;
                        StringBuilder text;
                        if (!grouped.TryGetValue(directory, out text)) grouped[directory] = text = new StringBuilder();
                        text.Append(entry.Text);
                    }
                    if (grouped.Count == 0) return;
                    foreach (KeyValuePair<string, StringBuilder> group in grouped)
                    {
                        string text = group.Value.ToString();
                        if (droppedBatch > 0) text += DateTime.UtcNow.ToString("o") + " [WARN] AppLogger: Dropped " + droppedBatch.ToString(CultureInfo.InvariantCulture) + " log entries because the pending log limit was reached." + Environment.NewLine;
                        RotateLogFileIfNeeded(Path.Combine(group.Key, "KjTabBar.log"), Encoding.UTF8.GetByteCount(text));
                        Directory.CreateDirectory(group.Key);
                        File.AppendAllText(Path.Combine(group.Key, "KjTabBar.log"), text, Encoding.UTF8);
                    }
                    for (int i = 0; i < batch.Length; i++) { PendingLogEntry ignored; PendingLogEntries.TryDequeue(out ignored); }
                    _droppedLogEntries = 0;
                }
            }
        }

        internal static void FlushPendingLogEntries(ConcurrentQueue<string> entries, string logPath)
        {
            long dropped = 0;
            FlushPendingLogEntries(entries, logPath, ref dropped);
        }

        internal static void FlushPendingLogEntries(ConcurrentQueue<string> entries, string logPath, ref long dropped)
        {
            lock (SyncRoot)
            {
                string[] batch;
                long droppedBatch;
                lock (PendingLogSync)
                {
                    batch = entries.ToArray();
                    droppedBatch = dropped;
                }
                if (batch.Length == 0 && droppedBatch == 0) return;
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                string batchText = string.Concat(batch);
                if (droppedBatch > 0)
                    batchText += DateTime.UtcNow.ToString("o") + " [WARN] AppLogger: Dropped " +
                        droppedBatch.ToString(CultureInfo.InvariantCulture) + " log entries because the pending log limit was reached." + Environment.NewLine;
                RotateLogFileIfNeeded(logPath, Encoding.UTF8.GetByteCount(batchText));
                File.AppendAllText(logPath, batchText, Encoding.UTF8);
                // Commit removal only after the write succeeds. A failure leaves the batch for retry.
                lock (PendingLogSync)
                {
                    string entry;
                    for (int i = 0; i < batch.Length; i++) entries.TryDequeue(out entry);
                    dropped -= droppedBatch;
                }
            }
        }

        private static void RotateLogFileIfNeeded(string logPath, int pendingByteCount)
        {
            try
            {
                if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
                {
                    return;
                }

                FileInfo fileInfo = new FileInfo(logPath);
                if (!ShouldRotateLogFile(fileInfo.Length, pendingByteCount, MaxLogFileBytes))
                {
                    return;
                }

                for (int i = MaxLogArchiveFiles; i >= 1; i--)
                {
                    string archivePath = GetArchiveLogPath(logPath, i);
                    if (!File.Exists(archivePath))
                    {
                        continue;
                    }

                    if (i == MaxLogArchiveFiles)
                    {
                        File.Delete(archivePath);
                    }
                    else
                    {
                        string nextArchivePath = GetArchiveLogPath(logPath, i + 1);
                        if (File.Exists(nextArchivePath))
                        {
                            File.Delete(nextArchivePath);
                        }
                        File.Move(archivePath, nextArchivePath);
                    }
                }

                string firstArchivePath = GetArchiveLogPath(logPath, 1);
                if (File.Exists(firstArchivePath))
                {
                    File.Delete(firstArchivePath);
                }

                File.Move(logPath, firstArchivePath);
            }
            catch
            {
            }
        }

        private static string GetArchiveLogPath(string logPath, int index)
        {
            return logPath + "." + index.ToString(CultureInfo.InvariantCulture);
        }

        private static string GetLogDirectory()
        {
            string testLogDirectory;
            lock (SyncRoot) testLogDirectory = _logDirectoryOverrideForTests;
            if (!string.IsNullOrEmpty(testLogDirectory)) return testLogDirectory;
            return GetDefaultLogDirectory();
        }

        private static string GetDefaultLogDirectory()
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
