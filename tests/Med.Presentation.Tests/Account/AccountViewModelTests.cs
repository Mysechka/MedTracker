using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Entities;
using Med.Domain.ValueObjects;
using Med.Presentation.Abstractions;
using Med.Presentation.Account;
using Med.Presentation.Feedback;
using Med.Presentation.Messaging;
using Xunit;

namespace Med.Presentation.Tests.Account;

public sealed class AccountViewModelTests
{
    private static readonly Guid TestUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    [Fact]
    public async Task Refresh_загружает_имя_пользователя_и_считает_инициал()
    {
        Profile profile = CreateProfile("Александр");
        FakeProfileRepo repo = new(profile);
        FakeAuthService auth = new(TestUserId);
        AccountViewModel vm = CreateViewModel(repo, auth);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Username.Should().Be("Александр");
        vm.AvatarInitial.Should().Be("А");
        vm.HasUsernameError.Should().BeFalse();
    }

    [Fact]
    public async Task Save_с_валидными_данными_обновляет_профиль_и_пароль()
    {
        Profile profile = CreateProfile("СтароеИмя");
        FakeProfileRepo repo = new(profile);
        FakeAuthService auth = new(TestUserId);
        AccountViewModel vm = CreateViewModel(repo, auth);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Username = "НовоеИмя";
        vm.Codeword = "newSecret123";

        await vm.SaveCommand.ExecuteAsync(null);

        repo.UpdatedProfile.Should().NotBeNull();
        repo.UpdatedProfile!.Username.Should().Be("НовоеИмя");
        auth.LastUpdatedPassword.Should().Be("newSecret123");
        vm.Codeword.Should().BeEmpty();
        vm.HasUsernameError.Should().BeFalse();
        vm.HasCodewordError.Should().BeFalse();
    }

    [Fact]
    public async Task Save_с_пустым_именем_выставляет_ошибку_и_не_сохраняет()
    {
        Profile profile = CreateProfile("Иван");
        FakeProfileRepo repo = new(profile);
        FakeAuthService auth = new(TestUserId);
        AccountViewModel vm = CreateViewModel(repo, auth);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Username = "";

        await vm.SaveCommand.ExecuteAsync(null);

        vm.HasUsernameError.Should().BeTrue();
        repo.UpdatedProfile.Should().BeNull();
    }

    [Fact]
    public async Task Save_с_коротким_паролем_выставляет_ошибку()
    {
        Profile profile = CreateProfile("Иван");
        FakeProfileRepo repo = new(profile);
        FakeAuthService auth = new(TestUserId);
        AccountViewModel vm = CreateViewModel(repo, auth);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Username = "Иван";
        vm.Codeword = "123";

        await vm.SaveCommand.ExecuteAsync(null);

        vm.HasCodewordError.Should().BeTrue();
        auth.LastUpdatedPassword.Should().BeNull();
    }

    [Fact]
    public async Task SignOut_вызывает_сервис_аутентификации()
    {
        FakeProfileRepo repo = new(CreateProfile("Иван"));
        FakeAuthService auth = new(TestUserId);
        AccountViewModel vm = CreateViewModel(repo, auth);

        await vm.SignOutCommand.ExecuteAsync(null);

        auth.SignOutCalled.Should().BeTrue();
    }

    private static Profile CreateProfile(string username) =>
        Profile.Create(
            TestUserId,
            username,
            "Europe/Moscow",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)));

    private static AccountViewModel CreateViewModel(
        IProfileRepository repo,
        IAuthService auth,
        IFilePickerService? filePicker = null)
    {
        UpdateProfileUseCase useCase = new(repo);
        return new AccountViewModel(
            repo,
            auth,
            useCase,
            filePicker ?? new NullFilePickerService(),
            TestFeedback.Instance);
    }

    private sealed class FakeProfileRepo(Profile profile) : IProfileRepository
    {
        public Profile? UpdatedProfile { get; private set; }

        public Task<Profile?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Profile?>(UpdatedProfile ?? profile);

        public Task UpdateAsync(Profile newProfile, CancellationToken cancellationToken = default)
        {
            UpdatedProfile = newProfile;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthService(Guid userId) : IAuthService
    {
        public string? LastUpdatedPassword { get; private set; }
        public bool SignOutCalled { get; private set; }

        public AuthSession? CurrentSession => new(userId, "test@medtracker.local", "token", "refresh", DateTimeOffset.UtcNow.AddHours(1));
        public Guid? CurrentUserId => userId;

        public event EventHandler<AuthSession?>? AuthStateChanged
        {
            add { }
            remove { }
        }

        public Task<AuthSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AuthSession> SignUpWithPasswordAsync(string email, string password, string? username = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default)
        {
            LastUpdatedPassword = newPassword;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            SignOutCalled = true;
            return Task.CompletedTask;
        }
    }
}
