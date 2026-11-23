using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Med.Presentation.Settings;

namespace Med.Ui.Controls;

public partial class TimezoneMapControl : UserControl
{
    public static readonly StyledProperty<WorldTimezoneCity?> SelectedCityProperty =
        AvaloniaProperty.Register<TimezoneMapControl, WorldTimezoneCity?>(nameof(SelectedCity));

    public WorldTimezoneCity? SelectedCity
    {
        get => GetValue(SelectedCityProperty);
        set => SetValue(SelectedCityProperty, value);
    }

    public static readonly StyledProperty<string> CurrentTimeProperty =
        AvaloniaProperty.Register<TimezoneMapControl, string>(nameof(CurrentTime), defaultValue: "--:--");

    public string CurrentTime
    {
        get => GetValue(CurrentTimeProperty);
        set => SetValue(CurrentTimeProperty, value);
    }

    public static readonly StyledProperty<ICommand?> SelectCoordinatesCommandProperty =
        AvaloniaProperty.Register<TimezoneMapControl, ICommand?>(nameof(SelectCoordinatesCommand));

    public ICommand? SelectCoordinatesCommand
    {
        get => GetValue(SelectCoordinatesCommandProperty);
        set => SetValue(SelectCoordinatesCommandProperty, value);
    }

    public static readonly StyledProperty<Action<double, double>?> CoordinatesSelectedCallbackProperty =
        AvaloniaProperty.Register<TimezoneMapControl, Action<double, double>?>(nameof(CoordinatesSelectedCallback));

    public Action<double, double>? CoordinatesSelectedCallback
    {
        get => GetValue(CoordinatesSelectedCallbackProperty);
        set => SetValue(CoordinatesSelectedCallbackProperty, value);
    }

    public static readonly StyledProperty<Action?> StepNextZoneCallbackProperty =
        AvaloniaProperty.Register<TimezoneMapControl, Action?>(nameof(StepNextZoneCallback));

    public Action? StepNextZoneCallback
    {
        get => GetValue(StepNextZoneCallbackProperty);
        set => SetValue(StepNextZoneCallbackProperty, value);
    }

    public static readonly StyledProperty<Action?> StepPreviousZoneCallbackProperty =
        AvaloniaProperty.Register<TimezoneMapControl, Action?>(nameof(StepPreviousZoneCallback));

    public Action? StepPreviousZoneCallback
    {
        get => GetValue(StepPreviousZoneCallbackProperty);
        set => SetValue(StepPreviousZoneCallbackProperty, value);
    }

    public TimezoneMapControl()
    {
        InitializeComponent();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y > 0 || e.Delta.X > 0)
        {
            StepNextZoneCallback?.Invoke();
            e.Handled = true;
        }
        else if (e.Delta.Y < 0 || e.Delta.X < 0)
        {
            StepPreviousZoneCallback?.Invoke();
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && MapCanvas != null && MapCanvas.Bounds.Width > 0 && MapCanvas.Bounds.Height > 0)
        {
            Point pt = e.GetPosition(MapCanvas);
            double xPercent = Math.Clamp((pt.X / MapCanvas.Bounds.Width) * 100.0, 0.0, 100.0);
            double yPercent = Math.Clamp((pt.Y / MapCanvas.Bounds.Height) * 100.0, 0.0, 100.0);
            CoordinatesSelectedCallback?.Invoke(xPercent, yPercent);
            if (SelectCoordinatesCommand?.CanExecute((xPercent, yPercent)) == true)
            {
                SelectCoordinatesCommand.Execute((xPercent, yPercent));
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (MapCanvas == null || MapCanvas.Bounds.Width <= 0 || MapCanvas.Bounds.Height <= 0)
        {
            return;
        }

        Point pt = e.GetPosition(MapCanvas);
        double xPercent = Math.Clamp((pt.X / MapCanvas.Bounds.Width) * 100.0, 0.0, 100.0);
        double yPercent = Math.Clamp((pt.Y / MapCanvas.Bounds.Height) * 100.0, 0.0, 100.0);

        CoordinatesSelectedCallback?.Invoke(xPercent, yPercent);
        if (SelectCoordinatesCommand?.CanExecute((xPercent, yPercent)) == true)
        {
            SelectCoordinatesCommand.Execute((xPercent, yPercent));
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedCityProperty)
        {
            UpdateVisualPositions();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateVisualPositions();
    }

    private void UpdateVisualPositions()
    {
        if (MapCanvas == null || MapCanvas.Bounds.Width <= 0 || MapCanvas.Bounds.Height <= 0 || SelectedCity == null)
        {
            return;
        }

        double width = MapCanvas.Bounds.Width;
        double height = MapCanvas.Bounds.Height;

        // Highlight timezone meridian band (vertical strip bounded by meridians)
        if (TimezoneBand != null)
        {
            double bandCenter = ((SelectedCity.UtcOffsetHours * 15.0 + 180.0) / 360.0) * width;
            double bandWidth = width / 24.0;
            double bandLeft = bandCenter - (bandWidth / 2.0);
            Canvas.SetLeft(TimezoneBand, Math.Max(0, bandLeft));
            Canvas.SetTop(TimezoneBand, 0);
            TimezoneBand.Width = bandWidth;
            TimezoneBand.Height = height;
            TimezoneBand.IsVisible = true;
        }

        // Position pin
        if (PinMarker != null)
        {
            double pinX = (SelectedCity.XPercent * width / 100.0) - 10.0;
            double pinY = (SelectedCity.YPercent * height / 100.0) - 10.0;
            Canvas.SetLeft(PinMarker, Math.Max(0, pinX));
            Canvas.SetTop(PinMarker, Math.Max(0, pinY));
            PinMarker.IsVisible = true;
        }

        // Position tooltip badge near pin
        if (CityTooltip != null)
        {
            double tooltipX = (SelectedCity.XPercent * width / 100.0) - 100.0;
            double tooltipY = (SelectedCity.YPercent * height / 100.0) - 52.0;

            // Keep within map boundaries
            tooltipX = Math.Clamp(tooltipX, 10.0, Math.Max(10.0, width - 240.0));
            tooltipY = Math.Clamp(tooltipY, 10.0, Math.Max(10.0, height - 50.0));

            Canvas.SetLeft(CityTooltip, tooltipX);
            Canvas.SetTop(CityTooltip, tooltipY);
            CityTooltip.IsVisible = true;
        }
    }
}
