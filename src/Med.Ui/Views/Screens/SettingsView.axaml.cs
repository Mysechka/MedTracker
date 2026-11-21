using Avalonia;
using Avalonia.Controls;

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
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        bool compact = e.NewSize.Width < 680;
        IsCompact = compact;

        if (TimezonesGrid != null && MoscowCard != null && NovosibirskCard != null)
        {
            if (compact)
            {
                TimezonesGrid.ColumnDefinitions = new ColumnDefinitions("*");
                TimezonesGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
                Grid.SetColumn(MoscowCard, 0);
                Grid.SetRow(MoscowCard, 0);
                Grid.SetColumn(NovosibirskCard, 0);
                Grid.SetRow(NovosibirskCard, 1);
                NovosibirskCard.Margin = new Thickness(0, 12, 0, 0);
            }
            else
            {
                TimezonesGrid.ColumnDefinitions = new ColumnDefinitions("*,*");
                TimezonesGrid.RowDefinitions = new RowDefinitions("Auto");
                Grid.SetColumn(MoscowCard, 0);
                Grid.SetRow(MoscowCard, 0);
                Grid.SetColumn(NovosibirskCard, 1);
                Grid.SetRow(NovosibirskCard, 0);
                NovosibirskCard.Margin = new Thickness(0);
            }
        }
    }
}
