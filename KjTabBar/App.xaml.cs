using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.ViewModels;
using KjTabBar.Views;

namespace KjTabBar
{
    public partial class App : Application
    {
        private IExplorerService _explorerService = new Models.ExplorerManager();
        private System.Threading.Mutex _mutex;
        private DispatcherTimer _monitorTimer;
        private Dictionary<IntPtr, TabBarWindow> _tabBars = new Dictionary<IntPtr, TabBarWindow>();
        private HashSet<IntPtr> _ignoredWindows = new HashSet<IntPtr>();
        private HashSet<IntPtr> _processingExplorerWindows = new HashSet<IntPtr>();
        private Dictionary<IntPtr, int> _absorbPathRetryCounts = new Dictionary<IntPtr, int>();
        private HashSet<IntPtr> _desktopLaunchCandidates = new HashSet<IntPtr>();
        private HashSet<IntPtr> _desktopInteractiveLaunchCandidates = new HashSet<IntPtr>();
        private HashSet<IntPtr> _controlPanelTabLaunchCandidates = new HashSet<IntPtr>();
        private const int MaxAbsorbPathRetryCount = 16; // 最大8秒待機 (500ms * 16)
        private const int MaxTransientControlPanelRetryCount = 8; // 一時パス吸収の待機上限 (500ms * 8 = 4秒)

        private bool _isDesktopForeground = false;
        private DateTime _lastDesktopLaunchTokenUtc = DateTime.MinValue;
        private DateTime _lastDesktopInteractiveLaunchTokenUtc = DateTime.MinValue;
        private IntPtr _lastForegroundWindow = IntPtr.Zero;
        private string _lastForegroundClassName = string.Empty;
        private static readonly TimeSpan DesktopLaunchDetectWindow = TimeSpan.FromSeconds(2);
        private IntPtr _foregroundEventHook = IntPtr.Zero;
        private NativeMethods.WinEventDelegate _foregroundEventProc;
        private System.Windows.Forms.NotifyIcon _trayIcon;
        private System.Windows.Forms.ContextMenuStrip _trayMenu;
        private string _lastSavedTabs = ""; // 初期化時に保存済みとみなすことで初回冗長保存を抑制
        private DateTime _lastMemoryMaintenanceUtc = DateTime.MinValue;
        private static readonly TimeSpan MemoryMaintenanceInterval = TimeSpan.FromSeconds(60);
        private IntPtr _showEventHook = IntPtr.Zero;
        private NativeMethods.WinEventDelegate _showEventProc;
        private Dictionary<IntPtr, DateTime> _hiddenPendingAbsorb = new Dictionary<IntPtr, DateTime>();
        private Dictionary<IntPtr, NativeMethods.RECT> _hiddenOriginalRects = new Dictionary<IntPtr, NativeMethods.RECT>();
        private static readonly TimeSpan MaxHiddenDuration = TimeSpan.FromSeconds(2);
        private HashSet<string> _desktopShellItemPathsCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastDesktopShellItemCacheUtc = DateTime.MinValue;
        private static readonly TimeSpan DesktopShellItemCacheDuration = TimeSpan.FromSeconds(5);
        private readonly object _desktopShellItemCacheSync = new object();
        private System.Drawing.Icon _trayIconObj;
        private IntPtr _trayIconHandle = IntPtr.Zero;
        private const string ShellRelaunchArgument = "--kjtb-shell";



        private void Application_Exit(object sender, ExitEventArgs e)
        {
            try
            {
                TabBarViewModel saveTarget = FindValidTabBarTarget();
                if (saveTarget != null)
                {
                    SaveTabsIfChanged(saveTarget);
                }
            }
            catch
            {
            }

            if (_monitorTimer != null)
            {
                _monitorTimer.Tick -= MonitorTimer_Tick;
                _monitorTimer.Stop();
                _monitorTimer = null;
            }

            ThemeManager.Instance.StopMonitoring();

            _explorerService.ReleaseCachedComObjects();

            foreach (KeyValuePair<IntPtr, TabBarWindow> kvp in _tabBars)
            {
                try { kvp.Value.Close(); } catch { }
            }
            _tabBars.Clear();

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            if (_trayIconObj != null)
            {
                _trayIconObj.Dispose();
                _trayIconObj = null;
            }

            if (_trayIconHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_trayIconHandle);
                _trayIconHandle = IntPtr.Zero;
            }

            if (_trayMenu != null)
            {
                _trayMenu.Dispose();
                _trayMenu = null;
            }

            if (_showEventHook != IntPtr.Zero)
            {
                try { NativeMethods.UnhookWinEvent(_showEventHook); } catch { }
                _showEventHook = IntPtr.Zero;
                _showEventProc = null;
            }

            // 非表示（画面外）のまま残ったウィンドウを本来の位置に再表示する
            foreach (KeyValuePair<IntPtr, DateTime> kvp in _hiddenPendingAbsorb)
            {
                try
                {
                    if (NativeMethods.IsWindow(kvp.Key))
                    {
                        NativeMethods.RECT origRect;
                        if (_hiddenOriginalRects.TryGetValue(kvp.Key, out origRect))
                        {
                            NativeMethods.SetWindowPos(kvp.Key, IntPtr.Zero, origRect.Left, origRect.Top, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
                        }
                    }
                }
                catch { }
            }
            _hiddenPendingAbsorb.Clear();
            _hiddenOriginalRects.Clear();

            if (_foregroundEventHook != IntPtr.Zero)
            {
                try { NativeMethods.UnhookWinEvent(_foregroundEventHook); } catch { }
                _foregroundEventHook = IntPtr.Zero;
                _foregroundEventProc = null;
            }


            if (_mutex != null)
            {
                try { _mutex.ReleaseMutex(); } catch { }
                _mutex.Dispose();
                _mutex = null;
            }
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            ApplyLanguageResource();

            if (!HasStartupArgument(e, ShellRelaunchArgument) && IsRunningAsAdministrator())
            {
                if (TryRelaunchAsStandardUser())
                {
                    Shutdown();
                    return;
                }
            }

            _mutex = new System.Threading.Mutex(false, "KjTabBar_Application_Mutex");
            bool hasHandle = false;
            try
            {
                hasHandle = _mutex.WaitOne(0, false);
            }
            catch (System.Threading.AbandonedMutexException)
            {
                hasHandle = true;
            }

            if (!hasHandle)
            {
                _mutex.Dispose();
                _mutex = null;
                Shutdown();
                return;
            }

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            SetupTrayIcon();
            SetupForegroundHook();
            SetupShowHook();

            _monitorTimer = new DispatcherTimer();
            _monitorTimer.Interval = TimeSpan.FromMilliseconds(500);
            _monitorTimer.Tick += MonitorTimer_Tick;
            _monitorTimer.Start();

            ThemeManager.Instance.StartMonitoring();

            MonitorTimer_Tick(null, null);
        }

        private bool HasStartupArgument(StartupEventArgs e, string argument)
        {
            if (e == null || e.Args == null || string.IsNullOrEmpty(argument))
            {
                return false;
            }

            for (int i = 0; i < e.Args.Length; i++)
            {
                if (string.Equals(e.Args[i], argument, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyLanguageResource()
        {
            try
            {
                System.Globalization.CultureInfo culture = System.Threading.Thread.CurrentThread.CurrentUICulture;
                string dictName = "StringResources.en.xaml";
                if (culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                {
                    dictName = "StringResources.ja.xaml";
                }

                ResourceDictionary dict = new ResourceDictionary();
                dict.Source = new Uri($"/KjTabBar;component/Assets/Strings/{dictName}", UriKind.Relative);
                this.Resources.MergedDictionaries.Add(dict);
            }
            catch { }
        }

        private bool IsRunningAsAdministrator()
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
            catch
            {
                return false;
            }
            finally
            {
                if (identity != null)
                {
                    identity.Dispose();
                }
            }
        }

        private bool TryRelaunchAsStandardUser()
        {
            System.Diagnostics.Process currentProcess = null;
            try
            {
                currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                string exePath = currentProcess.MainModule.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    return false;
                }

                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                psi.FileName = "explorer.exe";
                psi.Arguments = "\"" + exePath + "\" " + ShellRelaunchArgument;
                psi.WorkingDirectory = Path.GetDirectoryName(exePath);
                psi.UseShellExecute = true;

                System.Diagnostics.Process relaunched = System.Diagnostics.Process.Start(psi);
                if (relaunched != null)
                {
                    relaunched.Dispose();
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (currentProcess != null)
                {
                    currentProcess.Dispose();
                }
            }
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon();
            
            string programName = "KjTabBar";
            try
            {
                System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                System.Reflection.AssemblyTitleAttribute titleAttr = (System.Reflection.AssemblyTitleAttribute)Attribute.GetCustomAttribute(asm, typeof(System.Reflection.AssemblyTitleAttribute));
                if (titleAttr != null && !string.IsNullOrEmpty(titleAttr.Title))
                {
                    programName = titleAttr.Title;
                }
            }
            catch { }

            _trayIcon.Text = programName;
            
            try
            {
                // WPFのリソースからPNGを読み込んでIconに変換する
                Uri iconUri = new Uri("pack://application:,,,/KjTabBar;component/Assets/Icons/app_icon.png");
                System.Windows.Resources.StreamResourceInfo streamInfo = Application.GetResourceStream(iconUri);
                if (streamInfo != null)
                {
                    System.IO.Stream stream = streamInfo.Stream;
                    System.Drawing.Bitmap bitmap = null;
                    try
                    {
                        bitmap = new System.Drawing.Bitmap(stream);
                        // 背景の黒を透過させる処理は、高品質なPNGのアルファ値を損なうため削除。
                        // GetHicon() が返すネイティブハンドルは明示的な破棄が必要。
                        _trayIconHandle = bitmap.GetHicon();
                        _trayIconObj = System.Drawing.Icon.FromHandle(_trayIconHandle);
                        _trayIcon.Icon = _trayIconObj;
                    }
                    finally
                    {
                        if (bitmap != null) bitmap.Dispose();
                        if (stream != null) stream.Dispose();
                    }
                }
                else
                {
                    _trayIcon.Icon = System.Drawing.SystemIcons.Application;
                }
            }
            catch
            {
                _trayIcon.Icon = System.Drawing.SystemIcons.Application;
            }
            
            _trayIcon.Visible = true;

            _trayMenu = new System.Windows.Forms.ContextMenuStrip();
            string exitText = TryFindResource("TrayMenuExit") as string ?? "終了";
            System.Windows.Forms.ToolStripMenuItem exitItem = new System.Windows.Forms.ToolStripMenuItem(exitText);
            exitItem.Click += (s, ev) =>
            {
                Shutdown();
            };
            _trayMenu.Items.Add(exitItem);
            _trayIcon.ContextMenuStrip = _trayMenu;
        }

        private void SetupForegroundHook()
        {
            try
            {
                _foregroundEventProc = ForegroundEventCallback;
                _foregroundEventHook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    _foregroundEventProc,
                    0,
                    0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT);
            }
            catch
            {
                // フック設定に失敗してもポーリングでの前景判定にフォールバックする
                _foregroundEventHook = IntPtr.Zero;
            }
        }

        private void SetupShowHook()
        {
            try
            {
                _showEventProc = ShowEventCallback;
                _showEventHook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_OBJECT_SHOW,
                    NativeMethods.EVENT_OBJECT_SHOW,
                    IntPtr.Zero,
                    _showEventProc,
                    0, 0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT);
            }
            catch
            {
                _showEventHook = IntPtr.Zero;
            }
        }

        /// <summary>
        /// EVENT_OBJECT_SHOW コールバック。
        /// 新規エクスプローラーウィンドウが表示された瞬間に非表示にし、
        /// タイマーTickでの吸収処理まで表示を抑制する。
        /// </summary>
        private void ShowEventCallback(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (eventType != NativeMethods.EVENT_OBJECT_SHOW) return;
                if (hwnd == IntPtr.Zero) return;
                if (idObject != 0) return;

                if (_tabBars.ContainsKey(hwnd)) return;
                if (_ignoredWindows.Contains(hwnd)) return;
                if (_hiddenPendingAbsorb.ContainsKey(hwnd)) return;
                if (_absorbPathRetryCounts.ContainsKey(hwnd)) return;

                StringBuilder className = new StringBuilder(256);
                NativeMethods.GetClassName(hwnd, className, className.Capacity);
                if (className.ToString() != "CabinetWClass") return;

                TabBarViewModel validTarget = FindValidTabBarTarget();
                if (validTarget == null) return;

                if (IsForegroundRelatedWindow(validTarget.ExplorerHwnd) &&
                    HasActiveControlPanelTab(validTarget))
                {
                    _controlPanelTabLaunchCandidates.Add(hwnd);
                }

                // デスクトップからの遷移直後だけ候補化し、
                // 実際の吸収可否は後段のパス判定で絞り込む。
                if (!WasDesktopForegroundRecently()) return;

                _desktopLaunchCandidates.Add(hwnd);
                if (WasDesktopInteractiveForegroundRecently())
                {
                    _desktopInteractiveLaunchCandidates.Add(hwnd);
                }

                // ちらつき防止のため表示直後に画面外へ一時的に移動する。
                // 吸収完了またはフォールバック時に元の位置へ戻す。

                NativeMethods.RECT rect;
                if (NativeMethods.GetWindowRect(hwnd, out rect))
                {
                    _hiddenOriginalRects[hwnd] = rect;
                    NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, -32000, -32000, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
                }

                _hiddenPendingAbsorb[hwnd] = DateTime.UtcNow;
            }
            catch { }
        }

        private void ForegroundEventCallback(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (eventType != NativeMethods.EVENT_SYSTEM_FOREGROUND) return;
                if (hwnd == IntPtr.Zero) return;

                StringBuilder className = new StringBuilder(256);
                NativeMethods.GetClassName(hwnd, className, className.Capacity);
                UpdateForegroundClassState(hwnd, className.ToString());
            }
            catch { }
        }

        private void MonitorTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                MonitorTimer_TickCore();
            }
            catch
            {
                // 例外が発生してもタイマーは継続
            }
        }

        private void MonitorTimer_TickCore()
        {
            UpdateDesktopForegroundState();

            List<IntPtr> explorerWindows = _explorerService.FindExplorerWindows();

            // 非表示にしたウィンドウも処理対象に追加（FindExplorerWindowsはIsWindowVisibleで除外するため）
            // コレクション変更に備え、キーのスナップショットを取得してからイテレーションする
            IntPtr[] hiddenKeys = new IntPtr[_hiddenPendingAbsorb.Count];
            _hiddenPendingAbsorb.Keys.CopyTo(hiddenKeys, 0);
            for (int h = 0; h < hiddenKeys.Length; h++)
            {
                bool alreadyInList = false;
                for (int i = 0; i < explorerWindows.Count; i++)
                {
                    if (explorerWindows[i] == hiddenKeys[h])
                    {
                        alreadyInList = true;
                        break;
                    }
                }
                if (!alreadyInList)
                {
                    explorerWindows.Add(hiddenKeys[h]);
                }
            }

            // 閉じたExplorerに対応するタブバーを除去
            List<IntPtr> toRemove = new List<IntPtr>();
            foreach (KeyValuePair<IntPtr, TabBarWindow> kvp in _tabBars)
            {
                bool shouldRemove = false;
                try
                {
                    bool found = false;
                    for (int i = 0; i < explorerWindows.Count; i++)
                    {
                        if (explorerWindows[i] == kvp.Key)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        shouldRemove = true;
                    }
                    else if (!kvp.Value.IsExplorerAlive())
                    {
                        shouldRemove = true;
                    }
                }
                catch
                {
                    // タブバーに問題があれば除去
                    shouldRemove = true;
                }

                if (shouldRemove)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                TabBarWindow window;
                if (_tabBars.TryGetValue(toRemove[i], out window))
                {
                    try { window.Close(); } catch { }
                    _tabBars.Remove(toRemove[i]);
                }
            }

            // 閉じたウィンドウを各コレクションからクリーンアップ
            RemoveClosedWindows(_ignoredWindows, explorerWindows);
            RemoveClosedWindowKeys(_absorbPathRetryCounts, explorerWindows);
            RemoveClosedWindows(_desktopLaunchCandidates, explorerWindows);
            RemoveClosedWindows(_desktopInteractiveLaunchCandidates, explorerWindows);
            RemoveClosedWindows(_controlPanelTabLaunchCandidates, explorerWindows);
            RemoveClosedWindows(_processingExplorerWindows, explorerWindows);
            RemoveClosedWindowKeys(_hiddenPendingAbsorb, explorerWindows);
            RemoveClosedWindowKeys(_hiddenOriginalRects, explorerWindows);

            // 新しいExplorerを処理
            for (int i = 0; i < explorerWindows.Count; i++)
            {
                IntPtr hwnd = explorerWindows[i];

                // 既に管理済み or 吸収済みならスキップ
                if (_tabBars.ContainsKey(hwnd)) continue;
                if (_processingExplorerWindows.Contains(hwnd)) continue;

                // 既存のタブバーが有効かチェック
                TabBarViewModel validTarget = FindValidTabBarTarget();
                if (_ignoredWindows.Contains(hwnd))
                {
                    if (!ExplorerWindowDecisionLogic.ShouldReevaluateIgnoredWindow(validTarget != null))
                    {
                        continue;
                    }

                    _ignoredWindows.Remove(hwnd);
                }

                try
                {
                    _processingExplorerWindows.Add(hwnd);
                    _ = ProcessNewExplorerWindowAsync(hwnd, validTarget);
                }
                catch
                {
                    _processingExplorerWindows.Remove(hwnd);
                }
            }

            TabBarViewModel saveTarget = FindValidTabBarTarget();
            if (saveTarget != null)
            {
                SaveTabsIfChanged(saveTarget);
            }

            // 非表示にしたが吸収されなかったウィンドウを再表示する
            // コレクション変更に備え、キーのスナップショットを取得してからイテレーションする
            List<IntPtr> hiddenToRestore = new List<IntPtr>();
            IntPtr[] hiddenRestoreKeys = new IntPtr[_hiddenPendingAbsorb.Count];
            _hiddenPendingAbsorb.Keys.CopyTo(hiddenRestoreKeys, 0);
            for (int h = 0; h < hiddenRestoreKeys.Length; h++)
            {
                bool shouldRestore = false;

                if (_ignoredWindows.Contains(hiddenRestoreKeys[h]))
                {
                    // 吸収対象外と判定された → 再表示
                    shouldRestore = true;
                }
                else
                {
                    DateTime hiddenTime;
                    if (_hiddenPendingAbsorb.TryGetValue(hiddenRestoreKeys[h], out hiddenTime))
                    {
                        if (!IsAbsorbDecisionPending(hiddenRestoreKeys[h]) &&
                            (DateTime.UtcNow - hiddenTime) > MaxHiddenDuration)
                        {
                            // 非表示のまま長時間経過 → 再表示してフォールバック
                            shouldRestore = true;
                        }
                    }
                }

                if (shouldRestore)
                {
                    hiddenToRestore.Add(hiddenRestoreKeys[h]);
                }
            }
            for (int i = 0; i < hiddenToRestore.Count; i++)
            {
                _hiddenPendingAbsorb.Remove(hiddenToRestore[i]);
                if (NativeMethods.IsWindow(hiddenToRestore[i]))
                {
                    NativeMethods.RECT origRect;
                    if (_hiddenOriginalRects.TryGetValue(hiddenToRestore[i], out origRect))
                    {
                        NativeMethods.SetWindowPos(hiddenToRestore[i], IntPtr.Zero, origRect.Left, origRect.Top, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
                        _hiddenOriginalRects.Remove(hiddenToRestore[i]);
                    }
                }
            }

            PerformPeriodicMemoryMaintenance();
        }

        private bool IsAbsorbDecisionPending(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            return _absorbPathRetryCounts.ContainsKey(hwnd);
        }

        private void PerformPeriodicMemoryMaintenance()
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (_lastMemoryMaintenanceUtc != DateTime.MinValue)
            {
                if ((nowUtc - _lastMemoryMaintenanceUtc) < MemoryMaintenanceInterval)
                {
                    return;
                }
            }

            _lastMemoryMaintenanceUtc = nowUtc;

            try
            {
                _explorerService.ReleaseCachedComObjects();
                // バックグラウンドスレッド側のCOMキャッシュもクリアしてリークを防止
                _ = Services.ComThreadService.Instance.InvokeAsync(() =>
                {
                    _explorerService.ReleaseCachedComObjects();
                });

                System.Runtime.InteropServices.Marshal.CleanupUnusedObjectsInCurrentContext();
                // spec.md に従い明示的な収集を実行
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch { }
        }

        private void RemoveClosedWindows(HashSet<IntPtr> collection, List<IntPtr> explorerWindows)
        {
            List<IntPtr> toRemove = new List<IntPtr>();
            foreach (IntPtr item in collection)
            {
                bool found = false;
                for (int i = 0; i < explorerWindows.Count; i++)
                {
                    if (explorerWindows[i] == item)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    toRemove.Add(item);
                }
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                collection.Remove(toRemove[i]);
            }
        }

        private void RemoveClosedWindowKeys<TValue>(Dictionary<IntPtr, TValue> collection, List<IntPtr> explorerWindows)
        {
            List<IntPtr> toRemove = new List<IntPtr>();
            foreach (KeyValuePair<IntPtr, TValue> kvp in collection)
            {
                bool found = false;
                for (int i = 0; i < explorerWindows.Count; i++)
                {
                    if (explorerWindows[i] == kvp.Key)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                collection.Remove(toRemove[i]);
            }
        }

        private void LoadTabsToVM(TabBarViewModel vm)
        {
            try
            {
                string file = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KjTabBar", "tabs.txt");
                if (System.IO.File.Exists(file))
                {
                    string[] paths = System.IO.File.ReadAllLines(file);
                    vm.RestoreTabs(paths);
                    _lastSavedTabs = paths.Length > 0 ? string.Join("|", paths) + "|" : "";
                }
            }
            catch { }
        }

        private void SaveTabsIfChanged(TabBarViewModel vm)
        {
            try
            {
                if (vm == null || vm.Tabs.Count == 0) return;

                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < vm.Tabs.Count; i++)
                {
                    if (IsPathPersistable(vm.Tabs[i].Path))
                    {
                        sb.Append(vm.Tabs[i].Path);
                        sb.Append("|");
                    }
                }
                string currentTabsStr = sb.ToString();

                if (_lastSavedTabs != currentTabsStr)
                {
                    _lastSavedTabs = currentTabsStr;

                    List<string> paths = new List<string>();
                    for (int i = 0; i < vm.Tabs.Count; i++)
                    {
                        if (IsPathPersistable(vm.Tabs[i].Path))
                        {
                            paths.Add(vm.Tabs[i].Path);
                        }
                    }

                    string file = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KjTabBar", "tabs.txt");
                    string dir = System.IO.Path.GetDirectoryName(file);
                    if (!System.IO.Directory.Exists(dir))
                    {
                        System.IO.Directory.CreateDirectory(dir);
                    }
                    System.IO.File.WriteAllLines(file, paths.ToArray());
                }
            }
            catch { }
        }

        /// <summary>
        /// 有効な（生きている）タブバーのViewModelを探す。見つからなければnull。
        /// </summary>
        private TabBarViewModel FindValidTabBarTarget()
        {
            TabBarViewModel firstValidTarget = null;
            foreach (KeyValuePair<IntPtr, TabBarWindow> kvp in _tabBars)
            {
                TabBarViewModel vm;
                if (!TryGetAliveTabBarViewModel(kvp, out vm))
                {
                    continue;
                }

                if (firstValidTarget == null)
                {
                    firstValidTarget = vm;
                }

                if (IsForegroundRelatedWindow(vm.ExplorerHwnd))
                {
                    return vm;
                }
            }

            return firstValidTarget;
        }

        private bool TryGetAliveTabBarViewModel(KeyValuePair<IntPtr, TabBarWindow> entry, out TabBarViewModel viewModel)
        {
            viewModel = null;
            try
            {
                if (!entry.Value.IsExplorerAlive())
                {
                    return false;
                }

                viewModel = entry.Value.DataContext as TabBarViewModel;
                if (viewModel == null)
                {
                    return false;
                }

                return NativeMethods.IsWindow(viewModel.ExplorerHwnd);
            }
            catch
            {
                viewModel = null;
                return false;
            }
        }

        private bool IsForegroundRelatedWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                return false;
            }

            if (foregroundWindow == hwnd)
            {
                return true;
            }

            IntPtr foregroundRoot = NativeMethods.GetAncestor(foregroundWindow, NativeMethods.GA_ROOT);
            return foregroundRoot == hwnd;
        }

        private IntPtr GetRootWindowOrSelf(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr rootHwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (rootHwnd != IntPtr.Zero)
            {
                return rootHwnd;
            }

            return hwnd;
        }

        private bool IsManagedControlPanelLaunchSource(IntPtr explorerHwnd)
        {
            if (explorerHwnd == IntPtr.Zero)
            {
                return false;
            }

            foreach (KeyValuePair<IntPtr, TabBarWindow> kvp in _tabBars)
            {
                TabBarViewModel vm;
                if (!TryGetAliveTabBarViewModel(kvp, out vm))
                {
                    continue;
                }

                if (vm.ExplorerHwnd != explorerHwnd)
                {
                    continue;
                }

                return HasActiveControlPanelTab(vm);
            }

            return false;
        }

        private bool IsUnmanagedExplorerWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            if (_tabBars.ContainsKey(hwnd))
            {
                return false;
            }

            if (!NativeMethods.IsWindow(hwnd))
            {
                return false;
            }

            StringBuilder className = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, className, className.Capacity);
            return className.ToString().Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase);
        }

        private void RegisterControlPanelTabLaunchCandidate(IntPtr foregroundWindow)
        {
            IntPtr currentRoot = GetRootWindowOrSelf(foregroundWindow);
            IntPtr previousRoot = GetRootWindowOrSelf(_lastForegroundWindow);
            if (currentRoot == IntPtr.Zero || previousRoot == IntPtr.Zero)
            {
                return;
            }

            if (currentRoot == previousRoot)
            {
                return;
            }

            if (!IsManagedControlPanelLaunchSource(previousRoot))
            {
                return;
            }

            if (!IsUnmanagedExplorerWindow(currentRoot))
            {
                return;
            }

            _controlPanelTabLaunchCandidates.Add(currentRoot);
        }

        private TabBarViewModel FindControlPanelTabBarTarget(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            TabBarViewModel activeControlPanelTarget = null;
            TabBarViewModel firstControlPanelTarget = null;
            TabBarViewModel fallbackControlPanelHost = null;
            foreach (KeyValuePair<IntPtr, TabBarWindow> kvp in _tabBars)
            {
                TabBarViewModel vm;
                if (!TryGetAliveTabBarViewModel(kvp, out vm))
                {
                    continue;
                }

                if (fallbackControlPanelHost == null && HasAnyControlPanelTab(vm))
                {
                    fallbackControlPanelHost = vm;
                }

                if (!HasEquivalentControlPanelTab(vm, path))
                {
                    continue;
                }

                if (IsForegroundRelatedWindow(vm.ExplorerHwnd))
                {
                    return vm;
                }

                if (activeControlPanelTarget == null)
                {
                    TabItemViewModel activeTab = vm.ActiveTab;
                    if (activeTab != null && _explorerService.IsControlPanelPath(activeTab.Path))
                    {
                        activeControlPanelTarget = vm;
                    }
                }

                if (firstControlPanelTarget == null)
                {
                    firstControlPanelTarget = vm;
                }
            }

            if (activeControlPanelTarget != null)
            {
                return activeControlPanelTarget;
            }

            if (fallbackControlPanelHost != null)
            {
                return fallbackControlPanelHost;
            }

            return firstControlPanelTarget;
        }

        private bool HasAnyControlPanelTab(TabBarViewModel targetVM)
        {
            if (targetVM == null)
            {
                return false;
            }

            for (int i = 0; i < targetVM.Tabs.Count; i++)
            {
                string tabPath = targetVM.Tabs[i].Path;
                if (string.IsNullOrEmpty(tabPath))
                {
                    continue;
                }

                if (_explorerService.IsControlPanelPath(tabPath))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryRegisterDesktopLaunchCandidate(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            if (_desktopLaunchCandidates.Contains(hwnd))
            {
                return true;
            }

            // デスクトップからの遷移直後だけ候補化する。
            // 実際の吸収可否は後段のパス判定で絞り込む。
            if (!WasDesktopForegroundRecently())
            {
                return false;
            }

            _desktopLaunchCandidates.Add(hwnd);
            if (WasDesktopInteractiveForegroundRecently())
            {
                _desktopInteractiveLaunchCandidates.Add(hwnd);
            }
            return true;
        }

        private void IgnoreExplorerWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            _desktopLaunchCandidates.Remove(hwnd);
            _desktopInteractiveLaunchCandidates.Remove(hwnd);
            _controlPanelTabLaunchCandidates.Remove(hwnd);
            _absorbPathRetryCounts.Remove(hwnd);
            _ignoredWindows.Add(hwnd);

            if (_hiddenPendingAbsorb.Remove(hwnd))
            {
                if (NativeMethods.IsWindow(hwnd))
                {
                    NativeMethods.RECT origRect;
                    if (_hiddenOriginalRects.TryGetValue(hwnd, out origRect))
                    {
                        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, origRect.Left, origRect.Top, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
                        _hiddenOriginalRects.Remove(hwnd);
                    }
                }
            }
        }


        private async Task ProcessNewExplorerWindowAsync(IntPtr hwnd, TabBarViewModel validTarget)
        {
            try
            {
                int retryCount = 0;
                _absorbPathRetryCounts.TryGetValue(hwnd, out retryCount);

                bool isDesktopCandidate = _desktopLaunchCandidates.Contains(hwnd);
                bool isDesktopInteractiveCandidate = _desktopInteractiveLaunchCandidates.Contains(hwnd);
                bool isControlPanelTabLaunchCandidate = _controlPanelTabLaunchCandidates.Contains(hwnd);

                if (!isDesktopCandidate && TryRegisterDesktopLaunchCandidate(hwnd))
                {
                    isDesktopCandidate = true;
                    isDesktopInteractiveCandidate = _desktopInteractiveLaunchCandidates.Contains(hwnd);
                }

                bool isHiddenPending = _hiddenPendingAbsorb.ContainsKey(hwnd);

                (AbsorptionAction, string, bool, bool, TabBarViewModel) result = await Services.ComThreadService.Instance.InvokeAsync(() =>
                {
                    string path = _explorerService.GetCurrentPath(hwnd);
                    string titlePath = GetDesktopVirtualPathFromWindowTitle(hwnd);

                    bool isControlPanelPath = _explorerService.IsControlPanelPath(path) ||
                                              (!string.IsNullOrEmpty(titlePath) && _explorerService.IsControlPanelRootPath(titlePath));

                    TabBarViewModel controlPanelTargetLocal = null;
                    if (isControlPanelPath)
                    {
                        string searchPath = _explorerService.IsControlPanelRootPath(titlePath) ? _explorerService.AllControlPanelPath : path;
                        controlPanelTargetLocal = (TabBarViewModel)Application.Current.Dispatcher.Invoke(new Func<TabBarViewModel>(() =>
                        {
                            return FindControlPanelTabBarTarget(searchPath);
                        }));
                    }

                    ExplorerWindowContext context = new ExplorerWindowContext
                    {
                        CurrentRetryCount = retryCount,
                        IsDesktopCandidate = isDesktopCandidate,
                        IsDesktopInteractiveCandidate = isDesktopInteractiveCandidate,
                        IsHiddenPending = isHiddenPending,
                        IsControlPanelTabLaunchCandidate = isControlPanelTabLaunchCandidate,
                        HasValidTarget = validTarget != null,
                        HasControlPanelTarget = controlPanelTargetLocal != null,

                        CurrentPath = path,
                        TitleVirtualPath = titlePath,

                        IsDesktopShortcutTargetFunc = p => IsDesktopShortcutTargetPath(p),
                        IsDesktopFolderPathFunc = p => IsDesktopFolderPath(p),
                        IsDesktopShellItemPathFunc = p => IsDesktopShellItemPath(p),
                        IsDesktopSpecialShellPathFunc = p => IsDesktopSpecialShellPath(p),

                        HasEquivalentControlPanelTabFunc = p =>
                        {
                            return (bool)Application.Current.Dispatcher.Invoke(new Func<bool>(() =>
                            {
                                return controlPanelTargetLocal != null && HasEquivalentControlPanelTab(controlPanelTargetLocal, p);
                            }));
                        },
                        HasActiveControlPanelTabFunc = () =>
                        {
                            return (bool)Application.Current.Dispatcher.Invoke(new Func<bool>(() =>
                            {
                                return HasActiveControlPanelTab(validTarget);
                            }));
                        }
                    };

                    string outPath;
                    bool outSpecial;
                    AbsorptionAction outAction = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out outPath, out outSpecial);

                    return (outAction, outPath, outSpecial, isControlPanelPath, controlPanelTargetLocal);
                });

                AbsorptionAction action = result.Item1;
                string resolvedPath = result.Item2;
                bool allowSpecialPath = result.Item3;
                bool isControlPanelPathUI = result.Item4;
                TabBarViewModel controlPanelTarget = result.Item5;

                if (_tabBars.ContainsKey(hwnd) || _ignoredWindows.Contains(hwnd))
                {
                    return;
                }

                TabBarViewModel latestValidTarget = FindValidTabBarTarget();
                if (latestValidTarget != null)
                {
                    validTarget = latestValidTarget;
                }
                if (isControlPanelPathUI)
                {
                    controlPanelTarget = FindControlPanelTabBarTarget(resolvedPath);
                }

                if (action == AbsorptionAction.CreateNewTabBar && validTarget != null)
                {
                    return;
                }

                switch (action)
                {
                    case AbsorptionAction.WaitAndRetryIncrement:
                        _absorbPathRetryCounts[hwnd] = retryCount + 1;
                        break;

                    case AbsorptionAction.AbsorbWithFallback:
                    case AbsorptionAction.Absorb:
                        _absorbPathRetryCounts.Remove(hwnd);
                        _desktopLaunchCandidates.Remove(hwnd);
                        _desktopInteractiveLaunchCandidates.Remove(hwnd);
                        _controlPanelTabLaunchCandidates.Remove(hwnd);
                        TabBarViewModel targetToUse = (action == AbsorptionAction.Absorb && isControlPanelPathUI && controlPanelTarget != null) ? controlPanelTarget : validTarget;
                        AbsorbExplorerWindow(hwnd, targetToUse, resolvedPath, allowSpecialPath);
                        break;

                    case AbsorptionAction.CreateNewTabBar:
                        _absorbPathRetryCounts.Remove(hwnd);
                        _desktopLaunchCandidates.Remove(hwnd);
                        _desktopInteractiveLaunchCandidates.Remove(hwnd);
                        _controlPanelTabLaunchCandidates.Remove(hwnd);
                        CreateNewTabBar(hwnd);
                        break;

                    case AbsorptionAction.Ignore:
                        _absorbPathRetryCounts.Remove(hwnd);
                        _desktopLaunchCandidates.Remove(hwnd);
                        _desktopInteractiveLaunchCandidates.Remove(hwnd);
                        _controlPanelTabLaunchCandidates.Remove(hwnd);
                        IgnoreExplorerWindow(hwnd);
                        break;
                }
            }
            catch
            {
            }
            finally
            {
                _processingExplorerWindows.Remove(hwnd);
            }
        }

        private void CreateNewTabBar(IntPtr hwnd)
        {
            try
            {
                if (_hiddenPendingAbsorb.Remove(hwnd))
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
                }

                TabBarViewModel vm = new TabBarViewModel(hwnd, UserSettings.Current, _explorerService);
                LoadTabsToVM(vm);

                TabBarWindow tabBarWindow = new TabBarWindow();
                tabBarWindow.ExplorerService = _explorerService;
                tabBarWindow.DataContext = vm;
                tabBarWindow.Show();
                _tabBars[hwnd] = tabBarWindow;
            }
            catch
            {
            }
        }

        private bool HasEquivalentControlPanelTab(TabBarViewModel targetVM, string path)
        {
            if (targetVM == null || string.IsNullOrEmpty(path))
            {
                return false;
            }

            string normalizedPath = _explorerService.NormalizeShellNamespacePath(path);
            string trimmedPath = path.TrimEnd('\\');
            for (int i = 0; i < targetVM.Tabs.Count; i++)
            {
                string tabPath = targetVM.Tabs[i].Path;
                if (string.IsNullOrEmpty(tabPath))
                {
                    continue;
                }
                if (!_explorerService.IsControlPanelPath(tabPath))
                {
                    continue;
                }

                if (tabPath.TrimEnd('\\').Equals(trimmedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string normalizedTabPath = _explorerService.NormalizeShellNamespacePath(tabPath);
                if (!string.IsNullOrEmpty(normalizedPath) &&
                    !string.IsNullOrEmpty(normalizedTabPath) &&
                    normalizedTabPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasActiveControlPanelTab(TabBarViewModel targetVM)
        {
            if (targetVM == null)
            {
                return false;
            }

            TabItemViewModel activeTab = targetVM.ActiveTab;
            if (activeTab == null || string.IsNullOrEmpty(activeTab.Path))
            {
                return false;
            }

            return _explorerService.IsControlPanelPath(activeTab.Path);
        }

        private string GetDesktopVirtualPathFromWindowTitle(IntPtr explorerHwnd)
        {
            if (explorerHwnd == IntPtr.Zero)
            {
                return null;
            }

            StringBuilder titleBuilder = new StringBuilder(512);
            NativeMethods.GetWindowText(explorerHwnd, titleBuilder, titleBuilder.Capacity);
            string title = titleBuilder.ToString();
            if (string.IsNullOrEmpty(title))
            {
                return null;
            }

            return _explorerService.MapLocationNameToKnownShellPath(title);
        }

        private static readonly string[] DesktopSpecialShellPaths = new string[]
        {
            "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
            "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}",
            "::{645FF040-5081-101B-9F08-00AA002F954E}"
        };

        private bool IsDesktopSpecialShellPath(string path)
        {
            string normalizedPath = _explorerService.NormalizeShellNamespacePath(path);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            for (int i = 0; i < DesktopSpecialShellPaths.Length; i++)
            {
                if (normalizedPath.Equals(DesktopSpecialShellPaths[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateDesktopForegroundState()
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return;
            }

            StringBuilder className = new StringBuilder(256);
            NativeMethods.GetClassName(foreground, className, className.Capacity);
            UpdateForegroundClassState(foreground, className.ToString());
        }

        // WINEVENT_OUTOFCONTEXT は UI スレッドのメッセージループで配信されるため、
        // タイマーTickやフックコールバックとの排他制御は不要。
        private bool WasDesktopForegroundRecently()
        {
            if (_isDesktopForeground)
            {
                return true;
            }
            if (_lastDesktopLaunchTokenUtc == DateTime.MinValue)
            {
                return false;
            }
            return (DateTime.UtcNow - _lastDesktopLaunchTokenUtc) <= DesktopLaunchDetectWindow;
        }

        private bool WasDesktopInteractiveForegroundRecently()
        {
            if (_lastDesktopInteractiveLaunchTokenUtc == DateTime.MinValue)
            {
                return false;
            }
            return (DateTime.UtcNow - _lastDesktopInteractiveLaunchTokenUtc) <= DesktopLaunchDetectWindow;
        }

        private void UpdateForegroundClassState(IntPtr foregroundWindow, string className)
        {
            RegisterControlPanelTabLaunchCandidate(foregroundWindow);

            bool isDesktopWindowClass = IsDesktopShellWindowClass(className);
            if (!isDesktopWindowClass && _isDesktopForeground)
            {
                _lastDesktopLaunchTokenUtc = DateTime.UtcNow;
                if (IsDesktopItemViewWindowClass(_lastForegroundClassName))
                {
                    _lastDesktopInteractiveLaunchTokenUtc = DateTime.UtcNow;
                }
            }
            _isDesktopForeground = isDesktopWindowClass;
            _lastForegroundWindow = foregroundWindow;
            _lastForegroundClassName = className;
        }

        private static readonly string[] DesktopShellWindowClasses = new string[]
        {
            "Progman", "WorkerW", "SHELLDLL_DefView", "SysListView32"
        };

        private static readonly string[] DesktopItemViewWindowClasses = new string[]
        {
            "SHELLDLL_DefView", "SysListView32"
        };

        private bool IsDesktopShellWindowClass(string className)
        {
            return MatchesAnyClassName(className, DesktopShellWindowClasses);
        }

        private bool IsDesktopItemViewWindowClass(string className)
        {
            return MatchesAnyClassName(className, DesktopItemViewWindowClasses);
        }

        private static bool MatchesAnyClassName(string className, string[] candidates)
        {
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }
            for (int i = 0; i < candidates.Length; i++)
            {
                if (className.Equals(candidates[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }



        private bool IsDesktopFolderPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string userDesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string commonDesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

            if (IsSameOrChildPath(path, userDesktopPath))
            {
                return true;
            }
            if (IsSameOrChildPath(path, commonDesktopPath))
            {
                return true;
            }

            return false;
        }

        private bool IsDesktopShortcutTargetPath(string path)
        {
            return Models.ExplorerAbsorptionLogic.IsDesktopShortcutTargetPath(_explorerService, path);
        }

        private bool IsDesktopShellItemPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            DateTime now = DateTime.UtcNow;
            bool needsUpdate = false;
            lock (_desktopShellItemCacheSync)
            {
                if ((now - _lastDesktopShellItemCacheUtc) > DesktopShellItemCacheDuration)
                {
                    needsUpdate = true;
                }
            }

            if (needsUpdate)
            {
                HashSet<string> newCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool success = false;
                object shellObject = null;
                object desktopFolder = null;
                object desktopItems = null;
                try
                {
                    if (Models.ExplorerManager.TryGetShellApplication(out shellObject))
                    {
                        desktopFolder = Models.ExplorerManager.InvokeComMethod(shellObject, "NameSpace", 0);
                        if (desktopFolder != null)
                        {
                            desktopItems = Models.ExplorerManager.InvokeComMethod(desktopFolder, "Items");
                            object countObj = Models.ExplorerManager.GetComProperty(desktopItems, "Count");
                            if (countObj != null)
                            {
                                int count = 0;
                                try { count = Convert.ToInt32(countObj); } catch { }

                                for (int i = 0; i < count; i++)
                                {
                                    object item = null;
                                    try
                                    {
                                        item = Models.ExplorerManager.InvokeComMethod(desktopItems, "Item", i);
                                        if (item == null) continue;

                                        string itemPath = Models.ExplorerManager.GetComProperty(item, "Path") as string;
                                        if (!string.IsNullOrEmpty(itemPath))
                                        {
                                            newCache.Add(itemPath);

                                            string normalizedItemShellPath = _explorerService.NormalizeShellNamespacePath(itemPath);
                                            if (!string.IsNullOrEmpty(normalizedItemShellPath))
                                            {
                                                newCache.Add(normalizedItemShellPath);
                                            }

                                            string normalizedPath;
                                            if (TryNormalizePath(itemPath, out normalizedPath))
                                            {
                                                newCache.Add(normalizedPath);
                                            }
                                        }
                                    }
                                    catch { }
                                    finally
                                    {
                                        Models.ExplorerManager.ReleaseComObjectSafe(item);
                                    }
                                }
                                success = true;
                            }
                        }
                    }
                }
                catch { }
                finally
                {
                    Models.ExplorerManager.ReleaseComObjectSafe(desktopItems);
                    Models.ExplorerManager.ReleaseComObjectSafe(desktopFolder);
                }

                if (success)
                {
                    lock (_desktopShellItemCacheSync)
                    {
                        _desktopShellItemPathsCache = newCache;
                        _lastDesktopShellItemCacheUtc = now;
                    }
                }
            }

            bool result = false;
            lock (_desktopShellItemCacheSync)
            {
                if (_desktopShellItemPathsCache.Contains(path))
                {
                    result = true;
                }
                else
                {
                    string normalizedPath = null;
                    if (TryNormalizePath(path, out normalizedPath))
                    {
                        if (_desktopShellItemPathsCache.Contains(normalizedPath))
                        {
                            result = true;
                        }
                    }

                    if (!result)
                    {
                        string normalizedShellPath = _explorerService.NormalizeShellNamespacePath(path);
                        if (!string.IsNullOrEmpty(normalizedShellPath))
                        {
                            if (_desktopShellItemPathsCache.Contains(normalizedShellPath))
                            {
                                result = true;
                            }
                        }
                    }
                }
            }

            return result;
        }

        private bool HasShortcutToPathInDesktop(string desktopPath, string targetPath)
        {
            return Models.ExplorerAbsorptionLogic.HasShortcutToPathInDesktop(_explorerService, desktopPath, targetPath);
        }

        private bool TryNormalizePath(string path, out string normalizedPath)
        {
            return Models.ExplorerAbsorptionLogic.TryNormalizePath(path, out normalizedPath);
        }

        private bool AreEquivalentDesktopShortcutTargetPath(string path1, string path2)
        {
            return Models.ExplorerAbsorptionLogic.AreEquivalentDesktopShortcutTargetPath(path1, path2);
        }

        private bool TryGetUsersRelativePath(string normalizedPath, out string usersRelativePath)
        {
            return Models.ExplorerAbsorptionLogic.TryGetUsersRelativePath(normalizedPath, out usersRelativePath);
        }



        private bool IsSameOrChildPath(string path, string rootPath)
        {
            return Models.ExplorerAbsorptionLogic.IsSameOrChildPath(path, rootPath);
        }

        /// <summary>
        /// 新しいエクスプローラーウィンドウのパスを既存のタブバーに追加し、
        /// そのエクスプローラーウィンドウを閉じる。
        /// </summary>
        private void AbsorbExplorerWindow(IntPtr newExplorerHwnd, TabBarViewModel targetVM, string path, bool allowSpecialPath)
        {
            try
            {
                if (!allowSpecialPath && !IsPathTabCompatible(path))
                {
                    IgnoreExplorerWindow(newExplorerHwnd);
                    return;
                }

                List<string> selectedItems = _explorerService.GetSelectedItems(newExplorerHwnd);
                int insertIndex = targetVM.Tabs.Count;
                targetVM.InsertTabWithPathAndSelect(path, insertIndex, selectedItems, allowSpecialPath);

                // 既存のエクスプローラーをフォアグラウンドに
                NativeMethods.ForceSetForegroundWindow(targetVM.ExplorerHwnd);

                // 新しいエクスプローラーを閉じる
                _desktopLaunchCandidates.Remove(newExplorerHwnd);
                _desktopInteractiveLaunchCandidates.Remove(newExplorerHwnd);
                _controlPanelTabLaunchCandidates.Remove(newExplorerHwnd);
                _absorbPathRetryCounts.Remove(newExplorerHwnd);
                _hiddenPendingAbsorb.Remove(newExplorerHwnd);
                _ignoredWindows.Add(newExplorerHwnd);
                NativeMethods.PostMessage(newExplorerHwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                // 吸収に失敗した場合: 非表示状態（画面外）から元の位置に戻す
                if (_hiddenPendingAbsorb.Remove(newExplorerHwnd))
                {
                    if (NativeMethods.IsWindow(newExplorerHwnd))
                    {
                        NativeMethods.RECT origRect;
                        if (_hiddenOriginalRects.TryGetValue(newExplorerHwnd, out origRect))
                        {
                            NativeMethods.SetWindowPos(newExplorerHwnd, IntPtr.Zero, origRect.Left, origRect.Top, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
                            _hiddenOriginalRects.Remove(newExplorerHwnd);
                        }
                    }
                }
            }
        }

        private bool IsPathTabCompatible(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            if (_explorerService.IsControlPanelPath(path))
            {
                return false;
            }
            return true;
        }

        private bool IsPathPersistable(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            return true;
        }
    }
}






