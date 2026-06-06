using System;
using System.Threading;
using System.Windows.Threading;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.ViewModels;

namespace KjTabBar.Services
{
    internal sealed class AppRuntimeContext
    {
        public TabBarViewModel SaveTarget { get; set; }
        public TabPersistenceService TabPersistence { get; set; }
        public DispatcherTimer MonitorTimer { get; set; }
        public EventHandler MonitorTickHandler { get; set; }
        public IExplorerService ExplorerService { get; set; }
        public TabBarRegistry TabBars { get; set; }
        public TrayIconService TrayIconService { get; set; }
        public WinEventHookRegistration ShowEventHook { get; set; }
        public ExplorerWindowTrackingState WindowTracking { get; set; }
        public WinEventHookRegistration ForegroundEventHook { get; set; }
        public Mutex Mutex { get; set; }
    }

    internal sealed class AppRuntimeCoordinator
    {
        public bool TryAcquireSingleInstanceMutex(string mutexName, out Mutex mutex)
        {
            mutex = new Mutex(false, mutexName);
            bool hasHandle = false;
            try
            {
                hasHandle = mutex.WaitOne(0, false);
            }
            catch (AbandonedMutexException)
            {
                hasHandle = true;
            }

            if (hasHandle)
            {
                return true;
            }

            mutex.Dispose();
            mutex = null;
            return false;
        }

        public WinEventHookRegistration TryRegisterWinEventHook(
            string name,
            uint eventMin,
            uint eventMax,
            NativeMethods.WinEventDelegate callback,
            string failureMessage)
        {
            try
            {
                WinEventHookRegistration hook = new WinEventHookRegistration(name);
                hook.Register(eventMin, eventMax, callback);
                return hook;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("App", failureMessage, ex);
                return null;
            }
        }

        public DispatcherTimer CreateMonitorTimer(TimeSpan interval, EventHandler tickHandler)
        {
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = interval;
            timer.Tick += tickHandler;
            timer.Start();
            return timer;
        }

        public void Shutdown(AppRuntimeContext context)
        {
            if (context == null)
            {
                return;
            }

            try
            {
                if (context.SaveTarget != null && context.TabPersistence != null)
                {
                    context.TabPersistence.SaveTabsIfChanged(context.SaveTarget);
                }
            }
            catch
            {
                AppLogger.LogInfo("App", "Failed to save tabs during application exit.");
            }

            if (context.MonitorTimer != null)
            {
                context.MonitorTimer.Tick -= context.MonitorTickHandler;
                context.MonitorTimer.Stop();
            }

            ThemeManager.Instance.StopMonitoring();

            if (context.ExplorerService != null)
            {
                context.ExplorerService.ReleaseCachedComObjects();
            }

            if (context.TabBars != null)
            {
                context.TabBars.ClearAndCloseAll();
            }

            DisposeIfPresent(context.TrayIconService);
            DisposeIfPresent(context.ShowEventHook);

            try
            {
                if (context.WindowTracking != null)
                {
                    context.WindowTracking.RestoreAllHiddenWindows();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("App", "Failed to restore hidden explorer windows during exit.", ex);
            }

            DisposeIfPresent(context.ForegroundEventHook);

            if (ComThreadService.IsCreated)
            {
                ComThreadService.Instance.Dispose();
            }

            if (context.Mutex != null)
            {
                try
                {
                    context.Mutex.ReleaseMutex();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("App", "Failed to release application mutex.", ex);
                }

                context.Mutex.Dispose();
            }
        }

        private static void DisposeIfPresent(IDisposable disposable)
        {
            if (disposable != null)
            {
                disposable.Dispose();
            }
        }
    }
}
