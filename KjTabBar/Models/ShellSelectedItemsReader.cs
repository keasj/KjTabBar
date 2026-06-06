using System;
using System.Collections.Generic;

namespace KjTabBar.Models
{
    internal sealed class ShellSelectedItemsReader
    {
        private readonly Func<object, string, object> _getComProperty;
        private readonly Func<object, string, object[], object> _invokeComMethod;
        private readonly Action<object> _releaseComObject;
        private readonly Action<string, string, string, Exception, TimeSpan> _logErrorThrottled;

        public ShellSelectedItemsReader(
            Func<object, string, object> getComProperty,
            Func<object, string, object[], object> invokeComMethod,
            Action<object> releaseComObject,
            Action<string, string, string, Exception, TimeSpan> logErrorThrottled)
        {
            _getComProperty = getComProperty;
            _invokeComMethod = invokeComMethod;
            _releaseComObject = releaseComObject;
            _logErrorThrottled = logErrorThrottled;
        }

        public List<string> ReadSelectedItemPaths(object document)
        {
            List<string> selectedItems = new List<string>();
            object selected = null;
            try
            {
                selected = _invokeComMethod(document, "SelectedItems", new object[0]);
                object selCountObj = _getComProperty(selected, "Count");
                int selCount = 0;
                if (selCountObj != null)
                {
                    try
                    {
                        selCount = Convert.ToInt32(selCountObj);
                    }
                    catch (Exception ex)
                    {
                        _logErrorThrottled("ExplorerManager", "GetSelectedItemsSelectionCount", "Failed to convert selected item count.", ex, TimeSpan.FromMinutes(5));
                    }
                }

                for (int j = 0; j < selCount; j++)
                {
                    object item = null;
                    try
                    {
                        item = _invokeComMethod(selected, "Item", new object[] { j });
                        string selectedItemPath = _getComProperty(item, "Path") as string;
                        if (!string.IsNullOrEmpty(selectedItemPath))
                        {
                            selectedItems.Add(selectedItemPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logErrorThrottled("ExplorerManager", "GetSelectedItemsEnumerate", "Failed to enumerate a selected item.", ex, TimeSpan.FromMinutes(5));
                    }
                    finally
                    {
                        _releaseComObject(item);
                    }
                }
            }
            finally
            {
                _releaseComObject(selected);
            }

            return selectedItems;
        }
    }
}
