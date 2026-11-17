using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using KjTabBar.Helpers;
using KjTabBar.ViewModels;

namespace KjTabBar.Services
{
    // Local, opt-in acceptance-test controls. No commands or paths are executed from files.
    internal static class AcceptanceTestHooks
    {
#if KJTB_ACCEPTANCE_TESTS
        internal static readonly bool Available = true;
#else
        internal static readonly bool Available = false;
#endif
        internal static string CommandDirectory
        {
            get
            {
#if KJTB_ACCEPTANCE_TESTS
                if (Environment.GetEnvironmentVariable("KJTB_DIAGNOSTICS") != "1") return null;
                string root = Environment.GetEnvironmentVariable("KJTB_ACCEPTANCE_DIR");
                if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root)) return null;
                try
                {
                    using (Process process = Process.GetCurrentProcess())
                        return Path.Combine(Path.GetFullPath(root), process.Id.ToString());
                }
                catch (ArgumentException) { return null; }
                catch (NotSupportedException) { return null; }
                catch (IOException) { return null; }
                catch (System.Security.SecurityException) { return null; }
#else
                return null;
#endif
            }
        }

        private static string Take(string name)
        {
            string directory = CommandDirectory;
            if (directory == null) return null;
            string claimed = null;
            try
            {
                string file = Path.Combine(directory, name + ".txt");
                if (!File.Exists(file)) return null;
                // Rename claims one command exactly once, even if operations overlap.
                claimed = file + "." + Guid.NewGuid().ToString("N") + ".used";
                File.Move(file, claimed);
                FileInfo info = new FileInfo(claimed);
                if (info.Length > 128 || DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromMinutes(1))
                {
                    Log("ignored-expired-or-oversized-" + name);
                    return null;
                }
                return File.ReadAllText(claimed).Trim();
            }
            catch (IOException) { Log("command-io-error-" + name); return null; }
            catch (UnauthorizedAccessException) { Log("command-access-error-" + name); return null; }
            finally
            {
                if (claimed != null)
                {
                    try { File.Delete(claimed); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        // Markers expire automatically. Removing them resumes normal behavior immediately.
        internal static bool MarkerActive(string name)
        {
            string directory = CommandDirectory;
            if (directory == null) return false;
            try
            {
                string file = Path.Combine(directory, name + ".flag");
                if (!File.Exists(file)) return false;
                TimeSpan age = DateTime.UtcNow - File.GetLastWriteTimeUtc(file);
                return age >= TimeSpan.Zero && age < TimeSpan.FromSeconds(30);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal static bool RejectRollback()
        {
            string command = Take("rollback");
            if (command == null) return false;
            bool reject = command == "reject";
            Log(reject ? "rollback-rejected" : "invalid-rollback-command");
            return reject;
        }

        internal static Task<bool> NavigateAsync(Func<Task<bool>> navigate, Func<bool> hostCurrent)
        {
            string command = Take("navigate");
            if (command == null) return navigate();
            if (command == "reject") { Log("navigate-rejected"); return Task.FromResult(false); }
            if (command == "throw") { Log("navigate-thrown"); return Task.FromException<bool>(new TimeoutException("Acceptance test navigation failure.")); }
            if (command == "drop") { Log("navigate-accepted-without-request"); return Task.FromResult(true); }
            string[] parts = command.Split(':');
            int delay;
            if (parts.Length != 2 || !int.TryParse(parts[1], out delay) || delay < 1 || delay > 30000 ||
                (parts[0] != "delay" && parts[0] != "late-throw"))
            {
                Log("invalid-navigation-command");
                return navigate();
            }
            Log("navigate-" + parts[0] + " delayMs=" + delay);
            if (parts[0] == "delay") return DelayedNavigationAsync(delay, navigate, hostCurrent);
            // DelayedNavigationAsync observes every exception; no async void or unobserved faults.
            _ = DelayedNavigationAsync(delay, navigate, hostCurrent);
            return Task.FromException<bool>(new TimeoutException("Acceptance test failure with a delayed real request."));
        }

        private static async Task<bool> DelayedNavigationAsync(int delay, Func<Task<bool>> navigate, Func<bool> hostCurrent)
        {
            try
            {
                await Task.Delay(delay);
                if (CommandDirectory == null || !hostCurrent()) { Log("delayed-request-discarded"); return false; }
                bool accepted = await navigate();
                Log("delayed-real-request accepted=" + accepted);
                return accepted;
            }
            catch (Exception ex)
            {
                Log("delayed-request-error type=" + ex.GetType().Name);
                return false;
            }
        }

        internal static void Trace(string stage, TabBarViewModel vm, string observedPath = null)
        {
            if (CommandDirectory == null) return;
            Log(stage + " host=" + vm.ExplorerHwnd + " version=" + vm.SynchronizationVersion +
                " selected=" + vm.ActiveTabIndex + " protected=" + vm.IsHostLocationUnconfirmed +
                " restoring=" + vm.IsRestoringControlPanelHost + " pending=" + (vm.NavigationTracker.NavigatingToPath != null) +
                " matchesActive=" + (!string.IsNullOrEmpty(observedPath) && vm.ActiveTab != null && vm.PathEquals(observedPath, vm.ActiveTab.Path)) +
                " cancelledMatch=" + (!string.IsNullOrEmpty(observedPath) && vm.IsCancelledNavigationMatch(observedPath)) +
                " grace=" + vm.NavigationTracker.IsExplorerHostSwitchGraceActive(DateTime.UtcNow) +
                " hostAgeMs=" + vm.NavigationTracker.HostSwitchAgeMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                " syntheticGrace=" + MarkerActive("grace"));
        }

        private static void Log(string message)
        {
            using (Process process = Process.GetCurrentProcess())
                AppLogger.LogDiagnostic("AcceptanceTest", "pid=" + process.Id + " tick=" + Stopwatch.GetTimestamp() + " " + message);
        }
    }
}
