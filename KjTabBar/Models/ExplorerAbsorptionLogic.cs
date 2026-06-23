using System;
using System.Collections.Generic;
using System.IO;

namespace KjTabBar.Models
{
    public static class ExplorerAbsorptionLogic
    {
        private static readonly object ShortcutTargetCacheSync = new object();
        private static readonly Dictionary<string, ShortcutTargetCacheEntry> ShortcutTargetCache = new Dictionary<string, ShortcutTargetCacheEntry>(StringComparer.OrdinalIgnoreCase);

        public static bool IsDesktopShortcutTargetPath(IExplorerService explorerService, string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            string userDesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string commonDesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

            if (HasShortcutToPathInDesktop(explorerService, userDesktopPath, path)) return true;
            if (HasShortcutToPathInDesktop(explorerService, commonDesktopPath, path)) return true;

            return false;
        }

        public static bool HasShortcutToPathInDesktop(IExplorerService explorerService, string desktopPath, string targetPath)
        {
            if (string.IsNullOrEmpty(desktopPath) || !Directory.Exists(desktopPath))
            {
                return false;
            }

            string normalizedTargetShellPath = explorerService.NormalizeShellNamespacePath(targetPath);
            string[] shortcutFiles;
            try
            {
                shortcutFiles = Directory.GetFiles(desktopPath, "*.lnk", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return false;
            }

            for (int i = 0; i < shortcutFiles.Length; i++)
            {
                string resolvedPath = ResolveShortcutTargetCached(explorerService, shortcutFiles[i]);
                string normalizedResolvedShellPath = explorerService.NormalizeShellNamespacePath(resolvedPath);

                if (!string.IsNullOrEmpty(normalizedTargetShellPath) &&
                    !string.IsNullOrEmpty(normalizedResolvedShellPath) &&
                    normalizedResolvedShellPath.Equals(normalizedTargetShellPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (AreEquivalentDesktopShortcutTargetPath(resolvedPath, targetPath))
                {
                    return true;
                }
            }

            return false;
        }

        internal static void ClearShortcutTargetCacheForTests()
        {
            lock (ShortcutTargetCacheSync)
            {
                ShortcutTargetCache.Clear();
            }
        }

        private static string ResolveShortcutTargetCached(IExplorerService explorerService, string shortcutPath)
        {
            if (explorerService == null || string.IsNullOrEmpty(shortcutPath))
            {
                return shortcutPath;
            }

            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(shortcutPath);
            }
            catch
            {
                return explorerService.ResolveShortcutTarget(shortcutPath);
            }

            DateTime lastWriteTimeUtc = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.MinValue;
            long length = fileInfo.Exists ? fileInfo.Length : -1;

            lock (ShortcutTargetCacheSync)
            {
                ShortcutTargetCacheEntry entry;
                if (ShortcutTargetCache.TryGetValue(shortcutPath, out entry) &&
                    entry.LastWriteTimeUtc == lastWriteTimeUtc &&
                    entry.Length == length)
                {
                    return entry.ResolvedPath;
                }
            }

            string resolvedPath = explorerService.ResolveShortcutTarget(shortcutPath);
            lock (ShortcutTargetCacheSync)
            {
                ShortcutTargetCache[shortcutPath] = new ShortcutTargetCacheEntry(lastWriteTimeUtc, length, resolvedPath);
            }

            return resolvedPath;
        }

        public static bool TryNormalizePath(string path, out string normalizedPath)
        {
            normalizedPath = null;
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                normalizedPath = Path.GetFullPath(path).TrimEnd('\\');
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool AreEquivalentDesktopShortcutTargetPath(string path1, string path2)
        {
            if (string.IsNullOrEmpty(path1) || string.IsNullOrEmpty(path2)) return false;

            string normalizedPath1;
            string normalizedPath2;
            if (TryNormalizePath(path1, out normalizedPath1) && TryNormalizePath(path2, out normalizedPath2))
            {
                if (normalizedPath1.Equals(normalizedPath2, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string usersRelativePath1;
                string usersRelativePath2;
                if (TryGetUsersRelativePath(normalizedPath1, out usersRelativePath1) &&
                    TryGetUsersRelativePath(normalizedPath2, out usersRelativePath2) &&
                    usersRelativePath1.Equals(usersRelativePath2, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return path1.TrimEnd('\\').Equals(path2.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryGetUsersRelativePath(string normalizedPath, out string usersRelativePath)
        {
            usersRelativePath = null;
            if (string.IsNullOrEmpty(normalizedPath)) return false;

            // ドライブレターまたはネットワーク共有直下の "Users" フォルダか判定する
            // "C:\Users\..." や "E:\Users\..." などの形式のみマッチさせる (単純な "\Users\" 検索による誤爆を防ぐ)
            if (normalizedPath.Length >= 3 && normalizedPath[1] == ':' && normalizedPath[2] == '\\')
            {
                string withoutDrive = normalizedPath.Substring(3);
                if (withoutDrive.StartsWith("Users\\", StringComparison.OrdinalIgnoreCase))
                {
                    string relativeToUsers = withoutDrive.Substring(6); // "username\..." となる
                    int separatorIndex = relativeToUsers.IndexOf('\\');
                    if (separatorIndex >= 0 && separatorIndex + 1 < relativeToUsers.Length)
                    {
                        usersRelativePath = relativeToUsers.Substring(separatorIndex + 1);
                        return true;
                    }
                }
            }
            // ネットワークパス "\\server\Users\..." 等の対応
            else if (normalizedPath.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase))
            {
                int shareEndIndex = normalizedPath.IndexOf('\\', 2);
                if (shareEndIndex > 2)
                {
                    int rootDirEndIndex = normalizedPath.IndexOf('\\', shareEndIndex + 1);
                    if (rootDirEndIndex > shareEndIndex)
                    {
                        string folder = normalizedPath.Substring(rootDirEndIndex + 1);
                        if (folder.StartsWith("Users\\", StringComparison.OrdinalIgnoreCase))
                        {
                            string relativeToUsers = folder.Substring(6);
                            int separatorIndex = relativeToUsers.IndexOf('\\');
                            if (separatorIndex >= 0 && separatorIndex + 1 < relativeToUsers.Length)
                            {
                                usersRelativePath = relativeToUsers.Substring(separatorIndex + 1);
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private sealed class ShortcutTargetCacheEntry
        {
            public ShortcutTargetCacheEntry(DateTime lastWriteTimeUtc, long length, string resolvedPath)
            {
                LastWriteTimeUtc = lastWriteTimeUtc;
                Length = length;
                ResolvedPath = resolvedPath;
            }

            public DateTime LastWriteTimeUtc { get; private set; }
            public long Length { get; private set; }
            public string ResolvedPath { get; private set; }
        }

        public static bool IsSameOrChildPath(string path, string rootPath)
        {
            string normalizedPath;
            if (!TryNormalizePath(path, out normalizedPath)) return false;

            string normalizedRoot;
            if (!TryNormalizePath(rootPath, out normalizedRoot)) return false;

            if (normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)) return true;
            return normalizedPath.StartsWith(normalizedRoot + "\\", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldAbsorbDesktopOriginPath(
            bool isDesktopFolderPath,
            bool isDesktopShortcutTargetPath,
            bool isDesktopShellItemPath,
            bool isDesktopSpecialShellPath)
        {
            if (isDesktopFolderPath) return true;
            if (isDesktopShortcutTargetPath) return true;
            if (isDesktopShellItemPath) return true;
            if (isDesktopSpecialShellPath) return true;
            return false;
        }
    }
}
