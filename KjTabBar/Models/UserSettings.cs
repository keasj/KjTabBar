using System;
using System.IO;
using System.Xml.Serialization;

namespace KjTabBar.Models
{
    public class UserSettings : IUserSettings
    {
        public const double DefaultFontSize = 11.5;
        public const double MinFontSize = 8.0;
        public const double MaxFontSize = 32.0;

        private static UserSettings _current;
        public static UserSettings Current
        {
            get
            {
                if (_current == null) _current = Load();
                return _current;
            }
        }

        public event EventHandler SettingsChanged;

        public string FontFamily { get; set; }
        public double FontSize { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }


        public UserSettings()
        {
            FontFamily = "Segoe UI";
            FontSize = DefaultFontSize;
            IsBold = false;
            IsItalic = false;

        }

        private static readonly XmlSerializer _serializer = new XmlSerializer(typeof(UserSettings));

        private static string GetConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "KjTabBar", "settings.xml");
        }

        public static double NormalizeFontSize(double fontSize)
        {
            if (double.IsNaN(fontSize) || double.IsInfinity(fontSize))
            {
                return DefaultFontSize;
            }

            if (fontSize < MinFontSize)
            {
                return MinFontSize;
            }

            if (fontSize > MaxFontSize)
            {
                return MaxFontSize;
            }

            return fontSize;
        }

        public static UserSettings Load()
        {
            try
            {
                string path = GetConfigPath();
                if (File.Exists(path))
                {
                    XmlSerializer serializer = _serializer;
                    StreamReader reader = null;
                    try
                    {
                        reader = new StreamReader(path);
                        UserSettings settings = (UserSettings)serializer.Deserialize(reader);
                        if (settings == null)
                        {
                            return new UserSettings();
                        }

                        settings.FontSize = NormalizeFontSize(settings.FontSize);
                        if (string.IsNullOrEmpty(settings.FontFamily))
                        {
                            settings.FontFamily = "Segoe UI";
                        }
                        return settings;
                    }
                    finally
                    {
                        if (reader != null)
                        {
                            reader.Dispose();
                        }
                    }
                }
            }
            catch { }
            return new UserSettings();
        }

        public void Save()
        {
            try
            {
                FontSize = NormalizeFontSize(FontSize);
                if (string.IsNullOrEmpty(FontFamily))
                {
                    FontFamily = "Segoe UI";
                }

                string path = GetConfigPath();
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                XmlSerializer serializer = _serializer;
                StreamWriter writer = null;
                try
                {
                    writer = new StreamWriter(path);
                    serializer.Serialize(writer, this);
                }
                finally
                {
                    if (writer != null)
                    {
                        writer.Dispose();
                    }
                }

                _current = this;
                EventHandler handler = SettingsChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
            catch { }
        }
    }
}
