using Avalonia;
using Avalonia.Controls;
using Med.Presentation.Settings;

namespace Med.Ui.Views.Screens;

public partial class SettingsView : UserControl
{
    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<SettingsView, bool>(nameof(IsCompact));

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public SettingsView()
    {
        InitializeComponent();
        TimezoneMap.CoordinatesSelectedCallback = (x, y) =>
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.SelectCityByCoordinates(x, y);
            }
        };
        TimezoneMap.StepNextZoneCallback = () =>
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.SelectNextZone();
            }
        };
        TimezoneMap.StepPreviousZoneCallback = () =>
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.SelectPreviousZone();
            }
        };
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        bool compact = e.NewSize.Width < 680;
        IsCompact = compact;

        if (TimezonesGrid != null && TimezoneCard != null && ThemeCard != null)
        {
            if (compact)
            {
                Grid.SetColumn(TimezoneCard, 0);
                Grid.SetRow(TimezoneCard, 0);
                Grid.SetColumn(ThemeCard, 0);
                Grid.SetRow(ThemeCard, 1);
                ThemeCard.Margin = new Thickness(0, 12, 0, 0);

                TimezonesGrid.ColumnDefinitions = new ColumnDefinitions("*");
                TimezonesGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            }
            else
            {
                TimezonesGrid.ColumnDefinitions = new ColumnDefinitions("*,*");
                TimezonesGrid.RowDefinitions = new RowDefinitions("Auto");

                Grid.SetColumn(TimezoneCard, 0);
                Grid.SetRow(TimezoneCard, 0);
                Grid.SetColumn(ThemeCard, 1);
                Grid.SetRow(ThemeCard, 0);
                ThemeCard.Margin = new Thickness(0);
            }
        }
    }
}
