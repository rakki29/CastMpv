using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CastMPV.Core
{
    [DataContract]
    public sealed class AppSettings
    {
        [DataMember]
        public string DeviceName { get; set; } = "CastMPV Receiver";

        [DataMember]
        public int DlnaPort { get; set; } = 8200;

        [DataMember]
        public string DeviceUuid { get; set; } = Guid.NewGuid().ToString("D");

        [DataMember]
        public bool DlnaEnabled { get; set; } = true;

        [DataMember]
        public bool AutoFullscreenOnCast { get; set; } = true;

        [DataMember]
        public bool AutoMinimizeOnStop { get; set; } = true;

        [DataMember]
        public bool StartMinimizedToTray { get; set; } = true;

        [DataMember]
        public bool MinimizeToTrayOnClose { get; set; } = true;

        [DataMember]
        public bool HardwareAcceleration { get; set; } = true;

        [DataMember]
        public int DefaultVolume { get; set; } = 85;

        [DataMember]
        public int SubtitleFontSize { get; set; } = 48;

        [DataMember]
        public string SubtitleColor { get; set; } = "#FFFFFF";

        [DataMember]
        public string DefaultUserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

        [DataMember]
        public string ScreenshotDirectory { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "CastMPV");

        private static string SettingsDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CastMPV");

        private static string SettingsFilePath =>
            Path.Combine(SettingsDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    byte[] bytes = File.ReadAllBytes(SettingsFilePath);
                    using var ms = new MemoryStream(bytes);
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    if (serializer.ReadObject(ms) is AppSettings loaded)
                    {
                        loaded.Normalize();
                        return loaded;
                    }
                }
            }
            catch
            {
                // Fall back to defaults if settings file is corrupted
            }

            var defaults = new AppSettings();
            defaults.Save();
            return defaults;
        }

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(DeviceName))
                DeviceName = "CastMPV Receiver";
            if (DlnaPort < 1024 || DlnaPort > 65535)
                DlnaPort = 8200;
            if (string.IsNullOrWhiteSpace(DeviceUuid))
                DeviceUuid = Guid.NewGuid().ToString("D");
            if (DefaultVolume < 0 || DefaultVolume > 100)
                DefaultVolume = 85;
            if (SubtitleFontSize < 16 || SubtitleFontSize > 120)
                SubtitleFontSize = 48;
            if (string.IsNullOrWhiteSpace(SubtitleColor) || !SubtitleColor.StartsWith("#"))
                SubtitleColor = "#FFFFFF";
            if (string.IsNullOrWhiteSpace(ScreenshotDirectory))
                ScreenshotDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CastMPV");
        }

        public void Save()
        {
            try
            {
                Normalize();
                Directory.CreateDirectory(SettingsDirectory);
                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                using var ms = new MemoryStream();
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, ownsStream: false, indent: true))
                {
                    serializer.WriteObject(writer, this);
                }
                File.WriteAllBytes(SettingsFilePath, ms.ToArray());
            }
            catch
            {
                // Ignore disk write errors on read-only environments
            }
        }
    }
}
