using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Med.Presentation.Account;

namespace Med.Ui.Views.Screens;

public partial class AccountView : UserControl
{
    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<AccountView, bool>(nameof(IsCompact));

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    private bool _isDragging;
    private Point _lastPointerPosition;

    public AccountView()
    {
        InitializeComponent();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        bool compact = e.NewSize.Width < 680;
        IsCompact = compact;

        if (BottomCardsGrid != null && LeftCard != null && RightCard != null)
        {
            if (compact)
            {
                BottomCardsGrid.ColumnDefinitions = new ColumnDefinitions("*");
                BottomCardsGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
                Grid.SetColumn(LeftCard, 0);
                Grid.SetRow(LeftCard, 0);
                Grid.SetColumn(RightCard, 0);
                Grid.SetRow(RightCard, 1);
                RightCard.Margin = new Thickness(0, 18, 0, 0);
            }
            else
            {
                BottomCardsGrid.ColumnDefinitions = new ColumnDefinitions("*,*");
                BottomCardsGrid.RowDefinitions = new RowDefinitions("Auto");
                Grid.SetColumn(LeftCard, 0);
                Grid.SetRow(LeftCard, 0);
                Grid.SetColumn(RightCard, 1);
                Grid.SetRow(RightCard, 0);
                RightCard.Margin = new Thickness(0);
            }
        }
    }

    private void OnCropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not AccountViewModel vm || !vm.IsCropping)
        {
            return;
        }

        var control = sender as Control;
        var currentPoint = e.GetCurrentPoint(control);
        if (currentPoint.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _lastPointerPosition = e.GetPosition(control);
            e.Pointer.Capture(control);
            e.Handled = true;
        }
    }

    private void OnCropPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || DataContext is not AccountViewModel vm || !vm.IsCropping)
        {
            return;
        }

        var control = sender as Control;
        var currentPosition = e.GetPosition(control);
        var delta = currentPosition - _lastPointerPosition;
        _lastPointerPosition = currentPosition;

        vm.PanCrop(delta.X, delta.Y);
        e.Handled = true;
    }

    private void OnCropPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void OnCropPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDragging = false;
    }

    private void OnCropPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not AccountViewModel vm || !vm.IsCropping)
        {
            return;
        }

        double step = e.Delta.Y > 0 ? 0.2 : -0.2;
        vm.CropZoom = Math.Clamp(Math.Round(vm.CropZoom + step, 2), 0.5, 10.0);
        e.Handled = true;
    }
}
