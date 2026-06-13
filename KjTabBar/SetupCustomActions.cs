using System;
using System.Collections;
using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using KjTabBar.Helpers;
using Microsoft.Win32;

namespace KjTabBar
{
    [RunInstaller(true)]
    public class SetupCustomActions : Installer
    {
        internal const string StartupValueName = "KjTabBar";
        internal const string PostInstallHelperArgument = "--kjtb-post-install";
        private const string SetupExePathEnvironmentName = "KJTB_EXE_PATH";
        private const string SetupWorkingDirectoryEnvironmentName = "KJTB_WORKING_DIRECTORY";
        private const string SetupTargetDirParameterName = "targetdir";
        private const string StartupRunSubKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupApprovedRunSubKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        public SetupCustomActions()
        {
        }

        public override void Install(IDictionary stateSaver)
        {
            base.Install(stateSaver);
            LaunchApplicationDelayed();
        }

        private void LaunchApplicationDelayed()
        {
            try
            {
                string exePath;
                string targetDir;
                if (TryGetInstalledExecutablePathFromContext(out exePath, out targetDir))
                {
                    Process installerProcess = null;
                    int installerProcessId;
                    int preferredSessionId;
                    try
                    {
                        installerProcess = Process.GetCurrentProcess();
                        installerProcessId = installerProcess.Id;
                        preferredSessionId = SetupEnvironmentResolver.GetPreferredSessionId(installerProcess.SessionId);
                    }
                    finally
                    {
                        if (installerProcess != null)
                        {
                            installerProcess.Dispose();
                        }
                    }

                    // ユーザー UI 側の msiexec が閉じた後に、ショートカットを更新してからユーザーシェル経由で本体を起動する。
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = exePath;
                    psi.Arguments = BuildPostInstallHelperArguments(installerProcessId, preferredSessionId);
                    psi.WorkingDirectory = targetDir;
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.EnvironmentVariables[SetupExePathEnvironmentName] = exePath;
                    psi.EnvironmentVariables[SetupWorkingDirectoryEnvironmentName] = targetDir;

                    Process process = Process.Start(psi);
                    if (process != null)
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                AppLogger.LogInfo("SetupCustomActions", "LaunchApplicationDelayed failed.");
                // 起動失敗してもインストール自体は成功とする
            }
        }

        internal static string BuildPostInstallHelperArguments(int installerProcessId, int preferredSessionId)
        {
            return PostInstallHelperArgument + " " + installerProcessId.ToString() + " " + preferredSessionId.ToString();
        }

        internal static bool IsPostInstallHelperRequest(string[] args)
        {
            return args != null && args.Length > 0 && string.Equals(args[0], PostInstallHelperArgument, StringComparison.OrdinalIgnoreCase);
        }

        internal static void RunPostInstallHelper(string[] args)
        {
            try
            {
                int installerProcessId;
                int preferredSessionId;
                if (!TryParsePostInstallHelperArguments(args, out installerProcessId, out preferredSessionId))
                {
                    AppLogger.LogInfo("SetupCustomActions", "Skipping post-install helper because arguments are invalid.");
                    return;
                }

                string exePath = Environment.GetEnvironmentVariable(SetupExePathEnvironmentName);
                string workingDirectory = Environment.GetEnvironmentVariable(SetupWorkingDirectoryEnvironmentName);
                if (!IsPostInstallHelperEnvironmentTrusted(exePath, workingDirectory))
                {
                    AppLogger.LogInfo("SetupCustomActions", "Skipping post-install helper because environment values are not trusted.");
                    return;
                }

                WaitForInstallerExit(installerProcessId, preferredSessionId);
                SetupShortcutUpdater.UpdatePostInstallShortcuts(exePath, workingDirectory);
                string targetSid = SetupEnvironmentResolver.TryGetExplorerOwnerSid(preferredSessionId);
                SetupRegistryManager.RegisterStartupRunValue(exePath, preferredSessionId, targetSid);
                StartInstalledApplicationThroughExplorer(exePath);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SetupCustomActions", "Post-install helper failed.", ex);
            }
        }

        private static bool TryParsePostInstallHelperArguments(string[] args, out int installerProcessId, out int preferredSessionId)
        {
            installerProcessId = 0;
            preferredSessionId = 0;
            if (!IsPostInstallHelperRequest(args) || args.Length != 3)
            {
                return false;
            }

            return int.TryParse(args[1], out installerProcessId) && int.TryParse(args[2], out preferredSessionId);
        }

        internal static bool IsPostInstallHelperEnvironmentTrusted(string exePath, string workingDirectory)
        {
            if (string.IsNullOrEmpty(exePath) || string.IsNullOrEmpty(workingDirectory))
            {
                return false;
            }

            try
            {
                string installedExePath;
                string installedTargetDir;
                if (!TryGetInstalledExecutablePath(out installedExePath, out installedTargetDir))
                {
                    return false;
                }

                if (!File.Exists(exePath) || !Directory.Exists(workingDirectory))
                {
                    return false;
                }

                string normalizedExePath = Path.GetFullPath(exePath);
                string normalizedInstalledExePath = Path.GetFullPath(installedExePath);
                string normalizedWorkingDirectory = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string normalizedInstalledTargetDir = Path.GetFullPath(installedTargetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                return string.Equals(normalizedExePath, normalizedInstalledExePath, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(normalizedWorkingDirectory, normalizedInstalledTargetDir, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SetupCustomActions", "Post-install helper environment validation failed.", ex);
                return false;
            }
        }

        private static void WaitForInstallerExit(int installerProcessId, int preferredSessionId)
        {
            Process process = SetupEnvironmentResolver.FindInstallerUiProcess(preferredSessionId);
            if (process == null)
            {
                try
                {
                    process = Process.GetProcessById(installerProcessId);
                }
                catch
                {
                    return;
                }
            }

            try
            {
                process.WaitForExit(600000);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SetupCustomActions", "Waiting for installer process failed.", ex);
            }
            finally
            {
                process.Dispose();
            }
        }

        private static Process FindInstallerUiProcess(int preferredSessionId)
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
                AppLogger.LogError("SetupCustomActions", "Failed to find installer UI process.", ex);
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

        private static bool IsStartedAfter(Process process, Process other)
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



        private static void StartInstalledApplicationThroughExplorer(string exePath)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "explorer.exe";
                psi.Arguments = QuoteCommandLineArgument(exePath);
                psi.UseShellExecute = false;
                Process process = Process.Start(psi);
                if (process != null)
                {
                    process.Dispose();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SetupCustomActions", "Starting installed application through Explorer failed.", ex);
            }
        }

        private static string QuoteCommandLineArgument(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        internal static string BuildStartupRunCommand(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
            {
                return null;
            }

            return "\"" + exePath + "\"";
        }

        internal static bool IsStartupRunCommandForExecutable(string runCommand, string installedExePath)
        {
            if (string.IsNullOrEmpty(runCommand) || string.IsNullOrEmpty(installedExePath))
            {
                return false;
            }

            string expandedCommand = Environment.ExpandEnvironmentVariables(runCommand.Trim());
            string executablePath = ExtractExecutablePathFromCommand(expandedCommand);
            return IsInstalledExecutablePathMatch(installedExePath, executablePath);
        }

        internal static string ExtractExecutablePathFromCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                return null;
            }

            string trimmed = command.Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            if (trimmed[0] == '"')
            {
                int closingQuoteIndex = trimmed.IndexOf('"', 1);
                if (closingQuoteIndex > 1)
                {
                    return trimmed.Substring(1, closingQuoteIndex - 1);
                }
            }

            int exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex >= 0)
            {
                return trimmed.Substring(0, exeIndex + 4);
            }

            return trimmed;
        }

        private bool TryGetInstalledExecutablePathFromContext(out string exePath, out string targetDir)
        {
            string configuredTargetDir = Context != null ? Context.Parameters[SetupTargetDirParameterName] : null;
            string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            return TryResolveInstalledExecutablePath(configuredTargetDir, assemblyPath, out exePath, out targetDir);
        }

        private static bool TryGetInstalledExecutablePath(out string exePath, out string targetDir)
        {
            string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            return TryResolveInstalledExecutablePath(null, assemblyPath, out exePath, out targetDir);
        }

        internal static bool TryResolveInstalledExecutablePath(string configuredTargetDir, string assemblyPath, out string exePath, out string targetDir)
        {
            exePath = null;
            targetDir = null;

            if (!string.IsNullOrWhiteSpace(configuredTargetDir))
            {
                string normalizedTargetDir = configuredTargetDir.Trim().Trim('"');
                if (!string.IsNullOrEmpty(normalizedTargetDir))
                {
                    targetDir = Path.GetFullPath(normalizedTargetDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    exePath = Path.Combine(targetDir, "KjTabBar.exe");
                    return true;
                }
            }

            if (string.IsNullOrEmpty(assemblyPath))
            {
                return false;
            }

            targetDir = Path.GetDirectoryName(assemblyPath);
            if (string.IsNullOrEmpty(targetDir))
            {
                return false;
            }

            exePath = Path.Combine(targetDir, "KjTabBar.exe");
            if (!File.Exists(exePath))
            {
                exePath = assemblyPath;
            }

            return File.Exists(exePath);
        }



        public override void Uninstall(IDictionary savedState)
        {
            KillRunningInstances();
            base.Uninstall(savedState);
            CleanUpOnUninstall();
        }

        private void KillRunningInstances()
        {
            try
            {
                string installedExePath;
                string installedTargetDir;
                if (!TryGetInstalledExecutablePathFromContext(out installedExePath, out installedTargetDir))
                {
                    AppLogger.LogInfo("SetupCustomActions", "Skipping process termination because the installed executable path could not be resolved.");
                    return;
                }

                Process[] processes = Process.GetProcessesByName("KjTabBar");
                for (int i = 0; i < processes.Length; i++)
                {
                    try
                    {
                        if (!IsTargetInstalledProcess(processes[i], installedExePath))
                        {
                            continue;
                        }

                        processes[i].Kill();
                        processes[i].WaitForExit(5000);
                    }
                    catch
                    {
                        AppLogger.LogInfo("SetupCustomActions", "Failed to terminate a KjTabBar process during uninstall.");
                        // 個別のプロセス終了失敗は無視
                    }
                    finally
                    {
                        processes[i].Dispose();
                    }
                }
            }
            catch
            {
                AppLogger.LogInfo("SetupCustomActions", "Failed to enumerate KjTabBar processes during uninstall.");
                // プロセス取得失敗は無視して続行
            }
        }

        internal static bool IsTargetInstalledProcess(Process process, string installedExePath)
        {
            if (process == null || string.IsNullOrEmpty(installedExePath))
            {
                return false;
            }

            try
            {
                string processPath = process.MainModule != null ? process.MainModule.FileName : null;
                if (string.IsNullOrEmpty(processPath))
                {
                    AppLogger.LogInfo("SetupCustomActions", "Skipping a KjTabBar-named process because its executable path could not be determined.");
                    return false;
                }

                return IsInstalledExecutablePathMatch(installedExePath, processPath);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SetupCustomActions", "Skipping a KjTabBar-named process because its executable path check failed.", ex);
                return false;
            }
        }

        internal static bool IsInstalledExecutablePathMatch(string installedExePath, string processPath)
        {
            if (string.IsNullOrEmpty(installedExePath) || string.IsNullOrEmpty(processPath))
            {
                return false;
            }

            string normalizedInstalledPath = Path.GetFullPath(installedExePath);
            string normalizedProcessPath = Path.GetFullPath(processPath);
            return string.Equals(normalizedInstalledPath, normalizedProcessPath, StringComparison.OrdinalIgnoreCase);
        }

        private void CleanUpOnUninstall()
        {
            string installedExePath;
            string installedTargetDir;
            if (!TryGetInstalledExecutablePathFromContext(out installedExePath, out installedTargetDir))
            {
                AppLogger.LogInfo("SetupCustomActions", "Skipping startup cleanup because the installed executable path could not be resolved.");
                return;
            }

            SetupRegistryManager.RemoveStartupRegistryValues(installedExePath);
        }
    }
}

