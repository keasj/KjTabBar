using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ExceptionLoggingTests
    {
        private static string TestLogDirectory;

        [ClassInitialize]
        public static void InitializeTestLogDirectory(TestContext context)
        {
            AppLogger.ResetThrottledLogTimesForTests();
            EnsureTestLogDirectory();
        }

        [TestInitialize]
        public void ResetTestLogThrottle()
        {
            AppLogger.ResetThrottledLogTimesForTests();
            EnsureTestLogDirectory();
        }

        [ClassCleanup]
        public static void CleanupTestLogDirectory()
        {
            AppLogger.Flush();
            AppLogger.SetLogDirectoryOverrideForTests(null);
            if (!string.IsNullOrEmpty(TestLogDirectory) && Directory.Exists(TestLogDirectory))
                Directory.Delete(TestLogDirectory, true);
        }

        private static void EnsureTestLogDirectory()
        {
            if (string.IsNullOrEmpty(TestLogDirectory))
                TestLogDirectory = Path.Combine(Path.GetTempPath(), "KjtbExceptionTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TestLogDirectory);
            AppLogger.SetLogDirectoryOverrideForTests(TestLogDirectory);
        }

        [TestMethod]
        public void Selection_OtherWindowFailureDoesNotRejectSuccessfulTarget()
        {
            SelectionDocument document = new SelectionDocument { PrecedeWithBrokenWindow = true };
            WithSelectionShell(document, interop => interop.SelectItems((IntPtr)100, SelectionPaths()));
            CollectionAssert.AreEqual(new[] { 29, 1 }, document.Flags);
        }

        public sealed class BrokenWindow
        {
            public string FullName { get { throw new IOException("Unrelated window disappeared."); } }
        }

        [TestMethod]
        public void LogOutage_BoundsEntriesAndSizeAndReportsOverflowAfterRecovery()
        {
            string root = Path.Combine(Path.GetTempPath(), "KjtbLogLimit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "test.log");
            System.Collections.Concurrent.ConcurrentQueue<string> queue = new System.Collections.Concurrent.ConcurrentQueue<string>();
            long dropped = 0;
            string oversized = new string('x', AppLogger.MaxPendingLogEntryChars * 2);
            try
            {
                File.WriteAllText(path, "");
                using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        Parallel.For(0, AppLogger.MaxPendingLogEntries,
                            i => AppLogger.EnqueueLogEntry(queue, oversized, ref dropped));
                        bool failed = false;
                        try { AppLogger.FlushPendingLogEntries(queue, path, ref dropped); }
                        catch (IOException) { failed = true; }
                        Assert.IsTrue(failed);
                        Assert.AreEqual(AppLogger.MaxPendingLogEntries, queue.Count);
                        Assert.AreEqual((long)attempt * AppLogger.MaxPendingLogEntries, dropped);
                        foreach (string entry in queue) Assert.IsTrue(entry.Length <= AppLogger.MaxPendingLogEntryChars);
                    }
                }
                long expectedDropped = dropped;
                AppLogger.FlushPendingLogEntries(queue, path, ref dropped);
                Assert.AreEqual(0, queue.Count);
                Assert.AreEqual(0L, dropped);
                string[] lines = File.ReadAllLines(path);
                Assert.AreEqual(AppLogger.MaxPendingLogEntries + 1, lines.Length);
                StringAssert.Contains(lines[lines.Length - 1], "[WARN] AppLogger: Dropped " + expectedDropped + " log entries");
                Assert.IsTrue(AppLogger.EnqueueLogEntry(queue, "recovered" + Environment.NewLine, ref dropped));
                AppLogger.FlushPendingLogEntries(queue, path, ref dropped);
                string[] recoveredLines = File.ReadAllLines(path);
                Assert.AreEqual("recovered", recoveredLines[recoveredLines.Length - 1]);
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public void PreparationFailures_ReachCallerBeforeSelection()
        {
            foreach (string stage in new[] { "Folder", "Windows", "WindowsCount", "WindowItem", "FullName", "HWND", "Document", "Items", "Count", "Item", "Path", "ParseName" })
            {
                SelectionDocument document = new SelectionDocument { FailureStage = stage };
                DateTime start = DateTime.UtcNow;
                Exception failure = null;
                WithSelectionShell(document, interop =>
                {
                    try { interop.SelectItems((IntPtr)100, SelectionPaths()); }
                    catch (Exception ex) { failure = ex; }
                });
                Assert.IsNotNull(failure, stage + " must not be treated as success.");
                StringAssert.Contains(failure.ToString(), "InjectedPreparationFailure_" + stage);
                Assert.AreEqual(0, document.Flags.Count, stage);
                if (stage == "Folder")
                    AssertLog(start, "Failed while preparing explorer item selection.", "InjectedPreparationFailure_Folder");
            }
        }

        [TestMethod]
        public void LogWriteFailure_RetainsBatchAndRetriesInOrderWithoutDuplicates()
        {
            string root = Path.Combine(Path.GetTempPath(), "KjtbLogRetry-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "test.log");
            System.Collections.Concurrent.ConcurrentQueue<string> queue = new System.Collections.Concurrent.ConcurrentQueue<string>();
            queue.Enqueue("first" + Environment.NewLine);
            queue.Enqueue("second" + Environment.NewLine);
            try
            {
                File.WriteAllText(path, "existing" + Environment.NewLine);
                using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bool failed = false;
                    try { AppLogger.FlushPendingLogEntries(queue, path); }
                    catch (IOException) { failed = true; }
                    Assert.IsTrue(failed, "The write must actually fail.");
                    Assert.AreEqual(2, queue.Count, "A failed write must not discard its batch.");
                }
                queue.Enqueue("third" + Environment.NewLine);
                Parallel.Invoke(() => AppLogger.FlushPendingLogEntries(queue, path),
                    () => AppLogger.FlushPendingLogEntries(queue, path));
                Assert.AreEqual(0, queue.Count);
                CollectionAssert.AreEqual(new[] { "existing", "first", "second", "third" }, File.ReadAllLines(path));
                AppLogger.FlushPendingLogEntries(queue, path);
                Assert.AreEqual(4, File.ReadAllLines(path).Length);
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public void SelectionFailure_PropagatesAndDoesNotAdvanceFirstSelectionFlags()
        {
            SelectionDocument document = new SelectionDocument { FailFirst = true };
            DateTime start = DateTime.UtcNow;
            Exception failure = null;
            WithSelectionShell(document, interop =>
            {
                try { interop.SelectItems((IntPtr)100, SelectionPaths()); }
                catch (Exception ex) { failure = ex; }
            });
            Assert.IsNotNull(failure, "Selection failure must reach the caller.");
            CollectionAssert.AreEqual(new[] { 29, 29 }, document.Flags);
            AssertLog(start, "Failed to select an individual explorer item.", "InjectedSelectionFailure");
        }

        [TestMethod]
        public void SelectionSuccess_PreservesFirstAndSubsequentSelectionFlags()
        {
            SelectionDocument document = new SelectionDocument();
            WithSelectionShell(document, interop => interop.SelectItems((IntPtr)100, SelectionPaths()));
            CollectionAssert.AreEqual(new[] { 29, 1 }, document.Flags);
        }

        [TestMethod]
        public void Selection_NullDocumentIsReportedAsPreparationFailure()
        {
            SelectionDocument document = new SelectionDocument { ReturnNullDocument = true };
            Exception failure = null;
            WithSelectionShell(document, interop =>
            {
                try { interop.SelectItems((IntPtr)100, SelectionPaths()); }
                catch (Exception ex) { failure = ex; }
            });
            Assert.IsNotNull(failure);
            StringAssert.Contains(failure.ToString(), "Explorer document is unavailable");
            CollectionAssert.AreEqual(new int[0], document.Flags);
        }

        [TestMethod]
        public void Selection_MissingTargetWindowIsReportedAsPreparationFailure()
        {
            SelectionDocument document = new SelectionDocument();
            Exception failure = null;
            WithSelectionShell(document, interop =>
            {
                try { interop.SelectItems((IntPtr)999, SelectionPaths()); }
                catch (Exception ex) { failure = ex; }
            });
            Assert.IsNotNull(failure);
            StringAssert.Contains(failure.ToString(), "Failed to restore Explorer item selection");
            CollectionAssert.AreEqual(new int[0], document.Flags);
        }

        [TestMethod]
        public void Selection_ParseNameFallbackRejectsDifferentFileSystemItem()
        {
            SelectionDocument document = new SelectionDocument();
            ShellFolderItemSelectionHelper helper = new ShellFolderItemSelectionHelper(
                (value, property) => value is SelectionItem ? ((SelectionItem)value).Path : null,
                (value, method, args) => method == "ParseName"
                    ? new SelectionItem { ItemPath = @"C:\Wrong\one", Document = document }
                    : null,
                delegate { }, new ShellItemPathResolver(value => value), delegate { });
            object[] matches = helper.FindFolderItemsByPaths(document, document, 0, new[] { @"C:\B\one" });
            Assert.IsNull(matches[0]);
        }

        [TestMethod]
        public void Selection_ParseNameFallbackPreservesVirtualItem()
        {
            SelectionDocument document = new SelectionDocument();
            SelectionItem virtualItem = new SelectionItem { ItemPath = "virtual-item", Document = document };
            ShellFolderItemSelectionHelper helper = new ShellFolderItemSelectionHelper(
                (value, property) => value is SelectionItem ? ((SelectionItem)value).Path : null,
                (value, method, args) => method == "ParseName" ? virtualItem : null,
                delegate { }, new ShellItemPathResolver(value => value), delegate { });
            object[] matches = helper.FindFolderItemsByPaths(document, document, 0, new[] { "::{00000000-0000-0000-0000-000000000000}" });
            Assert.AreSame(virtualItem, matches[0]);
        }

        [TestMethod]
        public async Task Absorption_SelectionFailurePreservesSourceAndReleasesOperation()
        {
            await VerifyAbsorptionFailure(null);
        }

        [TestMethod]
        public async Task Absorption_PreparationFailurePreservesSourceAndAllowsRetry()
        {
            foreach (string stage in new[] { "Windows", "Document", "Folder", "Items", "Count", "Item", "Path", "ParseName" })
                await VerifyAbsorptionFailure(stage);
        }

        private static async Task VerifyAbsorptionFailure(string stage)
        {
            SelectionExplorer explorer = new SelectionExplorer { FailureStage = stage, FailSelection = stage == null };
            int closed = 0, shown = 0;
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(explorer,
                new ExplorerWindowTrackingState(h => true, delegate { }, delegate { }),
                TestTabPersistenceFactory.Create(), h => "", h => shown++, delegate { }, h => closed++, () => null, delegate { });
            using (KjTabBar.ViewModels.TabBarViewModel vm = new KjTabBar.ViewModels.TabBarViewModel(
                (IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
            {
                Exception failure = null;
                try { await service.AbsorbExplorerWindowAsync((IntPtr)200, vm, @"C:\B", false, false, null); }
                catch (Exception ex) { failure = ex; }
                Assert.IsNotNull(failure);
                Assert.AreEqual(0, closed);
                Assert.AreEqual(1, shown);
                Assert.IsFalse(vm.IsTabOperationPending);
                explorer.FailSelection = false;
                explorer.FailureStage = null;
                Assert.IsTrue(await service.AbsorbExplorerWindowAsync((IntPtr)200, vm, @"C:\B", false, false, null));
                Assert.AreEqual(1, closed, "A retry must still be able to complete absorption.");
            }
        }

        [TestMethod]
        public void NestedExceptions_LogAllCausesAndMaskPaths()
        {
            string marker = "Nested_" + Guid.NewGuid().ToString("N");
            DateTime start = DateTime.UtcNow;
            Exception failure = new TargetInvocationException(new AggregateException(
                new IOException(marker + "_first " + @"C:\private\secret.txt"),
                new InvalidOperationException(marker + "_second")));
            AppLogger.LogError("ExceptionLoggingTests", marker, failure);
            string record = AssertLog(start, marker, marker + "_second");
            StringAssert.Contains(record, "System.Reflection.TargetInvocationException");
            StringAssert.Contains(record, "System.IO.IOException");
            StringAssert.Contains(record, marker + "_first");
            StringAssert.Contains(record, "System.InvalidOperationException");
            Assert.IsFalse(record.Contains(@"C:\private"));
        }

        private static System.Collections.Generic.List<string> SelectionPaths()
        {
            return new System.Collections.Generic.List<string> { @"C:\B\one", @"C:\B\two" };
        }

        private static void WithSelectionShell(SelectionDocument document, Action<ShellWindowComInterop> action)
        {
            FieldInfo cache = typeof(ShellWindowCacheManager).GetField("_threadLocalShellApplication", BindingFlags.Static | BindingFlags.NonPublic);
            object original = cache.GetValue(null);
            try
            {
                cache.SetValue(null, new SelectionShell(document));
                ShellWindowComInterop interop = new ShellWindowComInterop(
                    new ShellExplorerWindowMatcher(ShellWindowComInterop.GetComProperty, (h, flags) => h), null, null, null,
                    new ShellFolderItemSelectionHelper(ShellWindowComInterop.GetComProperty,
                        (target, method, args) => ShellWindowComInterop.InvokeComMethod(target, method, args),
                        ShellWindowComInterop.ReleaseComObjectSafe, new ShellItemPathResolver(path => path), AppLogger.LogErrorThrottled),
                    null, path => path, path => true);
                action(interop);
            }
            finally { cache.SetValue(null, original); }
        }

        public sealed class SelectionShell
        {
            private readonly SelectionDocument _document;
            public SelectionShell(SelectionDocument document) { _document = document; }
            public object Windows() { _document.ThrowIf("Windows"); return this; }
            public int Count { get { _document.ThrowIf("WindowsCount"); return _document.PrecedeWithBrokenWindow ? 2 : 1; } }
            public object Item(int index) { _document.ThrowIf("WindowItem"); return _document.PrecedeWithBrokenWindow && index == 0 ? (object)new BrokenWindow() : this; }
            public string FullName { get { _document.ThrowIf("FullName"); return "explorer.exe"; } }
            public int HWND { get { _document.ThrowIf("HWND"); return 100; } }
            public object Document { get { _document.ThrowIf("Document"); return _document.ReturnNullDocument ? null : (object)_document; } }
        }

        public sealed class SelectionDocument
        {
            public bool FailFirst;
            public string FailureStage;
            public bool PrecedeWithBrokenWindow;
            public bool ReturnNullDocument;
            public void ThrowIf(string stage) { if (FailureStage == stage) throw new IOException("InjectedPreparationFailure_" + stage); }
            public readonly System.Collections.Generic.List<int> Flags = new System.Collections.Generic.List<int>();
            public object Folder { get { ThrowIf("Folder"); return this; } }
            public object Items() { ThrowIf("Items"); return this; }
            public int Count { get { ThrowIf("Count"); return FailureStage == "ParseName" ? 0 : 2; } }
            public object Item(int index) { ThrowIf("Item"); return new SelectionItem { ItemPath = SelectionPaths()[index], Document = this }; }
            public object ParseName(string name) { ThrowIf("ParseName"); return null; }
            public void SelectItem(object item, int flags)
            {
                Flags.Add(flags);
                if (FailFirst && Flags.Count == 1) throw new IOException("InjectedSelectionFailure");
            }
        }

        public sealed class SelectionItem
        {
            public string ItemPath;
            public SelectionDocument Document;
            public string Path { get { Document.ThrowIf("Path"); return ItemPath; } }
        }

        private sealed class SelectionExplorer : UnitTestProject.MockExplorerService, IExplorerService, IAsyncExplorerService
        {
            internal string Current = @"C:\A";
            internal bool FailSelection = true;
            internal string FailureStage;
            public override string GetCurrentPath(IntPtr hwnd) { return Current; }
            System.Collections.Generic.List<string> IExplorerService.GetSelectedItems(IntPtr hwnd) { return SelectionPaths(); }
            public Task<string> GetCurrentPathAsync(IntPtr hwnd) { return Task.FromResult(Current); }
            public Task<bool> NavigateAsync(IntPtr hwnd, string path) { Current = path; return Task.FromResult(true); }
            public Task SelectItemsAsync(IntPtr hwnd, System.Collections.Generic.List<string> items)
            {
                WithSelectionShell(new SelectionDocument { FailFirst = FailSelection, FailureStage = FailureStage }, interop => interop.SelectItems(hwnd, items));
                return Task.CompletedTask;
            }
        }

        [TestMethod]
        public void ComFailures_LogAndStillReturnNull()
        {
            string name = "Missing_" + Guid.NewGuid().ToString("N");
            DateTime start = DateTime.UtcNow;
            object target = new object();
            Assert.IsNull(ShellWindowComInterop.GetComProperty(target, name));
            Assert.IsNull(ShellWindowComInterop.InvokeComMethod(target, name));
            AssertLog(start, "Failed to read a COM property: " + name, "System.MissingMethodException");
            AssertLog(start, "Failed to invoke a COM method: " + name, "System.MissingMethodException");
            // Repeat failures retain the same result even when the log is throttled.
            Assert.IsNull(ShellWindowComInterop.GetComProperty(target, name));
            Assert.IsNull(ShellWindowComInterop.InvokeComMethod(target, name));
            Assert.IsNull(ShellWindowComInterop.GetComProperty(null, name));
            Assert.IsNull(ShellWindowComInterop.InvokeComMethod(null, name));
            Assert.AreEqual(3, ShellWindowComInterop.GetComProperty("abc", "Length"));
            Assert.AreEqual("abc", ShellWindowComInterop.InvokeComMethod("abc", "ToString"));
        }

        [TestMethod]
        public void ShortcutSaveFailure_LogsReturnsFalseReleasesAndDeletesPartialFile()
        {
            VerifyShortcutFailure(false);
        }

        [TestMethod]
        public void ShortcutCleanupFailure_LogsAndStillReturnsFalseAndReleases()
        {
            VerifyShortcutFailure(true);
        }

        private static void VerifyShortcutFailure(bool blockDeletion)
        {
            string root = Path.Combine(Path.GetTempPath(), "KjtbExceptionTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "partial.lnk");
            FailingShortcut shortcut = new FailingShortcut();
            FakeShell shell = new FakeShell(shortcut);
            object released = null;
            int releaseCount = 0;
            ShellShortcutManager manager = new ShellShortcutManager(
                value => value, value => value, value => null, () => null,
                value => { released = value; releaseCount++; },
                (value, property) => null, (value, method, args) => null, "", "", "");
            FileStream lockedFile = null;
            try
            {
                File.WriteAllText(path, "partial shortcut");
                if (blockDeletion) lockedFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                DateTime start = DateTime.UtcNow;
                MethodInfo create = typeof(ShellShortcutManager).GetMethod("TryCreateShortcutFile", BindingFlags.Instance | BindingFlags.NonPublic);
                bool result = (bool)create.Invoke(manager, new object[] { typeof(FakeShell), shell, "source", path });
                Assert.IsFalse(result);
                Assert.AreEqual(1, shortcut.SaveCalls);
                Assert.AreEqual("source", shortcut.TargetPath);
                Assert.AreEqual(1, releaseCount);
                Assert.AreSame(shortcut, released);
                Assert.AreEqual(blockDeletion, File.Exists(path));
                string saveRecord = AssertLog(start, "Failed to create shortcut file.", "System.Reflection.TargetInvocationException");
                StringAssert.Contains(saveRecord, "System.IO.IOException: Injected shortcut save failure.");
                if (blockDeletion)
                    AssertLog(start, "Failed to delete partially created shortcut file.", "System.IO.IOException");
            }
            finally
            {
                if (lockedFile != null) lockedFile.Dispose();
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void ShutdownFlushFailure_LogsAndContinuesThroughTimerAndMutexDisposal()
        {
            // Shutdown disposes process-wide services; run it in a separate process.
            string framework = typeof(Assert).Assembly.Location.Replace("'", "''");
            string assembly = typeof(ExceptionLoggingTests).Assembly.Location.Replace("'", "''");
            string script = "$ErrorActionPreference='Stop'; [Reflection.Assembly]::LoadFrom('" + framework +
                "') | Out-Null; [Reflection.Assembly]::LoadFrom('" + assembly +
                "') | Out-Null; $probe=New-Object 'UnitTestProject.ExceptionLoggingTests+ShutdownProbe'; $probe.Run(); exit 0";
            System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)),
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(info))
            {
                if (!process.WaitForExit(15000))
                {
                    process.Kill();
                    Assert.Fail("Shutdown probe timed out.");
                }
                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                Assert.AreEqual(0, process.ExitCode, output);
            }
        }

        public sealed class ShutdownProbe
        {
            public void Run()
            {
                EnsureTestLogDirectory();
                VerifyShutdownFailure();
            }
        }

        private static void VerifyShutdownFailure()
        {
            string marker = "InjectedShutdownFailure_" + Guid.NewGuid().ToString("N");
            TabPersistenceService persistence = new TabPersistenceService();
            // FlushPendingSave is the actual boundary covered by the shutdown catch.
            typeof(TabPersistenceService).GetField("_pendingSave", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(persistence, Task.FromException(new IOException(marker)));
            int queued = 0;
            ExplorerMonitorTimer timer = new ExplorerMonitorTimer(TimeSpan.FromHours(1), action => queued++, () => { });
            Mutex mutex = new Mutex(true);
            try
            {
                DateTime start = DateTime.UtcNow;
                new AppRuntimeCoordinator().Shutdown(new AppRuntimeContext
                {
                    TabPersistence = persistence,
                    MonitorTimer = timer,
                    Mutex = mutex
                });
                timer.RequestTick();
                Assert.AreEqual(0, queued, "Shutdown must dispose the monitor even after a save failure.");
                Assert.IsTrue(mutex.SafeWaitHandle.IsClosed, "Shutdown must reach its final mutex cleanup.");
                AssertLog(start, "Failed to save tabs during application exit.", "System.IO.IOException: " + marker);
            }
            finally
            {
                timer.Dispose();
                if (!mutex.SafeWaitHandle.IsClosed) { mutex.ReleaseMutex(); mutex.Dispose(); }
            }
        }

        private static string AssertLog(DateTime start, string message, string exception)
        {
            // Flush and the background writer can overlap; wait for the on-disk record.
            string matchedRecord = null;
            bool found = SpinWait.SpinUntil(delegate
            {
                AppLogger.Flush();
                List<string> logDirectories = new List<string>(Directory.GetDirectories(Path.GetTempPath(), "KjtbExceptionTests-*"));
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(appData)) logDirectories.Add(Path.Combine(appData, "KjTabBar", "Logs"));
                foreach (string directory in logDirectories)
                {
                    string path = Path.Combine(directory, "KjTabBar.log");
                    if (!File.Exists(path)) continue;
                    string content;
                    try
                    {
                        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (StreamReader reader = new StreamReader(stream)) content = reader.ReadToEnd();
                    }
                    catch (IOException) { continue; }
                    foreach (string line in System.Text.RegularExpressions.Regex.Split(content, @"(?m)(?=^\d{4}-\d{2}-\d{2}T)"))
                    {
                        int separator = line.IndexOf(' ');
                        DateTime timestamp;
                        if (separator > 0 && DateTime.TryParse(line.Substring(0, separator), CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out timestamp) && timestamp >= start &&
                            line.Contains("[ERROR]") && line.Contains(message) && line.Contains(exception)) { matchedRecord = line; return true; }
                    }
                }
                return false;
            }, 5000);
            Assert.IsTrue(found, "Expected a new on-disk error record: " + message + " / " + exception);
            return matchedRecord;
        }

        public sealed class FakeShell
        {
            private readonly FailingShortcut _shortcut;
            public FakeShell(FailingShortcut shortcut) { _shortcut = shortcut; }
            public object CreateShortcut(string path) { return _shortcut; }
        }

        public sealed class FailingShortcut
        {
            public string TargetPath { get; set; }
            public int SaveCalls { get; private set; }
            public void Save()
            {
                SaveCalls++;
                throw new IOException("Injected shortcut save failure.");
            }
        }
    }
}
