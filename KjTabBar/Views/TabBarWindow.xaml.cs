using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Data;
using System.Threading.Tasks;
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
        private IExplorerService _explorerService => ExplorerService;

        private DispatcherTimer _positionTimer;
        private DispatcherTimer _syncTimer;
        private IntPtr _myHwnd;
        private IntPtr _locationHook = IntPtr.Zero;
        private IntPtr _trackedExplorerHwnd = IntPtr.Zero;
        private NativeMethods.WinEventDelegate _locationEventCallback;

        // ドラッグ用変数
        private Point _dragStartPoint;
        private bool _isDragging = false;
        private Button _addTabButton;

        // ヘルパークラス
        private TabBarWindowPositioner _positioner;
        private TabBarWindowContextMenuBuilder _contextMenuBuilder;
        private TabBarWindowDragDropHandler _dragDropHandler;

        public TabBarWindow()
        {
            InitializeComponent();
            Loaded += TabBarWindow_Loaded;
            PreviewDragEnter += TabBarWindow_DragEnter;
            PreviewDragOver += TabBarWindow_DragOver;
            PreviewDrop += TabBarWindow_Drop;
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

            // テーマ適用
            ApplyTheme();
            ThemeManager.Instance.ThemeChanged += ThemeManager_ThemeChanged;

            TabBarViewModel vm = GetVM();
            if (vm != null)
            {
                SetupTabsWithAddButton(vm);
                RegisterLocationHook(vm.ExplorerHwnd);
            }

            _positionTimer = new DispatcherTimer();
            _positionTimer.Interval = TimeSpan.FromMilliseconds(50);
            _positionTimer.Tick += PositionTimer_Tick;
            _positionTimer.Start();

            _syncTimer = new DispatcherTimer();
            _syncTimer.Interval = TimeSpan.FromMilliseconds(300);
            _syncTimer.Tick += SyncTimer_Tick;
            _syncTimer.Start();

            UpdatePosition();
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
            _addTabButton.ToolTip = "新しいタブ (フォルダを選択)";
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
                helper.Owner = vm.ExplorerHwnd;
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
            UpdatePosition();
        }

        private TabBarViewModel GetVM()
        {
            return DataContext as TabBarViewModel;
        }

        // ====== エクスプローラー状態チェック ======

        public bool IsExplorerAlive()
        {
            TabBarViewModel vm = GetVM();
            if (vm == null || _positioner == null) return false;
            return _positioner.IsExplorerAlive(vm.ExplorerHwnd);
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
                WindowInteropHelper helper = new WindowInteropHelper(this);
                helper.Owner = explorerHwnd;
                RegisterLocationHook(explorerHwnd);
                UpdatePosition();
            }
        }

        private bool IsExplorerMinimized()
        {
            TabBarViewModel vm = GetVM();
            if (vm == null || _positioner == null) return true;
            return _positioner.IsExplorerMinimized(vm.ExplorerHwnd);
        }

        private bool IsExplorerOrSelfForeground()
        {
            TabBarViewModel vm = GetVM();
            if (vm == null || _positioner == null) return false;
            return _positioner.IsExplorerOrSelfForeground(vm.ExplorerHwnd, _myHwnd);
        }

        // ====== 位置とタイマー ======

        private void UpdatePosition()
        {
            TabBarViewModel vm = GetVM();
            if (vm == null || _positioner == null) return;
            _positioner.UpdatePosition(vm.ExplorerHwnd, vm);
        }

        private void PositionTimer_Tick(object sender, EventArgs e)
        {
            if (!IsExplorerAlive())
            {
                StopTimers();
                Close();
                return;
            }
            UpdatePosition();
        }

        private async void SyncTimer_Tick(object sender, EventArgs e)
        {
            TabBarViewModel vm = GetVM();
            if (vm != null)
            {
                try
                {
                    await vm.SyncWithExplorerAsync();
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("TabBarWindow", "SyncWithExplorerAsync failed.", ex);
                }
            }
        }

        private void StopTimers()
        {
            if (_positionTimer != null)
            {
                _positionTimer.Tick -= PositionTimer_Tick;
                _positionTimer.Stop();
                _positionTimer = null;
            }
            if (_syncTimer != null)
            {
                _syncTimer.Tick -= SyncTimer_Tick;
                _syncTimer.Stop();
                _syncTimer = null;
            }
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
            UnregisterLocationHook();
            StopTimers();
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
                title = "タブに追加するフォルダを選択してください";
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
                if (!string.IsNullOrEmpty(tab.Path) && !_explorerService.IsTabPathCurrentlyAvailable(tab.Path))
                {
                    vm.CloseTab(tab);
                    ReturnFocusToExplorer();
                    return;
                }

                vm.SelectTab(tab);
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

        private void UnregisterLocationHook()
        {
            _trackedExplorerHwnd = IntPtr.Zero;
            if (_locationHook != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.UnhookWinEvent(_locationHook);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("TabBarWindow", "Failed to unhook location event hook.", ex);
                }
                _locationHook = IntPtr.Zero;
                _locationEventCallback = null;
            }
        }

        private void RegisterLocationHook(IntPtr explorerHwnd)
        {
            UnregisterLocationHook();
            if (explorerHwnd == IntPtr.Zero) return;
            _trackedExplorerHwnd = explorerHwnd;

            try
            {
                uint processId;
                NativeMethods.GetWindowThreadProcessId(explorerHwnd, out processId);
                if (processId == 0) return;

                _locationEventCallback = LocationEventCallback;
                _locationHook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                    NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                    IntPtr.Zero,
                    _locationEventCallback,
                    processId,
                    0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindow", "Failed to register location event hook.", ex);
            }
        }

        private void LocationEventCallback(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (!ShouldHandleLocationChangeEvent(eventType, hwnd, idObject, _trackedExplorerHwnd)) return;
                if (Dispatcher == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdatePosition();
                }));
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindow", "Error in LocationEventCallback.", ex);
            }
        }

        internal static bool ShouldHandleLocationChangeEvent(uint eventType, IntPtr hwnd, int idObject, IntPtr trackedExplorerHwnd)
        {
            return eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE &&
                   idObject == 0 &&
                   hwnd != IntPtr.Zero &&
                   hwnd == trackedExplorerHwnd;
        }
    }
}
