using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Data;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.ViewModels;
using KjTabBar.Services;

namespace KjTabBar.Views
{
    public partial class TabBarWindow : Window
    {
        public IExplorerService ExplorerService { get; set; }
        internal ExplorerWindowTrackingState WindowTrackingState { get; set; }
        internal ExplorerHostSwitchCoordinator ExplorerHostSwitchCoordinator { get; set; }
        private IExplorerService _explorerService => ExplorerService;

        private IntPtr _myHwnd;
        private IntPtr _pendingOwnerExplorerHwnd;

        // ドラッグ用変数
        private Point _dragStartPoint;
        private bool _isDragging = false;
        private Button _addTabButton;

        // ヘルパークラス
        private TabBarWindowPositioner _positioner;
        private TabBarWindowContextMenuBuilder _contextMenuBuilder;
        private TabBarWindowDragDropHandler _dragDropHandler;
        private TabBarWindowRuntimeCoordinator _runtimeCoordinator;

        public TabBarWindow()
        {
            InitializeComponent();
            Loaded += TabBarWindow_Loaded;
            PreviewDragEnter += TabBarWindow_DragEnter;
            PreviewDragOver += TabBarWindow_DragOver;
            PreviewDrop += TabBarWindow_Drop;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (_runtimeCoordinator != null)
            {
                _runtimeCoordinator.HandleRenderSizeChanged(IsLoaded, sizeInfo.HeightChanged);
            }
        }

        private void TabBarWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // ヘルパーの初期化
            _positioner = new TabBarWindowPositioner(this, _explorerService);
            _contextMenuBuilder = new TabBarWindowContextMenuBuilder(this, _explorerService);
            _dragDropHandler = new TabBarWindowDragDropHandler(this, _explorerService, _contextMenuBuilder);

            SetToolWindowStyle();
            _positioner.InitDpiScale();

            WindowInteropHelper helper = new WindowInteropHelper(this);
            _myHwnd = helper.Handle;
            _runtimeCoordinator = new TabBarWindowRuntimeCoordinator(
                Dispatcher,
                IsExplorerAliveCore,
                UpdatePosition,
                SyncWithExplorerAsync,
                Close);

            // テーマ適用
            ApplyTheme();
            ThemeManager.Instance.ThemeChanged += ThemeManager_ThemeChanged;

            TabBarViewModel vm = GetVM();
            if (vm != null)
            {
                SetupTabsWithAddButton(vm);
                _runtimeCoordinator.Start(vm.ExplorerHwnd);
            }
        }

        private void SetupTabsWithAddButton(TabBarViewModel vm)
        {
            if (_addTabButton != null)
            {
                _addTabButton.Click -= AddTab_Click;
                _addTabButton = null;
            }

            CompositeCollection composite = new CompositeCollection();

            CollectionContainer cc = new CollectionContainer();
            BindingOperations.SetBinding(cc, CollectionContainer.CollectionProperty, new Binding("Tabs") { Source = vm });
            composite.Add(cc);

            _addTabButton = new Button();
            _addTabButton.Style = (Style)FindResource("AddTabBtn");
            _addTabButton.Click += AddTab_Click;
            _addTabButton.Margin = new Thickness(2, 2, 0, 0);
            _addTabButton.ToolTip = TryFindResource("AddTabButtonToolTip") as string ?? "New Tab (Select Folder)";
            composite.Add(_addTabButton);

            TabItemsControl.ItemsSource = composite;
        }

        private void SetToolWindowStyle()
        {
            WindowInteropHelper helper = new WindowInteropHelper(this);
            int exStyle = (int)NativeMethods.GetWindowLongPtr(helper.Handle, NativeMethods.GWL_EXSTYLE);
            exStyle = exStyle | NativeMethods.WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLongPtr(helper.Handle, NativeMethods.GWL_EXSTYLE, (IntPtr)exStyle);

            TabBarViewModel vm = GetVM();
            if (vm != null)
            {
                UpdateOwnerWindow(vm.ExplorerHwnd);
            }

            if (!IsRunningElevated())
            {
                try
                {
                    NativeMethods.ChangeWindowMessageFilterEx(helper.Handle,
                        NativeMethods.WM_DROPFILES, NativeMethods.MSGFLT_ALLOW, IntPtr.Zero);
                    NativeMethods.ChangeWindowMessageFilterEx(helper.Handle,
                        NativeMethods.WM_COPYGLOBALDATA, NativeMethods.MSGFLT_ALLOW, IntPtr.Zero);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("TabBarWindow", "Failed to enable drag-and-drop related window messages.", ex);
                }
            }
        }

        private bool IsRunningElevated()
        {
            System.Security.Principal.WindowsIdentity identity = null;
            try
            {
                identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                if (identity == null)
                {
                    return false;
                }

                System.Security.Principal.WindowsPrincipal principal =
                    new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindow", "Failed to determine administrator role.", ex);
                return true;
            }
            finally
            {
                if (identity != null)
                {
                    identity.Dispose();
                }
            }
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            if (_positioner != null)
            {
                _positioner.DpiScale = newDpi.DpiScaleX;
            }
            if (_runtimeCoordinator != null)
            {
                _runtimeCoordinator.HandleDpiChanged();
            }
        }

        private TabBarViewModel GetVM()
        {
            return DataContext as TabBarViewModel;
        }

        internal bool IsPointOverAbsorbZone(NativeMethods.POINT screenPoint)
        {
            if (AbsorbDropZone == null || !IsLoaded || !AbsorbDropZone.IsVisible)
            {
                return false;
            }

            Point topLeft = AbsorbDropZone.PointToScreen(new Point(0, 0));
            Point bottomRight = AbsorbDropZone.PointToScreen(new Point(AbsorbDropZone.ActualWidth, AbsorbDropZone.ActualHeight));

            return screenPoint.X >= topLeft.X &&
                   screenPoint.X < bottomRight.X &&
                   screenPoint.Y >= topLeft.Y &&
                   screenPoint.Y < bottomRight.Y;
        }

        // ====== エクスプローラー状態チェック ======

        public bool IsExplorerAlive()
        {
            return IsExplorerAliveCore();
        }

        internal void RebindExplorer(IntPtr explorerHwnd)
        {
            if (explorerHwnd == IntPtr.Zero)
            {
                return;
            }

            TabBarViewModel vm = GetVM();
            if (vm == null)
            {
                return;
            }

            vm.SetExplorerHwnd(explorerHwnd);

            if (IsLoaded)
            {
                UpdateOwnerWindow(explorerHwnd);
                if (_runtimeCoordinator != null)
                {
                    _runtimeCoordinator.RebindExplorer(explorerHwnd);
                }
            }
        }

        // ====== 位置とタイマー ======

        private void UpdatePosition()
        {
            TabBarViewModel vm = GetVM();
            if (vm == null || _positioner == null) return;
            _positioner.UpdatePosition(vm.ExplorerHwnd, vm);
            ApplyPendingOwnerWindowIfReady();
        }

        private void DetachDynamicUiResources()
        {
            if (_addTabButton != null)
            {
                _addTabButton.Click -= AddTab_Click;
                _addTabButton = null;
            }

            if (TabItemsControl != null)
            {
                TabItemsControl.ItemsSource = null;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            Loaded -= TabBarWindow_Loaded;
            PreviewDragEnter -= TabBarWindow_DragEnter;
            PreviewDragOver -= TabBarWindow_DragOver;
            PreviewDrop -= TabBarWindow_Drop;
            ThemeManager.Instance.ThemeChanged -= ThemeManager_ThemeChanged;
            DisposeRuntimeCoordinator();
            DetachDynamicUiResources();
            IDisposable disposableVm = DataContext as IDisposable;
            if (disposableVm != null)
            {
                disposableVm.Dispose();
            }
            DataContext = null;
            base.OnClosed(e);
        }

        // ====== イベントハンドラ ======

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            TabBarViewModel vm = GetVM();
            if (vm == null) return;

            string title = TryFindResource("AddTabDialogDescription") as string;
            if (string.IsNullOrEmpty(title))
            {
                title = "Select a folder to add as a tab.";
            }

            string selectedPath = ShellFolderPicker.BrowseForFolder(title, _explorerService);
            if (!string.IsNullOrEmpty(selectedPath))
            {
                vm.InsertTabWithPath(selectedPath, vm.Tabs.Count, true);
            }

            ReturnFocusToExplorer();
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            TabItemViewModel tab = (TabItemViewModel)button.DataContext;
            TabBarViewModel vm = GetVM();
            if (vm != null)
            {
                vm.CloseTab(tab);
            }
            ReturnFocusToExplorer();
            e.Handled = true;
        }

        private void Tab_Click(object sender, MouseButtonEventArgs e)
        {
            FrameworkElement element = (FrameworkElement)sender;
            TabItemViewModel tab = (TabItemViewModel)element.DataContext;
            TabBarViewModel vm = GetVM();
            if (vm != null && tab != null)
            {
                if (ExplorerHostSwitchCoordinator != null &&
                    !ExplorerHostSwitchCoordinator.PrepareForPath(vm, tab.Path))
                {
                    ReturnFocusToExplorer();
                    return;
                }

                vm.SelectTab(tab);
                if (ExplorerHostSwitchCoordinator != null)
                {
                    ExplorerHostSwitchCoordinator.CompletePendingReveal();
                }
            }
            ReturnFocusToExplorer();
            e.Handled = true;
        }

        private void TabBd_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            Border tabBd = sender as Border;
            if (tabBd == null) return;
            TabItemViewModel tabVM = tabBd.DataContext as TabItemViewModel;
            if (tabVM == null) return;

            TabBarViewModel vm = GetVM();
            if (vm == null || _contextMenuBuilder == null) return;

            _contextMenuBuilder.ShowTabContextMenu(tabBd, tabVM, vm, ReturnFocusToExplorer);
            e.Handled = true;
        }

        private void Border_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            TabBarViewModel vm = GetVM();
            if (_contextMenuBuilder == null) return;

            _contextMenuBuilder.ShowBackgroundContextMenu(sender as UIElement, vm, ReturnFocusToExplorer);
            e.Handled = true;
        }

        // ====== ドラッグ&ドロップ（WPF） ======

        private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;
        }

        private void Tab_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point position = e.GetPosition(null);
                if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    FrameworkElement element = (FrameworkElement)sender;
                    TabItemViewModel tab = (TabItemViewModel)element.DataContext;
                    if (tab != null)
                    {
                        string draggedPath = tab.Path;
                        DragDropEffects effect = DragDrop.DoDragDrop(element, tab, DragDropEffects.Move);
                        OpenDraggedTabInNewWindowIfDroppedOutside(effect, tab, draggedPath);
                    }
                    _isDragging = false;
                }
            }
        }

        private void OpenDraggedTabInNewWindowIfDroppedOutside(DragDropEffects effect, TabItemViewModel tab, string draggedPath)
        {
            TabBarViewModel vm = GetVM();
            if (tab == null || vm == null || _explorerService == null)
            {
                return;
            }

            NativeMethods.POINT cursorPoint;
            if (!NativeMethods.GetCursorPos(out cursorPoint))
            {
                return;
            }

            NativeMethods.RECT windowRect;
            if (!NativeMethods.GetWindowRect(_myHwnd, out windowRect))
            {
                return;
            }

            TabExternalDragOpenDecider.TryOpenInNewWindowAndCloseSourceTab(
                effect,
                tab,
                draggedPath,
                vm,
                OpenPathInNewWindow,
                cursorPoint,
                windowRect);
        }

        internal bool OpenPathInNewWindow(string path)
        {
            if (string.IsNullOrEmpty(path) || _explorerService == null)
            {
                return false;
            }

            if (WindowTrackingState != null)
            {
                WindowTrackingState.RegisterExplicitIndependentLaunchRequest();
            }

            bool opened = false;
            try
            {
                opened = _explorerService.OpenInNewWindow(path);
                return opened;
            }
            finally
            {
                if (!opened && WindowTrackingState != null)
                {
                    WindowTrackingState.CancelExplicitIndependentLaunchRequest();
                }
            }
        }

        private void TabBarWindow_DragEnter(object sender, DragEventArgs e)
        {
            if (_dragDropHandler != null)
            {
                _dragDropHandler.HandleDragEnter(e);
            }
        }

        private void TabBarWindow_DragOver(object sender, DragEventArgs e)
        {
            if (_dragDropHandler != null)
            {
                _dragDropHandler.HandleDragOver(TabItemsControl, e);
            }
        }

        private void TabBarWindow_Drop(object sender, DragEventArgs e)
        {
            if (_dragDropHandler != null)
            {
                TabBarViewModel vm = GetVM();
                _dragDropHandler.HandleDrop(TabItemsControl, e, vm, ReturnFocusToExplorer);
            }
        }

        // ====== フォントサイズ変更 ======

        private void TabBarWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Delta > 0)
                {
                    ChangeFontSize(1.0);
                }
                else if (e.Delta < 0)
                {
                    ChangeFontSize(-1.0);
                }
                e.Handled = true;
            }
        }

        private void ChangeFontSize(double delta)
        {
            UserSettings settings = UserSettings.Current;
            double newSize = settings.FontSize + delta;

            if (newSize < 8.0) newSize = 8.0;
            if (newSize > 32.0) newSize = 32.0;

            if (settings.FontSize != newSize)
            {
                settings.FontSize = newSize;
                settings.Save();
            }
            ReturnFocusToExplorer();
        }

        // ====== テーマ ======

        private void ApplyTheme()
        {
            ThemeManager.Instance.ApplyThemeToResources(this.Resources);
        }

        private void ThemeManager_ThemeChanged(object sender, EventArgs e)
        {
            ApplyTheme();
        }

        // ====== ユーティリティ ======

        private void ReturnFocusToExplorer()
        {
            TabBarViewModel vm = GetVM();
            if (vm != null)
            {
                NativeMethods.ForceSetForegroundWindow(vm.ExplorerHwnd);
            }
        }

        private bool IsExplorerAliveCore()
        {
            TabBarViewModel vm = GetVM();
            if (vm == null || _positioner == null)
            {
                return false;
            }

            return _positioner.IsExplorerAlive(vm.ExplorerHwnd);
        }

        private void UpdateOwnerWindow(IntPtr explorerHwnd)
        {
            WindowInteropHelper helper = new WindowInteropHelper(this);
            if (explorerHwnd == IntPtr.Zero)
            {
                helper.Owner = IntPtr.Zero;
                _pendingOwnerExplorerHwnd = IntPtr.Zero;
                return;
            }

            if (NativeMethods.IsWindowVisible(explorerHwnd))
            {
                helper.Owner = explorerHwnd;
                _pendingOwnerExplorerHwnd = IntPtr.Zero;
                AppLogger.LogInfo("TabBarWindow", string.Format("UpdateOwnerWindow applied owner={0}", explorerHwnd));
                return;
            }

            helper.Owner = IntPtr.Zero;
            _pendingOwnerExplorerHwnd = explorerHwnd;
            AppLogger.LogInfo("TabBarWindow", string.Format("UpdateOwnerWindow deferred owner={0}", explorerHwnd));
        }

        private void ApplyPendingOwnerWindowIfReady()
        {
            if (_pendingOwnerExplorerHwnd == IntPtr.Zero ||
                !NativeMethods.IsWindow(_pendingOwnerExplorerHwnd) ||
                !NativeMethods.IsWindowVisible(_pendingOwnerExplorerHwnd))
            {
                return;
            }

            WindowInteropHelper helper = new WindowInteropHelper(this);
            helper.Owner = _pendingOwnerExplorerHwnd;
            NativeMethods.ShowWindow(_myHwnd, NativeMethods.SW_SHOW);
            AppLogger.LogInfo("TabBarWindow", string.Format("ApplyPendingOwnerWindowIfReady applied owner={0}", _pendingOwnerExplorerHwnd));
            _pendingOwnerExplorerHwnd = IntPtr.Zero;
        }

        private async System.Threading.Tasks.Task SyncWithExplorerAsync()
        {
            TabBarViewModel vm = GetVM();
            if (vm == null)
            {
                return;
            }

            await vm.SyncWithExplorerAsync();
        }

        private void DisposeRuntimeCoordinator()
        {
            if (_runtimeCoordinator != null)
            {
                _runtimeCoordinator.Dispose();
                _runtimeCoordinator = null;
            }
        }
    }
}
