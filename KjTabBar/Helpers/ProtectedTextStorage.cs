using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KjTabBar.Helpers
{
    internal static class ProtectedTextStorage
    {
        private const string ProtectedPrefix = "kjtb-dpapi-v1:";
        private static readonly byte[] AdditionalEntropy = Encoding.UTF8.GetBytes("KjTabBar.TabState");

        public static string[] LoadLines(string path)
        {
            if (!File.Exists(path))
            {
                return new string[0];
            }

            string persistedText = File.ReadAllText(path, Encoding.UTF8);
            return DeserializeLines(persistedText);
        }

        public static void SaveLines(string path, IList<string> lines)
        {
            string persistedText = SerializeLines(lines);
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                directory = Directory.GetCurrentDirectory();
            }

            string tempPath = Path.Combine(directory, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(tempPath, persistedText, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        internal static string SerializeLines(IList<string> lines)
        {
            StringBuilder builder = new StringBuilder();
            if (lines != null)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append('\n');
                    }

                    builder.Append(lines[i] ?? string.Empty);
                }
            }

            byte[] plainBytes = Encoding.UTF8.GetBytes(builder.ToString());
            byte[] protectedBytes = ProtectedData.Protect(plainBytes, AdditionalEntropy, DataProtectionScope.CurrentUser);
            return ProtectedPrefix + Convert.ToBase64String(protectedBytes);
        }

        internal static bool IsProtectedText(string persistedText)
        {
            return !string.IsNullOrEmpty(persistedText) && persistedText.StartsWith(ProtectedPrefix, StringComparison.Ordinal);
        }

        public static bool IsProtectedFile(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            string persistedText = File.ReadAllText(path, Encoding.UTF8);
            return IsProtectedText(persistedText);
        }

        internal static string[] DeserializeLines(string persistedText)
        {
            if (string.IsNullOrEmpty(persistedText))
            {
                return new string[0];
            }

            if (IsProtectedText(persistedText))
            {
                string protectedPayload = persistedText.Substring(ProtectedPrefix.Length).Trim();
                if (string.IsNullOrEmpty(protectedPayload))
                {
                    return new string[0];
                }

                byte[] protectedBytes = Convert.FromBase64String(protectedPayload);
                byte[] plainBytes = ProtectedData.Unprotect(protectedBytes, AdditionalEntropy, DataProtectionScope.CurrentUser);
                string plainText = Encoding.UTF8.GetString(plainBytes);
                return SplitLines(plainText);
            }

            return SplitLines(persistedText);
        }

        private static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new string[0];
            }

            string normalizedText = text.Replace("\r\n", "\n");
            if (normalizedText.EndsWith("\n", StringComparison.Ordinal))
            {
                normalizedText = normalizedText.Substring(0, normalizedText.Length - 1);
            }

            if (string.IsNullOrEmpty(normalizedText))
            {
                return new string[0];
            }

            return normalizedText.Split(new char[] { '\n' }, StringSplitOptions.None);
        }
    }
}