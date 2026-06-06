using System;
using System.Collections.Generic;
using KjTabBar.Helpers;

namespace KjTabBar.Models
{
    internal sealed class DesktopShellItemPathCache
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);
        private readonly IExplorerService _explorerService;
        private readonly object _sync = new object();
        private HashSet<string> _paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastCacheUtc = DateTime.MinValue;

        public DesktopShellItemPathCache(IExplorerService explorerService)
        {
            _explorerService = explorerService;
        }

        public bool Contains(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            DateTime nowUtc = DateTime.UtcNow;
            if (NeedsUpdate(nowUtc))
            {
                Refresh(nowUtc);
            }

            lock (_sync)
            {
                if (_paths.Contains(path))
                {
                    return true;
                }

                string normalizedPath;
                if (ExplorerAbsorptionLogic.TryNormalizePath(path, out normalizedPath) && _paths.Contains(normalizedPath))
                {
                    return true;
                }

                string normalizedShellPath = _explorerService.NormalizeShellNamespacePath(path);
                return !string.IsNullOrEmpty(normalizedShellPath) && _paths.Contains(normalizedShellPath);
            }
        }

        private bool NeedsUpdate(DateTime nowUtc)
        {
            lock (_sync)
            {
                return (nowUtc - _lastCacheUtc) > CacheDuration;
            }
        }

        private void Refresh(DateTime nowUtc)
        {
            HashSet<string> newCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool success = false;
            object shellObject = null;
            object desktopFolder = null;
            object desktopItems = null;
            try
            {
                if (ShellWindowComInterop.TryGetShellApplication(out shellObject))
                {
                    desktopFolder = ShellWindowComInterop.InvokeComMethod(shellObject, "NameSpace", 0);
                    if (desktopFolder != null)
                    {
                        desktopItems = ShellWindowComInterop.InvokeComMethod(desktopFolder, "Items");
                        object countObj = ShellWindowComInterop.GetComProperty(desktopItems, "Count");
                        if (countObj != null)
                        {
                            int count = 0;
                            try { count = Convert.ToInt32(countObj); } catch (Exception ex) { AppLogger.LogErrorThrottled("DesktopShellItemPathCache", "DesktopShellItemCountConvert", "Failed to convert desktop item count.", ex, TimeSpan.FromMinutes(5)); }
 
                            for (int i = 0; i < count; i++)
                            {
                                AddDesktopItemPath(desktopItems, i, newCache);
                            }
                            success = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogErrorThrottled("DesktopShellItemPathCache", "DesktopShellItemCacheRefresh", "Failed to refresh desktop shell item cache.", ex, TimeSpan.FromMinutes(5));
            }
            finally
            {
                ShellWindowComInterop.ReleaseComObjectSafe(desktopItems);
                ShellWindowComInterop.ReleaseComObjectSafe(desktopFolder);
            }

            if (success)
            {
                lock (_sync)
                {
                    _paths = newCache;
                    _lastCacheUtc = nowUtc;
                }
            }
        }

        private void AddDesktopItemPath(object desktopItems, int index, HashSet<string> newCache)
        {
            object item = null;
            try
            {
                item = ShellWindowComInterop.InvokeComMethod(desktopItems, "Item", index);
                if (item == null) return;
 
                string itemPath = ShellWindowComInterop.GetComProperty(item, "Path") as string;
                if (!string.IsNullOrEmpty(itemPath))
                {
                    newCache.Add(itemPath);
 
                    string normalizedItemShellPath = _explorerService.NormalizeShellNamespacePath(itemPath);
                    if (!string.IsNullOrEmpty(normalizedItemShellPath))
                    {
                        newCache.Add(normalizedItemShellPath);
                    }
 
                    string normalizedPath;
                    if (ExplorerAbsorptionLogic.TryNormalizePath(itemPath, out normalizedPath))
                    {
                        newCache.Add(normalizedPath);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogErrorThrottled("DesktopShellItemPathCache", "DesktopShellItemEnumerate", "Failed to enumerate a desktop shell item.", ex, TimeSpan.FromMinutes(5));
            }
            finally
            {
                ShellWindowComInterop.ReleaseComObjectSafe(item);
            }
        }
    }
}