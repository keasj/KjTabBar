using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.ViewModels;
using KjTabBar.Services;

namespace KjTabBar.Views
{
    public partial class TabBarWindow : Window
    {
        public Models.IExplorerService ExplorerService { get; set; }
        private Models.IExplorerService _explorerService => ExplorerService;
        private DispatcherTimer _positionTimer;
        private DispatcherTimer _syncTimer;
        private double _dpiScale = 1.0;
        private IntPtr _myHwnd;

        // ドラッグ用変数
        private Point _dragStartPoint;
        private bool _isDragging = false;
        private bool _wasRightDrag = false;
        private Button _addTabButton;

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
            SetToolWindowStyle();
            InitDpiScale();

            WindowInteropHelper helper = new WindowInteropHelper(this);
            _myHwnd = helper.Handle;

            // テーマ適用
            ApplyTheme();
            ThemeManager.Instance.ThemeChanged += ThemeManager_ThemeChanged;

            // Viewでは購読せずViewModelに任せる
            TabBarViewModel vm = GetVM();
            if (vm != null)
            {
                SetupTabsWithAddButton(vm);
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

            System.Windows.Data.CompositeCollection composite = new System.Windows.Data.CompositeCollection();

            System.Windows.Data.CollectionContainer cc = new System.Windows.Data.CollectionContainer();
            System.Windows.Data.BindingOperations.SetBinding(cc, System.Windows.Data.CollectionContainer.CollectionProperty, new System.Windows.Data.Binding("Tabs") { Source = vm });
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
                // エクスプローラーをOwnerに設定し、z-orderを自動管理。
                // Wn32 WM_DROPFILES方式ならOwnerでもドラッグ&ドロップ可能！
                helper.Owner = vm.ExplorerHwnd;
            }

            try
            {
                // ドラッグ&ドロップメッセージをUIPI経由で許可（念のため）
                NativeMethods.ChangeWindowMessageFilterEx(helper.Handle,
                    NativeMethods.WM_DROPFILES, NativeMethods.MSGFLT_ALLOW, IntPtr.Zero);
                NativeMethods.ChangeWindowMessageFilterEx(helper.Handle,
                    NativeMethods.WM_COPYGLOBALDATA, NativeMethods.MSGFLT_ALLOW, IntPtr.Zero);
                NativeMethods.ChangeWindowMessageFilterEx(helper.Handle,
                    NativeMethods.WM_COPYDATA, NativeMethods.MSGFLT_ALLOW, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindow", "Failed to enable drag-and-drop related window messages.", ex);
            }
        }

        private void InitDpiScale()
        {
            try
            {
                DpiScale dpi = VisualTreeHelper.GetDpi(this);
                _dpiScale = dpi.DpiScaleX;
            }
            catch
            {
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source != null)
                {
                    _dpiScale = source.CompositionTarget.TransformToDevice.M11;
                }
            }
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            _dpiScale = newDpi.DpiScaleX;
            // DPI 変更直後に位置を再計算し、ずれを最小化する
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
            if (vm == null) return false;
            return NativeMethods.IsWindow(vm.ExplorerHwnd);
        }

        private bool IsExplorerMinimized()
        {
            TabBarViewModel vm = GetVM();
            if (vm == null) return true;
            return NativeMethods.IsIconic(vm.ExplorerHwnd);
        }

        private bool IsExplorerOrSelfForeground()
        {
            TabBarViewModel vm = GetVM();
            if (vm == null) return false;
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            if (foreground == vm.ExplorerHwnd) return true;
            if (foreground == _myHwnd) return true;
            return false;
        }

        // ====== 位置とタイマー ======

        private void UpdatePosition()
        {
            TabBarViewModel vm = GetVM();
            if (vm == null) return;

            // エクスプローラーが存在しない or 最小化 → 非表示
            if (!NativeMethods.IsWindow(vm.ExplorerHwnd) || IsExplorerMinimized())
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
            NativeMethods.RECT contentRect = _explorerService.GetExplorerWindowRect(vm.ExplorerHwnd);
            if (contentRect.Width <= 0) return;

            double expectedLeft = contentRect.Left / _dpiScale;
            double expectedTop = contentRect.Top / _dpiScale - this.ActualHeight;
            double expectedWidth = contentRect.Width / _dpiScale;

            if (Math.Abs(this.Left - expectedLeft) > 0.1) Left = expectedLeft;
            if (Math.Abs(this.Top - expectedTop) > 0.1) Top = expectedTop;
            if (Math.Abs(this.Width - expectedWidth) > 0.1) Width = expectedWidth;
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

        private void SyncTimer_Tick(object sender, EventArgs e)
        {
            TabBarViewModel vm = GetVM();
            if (vm != null)
            {
                vm.SyncWithExplorer();
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

        // Removed manual UI generation methods

        // ====== イベントハンドラ ======

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            TabBarViewModel vm = GetVM();
            if (vm == null) return;
            object shellApp = null;
            object folderObj = null;
            object selfObj = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return;

                shellApp = Activator.CreateInstance(shellType);
                string title = TryFindResource("AddTabDialogDescription") as string;
                if (string.IsNullOrEmpty(title))
                {
                    title = "タブに追加するフォルダを選択してください";
                }

                // BrowseForFolder: 0x0040(BIF_NEWDIALOGSTYLE) | 0x0200(BIF_NONEWFOLDERBUTTON)
                folderObj = shellType.InvokeMember(
                    "BrowseForFolder",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    shellApp,
                    new object[] { 0, title, 0x0040 | 0x0200, 0 });
                if (folderObj != null)
                {
                    selfObj = folderObj.GetType().InvokeMember(
                        "Self",
                        System.Reflection.BindingFlags.GetProperty,
                        null,
                        folderObj,
                        null);
                    if (selfObj != null)
                    {
                        string selectedPath = selfObj.GetType().InvokeMember(
                            "Path",
                            System.Reflection.BindingFlags.GetProperty,
                            null,
                            selfObj,
                            null) as string;
                        if (string.IsNullOrEmpty(selectedPath))
                        {
                            try
                            {
                                selectedPath = selfObj.GetType().InvokeMember(
                                    "ExtendedProperty",
                                    System.Reflection.BindingFlags.InvokeMethod,
                                    null,
                                    selfObj,
                                    new object[] { "System.ParsingPath" }) as string;
                            }
                            catch
                            {
                            }
                        }
                        if (string.IsNullOrEmpty(selectedPath))
                        {
                            try
                            {
                                selectedPath = selfObj.GetType().InvokeMember(
                                    "ExtendedProperty",
                                    System.Reflection.BindingFlags.InvokeMethod,
                                    null,
                                    selfObj,
                                    new object[] { "System.ItemPathDisplay" }) as string;
                            }
                            catch
                            {
                            }
                        }
                        if (string.IsNullOrEmpty(selectedPath))
                        {
                            try
                            {
                                string name = selfObj.GetType().InvokeMember(
                                    "Name",
                                    System.Reflection.BindingFlags.GetProperty,
                                    null,
                                    selfObj,
                                    null) as string;
                                selectedPath = _explorerService.MapLocationNameToKnownShellPath(name);
                            }
                            catch
                            {
                            }
                        }

                        if (!string.IsNullOrEmpty(selectedPath))
                        {
                            selectedPath = _explorerService.NormalizeKnownPath(selectedPath);
                        }
                        if (!string.IsNullOrEmpty(selectedPath))
                        {
                            vm.InsertTabWithPath(selectedPath, vm.Tabs.Count, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindow", "Failed to open folder picker for add tab.", ex);
            }
            finally
            {
                if (selfObj != null && System.Runtime.InteropServices.Marshal.IsComObject(selfObj))
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(selfObj);
                }
                if (folderObj != null && System.Runtime.InteropServices.Marshal.IsComObject(folderObj))
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(folderObj);
                }
                if (shellApp != null && System.Runtime.InteropServices.Marshal.IsComObject(shellApp))
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(shellApp);
                }
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
                // パスが指定されており、かつナビゲート不可能な（存在しない）パスの場合はタブを閉じる
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
            if (vm == null) return;

            this.Activate();
            ContextMenu menu = new ContextMenu();
            ApplyFluentMenuStyle(menu);
            
            MenuItem duplicateItem = new MenuItem() { Header = TryFindResource("MenuDuplicateTab") as string ?? "タブの複製(&D)" };
            duplicateItem.Click += (s, ev) =>
            {
                vm.DuplicateTab(tabVM);
            };
            menu.Items.Add(duplicateItem);

            MenuItem openInNewWindowItem = new MenuItem() { Header = TryFindResource("MenuOpenNewWindow") as string ?? "別ウィンドウで開く(&N)" };
            openInNewWindowItem.Click += (s, ev) =>
            {
                string path = tabVM.Path;
                if (string.IsNullOrEmpty(path)) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                _explorerService.OpenInNewWindow(path);
            };
            menu.Items.Add(openInNewWindowItem);

            if (!string.IsNullOrEmpty(tabVM.Path))
            {
                MenuItem copyPathItem = new MenuItem() { Header = TryFindResource("MenuCopyPath") as string ?? "パスのコピー(&P)" };
                copyPathItem.Click += (s, ev) =>
                {
                    try { Clipboard.SetText(tabVM.Path); } catch (Exception ex) { AppLogger.LogError("TabBarWindow", "Failed to copy tab path to clipboard.", ex); }
                };
                menu.Items.Add(copyPathItem);
            }

            menu.Items.Add(new Separator());

            MenuItem closeItem = new MenuItem() { Header = TryFindResource("MenuCloseTab") as string ?? "タブを閉じる(&C)" };
            closeItem.Click += (s, ev) =>
            {
                vm.CloseTab(tabVM);
            };
            menu.Items.Add(closeItem);

            int tabIndex = vm.Tabs.IndexOf(tabVM);

            MenuItem closeToRightItem = new MenuItem() { Header = TryFindResource("MenuCloseTabsToRight") as string ?? "右側のタブを閉じる(&R)" };
            closeToRightItem.IsEnabled = (tabIndex >= 0 && tabIndex < vm.Tabs.Count - 1);
            closeToRightItem.Click += (s, ev) =>
            {
                vm.CloseTabsToRight(tabVM);
            };
            menu.Items.Add(closeToRightItem);

            MenuItem closeToLeftItem = new MenuItem() { Header = TryFindResource("MenuCloseTabsToLeft") as string ?? "左側のタブを閉じる(&L)" };
            closeToLeftItem.IsEnabled = (tabIndex > 0);
            closeToLeftItem.Click += (s, ev) =>
            {
                vm.CloseTabsToLeft(tabVM);
            };
            menu.Items.Add(closeToLeftItem);

            menu.Items.Add(new Separator());

            MenuItem reopenItem = new MenuItem() { Header = TryFindResource("MenuReopenClosedTab") as string ?? "閉じたタブを開く(&T)" };
            reopenItem.IsEnabled = vm.HasClosedTabs;
            reopenItem.Click += (s, ev) =>
            {
                vm.ReopenClosedTab();
            };
            menu.Items.Add(reopenItem);



            menu.Closed += (s, ev) => ReturnFocusToExplorer();

            menu.PlacementTarget = tabBd;
            menu.IsOpen = true;
            e.Handled = true;
        }


        private void Border_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Activate window first so the WPF popup doesn't immediately close due to lack of focus
            this.Activate();

            ContextMenu menu = new ContextMenu();
            ApplyFluentMenuStyle(menu);

            TabBarViewModel vm = GetVM();
            MenuItem reopenItem = new MenuItem() { Header = TryFindResource("MenuReopenClosedTab") as string ?? "閉じたタブを開く(&T)" };
            reopenItem.IsEnabled = (vm != null && vm.HasClosedTabs);
            reopenItem.Click += (s, ev) =>
            {
                if (vm != null) vm.ReopenClosedTab();
            };
            menu.Items.Add(reopenItem);

            menu.Items.Add(new Separator());

            MenuItem settingsItem = new MenuItem() { Header = TryFindResource("MenuSettings") as string ?? "設定..." };
            settingsItem.Click += (s, ev) =>
            {
                SettingsWindow w = new SettingsWindow();
                w.Owner = this;
                w.ShowDialog();
            };
            menu.Items.Add(settingsItem);
            menu.Closed += (s, ev) => ReturnFocusToExplorer();
            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
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
                        DragDrop.DoDragDrop(element, tab, DragDropEffects.Move);
                    }
                    _isDragging = false;
                }
            }
        }

        private int GetDropIndex(DragEventArgs e)
        {
            if (TabItemsControl == null) return 0;
            Point position = e.GetPosition(TabItemsControl);
            int index = 0;

            for (int i = 0; i < TabItemsControl.Items.Count; i++)
            {
                FrameworkElement fe = TabItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (fe is Button) break; // + button

                if (fe != null)
                {
                    Point childPos = fe.TranslatePoint(new Point(0, 0), TabItemsControl);
                    double top = childPos.Y;
                    double bottom = childPos.Y + fe.ActualHeight;
                    double midPoint = childPos.X + (fe.ActualWidth / 2);

                    // ドラッグ位置が要素の行より上にある場合は、現在の位置へ挿入
                    if (position.Y < top)
                    {
                        return index;
                    }

                    // ドラッグ位置が要素の行にある場合はX座標の半分より前か判定
                    if (position.Y <= bottom)
                    {
                        if (position.X < midPoint)
                        {
                            return index;
                        }
                    }
                }
                index++;
            }
            return index;
        }

        private void TabBarWindow_DragEnter(object sender, DragEventArgs e)
        {
            // Linkを優先し、タブとして挿入される操作であることを示す。
            if ((e.AllowedEffects & DragDropEffects.Link) != 0)
            {
                e.Effects = DragDropEffects.Link;
            }
            else if ((e.AllowedEffects & DragDropEffects.Copy) != 0)
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = e.AllowedEffects;
            }
            e.Handled = true;
        }

        private void TabBarWindow_DragOver(object sender, DragEventArgs e)
        {
            if ((e.KeyStates & DragDropKeyStates.RightMouseButton) == DragDropKeyStates.RightMouseButton)
                _wasRightDrag = true;
            else if ((e.KeyStates & DragDropKeyStates.LeftMouseButton) == DragDropKeyStates.LeftMouseButton)
                _wasRightDrag = false;

            if (e.Data.GetDataPresent(typeof(TabItemViewModel)))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                Point position = e.GetPosition(TabItemsControl);
                DependencyObject hit = VisualTreeHelper.HitTest(TabItemsControl, position)?.VisualHit;
                bool isOverValidTab = false;
                while (hit != null && hit != TabItemsControl)
                {
                    Border b = hit as Border;
                    if (b != null && b.DataContext is TabItemViewModel tabVM && !string.IsNullOrEmpty(tabVM.Path))
                    {
                        isOverValidTab = true;
                        break;
                    }
                    hit = VisualTreeHelper.GetParent(hit);
                }

                if (isOverValidTab)
                {
                    if ((e.KeyStates & DragDropKeyStates.ShiftKey) == DragDropKeyStates.ShiftKey)
                        e.Effects = DragDropEffects.Move;
                    else if ((e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey)
                        e.Effects = DragDropEffects.Copy;
                    else if ((e.KeyStates & DragDropKeyStates.RightMouseButton) == DragDropKeyStates.RightMouseButton)
                        e.Effects = DragDropEffects.Copy | DragDropEffects.Move; // 右ドラッグ
                    else
                        e.Effects = DragDropEffects.Copy; // デフォルトはCopy表示にする（OS標準と完全に一致させるには同一ドライブ判定が必要だが簡略化）
                }
                else
                {
                    // タブ追加（Link優先）として表示する
                    if ((e.AllowedEffects & DragDropEffects.Link) != 0)
                    {
                        e.Effects = DragDropEffects.Link;
                    }
                    else if ((e.AllowedEffects & DragDropEffects.Copy) != 0)
                    {
                        e.Effects = DragDropEffects.Copy;
                    }
                    else
                    {
                        e.Effects = e.AllowedEffects;
                    }
                }
            }

            e.Handled = true;
        }



        private string[] GetPathsFromDataObject(System.Windows.IDataObject data)
        {
            // まずFileDrop形式を試す（通常のファイル/フォルダ）
            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] filePaths = data.GetData(DataFormats.FileDrop) as string[];
                if (filePaths != null && filePaths.Length > 0) return filePaths;
            }

            // Shell IDList Array形式を解析（仮想フォルダ: PC、コントロールパネル等）
            if (data.GetDataPresent("Shell IDList Array"))
            {
                System.IO.MemoryStream ms = null;
                try
                {
                    ms = data.GetData("Shell IDList Array") as System.IO.MemoryStream;
                    if (ms != null)
                    {
                        byte[] bytes = ms.ToArray();
                        return ParseCIDA(bytes);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("TabBarWindow", "Failed to parse Shell IDList Array from data object.", ex);
                }
                finally
                {
                    if (ms != null) ms.Dispose();
                }
            }

            return null;
        }

        private bool IsShellNamespacePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string trimmed = path.Trim();
            if (trimmed.StartsWith("::{", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (trimmed.StartsWith("shell:::{", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (trimmed.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private bool TryInsertAsTabs(TabBarViewModel vm, int dropIndex, string[] paths)
        {
            if (vm == null || paths == null || paths.Length == 0)
            {
                return false;
            }

            bool inserted = false;
            for (int i = 0; i < paths.Length; i++)
            {
                string targetPath = _explorerService.ResolveShortcutTarget(paths[i]);
                if (string.IsNullOrEmpty(targetPath))
                {
                    continue;
                }

                bool isDirectoryPath = Directory.Exists(targetPath);
                bool isShellPath = IsShellNamespacePath(targetPath);
                if (!isDirectoryPath && !isShellPath)
                {
                    continue;
                }

                if (vm.TryInsertTabWithPath(targetPath, dropIndex, isShellPath))
                {
                    dropIndex++;
                    inserted = true;
                }
            }

            return inserted;
        }


        /// <summary>
        /// CIDA (Shell IDList Array) 構造体を解析してパスを取得する
        /// CIDA: [cidl:uint] [aoffset[0]:uint (親PIDL)] [aoffset[1..n]:uint (子PIDL)]
        /// </summary>
        private string[] ParseCIDA(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8) return null;

            uint cidl = BitConverter.ToUInt32(bytes, 0);
            if (cidl == 0) return null;

            uint maxCidl = (uint)((bytes.Length - 4) / 4 - 1);
            if (cidl > maxCidl) return null;

            // cidl + 1 個のオフセットが必要 (親1つ + 子cidl個)
            int headerSize = 4 + ((int)cidl + 1) * 4;
            if (bytes.Length < headerSize) return null;

            uint parentOffset = BitConverter.ToUInt32(bytes, 4);
            if (!IsValidCidaOffset(bytes, parentOffset)) return null;

            System.Collections.Generic.List<string> paths = new System.Collections.Generic.List<string>();
            System.Runtime.InteropServices.GCHandle handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                IntPtr pData = handle.AddrOfPinnedObject();
                IntPtr parentPidl = IntPtr.Add(pData, (int)parentOffset);
                if (!IsPidlWithinCidaBuffer(bytes, pData, parentPidl))
                {
                    return null;
                }

                for (uint i = 0; i < cidl; i++)
                {
                    uint childOffset = BitConverter.ToUInt32(bytes, (int)(8 + i * 4));
                    if (!IsValidCidaOffset(bytes, childOffset))
                    {
                        continue;
                    }

                    IntPtr childPidl = IntPtr.Add(pData, (int)childOffset);
                    if (!IsPidlWithinCidaBuffer(bytes, pData, childPidl))
                    {
                        continue;
                    }

                    // 親PIDLと子PIDLを結合して絶対PIDLを作成
                    IntPtr absolutePidl = NativeMethods.ILCombine(parentPidl, childPidl);
                    if (absolutePidl != IntPtr.Zero)
                    {
                        try
                        {
                            IntPtr pName;
                            int hr = NativeMethods.SHGetNameFromIDList(absolutePidl, NativeMethods.SIGDN.DESKTOPABSOLUTEPARSING, out pName);
                            if (hr == 0 && pName != IntPtr.Zero)
                            {
                                string path = System.Runtime.InteropServices.Marshal.PtrToStringAuto(pName);
                                System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pName);
                                if (!string.IsNullOrEmpty(path))
                                {
                                    paths.Add(path.TrimEnd('\0'));
                                }
                            }
                        }
                        finally
                        {
                            NativeMethods.ILFree(absolutePidl);
                        }
                    }
                }
            }
            finally
            {
                handle.Free();
            }

            return paths.Count > 0 ? paths.ToArray() : null;
        }

        private bool IsValidCidaOffset(byte[] bytes, uint offset)
        {
            if (bytes == null)
            {
                return false;
            }

            return offset < bytes.Length && bytes.Length - offset >= 2;
        }

        private bool IsPidlWithinCidaBuffer(byte[] bytes, IntPtr bufferStart, IntPtr pidl)
        {
            if (bytes == null || bufferStart == IntPtr.Zero || pidl == IntPtr.Zero)
            {
                return false;
            }

            long offset = pidl.ToInt64() - bufferStart.ToInt64();
            if (offset < 0 || offset >= bytes.Length)
            {
                return false;
            }

            uint size = NativeMethods.ILGetSize(pidl);
            return size > 0 && size <= bytes.Length - offset;
        }
        private void ExecuteFileOperation(string[] sources, string destination, uint wFunc)
        {
            if (sources == null || sources.Length == 0 || string.IsNullOrEmpty(destination)) return;

            string sourcePaths = string.Join("\0", sources) + "\0\0";
            string destPath = destination + "\0\0";
            IntPtr ownerHwnd = _myHwnd;

            // 各ファイル操作を個別のバックグラウンドスレッドで実行することで、
            // 複数のコピー/移動操作を並行に開始でき（複数ダイアログが表示される）、
            // かつエクスプローラー同期タイマー等も妨げないようにする。
            Thread thread = new Thread(() =>
            {
                try
                {
                    NativeMethods.SHFILEOPSTRUCT shf = new NativeMethods.SHFILEOPSTRUCT();
                    shf.hwnd = ownerHwnd;
                    shf.wFunc = wFunc;
                    shf.pFrom = sourcePaths;
                    shf.pTo = destPath;
                    shf.fFlags = NativeMethods.FOF_ALLOWUNDO;

                    int result = NativeMethods.SHFileOperation(ref shf);
                    if (result != 0 || shf.fAnyOperationsAborted)
                    {
                        AppLogger.LogInfo("TabBarWindow", "SHFileOperation reported failure or cancellation.");
                        this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            MessageBox.Show(
                                "ファイル操作を完了できませんでした。",
                                "操作エラー",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }));
                    }

                    // 完了後に UI スレッドでフォーカス復帰を行う
                    this.Dispatcher.BeginInvoke(new Action(() => {
                        ReturnFocusToExplorer();
                    }));
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("TabBarWindow", "ExecuteFileOperation failed.", ex);
                    this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        MessageBox.Show(
                            "ファイル操作の開始に失敗しました。",
                            "操作エラー",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }));
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        private void TabBarWindow_Drop(object sender, DragEventArgs e)
        {
            // Explorer本体へドロップが二重伝播すると別ウィンドウ起動やコピーが発生するため、ここで確実に消費する。
            e.Handled = true;

            int dropIndex = GetDropIndex(e);
            TabBarViewModel vm = GetVM();
            if (vm == null) return;

            Point position = e.GetPosition(TabItemsControl);
            Border targetTabBd = null;
            DependencyObject hit = VisualTreeHelper.HitTest(TabItemsControl, position)?.VisualHit;
            while (hit != null && hit != TabItemsControl)
            {
                Border b = hit as Border;
                if (b != null && b.DataContext is TabItemViewModel)
                {
                    targetTabBd = b;
                    break;
                }
                hit = VisualTreeHelper.GetParent(hit);
            }
            TabItemViewModel targetTab = targetTabBd?.DataContext as TabItemViewModel;

            if (e.Data.GetDataPresent(typeof(TabItemViewModel)))
            {
                TabItemViewModel draggedTab = e.Data.GetData(typeof(TabItemViewModel)) as TabItemViewModel;
                if (draggedTab != null)
                {
                    int oldIndex = -1;
                    for (int i = 0; i < vm.Tabs.Count; i++)
                    {
                        if (vm.Tabs[i] == draggedTab) { oldIndex = i; break; }
                    }
                    if (oldIndex >= 0 && oldIndex != dropIndex)
                    {
                        if (oldIndex < dropIndex)
                        {
                            dropIndex--;
                        }
                        vm.MoveTab(oldIndex, dropIndex);
                    }
                }
            }
            else
            {
                string[] paths = GetPathsFromDataObject(e.Data);
                if (paths != null && paths.Length > 0)
                {
                    if (targetTab != null && !string.IsNullOrEmpty(targetTab.Path))
                    {
                        // 既存のタブ(有効なパスを持つ)の上でドロップされた場合 → SHFileOperation
                        bool isRightDrag = _wasRightDrag;
                        _wasRightDrag = false; // 状態をリセット
                        if (isRightDrag)
                        {
                            ContextMenu menu = new ContextMenu();
                            ApplyFluentMenuStyle(menu);
                            MenuItem copyItem = new MenuItem() { Header = TryFindResource("MenuCopyHere") as string ?? "ここにコピー(&C)" };
                            copyItem.Click += (s, ev) => ExecuteFileOperation(paths, targetTab.Path, NativeMethods.FO_COPY);
                            menu.Items.Add(copyItem);

                            MenuItem moveItem = new MenuItem() { Header = TryFindResource("MenuMoveHere") as string ?? "ここに移動(&M)" };
                            moveItem.Click += (s, ev) => ExecuteFileOperation(paths, targetTab.Path, NativeMethods.FO_MOVE);
                            menu.Items.Add(moveItem);

                            MenuItem shortcutItem = new MenuItem() { Header = TryFindResource("MenuShortcutHere") as string ?? "ショートカットをここに作成(&S)" };
                            shortcutItem.Click += (s, ev) => _explorerService.CreateShortcuts(paths, targetTab.Path, _myHwnd);
                            menu.Items.Add(shortcutItem);

                            MenuItem symlinkItem = new MenuItem() { Header = TryFindResource("MenuSymlinkHere") as string ?? "シンボリックリンクをここに作成(&L)" };
                            symlinkItem.Click += (s, ev) => _explorerService.CreateSymbolicLinks(paths, targetTab.Path, _myHwnd);
                            menu.Items.Add(symlinkItem);

                            menu.Items.Add(new Separator());

                            MenuItem cancelItem = new MenuItem() { Header = TryFindResource("SettingsButtonCancel") as string ?? "キャンセル" };
                            menu.Items.Add(cancelItem);

                            menu.Closed += (s, ev) => ReturnFocusToExplorer();

                            menu.PlacementTarget = targetTabBd;
                            menu.IsOpen = true;
                            return; // フォーカス奪取によりメニューが即座に消えるのを防ぐため早期リターン
                        }
                        else
                        {
                            uint op = NativeMethods.FO_COPY;
                            if ((e.KeyStates & DragDropKeyStates.ShiftKey) == DragDropKeyStates.ShiftKey)
                            {
                                op = NativeMethods.FO_MOVE;
                            }
                            else if ((e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey)
                            {
                                op = NativeMethods.FO_COPY;
                            }
                            else
                            {
                                try
                                {
                                    string destRoot = System.IO.Path.GetPathRoot(targetTab.Path);
                                    string srcRoot = System.IO.Path.GetPathRoot(paths[0]);
                                    if (string.Equals(srcRoot, destRoot, StringComparison.OrdinalIgnoreCase))
                                    {
                                        op = NativeMethods.FO_MOVE;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    AppLogger.LogError("TabBarWindow", "Failed to determine source/destination drive roots for drag-drop operation.", ex);
                                }
                            }
                            ExecuteFileOperation(paths, targetTab.Path, op);
                        }
                    }
                    else
                    {
                        // 背景（または無効なパスのタブ）の上でドロップされた場合 → 新規タブとして開く
                        TryInsertAsTabs(vm, dropIndex, paths);
                    }
                }
            }
            ReturnFocusToExplorer();
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

        private void ApplyFluentMenuStyle(ContextMenu menu)
        {
            if (menu == null) return;

            try
            {
                menu.Background = TryFindResource("ThemeWindowBg") as Brush;
                menu.Foreground = TryFindResource("ThemeFgNormal") as Brush;
                menu.BorderBrush = TryFindResource("ThemeBorderLine") as Brush;
                menu.BorderThickness = new Thickness(1);
                menu.Padding = new Thickness(4);
                menu.MinWidth = 220;

                Style itemStyle = new Style(typeof(MenuItem));
                itemStyle.Setters.Add(new Setter(MenuItem.BackgroundProperty, Brushes.Transparent));
                itemStyle.Setters.Add(new Setter(MenuItem.ForegroundProperty, TryFindResource("ThemeFgNormal")));
                itemStyle.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(10, 6, 10, 6)));
                itemStyle.Setters.Add(new Setter(MenuItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

                Trigger itemHoverTrigger = new Trigger();
                itemHoverTrigger.Property = MenuItem.IsHighlightedProperty;
                itemHoverTrigger.Value = true;
                itemHoverTrigger.Setters.Add(new Setter(MenuItem.BackgroundProperty, TryFindResource("ThemeTabHover")));
                itemHoverTrigger.Setters.Add(new Setter(MenuItem.ForegroundProperty, TryFindResource("ThemeFgNormal")));
                itemStyle.Triggers.Add(itemHoverTrigger);

                Trigger disabledTrigger = new Trigger();
                disabledTrigger.Property = MenuItem.IsEnabledProperty;
                disabledTrigger.Value = false;
                disabledTrigger.Setters.Add(new Setter(MenuItem.OpacityProperty, 0.55));
                itemStyle.Triggers.Add(disabledTrigger);

                menu.Resources[typeof(MenuItem)] = itemStyle;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindow", "ApplyFluentMenuStyle failed. Falling back to default menu style.", ex);
                // スタイル適用で例外が発生しても、右クリックメニュー機能自体は維持する。
            }
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
    }
}


