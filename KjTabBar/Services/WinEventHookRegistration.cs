using System;
using KjTabBar.Helpers;

namespace KjTabBar.Services
{
    internal sealed class WinEventHookRegistration : IDisposable
    {
        private readonly string _name;
        private IntPtr _hook = IntPtr.Zero;
        private NativeMethods.WinEventDelegate _callback;
        private bool _disposed;

        public WinEventHookRegistration(string name)
        {
            _name = name;
        }

        public void Register(uint eventMin, uint eventMax, NativeMethods.WinEventDelegate callback)
        {
            _callback = callback;
            _hook = NativeMethods.SetWinEventHook(
                eventMin,
                eventMax,
                IntPtr.Zero,
                _callback,
                0,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_hook != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.UnhookWinEvent(_hook);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("WinEventHookRegistration", "Failed to unhook " + _name + " event.", ex);
                }

                _hook = IntPtr.Zero;
            }

            _callback = null;
            _disposed = true;
        }
    }
}
