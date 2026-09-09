using Avalonia.Controls;
using Avalonia.Input;
using Med.Presentation.Snackbar;

namespace Med.Ui.Controls;

public sealed partial class SnackbarHost : UserControl
{
    public SnackbarHost() => InitializeComponent();

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is SnackbarViewModel vm)
        {
            vm.PointerEntered();
        }
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is SnackbarViewModel vm)
        {
            vm.PointerExited();
        }
    }
}
