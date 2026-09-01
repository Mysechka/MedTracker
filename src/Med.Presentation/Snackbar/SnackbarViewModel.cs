using CommunityToolkit.Mvvm.ComponentModel;

namespace Med.Presentation.Snackbar;

/// <summary>Состояние нижнего snackbar для привязки в <c>SnackbarHost</c>.</summary>
public sealed partial class SnackbarViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isOpen;

    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
}
