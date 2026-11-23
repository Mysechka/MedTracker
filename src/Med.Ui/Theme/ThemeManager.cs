using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Messaging;
using Med.Application.Abstractions;
using Med.Presentation.Messaging;
using Med.Presentation.Settings;

namespace Med.Ui.Theme;

public static class ThemeManager
{
    public const string SettingsKey = "app_theme";
    public const string DefaultThemeId = "ClinicalCobalt";

    public static IReadOnlyList<AppThemeInfo> AllThemes => AppThemeInfo.AllThemes;

    private static IAppSettingsStore? _settingsStore;

    public static void Initialize(IAppSettingsStore? settingsStore)
    {
        _settingsStore = settingsStore;

        WeakReferenceMessenger.Default.Register<AppThemeChangedMessage>(typeof(ThemeManager), (_, msg) =>
        {
            ApplyTheme(msg.Value);
        });

        string savedThemeId = _settingsStore?.GetString(SettingsKey) ?? DefaultThemeId;
        AppThemeInfo themeToApply = AllThemes.FirstOrDefault(t => t.Id == savedThemeId) ?? AllThemes[1];
        ApplyTheme(themeToApply);
    }

    public static void ApplyTheme(AppThemeInfo theme)
    {
        if (Avalonia.Application.Current is not { } app)
        {
            return;
        }

        try
        {
            var merged = app.Resources.MergedDictionaries;
            var existingThemeInclude = merged.OfType<ResourceInclude>()
                .FirstOrDefault(r => r.Source != null &&
                                     (r.Source.OriginalString.Contains("Theme") ||
                                      r.Source.OriginalString.Contains("ThemeSageOlive") ||
                                      r.Source.OriginalString.Contains("ThemeClinicalCobalt") ||
                                      r.Source.OriginalString.Contains("ThemePorcelainWhite") ||
                                      r.Source.OriginalString.Contains("ThemeWarmCream") ||
                                      r.Source.OriginalString.Contains("ThemeCharcoalDark")) &&
                                     !r.Source.OriginalString.Contains("MedStyles") &&
                                     !r.Source.OriginalString.Contains("MedIcons"));

            var newInclude = new ResourceInclude(new Uri("avares://Med.Ui/App.axaml"))
            {
                Source = new Uri(theme.SourceUri)
            };

            if (existingThemeInclude != null)
            {
                int index = merged.IndexOf(existingThemeInclude);
                merged.RemoveAt(index);
                merged.Insert(index, newInclude);
            }
            else
            {
                merged.Add(newInclude);
            }

            app.RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
            _settingsStore?.SetString(SettingsKey, theme.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeManager] Ошибка смены темы: {ex.Message}");
        }
    }
}
