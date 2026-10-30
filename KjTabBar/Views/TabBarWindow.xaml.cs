using System;
using System.Threading.Tasks;
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
        internal Action<TabBarViewModel> PersistTabState { get; set; }
        internal ExplorerWindowTrackingState WindowTrackingState { get; set; }
        internal ExplorerHostSwitchCoordinator ExplorerHostSwitchCoordinator { get; set; }
        public IntPtr ExplorerHwnd { get; private set; }
        private IExplorerService _explorerService => ExplorerService;

        private IntPtr _myHwnd;
        private IntPtr _pendingOwnerExplorerHwnd;

        // ドラッグ用変数
        private Point _dragStartPoint;
        private FrameworkElement _dragSourceElement;
        private bool _isDragging = false;
        private Button _addTabButton;
        private UIElement _explorerMoveCapture;
        private IntPtr _explorerMoveHwnd;
        private NativeMethods.POINT _explorerMoveOrigin;
        private NativeMethods.RECT _explorerMoveRect;
        private bool _explorerMoveStarted;

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
            PreviewDragLeave += TabBarWindow_DragLeave;
            DataContextChanged += TabBarWindow_DataContextChanged;
        }

        private void TabBarWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            TabBarViewModel vm = e.NewValue as TabBarViewModel;
            if (vm != null)
            {
                ExplorerHwnd = vm.ExplorerHwnd;
            }
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
                CloseWithDiagnosticTiming);

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

        internal bool IsPointOverAbsorbZone(NativeMethods.POINT screenPoint, IntPtr draggedWindow)
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
                   screenPoint.Y < bottomRight.Y && IsDropTargetInFront(draggedWindow, screenPoint);
        }

        internal bool IsDropTargetInFront(IntPtr draggedWindow, NativeMethods.POINT? dropPoint = null)
        {
            if (!IsLoaded || !IsVisible) return false;
            NativeMethods.POINT point;
            if (dropPoint.HasValue) point = dropPoint.Value;
            else if (!NativeMethods.GetCursorPos(out point)) return false;
            IntPtr target = new WindowInteropHelper(this).Handle;
            return IsDropTargetInFront(target, draggedWindow,
                hwnd => NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDPREV),
                NativeMethods.IsBlockingDropWindow, ExplorerHwnd, hwnd =>
                {
                    NativeMethods.RECT rect;
                    return !NativeMethods.GetWindowRect(hwnd, out rect) ||
                        (point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom);
                });
        }

        internal static bool IsDropTargetInFront(IntPtr target, IntPtr draggedWindow,
            Func<IntPtr, IntPtr> getPreviousWindow, Func<IntPtr, bool> isBlockingWindow, IntPtr explorerHost = default(IntPtr),
            Func<IntPtr, bool> coversDropPoint = null)
        {
            if (target == IntPtr.Zero) return false;
            System.Collections.Generic.HashSet<IntPtr> visited = new System.Collections.Generic.HashSet<IntPtr>();
            visited.Add(target);
            for (IntPtr current = getPreviousWindow(target); current != IntPtr.Zero; current = getPreviousWindow(current))
            {
                // Window order can change during enumeration; reject an inconsistent snapshot.
                if (!visited.Add(current)) return false;
                if (current != draggedWindow && current != explorerHost && isBlockingWindow(current) &&
                    (coversDropPoint == null || coversDropPoint(current))) return false;
            }
            return true;
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
            ExplorerHwnd = explorerHwnd;

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

        internal void HandleExplorerForegroundChanged(IntPtr hwnd)
        {
            if (IsLoaded && _runtimeCoordinator != null)
                _runtimeCoordinator.HandleForegroundChanged(hwnd, NativeMethods.GetForegroundWindow());
        }

        private void UpdatePosition()
        {
            TabBarViewModel vm = GetVM();
            if (vm == null || _positioner == null) return;
            System.Diagnostics.Stopwatch positionTimer = AppLogger.StartDiagnosticTiming();
            _positioner.UpdatePosition(vm.ExplorerHwnd, vm);
            ApplyPendingOwnerWindowIfReady();
            if (positionTimer != null && positionTimer.ElapsedMilliseconds >= 100)
                AppLogger.LogDiagnosticTiming("Position.Completed", vm.ExplorerHwnd, positionTimer);
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

        internal void CloseWithDiagnosticTiming()
        {
            IntPtr explorerHwnd = ExplorerHwnd;
            System.Diagnostics.Stopwatch timer = AppLogger.StartDiagnosticTiming();
            AppLogger.LogDiagnosticTiming("CloseCall.Begin", explorerHwnd, timer);
            try
            {
                Close();
            }
            finally
            {
                AppLogger.LogDiagnosticTiming("CloseCall.Returned", explorerHwnd, timer);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            System.Diagnostics.Stopwatch closeTimer = AppLogger.StartDiagnosticTiming();
            AppLogger.LogDiagnosticTiming("Close.Begin", ExplorerHwnd, closeTimer);
            Loaded -= TabBarWindow_Loaded;
            PreviewDragEnter -= TabBarWindow_DragEnter;
            PreviewDragOver -= TabBarWindow_DragOver;
            PreviewDrop -= TabBarWindow_Drop;
            PreviewDragLeave -= TabBarWindow_DragLeave;
            ThemeManager.Instance.ThemeChanged -= ThemeManager_ThemeChanged;
            PersistCurrentTabState(GetVM(), PersistTabState);
            AppLogger.LogDiagnosticTiming("Close.Persisted", ExplorerHwnd, closeTimer);
            RememberRecentClosedManagedExplorerRect();

            if (WindowTrackingState != null && ExplorerHwnd != IntPtr.Zero)
            {
                WindowTrackingState.CloseParkedExplorerOrigin(ExplorerHwnd);
            AppLogger.LogDiagnosticTiming("Close.ParkedOrigin", ExplorerHwnd, closeTimer);
            }

            DisposeRuntimeCoordinator();
            AppLogger.LogDiagnosticTiming("Close.RuntimeDisposed", ExplorerHwnd, closeTimer);
            DetachDynamicUiResources();
            AppLogger.LogDiagnosticTiming("Close.ResourcesDetached", ExplorerHwnd, closeTimer);
            IDisposable disposableVm = DataContext as IDisposable;
            if (disposableVm != null)
            {
                disposableVm.Dispose();
            AppLogger.LogDiagnosticTiming("Close.ViewModelDisposed", ExplorerHwnd, closeTimer);
            }
            DataContext = null;
            base.OnClosed(e);
            AppLogger.LogDiagnosticTiming("Close.Completed", ExplorerHwnd, closeTimer);
        }

        internal static void PersistCurrentTabState(TabBarViewModel viewModel, Action<TabBarViewModel> persistTabState)
        {
            if (viewModel == null || persistTabState == null)
            {
                return;
            }

            try
            {
                persistTabState(viewModel);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindow", "Failed to persist tab state while closing the tab bar window.", ex);
            }
        }

        private void RememberRecentClosedManagedExplorerRect()
        {
            if (WindowTrackingState == null || _positioner == null)
            {
                return;
            }

            NativeMethods.RECT? lastKnownExplorerRect = _positioner.LastKnownExplorerWindowRect;
            if (!lastKnownExplorerRect.HasValue ||
                lastKnownExplorerRect.Value.Width <= 0 ||
                lastKnownExplorerRect.Value.Height <= 0)
            {
                return;
            }

            WindowTrackingState.RememberRecentClosedManagedExplorerRect(lastKnownExplorerRect.Value, DateTime.UtcNow,
                _positioner.LastKnownExplorerWindowPlacement);
        }

        // ====== イベントハンドラ ======

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            _ = ObserveEventTaskAsync(AddTab_ClickAsync(sender, e), "AddTab_Click");
        }

        private async Task AddTab_ClickAsync(object sender, RoutedEventArgs e)
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
                try
                {
                    ExplorerHostSwitchCoordinator coordinator = ExplorerHostSwitchCoordinator;
                    await vm.InsertTabWithPathAsync(selectedPath, vm.Tabs.Count,
                        coordinator != null ? new Func<string, Task<bool>>(path => coordinator.PrepareForPathAsync(vm, path, vm.IsPreparedTabOperationCurrent)) : null,
                        coordinator != null ? new Action(coordinator.CompletePendingReveal) : null);
                    if (PersistTabState != null) PersistTabState(vm);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("TabBarWindow", "Failed to add a tab.", ex);
                }
            }

            ReturnFocusToExplorer();
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            _ = ObserveEventTaskAsync(CloseTab_ClickAsync(sender, e), "CloseTab_Click");
        }

        private async Task CloseTab_ClickAsync(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            Button button = (Button)sender;
            TabItemViewModel tab = (TabItemViewModel)button.DataContext;
            TabBarViewModel vm = GetVM();
            if (vm != null)
            {
                await CloseTabsAsync(vm, tab);
            }
            ReturnFocusToExplorer();
            e.Handled = true;
        }

        private void Tab_Click(object sender, MouseButtonEventArgs e)
        {
            EndTabDragCapture();
            _ = ObserveEventTaskAsync(Tab_ClickAsync(sender, e), "Tab_Click");
        }

        private async Task Tab_ClickAsync(object sender, MouseButtonEventArgs e)
        {
            FrameworkElement element = (FrameworkElement)sender;
            TabItemViewModel tab = (TabItemViewModel)element.DataContext;
            TabBarViewModel vm = GetVM();
            if (vm != null && tab != null)
            {
                try
                {
                    ExplorerHostSwitchCoordinator coordinator = ExplorerHostSwitchCoordinator;
                    await vm.SelectTabAsync(tab,
                        coordinator != null ? new Func<string, Task<bool>>(path => coordinator.PrepareForPathAsync(vm, path, vm.IsPreparedTabOperationCurrent)) : null,
                        coordinator != null ? new Action(coordinator.CompletePendingReveal) : null);

                    if (PersistTabState != null)
                    {
                        PersistTabState(vm);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("TabBarWindow", "Tab_Click failed while selecting a tab.", ex);
                }
            }
            ReturnFocusToExplorer();
            e.Handled = true;
        }

        internal async Task TransferTabListAsync(TabBarViewModel vm, bool importing)
        {
            if (vm == null || vm.IsDisposed || vm.IsTabOperationPending) return;
            string title = TryFindResource(importing ? "MenuImportTabs" : "MenuExportTabs") as string;
            string filter = TryFindResource("TabListFileFilter") as string ?? "Text files (*.txt)|*.txt";
            try
            {
                if (importing)
                {
                    Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = title, Filter = filter, DefaultExt = ".txt", Multiselect = false
                    };
                    if (dialog.ShowDialog(this) != true) return;
                    TabListFile list = await Task.Run(() => TabListFile.Read(dialog.FileName));
                    ExplorerHostSwitchCoordinator coordinator = ExplorerHostSwitchCoordinator;
                    bool imported = await vm.ImportTabsAsync(list,
                        coordinator != null ? new Func<string, Task<bool>>(path => coordinator.PrepareForPathAsync(vm, path, vm.IsPreparedTabOperationCurrent)) : null,
                        coordinator != null ? new Action(coordinator.CompletePendingReveal) : null,
                        () => MessageBox.Show(this,
                            string.Format(TryFindResource("TabListImportLimitExceeded") as string ?? "The tab limit has been exceeded. Only the first {0} tabs will be imported.", TabBarViewModel.MaxTabCount),
                            title ?? "KjTabBar", MessageBoxButton.OK, MessageBoxImage.Warning));
                    if (imported && PersistTabState != null) PersistTabState(vm);
                }
                else
                {
                    Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = title, Filter = filter, DefaultExt = ".txt", AddExtension = true,
                        FileName = "tabs.txt", OverwritePrompt = true
                    };
                    if (dialog.ShowDialog(this) != true || vm.IsDisposed || vm.IsTabOperationPending) return;
                    string[] paths = new string[vm.Tabs.Count];
                    for (int i = 0; i < paths.Length; i++) paths[i] = vm.Tabs[i].Path;
                    int activeIndex = vm.Tabs.IndexOf(vm.ActiveTab);
                    await FileOperationTracker.Shared.RunAsync(() => TabListFile.Write(dialog.FileName, paths, activeIndex));
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindow", "Tab list transfer failed.", ex);
                MessageBox.Show(this, TryFindResource("TabListTransferError") as string ?? "Unable to read or write the tab list. Check the file format and file permissions.",
                    title ?? "KjTabBar", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { ReturnFocusToExplorer(); }
        }

        internal async Task DuplicateTabAsync(TabBarViewModel vm, TabItemViewModel tab)
        {
            if (vm == null || tab == null) return;
            try
            {
                ExplorerHostSwitchCoordinator coordinator = ExplorerHostSwitchCoordinator;
                await vm.DuplicateTabAsync(tab,
                    coordinator != null ? new Func<string, Task<bool>>(path => coordinator.PrepareForPathAsync(vm, path, vm.IsPreparedTabOperationCurrent)) : null,
                    coordinator != null ? new Action(coordinator.CompletePendingReveal) : null);
                if (PersistTabState != null) PersistTabState(vm);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindow", "Failed to duplicate a tab.", ex);
            }
            ReturnFocusToExplorer();
        }

        internal async Task CloseTabsAsync(TabBarViewModel vm, TabItemViewModel tab, int direction = 0)
        {
            if (vm == null || tab == null) return;
            int index = vm.Tabs.IndexOf(tab);
            if (index < 0) return;
            int startIndex = direction < 0 ? 0 : direction > 0 ? index + 1 : index;
            int count = direction < 0 ? index : direction > 0 ? vm.Tabs.Count - index - 1 : 1;
            try
            {
                ExplorerHostSwitchCoordinator coordinator = ExplorerHostSwitchCoordinator;
                await vm.CloseTabsAsync(startIndex, count,
                    coordinator != null ? new Func<string, Task<bool>>(path => coordinator.PrepareForPathAsync(vm, path, vm.IsPreparedTabOperationCurrent)) : null,
                    coordinator != null ? new Action(coordinator.CompletePendingReveal) : null);
                if (PersistTabState != null) PersistTabState(vm);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindow", "Failed to close tabs.", ex);
            }
            ReturnFocusToExplorer();
        }

        internal async Task ReopenClosedTabAsync(TabBarViewModel vm, ClosedTabInfo selectedItem = null)
        {
            if (vm == null) return;
            try
            {
                ExplorerHostSwitchCoordinator coordinator = ExplorerHostSwitchCoordinator;
                await vm.ReopenClosedTabAsync(
                    coordinator != null ? new Func<string, Task<bool>>(path => coordinator.PrepareForPathAsync(vm, path, vm.IsPreparedTabOperationCurrent)) : null,
                    coordinator != null ? new Action<Action>(action => ExecuteTabSelectionWithPendingReveal(action, coordinator.CompletePendingReveal)) : null, selectedItem);
                if (PersistTabState != null) PersistTabState(vm);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindow", "Failed to reopen closed tabs.", ex);
            }
            ReturnFocusToExplorer();
        }

        internal static void ExecuteTabSelectionWithPendingReveal(Action selectTabAction, Action completePendingReveal)
        {
            try
            {
                if (selectTabAction != null)
                {
                    selectTabAction();
                }
            }
            finally
            {
                if (completePendingReveal != null)
                {
                    completePendingReveal();
                }
            }
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

        internal static bool IsWindowMoveBackground(DependencyObject source, DependencyObject background)
        {
            DependencyObject current = source;
            while (current != null && current != background)
            {
                FrameworkElement element = current as FrameworkElement;
                if (current is System.Windows.Controls.Primitives.ButtonBase ||
                    (element != null && element.DataContext is TabItemViewModel))
                {
                    return false;
                }
                current = current is System.Windows.Media.Visual
                    ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }
            return current != null && current == background;
        }

        private void Background_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || e.ClickCount != 1 ||
                !IsWindowMoveBackground(e.OriginalSource as DependencyObject, sender as DependencyObject)) return;
            TabBarViewModel vm = GetVM();
            UIElement capture = sender as UIElement;
            NativeMethods.POINT cursor;
            NativeMethods.RECT rect;
            if (vm == null || capture == null || !NativeMethods.IsWindow(vm.ExplorerHwnd) ||
                !NativeMethods.GetCursorPos(out cursor) || !NativeMethods.GetWindowRect(vm.ExplorerHwnd, out rect)) return;

            // Raise the host for this explicit press, then keep drag input on the bar.
            NativeMethods.ForceSetForegroundWindow(vm.ExplorerHwnd);
            Activate();
            if (!capture.CaptureMouse()) return;
            _explorerMoveCapture = capture;
            _explorerMoveHwnd = vm.ExplorerHwnd;
            _explorerMoveOrigin = cursor;
            _explorerMoveRect = rect;
            _explorerMoveStarted = false;
            e.Handled = true;
        }

        private void Background_MouseMove(object sender, MouseEventArgs e)
        {
            if (_explorerMoveCapture == null) return;
            if (e.LeftButton != MouseButtonState.Pressed || !NativeMethods.IsWindow(_explorerMoveHwnd))
            {
                EndExplorerMove(false);
                return;
            }
            NativeMethods.POINT cursor;
            if (!NativeMethods.GetCursorPos(out cursor)) return;
            int dx = cursor.X - _explorerMoveOrigin.X;
            int dy = cursor.Y - _explorerMoveOrigin.Y;
            if (!_explorerMoveStarted)
            {
                System.Windows.DpiScale dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                if (!ShouldStartExplorerMove(dx, dy, dpi.DpiScaleX, dpi.DpiScaleY)) return;
                NativeMethods.WINDOWPLACEMENT placement = new NativeMethods.WINDOWPLACEMENT();
                placement.length = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.WINDOWPLACEMENT));
                if (NativeMethods.GetWindowPlacement(_explorerMoveHwnd, ref placement) && placement.showCmd == 3)
                {
                    NativeMethods.ShowWindow(_explorerMoveHwnd, 4); // SW_SHOWNOACTIVATE
                    NativeMethods.RECT restored;
                    if (!NativeMethods.GetWindowRect(_explorerMoveHwnd, out restored)) { EndExplorerMove(false); return; }
                    double fraction = Math.Max(0, Math.Min(1, (double)(_explorerMoveOrigin.X - _explorerMoveRect.Left) / Math.Max(1, _explorerMoveRect.Width)));
                    int offsetY = _explorerMoveOrigin.Y - _explorerMoveRect.Top;
                    _explorerMoveRect = restored;
                    _explorerMoveRect.Left = cursor.X - (int)Math.Round(restored.Width * fraction);
                    _explorerMoveRect.Top = cursor.Y - offsetY;
                    _explorerMoveOrigin = cursor;
                    dx = dy = 0;
                }
                _explorerMoveStarted = true;
            }
            NativeMethods.SetWindowPos(_explorerMoveHwnd, IntPtr.Zero,
                _explorerMoveRect.Left + dx, _explorerMoveRect.Top + dy, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            UpdatePosition();
            e.Handled = true;
        }

        internal static bool ShouldStartExplorerMove(int dx, int dy, double scaleX, double scaleY)
        {
            return Math.Abs(dx) >= SystemParameters.MinimumHorizontalDragDistance * scaleX ||
                Math.Abs(dy) >= SystemParameters.MinimumVerticalDragDistance * scaleY;
        }

        private void Background_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_explorerMoveCapture == null) return;
            EndExplorerMove(true);
            e.Handled = true;
        }

        private void Background_LostMouseCapture(object sender, MouseEventArgs e)
        {
            EndExplorerMove(false);
        }

        private void EndExplorerMove(bool activateExplorer)
        {
            UIElement capture = _explorerMoveCapture;
            IntPtr hwnd = _explorerMoveHwnd;
            _explorerMoveCapture = null;
            _explorerMoveHwnd = IntPtr.Zero;
            _explorerMoveStarted = false;
            if (capture != null && capture.IsMouseCaptured) capture.ReleaseMouseCapture();
            if (activateExplorer && hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd))
                NativeMethods.ForceSetForegroundWindow(hwnd);
        }

        private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            EndTabDragCapture();
            FrameworkElement element = sender as FrameworkElement;
            if (element == null || _isDragging) return;
            DependencyObject source = e.OriginalSource as DependencyObject;
            while (source != null && source != element)
            {
                if (source is System.Windows.Controls.Primitives.ButtonBase) return;
                source = source is System.Windows.Media.Visual
                    ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }
            if (!element.CaptureMouse()) return;
            _dragSourceElement = element;
            _dragStartPoint = e.GetPosition(element);
        }

        private void EndTabDragCapture()
        {
            FrameworkElement source = _dragSourceElement;
            _dragSourceElement = null;
            if (source != null && source.IsMouseCaptured) source.ReleaseMouseCapture();
        }

        private void Tab_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (ReferenceEquals(sender, _dragSourceElement)) EndTabDragCapture();
        }

        private void Tab_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            FrameworkElement source = _dragSourceElement;
            if (source == null) return;
            bool inside = new Rect(new Point(), source.RenderSize).Contains(e.GetPosition(source));
            EndTabDragCapture();
            if (!inside) e.Handled = true;
        }

        private void Tab_MouseMove(object sender, MouseEventArgs e)
        {
            _ = ObserveEventTaskAsync(Tab_MouseMoveAsync(sender, e), "Tab_MouseMove");
        }

        private async Task ObserveEventTaskAsync(Task operation, string operationName)
        {
            try
            {
                await operation;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindow", operationName + " failed.", ex);
            }
        }

        private async Task Tab_MouseMoveAsync(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndTabDragCapture();
                return;
            }
            if (_dragSourceElement != null && !_isDragging)
            {
                FrameworkElement element = _dragSourceElement;
                Point position = e.GetPosition(element);
                if (ShouldStartTabDrag(_dragStartPoint, position, element.RenderSize))
                {
                    _isDragging = true;
                    EndTabDragCapture();
                    TabItemViewModel tab = (TabItemViewModel)element.DataContext;
                    try
                    {
                        if (tab != null)
                        {
                            string draggedPath = tab.Path;
                            DragDropEffects effect;
                            bool cancelled;
                            ShowDraggedTabOutline(element);
                            element.GiveFeedback += Tab_GiveFeedback;
                            try
                            {
                                effect = RunTabDrag(element, () => DragDrop.DoDragDrop(element, tab, DragDropEffects.Move), out cancelled);
                            }
                            finally
                            {
                                element.GiveFeedback -= Tab_GiveFeedback;
                                DraggedTabOutline.Visibility = Visibility.Collapsed;
                                TabInsertionBar.Visibility = Visibility.Collapsed;
                            }
                            if (!cancelled) await OpenDraggedTabInNewWindowIfDroppedOutsideAsync(effect, tab, draggedPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("TabBarWindow", "Failed to detach a tab.", ex);
                    }
                    finally { _isDragging = false; }
                }
            }
        }

        internal static DragDropEffects RunTabDrag(FrameworkElement element, Func<DragDropEffects> drag, out bool cancelled)
        {
            bool cancellationObserved = false;
            QueryContinueDragEventHandler handler = (sender, e) =>
            {
                if (e.EscapePressed || e.Action == DragAction.Cancel) cancellationObserved = true;
            };
            element.AddHandler(DragDrop.QueryContinueDragEvent, handler, true);
            try
            {
                return drag();
            }
            finally
            {
                element.RemoveHandler(DragDrop.QueryContinueDragEvent, handler);
                cancelled = cancellationObserved;
            }
        }

        internal static bool ShouldStartTabDrag(Point start, Point position, Size tabSize)
        {
            if (new Rect(new Point(), tabSize).Contains(position)) return false;
            return Math.Abs(position.X - start.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(position.Y - start.Y) > SystemParameters.MinimumVerticalDragDistance;
        }

        private async Task OpenDraggedTabInNewWindowIfDroppedOutsideAsync(DragDropEffects effect, TabItemViewModel tab, string draggedPath)
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

            NativeMethods.RECT explorerRect;
            if (!NativeMethods.GetWindowRect(ExplorerHwnd, out explorerRect) ||
                !TabExternalDragOpenDecider.ShouldOpenInNewWindow(effect, draggedPath, cursorPoint, windowRect, explorerRect))
            {
                return;
            }

            ExplorerHostSwitchCoordinator coordinator = ExplorerHostSwitchCoordinator;
            TabDetachResult result = await TabExternalDragOpenDecider.TryOpenInNewWindowAndCloseSourceTabAsync(
                effect,
                tab,
                draggedPath,
                vm,
                OpenPathInNewWindow,
                cursorPoint,
                windowRect,
                coordinator != null ? new Func<string, Task<bool>>(path => coordinator.PrepareForPathAsync(vm, path, vm.IsPreparedTabOperationCurrent)) : null,
                coordinator != null ? new Action(coordinator.CompletePendingReveal) : null);
            if (result == TabDetachResult.ClosePending)
                result = await TabExternalDragOpenDecider.CompletePendingCloseAsync(vm, tab);
            if (vm.IsDisposed) return;
            if (result == TabDetachResult.WindowOpenedSourceRetained)
                MessageBox.Show(this, TryFindResource("DetachSourceRetainedMessage") as string ?? "The new window opened, but the original tab was kept because switching folders did not complete.",
                    "KjTabBar", MessageBoxButton.OK, MessageBoxImage.Information);
            if (result != TabDetachResult.NotOpened && PersistTabState != null) PersistTabState(vm);
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

        private void ShowDraggedTabOutline(FrameworkElement element)
        {
            Point position = element.TranslatePoint(new Point(), TabDragOverlay);
            Canvas.SetLeft(DraggedTabOutline, position.X);
            Canvas.SetTop(DraggedTabOutline, position.Y);
            DraggedTabOutline.Width = element.ActualWidth;
            DraggedTabOutline.Height = element.ActualHeight;
            DraggedTabOutline.Visibility = Visibility.Visible;
        }

        private void Tab_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            // An external detach uses None until the OLE loop ends. Its default
            // cursor would incorrectly imply that releasing the tab is forbidden.
            e.UseDefaultCursors = false;
            Mouse.SetCursor(Cursors.Arrow);
            e.Handled = true;
        }

        private void TabBarWindow_DragLeave(object sender, DragEventArgs e)
        {
            TabInsertionBar.Visibility = Visibility.Collapsed;
        }

        private void UpdateTabInsertionBar(DragEventArgs e)
        {
            TabInsertionBar.Visibility = Visibility.Collapsed;
            TabBarViewModel vm = GetVM();
            if (e.Effects != DragDropEffects.Move || vm == null ||
                !e.Data.GetDataPresent(typeof(TabItemViewModel))) return;
            TabItemViewModel tab = e.Data.GetData(typeof(TabItemViewModel)) as TabItemViewModel;
            if (tab == null || !vm.Tabs.Contains(tab)) return;

            int index = TabBarWindowDragDropHandler.GetDropIndex(TabItemsControl, e);
            int targetIndex = Math.Min(index, vm.Tabs.Count - 1);
            FrameworkElement target = TabItemsControl.ItemContainerGenerator.ContainerFromIndex(targetIndex) as FrameworkElement;
            if (target == null) return;
            Point position = target.TranslatePoint(new Point(), TabDragOverlay);
            // Use the same insertion index as Drop, including wrapped rows and the end.
            double x = position.X + (index >= vm.Tabs.Count ? target.ActualWidth : 0);
            Canvas.SetLeft(TabInsertionBar, x - TabInsertionBar.Width / 2);
            Canvas.SetTop(TabInsertionBar, position.Y);
            TabInsertionBar.Height = target.ActualHeight;
            TabInsertionBar.Visibility = Visibility.Visible;
        }

        private void TabBarWindow_DragEnter(object sender, DragEventArgs e)
        {
            if (_dragDropHandler != null)
            {
                _dragDropHandler.HandleDragEnter(TabItemsControl, e);
                UpdateTabInsertionBar(e);
            }
        }

        private void TabBarWindow_DragOver(object sender, DragEventArgs e)
        {
            if (_dragDropHandler != null)
            {
                _dragDropHandler.HandleDragOver(TabItemsControl, e);
                UpdateTabInsertionBar(e);
            }
        }

        private void TabBarWindow_Drop(object sender, DragEventArgs e)
        {
            TabInsertionBar.Visibility = Visibility.Collapsed;
            if (_dragDropHandler != null)
            {
                TabBarViewModel vm = GetVM();
                _ = _dragDropHandler.HandleDrop(TabItemsControl, e, vm, ReturnFocusToExplorer);
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
                string error;
                if (!TryChangeFontSize(settings, newSize, out error))
                {
                    MessageBox.Show(this,
                        (TryFindResource("SaveSettingsErrorMessage") as string ?? "Failed to save settings.") +
                        (string.IsNullOrEmpty(error) ? string.Empty : "\n\n" + error),
                        TryFindResource("SaveSettingsErrorTitle") as string ?? "Save Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            ReturnFocusToExplorer();
        }

        internal static bool TryChangeFontSize(IUserSettings settings, double size, out string error)
        {
            double previous = settings.FontSize;
            bool saved = false;
            try
            {
                settings.FontSize = UserSettings.NormalizeFontSize(size);
                saved = settings.TrySave(out error);
                return saved;
            }
            finally { if (!saved) settings.FontSize = previous; }
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
            // A registered tab bar may still be hidden while its host is restored.
            return vm != null && NativeMethods.IsWindow(vm.ExplorerHwnd);
        }

        private IntPtr GetExplorerOwner(IntPtr explorerHwnd)
        {
            TabBarViewModel vm = GetVM();
            bool isControlPanel = ExplorerHostSwitchCoordinator != null &&
                ExplorerHostSwitchCoordinator.CurrentHostIsControlPanel.HasValue
                ? ExplorerHostSwitchCoordinator.CurrentHostIsControlPanel.Value
                : vm != null && vm.ActiveTab != null && _explorerService.IsControlPanelPath(vm.ActiveTab.Path);
            AppLogger.LogDiagnostic("ExplorerOwner", string.Format(
                "explorer={0} controlPanel={1} requestedOwner={2}", explorerHwnd, isControlPanel,
                isControlPanel ? IntPtr.Zero : explorerHwnd));
            // A cross-process owner joins input handling during Control Panel shutdown.
            // Keep positioning independent to avoid the observed Explorer close failure.
            return isControlPanel ? IntPtr.Zero : explorerHwnd;
        }
        private void UpdateOwnerWindow(IntPtr explorerHwnd)
        {
            WindowInteropHelper helper = new WindowInteropHelper(this);
            if (explorerHwnd == IntPtr.Zero)
            {
                NativeMethods.SetWindowLongPtr(helper.Handle, NativeMethods.GWL_HWNDPARENT, IntPtr.Zero);
                _pendingOwnerExplorerHwnd = IntPtr.Zero;
                return;
            }

            if (NativeMethods.IsWindowVisible(explorerHwnd))
            {
                IntPtr ownerHwnd = GetExplorerOwner(explorerHwnd);
                NativeMethods.SetWindowLongPtr(helper.Handle, NativeMethods.GWL_HWNDPARENT, ownerHwnd);
                _pendingOwnerExplorerHwnd = IntPtr.Zero;
                AppLogger.LogInfo("TabBarWindow", string.Format("UpdateOwnerWindow applied Win32 owner={0}", ownerHwnd));

                NativeMethods.SetWindowPos(
                    helper.Handle,
                    IntPtr.Zero,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                return;
            }

            NativeMethods.SetWindowLongPtr(helper.Handle, NativeMethods.GWL_HWNDPARENT, IntPtr.Zero);
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
            IntPtr ownerHwnd = GetExplorerOwner(_pendingOwnerExplorerHwnd);
            NativeMethods.SetWindowLongPtr(helper.Handle, NativeMethods.GWL_HWNDPARENT, ownerHwnd);
            NativeMethods.ShowWindow(_myHwnd, NativeMethods.SW_SHOWNA);
            AppLogger.LogInfo("TabBarWindow", string.Format("ApplyPendingOwnerWindowIfReady applied Win32 owner={0}", ownerHwnd));
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


