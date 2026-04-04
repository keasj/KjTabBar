using System;
using System.Collections;
using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace KjTabBar
{
    [RunInstaller(true)]
    public class SetupCustomActions : Installer
    {
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
                // アセンブリ自身の場所からexeパスを特定
                string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string targetDir = Path.GetDirectoryName(assemblyPath);
                string exePath = Path.Combine(targetDir, "KjTabBar.exe");

                if (!File.Exists(exePath))
                {
                    // アセンブリ自体がKjTabBar.exeの場合
                    exePath = assemblyPath;
                }

                if (File.Exists(exePath))
                {
                    Process installerProcess = Process.GetCurrentProcess();
                    int installerProcessId = installerProcess.Id;
                    int preferredSessionId = GetPreferredSessionId(installerProcess.SessionId);
                    installerProcess.Dispose();

                    string escapedExePath = exePath.Replace("'", "''");
                    string escapedWorkingDirectory = targetDir.Replace("'", "''");

                    // ユーザー UI 側の msiexec が閉じた後に、ショートカットを更新してからユーザーシェル経由で本体を起動する。
                    string script = BuildPostInstallScript(installerProcessId, preferredSessionId, escapedExePath, escapedWorkingDirectory);

                    string encodedCommand = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));

                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = "powershell.exe";
                    psi.Arguments = "-WindowStyle Hidden -NoProfile -EncodedCommand " + encodedCommand;
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;

                    Process process = Process.Start(psi);
                    if (process != null)
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                // 起動失敗してもインストール自体は成功とする
            }
        }

        private string BuildPostInstallScript(int installerProcessId, int preferredSessionId, string escapedExePath, string escapedWorkingDirectory)
        {
            return "$installerPid = " + installerProcessId.ToString() + "; " +
                   "$sessionId = " + preferredSessionId.ToString() + "; " +
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
                   "$shortcut.TargetPath = '" + escapedExePath + "'; " +
                   "$shortcut.WorkingDirectory = '" + escapedWorkingDirectory + "'; " +
                   "$shortcut.Arguments = ''; " +
                   "$shortcut.Description = 'KjTabBar'; " +
                   "$shortcut.IconLocation = '" + escapedExePath + ",0'; " +
                   "$shortcut.Save(); " +
                   "} catch {} " +
                   "finally { if ($shortcut -ne $null) { [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null } } " +
                   "} " +
                   "} catch {} " +
                   "finally { if ($ws -ne $null) { [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($ws) | Out-Null } }; " +
                   "Start-Process -FilePath 'explorer.exe' -ArgumentList '\"" + escapedExePath + "\"'";
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
                Process[] processes = Process.GetProcessesByName("KjTabBar");
                for (int i = 0; i < processes.Length; i++)
                {
                    try
                    {
                        processes[i].Kill();
                        processes[i].WaitForExit(5000);
                    }
                    catch
                    {
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
                // プロセス取得失敗は無視して続行
            }
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
            const string valueName = "KjTabBar";
            string[] targetSubKeyPaths = new string[]
            {
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
            };

            // HKCUに対する削除（アンインストーラがユーザー権限で動いている場合）
            RemoveStartupRegistryValuesForHive(RegistryHive.CurrentUser, RegistryView.Registry64, valueName, targetSubKeyPaths);
            RemoveStartupRegistryValuesForHive(RegistryHive.CurrentUser, RegistryView.Registry32, valueName, targetSubKeyPaths);

            // SYSTEM権限で動いている場合、HKCUはSYSTEMのものになってしまうため、
            // HKEY_USERS にロードされている全ユーザープロファイルから削除する。
            RemoveStartupRegistryValuesFromAllUsers(targetSubKeyPaths, valueName);
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
                    // .DEFAULT や S-1-5-18(SYSTEM), _Classes などを除外せず、全てのユーザーハイブに対して試行する
                    if (userSid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase)) continue;

                    for (int j = 0; j < targetSubKeyPaths.Length; j++)
                    {
                        string fullPath = userSid + @"\" + targetSubKeyPaths[j];
                        DeleteRegistryValue(usersKey, fullPath, valueName);
                    }
                }
            }
            catch
            {
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


    }
}

