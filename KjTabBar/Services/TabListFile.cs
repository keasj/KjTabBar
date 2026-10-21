using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KjTabBar.Services
{
    internal sealed class TabListFile
    {
        internal string[] Paths { get; private set; }
        internal int ActiveIndex { get; private set; }

        internal static TabListFile Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            using (StringReader reader = new StringReader(text)) return Parse(reader);
        }

        private static TabListFile Parse(TextReader reader)
        {
            string header = reader.ReadLine();
            int activeIndex;
            if (header == null || !header.StartsWith("active=", StringComparison.Ordinal) ||
                !int.TryParse(header.Substring(7), NumberStyles.None, CultureInfo.InvariantCulture, out activeIndex))
                throw new FormatException("Invalid active index.");
            List<string> paths = new List<string>();
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line.IndexOf('\0') >= 0)
                    throw new FormatException("Invalid tab entry.");
                paths.Add(line);
            }
            if (activeIndex >= paths.Count) throw new FormatException("Active index is out of range.");
            return new TabListFile { Paths = paths.ToArray(), ActiveIndex = activeIndex };
        }

        internal static TabListFile Read(string fileName)
        {
            // Stream UTF-8 without retaining duplicate full-file byte and character buffers.
            // Disable automatic encoding detection so UTF-16/32 remain invalid inputs.
            using (FileStream stream = File.OpenRead(fileName))
            {
                bool hasBom = stream.ReadByte() == 0xef && stream.ReadByte() == 0xbb && stream.ReadByte() == 0xbf;
                stream.Position = hasBom ? 3 : 0;
                using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false, true), false))
                    return Parse(reader);
            }
        }

        internal static string Serialize(string[] paths, int activeIndex)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            StringBuilder text = new StringBuilder("active=" + activeIndex.ToString(CultureInfo.InvariantCulture) + "\r\n");
            foreach (string path in paths)
            {
                if (path == null || path.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                    throw new FormatException("Invalid tab entry.");
                text.Append(path).Append("\r\n");
            }
            string result = text.ToString();
            Parse(result);
            return result;
        }

        internal static void Write(string fileName, string[] paths, int activeIndex)
        {
            string text = Serialize(paths, activeIndex);
            string temporary = fileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false, true));
                if (File.Exists(fileName)) File.Replace(temporary, fileName, null);
                else File.Move(temporary, fileName);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
