using System;
using System.Runtime.InteropServices;
using KjTabBar.Helpers;
using KjTabBar.Models;

namespace KjTabBar.Helpers
{
    internal static class ShellFolderPicker
    {
        public static string BrowseForFolder(string title, IExplorerService explorerService)
        {
            object shellApp = null;
            object folderObj = null;
            object selfObj = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return null;

                shellApp = Activator.CreateInstance(shellType);

                // BrowseForFolder: 0x0040(BIF_NEWDIALOGSTYLE) | 0x0200(BIF_NONEWFOLDERBUTTON)
                folderObj = shellType.InvokeMember(
                    "BrowseForFolder",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    shellApp,
                    new object[] { 0, title, 0x0040 | 0x0200, 0 });
                if (folderObj != null)
                {
                    selfObj = folderObj.GetType().InvokeMember(
                        "Self",
                        System.Reflection.BindingFlags.GetProperty,
                        null,
                        folderObj,
                        null);
                    if (selfObj != null)
                    {
                        string selectedPath = selfObj.GetType().InvokeMember(
                            "Path",
                            System.Reflection.BindingFlags.GetProperty,
                            null,
                            selfObj,
                            null) as string;
                        if (string.IsNullOrEmpty(selectedPath))
                        {
                            try
                            {
                                selectedPath = selfObj.GetType().InvokeMember(
                                    "ExtendedProperty",
                                    System.Reflection.BindingFlags.InvokeMethod,
                                    null,
                                    selfObj,
                                    new object[] { "System.ParsingPath" }) as string;
                            }
                            catch
                            {
                            }
                        }
                        if (string.IsNullOrEmpty(selectedPath))
                        {
                            try
                            {
                                selectedPath = selfObj.GetType().InvokeMember(
                                    "ExtendedProperty",
                                    System.Reflection.BindingFlags.InvokeMethod,
                                    null,
                                    selfObj,
                                    new object[] { "System.ItemPathDisplay" }) as string;
                            }
                            catch
                            {
                            }
                        }
                        if (string.IsNullOrEmpty(selectedPath))
                        {
                            try
                            {
                                string name = selfObj.GetType().InvokeMember(
                                    "Name",
                                    System.Reflection.BindingFlags.GetProperty,
                                    null,
                                    selfObj,
                                    null) as string;
                                selectedPath = explorerService.MapLocationNameToKnownShellPath(name);
                            }
                            catch
                            {
                            }
                        }

                        if (!string.IsNullOrEmpty(selectedPath))
                        {
                            selectedPath = explorerService.NormalizeKnownPath(selectedPath);
                        }
                        return selectedPath;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ShellFolderPicker", "Failed to browse for folder.", ex);
            }
            finally
            {
                if (selfObj != null && Marshal.IsComObject(selfObj))
                {
                    Marshal.ReleaseComObject(selfObj);
                }
                if (folderObj != null && Marshal.IsComObject(folderObj))
                {
                    Marshal.ReleaseComObject(folderObj);
                }
                if (shellApp != null && Marshal.IsComObject(shellApp))
                {
                    Marshal.ReleaseComObject(shellApp);
                }
            }
            return null;
        }
    }
}
