using System;
using System.IO;
using KjTabBar.Helpers;
using Microsoft.Win32;

namespace KjTabBar
{
    internal static class SetupRegistryManager
    {
        private const string StartupRunSubKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupApprovedRunSubKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        public static void RegisterStartupRunValue(string exePath, int preferredSessionId, string targetSid)
        {
            string runValue = SetupCustomActions.BuildStartupRunCommand(exePath);
            try
            {
                if (!string.IsNullOrEmpty(targetSid))
                {
                    using (RegistryKey usersKey = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default))
                    using (RegistryKey runKey = usersKey.CreateSubKey(targetSid + @"\" + StartupRunSubKeyPath))
                    {
                        if (runKey != null)
                        {
                            runKey.SetValue(SetupCustomActions.StartupValueName, runValue, RegistryValueKind.String);
                        }
                    }
                }
                else
                {
                    AppLogger.LogInfo("SetupRegistryManager", "Skipped startup Run registration because the target user SID could not be resolved.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SetupRegistryManager", "Startup Run registration failed.", ex);
            }
        }

        public static void RemoveStartupRegistryValues(string installedExePath)
        {
            // HKCU に対する削除（アンインストーラーがユーザー権限で動いている場合）
            RemoveStartupRegistryValuesForHive(RegistryHive.CurrentUser, RegistryView.Registry64, installedExePath);
            RemoveStartupRegistryValuesForHive(RegistryHive.CurrentUser, RegistryView.Registry32, installedExePath);

            // SYSTEM 権限で動いている場合、HKCU は SYSTEM のものになってしまうため、
            // HKEY_USERS にロードされている全ユーザープロファイルから削除する。
            RemoveStartupRegistryValuesFromAllUsers(installedExePath);
        }

        private static void RemoveStartupRegistryValuesFromAllUsers(string installedExePath)
        {
            RegistryKey usersKey = null;
            try
            {
                usersKey = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default);
                if (usersKey == null) return;
                
                string[] subKeyNames = usersKey.GetSubKeyNames();
                for (int i = 0; i < subKeyNames.Length; i++)
                {
                    string userSid = subKeyNames[i];
                    if (userSid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!SetupEnvironmentResolver.IsRegularUserSid(userSid)) continue;

                    string runPath = userSid + @"\" + StartupRunSubKeyPath;
                    if (!ShouldDeleteStartupEntry(usersKey, runPath, installedExePath))
                    {
                        continue;
                    }

                    DeleteRegistryValue(usersKey, runPath);
                    DeleteRegistryValue(usersKey, userSid + @"\" + StartupApprovedRunSubKeyPath);
                }
            }
            catch
            {
                AppLogger.LogInfo("SetupRegistryManager", "Failed while enumerating user hives for startup cleanup.");
            }
            finally
            {
                if (usersKey != null)
                {
                    usersKey.Dispose();
                }
            }
        }

        private static void RemoveStartupRegistryValuesForHive(
            RegistryHive hive,
            RegistryView view,
            string installedExePath)
        {
            RegistryKey baseKey = null;
            try
            {
                baseKey = RegistryKey.OpenBaseKey(hive, view);
                if (baseKey == null)
                {
                    return;
                }

                if (!ShouldDeleteStartupEntry(baseKey, StartupRunSubKeyPath, installedExePath))
                {
                    return;
                }

                DeleteRegistryValue(baseKey, StartupRunSubKeyPath);
                DeleteRegistryValue(baseKey, StartupApprovedRunSubKeyPath);
            }
            catch
            {
                AppLogger.LogInfo("SetupRegistryManager", "Failed while opening base registry hive for startup cleanup.");
            }
            finally
            {
                if (baseKey != null)
                {
                    baseKey.Dispose();
                }
            }
        }

        private static bool ShouldDeleteStartupEntry(RegistryKey rootKey, string subKeyPath, string installedExePath)
        {
            RegistryKey key = null;
            try
            {
                key = rootKey.OpenSubKey(subKeyPath, false);
                if (key == null)
                {
                    return false;
                }

                string runCommand = key.GetValue(SetupCustomActions.StartupValueName) as string;
                return SetupCustomActions.IsStartupRunCommandForExecutable(runCommand, installedExePath);
            }
            catch
            {
                AppLogger.LogInfo("SetupRegistryManager", "Failed while reading startup registry value.");
                return false;
            }
            finally
            {
                if (key != null)
                {
                    key.Dispose();
                }
            }
        }

        private static void DeleteRegistryValue(RegistryKey rootKey, string subKeyPath)
        {
            RegistryKey key = null;
            try
            {
                key = rootKey.OpenSubKey(subKeyPath, true);
                if (key != null && key.GetValue(SetupCustomActions.StartupValueName) != null)
                {
                    key.DeleteValue(SetupCustomActions.StartupValueName, false);
                }
            }
            catch
            {
                AppLogger.LogInfo("SetupRegistryManager", "Failed while deleting startup registry value.");
            }
            finally
            {
                if (key != null)
                {
                    key.Dispose();
                }
            }
        }
    }
}
