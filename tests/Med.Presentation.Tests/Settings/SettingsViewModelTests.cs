using FluentAssertions;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Presentation.Abstractions;
using Med.Presentation.Diagnostics;
using Med.Presentation.Feedback;
using Med.Presentation.Settings;
using Med.Application.UseCases;
using Med.Domain.ValueObjects;
using Xunit;

namespace Med.Presentation.Tests.Settings;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task GenerateTelegramCode_пишет_link_code()
    {
        Guid userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        FakeAuth auth = new(userId);
        FakeLinks links = new();
        FakeProfiles profiles = new(Profile.Create(
            userId,
            "brenda",
            "UTC",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0))));

        SettingsViewModel vm = new(profiles, links, auth, new UpdateProfileUseCase(profiles), NewDiagnostics(), TestFeedback.Instance);

        await vm.GenerateTelegramCodeCommand.ExecuteAsync(null);

        vm.TelegramLinkCode.Should().NotBeNullOrWhiteSpace();
        vm.TelegramLinkCode.Should().MatchRegex("^[0-9A-Z]{6}$");
        vm.IsLinkCardVisible.Should().BeTrue();
        vm.ActiveLinkCode.Should().Be(vm.TelegramLinkCode);
        vm.ActiveChannelName.Should().Be("Telegram");

        links.Stored.Should().ContainSingle(l =>
            l.ChannelType == MessengerChannelType.Telegram
            && l.LinkCode == vm.TelegramLinkCode
            && !l.IsConfirmed);

        // Проверяем закрытие карточки по клику на крестик
        vm.DismissLinkCardCommand.Execute(null);
        vm.IsLinkCardVisible.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateDiscordCode_пишет_6_значный_link_code()
    {
        Guid userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        FakeAuth auth = new(userId);
        FakeLinks links = new();
        FakeProfiles profiles = new(Profile.Create(
            userId,
            "brenda",
            "UTC",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0))));

        SettingsViewModel vm = new(profiles, links, auth, new UpdateProfileUseCase(profiles), NewDiagnostics(), TestFeedback.Instance);

        await vm.GenerateDiscordCodeCommand.ExecuteAsync(null);

        vm.DiscordLinkCode.Should().NotBeNullOrWhiteSpace();
        vm.DiscordLinkCode.Should().MatchRegex("^[0-9A-Z]{6}$");
        vm.IsLinkCardVisible.Should().BeTrue();
        vm.ActiveLinkCode.Should().Be(vm.DiscordLinkCode);
        vm.ActiveChannelName.Should().Be("Discord");

        links.Stored.Should().ContainSingle(l =>
            l.ChannelType == MessengerChannelType.Discord
            && l.LinkCode == vm.DiscordLinkCode
            && !l.IsConfirmed);
    }

    [Fact]
    public async Task Генерация_кода_обновляет_список_привязок()
    {
        FakeProfiles profiles = NewProfiles();
        FakeLinks links = new();
        SettingsViewModel vm = new(
            profiles,
            links,
            new FakeAuth(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new UpdateProfileUseCase(profiles),
            NewDiagnostics(),
            TestFeedback.Instance);

        await vm.RefreshCommand.ExecuteAsync(null);
        vm.IsLinksEmpty.Should().BeTrue();

        await vm.GenerateTelegramCodeCommand.ExecuteAsync(null);

        vm.Links.Should().ContainSingle();
        vm.IsLinksEmpty.Should().BeFalse();
    }

    [Theory]
    [InlineData("0", "Europe/Moscow")]
    [InlineData("+4", "Etc/GMT-7")]
    [InlineData("-2", "Etc/GMT-1")]
    [InlineData(" 4 ", "Etc/GMT-7")]
    public async Task Смещение_от_Москвы_сохраняется_как_фиксированная_зона(string input, string expectedZone)
    {
        FakeProfiles profiles = NewProfiles();
        SettingsViewModel vm = NewViewModel(profiles);
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.MoscowOffsetHours = input;

        await vm.SaveProfileCommand.ExecuteAsync(null);

        profiles.Current!.TimeZoneId.Should().Be(expectedZone);
        vm.ResolvedTimeZoneId.Should().Be(expectedZone);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("-13")]
    [InlineData("Europe/Berlin")]
    [InlineData("")]
    public async Task Недопустимое_смещение_не_сохраняется(string input)
    {
        FakeProfiles profiles = NewProfiles();
        SettingsViewModel vm = NewViewModel(profiles);
        await vm.RefreshCommand.ExecuteAsync(null);
        string before = profiles.Current!.TimeZoneId;

        vm.MoscowOffsetHours = input;

        await vm.SaveProfileCommand.ExecuteAsync(null);

        profiles.Current!.TimeZoneId.Should().Be(before);
    }

    [Fact]
    public async Task Refresh_читает_смещение_из_зоны_профиля()
    {
        FakeProfiles profiles = NewProfiles(MoscowOffset.FromHours(-2).ToTimeZoneId());
        SettingsViewModel vm = NewViewModel(profiles);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.MoscowOffsetHours.Should().Be("-2");
        vm.ResolvedTimeZoneId.Should().Be("Etc/GMT-1");
    }

    [Fact]
    public async Task Зона_вне_схемы_показывается_как_требующая_решения()
    {
        FakeProfiles profiles = NewProfiles("Europe/Berlin");
        SettingsViewModel vm = NewViewModel(profiles);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.MoscowOffsetHours.Should().BeEmpty();
    }

    [Fact]
    public async Task SignOut_вызывает_сервис_аутентификации()
    {
        FakeAuth auth = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        FakeProfiles profiles = NewProfiles();
        SettingsViewModel vm = new(
            profiles,
            new FakeLinks(),
            auth,
            new UpdateProfileUseCase(profiles),
            NewDiagnostics(),
            TestFeedback.Instance);

        await vm.SignOutCommand.ExecuteAsync(null);

        auth.SignedOut.Should().BeTrue();
    }

    private static FakeProfiles NewProfiles(string timeZoneId = "Europe/Moscow") =>
        new(Profile.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "brenda",
            timeZoneId,
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0))));

    private static SettingsViewModel NewViewModel(FakeProfiles profiles) =>
        new(profiles,
            new FakeLinks(),
            new FakeAuth(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new UpdateProfileUseCase(profiles),
            NewDiagnostics(),
            TestFeedback.Instance);

    /// <summary>Диагностика — вкладка настроек, поэтому ViewModel настроек её содержит.</summary>
    private static DiagnosticsViewModel NewDiagnostics() =>
        new(new FakeDeliveries(), new FakeTick(), new FakeRealtime(), new ImmediateUiDispatcher());

    private sealed class FakeDeliveries : INotificationDeliveryRepository
    {
        public Task<IReadOnlyList<NotificationDelivery>> ListByDoseEventAsync(
            Guid doseEventId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NotificationDelivery>>([]);

        public Task<IReadOnlyList<NotificationDelivery>> ListRecentAsync(
            int limit = 50,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NotificationDelivery>>([]);
    }

    private sealed class FakeTick : ITickInvoker
    {
        public Task<TickInvokeResult> InvokeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TickInvokeResult(true, "{}"));
    }

    private sealed class FakeRealtime : IDoseEventRealtime
    {
        public event EventHandler<DoseEventChange>? Changed
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAuth(Guid userId) : IAuthService
    {
        public bool SignedOut { get; private set; }

        public AuthSession? CurrentSession => new(userId, "a@b.c", "token", "refresh", DateTimeOffset.UtcNow.AddHours(1));

        public Guid? CurrentUserId => userId;

        public event EventHandler<AuthSession?>? AuthStateChanged
        {
            add { }
            remove { }
        }

        public Task<AuthSession> SignUpWithPasswordAsync(
            string email,
            string password,
            string? username = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<AuthSession> SignInWithPasswordAsync(
            string email,
            string password,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            SignedOut = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProfiles(Profile profile) : IProfileRepository
    {
        public Profile? Current { get; private set; } = profile;

        public Task<Profile?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task UpdateAsync(Profile profile, CancellationToken cancellationToken = default)
        {
            Current = profile;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLinks : IMessengerLinkRepository
    {
        public List<MessengerLink> Stored { get; } = [];

        public Task<IReadOnlyList<MessengerLink>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MessengerLink>>(Stored.ToArray());

        public Task<MessengerLink?> GetByChannelAsync(
            MessengerChannelType channelType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored.FirstOrDefault(l => l.ChannelType == channelType));

        public Task UpsertAsync(MessengerLink link, CancellationToken cancellationToken = default)
        {
            Stored.RemoveAll(l => l.Id == link.Id || l.ChannelType == link.ChannelType);
            Stored.Add(link);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
