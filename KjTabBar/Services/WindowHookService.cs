using System;
using KjTabBar.Helpers;

namespace KjTabBar.Services
{
    public delegate void WindowEventEventHandler(IntPtr hwnd);

    public class WindowHookService : IDisposable
    {
        private IntPtr _foregroundEventHook = IntPtr.Zero;
        private NativeMethods.WinEventDelegate _foregroundEventProc;
        private IntPtr _showEventHook = IntPtr.Zero;
        private NativeMethods.WinEventDelegate _showEventProc;

        public event WindowEventEventHandler ForegroundWindowChanged;
        public event WindowEventEventHandler WindowShown;

        public WindowHookService()
        {
        }

        public void Start()
        {
            try
            {
                _foregroundEventProc = ForegroundEventCallback;
                _foregroundEventHook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    _foregroundEventProc,
                    0,
                    0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT);
            }
            catch
            {
                _foregroundEventHook = IntPtr.Zero;
            }

            try
            {
                _showEventProc = ShowEventCallback;
                _showEventHook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_OBJECT_SHOW,
                    NativeMethods.EVENT_OBJECT_SHOW,
                    IntPtr.Zero,
                    _showEventProc,
                    0, 0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT);
            }
            catch
            {
                _showEventHook = IntPtr.Zero;
            }
        }

        private void ForegroundEventCallback(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND && hwnd != IntPtr.Zero)
                {
                    ForegroundWindowChanged?.Invoke(hwnd);
                }
            }
            catch { }
        }

        private void ShowEventCallback(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (eventType == NativeMethods.EVENT_OBJECT_SHOW && hwnd != IntPtr.Zero && idObject == 0)
                {
                    WindowShown?.Invoke(hwnd);
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (_showEventHook != IntPtr.Zero)
            {
                try { NativeMethods.UnhookWinEvent(_showEventHook); } catch { }
                _showEventHook = IntPtr.Zero;
                _showEventProc = null;
            }

            if (_foregroundEventHook != IntPtr.Zero)
            {
                try { NativeMethods.UnhookWinEvent(_foregroundEventHook); } catch { }
                _foregroundEventHook = IntPtr.Zero;
                _foregroundEventProc = null;
            }
        }
    }
}
