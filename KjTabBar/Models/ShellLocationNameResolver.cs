using System;
using System.Text;

namespace KjTabBar.Models
{
    internal sealed class ShellLocationNameResolver
    {
        private readonly string _allControlPanelPath;
        private readonly string _homeFolderPath;
        private readonly string _programsAndFeaturesPath;
        private readonly string _powerOptionsPath;
        private readonly Func<string, string> _findControlPanelItemPathByTitle;

        public ShellLocationNameResolver(
            string allControlPanelPath,
            string homeFolderPath,
            string programsAndFeaturesPath,
            string powerOptionsPath,
            Func<string, string> findControlPanelItemPathByTitle)
        {
            _allControlPanelPath = allControlPanelPath;
            _homeFolderPath = homeFolderPath;
            _programsAndFeaturesPath = programsAndFeaturesPath;
            _powerOptionsPath = powerOptionsPath;
            _findControlPanelItemPathByTitle = findControlPanelItemPathByTitle;
        }

        public bool IsControlPanelRootName(string value, string localizedControlPanelTitle)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string compactValue = CompactForComparison(value.ToLowerInvariant());
            string compactLocalizedControlPanelTitle = CompactForComparison(localizedControlPanelTitle.ToLowerInvariant());
            return compactValue.Equals("controlpanel") ||
                   compactValue.Equals("controlpanelfolder") ||
                   compactValue.Equals("allcontrolpanelitems") ||
                   compactValue.Equals(compactLocalizedControlPanelTitle);
        }

        public string MapLocationNameToKnownShellPath(
            string locationName,
            string localizedControlPanelTitle,
            string localizedHomeTitle,
            string localizedNetworkTitle,
            string localizedRecycleBinTitle,
            string localizedThisPCTitle)
        {
            if (string.IsNullOrEmpty(locationName))
            {
                return null;
            }

            string compactName = CompactForComparison(locationName.ToLowerInvariant());
            if (IsControlPanelRootName(locationName, localizedControlPanelTitle))
            {
                return _allControlPanelPath;
            }

            string compactLocalizedHome = CompactForComparison(localizedHomeTitle.ToLowerInvariant());
            if (compactName.Equals("home") ||
                compactName.Equals("quickaccess") ||
                compactName.Equals(compactLocalizedHome))
            {
                return _homeFolderPath;
            }

            string compactLocalizedNetwork = CompactForComparison(localizedNetworkTitle.ToLowerInvariant());
            if (compactName.Equals("network") || compactName.Equals(compactLocalizedNetwork))
            {
                return "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";
            }

            string compactLocalizedRecycleBin = CompactForComparison(localizedRecycleBinTitle.ToLowerInvariant());
            if (compactName.Equals("recyclebin") || compactName.Equals(compactLocalizedRecycleBin))
            {
                return "::{645FF040-5081-101B-9F08-00AA002F954E}";
            }

            string compactLocalizedThisPC = CompactForComparison(localizedThisPCTitle.ToLowerInvariant());
            if (compactName.Equals("pc") || compactName.Equals("thispc") || compactName.Equals(compactLocalizedThisPC))
            {
                return "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
            }

            if (compactName.Equals("programsandfeatures"))
            {
                return _programsAndFeaturesPath;
            }

            if (compactName.Equals("poweroptions"))
            {
                return _powerOptionsPath;
            }

            if (_findControlPanelItemPathByTitle != null)
            {
                return _findControlPanelItemPathByTitle(locationName);
            }

            return null;
        }

        public static string CompactForComparison(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder stringBuilder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == ' ' || ch == '\t' || ch == '\u3000')
                {
                    continue;
                }

                stringBuilder.Append(ch);
            }

            return stringBuilder.ToString();
        }
    }
}
