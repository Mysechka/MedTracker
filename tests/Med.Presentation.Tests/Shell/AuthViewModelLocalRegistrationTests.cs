using FluentAssertions;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Infrastructure.LocalStorage;
using Med.Presentation.Feedback;
using Med.Presentation.Shell;
using Xunit;

namespace Med.Presentation.Tests.Shell;

public sealed class AuthViewModelLocalRegistrationTests : IDisposable
{
    private readonly LocalDatabase _db;
    private readonly LocalProfileRepository _profileRepo;
    private readonly RepositoryModeProvider _modeProvider;
    private readonly LocalAuthService _authService;
    private readonly AuthViewModel _vm;

    public AuthViewModelLocalRegistrationTests()
    {
        string memConn = $"Data Source=InMemoryAuthTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _db = new LocalDatabase(memConn);
        _profileRepo = new LocalProfileRepository(_db);
        _modeProvider = new RepositoryModeProvider();
        _authService = new LocalAuthService(_profileRepo, _modeProvider);
        _vm = new AuthViewModel(_authService, TestFeedback.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task StartLocal_WithValidUsername_CreatesLocalSession()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _vm.Username = "TestPillMaster";

        await _vm.StartLocalAsync(ct);

        _vm.HasUsernameError.Should().BeFalse();
        _vm.ErrorMessage.Should().BeEmpty();
        _authService.CurrentSession.Should().NotBeNull();
        _authService.CurrentSession!.Username.Should().Be("TestPillMaster");
        _authService.CurrentSession.IsLocalOnly.Should().BeTrue();
        _authService.IsLocalOnly.Should().BeTrue();
    }

    [Fact]
    public async Task StartLocal_WithEmptyUsername_ShowsError()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _vm.Username = string.Empty;

        await _vm.StartLocalAsync(ct);

        _vm.HasUsernameError.Should().BeTrue();
        _vm.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        _authService.CurrentSession.Should().BeNull();
    }

    [Fact]
    public async Task StartLocal_WithWhitespaceUsername_ShowsError()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _vm.Username = "   ";

        await _vm.StartLocalAsync(ct);

        _vm.HasUsernameError.Should().BeTrue();
        _vm.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        _authService.CurrentSession.Should().BeNull();
    }

    [Fact]
    public async Task StartLocal_CreatesProfileInLocalDb()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _vm.Username = "AlexeyHealth";

        await _vm.StartLocalAsync(ct);

        Profile? profile = await _profileRepo.GetCurrentAsync(ct);
        profile.Should().NotBeNull();
        profile!.Username.Should().Be("AlexeyHealth");
        profile.UserId.Should().Be(_authService.CurrentUserId!.Value);
    }

    [Fact]
    public async Task SessionRestart_RestoresActiveUserAndProfile()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _vm.Username = "муся";
        await _vm.StartLocalAsync(ct);

        Guid originalUserId = _authService.CurrentUserId!.Value;

        // Simulate app restart by creating a new LocalAuthService against same database
        var restartedAuth = new LocalAuthService(_profileRepo, new RepositoryModeProvider(), _db);

        restartedAuth.CurrentSession.Should().NotBeNull();
        restartedAuth.CurrentSession!.Username.Should().Be("муся");
        restartedAuth.CurrentSession.UserId.Should().Be(originalUserId);
        restartedAuth.CurrentSession.IsLocalOnly.Should().BeTrue();
    }

    [Fact]
    public async Task MultipleUsers_RestoresLatestActiveUser()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _vm.Username = "Мусечка";
        await _vm.StartLocalAsync(ct);

        _vm.Username = "муся";
        await _vm.StartLocalAsync(ct);

        Guid activeUserId = _authService.CurrentUserId!.Value;

        var restartedAuth = new LocalAuthService(_profileRepo, new RepositoryModeProvider(), _db);

        restartedAuth.CurrentSession.Should().NotBeNull();
        restartedAuth.CurrentSession!.Username.Should().Be("муся");
        restartedAuth.CurrentSession.UserId.Should().Be(activeUserId);
    }

    [Fact]
    public async Task SignOut_DoesNotAutoRestoreSessionOnRestart()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _vm.Username = "муся";
        await _vm.StartLocalAsync(ct);

        await _authService.SignOutAsync(ct);
        _authService.CurrentSession.Should().BeNull();

        var restartedAuth = new LocalAuthService(_profileRepo, new RepositoryModeProvider(), _db);
        restartedAuth.CurrentSession.Should().BeNull();
    }
}
