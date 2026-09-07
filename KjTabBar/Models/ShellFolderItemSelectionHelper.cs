using System;
using System.Collections.Generic;

namespace KjTabBar.Models
{
    internal sealed class ShellFolderItemSelectionHelper
    {
        private readonly Func<object, string, object> _getComProperty;
        private readonly Func<object, string, object[], object> _invokeComMethod;
        private readonly Action<object> _releaseComObject;
        private readonly ShellItemPathResolver _shellItemPathResolver;
        private readonly Action<string, string, string, Exception, TimeSpan> _logErrorThrottled;

        public ShellFolderItemSelectionHelper(
            Func<object, string, object> getComProperty,
            Func<object, string, object[], object> invokeComMethod,
            Action<object> releaseComObject,
            ShellItemPathResolver shellItemPathResolver,
            Action<string, string, string, Exception, TimeSpan> logErrorThrottled)
        {
            _getComProperty = getComProperty;
            _invokeComMethod = invokeComMethod;
            _releaseComObject = releaseComObject;
            _shellItemPathResolver = shellItemPathResolver;
            _logErrorThrottled = logErrorThrottled;
        }

        public int GetComCollectionCount(object comCollection)
        {
            if (comCollection == null)
            {
                return 0;
            }

            object countObject = _getComProperty(comCollection, "Count");
            if (countObject == null)
            {
                return 0;
            }

            try
            {
                return Convert.ToInt32(countObject);
            }
            catch (Exception ex)
            {
                _logErrorThrottled("ExplorerManager", "GetComCollectionCount", "Failed to convert COM collection count.", ex, TimeSpan.FromMinutes(5));
                return 0;
            }
        }

        public object FindFolderItemByPath(object folder, object folderItems, int itemCount, string targetPath)
        {
            return FindFolderItemsByPaths(folder, folderItems, itemCount, new[] { targetPath })[0];
        }

        public object[] FindFolderItemsByPaths(object folder, object folderItems, int itemCount, IList<string> targetPaths)
        {
            object[] matches = new object[targetPaths.Count];
            int remaining = targetPaths.Count;
            for (int i = 0; i < itemCount && remaining > 0; i++)
            {
                object item = null;
                bool retained = false;
                try
                {
                    item = _invokeComMethod(folderItems, "Item", new object[] { i });
                    string itemPath = _getComProperty(item, "Path") as string;
                    for (int j = 0; j < targetPaths.Count; j++)
                    {
                        if (matches[j] == null && _shellItemPathResolver.AreEquivalentItemPaths(itemPath, targetPaths[j]))
                        {
                            matches[j] = item;
                            retained = true;
                            remaining--;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logErrorThrottled("ExplorerManager", "FindFolderItemByPathEnumerate", "Failed while enumerating folder items.", ex, TimeSpan.FromMinutes(5));
                }
                finally
                {
                    if (!retained) _releaseComObject(item);
                }
            }
            for (int j = 0; j < targetPaths.Count; j++)
            {
                if (matches[j] != null || string.IsNullOrEmpty(targetPaths[j])) continue;
                string parseName = _shellItemPathResolver.GetItemParseName(targetPaths[j]);
                if (string.IsNullOrEmpty(parseName)) continue;
                try { matches[j] = _invokeComMethod(folder, "ParseName", new object[] { parseName }); }
                catch (Exception ex)
                {
                    _logErrorThrottled("ExplorerManager", "FindFolderItemParseName", "Failed to resolve a folder item.", ex, TimeSpan.FromMinutes(5));
                }
            }
            return matches;
        }
    }
}
