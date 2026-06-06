using System;
using System.Text;
using KjTabBar.Helpers;

namespace KjTabBar.Models
{
    internal sealed class ExplorerLaunchTracker
    {
        private readonly DesktopForegroundTracker _desktopForegroundTracker;
        private readonly ExplorerWindowTrackingState _windowTracking;
        private readonly Func<IntPtr, bool> _isManagedControlPanelLaunchSource;
        private readonly Func<IntPtr, bool> _isManagedExplorerWindow;
        private readonly Func<IntPtr> _getForegroundWindow;
        private readonly Func<IntPtr, string> _getClassName;
        private readonly Func<IntPtr, uint, IntPtr> _getAncestor;
        private readonly Func<IntPtr, bool> _isWindow;

        public ExplorerLaunchTracker(
            DesktopForegroundTracker desktopForegroundTracker,
            ExplorerWindowTrackingState windowTracking,
            Func<IntPtr, bool> isManagedControlPanelLaunchSource,
            Func<IntPtr, bool> isManagedExplorerWindow)
            : this(
                  desktopForegroundTracker,
                  windowTracking,
                  isManagedControlPanelLaunchSource,
                  isManagedExplorerWindow,
                  NativeMethods.GetForegroundWindow,
                  GetClassNameCore,
                  NativeMethods.GetAncestor,
                  NativeMethods.IsWindow)
        {
        }

        internal ExplorerLaunchTracker(
            DesktopForegroundTracker desktopForegroundTracker,
            ExplorerWindowTrackingState windowTracking,
            Func<IntPtr, bool> isManagedControlPanelLaunchSource,
            Func<IntPtr, bool> isManagedExplorerWindow,
            Func<IntPtr> getForegroundWindow,
            Func<IntPtr, string> getClassName,
            Func<IntPtr, uint, IntPtr> getAncestor,
            Func<IntPtr, bool> isWindow)
        {
            _desktopForegroundTracker = desktopForegroundTracker;
            _windowTracking = windowTracking;
            _isManagedControlPanelLaunchSource = isManagedControlPanelLaunchSource;
            _isManagedExplorerWindow = isManagedExplorerWindow;
            _getForegroundWindow = getForegroundWindow;
            _getClassName = getClassName;
            _getAncestor = getAncestor;
            _isWindow = isWindow;
        }

        public void UpdateForegroundState()
        {
            IntPtr foregroundWindow = _getForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                return;
            }

            UpdateForegroundState(foregroundWindow, _getClassName(foregroundWindow));
        }

        public void UpdateForegroundState(IntPtr foregroundWindow, string className)
        {
            RegisterControlPanelTabLaunchCandidate(foregroundWindow, _desktopForegroundTracker.LastForegroundWindow);
            _desktopForegroundTracker.Update(foregroundWindow, className);
        }

        public bool IsForegroundRelatedWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            IntPtr foregroundWindow = _getForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                return false;
            }

            if (foregroundWindow == hwnd)
            {
                return true;
            }

            IntPtr foregroundRoot = _getAncestor(foregroundWindow, NativeMethods.GA_ROOT);
            return foregroundRoot == hwnd;
        }

        public bool TryRegisterDesktopLaunchCandidate(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            if (_windowTracking.DesktopLaunchCandidates.Contains(hwnd))
            {
                return true;
            }

            if (!_desktopForegroundTracker.WasDesktopForegroundRecently())
            {
                return false;
            }

            _windowTracking.DesktopLaunchCandidates.Add(hwnd);
            if (_desktopForegroundTracker.WasDesktopInteractiveForegroundRecently())
            {
                _windowTracking.DesktopInteractiveLaunchCandidates.Add(hwnd);
            }

            return true;
        }

        private void RegisterControlPanelTabLaunchCandidate(IntPtr foregroundWindow, IntPtr previousForegroundWindow)
        {
            IntPtr currentRoot = GetRootWindowOrSelf(foregroundWindow);
            IntPtr previousRoot = GetRootWindowOrSelf(previousForegroundWindow);
            if (currentRoot == IntPtr.Zero || previousRoot == IntPtr.Zero)
            {
                return;
            }

            if (currentRoot == previousRoot)
            {
                return;
            }

            if (_isManagedControlPanelLaunchSource == null || !_isManagedControlPanelLaunchSource(previousRoot))
            {
                return;
            }

            if (!IsUnmanagedExplorerWindow(currentRoot))
            {
                return;
            }

            _windowTracking.ControlPanelTabLaunchCandidates.Add(currentRoot);
        }

        private IntPtr GetRootWindowOrSelf(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr rootHwnd = _getAncestor(hwnd, NativeMethods.GA_ROOT);
            if (rootHwnd != IntPtr.Zero)
            {
                return rootHwnd;
            }

            return hwnd;
        }

        private bool IsUnmanagedExplorerWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            if (_isManagedExplorerWindow != null && _isManagedExplorerWindow(hwnd))
            {
                return false;
            }

            if (!_isWindow(hwnd))
            {
                return false;
            }

            string className = _getClassName(hwnd);
            return className.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetClassNameCore(IntPtr hwnd)
        {
            StringBuilder className = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, className, className.Capacity);
            return className.ToString();
        }
    }
}
