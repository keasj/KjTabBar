using System;
using System.Windows;
using System.Windows.Media;
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

        public double DpiScale
        {
            get { return _dpiScale; }
            set { _dpiScale = value; }
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

            double expectedLeft = contentRect.Left / _dpiScale;
            double expectedTop = contentRect.Top / _dpiScale - _window.ActualHeight;
            double expectedWidth = contentRect.Width / _dpiScale;

            if (Math.Abs(_window.Left - expectedLeft) > 0.1) _window.Left = expectedLeft;
            if (Math.Abs(_window.Top - expectedTop) > 0.1) _window.Top = expectedTop;
            if (Math.Abs(_window.Width - expectedWidth) > 0.1) _window.Width = expectedWidth;
        }
    }
}
