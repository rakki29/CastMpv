using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CastMPV.Core
{
    public static class StartupManager
    {
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppValueName = "CastMPV";

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                object val = key?.GetValue(AppValueName);
                return val is string s && !string.IsNullOrWhiteSpace(s);
            }
            catch
            {
                return false;
            }
        }

        public static bool SetAutoStart(bool enable)
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                                      ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (key == null) return false;

                if (enable)
                {
                    string exePath = Application.ExecutablePath;
                    key.SetValue(AppValueName, $"\"{exePath}\" --background", RegistryValueKind.String);
                }
                else
                {
                    if (key.GetValue(AppValueName) != null)
                        key.DeleteValue(AppValueName, throwOnMissingValue: false);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
