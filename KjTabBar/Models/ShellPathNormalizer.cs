using System;
using System.Collections.Generic;
using Microsoft.Win32;
using KjTabBar.Helpers;

namespace KjTabBar.Models
{
    internal sealed class ShellPathNormalizer
    {
        private readonly string _allControlPanelPath;
        private readonly string _homeFolderPath;
        private readonly string _programsAndFeaturesPath;
        private readonly string _powerOptionsPath;
        private const string ControlPanelItemNavigationPrefix = "::{26EE0668-A00A-44D7-9371-BEB064C98683}\\0\\";

        private readonly Func<string> _getLocalizedControlPanelTitle;
        private readonly Func<string> _getLocalizedHomeTitle;
        private readonly Func<string> _getLocalizedNetworkTitle;
        private readonly Func<string> _getLocalizedRecycleBinTitle;
        private readonly Func<string> _getLocalizedThisPCTitle;
        private readonly Func<string> _getResolvedHomeFolderPath;
        private readonly ShellLocationNameResolver _shellLocationNameResolver;
        private readonly Func<string, string> _getFolderNameInternal;

        private readonly object _controlPanelItemTitleMapSync = new object();
        private Dictionary<string, string> _controlPanelItemPathsByTitle = null;
        private HashSet<string> _controlPanelItemPaths = null;

        private static readonly string[] ControlPanelRootGuidTokens = new string[]
        {
            "26ee0668-a00a-44d7-9371-beb064c98683",
            "21ec2020-3aea-1069-a2dd-08002b30309d",
            "5399e694-6ce5-4d6c-8fce-1d8870fdcba0",
            "82a74aeb-aeb4-465c-a014-d097ee346d63"
        };

        public ShellPathNormalizer(
            string allControlPanelPath,
            string homeFolderPath,
            string programsAndFeaturesPath,
            string powerOptionsPath,
            Func<string> getLocalizedControlPanelTitle,
            Func<string> getLocalizedHomeTitle,
            Func<string> getLocalizedNetworkTitle,
            Func<string> getLocalizedRecycleBinTitle,
            Func<string> getLocalizedThisPCTitle,
            Func<string> getResolvedHomeFolderPath,
            ShellLocationNameResolver shellLocationNameResolver,
            Func<string, string> getFolderNameInternal)
        {
            _allControlPanelPath = allControlPanelPath;
            _homeFolderPath = homeFolderPath;
            _programsAndFeaturesPath = programsAndFeaturesPath;
            _powerOptionsPath = powerOptionsPath;
            _getLocalizedControlPanelTitle = getLocalizedControlPanelTitle;
            _getLocalizedHomeTitle = getLocalizedHomeTitle;
            _getLocalizedNetworkTitle = getLocalizedNetworkTitle;
            _getLocalizedRecycleBinTitle = getLocalizedRecycleBinTitle;
            _getLocalizedThisPCTitle = getLocalizedThisPCTitle;
            _getResolvedHomeFolderPath = getResolvedHomeFolderPath;
            _shellLocationNameResolver = shellLocationNameResolver;
            _getFolderNameInternal = getFolderNameInternal;
        }

        private bool ContainsControlPanelRootGuid(string lowerPath)
        {
            if (string.IsNullOrEmpty(lowerPath))
            {
                return false;
            }

            for (int i = 0; i < ControlPanelRootGuidTokens.Length; i++)
            {
                if (lowerPath.Contains(ControlPanelRootGuidTokens[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsControlPanelRootGuid(string guidToken)
        {
            if (string.IsNullOrEmpty(guidToken))
            {
                return false;
            }

            for (int i = 0; i < ControlPanelRootGuidTokens.Length; i++)
            {
                if (guidToken.Equals(ControlPanelRootGuidTokens[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ContainsNonControlPanelRootGuid(string lowerPath)
        {
            if (string.IsNullOrEmpty(lowerPath))
            {
                return false;
            }

            int searchIndex = 0;
            while (searchIndex < lowerPath.Length)
            {
                int openBraceIndex = lowerPath.IndexOf('{', searchIndex);
                if (openBraceIndex < 0)
                {
                    break;
                }

                int closeBraceIndex = lowerPath.IndexOf('}', openBraceIndex + 1);
                if (closeBraceIndex < 0)
                {
                    break;
                }

                string guidToken = lowerPath.Substring(openBraceIndex + 1, closeBraceIndex - openBraceIndex - 1);
                if (!IsControlPanelRootGuid(guidToken))
                {
                    return true;
                }

                searchIndex = closeBraceIndex + 1;
            }

            return false;
        }

        public bool IsControlPanelRootPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            string lowerPath = path.ToLowerInvariant();
            string compactPath = ShellLocationNameResolver.CompactForComparison(lowerPath);
            if (_shellLocationNameResolver.IsControlPanelRootName(compactPath, _getLocalizedControlPanelTitle()))
            {
                return true;
            }

            if (lowerPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                if (compactPath.Equals("shell:controlpanel") ||
                    compactPath.StartsWith("shell:controlpanelfolder"))
                {
                    return true;
                }
                if (!lowerPath.StartsWith("shell:::{", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (!lowerPath.StartsWith("::{") && !lowerPath.StartsWith("shell:::{"))
            {
                return false;
            }

            if (!ContainsControlPanelRootGuid(lowerPath))
            {
                return false;
            }

            string normalizedNamespacePath = NormalizeBasicShellNamespacePath(path);
            string lastGuidToken = GetLastGuidToken(normalizedNamespacePath);
            if (string.IsNullOrEmpty(lastGuidToken) || !IsControlPanelRootGuid(lastGuidToken))
            {
                return false;
            }

            if (ContainsNonControlPanelRootGuid(lowerPath))
            {
                return false;
            }

            return true;
        }

        public bool IsControlPanelItemPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string normalizedPath = NormalizeShellPath(path);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                normalizedPath = NormalizeShellNamespacePath(path);
            }

            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            if (IsControlPanelRootPath(normalizedPath))
            {
                return false;
            }

            EnsureControlPanelItemTitleMap();

            lock (_controlPanelItemTitleMapSync)
            {
                if (_controlPanelItemPaths == null)
                {
                    return false;
                }

                return _controlPanelItemPaths.Contains(normalizedPath);
            }
        }

        public bool IsControlPanelPath(string path)
        {
            if (IsControlPanelRootPath(path))
            {
                return true;
            }

            return IsControlPanelItemPath(path);
        }

        public bool IsTransientShellPlaceholderPath(string path)
        {
            string normalizedPath = NormalizeShellNamespacePath(path);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            if (normalizedPath.Equals(_allControlPanelPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (normalizedPath.Equals(_homeFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (normalizedPath.Equals("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public string NormalizeShellPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string trimmed = path.Trim().TrimEnd('\\');
            string compactTrimmed = ShellLocationNameResolver.CompactForComparison(trimmed.ToLowerInvariant());
            if (trimmed.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                if (compactTrimmed.Equals("shell:home") ||
                    compactTrimmed.Equals("shell:homefolder") ||
                    compactTrimmed.StartsWith("shell:quickaccess"))
                {
                    return _homeFolderPath;
                }
                if (compactTrimmed.StartsWith("shell:controlpanelfolder"))
                {
                    return _allControlPanelPath;
                }
                if (compactTrimmed.StartsWith("shell:mycomputerfolder") ||
                    compactTrimmed.StartsWith("shell:thispcfolder"))
                {
                    return "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
                }
                if (compactTrimmed.StartsWith("shell:networkplacesfolder"))
                {
                    return "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";
                }
                if (compactTrimmed.StartsWith("shell:recyclebinfolder"))
                {
                    return "::{645FF040-5081-101B-9F08-00AA002F954E}";
                }
            }

            if (compactTrimmed.Contains("microsoft.programsandfeatures") || compactTrimmed.Contains("appwiz.cpl"))
            {
                return _programsAndFeaturesPath;
            }

            if (compactTrimmed.Contains("microsoft.poweroptions") || compactTrimmed.Contains("powercfg.cpl"))
            {
                return _powerOptionsPath;
            }

            return NormalizeShellNamespacePath(trimmed);
        }

        public string NormalizeKnownPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            string normalizedPath = NormalizeShellPath(path);
            if (!string.IsNullOrEmpty(normalizedPath))
            {
                return normalizedPath;
            }

            return path;
        }

        public string GetNavigableShellPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            string normalizedPath = NormalizeShellPath(path);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return path;
            }

            if (normalizedPath.Equals(_homeFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                return _getResolvedHomeFolderPath();
            }

            if (IsControlPanelRootPathForNavigation(normalizedPath))
            {
                return _allControlPanelPath;
            }

            string controlPanelItemPath = GetControlPanelItemPathForNavigation(normalizedPath);
            if (!string.IsNullOrEmpty(controlPanelItemPath))
            {
                return ControlPanelItemNavigationPrefix + controlPanelItemPath;
            }

            return normalizedPath;
        }

        private bool IsControlPanelRootPathForNavigation(string normalizedPath)
        {
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            string lowerPath = normalizedPath.ToLowerInvariant();
            if (!lowerPath.StartsWith("::{", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!ContainsControlPanelRootGuid(lowerPath))
            {
                return false;
            }

            if (ContainsNonControlPanelRootGuid(lowerPath))
            {
                return false;
            }

            return true;
        }

        private string GetControlPanelItemPathForNavigation(string normalizedPath)
        {
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return null;
            }

            string normalizedCompositeItemPath = NormalizeControlPanelItemShellPath(normalizedPath);
            if (!string.IsNullOrEmpty(normalizedCompositeItemPath))
            {
                return normalizedCompositeItemPath;
            }

            if (!normalizedPath.StartsWith("::{", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string guidToken = GetLastGuidToken(normalizedPath);
            if (string.IsNullOrEmpty(guidToken) || IsControlPanelRootGuid(guidToken))
            {
                return null;
            }

            string standaloneItemPath = "::{" + guidToken.ToUpperInvariant() + "}";
            if (IsKnownControlPanelItemPathForNavigation(standaloneItemPath))
            {
                return standaloneItemPath;
            }

            return null;
        }

        private bool IsKnownControlPanelItemPathForNavigation(string normalizedPath)
        {
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            if (normalizedPath.Equals(_programsAndFeaturesPath, StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Equals(_powerOptionsPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            lock (_controlPanelItemTitleMapSync)
            {
                if (_controlPanelItemPaths != null && _controlPanelItemPaths.Contains(normalizedPath))
                {
                    return true;
                }
            }

            string guidToken = GetLastGuidToken(normalizedPath);
            if (string.IsNullOrEmpty(guidToken) || IsControlPanelRootGuid(guidToken))
            {
                return false;
            }

            RegistryKey clsidKey = null;
            try
            {
                clsidKey = Registry.ClassesRoot.OpenSubKey(@"CLSID\{" + guidToken.ToUpperInvariant() + "}");
                if (clsidKey == null)
                {
                    return false;
                }

                if (clsidKey.GetValue("System.ControlPanel.Category") != null)
                {
                    return true;
                }
            }
            catch
            {
            }
            finally
            {
                if (clsidKey != null)
                {
                    clsidKey.Dispose();
                }
            }

            return false;
        }

        private string NormalizeBasicShellNamespacePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            string trimmed = path.TrimEnd('\0').Trim().TrimEnd('\\');
            if (trimmed.Length > 2 && trimmed[trimmed.Length - 2] == '\\' && char.IsDigit(trimmed[trimmed.Length - 1]))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 2);
            }
            if (trimmed.StartsWith("shell:::{", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.Length > 9)
                {
                    return "::{" + trimmed.Substring(9);
                }

                return null;
            }
            if (trimmed.StartsWith("::{", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return null;
        }

        private string GetLastGuidToken(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            string lastGuidToken = null;
            int searchIndex = 0;
            while (searchIndex < path.Length)
            {
                int openBraceIndex = path.IndexOf('{', searchIndex);
                if (openBraceIndex < 0)
                {
                    break;
                }

                int closeBraceIndex = path.IndexOf('}', openBraceIndex + 1);
                if (closeBraceIndex < 0)
                {
                    break;
                }

                lastGuidToken = path.Substring(openBraceIndex + 1, closeBraceIndex - openBraceIndex - 1);
                searchIndex = closeBraceIndex + 1;
            }

            return lastGuidToken;
        }

        private string NormalizeControlPanelItemShellPath(string normalizedNamespacePath)
        {
            if (string.IsNullOrEmpty(normalizedNamespacePath))
            {
                return null;
            }

            string lowerPath = normalizedNamespacePath.ToLowerInvariant();
            if (!ContainsControlPanelRootGuid(lowerPath))
            {
                return null;
            }

            string lastGuidToken = GetLastGuidToken(lowerPath);
            if (string.IsNullOrEmpty(lastGuidToken) || IsControlPanelRootGuid(lastGuidToken))
            {
                return null;
            }

            return "::{" + lastGuidToken.ToUpperInvariant() + "}";
        }

        public string NormalizeShellNamespacePath(string path)
        {
            string normalizedNamespacePath = NormalizeBasicShellNamespacePath(path);
            if (string.IsNullOrEmpty(normalizedNamespacePath))
            {
                return null;
            }

            string normalizedControlPanelItemPath = NormalizeControlPanelItemShellPath(normalizedNamespacePath);
            if (!string.IsNullOrEmpty(normalizedControlPanelItemPath))
            {
                return normalizedControlPanelItemPath;
            }

            int embeddedNullIndex = normalizedNamespacePath.IndexOf('\0');
            if (embeddedNullIndex >= 0)
            {
                string normalizedNamespacePrefix = NormalizeBasicShellNamespacePath(
                    normalizedNamespacePath.Substring(0, embeddedNullIndex));
                if (!string.IsNullOrEmpty(normalizedNamespacePrefix))
                {
                    return normalizedNamespacePrefix;
                }
            }

            return normalizedNamespacePath;
        }

        public string MapLocationNameToKnownShellPath(string locationName)
        {
            return _shellLocationNameResolver.MapLocationNameToKnownShellPath(
                locationName,
                _getLocalizedControlPanelTitle(),
                _getLocalizedHomeTitle(),
                _getLocalizedNetworkTitle(),
                _getLocalizedRecycleBinTitle(),
                _getLocalizedThisPCTitle());
        }

        internal string FindControlPanelItemPathByTitle(string locationName)
        {
            if (string.IsNullOrEmpty(locationName))
            {
                return null;
            }

            string compactLocationName = ShellLocationNameResolver.CompactForComparison(locationName.ToLowerInvariant());
            if (string.IsNullOrEmpty(compactLocationName))
            {
                return null;
            }

            EnsureControlPanelItemTitleMap();

            lock (_controlPanelItemTitleMapSync)
            {
                if (_controlPanelItemPathsByTitle == null)
                {
                    return null;
                }

                string mappedPath;
                if (_controlPanelItemPathsByTitle.TryGetValue(compactLocationName, out mappedPath))
                {
                    return mappedPath;
                }
            }

            return null;
        }

        public void EnsureControlPanelItemTitleMap()
        {
            lock (_controlPanelItemTitleMapSync)
            {
                if (_controlPanelItemPathsByTitle != null)
                {
                    return;
                }

                Dictionary<string, string> titleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> itemPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                RegistryKey namespaceRootKey = null;
                try
                {
                    namespaceRootKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel\NameSpace");
                    if (namespaceRootKey != null)
                    {
                        string[] subKeyNames = namespaceRootKey.GetSubKeyNames();
                        for (int i = 0; i < subKeyNames.Length; i++)
                        {
                            string subKeyName = subKeyNames[i];
                            if (string.IsNullOrEmpty(subKeyName))
                            {
                                continue;
                            }

                            string trimmedSubKeyName = subKeyName.Trim();
                            if (!trimmedSubKeyName.StartsWith("{", StringComparison.OrdinalIgnoreCase) ||
                                !trimmedSubKeyName.EndsWith("}", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            string shellPath = "::" + trimmedSubKeyName;
                            string title = _getFolderNameInternal(shellPath);
                            AddControlPanelItemTitleMapEntry(titleMap, title, shellPath);
                            AddControlPanelItemPathEntry(itemPaths, shellPath);

                            RegistryKey clsidKey = null;
                            try
                            {
                                clsidKey = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + trimmedSubKeyName);
                                if (clsidKey != null)
                                {
                                    object defaultTitleObj = clsidKey.GetValue(null);
                                    string defaultTitle = defaultTitleObj as string;
                                    AddControlPanelItemTitleMapEntry(titleMap, defaultTitle, shellPath);
                                }
                            }
                            catch (Exception ex)
                            {
                                AppLogger.LogError("ShellPathNormalizer", "Failed to read CLSID title while building control panel title map.", ex);
                            }
                            finally
                            {
                                if (clsidKey != null)
                                {
                                    clsidKey.Dispose();
                                }
                            }
                        }
                    }

                    AddControlPanelItemTitleMapEntry(titleMap, "Programs and Features", _programsAndFeaturesPath);
                    AddControlPanelItemTitleMapEntry(titleMap, "プログラムと機能", _programsAndFeaturesPath);
                    AddControlPanelItemTitleMapEntry(titleMap, "Power Options", _powerOptionsPath);
                    AddControlPanelItemTitleMapEntry(titleMap, "電源オプション", _powerOptionsPath);
                    AddControlPanelItemPathEntry(itemPaths, _programsAndFeaturesPath);
                    AddControlPanelItemPathEntry(itemPaths, _powerOptionsPath);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("ShellPathNormalizer", "Failed to build control panel item title map.", ex);
                }
                finally
                {
                    if (namespaceRootKey != null)
                    {
                        namespaceRootKey.Dispose();
                    }
                }

                _controlPanelItemPathsByTitle = titleMap;
                _controlPanelItemPaths = itemPaths;
            }
        }

        private void AddControlPanelItemTitleMapEntry(Dictionary<string, string> titleMap, string title, string path)
        {
            if (titleMap == null || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(path))
            {
                return;
            }

            string compactTitle = ShellLocationNameResolver.CompactForComparison(title.ToLowerInvariant());
            if (string.IsNullOrEmpty(compactTitle))
            {
                return;
            }

            if (!titleMap.ContainsKey(compactTitle))
            {
                titleMap.Add(compactTitle, path);
            }
        }

        private void AddControlPanelItemPathEntry(HashSet<string> itemPaths, string path)
        {
            if (itemPaths == null || string.IsNullOrEmpty(path))
            {
                return;
            }

            string normalizedPath = NormalizeShellNamespacePath(path);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                normalizedPath = NormalizeShellPath(path);
            }

            if (string.IsNullOrEmpty(normalizedPath))
            {
                return;
            }

            if (IsControlPanelRootPath(normalizedPath))
            {
                return;
            }

            itemPaths.Add(normalizedPath);
        }
    }
}
