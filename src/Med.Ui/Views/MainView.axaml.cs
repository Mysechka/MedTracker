using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Med.Ui.Views;

public sealed partial class MainView : UserControl
{
    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<MainView, bool>(nameof(IsCompact));

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public MainView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) =>
        {
            AvaloniaFilePickerService.FallbackTopLevelResolver = () => TopLevel.GetTopLevel(this);
        };

        // Автоматическая прокрутка к активному полю при фокусе (чтобы экранная клавиатура не закрывала ввод)
        AddHandler(InputElement.GotFocusEvent, (sender, e) =>
        {
            if (e.Source is Control control)
            {
                control.BringIntoView();
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await Task.Delay(250);
                    control.BringIntoView();
                });
            }
        });
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        IsCompact = e.NewSize.Width < 640;
    }
}
