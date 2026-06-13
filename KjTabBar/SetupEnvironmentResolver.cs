using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using KjTabBar.Helpers;

namespace KjTabBar
{
    internal static class SetupEnvironmentResolver
    {
        public static string TryGetExplorerOwnerSid(int preferredSessionId)
        {
            string sessionUserSid = TryGetSessionUserSid(preferredSessionId);
            if (!string.IsNullOrEmpty(sessionUserSid))
            {
                return sessionUserSid;
            }

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
            string sid = TryGetProcessOwnerSidDirectly(processId);
            if (!string.IsNullOrEmpty(sid))
            {
                return sid;
            }

            return TryGetProcessOwnerSidFromAccount(processId);
        }

        internal static string TryGetSessionUserSid(int sessionId)
        {
            if (sessionId < 0)
            {
                return null;
            }

            string sid = TryGetSessionUserSidFromToken(sessionId);
            if (!string.IsNullOrEmpty(sid))
            {
                return sid;
            }

            try
            {
                string user = TryGetSessionInformationString(sessionId, NativeMethods.WTS_INFO_CLASS.WTSUserName);
                if (string.IsNullOrEmpty(user))
                {
                    return null;
                }

                string domain = TryGetSessionInformationString(sessionId, NativeMethods.WTS_INFO_CLASS.WTSDomainName);
                NTAccount account = string.IsNullOrEmpty(domain)
                    ? new NTAccount(user)
                    : new NTAccount(domain, user);
                SecurityIdentifier securityIdentifier = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
                return securityIdentifier.Value;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SetupEnvironmentResolver", "Session user SID resolution failed.", ex);
                return null;
            }
        }

        private static string TryGetSessionUserSidFromToken(int sessionId)
        {
            IntPtr tokenHandle = IntPtr.Zero;
            try
            {
                if (!NativeMethods.WTSQueryUserToken(sessionId, out tokenHandle) || tokenHandle == IntPtr.Zero)
                {
                    return null;
                }

                using (WindowsIdentity identity = new WindowsIdentity(tokenHandle))
                {
                    return identity.User != null ? identity.User.Value : null;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SetupEnvironmentResolver", "Session user token SID resolution failed.", ex);
                return null;
            }
            finally
            {
                if (tokenHandle != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(tokenHandle);
                }
            }
        }

        private static string TryGetSessionInformationString(int sessionId, NativeMethods.WTS_INFO_CLASS infoClass)
        {
            IntPtr buffer = IntPtr.Zero;
            int bytesReturned = 0;
            try
            {
                if (!NativeMethods.WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out buffer, out bytesReturned) || buffer == IntPtr.Zero || bytesReturned <= 1)
                {
                    return null;
                }

                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    NativeMethods.WTSFreeMemory(buffer);
                }
            }
        }

        private static string TryGetProcessOwnerSidDirectly(int processId)
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

        private static string TryGetProcessOwnerSidFromAccount(int processId)
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
                            object owner = process.InvokeMethod("GetOwner", null, null);
                            ManagementBaseObject ownerResult = owner as ManagementBaseObject;
                            if (ownerResult == null)
                            {
                                continue;
                            }

                            string user = ownerResult["User"] as string;
                            if (string.IsNullOrEmpty(user))
                            {
                                continue;
                            }

                            string domain = ownerResult["Domain"] as string;
                            NTAccount account = string.IsNullOrEmpty(domain)
                                ? new NTAccount(user)
                                : new NTAccount(domain, user);
                            SecurityIdentifier sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
                            return sid.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SetupEnvironmentResolver", "GetOwner fallback failed.", ex);
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
