using KjTabBar.Models;
using KjTabBar.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;

namespace KjTabBar.ViewModels
{
    public class TabItemViewModel : ViewModelBase
    {
        private const int IconCacheCapacity = 256;
        private static readonly object IconCacheSync = new object();
        private static readonly Dictionary<string, ImageSource> IconCache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly Queue<string> IconCacheOrder = new Queue<string>();
        private string _title;
        private Models.IExplorerService _explorerService;
        private string _path;
        private bool _isActive;
        private ImageSource _iconSource;

        private string _baseTitle;

        public string Title
        {
            get { return _title; }
            set { _title = value; OnPropertyChanged("Title"); }
        }

        public string BaseTitle
        {
            get { return _baseTitle; }
            set { _baseTitle = value; OnPropertyChanged("BaseTitle"); }
        }

        public string Path
        {
            get { return _path; }
            set
            {
                if (string.Equals(_path, value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _path = value;
                OnPropertyChanged("Path");
                UpdateIconSource();
            }
        }

        public bool IsActive
        {
            get { return _isActive; }
            set { _isActive = value; OnPropertyChanged("IsActive"); }
        }

        public ImageSource IconSource
        {
            get { return _iconSource; }
            set { _iconSource = value; OnPropertyChanged("IconSource"); }
        }

        public TabItemViewModel(string path, string title, Models.IExplorerService explorerService)
        {
            _path = path;
            _explorerService = explorerService;
            _baseTitle = string.IsNullOrEmpty(title) ? _explorerService.GetLocalizedHomeTitle() : title;
            _title = _baseTitle;
            _isActive = false;
            UpdateIconSource();
        }

        internal static bool ShouldUseFileAttributeIconLookup(string normalizedPath)
        {
            return ShouldUseFileAttributeIconLookup(normalizedPath, NativeMethods.GetDriveType);
        }

        internal static bool ShouldUseFileAttributeIconLookup(string normalizedPath, Func<string, uint> getDriveType)
        {
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            if (normalizedPath.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return true;
            }

            if (getDriveType == null || normalizedPath.Length < 2 ||
                !char.IsLetter(normalizedPath[0]) || normalizedPath[1] != ':')
            {
                return false;
            }

            try
            {
                string rootPath = normalizedPath.Substring(0, 2) + @"\";
                return getDriveType(rootPath) == NativeMethods.DRIVE_REMOTE;
            }
            catch
            {
                return false;
            }
        }

        private void UpdateIconSource()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                UpdateIconSourceCore();
            }
            finally
            {
                AppLogger.LogSlowOperation("TabItemViewModel", "TabItemViewModel.UpdateIconSource", "UpdateIconSource", stopwatch.Elapsed, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(1));
            }
        }

        private void UpdateIconSourceCore()
        {
            IntPtr pidl = IntPtr.Zero;
            IntPtr fallbackPidl = IntPtr.Zero;
            try
            {
                string normalizedPath = _explorerService != null ? _explorerService.NormalizeKnownPath(_path) : _path;
                if (string.IsNullOrEmpty(normalizedPath))
                {
                    IconSource = null;
                    return;
                }

                ImageSource cachedIcon;
                if (TryGetCachedIcon(normalizedPath, out cachedIcon))
                {
                    IconSource = cachedIcon;
                    return;
                }

                NativeMethods.SHFILEINFO fileInfo = new NativeMethods.SHFILEINFO();
                uint flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON;
                IntPtr result = IntPtr.Zero;
                if (normalizedPath.StartsWith("::{", StringComparison.OrdinalIgnoreCase) ||
                    normalizedPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                {
                    uint dummy;
                    int hr = NativeMethods.SHParseDisplayName(normalizedPath, IntPtr.Zero, out pidl, 0, out dummy);
                    if (hr == 0 && pidl != IntPtr.Zero)
                    {
                        result = NativeMethods.SHGetFileInfo(
                            pidl,
                            0,
                            out fileInfo,
                            (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.SHFILEINFO)),
                            flags | NativeMethods.SHGFI_PIDL);
                    }
                    // コントロールパネル配下項目は単独 GUID 解決だと親アイコンになる場合があるため、
                    // 一次取得が失敗したときのみ配下コンテキスト付きパスでも再取得する。
                    if ((result == IntPtr.Zero || fileInfo.hIcon == IntPtr.Zero) &&
                        _explorerService != null &&
                        _explorerService.IsControlPanelPath(normalizedPath) &&
                        !string.Equals(normalizedPath, _explorerService.AllControlPanelPath, StringComparison.OrdinalIgnoreCase))
                    {
                        string controlPanelCompositePath = "::{26EE0668-A00A-44D7-9371-BEB064C98683}\\0\\" + normalizedPath;
                        uint fallbackDummy;
                        int fallbackHr = NativeMethods.SHParseDisplayName(controlPanelCompositePath, IntPtr.Zero, out fallbackPidl, 0, out fallbackDummy);
                        if (fallbackHr == 0 && fallbackPidl != IntPtr.Zero)
                        {
                            result = NativeMethods.SHGetFileInfo(
                                fallbackPidl,
                                0,
                                out fileInfo,
                                (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.SHFILEINFO)),
                                flags | NativeMethods.SHGFI_PIDL);
                        }
                    }
                    // ホームフォルダ (クイックアクセス) の CLSID は仮想フォルダのため
                    // SHParseDisplayName で解決できない場合がある。
                    // 解決済みパスで再試行する。
                    if ((result == IntPtr.Zero || fileInfo.hIcon == IntPtr.Zero) &&
                        _explorerService != null &&
                        string.Equals(normalizedPath, _explorerService.HomeFolderPath, StringComparison.OrdinalIgnoreCase))
                    {
                        string resolvedHomePath = _explorerService.GetResolvedHomeFolderPath();
                        if (!string.IsNullOrEmpty(resolvedHomePath) &&
                            !string.Equals(resolvedHomePath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (fallbackPidl != IntPtr.Zero)
                            {
                                NativeMethods.ILFree(fallbackPidl);
                                fallbackPidl = IntPtr.Zero;
                            }
                            uint homeDummy;
                            int homeHr = NativeMethods.SHParseDisplayName(resolvedHomePath, IntPtr.Zero, out fallbackPidl, 0, out homeDummy);
                            if (homeHr == 0 && fallbackPidl != IntPtr.Zero)
                            {
                                result = NativeMethods.SHGetFileInfo(
                                    fallbackPidl,
                                    0,
                                    out fileInfo,
                                    (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.SHFILEINFO)),
                                    flags | NativeMethods.SHGFI_PIDL);
                            }
                        }
                    }
                }
                else
                {
                    bool useFileAttributeIconLookup = ShouldUseFileAttributeIconLookup(normalizedPath);
                    uint fileAttributes = useFileAttributeIconLookup ? NativeMethods.FILE_ATTRIBUTE_DIRECTORY : 0;
                    uint fileIconFlags = useFileAttributeIconLookup ? flags | NativeMethods.SHGFI_USEFILEATTRIBUTES : flags;

                    // UNC パスとマップされたネットワークドライブは実体確認で待たされやすいため、
                    // 属性指定で汎用フォルダアイコンを取得する。ローカルパスは従来通り
                    // desktop.ini のカスタムアイコンを優先する。
                    result = NativeMethods.SHGetFileInfo(
                        normalizedPath,
                        fileAttributes,
                        out fileInfo,
                        (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.SHFILEINFO)),
                        fileIconFlags);
                    // ローカルパスが一時的に利用不可の場合は汎用フォルダアイコンにフォールバックする。
                    if (!useFileAttributeIconLookup && (result == IntPtr.Zero || fileInfo.hIcon == IntPtr.Zero))
                    {
                        uint attrs = NativeMethods.FILE_ATTRIBUTE_DIRECTORY;
                        result = NativeMethods.SHGetFileInfo(
                            normalizedPath,
                            attrs,
                            out fileInfo,
                            (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.SHFILEINFO)),
                            flags | NativeMethods.SHGFI_USEFILEATTRIBUTES);
                    }
                }

                if (result == IntPtr.Zero || fileInfo.hIcon == System.IntPtr.Zero)
                {
                    IconSource = null;
                    return;
                }

                try
                {
                    BitmapSource bitmap = Imaging.CreateBitmapSourceFromHIcon(
                        fileInfo.hIcon,
                        System.Windows.Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bitmap.Freeze();
                    IconSource = bitmap;
                    AddCachedIcon(normalizedPath, bitmap);
                }
                finally
                {
                    NativeMethods.DestroyIcon(fileInfo.hIcon);
                }
            }
            catch
            {
                IconSource = null;
            }
            finally
            {
                if (pidl != IntPtr.Zero)
                {
                    NativeMethods.ILFree(pidl);
                }
                if (fallbackPidl != IntPtr.Zero)
                {
                    NativeMethods.ILFree(fallbackPidl);
                }
            }
        }

        private static bool TryGetCachedIcon(string normalizedPath, out ImageSource iconSource)
        {
            lock (IconCacheSync)
            {
                return IconCache.TryGetValue(normalizedPath, out iconSource);
            }
        }

        private static void AddCachedIcon(string normalizedPath, ImageSource iconSource)
        {
            lock (IconCacheSync)
            {
                if (IconCache.ContainsKey(normalizedPath))
                {
                    IconCache[normalizedPath] = iconSource;
                    return;
                }

                while (IconCache.Count >= IconCacheCapacity && IconCacheOrder.Count > 0)
                {
                    IconCache.Remove(IconCacheOrder.Dequeue());
                }

                IconCache[normalizedPath] = iconSource;
                IconCacheOrder.Enqueue(normalizedPath);
            }
        }
    }
}
