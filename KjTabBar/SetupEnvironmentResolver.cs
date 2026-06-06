using System;
using System.Diagnostics;
using System.Management;
using KjTabBar.Helpers;

namespace KjTabBar
{
    internal static class SetupEnvironmentResolver
    {
        public static string TryGetExplorerOwnerSid(int preferredSessionId)
        {
            Process[] processes = null;
            try
            {
                processes = Process.GetProcessesByName("explorer");
                Process target = null;
                for (int i = 0; i < processes.Length; i++)
                {
                    Process process = processes[i];
                    if (process.SessionId != preferredSessionId)
                    {
                        continue;
                    }

                    if (target == null || IsStartedAfter(process, target))
                    {
                        target = process;
                    }
                }

                if (target != null)
                {
                    return GetProcessOwnerSid(target.Id);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SetupEnvironmentResolver", "Target SID resolution failed.", ex);
            }
            finally
            {
                if (processes != null)
                {
                    for (int i = 0; i < processes.Length; i++)
                    {
                        processes[i].Dispose();
                    }
                }
            }

            return null;
        }

        public static string GetProcessOwnerSid(int processId)
        {
            try
            {
                string query = "SELECT * FROM Win32_Process WHERE ProcessId=" + processId.ToString();
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject process in results)
                    {
                        using (process)
                        {
                            object sid = process.InvokeMethod("GetOwnerSid", null, null);
                            ManagementBaseObject sidResult = sid as ManagementBaseObject;
                            if (sidResult != null)
                            {
                                return sidResult["Sid"] as string;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SetupEnvironmentResolver", "GetOwnerSid failed.", ex);
            }

            return null;
        }

        public static Process FindInstallerUiProcess(int preferredSessionId)
        {
            Process[] processes = null;
            Process result = null;
            try
            {
                processes = Process.GetProcessesByName("msiexec");
                for (int i = 0; i < processes.Length; i++)
                {
                    Process process = processes[i];
                    if (process.SessionId != preferredSessionId || process.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (result == null || IsStartedAfter(process, result))
                    {
                        if (result != null)
                        {
                            result.Dispose();
                        }

                        result = process;
                        processes[i] = null;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SetupEnvironmentResolver", "Failed to find installer UI process.", ex);
            }
            finally
            {
                if (processes != null)
                {
                    for (int i = 0; i < processes.Length; i++)
                    {
                        if (processes[i] != null)
                        {
                            processes[i].Dispose();
                        }
                    }
                }
            }

            return result;
        }

        public static bool IsStartedAfter(Process process, Process other)
        {
            try
            {
                return process.StartTime > other.StartTime;
            }
            catch
            {
                return false;
            }
        }

        public static int GetPreferredSessionId(int fallbackSessionId)
        {
            Process[] explorerProcesses = null;
            try
            {
                explorerProcesses = Process.GetProcessesByName("explorer");
                for (int i = 0; i < explorerProcesses.Length; i++)
                {
                    if (explorerProcesses[i].MainWindowHandle != IntPtr.Zero)
                    {
                        return explorerProcesses[i].SessionId;
                    }
                }
            }
            catch
            {
                AppLogger.LogInfo("SetupEnvironmentResolver", "GetPreferredSessionId failed. Falling back to installer session.");
            }
            finally
            {
                if (explorerProcesses != null)
                {
                    for (int i = 0; i < explorerProcesses.Length; i++)
                    {
                        explorerProcesses[i].Dispose();
                    }
                }
            }

            return fallbackSessionId;
        }

        public static bool IsRegularUserSid(string userSid)
        {
            if (string.IsNullOrEmpty(userSid))
            {
                return false;
            }

            return userSid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase);
        }
    }
}
