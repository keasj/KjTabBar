using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.ViewModels;

namespace KjTabBar.Services
{
    // A visible Explorer can be reused without CREATE or SHOW. Only a desktop
    // item's explicit invocation followed by reuse of that same host restores its state.
    internal sealed class DesktopRepeatedLaunchService : IDisposable
    {
        private const uint ObjectInvoked = 0x8013;
        private readonly Func<TabBarViewModel> _findTarget;
        private readonly Func<IntPtr, bool> _isDesktopItemView;
        private readonly Func<IntPtr, bool> _isVisible;
        private readonly Func<IntPtr> _getForeground;
        private readonly Func<IntPtr, int, IntPtr, Task<string>> _resolve;
        private readonly Func<DateTime> _utcNow;
        private readonly Func<IntPtr, bool> _shouldRestoreMaximized;
        private readonly Action<IntPtr> _restoreMaximized;
        private readonly Func<Action, Task> _applyOnUi;
        private readonly ExplorerManager _explorer;
        private readonly WinEventHookRegistration _hook;
        private Request _pending;
        private int _generation;
        private bool _disposed;

        private sealed class Request
        {
            internal TabBarViewModel Target;
            internal TabItemViewModel ActiveTab;
            internal string OriginalPath, OriginalTitle;
            internal IntPtr Host, Source;
            internal int Child, TabCount, Generation;
            internal DateTime Started;
            internal bool RestoreMaximized;
        }

        internal DesktopRepeatedLaunchService(ExplorerManager explorer, Func<TabBarViewModel> findTarget)
            : this(findTarget, IsDesktopItemView, NativeMethods.IsWindowVisible,
                  NativeMethods.GetForegroundWindow, null, () => DateTime.UtcNow,
                  WasMaximizedBeforeLaunch, hwnd => NativeMethods.ShowWindow(hwnd, 3))
        {
            System.Windows.Threading.Dispatcher dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            _applyOnUi = action => dispatcher.InvokeAsync(action).Task;
            _explorer = explorer ?? throw new ArgumentNullException(nameof(explorer));
            _resolve = ResolveAsync;
            _hook = new WinEventHookRegistration("desktop-invoke");
            _hook.Register(ObjectInvoked, ObjectInvoked, OnInvokeEvent);
        }

        internal DesktopRepeatedLaunchService(Func<TabBarViewModel> findTarget,
            Func<IntPtr, bool> isDesktopItemView, Func<IntPtr, bool> isVisible,
            Func<IntPtr> getForeground, Func<IntPtr, int, IntPtr, Task<string>> resolve,
            Func<DateTime> utcNow, Func<IntPtr, bool> shouldRestoreMaximized = null,
            Action<IntPtr> restoreMaximized = null)
        {
            _findTarget = findTarget;
            _isDesktopItemView = isDesktopItemView;
            _isVisible = isVisible;
            _getForeground = getForeground;
            _resolve = resolve;
            _utcNow = utcNow;
            _shouldRestoreMaximized = shouldRestoreMaximized ?? (hwnd => false);
            _restoreMaximized = restoreMaximized ?? (hwnd => { });
            _applyOnUi = action => { action(); return Task.CompletedTask; };
        }

        private void OnInvokeEvent(IntPtr hook, uint evt, IntPtr hwnd, int obj,
            int child, uint thread, uint tick)
        {
            try
            {
                if (evt == ObjectInvoked) CaptureInvocation(hwnd, obj, child);
            }
            catch (Exception ex) { AppLogger.LogError("DesktopRepeat", "Invocation capture failed.", ex); }
        }

        internal void CaptureInvocation(IntPtr source, int objectId, int child)
        {
            if (_disposed || objectId != -4 || child <= 0 || !_isDesktopItemView(source)) return;
            CancelForNewWindow();
            TabBarViewModel target = _findTarget();
            if (target == null || target.IsDisposed || target.IsTabOperationPending || target.ActiveTab == null || target.IsRestoringControlPanelHost ||
                !_isVisible(target.ExplorerHwnd) || string.IsNullOrEmpty(target.ActiveTab.Path)) return;
            _pending = new Request
            {
                Target = target, ActiveTab = target.ActiveTab, OriginalPath = target.ActiveTab.Path,
                OriginalTitle = target.ActiveTab.BaseTitle, Host = target.ExplorerHwnd,
                Source = source, Child = child, TabCount = target.Tabs.Count,
                Generation = _generation, Started = _utcNow(),
                RestoreMaximized = _shouldRestoreMaximized(target.ExplorerHwnd)
            };
            AppLogger.LogDiagnostic("DesktopRepeat", "Captured explicit invocation host=" + target.ExplorerHwnd + " restoreMaximized=" + _pending.RestoreMaximized);
        }

        internal void CancelForNewWindow()
        {
            _generation++;
            _pending = null;
        }

        internal async Task OnForegroundAsync(IntPtr hwnd)
        {
            Request request = _pending;
            _pending = null;
            if (request == null || request.Host != hwnd || !IsCurrent(request)) return;
            try
            {
                string path = await _resolve(request.Source, request.Child, request.Host).ConfigureAwait(false);
                await _applyOnUi(delegate
                {
                    if (!IsCurrent(request) || _getForeground() != request.Host || string.IsNullOrEmpty(path)) return;
                    TabBarViewModel target = request.Target;
                    if (!target.DesktopLaunchPathEquals(request.ActiveTab.Path, request.OriginalPath) &&
                        !target.DesktopLaunchPathEquals(request.ActiveTab.Path, path)) return;
                    if (!target.TryBeginExternalTabOperation()) return;
                    try
                    {
                        // Resolution verified that this host displays the invoked destination.
                        target.AdoptDesktopLaunchPath(path, request.ActiveTab, request.OriginalPath, request.OriginalTitle);
                        if (request.RestoreMaximized) _restoreMaximized(request.Host);
                        AppLogger.LogDiagnostic("DesktopRepeat", "Selected desktop destination for reused host=" + request.Host);
                    }
                    finally { target.EndExternalTabOperation(); }
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { AppLogger.LogError("DesktopRepeat", "Repeated launch resolution failed.", ex); }
        }

        private static bool WasMaximizedBeforeLaunch(IntPtr hwnd)
        {
            NativeMethods.WINDOWPLACEMENT placement = new NativeMethods.WINDOWPLACEMENT();
            placement.length = (uint)Marshal.SizeOf(typeof(NativeMethods.WINDOWPLACEMENT));
            return NativeMethods.GetWindowPlacement(hwnd, ref placement) && ShouldRestoreMaximized(placement);
        }

        internal static bool ShouldRestoreMaximized(NativeMethods.WINDOWPLACEMENT placement)
        {
            // The shell may reuse this host and apply the shortcut's normal show state.
            return placement.showCmd == 3 || (placement.showCmd == 2 && (placement.flags & 2) != 0);
        }

        private bool IsCurrent(Request request)
        {
            return !_disposed && !request.Target.IsDisposed && request.Generation == _generation &&
                _utcNow() - request.Started <= TimeSpan.FromSeconds(2) &&
                ReferenceEquals(_findTarget(), request.Target) && request.Target.ExplorerHwnd == request.Host &&
                ReferenceEquals(request.Target.ActiveTab, request.ActiveTab) &&
                request.Target.Tabs.Count == request.TabCount && _isVisible(request.Host);
        }

        private Task<string> ResolveAsync(IntPtr source, int child, IntPtr host)
        {
            return Task.Run(delegate
            {
                string path = _explorer.ResolveDesktopInvokedShortcut(source, child);
                if (string.IsNullOrEmpty(path)) return null;
                string current = _explorer.GetCurrentPath(host);
                bool matches = SamePath(_explorer.NormalizeKnownPath(path), _explorer.NormalizeKnownPath(current)) ||
                    (_explorer.IsControlPanelRootPath(path) && _explorer.IsControlPanelRootPath(current));
                return matches ? path : null;
            });
        }

        internal static bool SamePath(string first, string second)
        {
            return !string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(second) &&
                string.Equals(first.TrimEnd('\\'), second.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        internal static string ClassName(IntPtr hwnd)
        {
            StringBuilder name = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, name, name.Capacity);
            return name.ToString();
        }

        internal static bool IsDesktopItemView(IntPtr hwnd)
        {
            if (ClassName(hwnd) != "SysListView32") return false;
            IntPtr view = NativeMethods.GetAncestor(hwnd, 1);
            if (ClassName(view) != "SHELLDLL_DefView") return false;
            string rootClass = ClassName(NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT));
            return rootClass == "Progman" || rootClass == "WorkerW";
        }

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromEvent(IntPtr hwnd, uint objectId, uint childId,
            [MarshalAs(UnmanagedType.Interface)] out object accessible, out object child);

        // Called only inside the timeout-bounded Shell worker, never on the UI thread.
        internal static string ResolveInvokedShortcut(IntPtr source, int childId, ExplorerManager explorer)
        {
            if (childId <= 0 || !IsDesktopItemView(source)) return null;
            object accessible = null;
            object child;
            string name;
            try
            {
                int hr = AccessibleObjectFromEvent(source, unchecked((uint)-4), (uint)childId, out accessible, out child);
                if (hr < 0 || accessible == null) return null;
                name = accessible.GetType().InvokeMember("accName", BindingFlags.GetProperty,
                    null, accessible, new[] { child }) as string;
            }
            finally { if (accessible != null && Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible); }
            string shortcut = FindUniqueShortcut(name, new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            });
            if (shortcut == null) return ResolveDesktopFolderItem(name);
            string path = explorer.ResolveShortcutTarget(shortcut);
            return !string.IsNullOrEmpty(path) &&
                (!string.IsNullOrEmpty(explorer.NormalizeShellNamespacePath(path)) ||
                 (Path.IsPathRooted(path) && Directory.Exists(path))) ? path : null;
        }

        // Resolve ordinary and virtual folders by shell identity, including renamed desktop items.
        internal static string ResolveDesktopFolderItem(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            object shell;
            if (!ShellWindowComInterop.TryGetShellApplication(out shell)) return null;
            object desktop = null;
            object items = null;
            try
            {
                desktop = ShellWindowComInterop.InvokeComMethod(shell, "NameSpace", 0);
                items = ShellWindowComInterop.InvokeComMethod(desktop, "Items");
                object count = ShellWindowComInterop.GetComProperty(items, "Count");
                if (count == null) return null;
                string found = null;
                bool matched = false;
                for (int i = 0; i < Convert.ToInt32(count); i++)
                {
                    object item = ShellWindowComInterop.InvokeComMethod(items, "Item", i);
                    try
                    {
                        string itemName = ShellWindowComInterop.GetComProperty(item, "Name") as string;
                        if (!string.Equals(name, itemName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (matched) return null;
                        matched = true;
                        string path = ShellWindowComInterop.GetComProperty(item, "Path") as string;
                        if (ShellWindowComInterop.GetComProperty(item, "IsFolder") is bool isFolder && isFolder)
                            found = path;
                    }
                    finally { ShellWindowComInterop.ReleaseComObjectSafe(item); }
                }
                return found;
            }
            finally
            {
                ShellWindowComInterop.ReleaseComObjectSafe(items);
                ShellWindowComInterop.ReleaseComObjectSafe(desktop);
            }
        }

        internal static string FindUniqueShortcut(string name, string[] directories)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string found = null;
            foreach (string directory in directories)
            {
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) continue;
                foreach (string file in Directory.EnumerateFiles(directory, "*.lnk", SearchOption.TopDirectoryOnly))
                {
                    if (!string.Equals(Path.GetFileNameWithoutExtension(file), name, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (found != null && !SamePath(found, file)) return null;
                    found = file;
                }
            }
            return found;
        }

        public void Dispose()
        {
            _disposed = true;
            CancelForNewWindow();
            if (_hook != null) _hook.Dispose();
        }
    }
}
