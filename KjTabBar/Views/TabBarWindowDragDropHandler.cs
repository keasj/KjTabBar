using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KjTabBar.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.ViewModels;

namespace KjTabBar.Views
{
    internal sealed class TabBarWindowDragDropHandler
    {
        private readonly TabBarWindow _window;
        private readonly IExplorerService _explorerService;
        private readonly TabBarWindowContextMenuBuilder _contextMenuBuilder;
        private bool _wasRightDrag = false;
        private IDataObject _dragData;
        private string[] _dragPaths;

        private string[] GetDragPaths(IDataObject data)
        {
            if (!ReferenceEquals(_dragData, data))
            {
                _dragData = data;
                _dragPaths = GetPathsFromDataObject(data);
            }
            return _dragPaths;
        }

        public TabBarWindowDragDropHandler(
            TabBarWindow window,
            IExplorerService explorerService,
            TabBarWindowContextMenuBuilder contextMenuBuilder)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _explorerService = explorerService ?? throw new ArgumentNullException(nameof(explorerService));
            _contextMenuBuilder = contextMenuBuilder ?? throw new ArgumentNullException(nameof(contextMenuBuilder));
        }

        internal static DragDropEffects GetFileDropEffect(DragDropKeyStates keys, DragDropEffects allowed,
            string[] sources = null, string destination = null)
        {
            if ((keys & DragDropKeyStates.RightMouseButton) != 0) return allowed & (DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            bool control = (keys & DragDropKeyStates.ControlKey) != 0;
            bool shift = (keys & DragDropKeyStates.ShiftKey) != 0;
            DragDropEffects requested = control && shift ? DragDropEffects.Link : shift ? DragDropEffects.Move : DragDropEffects.Copy;
            if (!control && !shift && sources != null && sources.Length > 0 && !string.IsNullOrEmpty(destination))
            {
                try
                {
                    string root = System.IO.Path.GetPathRoot(destination);
                    bool sameDrive = !string.IsNullOrEmpty(root);
                    foreach (string source in sources)
                        sameDrive &= string.Equals(root, System.IO.Path.GetPathRoot(source), StringComparison.OrdinalIgnoreCase);
                    if (sameDrive) requested = DragDropEffects.Move;
                }
                catch (ArgumentException) { }
            }
            return requested & allowed;
        }

        public void HandleDragEnter(DragEventArgs e)
        {
            if (!ReferenceEquals(_dragData, e.Data)) { _dragData = null; _dragPaths = null; }
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

        public void HandleDragOver(ItemsControl tabItemsControl, DragEventArgs e)
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
                Point position = e.GetPosition(tabItemsControl);
                DependencyObject hit = VisualTreeHelper.HitTest(tabItemsControl, position)?.VisualHit;
                string destination = null;
                while (hit != null && hit != tabItemsControl)
                {
                    Border b = hit as Border;
                    if (b != null && b.DataContext is TabItemViewModel tabVM && !string.IsNullOrEmpty(tabVM.Path))
                    {
                        destination = tabVM.Path;
                        break;
                    }
                    hit = VisualTreeHelper.GetParent(hit);
                }

                if (destination != null)
                {
                    e.Effects = GetFileDropEffect(e.KeyStates, e.AllowedEffects, GetDragPaths(e.Data), destination);
                }
                else
                {
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

        public async void HandleDrop(ItemsControl tabItemsControl, DragEventArgs e, TabBarViewModel vm, Action onFinished)
        {
            e.Handled = true;
            try
            {
                int dropIndex = GetDropIndex(tabItemsControl, e);
                if (vm == null) return;

                Point position = e.GetPosition(tabItemsControl);
                Border targetTabBd = null;
                DependencyObject hit = VisualTreeHelper.HitTest(tabItemsControl, position)?.VisualHit;
                while (hit != null && hit != tabItemsControl)
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
                    onFinished?.Invoke();
                }
                else
                {
                    string[] paths = GetDragPaths(e.Data);
                    if (paths != null && paths.Length > 0)
                    {
                        if (targetTab != null && !string.IsNullOrEmpty(targetTab.Path))
                        {
                            string destination = targetTab.Path;
                            bool isRightDrag = _wasRightDrag;
                            _wasRightDrag = false;
                            if (isRightDrag)
                            {
                                int finishState = 0;
                                Action finishOnce = delegate
                                {
                                    if (Interlocked.Exchange(ref finishState, 1) == 0)
                                    {
                                        onFinished?.Invoke();
                                    }
                                };

                                ContextMenu menu = new ContextMenu();
                                _contextMenuBuilder.ApplyFluentMenuStyle(menu);
                                MenuItem copyItem = new MenuItem() { Header = _window.TryFindResource("MenuCopyHere") as string ?? "Copy Here(&C)" };
                                copyItem.Click += (s, ev) => ExecuteFileOperation(paths, destination, NativeMethods.FO_COPY, finishOnce);
                                copyItem.IsEnabled = (e.AllowedEffects & DragDropEffects.Copy) != 0;
                                menu.Items.Add(copyItem);

                                MenuItem moveItem = new MenuItem() { Header = _window.TryFindResource("MenuMoveHere") as string ?? "Move Here(&M)" };
                                moveItem.Click += (s, ev) => ExecuteFileOperation(paths, destination, NativeMethods.FO_MOVE, finishOnce);
                                moveItem.IsEnabled = (e.AllowedEffects & DragDropEffects.Move) != 0;
                                menu.Items.Add(moveItem);

                                MenuItem shortcutItem = new MenuItem() { Header = _window.TryFindResource("MenuShortcutHere") as string ?? "Create Shortcut Here(&S)" };
                                shortcutItem.Click += (s, ev) =>
                                {
                                    ExecuteLinkOperation(paths, destination, false, finishOnce);
                                };
                                shortcutItem.IsEnabled = (e.AllowedEffects & DragDropEffects.Link) != 0;
                                menu.Items.Add(shortcutItem);

                                MenuItem symlinkItem = new MenuItem() { Header = _window.TryFindResource("MenuSymlinkHere") as string ?? "Create Symbolic Link Here(&L)" };
                                symlinkItem.Click += (s, ev) =>
                                {
                                    ExecuteLinkOperation(paths, destination, true, finishOnce);
                                };
                                symlinkItem.IsEnabled = (e.AllowedEffects & DragDropEffects.Link) != 0;
                                menu.Items.Add(symlinkItem);

                                menu.Items.Add(new Separator());

                                MenuItem cancelItem = new MenuItem() { Header = _window.TryFindResource("SettingsButtonCancel") as string ?? "Cancel" };
                                cancelItem.Click += (s, ev) => finishOnce();
                                menu.Items.Add(cancelItem);

                                menu.Closed += (s, ev) => finishOnce();

                                menu.PlacementTarget = targetTabBd;
                                menu.IsOpen = true;
                            }
                            else
                            {
                                DragDropEffects effect = GetFileDropEffect(e.KeyStates, e.AllowedEffects, paths, destination);
                                e.Effects = effect;
                                if (effect == DragDropEffects.Copy || effect == DragDropEffects.Move)
                                    ExecuteFileOperation(paths, destination,
                                        effect == DragDropEffects.Move ? NativeMethods.FO_MOVE : NativeMethods.FO_COPY, onFinished);
                                else if (effect == DragDropEffects.Link) ExecuteLinkOperation(paths, destination, false, onFinished);
                                else onFinished?.Invoke();
                            }
                        }
                        else
                        {
                            await TryInsertAsTabsAsync(vm, dropIndex, paths);
                            onFinished?.Invoke();
                        }
                    }
                    else
                    {
                        onFinished?.Invoke();
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindowDragDropHandler", "Drop failed.", ex);
                onFinished?.Invoke();
            }
            finally { _dragData = null; _dragPaths = null; }
        }

        private int GetDropIndex(ItemsControl tabItemsControl, DragEventArgs e)
        {
            if (tabItemsControl == null) return 0;
            Point position = e.GetPosition(tabItemsControl);
            int index = 0;

            for (int i = 0; i < tabItemsControl.Items.Count; i++)
            {
                FrameworkElement fe = tabItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (fe is Button) break;

                if (fe != null)
                {
                    Point childPos = fe.TranslatePoint(new Point(0, 0), tabItemsControl);
                    double top = childPos.Y;
                    double bottom = childPos.Y + fe.ActualHeight;
                    double midPoint = childPos.X + (fe.ActualWidth / 2);

                    if (position.Y < top)
                    {
                        return index;
                    }

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

        private string[] GetPathsFromDataObject(IDataObject data)
        {
            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] filePaths = data.GetData(DataFormats.FileDrop) as string[];
                if (filePaths != null && filePaths.Length > 0) return filePaths;
            }

            if (data.GetDataPresent("Shell IDList Array"))
            {
                MemoryStream ms = null;
                try
                {
                    ms = data.GetData("Shell IDList Array") as MemoryStream;
                    if (ms != null)
                    {
                        byte[] bytes = ms.ToArray();
                        return ShellDragDropHelper.ParseCIDA(bytes);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("TabBarWindowDragDropHandler", "Failed to parse Shell IDList Array from data object.", ex);
                }
            }

            return null;
        }

        private static bool IsShellNamespacePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            string trimmed = path.Trim();
            if (trimmed.StartsWith("::{", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("shell:::{", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        internal Task<bool> TryInsertAsTabsAsync(TabBarViewModel vm, int dropIndex, string[] paths)
        {
            ExplorerHostSwitchCoordinator coordinator = _window.ExplorerHostSwitchCoordinator;
            return InsertDroppedPathsAsync(vm, dropIndex, paths, _explorerService,
                coordinator != null ? new Func<string, Task<bool>>(p => coordinator.PrepareForPathAsync(vm, p, vm.IsPreparedTabOperationCurrent)) : null,
                coordinator != null ? new Action(coordinator.CompletePendingReveal) : null);
        }

        internal static async Task<bool> InsertDroppedPathsAsync(TabBarViewModel vm, int dropIndex, string[] paths,
            IExplorerService explorer, Func<string, Task<bool>> prepare, Action reveal)
        {
            if (vm == null || vm.IsDisposed || vm.IsTabOperationPending || paths == null || paths.Length == 0) return false;
            bool inserted = false;
            foreach (string source in paths)
            {
                long version = vm.SynchronizationVersion;
                string targetPath = await ComThreadService.Instance.InvokeAsync(() => explorer.ResolveShortcutTarget(source));
                bool isShellPath = IsShellNamespacePath(targetPath);
                bool isDirectoryPath = !isShellPath && await Task.Run(() => Directory.Exists(targetPath));
                if (!vm.IsSynchronizationCurrent(version)) return inserted;
                if (string.IsNullOrEmpty(targetPath) || (!isDirectoryPath && !isShellPath)) continue;
                int before = vm.Tabs.Count;
                await vm.InsertTabWithPathAsync(targetPath, dropIndex, prepare, reveal);
                if (vm.IsDisposed || vm.LastNavigationFailed || vm.Tabs.Count == before) return inserted;
                dropIndex++;
                inserted = true;
            }
            return inserted;
        }

        private void ExecuteLinkOperation(string[] sources, string destination, bool symbolic, Action onFinished)
        {
            IntPtr owner = new WindowInteropHelper(_window).Handle;
            IDisposable lease = FileOperationTracker.Shared.TryBegin();
            if (lease == null) { onFinished?.Invoke(); return; }
            Thread worker = new Thread(delegate ()
            {
                try
                {
                    if (symbolic) _explorerService.CreateSymbolicLinks(sources, destination, owner);
                    else _explorerService.CreateShortcuts(sources, destination, owner);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("TabBarWindowDragDropHandler", "Link creation failed.", ex);
                }
                finally
                {
                    lease.Dispose();
                    PostFileOperationResult(() => onFinished?.Invoke());
                }
            });
            worker.SetApartmentState(ApartmentState.STA);
            worker.IsBackground = false;
            try { worker.Start(); }
            catch { lease.Dispose(); throw; }
        }

        private void PostFileOperationResult(Action callback)
        {
            if (_window.Dispatcher.HasShutdownStarted || _window.Dispatcher.HasShutdownFinished) return;
            try { _window.Dispatcher.BeginInvoke(callback); }
            catch (InvalidOperationException) { }
        }

        private void ExecuteFileOperation(string[] sources, string destination, uint wFunc, Action onFinished)
        {
            if (sources == null || sources.Length == 0 || string.IsNullOrEmpty(destination))
            {
                onFinished?.Invoke();
                return;
            }

            string sourcePaths = string.Join("\0", sources) + "\0\0";
            string destPath = destination + "\0\0";
            IntPtr ownerHwnd = new WindowInteropHelper(_window).Handle;

            IDisposable operation = KjTabBar.Services.FileOperationTracker.Shared.TryBegin();
            if (operation == null)
            {
                onFinished?.Invoke();
                return;
            }
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
                    if (result != 0 && !shf.fAnyOperationsAborted)
                    {
                        AppLogger.LogInfo("TabBarWindowDragDropHandler", "SHFileOperation reported failure.");
                        PostFileOperationResult(new Action(() =>
                        {
                            string errorMessage = _window.TryFindResource("FileOperationCompleteErrorMessage") as string ?? "The file operation could not be completed.";
                            string errorTitle = _window.TryFindResource("FileOperationErrorTitle") as string ?? "Operation Error";
                            MessageBox.Show(
                                errorMessage,
                                errorTitle,
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }));
                    }

                    PostFileOperationResult(new Action(() =>
                    {
                        onFinished?.Invoke();
                    }));
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("TabBarWindowDragDropHandler", "ExecuteFileOperation failed.", ex);
                    PostFileOperationResult(new Action(() =>
                    {
                        string errorMessage = _window.TryFindResource("FileOperationStartErrorMessage") as string ?? "Failed to start the file operation.";
                        string errorTitle = _window.TryFindResource("FileOperationErrorTitle") as string ?? "Operation Error";
                        MessageBox.Show(
                            errorMessage,
                            errorTitle,
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        onFinished?.Invoke();
                    }));
                }
                finally { operation.Dispose(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            // Keep in-flight native work alive even if shutdown bypasses the normal tray command.
            thread.IsBackground = false;
            try { thread.Start(); }
            catch { operation.Dispose(); throw; }
        }
    }
}
