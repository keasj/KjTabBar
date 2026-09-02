using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using KjTabBar.Models;

namespace KjTabBar.ViewModels
{
    internal static class TabTitleDisambiguator
    {
        public static void UpdateTitles(ObservableCollection<TabItemViewModel> tabs, IExplorerService explorerService)
        {
            if (tabs == null || tabs.Count == 0) return;

            for (int i = 0; i < tabs.Count; i++)
            {
                TabItemViewModel tab = tabs[i];
                if (string.IsNullOrEmpty(tab.BaseTitle))
                {
                    tab.BaseTitle = explorerService.GetFolderName(tab.Path);
                }
                tab.Title = tab.BaseTitle;
            }

            Dictionary<string, List<int>> baseNameGroups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tabs.Count; i++)
            {
                string title = tabs[i].Title;
                if (string.IsNullOrEmpty(title)) title = "Home";
                if (!baseNameGroups.ContainsKey(title)) baseNameGroups[title] = new List<int>();
                baseNameGroups[title].Add(i);
            }

            HashSet<int> collisionIndices = new HashSet<int>();
            foreach (KeyValuePair<string, List<int>> kvp in baseNameGroups)
            {
                if (kvp.Value.Count > 1)
                {
                    string firstPath = tabs[kvp.Value[0]].Path;
                    bool hasDifferentPath = false;
                    for (int i = 1; i < kvp.Value.Count; i++)
                    {
                        if (!string.Equals(firstPath, tabs[kvp.Value[i]].Path, StringComparison.OrdinalIgnoreCase))
                        {
                            hasDifferentPath = true;
                            break;
                        }
                    }

                    if (hasDifferentPath)
                    {
                        for (int i = 0; i < kvp.Value.Count; i++)
                        {
                            collisionIndices.Add(kvp.Value[i]);
                        }
                    }
                }
            }

            if (collisionIndices.Count > 0)
            {
                bool changed = true;
                int maxIterations = 10;
                while (changed && maxIterations-- > 0)
                {
                    changed = false;
                    Dictionary<string, List<int>> currentTitleGroups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                    foreach (int idx in collisionIndices)
                    {
                        string title = tabs[idx].Title;
                        if (string.IsNullOrEmpty(title)) continue;
                        if (!currentTitleGroups.ContainsKey(title)) currentTitleGroups[title] = new List<int>();
                        currentTitleGroups[title].Add(idx);
                    }

                    foreach (KeyValuePair<string, List<int>> entry in currentTitleGroups)
                    {
                        if (entry.Value.Count > 1)
                        {
                            foreach (int idx in entry.Value)
                            {
                                string nextTitle = GetDeeperTitle(tabs[idx].Path, tabs[idx].Title, explorerService);
                                if (!string.Equals(nextTitle, tabs[idx].Title, StringComparison.OrdinalIgnoreCase))
                                {
                                    tabs[idx].Title = nextTitle;
                                    changed = true;
                                }
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < tabs.Count; i++)
            {
                tabs[i].Title = ShortenTitle(tabs[i].Title, 30);
            }

            Dictionary<string, List<int>> finalTitleGroups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tabs.Count; i++)
            {
                string baseTitle = tabs[i].Title;
                if (!finalTitleGroups.ContainsKey(baseTitle))
                {
                    finalTitleGroups[baseTitle] = new List<int>();
                }
                finalTitleGroups[baseTitle].Add(i);
            }

            foreach (KeyValuePair<string, List<int>> kvp in finalTitleGroups)
            {
                if (kvp.Value.Count > 1)
                {
                    int count = 1;
                    foreach (int idx in kvp.Value)
                    {
                        tabs[idx].Title = "(" + count.ToString() + ")" + kvp.Key;
                        count++;
                    }
                }
            }
        }

        internal static string GetDeeperTitle(string path, string currentTitle, IExplorerService explorerService)
        {
            if (string.IsNullOrEmpty(path))
            {
                return currentTitle;
            }

            if (path.StartsWith("::{") || path.StartsWith("shell:"))
            {
                return currentTitle;
            }

            if (currentTitle.Contains("..."))
            {
                return path;
            }

            string normalizedPath = path.TrimEnd('\\');
            string[] segments = normalizedPath.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0) return path;

            int currentSegmentCount = currentTitle.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Length;
            int nextSegmentCount = currentSegmentCount + 1;

            if (segments.Length <= 1 || nextSegmentCount > segments.Length)
            {
                if (currentTitle.Length >= 2 && currentTitle[1] == ':')
                {
                    return currentTitle;
                }

                string parentName = explorerService.GetParentFolderName(normalizedPath);
                if (!string.IsNullOrEmpty(parentName) && !string.Equals(parentName, currentTitle, StringComparison.OrdinalIgnoreCase))
                {
                    string joined = parentName + @"\" + currentTitle;
                    if (path.Length >= 2 && path[1] == ':' && string.Equals(parentName, path.Substring(0, 2), StringComparison.OrdinalIgnoreCase))
                    {
                         return path;
                    }
                    return joined;
                }
                return path;
            }

            string parentSegment = segments[segments.Length - nextSegmentCount];
            string result = parentSegment + @"\" + currentTitle;

            if (path.StartsWith(@"\\") && !result.StartsWith(@"\\") && segments.Length == nextSegmentCount)
            {
                result = @"\\" + result;
            }
            else if (result.Length >= 2 && result[1] == ':' && result.Length > 2 && result[2] != '\\')
            {
                result = result.Insert(2, @"\");
            }

            return result;
        }

        internal static string ShortenTitle(string title, int maxLen)
        {
            if (string.IsNullOrEmpty(title) || title.Length <= maxLen) return title;

            if (title.StartsWith("::{") || title.StartsWith("shell:")) return title;

            if (title.Contains(@"\"))
            {
                int rootLen = 0;
                if (title.Length >= 3 && title[1] == ':' && title[2] == '\\') rootLen = 3;
                else if (title.StartsWith(@"\\"))
                {
                    int nextSlash = title.IndexOf(@"\", 2);
                    if (nextSlash > 0) rootLen = nextSlash + 1;
                }
                else if (title.StartsWith(@"\")) rootLen = 1;

                string leafName = title.Substring(title.LastIndexOf(@"\") + 1);
                if (string.IsNullOrEmpty(leafName)) leafName = title;

                if ((rootLen > 0 || title.StartsWith(@"\\")) && rootLen + 3 + leafName.Length <= maxLen)
                {
                    string rootPart = title.Substring(0, rootLen);
                    if (!rootPart.EndsWith(@"\") && !leafName.Contains(@"\")) rootPart += @"\";
                    return rootPart + @"...\" + leafName;
                }
            }

            int startCount = maxLen / 3;
            if (startCount < 1) startCount = 1;
            int endCount = maxLen - startCount - 3;
            if (endCount < 5) endCount = 5;

            if (startCount + endCount + 3 > title.Length) return title;

            return title.Substring(0, startCount) + "..." + title.Substring(title.Length - endCount);
        }
    }
}
