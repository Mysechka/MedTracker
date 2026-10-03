using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
    [NotifyCanExecuteChangedFor(nameof(SignUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _email = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasUsernameError;

    [ObservableProperty]
    private bool _hasPasswordError;

    [ObservableProperty]
    private bool _hasEmailError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [RelayCommand]
    public async Task StartLocalAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            HasUsernameError = true;
            ErrorMessage = "Введите имя пользователя";
            return;
        }

        HasUsernameError = false;
        ErrorMessage = string.Empty;

        await RunAsync(async () =>
        {
            AuthSession session = await _auth.SignUpWithPasswordAsync(
                string.Empty,
                string.Empty,
                Username.Trim(),
                cancellationToken);
            _feedback.Notify($"Добро пожаловать, {session.Username ?? Username}!");
        });
    }

    private bool CanSignUp => !string.IsNullOrWhiteSpace(Email) && Email.Contains('@') && Email.Contains('.');
    private bool CanSignIn => !string.IsNullOrWhiteSpace(Email) && Email.Contains('@') && Email.Contains('.');

    [RelayCommand]
    private void ShowRegistration()
    {
        IsRegistrationMode = true;
        ErrorMessage = string.Empty;
        HasUsernameError = false;
        HasPasswordError = false;
        HasEmailError = false;
    }

    [RelayCommand]
    private void ShowLogin()
    {
        IsRegistrationMode = false;
        ErrorMessage = string.Empty;
        HasUsernameError = false;
        HasPasswordError = false;
        HasEmailError = false;
    }

    [RelayCommand]
    private void ClearUsername()
    {
        Username = string.Empty;
        HasUsernameError = false;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void ClearEmail()
    {
        Email = string.Empty;
        HasEmailError = false;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void ClearPassword()
    {
        Password = string.Empty;
        HasPasswordError = false;
        ErrorMessage = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        if (!ValidateAuthFields())
        {
            return;
        }

        await RunAsync(async () =>
        {
            AuthSession session = await _auth.SignInWithPasswordAsync(Email.Trim(), Password, cancellationToken);
            _feedback.Notify($"Вход: {session.Username ?? Email}");
        });
    }

    [RelayCommand(CanExecute = nameof(CanSignUp))]
    private async Task SignUpAsync(CancellationToken cancellationToken)
    {
        if (!ValidateAuthFields())
        {
            return;
        }

        await RunAsync(async () =>
        {
            AuthSession session = await _auth.SignUpWithPasswordAsync(
                Email.Trim(),
                Password,
                string.IsNullOrWhiteSpace(Username) ? null : Username.Trim(),
                cancellationToken);
            _feedback.Notify($"Регистрация: {Username}");
        });
    }

    private bool ValidateAuthFields()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@') || !Email.Contains('.'))
        {
            HasEmailError = true;
            ErrorMessage = "Введите корректный адрес электронной почты (Email)";
            return false;
        }
        HasEmailError = false;

        if (IsRegistrationMode && string.IsNullOrWhiteSpace(Username))
        {
            HasUsernameError = true;
            ErrorMessage = "Введите имя пользователя";
            return false;
        }

        HasUsernameError = false;

        if (string.IsNullOrWhiteSpace(Password))
        {
            HasPasswordError = true;
            ErrorMessage = "Введите кодовое слово";
            return false;
        }

        if (Password.Length < 6)
        {
            HasPasswordError = true;
            ErrorMessage = "Кодовое слово должно содержать не менее 6 символов";
            return false;
        }

        HasPasswordError = false;
        return true;
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
            ErrorMessage = string.Empty;
            await action();
        }
        catch (Exception ex)
        {
            ErrorMessage = TranslateErrorMessage(ex.Message);
            _feedback.Notify(ErrorMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string TranslateErrorMessage(string message)
    {
        if (message.Contains("User already registered", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("user_already_exists", StringComparison.OrdinalIgnoreCase))
        {
            return "Пользователь с таким именем уже зарегистрирован. Переключитесь на «Войти в аккаунт».";
        }

        if (message.Contains("Invalid login credentials", StringComparison.OrdinalIgnoreCase))
        {
            return "Неверное имя пользователя или кодовое слово.";
        }

        if (message.Contains("Password should be at least", StringComparison.OrdinalIgnoreCase))
        {
            return "Кодовое слово должно содержать не менее 6 символов.";
        }

        return message;
    }
}
