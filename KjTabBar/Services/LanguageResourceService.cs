using System;
using System.Collections.Generic;
using System.Windows;

namespace KjTabBar.Services
{
    internal sealed class LanguageResourceService
    {
        internal string GetDictionaryName(System.Globalization.CultureInfo culture)
        {
            if (culture != null && culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            {
                return "StringResources.ja.xaml";
            }

            return "StringResources.en.xaml";
        }

        public void ApplyLanguageResource(ICollection<ResourceDictionary> mergedDictionaries, System.Globalization.CultureInfo culture)
        {
            string dictName = GetDictionaryName(culture);
            ResourceDictionary dict = new ResourceDictionary();
            dict.Source = new Uri("/KjTabBar;component/Assets/Strings/" + dictName, UriKind.Relative);
            mergedDictionaries.Add(dict);
        }
    }
}
