using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Med.Application.Abstractions;

namespace Med.Presentation.Shell;

/// <summary>Минимальный вход: email/пароль и magic link — чтобы проверить логику на macOS.</summary>
public sealed partial class AuthViewModel(IAuthService auth) : ViewModelBase
{
    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            AuthSession session = await auth.SignInWithPasswordAsync(Email, Password, cancellationToken);
            Message = $"Вход: {session.Email}";
        });
    }

    [RelayCommand]
    private async Task SignUpAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            AuthSession session = await auth.SignUpWithPasswordAsync(
                Email,
                Password,
                string.IsNullOrWhiteSpace(Username) ? null : Username,
                cancellationToken);
            Message = $"Регистрация: {session.Email}";
        });
    }

    [RelayCommand]
    private async Task SendMagicLinkAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            await auth.SendMagicLinkAsync(Email, cancellationToken);
            Message = "Magic link отправлен (если email верный).";
        });
    }

    [RelayCommand]
    private async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            await auth.SignOutAsync(cancellationToken);
            Message = "Выход выполнен.";
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            Message = string.Empty;
            await action();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
