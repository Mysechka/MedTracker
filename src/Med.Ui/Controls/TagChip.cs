using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;

namespace Med.Ui.Controls;

/// <summary>М3 filter chip: переключаемый тег без бизнес-логики.</summary>
public sealed class TagChip : TemplatedControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<TagChip, string>(nameof(Label), string.Empty);

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<TagChip, bool>(
            nameof(IsSelected),
            defaultValue: false,
            defaultBindingMode: BindingMode.TwoWay);

    static TagChip()
    {
        IsSelectedProperty.Changed.AddClassHandler<TagChip>((chip, e) =>
        {
            chip.PseudoClasses.Set(":selected", e.NewValue is true);
        });
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            IsSelected = !IsSelected;
            e.Handled = true;
        }
    }
}
