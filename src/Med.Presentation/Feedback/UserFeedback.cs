using Med.Presentation.Abstractions;

namespace Med.Presentation.Feedback;

/// <summary>Единая точка пользовательских toast-сообщений для ViewModels.</summary>
public sealed class UserFeedback
{
    private readonly ISnackbarService _snackbar;

    public UserFeedback(ISnackbarService snackbar)
    {
        _snackbar = snackbar;
    }

    public void Notify(string message) => _ = _snackbar.ShowAsync(message);

    public void ShowLoginRequired() =>
        _ = _snackbar.ShowAsync(
            "Чтобы продолжить работу приложения, войдите в аккаунт или пройдите процедуру регистрации.",
            "Войдите в аккаунт!");
}
