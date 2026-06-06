using System;

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
            if (string.IsNullOrEmpty(targetPath))
            {
                return null;
            }

            for (int i = 0; i < itemCount; i++)
            {
                object item = null;
                try
                {
                    item = _invokeComMethod(folderItems, "Item", new object[] { i });
                    string itemPath = _getComProperty(item, "Path") as string;
                    if (_shellItemPathResolver.AreEquivalentItemPaths(itemPath, targetPath))
                    {
                        return item;
                    }
                }
                catch (Exception ex)
                {
                    _logErrorThrottled("ExplorerManager", "FindFolderItemByPathEnumerate", "Failed while enumerating folder items.", ex, TimeSpan.FromMinutes(5));
                }

                _releaseComObject(item);
            }

            string parseName = _shellItemPathResolver.GetItemParseName(targetPath);
            if (string.IsNullOrEmpty(parseName))
            {
                return null;
            }

            return _invokeComMethod(folder, "ParseName", new object[] { parseName });
        }
    }
}
