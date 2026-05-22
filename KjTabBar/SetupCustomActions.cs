using System;
using System.Collections;
using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
using System.IO;
using KjTabBar.Helpers;
using Microsoft.Win32;

namespace KjTabBar
{
    [RunInstaller(true)]
    public class SetupCustomActions : Installer
    {
        internal const string StartupValueName = "KjTabBar";
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
                if (TryGetInstalledExecutablePath(out exePath, out targetDir))
                {
                    Process installerProcess = null;
                    int installerProcessId;
                    int preferredSessionId;
                    try
                    {
                        installerProcess = Process.GetCurrentProcess();
                        installerProcessId = installerProcess.Id;
                        preferredSessionId = GetPreferredSessionId(installerProcess.SessionId);
                    }
                    finally
                    {
                        if (installerProcess != null)
                        {
                            installerProcess.Dispose();
                        }
                    }

                    // ユーザー UI 側の msiexec が閉じた後に、ショートカットを更新してからユーザーシェル経由で本体を起動する。
                    string script = BuildPostInstallScript(installerProcessId, preferredSessionId);

                    string encodedCommand = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));

                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = "powershell.exe";
                    psi.Arguments = "-WindowStyle Hidden -NoProfile -EncodedCommand " + encodedCommand;
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.EnvironmentVariables["KJTB_EXE_PATH"] = exePath;
                    psi.EnvironmentVariables["KJTB_WORKING_DIRECTORY"] = targetDir;

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

        internal static string BuildPostInstallScript(int installerProcessId, int preferredSessionId)
        {
            return "$installerPid = " + installerProcessId.ToString() + "; " +
                   "$sessionId = " + preferredSessionId.ToString() + "; " +
                   "$exePath = $env:KJTB_EXE_PATH; " +
                   "$workingDirectory = $env:KJTB_WORKING_DIRECTORY; " +
                   // Intentional: generated setup logging must never fail installation if the log path is unavailable.
                   "function Write-KjtbSetupLog([string]$message) { try { $base = [Environment]::GetFolderPath('ApplicationData'); if ([string]::IsNullOrEmpty($base)) { $base = [System.IO.Path]::GetTempPath() }; $dir = [System.IO.Path]::Combine($base, 'KjTabBar', 'Logs'); [System.IO.Directory]::CreateDirectory($dir) | Out-Null; $line = ((Get-Date).ToUniversalTime().ToString('o') + ' [ERROR] SetupCustomActions: ' + $message + [Environment]::NewLine); [System.IO.File]::AppendAllText([System.IO.Path]::Combine($dir, 'KjTabBar.setup.log'), $line, [System.Text.Encoding]::UTF8) } catch {} }; " +
                   "if ([string]::IsNullOrEmpty($exePath) -or [string]::IsNullOrEmpty($workingDirectory)) { Write-KjtbSetupLog 'Missing post-install environment values.'; exit 1 }; " +
                   "$ui = Get-Process msiexec -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $sessionId -and $_.MainWindowHandle -ne 0 } | Sort-Object StartTime -Descending | Select-Object -First 1; " +
                   "if ($ui) { Wait-Process -Id $ui.Id -Timeout 600 -ErrorAction SilentlyContinue } " +
                   "else { Wait-Process -Id $installerPid -Timeout 600 -ErrorAction SilentlyContinue }; " +
                   "$desktopShortcut = [System.IO.Path]::Combine([Environment]::GetFolderPath('Desktop'), 'KjTabBar.lnk'); " +
                   "$programsShortcutDir = [System.IO.Path]::Combine([Environment]::GetFolderPath('Programs'), 'KjTabBar'); " +
                   "$programsShortcut = [System.IO.Path]::Combine($programsShortcutDir, 'KjTabBar.lnk'); " +
                   "$ws = $null; " +
                   "try { " +
                   "$ws = New-Object -ComObject WScript.Shell; " +
                   "foreach ($shortcutPath in @($desktopShortcut, $programsShortcut)) { " +
                   "$shortcutDir = Split-Path $shortcutPath -Parent; " +
                   "if (-not [string]::IsNullOrEmpty($shortcutDir) -and -not (Test-Path $shortcutDir)) { New-Item -ItemType Directory -Path $shortcutDir -Force | Out-Null }; " +
                   "$shortcut = $null; " +
                   "try { " +
                   "$shortcut = $ws.CreateShortcut($shortcutPath); " +
                   "$shortcut.TargetPath = $exePath; " +
                   "$shortcut.WorkingDirectory = $workingDirectory; " +
                   "$shortcut.Arguments = ''; " +
                   "$shortcut.Description = 'KjTabBar'; " +
                   "$shortcut.IconLocation = ($exePath + ',0'); " +
                   "$shortcut.Save(); " +
                   "} catch { Write-KjtbSetupLog ('Shortcut update failed: ' + $_.Exception.Message) } " +
                   "finally { if ($shortcut -ne $null) { [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null } } " +
                   "} " +
                   "} catch { Write-KjtbSetupLog ('Shortcut COM setup failed: ' + $_.Exception.Message) } " +
                   "finally { if ($ws -ne $null) { [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($ws) | Out-Null } }; " +
                   "$runValue = ('\"' + $exePath + '\"'); " +
                   "$targetSid = $null; " +
                   "try { $explorerForSid = Get-Process explorer -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $sessionId } | Sort-Object StartTime -Descending | Select-Object -First 1; if ($explorerForSid) { $explorerCim = Get-CimInstance Win32_Process -Filter \"ProcessId=$($explorerForSid.Id)\" -ErrorAction SilentlyContinue; if ($explorerCim) { $targetSid = (Invoke-CimMethod -InputObject $explorerCim -MethodName GetOwnerSid -ErrorAction SilentlyContinue).Sid } } } catch { Write-KjtbSetupLog ('Target SID resolution failed: ' + $_.Exception.Message) }; " +
                   "try { if (-not [string]::IsNullOrEmpty($targetSid)) { $runKey = 'Registry::HKEY_USERS\\' + $targetSid + '\\Software\\Microsoft\\Windows\\CurrentVersion\\Run' } else { $runKey = 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run' }; if (-not (Test-Path $runKey)) { New-Item -Path $runKey -Force | Out-Null }; Set-ItemProperty -Path $runKey -Name 'KjTabBar' -Value $runValue } catch { Write-KjtbSetupLog ('Startup Run registration failed: ' + $_.Exception.Message) }; " +
                   "Start-Process -FilePath 'explorer.exe' -ArgumentList ('\"' + $exePath + '\"')";
        }

        internal static string BuildStartupRunCommand(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
            {
                return null;
            }

            return "\"" + exePath + "\"";
        }

        private static bool TryGetInstalledExecutablePath(out string exePath, out string targetDir)
        {
            exePath = null;
            targetDir = null;

            string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
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

        private int GetPreferredSessionId(int fallbackSessionId)
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
                AppLogger.LogInfo("SetupCustomActions", "GetPreferredSessionId failed. Falling back to installer session.");
                // 取得失敗時はインストーラー自身のセッションを使う
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
                if (!TryGetInstalledExecutablePath(out installedExePath, out installedTargetDir))
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
            RemoveStartupRegistryValues();

            /*
            // 2. ユーザー設定 (.xml, tabs.txt) の削除
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string qttabDir = Path.Combine(appData, "KjTabBar");
                if (Directory.Exists(qttabDir))
                {
                    Directory.Delete(qttabDir, true);
                }
            }
            catch
            {
                // エラーは無視して続行
            }
            */
        }

        private void RemoveStartupRegistryValues()
        {
            string[] targetSubKeyPaths = new string[]
            {
                StartupRunSubKeyPath,
                StartupApprovedRunSubKeyPath
            };

            // HKCUに対する削除（アンインストーラがユーザー権限で動いている場合）
            RemoveStartupRegistryValuesForHive(RegistryHive.CurrentUser, RegistryView.Registry64, StartupValueName, targetSubKeyPaths);
            RemoveStartupRegistryValuesForHive(RegistryHive.CurrentUser, RegistryView.Registry32, StartupValueName, targetSubKeyPaths);

            // SYSTEM権限で動いている場合、HKCUはSYSTEMのものになってしまうため、
            // HKEY_USERS にロードされている全ユーザープロファイルから削除する。
            RemoveStartupRegistryValuesFromAllUsers(targetSubKeyPaths, StartupValueName);
        }

        private void RemoveStartupRegistryValuesFromAllUsers(string[] targetSubKeyPaths, string valueName)
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
                    if (!IsRegularUserSid(userSid)) continue;

                    for (int j = 0; j < targetSubKeyPaths.Length; j++)
                    {
                        string fullPath = userSid + @"\" + targetSubKeyPaths[j];
                        DeleteRegistryValue(usersKey, fullPath, valueName);
                    }
                }
            }
            catch
            {
                AppLogger.LogInfo("SetupCustomActions", "Failed while enumerating user hives for startup cleanup.");
                // エラーは無視して続行
            }
            finally
            {
                if (usersKey != null)
                {
                    usersKey.Dispose();
                }
            }
        }

        private void RemoveStartupRegistryValuesForHive(
            RegistryHive hive,
            RegistryView view,
            string valueName,
            string[] targetSubKeyPaths)
        {
            RegistryKey baseKey = null;
            try
            {
                baseKey = RegistryKey.OpenBaseKey(hive, view);
                if (baseKey == null)
                {
                    return;
                }

                for (int i = 0; i < targetSubKeyPaths.Length; i++)
                {
                    DeleteRegistryValue(baseKey, targetSubKeyPaths[i], valueName);
                }
            }
            catch
            {
                AppLogger.LogInfo("SetupCustomActions", "Failed while opening base registry hive for startup cleanup.");
                // エラーは無視して続行
            }
            finally
            {
                if (baseKey != null)
                {
                    baseKey.Dispose();
                }
            }
        }

        private void DeleteRegistryValue(RegistryKey rootKey, string subKeyPath, string valueName)
        {
            RegistryKey key = null;
            try
            {
                key = rootKey.OpenSubKey(subKeyPath, true);
                if (key != null && key.GetValue(valueName) != null)
                {
                    key.DeleteValue(valueName, false);
                }
            }
            catch
            {
                AppLogger.LogInfo("SetupCustomActions", "Failed while deleting startup registry value.");
                // エラーは無視して続行
            }
            finally
            {
                if (key != null)
                {
                    key.Dispose();
                }
            }
        }
        internal static bool IsRegularUserSid(string userSid)
        {
            if (string.IsNullOrEmpty(userSid))
            {
                return false;
            }

            return userSid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase);
        }

    }
}

