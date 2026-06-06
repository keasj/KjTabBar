using System;
using System.Runtime.InteropServices;
using KjTabBar.Helpers;

namespace KjTabBar.Models
{
    internal sealed class ShellWindowCacheManager
    {
        [ThreadStatic]
        private static object _threadLocalShellApplication = null;

        private int _comCleanupCounter = 0;
        private const int ComCleanupInterval = 40;

        public static void ReleaseComObjectSafe(object comObject)
        {
            if (comObject == null)
            {
                return;
            }

            try
            {
                if (Marshal.IsComObject(comObject))
                {
                    Marshal.FinalReleaseComObject(comObject);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ShellWindowCacheManager", "Failed to release COM object.", ex);
            }
        }

        public void RunPeriodicComCleanup()
        {
            int counter = System.Threading.Interlocked.Increment(ref _comCleanupCounter);
            if (counter < ComCleanupInterval)
            {
                return;
            }

            System.Threading.Interlocked.Exchange(ref _comCleanupCounter, 0);
            try
            {
                Marshal.CleanupUnusedObjectsInCurrentContext();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ShellWindowCacheManager", "Failed to clean up unused COM objects.", ex);
            }
        }

        public static void ResetShellApplication()
        {
            if (_threadLocalShellApplication != null)
            {
                AppLogger.LogInfo("ShellWindowCacheManager", "Releasing Shell.Application COM cache on thread " + System.Threading.Thread.CurrentThread.ManagedThreadId.ToString() + ".");
            }

            ReleaseComObjectSafe(_threadLocalShellApplication);
            _threadLocalShellApplication = null;
        }

        public void ReleaseCachedComObjects()
        {
            ResetShellApplication();
            RunPeriodicComCleanup();
        }

        public static bool TryGetShellApplication(out object shellObject)
        {
            shellObject = null;

            if (_threadLocalShellApplication != null)
            {
                shellObject = _threadLocalShellApplication;
                return true;
            }

            Type shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null)
            {
                return false;
            }

            try
            {
                _threadLocalShellApplication = Activator.CreateInstance(shellType);
                shellObject = _threadLocalShellApplication;
                if (shellObject != null)
                {
                    AppLogger.LogInfo("ShellWindowCacheManager", "Created Shell.Application COM cache on thread " + System.Threading.Thread.CurrentThread.ManagedThreadId.ToString() + ".");
                }
                return shellObject != null;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ShellWindowCacheManager", "Failed to create Shell.Application.", ex);
                return false;
            }
        }

        public bool TryCreateShellWindows(out object windowsObject)
        {
            windowsObject = null;

            object shellObject = null;
            if (!TryGetShellApplication(out shellObject))
            {
                return false;
            }

            try
            {
                object shellDynamic = shellObject;
                windowsObject = ShellWindowComInterop.InvokeComMethod(shellDynamic, "Windows");
                return windowsObject != null;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ShellWindowCacheManager", "Failed to create Shell Windows collection.", ex);
                ResetShellApplication();
                return false;
            }
        }
    }
}
