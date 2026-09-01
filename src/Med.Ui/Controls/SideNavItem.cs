using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace Med.Ui.Controls;

public sealed class SideNavItem : TemplatedControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<SideNavItem, string>(nameof(Label), string.Empty);

    public static readonly StyledProperty<Geometry?> IconDataProperty =
        AvaloniaProperty.Register<SideNavItem, Geometry?>(nameof(IconData));

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<SideNavItem, bool>(nameof(IsSelected));

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<SideNavItem, ICommand?>(nameof(Command));

    static SideNavItem()
    {
        IsSelectedProperty.Changed.AddClassHandler<SideNavItem>((item, e) =>
        {
            item.PseudoClasses.Set(":selected", e.NewValue is true);
        });
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public Geometry? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        e.Handled = true;
        if (Command?.CanExecute(null) == true)
        {
            Command.Execute(null);
        }
    }
}
