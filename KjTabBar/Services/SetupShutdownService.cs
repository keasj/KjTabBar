using System;
using System.Diagnostics;
using System.Threading;
using KjTabBar.Helpers;

namespace KjTabBar.Services
{
    // Same-session installers request the normal, file-operation-aware shutdown path.
    internal sealed class SetupShutdownService : IDisposable
    {
        private readonly EventWaitHandle _signal;
        private readonly RegisteredWaitHandle _registration;

        internal SetupShutdownService(Action shutdown)
        {
            if (shutdown == null) throw new ArgumentNullException("shutdown");
            using (Process process = Process.GetCurrentProcess())
                _signal = new EventWaitHandle(false, EventResetMode.AutoReset, GetEventName(process.Id));
            _registration = ThreadPool.RegisterWaitForSingleObject(_signal, (state, timedOut) =>
            {
                try { shutdown(); }
                catch (Exception ex) { AppLogger.LogError("SetupShutdown", "Could not dispatch shutdown request.", ex); }
            }, null, Timeout.Infinite, true);
        }

        internal static string GetEventName(int processId)
        {
            return @"Local\KjTabBar_Shutdown_" + processId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static bool TryRequest(int processId)
        {
            try
            {
                using (EventWaitHandle signal = EventWaitHandle.OpenExisting(GetEventName(processId)))
                    return signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public void Dispose()
        {
            _registration.Unregister(null);
            _signal.Dispose();
        }
    }
}
