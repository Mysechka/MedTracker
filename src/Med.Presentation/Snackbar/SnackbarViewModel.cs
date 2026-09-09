using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Med.Presentation.Abstractions;

namespace Med.Presentation.Snackbar;

/// <summary>Состояние нижнего snackbar для привязки в <c>SnackbarHost</c>.</summary>
public sealed partial class SnackbarViewModel : ViewModelBase
{
    public Action? DismissAction { get; set; }
    public Action? PauseAction { get; set; }
    public Action? ResumeAction { get; set; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private SnackbarType _type = SnackbarType.Info;

    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);

    [RelayCommand]
    public void Dismiss()
    {
        DismissAction?.Invoke();
        IsOpen = false;
    }

    [RelayCommand]
    public void PointerEntered()
    {
        PauseAction?.Invoke();
    }

    [RelayCommand]
    public void PointerExited()
    {
        ResumeAction?.Invoke();
    }
}
