using FluentAssertions;
using Med.Application.Abstractions;
using Med.Presentation.Feedback;
using Med.Presentation.Shell;
using Xunit;

namespace Med.Presentation.Tests.Shell;

public sealed class AuthViewModelTests
{
    [Fact]
    public async Task SignIn_с_валидными_данными_передает_email_и_вызывает_сервис()
    {
        FakeAuthService auth = new();
        AuthViewModel vm = new(auth, TestFeedback.Instance);

        vm.ShowLoginCommand.Execute(null);
        vm.Email = "mysechka@example.com";
        vm.Password = "secret123";

        await vm.SignInCommand.ExecuteAsync(null);

        auth.LastSignInEmail.Should().Be("mysechka@example.com");
        auth.LastSignInPassword.Should().Be("secret123");
        vm.HasEmailError.Should().BeFalse();
        vm.HasPasswordError.Should().BeFalse();
        vm.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task SignUp_с_валидными_данными_передает_email_и_username()
    {
        FakeAuthService auth = new();
        AuthViewModel vm = new(auth, TestFeedback.Instance);

        vm.ShowRegistrationCommand.Execute(null);
        vm.Email = "egor@example.com";
        vm.Username = "Egor";
        vm.Password = "codeWord999";

        await vm.SignUpCommand.ExecuteAsync(null);

        auth.LastSignUpEmail.Should().Be("egor@example.com");
        auth.LastSignUpPassword.Should().Be("codeWord999");
        auth.LastSignUpUsername.Should().Be("Egor");
        vm.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public void Валидация_Email_проверяет_символы_собаки_и_точки()
    {
        FakeAuthService auth = new();
        AuthViewModel vm = new(auth, TestFeedback.Instance);

        vm.Email = "invalid-email";
        vm.Username = "Egor";
        vm.Password = "password123";

        vm.SignUpCommand.CanExecute(null).Should().BeFalse();

        vm.Email = "valid@email.com";
        vm.SignUpCommand.CanExecute(null).Should().BeTrue();
    }

    [Theory]
    [InlineData("", "user", "password123", true, false, false, "Введите корректный адрес электронной почты (Email)")]
    [InlineData("bad-email", "user", "password123", true, false, false, "Введите корректный адрес электронной почты (Email)")]
    [InlineData("valid@mail.com", "", "password123", false, true, false, "Введите имя пользователя")]
    [InlineData("valid@mail.com", "   ", "password123", false, true, false, "Введите имя пользователя")]
    [InlineData("valid@mail.com", "user", "", false, false, true, "Введите кодовое слово")]
    [InlineData("valid@mail.com", "user", "12345", false, false, true, "Кодовое слово должно содержать не менее 6 символов")]
    public async Task Валидация_отклоняет_невалидные_поля_при_регистрации(
        string email,
        string username,
        string password,
        bool expectEmailError,
        bool expectUsernameError,
        bool expectPasswordError,
        string expectedErrorMsg)
    {
        FakeAuthService auth = new();
        AuthViewModel vm = new(auth, TestFeedback.Instance);

        vm.ShowRegistrationCommand.Execute(null);
        vm.Email = email;
        vm.Username = username;
        vm.Password = password;

        await vm.SignUpCommand.ExecuteAsync(null);

        vm.HasEmailError.Should().Be(expectEmailError);
        vm.HasUsernameError.Should().Be(expectUsernameError);
        vm.HasPasswordError.Should().Be(expectPasswordError);
        vm.ErrorMessage.Should().Be(expectedErrorMsg);
        auth.LastSignUpEmail.Should().BeNull();
    }

    [Fact]
    public async Task Ошибка_существующего_пользователя_переводится_на_понятный_русский_текст()
    {
        FakeAuthService auth = new() { ShouldThrowOnSignUp = true, SignUpExceptionMessage = "User already registered" };
        AuthViewModel vm = new(auth, TestFeedback.Instance);

        vm.Email = "existing@example.com";
        vm.Username = "existing_user";
        vm.Password = "secret123";

        await vm.SignUpCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("уже зарегистрирован");
    }

    private sealed class FakeAuthService : IAuthService
    {
        public bool ShouldThrowOnSignUp { get; init; }
        public string SignUpExceptionMessage { get; init; } = "Error";

        public string? LastSignInEmail { get; private set; }
        public string? LastSignInPassword { get; private set; }
        public string? LastSignUpEmail { get; private set; }
        public string? LastSignUpPassword { get; private set; }
        public string? LastSignUpUsername { get; private set; }

        public AuthSession? CurrentSession => null;
        public Guid? CurrentUserId => null;

        public event EventHandler<AuthSession?>? AuthStateChanged
        {
            add { }
            remove { }
        }

        public Task<AuthSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
        {
            LastSignInEmail = email;
            LastSignInPassword = password;
            return Task.FromResult(new AuthSession(Guid.NewGuid(), email, "token", "refresh", DateTimeOffset.UtcNow.AddHours(1)));
        }

        public Task<AuthSession> SignUpWithPasswordAsync(
            string email,
            string password,
            string? username = null,
            CancellationToken cancellationToken = default)
        {
            if (ShouldThrowOnSignUp)
            {
                throw new InvalidOperationException(SignUpExceptionMessage);
            }

            LastSignUpEmail = email;
            LastSignUpPassword = password;
            LastSignUpUsername = username;
            return Task.FromResult(new AuthSession(Guid.NewGuid(), email, "token", "refresh", DateTimeOffset.UtcNow.AddHours(1), username));
        }

        public Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
