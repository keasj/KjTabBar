using System;
using System.IO;
using System.Windows;
using KjTabBar.Helpers;

namespace KjTabBar.Services
{
    internal static class StandardUserRelaunchService
    {
        public const string ShellRelaunchArgument = "--kjtb-shell";

        public static bool ShouldRelaunchAsStandardUser(StartupEventArgs e)
        {
            return !HasStartupArgument(e, ShellRelaunchArgument) && IsRunningAsAdministrator();
        }

        public static bool TryRelaunchAsStandardUser()
        {
            System.Diagnostics.Process currentProcess = null;
            try
            {
                currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                string exePath = currentProcess.MainModule.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    return false;
                }

                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                psi.FileName = "explorer.exe";
                psi.Arguments = "\"" + exePath + "\" " + ShellRelaunchArgument;
                psi.WorkingDirectory = Path.GetDirectoryName(exePath);
                psi.UseShellExecute = true;

                System.Diagnostics.Process relaunched = System.Diagnostics.Process.Start(psi);
                if (relaunched != null)
                {
                    relaunched.Dispose();
                }

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("StandardUserRelaunchService", "Failed to relaunch as standard user.", ex);
                return false;
            }
            finally
            {
                if (currentProcess != null)
                {
                    currentProcess.Dispose();
                }
            }
        }

        private static bool HasStartupArgument(StartupEventArgs e, string argument)
        {
            if (e == null || e.Args == null || string.IsNullOrEmpty(argument))
            {
                return false;
            }

            for (int i = 0; i < e.Args.Length; i++)
            {
                if (string.Equals(e.Args[i], argument, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsRunningAsAdministrator()
        {
            System.Security.Principal.WindowsIdentity identity = null;
            try
            {
                identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                if (identity == null)
                {
                    return false;
                }

                System.Security.Principal.WindowsPrincipal principal =
                    new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("StandardUserRelaunchService", "Failed to determine administrator role.", ex);
                return false;
            }
            finally
            {
                if (identity != null)
                {
                    identity.Dispose();
                }
            }
        }
    }
}