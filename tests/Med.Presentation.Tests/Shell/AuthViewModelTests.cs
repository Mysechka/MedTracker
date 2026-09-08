using FluentAssertions;
using Med.Application.Abstractions;
using Med.Presentation.Feedback;
using Med.Presentation.Shell;
using Xunit;

namespace Med.Presentation.Tests.Shell;

public sealed class AuthViewModelTests
{
    [Fact]
    public async Task SignIn_с_валидными_данными_синтезирует_email_и_вызывает_сервис()
    {
        FakeAuthService auth = new();
        AuthViewModel vm = new(auth, TestFeedback.Instance);

        vm.ShowLoginCommand.Execute(null);
        vm.Username = "mysechka";
        vm.Password = "secret123";

        await vm.SignInCommand.ExecuteAsync(null);

        auth.LastSignInEmail.Should().Be("mysechka@medtracker.local");
        auth.LastSignInPassword.Should().Be("secret123");
        vm.HasUsernameError.Should().BeFalse();
        vm.HasPasswordError.Should().BeFalse();
        vm.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task SignUp_с_валидными_данными_синтезирует_email_и_передает_username()
    {
        FakeAuthService auth = new();
        AuthViewModel vm = new(auth, TestFeedback.Instance);

        vm.ShowRegistrationCommand.Execute(null);
        vm.Username = "Egor";
        vm.Password = "codeWord999";

        await vm.SignUpCommand.ExecuteAsync(null);

        auth.LastSignUpEmail.Should().Be("egor@medtracker.local");
        auth.LastSignUpPassword.Should().Be("codeWord999");
        auth.LastSignUpUsername.Should().Be("Egor");
        vm.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task SignUp_с_кириллическим_именем_синтезирует_валидный_ASCII_email()
    {
        FakeAuthService auth = new();
        AuthViewModel vm = new(auth, TestFeedback.Instance);

        vm.ShowRegistrationCommand.Execute(null);
        vm.Username = "Егор";
        vm.Password = "codeWord999";

        await vm.SignUpCommand.ExecuteAsync(null);

        auth.LastSignUpEmail.Should().StartWith("user_").And.EndWith("@medtracker.local");
        auth.LastSignUpUsername.Should().Be("Егор");
        vm.ErrorMessage.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "password123", true, false, "Введите имя пользователя")]
    [InlineData("   ", "password123", true, false, "Введите имя пользователя")]
    [InlineData("user", "", false, true, "Введите кодовое слово")]
    [InlineData("user", "12345", false, true, "Кодовое слово должно содержать не менее 6 символов")]
    public async Task Валидация_отклоняет_пустые_и_короткие_поля(
        string username,
        string password,
        bool expectUsernameError,
        bool expectPasswordError,
        string expectedErrorMsg)
    {
        FakeAuthService auth = new();
        AuthViewModel vm = new(auth, TestFeedback.Instance);

        vm.Username = username;
        vm.Password = password;

        await vm.SignInCommand.ExecuteAsync(null);

        vm.HasUsernameError.Should().Be(expectUsernameError);
        vm.HasPasswordError.Should().Be(expectPasswordError);
        vm.ErrorMessage.Should().Be(expectedErrorMsg);
        auth.LastSignInEmail.Should().BeNull();
    }

    [Fact]
    public async Task Ошибка_существующего_пользователя_переводится_на_понятный_русский_текст()
    {
        FakeAuthService auth = new() { ShouldThrowOnSignUp = true, SignUpExceptionMessage = "User already registered" };
        AuthViewModel vm = new(auth, TestFeedback.Instance);

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
