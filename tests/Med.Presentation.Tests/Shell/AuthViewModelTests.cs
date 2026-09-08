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
    }

    [Theory]
    [InlineData("", "password123", true, false)]
    [InlineData("   ", "password123", true, false)]
    [InlineData("user", "", false, true)]
    [InlineData("user", "12345", false, true)]
    public async Task Валидация_отклоняет_пустые_и_короткие_поля(
        string username,
        string password,
        bool expectUsernameError,
        bool expectPasswordError)
    {
        FakeAuthService auth = new();
        AuthViewModel vm = new(auth, TestFeedback.Instance);

        vm.Username = username;
        vm.Password = password;

        await vm.SignInCommand.ExecuteAsync(null);

        vm.HasUsernameError.Should().Be(expectUsernameError);
        vm.HasPasswordError.Should().Be(expectPasswordError);
        auth.LastSignInEmail.Should().BeNull();
    }

    private sealed class FakeAuthService : IAuthService
    {
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
            LastSignUpEmail = email;
            LastSignUpPassword = password;
            LastSignUpUsername = username;
            return Task.FromResult(new AuthSession(Guid.NewGuid(), email, "token", "refresh", DateTimeOffset.UtcNow.AddHours(1)));
        }

        public Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
