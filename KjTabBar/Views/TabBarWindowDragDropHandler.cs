using System;
using System.IO;
using System.Threading;
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

        public TabBarWindowDragDropHandler(
            TabBarWindow window,
            IExplorerService explorerService,
            TabBarWindowContextMenuBuilder contextMenuBuilder)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _explorerService = explorerService ?? throw new ArgumentNullException(nameof(explorerService));
            _contextMenuBuilder = contextMenuBuilder ?? throw new ArgumentNullException(nameof(contextMenuBuilder));
        }

        internal static DragDropEffects GetFileDropEffect(DragDropKeyStates keys, DragDropEffects allowed)
        {
            DragDropEffects requested = (keys & DragDropKeyStates.RightMouseButton) != 0
                ? DragDropEffects.Copy | DragDropEffects.Move
                : ((keys & DragDropKeyStates.ShiftKey) != 0 ? DragDropEffects.Move : DragDropEffects.Copy);
            return requested & allowed;
        }
        public void HandleDragEnter(DragEventArgs e)
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
                bool isOverValidTab = false;
                while (hit != null && hit != tabItemsControl)
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
                    e.Effects = GetFileDropEffect(e.KeyStates, e.AllowedEffects);
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

        public void HandleDrop(ItemsControl tabItemsControl, DragEventArgs e, TabBarViewModel vm, Action onFinished)
        {
            e.Handled = true;

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
                string[] paths = GetPathsFromDataObject(e.Data);
                if (paths != null && paths.Length > 0)
                {
                    if (targetTab != null && !string.IsNullOrEmpty(targetTab.Path))
                    {
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
                            copyItem.Click += (s, ev) => ExecuteFileOperation(paths, targetTab.Path, NativeMethods.FO_COPY, finishOnce);
                            copyItem.IsEnabled = (e.AllowedEffects & DragDropEffects.Copy) != 0;
                            menu.Items.Add(copyItem);

                            MenuItem moveItem = new MenuItem() { Header = _window.TryFindResource("MenuMoveHere") as string ?? "Move Here(&M)" };
                            moveItem.Click += (s, ev) => ExecuteFileOperation(paths, targetTab.Path, NativeMethods.FO_MOVE, finishOnce);
                            moveItem.IsEnabled = (e.AllowedEffects & DragDropEffects.Move) != 0;
                            menu.Items.Add(moveItem);

                            MenuItem shortcutItem = new MenuItem() { Header = _window.TryFindResource("MenuShortcutHere") as string ?? "Create Shortcut Here(&S)" };
                            shortcutItem.Click += (s, ev) =>
                            {
                                _explorerService.CreateShortcuts(paths, targetTab.Path, new WindowInteropHelper(_window).Handle);
                                finishOnce();
                            };
                            shortcutItem.IsEnabled = (e.AllowedEffects & DragDropEffects.Link) != 0;
                            menu.Items.Add(shortcutItem);

                            MenuItem symlinkItem = new MenuItem() { Header = _window.TryFindResource("MenuSymlinkHere") as string ?? "Create Symbolic Link Here(&L)" };
                            symlinkItem.Click += (s, ev) =>
                            {
                                _explorerService.CreateSymbolicLinks(paths, targetTab.Path, new WindowInteropHelper(_window).Handle);
                                finishOnce();
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
                            DragDropEffects effect = GetFileDropEffect(e.KeyStates, e.AllowedEffects);
                            e.Effects = effect;
                            if (effect == DragDropEffects.Copy || effect == DragDropEffects.Move)
                                ExecuteFileOperation(paths, targetTab.Path,
                                    effect == DragDropEffects.Move ? NativeMethods.FO_MOVE : NativeMethods.FO_COPY, onFinished);
                            else onFinished?.Invoke();
                        }
                    }
                    else
                    {
                        TryInsertAsTabs(vm, dropIndex, paths);
                        onFinished?.Invoke();
                    }
                }
                else
                {
                    onFinished?.Invoke();
                }
            }
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
                finally
                {
                    if (ms != null) ms.Dispose();
                }
            }

            return null;
        }

        private bool IsShellNamespacePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            string trimmed = path.Trim();
            if (trimmed.StartsWith("::{", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("shell:::{", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private bool TryInsertAsTabs(TabBarViewModel vm, int dropIndex, string[] paths)
        {
            if (vm == null || paths == null || paths.Length == 0) return false;

            bool inserted = false;
            for (int i = 0; i < paths.Length; i++)
            {
                string targetPath = _explorerService.ResolveShortcutTarget(paths[i]);
                if (string.IsNullOrEmpty(targetPath)) continue;

                bool isDirectoryPath = Directory.Exists(targetPath);
                bool isShellPath = IsShellNamespacePath(targetPath);
                if (!isDirectoryPath && !isShellPath) continue;

                if (vm.TryInsertTabWithPath(targetPath, dropIndex, isShellPath))
                {
                    dropIndex++;
                    inserted = true;
                }
            }

            return inserted;
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
