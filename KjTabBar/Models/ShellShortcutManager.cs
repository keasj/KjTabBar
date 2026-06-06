using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using KjTabBar.Helpers;

namespace KjTabBar.Models
{
    internal sealed class ShellShortcutManager
    {
        private readonly Func<string, string> _getFolderName;
        private readonly Func<string, string> _getNavigableShellPath;
        private readonly Func<string, string> _normalizeShellPath;
        private readonly Func<object> _getShellApplication;
        private readonly Action<object> _releaseComObjectSafe;
        private readonly Func<object, string, object> _getComProperty;
        private readonly Func<object, string, object[], object> _invokeComMethod;
        private readonly string _allControlPanelPath;
        private readonly string _programsAndFeaturesPath;
        private readonly string _powerOptionsPath;

        public ShellShortcutManager(
            Func<string, string> getFolderName,
            Func<string, string> getNavigableShellPath,
            Func<string, string> normalizeShellPath,
            Func<object> getShellApplication,
            Action<object> releaseComObjectSafe,
            Func<object, string, object> getComProperty,
            Func<object, string, object[], object> invokeComMethod,
            string allControlPanelPath,
            string programsAndFeaturesPath,
            string powerOptionsPath)
        {
            _getFolderName = getFolderName;
            _getNavigableShellPath = getNavigableShellPath;
            _normalizeShellPath = normalizeShellPath;
            _getShellApplication = getShellApplication;
            _releaseComObjectSafe = releaseComObjectSafe;
            _getComProperty = getComProperty;
            _invokeComMethod = invokeComMethod;
            _allControlPanelPath = allControlPanelPath;
            _programsAndFeaturesPath = programsAndFeaturesPath;
            _powerOptionsPath = powerOptionsPath;
        }

        public string ResolveShortcutTarget(string shortcutPath)
        {
            if (string.IsNullOrEmpty(shortcutPath)) return null;
            if (!shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return shortcutPath;

            object shell = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return shortcutPath;

                shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                if (shortcut != null)
                {
                    try
                    {
                        string targetPath = null;
                        string arguments = null;

                        try
                        {
                            targetPath = (string)shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.LogErrorThrottled("ShellShortcutManager", "ResolveShortcutTargetPathRead", "Failed to read shortcut TargetPath.", ex, TimeSpan.FromMinutes(5));
                        }

                        try
                        {
                            arguments = (string)shortcut.GetType().InvokeMember("Arguments", System.Reflection.BindingFlags.GetProperty, null, shortcut, null);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.LogErrorThrottled("ShellShortcutManager", "ResolveShortcutArgumentsRead", "Failed to read shortcut Arguments.", ex, TimeSpan.FromMinutes(5));
                        }

                        string shellPathFromArguments = ExtractShellPathFromShortcutArguments(arguments);
                        if (!string.IsNullOrEmpty(shellPathFromArguments))
                        {
                            return shellPathFromArguments;
                        }

                        if (!string.IsNullOrEmpty(targetPath))
                        {
                            string normalizedShellTargetPath = _normalizeShellPath(targetPath);
                            if (!string.IsNullOrEmpty(normalizedShellTargetPath))
                            {
                                return normalizedShellTargetPath;
                            }

                            return targetPath;
                        }
                    }
                    finally
                    {
                        _releaseComObjectSafe(shortcut);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ShellShortcutManager", "Failed to resolve shortcut target.", ex);
            }
            finally
            {
                _releaseComObjectSafe(shell);
            }

            string virtualTargetPath = ResolveVirtualShortcutTarget(shortcutPath);
            if (!string.IsNullOrEmpty(virtualTargetPath))
            {
                return virtualTargetPath;
            }

            return shortcutPath;
        }

        public void CreateShortcuts(string[] sourcePaths, string destinationDirectory, IntPtr ownerHwnd)
        {
            if (sourcePaths == null || sourcePaths.Length == 0 || string.IsNullOrEmpty(destinationDirectory)) return;

            object shell = null;
            List<string> tempShortcutPaths = new List<string>();
            string tempDirectory = null;
            bool anyShortcutCreated = false;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    ShowOperationError("ショートカットを作成できませんでした。");
                    return;
                }

                shell = Activator.CreateInstance(shellType);

                for (int i = 0; i < sourcePaths.Length; i++)
                {
                    string sourcePath = sourcePaths[i];
                    string fileName = Path.GetFileName(sourcePath);
                    if (string.IsNullOrEmpty(fileName))
                    {
                        fileName = _getFolderName(sourcePath);
                    }

                    char[] invalidChars = Path.GetInvalidFileNameChars();
                    for (int j = 0; j < invalidChars.Length; j++)
                    {
                        fileName = fileName.Replace(invalidChars[j], '_');
                    }

                    string shortcutPath = BuildUniqueShortcutPath(destinationDirectory, fileName);
                    if (TryCreateShortcutFile(shellType, shell, sourcePath, shortcutPath))
                    {
                        anyShortcutCreated = true;
                        continue;
                    }

                    if (string.IsNullOrEmpty(tempDirectory))
                    {
                        tempDirectory = Path.Combine(Path.GetTempPath(), "KjTabBar", Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(tempDirectory);
                    }

                    string tempShortcutPath = Path.Combine(tempDirectory, Path.GetFileName(shortcutPath));
                    if (TryCreateShortcutFile(shellType, shell, sourcePath, tempShortcutPath))
                    {
                        tempShortcutPaths.Add(tempShortcutPath);
                        anyShortcutCreated = true;
                    }
                }

                if (tempShortcutPaths.Count > 0)
                {
                    NativeMethods.SHFILEOPSTRUCT shf = new NativeMethods.SHFILEOPSTRUCT();
                    shf.hwnd = ownerHwnd;
                    shf.wFunc = NativeMethods.FO_MOVE;
                    shf.pFrom = string.Join("\0", tempShortcutPaths.ToArray()) + "\0\0";
                    shf.pTo = destinationDirectory + "\0\0";
                    shf.fFlags = NativeMethods.FOF_ALLOWUNDO;
                    int result = NativeMethods.SHFileOperation(ref shf);
                    if (result != 0 || shf.fAnyOperationsAborted)
                    {
                        AppLogger.LogInfo("ShellShortcutManager", "SHFileOperation failed while moving temporary shortcuts.");
                        ShowOperationError("ショートカットの配置に失敗しました。");
                    }
                }

                if (!anyShortcutCreated)
                {
                    ShowOperationError("ショートカットを作成できませんでした。");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ShellShortcutManager", "Failed to create shortcut file.", ex);
                ShowOperationError("ショートカットの作成に失敗しました。");
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempDirectory) && Directory.Exists(tempDirectory))
                {
                    try
                    {
                        Directory.Delete(tempDirectory, true);
                    }
                    catch
                    {
                    }
                }

                _releaseComObjectSafe(shell);
            }
        }

        public void CreateSymbolicLinks(string[] sourcePaths, string destinationDirectory, IntPtr ownerHwnd)
        {
            if (sourcePaths == null || sourcePaths.Length == 0 || string.IsNullOrEmpty(destinationDirectory)) return;

            var linksToCreate = new List<Tuple<string, string, bool>>();
            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var otherErrorOccurred = false;

            for (int i = 0; i < sourcePaths.Length; i++)
            {
                string sourcePath = sourcePaths[i];
                string fileName = Path.GetFileName(sourcePath);
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = _getFolderName(sourcePath);
                }

                char[] invalidChars = Path.GetInvalidFileNameChars();
                for (int j = 0; j < invalidChars.Length; j++)
                {
                    fileName = fileName.Replace(invalidChars[j], '_');
                }

                bool isDirectory;
                if (!TryGetSymbolicLinkTargetType(sourcePath, out isDirectory))
                {
                    otherErrorOccurred = true;
                    continue;
                }

                string ext = isDirectory ? "" : Path.GetExtension(sourcePath);
                string baseName = isDirectory || string.IsNullOrEmpty(ext) ? fileName : Path.GetFileNameWithoutExtension(fileName);

                string linkName = baseName + ext;
                string linkPath = Path.Combine(destinationDirectory, linkName);
                
                int count = 2;
                while (File.Exists(linkPath) || Directory.Exists(linkPath) || usedPaths.Contains(linkPath))
                {
                    linkName = baseName + " (" + count + ")" + ext;
                    linkPath = Path.Combine(destinationDirectory, linkName);
                    count++;
                }

                linksToCreate.Add(new Tuple<string, string, bool>(linkPath, sourcePath, isDirectory));
                usedPaths.Add(linkPath);
            }

            var failedLinks = new List<Tuple<string, string, bool>>();

            foreach (var link in linksToCreate)
            {
                string linkPath = link.Item1;
                string sourcePath = link.Item2;
                bool isDirectory = link.Item3;

                uint flags = isDirectory ? NativeMethods.SYMBOLIC_LINK_FLAG_DIRECTORY : NativeMethods.SYMBOLIC_LINK_FLAG_FILE;
                flags |= NativeMethods.SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE;

                if (!NativeMethods.CreateSymbolicLink(linkPath, sourcePath, flags))
                {
                    int errorCode = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    if (errorCode == 1314)
                    {
                        failedLinks.Add(link);
                    }
                    else
                    {
                        otherErrorOccurred = true;
                    }
                }
            }

            if (failedLinks.Count > 0)
            {
                System.Windows.MessageBox.Show(
                    "シンボリックリンクを作成する権限がありません。Windowsの設定で開発者モードをオンにするか、権限のある場所を指定してください。",
                    "権限の確認",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
            
            if (otherErrorOccurred)
            {
                System.Windows.MessageBox.Show(
                    "一部のシンボリックリンクの作成に失敗しました。",
                    "エラー",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private bool TryGetSymbolicLinkTargetType(string sourcePath, out bool isDirectory)
        {
            isDirectory = false;
            if (string.IsNullOrEmpty(sourcePath))
            {
                return false;
            }

            if (Directory.Exists(sourcePath))
            {
                isDirectory = true;
                return true;
            }

            if (File.Exists(sourcePath))
            {
                isDirectory = false;
                return true;
            }

            return false;
        }

        private void ShowOperationError(string message)
        {
            System.Windows.MessageBox.Show(
                message,
                "操作エラー",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }

        private string BuildUniqueShortcutPath(string destinationDirectory, string baseFileName)
        {
            string shortcutName = baseFileName + " - ショートカット.lnk";
            string shortcutPath = Path.Combine(destinationDirectory, shortcutName);

            int count = 2;
            while (File.Exists(shortcutPath))
            {
                shortcutName = baseFileName + " - ショートカット (" + count + ").lnk";
                shortcutPath = Path.Combine(destinationDirectory, shortcutName);
                count++;
            }

            return shortcutPath;
        }

        private bool TryCreateShortcutFile(Type shellType, object shell, string sourcePath, string shortcutPath)
        {
            object shortcut = null;
            try
            {
                shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                if (shortcut != null)
                {
                    string normalizedSourcePath = _normalizeShellPath(sourcePath);
                    if (!string.IsNullOrEmpty(normalizedSourcePath))
                    {
                        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                        string explorerExePath = Path.Combine(windowsDirectory, "explorer.exe");
                        string shortcutArguments = "\"" + _getNavigableShellPath(normalizedSourcePath) + "\"";

                        shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { explorerExePath });
                        shortcut.GetType().InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { shortcutArguments });
                    }
                    else
                    {
                        shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { sourcePath });
                    }

                    shortcut.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ShellShortcutManager", "Failed to delete partially created shortcut file.", ex);
            }
            finally
            {
                _releaseComObjectSafe(shortcut);
            }

            try
            {
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                }
            }
            catch
            {
            }

            return false;
        }

        private string ResolveVirtualShortcutTarget(string shortcutPath)
        {
            object shellObject = _getShellApplication();
            object folder = null;
            object folderItem = null;
            object link = null;
            object target = null;
            try
            {
                if (shellObject != null)
                {
                    object shell = shellObject;
                    string dir = Path.GetDirectoryName(shortcutPath);
                    string name = Path.GetFileName(shortcutPath);
                    folder = _invokeComMethod(shell, "NameSpace", new object[] { dir });
                    if (folder != null)
                    {
                        folderItem = _invokeComMethod(folder, "ParseName", new object[] { name });
                        if (folderItem != null)
                        {
                            bool isLink = false;
                            object isLinkObj = _getComProperty(folderItem, "IsLink");
                            if (isLinkObj != null && isLinkObj is bool)
                            {
                                isLink = (bool)isLinkObj;
                            }
                            if (isLink)
                            {
                                link = _getComProperty(folderItem, "GetLink");
                                if (link != null)
                                {
                                    target = _getComProperty(link, "Target");
                                    if (target != null)
                                    {
                                        string virtualPath = (string)_getComProperty(target, "Path");
                                        if (!string.IsNullOrEmpty(virtualPath))
                                        {
                                            return _normalizeShellPath(virtualPath) ?? virtualPath;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogErrorThrottled("ShellShortcutManager", "ResolveVirtualShortcutTargetFailed", "Failed to resolve virtual shortcut target.", ex, TimeSpan.FromMinutes(5));
            }
            finally
            {
                _releaseComObjectSafe(target);
                _releaseComObjectSafe(link);
                _releaseComObjectSafe(folderItem);
                _releaseComObjectSafe(folder);
            }
            return null;
        }

        private string ExtractShellPathFromShortcutArguments(string arguments)
        {
            if (string.IsNullOrEmpty(arguments)) return null;

            string trimmed = arguments.Trim();
            if (string.IsNullOrEmpty(trimmed)) return null;

            string normalizedDirect = _normalizeShellPath(trimmed);
            if (!string.IsNullOrEmpty(normalizedDirect))
            {
                return normalizedDirect;
            }

            string extractedFromShell = ExtractShellPathFromText(trimmed, "shell:::{");
            if (!string.IsNullOrEmpty(extractedFromShell))
            {
                return extractedFromShell;
            }

            string extractedFromClsid = ExtractShellPathFromText(trimmed, "::{");
            if (!string.IsNullOrEmpty(extractedFromClsid))
            {
                return extractedFromClsid;
            }

            string compact = ShellLocationNameResolver.CompactForComparison(trimmed.ToLowerInvariant());
            if (compact.Equals("controlpanelfolder"))
            {
                return _allControlPanelPath;
            }
            if (compact.Contains("microsoft.programsandfeatures") || compact.Contains("appwiz.cpl"))
            {
                return _programsAndFeaturesPath;
            }
            if (compact.Contains("microsoft.poweroptions") || compact.Contains("powercfg.cpl"))
            {
                return _powerOptionsPath;
            }

            return null;
        }

        private string ExtractShellPathFromText(string text, string marker)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (string.IsNullOrEmpty(marker)) return null;

            int startIndex = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0) return null;

            string token = ExtractShellToken(text, startIndex);
            if (string.IsNullOrEmpty(token)) return null;

            return _normalizeShellPath(token);
        }

        private string ExtractShellToken(string text, int startIndex)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (startIndex < 0 || startIndex >= text.Length) return null;

            int endIndex = text.Length;
            for (int i = startIndex; i < text.Length; i++)
            {
                char ch = text[i];
                if (char.IsWhiteSpace(ch) || ch == '"')
                {
                    endIndex = i;
                    break;
                }
            }

            string token = text.Substring(startIndex, endIndex - startIndex);
            token = token.Trim().TrimEnd(',');
            return token;
        }
    }
}
