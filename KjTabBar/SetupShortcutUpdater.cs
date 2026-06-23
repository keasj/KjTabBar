using System;
using System.IO;
using System.Runtime.InteropServices;
using KjTabBar.Helpers;

namespace KjTabBar
{
    internal static class SetupShortcutUpdater
    {
        public static void UpdatePostInstallShortcuts(string exePath, string workingDirectory)
        {
            string desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "KjTabBar.lnk");
            string programsShortcutDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "KjTabBar");
            string programsShortcut = Path.Combine(programsShortcutDir, "KjTabBar.lnk");
            UpdateShortcut(desktopShortcut, exePath, workingDirectory);
            UpdateShortcut(programsShortcut, exePath, workingDirectory);
        }

        public static void UpdateShortcut(string shortcutPath, string exePath, string workingDirectory)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                string shortcutDir = Path.GetDirectoryName(shortcutPath);
                if (!string.IsNullOrEmpty(shortcutDir) && !Directory.Exists(shortcutDir))
                {
                    Directory.CreateDirectory(shortcutDir);
                }

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    AppLogger.LogInfo("SetupShortcutUpdater", "WScript.Shell COM type is unavailable.");
                    return;
                }

                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
                shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
                shortcutType.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { string.Empty });
                shortcutType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "KjTabBar" });
                shortcutType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { exePath + ",0" });
                shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SetupShortcutUpdater", "Shortcut update failed.", ex);
            }
            finally
            {
                ReleaseComObject(shortcut);
                ReleaseComObject(shell);
            }
        }

        public static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }
    }
}
