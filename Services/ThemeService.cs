using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace WinDebloat.Services
{
    public enum AppTheme
    {
        Dark,
        Light
    }

    public static class ThemeService
    {
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinDebloat"
        );
        private static readonly string ThemeFilePath = Path.Combine(SettingsFolder, "theme.txt");

        public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;
        public static event Action<AppTheme>? ThemeChanged;

        public static void Initialize()
        {
            AppTheme selectedTheme = DetectPreferredTheme();
            ApplyTheme(selectedTheme, save: false);
        }

        public static void ToggleTheme()
        {
            AppTheme nextTheme = CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
            ApplyTheme(nextTheme, save: true);
        }

        public static void ApplyTheme(AppTheme theme, bool save = true)
        {
            if (Application.Current == null) return;

            void Apply()
            {
                string themeSource = theme == AppTheme.Light
                    ? "pack://application:,,,/Themes/LightTheme.xaml"
                    : "pack://application:,,,/Themes/DarkTheme.xaml";

                var newDict = new ResourceDictionary
                {
                    Source = new Uri(themeSource, UriKind.Absolute)
                };

                var merged = Application.Current.Resources.MergedDictionaries;
                // Find and remove existing theme dictionary if present
                ResourceDictionary? existingThemeDict = null;
                foreach (var dict in merged)
                {
                    if (dict.Source != null && 
                        (dict.Source.OriginalString.Contains("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
                         dict.Source.OriginalString.Contains("LightTheme.xaml", StringComparison.OrdinalIgnoreCase)))
                    {
                        existingThemeDict = dict;
                        break;
                    }
                }

                if (existingThemeDict != null)
                {
                    merged.Remove(existingThemeDict);
                }

                merged.Add(newDict);
                CurrentTheme = theme;

                if (save)
                {
                    SaveThemePreference(theme);
                }

                ThemeChanged?.Invoke(theme);
            }

            if (Application.Current.Dispatcher.CheckAccess())
            {
                Apply();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(Apply);
            }
        }

        private static AppTheme DetectPreferredTheme()
        {
            try
            {
                if (File.Exists(ThemeFilePath))
                {
                    string saved = File.ReadAllText(ThemeFilePath).Trim();
                    if (Enum.TryParse<AppTheme>(saved, true, out var parsedTheme))
                    {
                        return parsedTheme;
                    }
                }
            }
            catch
            {
                // Fall back to system preference
            }

            return DetectWindowsSystemTheme();
        }

        private static AppTheme DetectWindowsSystemTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    object? val = key.GetValue("AppsUseLightTheme");
                    if (val is int intVal && intVal == 1)
                    {
                        return AppTheme.Light;
                    }
                }
            }
            catch
            {
                // Fallback to dark
            }

            return AppTheme.Dark;
        }

        private static void SaveThemePreference(AppTheme theme)
        {
            try
            {
                if (!Directory.Exists(SettingsFolder))
                {
                    Directory.CreateDirectory(SettingsFolder);
                }
                File.WriteAllText(ThemeFilePath, theme.ToString());
            }
            catch
            {
                // Silently ignore disk write issues for preference
            }
        }
    }
}
