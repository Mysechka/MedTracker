using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Med.Application.Abstractions;
using Med.Presentation.Feedback;

namespace Med.Presentation.Shell;

/// <summary>Вход и регистрация по макету Figma.</summary>
public sealed partial class AuthViewModel : ViewModelBase
{
    private readonly IAuthService _auth;
    private readonly UserFeedback _feedback;

    public AuthViewModel(IAuthService auth, UserFeedback feedback)
    {
        _auth = auth;
        _feedback = feedback;
    }

    [ObservableProperty]
    private bool _isRegistrationMode = true;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasUsernameError;

    [ObservableProperty]
    private bool _hasPasswordError;

    [RelayCommand]
    private void ShowRegistration() => IsRegistrationMode = true;

    [RelayCommand]
    private void ShowLogin() => IsRegistrationMode = false;

    [RelayCommand]
    private void ClearUsername()
    {
        Username = string.Empty;
        HasUsernameError = false;
    }

    [RelayCommand]
    private void ClearPassword()
    {
        Password = string.Empty;
        HasPasswordError = false;
    }

    [RelayCommand]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        if (!ValidateAuthFields())
        {
            return;
        }

        await RunAsync(async () =>
        {
            string email = SynthesizeEmail(Username);
            AuthSession session = await _auth.SignInWithPasswordAsync(email, Password, cancellationToken);
            _feedback.Notify($"Вход: {Username}");
        });
    }

    [RelayCommand]
    private async Task SignUpAsync(CancellationToken cancellationToken)
    {
        if (!ValidateAuthFields())
        {
            return;
        }

        await RunAsync(async () =>
        {
            string email = SynthesizeEmail(Username);
            AuthSession session = await _auth.SignUpWithPasswordAsync(
                email,
                Password,
                Username.Trim(),
                cancellationToken);
            _feedback.Notify($"Регистрация: {Username}");
        });
    }

    private static string SynthesizeEmail(string username) =>
        $"{username.Trim().ToLowerInvariant()}@medtracker.local";

    private bool ValidateAuthFields()
    {
        HasUsernameError = string.IsNullOrWhiteSpace(Username);
        HasPasswordError = string.IsNullOrWhiteSpace(Password) || Password.Length < 6;
        return !HasUsernameError && !HasPasswordError;
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
            HasUsernameError = false;
            HasPasswordError = false;
            await action();
        }
        catch (Exception ex)
        {
            _feedback.Notify(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
