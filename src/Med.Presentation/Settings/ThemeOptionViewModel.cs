using CommunityToolkit.Mvvm.ComponentModel;

namespace Med.Presentation.Settings;

public sealed partial class ThemeOptionViewModel : ViewModelBase
{
    public AppThemeInfo Theme { get; }
    public string Id => Theme.Id;
    public string DisplayName => Theme.DisplayName;
    public string Description => Theme.Description;
    public string PrimaryHex => Theme.PrimaryHex;
    public string ContainerHex => Theme.ContainerHex;
    public string SurfaceHex => Theme.SurfaceHex;
    public string TextHex => Theme.TextHex;
    public string SecondaryTextHex => Theme.SecondaryTextHex;
    public string BackgroundHex => Theme.BackgroundHex;

    [ObservableProperty]
    private bool _isSelected;

    public ThemeOptionViewModel(AppThemeInfo theme, bool isSelected = false)
    {
        Theme = theme;
        _isSelected = isSelected;
    }
}
