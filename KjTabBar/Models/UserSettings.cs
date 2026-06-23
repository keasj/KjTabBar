using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

using KjTabBar.Helpers;

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

        internal static XmlReaderSettings CreateSafeXmlReaderSettings()
        {
            XmlReaderSettings settings = new XmlReaderSettings();
            settings.DtdProcessing = DtdProcessing.Prohibit;
            settings.XmlResolver = null;
            settings.MaxCharactersInDocument = 1024 * 1024;
            return settings;
        }

        private static UserSettings NormalizeLoadedSettings(UserSettings settings)
        {
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

        internal static UserSettings LoadFromPath(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    XmlSerializer serializer = _serializer;
                    StreamReader reader = null;
                    XmlReader xmlReader = null;
                    try
                    {
                        reader = new StreamReader(path);
                        xmlReader = XmlReader.Create(reader, CreateSafeXmlReaderSettings());
                        UserSettings settings = serializer.Deserialize(xmlReader) as UserSettings;
                        return NormalizeLoadedSettings(settings);
                    }
                    finally
                    {
                        if (xmlReader != null)
                        {
                            xmlReader.Dispose();
                        }

                        if (reader != null)
                        {
                            reader.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("UserSettings", "Failed to load settings.xml.", ex);
            }
            return new UserSettings();
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
            return LoadFromPath(GetConfigPath());
        }

        public void Save()
        {
            string ignoredError;
            TrySave(out ignoredError);
        }

        public bool TrySave(out string errorMessage)
        {
            errorMessage = null;
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

                string tempPath = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    XmlSerializer serializer = _serializer;
                    StreamWriter writer = null;
                    try
                    {
                        writer = new StreamWriter(tempPath, false, new UTF8Encoding(false));
                        serializer.Serialize(writer, this);
                    }
                    finally
                    {
                        if (writer != null)
                        {
                            writer.Dispose();
                        }
                    }

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

                _current = this;
                EventHandler handler = SettingsChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("UserSettings", "Failed to save settings.xml.", ex);
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}