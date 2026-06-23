using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.ViewModels;

namespace KjTabBar.Views
{
    internal sealed class TabBarWindowPositioner
    {
        private readonly TabBarWindow _window;
        private readonly IExplorerService _explorerService;
        private double _dpiScale = 1.0;
        private IntPtr _windowHwnd = IntPtr.Zero;
        private NativeMethods.RECT? _lastKnownExplorerWindowRect;

        public double DpiScale
        {
            get { return _dpiScale; }
            set { _dpiScale = value; }
        }

        public NativeMethods.RECT? LastKnownExplorerWindowRect
        {
            get { return _lastKnownExplorerWindowRect; }
        }

        public TabBarWindowPositioner(TabBarWindow window, IExplorerService explorerService)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _explorerService = explorerService ?? throw new ArgumentNullException(nameof(explorerService));
        }

        public void InitDpiScale()
        {
            try
            {
                DpiScale dpi = VisualTreeHelper.GetDpi(_window);
                _dpiScale = dpi.DpiScaleX;
            }
            catch
            {
                PresentationSource source = PresentationSource.FromVisual(_window);
                if (source != null)
                {
                    _dpiScale = source.CompositionTarget.TransformToDevice.M11;
                }
            }
        }

        public bool IsExplorerAlive(IntPtr explorerHwnd)
        {
            return NativeMethods.IsWindow(explorerHwnd);
        }

        public bool IsExplorerMinimized(IntPtr explorerHwnd)
        {
            return NativeMethods.IsIconic(explorerHwnd);
        }

        public bool IsExplorerOrSelfForeground(IntPtr explorerHwnd, IntPtr myHwnd)
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            if (foreground == explorerHwnd) return true;
            if (foreground == myHwnd) return true;
            return false;
        }

        private IntPtr GetWindowHandle()
        {
            if (_windowHwnd == IntPtr.Zero)
            {
                _windowHwnd = new WindowInteropHelper(_window).Handle;
            }
            return _windowHwnd;
        }

        private double GetDpiScaleForWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return 1.0;
            try
            {
                uint dpi = NativeMethods.GetDpiForWindow(hwnd);
                if (dpi != 0)
                {
                    return dpi / 96.0;
                }

                IntPtr hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (hMonitor != IntPtr.Zero)
                {
                    uint dpiX, dpiY;
                    if (NativeMethods.GetDpiForMonitor(hMonitor, 0, out dpiX, out dpiY) == 0)
                    {
                        return dpiX / 96.0;
                    }
                }
            }
            catch
            {
                // Fallback to internal _dpiScale if shcore.dll is unavailable or monitor API fails
            }
            return _dpiScale;
        }

        public void UpdatePosition(IntPtr explorerHwnd, TabBarViewModel vm)
        {
            if (vm == null) return;

            // エクスプローラーが存在しない or 最小化 → 非表示
            if (!IsExplorerAlive(explorerHwnd) || IsExplorerMinimized(explorerHwnd))
            {
                if (vm.WindowVisibility != Visibility.Hidden)
                {
                    vm.WindowVisibility = Visibility.Hidden;
                }
                return;
            }

            if (vm.WindowVisibility != Visibility.Visible)
            {
                vm.WindowVisibility = Visibility.Visible;
            }

            // 位置を更新
            NativeMethods.RECT contentRect = _explorerService.GetExplorerWindowRect(explorerHwnd);
            if (contentRect.Width <= 0) return;
            NativeMethods.RECT explorerWindowRect;
            if (NativeMethods.GetWindowRect(explorerHwnd, out explorerWindowRect))
            {
                _lastKnownExplorerWindowRect = explorerWindowRect;
            }

            IntPtr myHwnd = GetWindowHandle();
            if (myHwnd == IntPtr.Zero) return;

            double actualHeight = _window.ActualHeight;
            if (actualHeight <= 0)
            {
                _window.UpdateLayout();
                actualHeight = _window.ActualHeight;
                if (actualHeight <= 0)
                {
                    actualHeight = double.IsNaN(_window.Height) ? 30.0 : _window.Height;
                }
            }

            double currentDpi = GetDpiScaleForWindow(explorerHwnd);
            int expectedHeightPhysical = (int)Math.Round(actualHeight * currentDpi);
            int expectedLeftPhysical = contentRect.Left;
            int expectedTopPhysical = contentRect.Top - expectedHeightPhysical;
            int expectedWidthPhysical = contentRect.Width;

            NativeMethods.RECT myRect;
            if (NativeMethods.GetWindowRect(myHwnd, out myRect))
            {
                int currentWidth = myRect.Right - myRect.Left;
                int currentHeight = myRect.Bottom - myRect.Top;
                if (myRect.Left == expectedLeftPhysical &&
                    myRect.Top == expectedTopPhysical &&
                    currentWidth == expectedWidthPhysical &&
                    currentHeight == expectedHeightPhysical)
                {
                    return;
                }
            }

            NativeMethods.SetWindowPos(
                myHwnd,
                IntPtr.Zero,
                expectedLeftPhysical,
                expectedTopPhysical,
                expectedWidthPhysical,
                expectedHeightPhysical,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }
    }
}
