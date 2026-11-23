using System.Collections.Generic;

namespace Med.Presentation.Settings;

public sealed record AppThemeInfo(
    string Id,
    string DisplayName,
    string Description,
    string PrimaryHex,
    string SecondaryHex,
    string SurfaceHex,
    string BackgroundHex,
    string ContainerHex,
    bool IsDark,
    string SourceUri)
{
    public string TextHex => IsDark ? "#FFFFFF" : "#1A1C19";
    public string SecondaryTextHex => IsDark ? "#94A3B8" : "#49454F";

    public static readonly IReadOnlyList<AppThemeInfo> AllThemes =
    [
        new AppThemeInfo(
            Id: "SageOlive",
            DisplayName: "Sage & Olive Green",
            Description: "Натуральная зелёная палитра для снижения зрительной усталости",
            PrimaryHex: "#55624C",
            SecondaryHex: "#8FBC8F",
            SurfaceHex: "#FFFFFF",
            BackgroundHex: "#F4FAEF",
            ContainerHex: "#D8E7CB",
            IsDark: false,
            SourceUri: "avares://Med.Ui/Theme/ThemeSageOlive.axaml"),

        new AppThemeInfo(
            Id: "ClinicalCobalt",
            DisplayName: "Clinical Cobalt Blue",
            Description: "Клинический синий сапфир, строгий медицинский стиль",
            PrimaryHex: "#1E88E5",
            SecondaryHex: "#0D47A1",
            SurfaceHex: "#FFFFFF",
            BackgroundHex: "#F0F4F9",
            ContainerHex: "#D0E4FF",
            IsDark: false,
            SourceUri: "avares://Med.Ui/Theme/ThemeClinicalCobalt.axaml"),

        new AppThemeInfo(
            Id: "PorcelainWhite",
            DisplayName: "Pure Porcelain Minimalist",
            Description: "Чистый белый минимализм с высоким контрастом текста",
            PrimaryHex: "#1A1C19",
            SecondaryHex: "#4B5563",
            SurfaceHex: "#F8F9FA",
            BackgroundHex: "#FFFFFF",
            ContainerHex: "#ECEFF1",
            IsDark: false,
            SourceUri: "avares://Med.Ui/Theme/ThemePorcelainWhite.axaml"),

        new AppThemeInfo(
            Id: "WarmCream",
            DisplayName: "Warm Cream Alabaster",
            Description: "Тёплый алебастровый крем для комфортного чтения",
            PrimaryHex: "#B08968",
            SecondaryHex: "#A3704C",
            SurfaceHex: "#F0EAE1",
            BackgroundHex: "#FAF8F5",
            ContainerHex: "#E6D5C3",
            IsDark: false,
            SourceUri: "avares://Med.Ui/Theme/ThemeWarmCream.axaml"),

        new AppThemeInfo(
            Id: "CharcoalSlate",
            DisplayName: "Charcoal Slate Dark",
            Description: "Глубокая сланцевая тёмная тема для ночного режима",
            PrimaryHex: "#475569",
            SecondaryHex: "#64748B",
            SurfaceHex: "#1E222B",
            BackgroundHex: "#121418",
            ContainerHex: "#282D37",
            IsDark: true,
            SourceUri: "avares://Med.Ui/Theme/ThemeCharcoalDark.axaml")
    ];
}
