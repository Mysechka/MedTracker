using FluentAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Presentation.Abstractions;
using Med.Presentation.Diagnostics;
using Med.Presentation.Feedback;
using Med.Presentation.Messaging;
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

    [Fact]
    public void GenerateCodes_В_локальном_режиме_недоступны()
    {
        FakeProfiles profiles = NewProfiles();
        FakeLinks links = new();
        FakeAuth auth = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), isLocalOnly: true);
        SettingsViewModel vm = new(
            profiles,
            links,
            auth,
            new UpdateProfileUseCase(profiles),
            NewDiagnostics(),
            TestFeedback.Instance);

        vm.CanLinkMessengers.Should().BeFalse();
        vm.IsLocalOnly.Should().BeTrue();
        vm.GenerateTelegramCodeCommand.CanExecute(null).Should().BeFalse();
        vm.GenerateDiscordCodeCommand.CanExecute(null).Should().BeFalse();
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

    [Fact]
    public void SelectNovosibirskCommand_устанавливает_зону_и_смещение()
    {
        FakeProfiles profiles = NewProfiles();
        SettingsViewModel vm = NewViewModel(profiles);

        vm.SelectNovosibirskCommand.Execute(null);

        vm.IsNovosibirskSelected.Should().BeTrue();
        vm.IsMoscowSelected.Should().BeFalse();
        vm.SelectedTimeZone.Should().Be(SettingsViewModel.NovosibirskTimeZoneId);
        vm.MoscowOffsetHours.Should().Be("4");
        vm.ResolvedTimeZoneId.Should().Be(SettingsViewModel.NovosibirskTimeZoneId);
        vm.SelectedTimeZoneTitle.Should().Contain("Новосибирск");
    }

    [Fact]
    public void SelectMoscowCommand_устанавливает_московскую_зону()
    {
        FakeProfiles profiles = NewProfiles();
        SettingsViewModel vm = NewViewModel(profiles);

        vm.SelectNovosibirskCommand.Execute(null);
        vm.SelectMoscowCommand.Execute(null);

        vm.IsMoscowSelected.Should().BeTrue();
        vm.IsNovosibirskSelected.Should().BeFalse();
        vm.SelectedTimeZone.Should().Be(SettingsViewModel.MoscowTimeZoneId);
        vm.MoscowOffsetHours.Should().Be("0");
        vm.ResolvedTimeZoneId.Should().Be(SettingsViewModel.MoscowTimeZoneId);
        vm.SelectedTimeZoneTitle.Should().Contain("Москва");
    }

    [Fact]
    public async Task SaveProfile_после_выбора_Новосибирска_сохраняет_Etc_GMT7_и_отправляет_сообщение()
    {
        FakeProfiles profiles = NewProfiles();
        StrongReferenceMessenger messenger = new();
        ScheduleUpdatedMessage? messageReceived = null;
        messenger.Register<ScheduleUpdatedMessage>(this, (_, msg) => messageReceived = msg);

        SettingsViewModel vm = new(
            profiles,
            new FakeLinks(),
            new FakeAuth(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new UpdateProfileUseCase(profiles),
            NewDiagnostics(),
            TestFeedback.Instance,
            messenger);

        await vm.RefreshCommand.ExecuteAsync(null);
        vm.SelectNovosibirskCommand.Execute(null);
        await vm.SaveProfileCommand.ExecuteAsync(null);

        profiles.Current!.TimeZoneId.Should().Be(SettingsViewModel.NovosibirskTimeZoneId);
        messageReceived.Should().NotBeNull();
    }

    [Fact]
    public async Task Refresh_инициализирует_выбор_Новосибирска_из_профиля()
    {
        FakeProfiles profiles = NewProfiles(SettingsViewModel.NovosibirskTimeZoneId);
        SettingsViewModel vm = NewViewModel(profiles);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.IsNovosibirskSelected.Should().BeTrue();
        vm.IsMoscowSelected.Should().BeFalse();
        vm.SelectedTimeZone.Should().Be(SettingsViewModel.NovosibirskTimeZoneId);
    }

    [Fact]
    public void UiScale_По_умолчанию_равен_1_и_отправляет_UiScaleChangedMessage_при_изменении()
    {
        Guid userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        FakeAuth auth = new(userId);
        FakeLinks links = new();
        FakeProfiles profiles = NewProfiles();
        WeakReferenceMessenger messenger = new();
        UiScaleChangedMessage? receivedMessage = null;
        messenger.Register<UiScaleChangedMessage>(this, (_, msg) => receivedMessage = msg);

        SettingsViewModel vm = new(
            profiles,
            links,
            auth,
            new UpdateProfileUseCase(profiles),
            NewDiagnostics(),
            TestFeedback.Instance,
            messenger: messenger);

        vm.UiScale.Should().Be(1.0);
        vm.UiScalePercentText.Should().Be("100%");

        vm.SetUiScaleCommand.Execute("1.25");
        vm.UiScale.Should().Be(1.25);
        vm.UiScalePercentText.Should().Be("125%");
        receivedMessage.Should().NotBeNull();
        receivedMessage!.Value.Should().Be(1.25);

        // Проверяем ограничение (clamping)
        vm.UiScale = 0.5;
        vm.UiScale.Should().Be(0.75);

        vm.UiScale = 2.0;
        vm.UiScale.Should().Be(1.35);
    }

    [Fact]
    public void UiScale_Сохраняется_в_хранилище_и_восстанавливается_при_новом_запуске()
    {
        FakeProfiles profiles = NewProfiles();
        FakeAppSettingsStore store = new();

        SettingsViewModel vm1 = new(
            profiles,
            new FakeLinks(),
            new FakeAuth(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new UpdateProfileUseCase(profiles),
            NewDiagnostics(),
            TestFeedback.Instance,
            appSettings: store);

        vm1.UiScale.Should().Be(1.0);

        // Пользователь меняет масштаб на 125%
        vm1.SetUiScaleCommand.Execute("1.25");
        vm1.UiScale.Should().Be(1.25);
        store.GetDouble("ui_scale", 1.0).Should().Be(1.25);

        // Имитируем новый запуск приложения (новая ViewModel с тем же постоянным хранилищем)
        SettingsViewModel vm2 = new(
            profiles,
            new FakeLinks(),
            new FakeAuth(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new UpdateProfileUseCase(profiles),
            NewDiagnostics(),
            TestFeedback.Instance,
            appSettings: store);

        vm2.UiScale.Should().Be(1.25);
        vm2.UiScalePercentText.Should().Be("125%");
    }

    [Fact]
    public void UpdateTimePreviews_заполняет_время_в_поясах()
    {
        FakeProfiles profiles = NewProfiles();
        SettingsViewModel vm = NewViewModel(profiles);

        vm.UpdateTimePreviews();

        vm.MoscowCurrentTime.Should().MatchRegex(@"^\d{2}:\d{2}$");
        vm.NovosibirskCurrentTime.Should().MatchRegex(@"^\d{2}:\d{2}$");
    }

    [Fact]
    public async Task TimezoneModal_открытие_поиск_выбор_города_и_применение()
    {
        FakeProfiles profiles = NewProfiles();
        SettingsViewModel vm = NewViewModel(profiles);

        // Открытие диалога
        vm.OpenTimezoneModalCommand.Execute(null);
        vm.IsTimezoneModalOpen.Should().BeTrue();

        // Поиск города
        vm.TimezoneSearchQuery = "Новосиб";
        vm.FilteredCities.Should().ContainSingle(c => c.CityName == "Новосибирск");

        // Выбор города
        vm.SelectCityCommand.Execute(vm.FilteredCities.First());
        vm.TempSelectedCity.Should().NotBeNull();
        vm.TempSelectedCity!.CityName.Should().Be("Новосибирск");

        // Применение
        await vm.ApplyTimezoneCommand.ExecuteAsync(null);
        vm.IsTimezoneModalOpen.Should().BeFalse();
        vm.IsNovosibirskSelected.Should().BeTrue();
        vm.SelectedTimeZone.Should().Be("Etc/GMT-7");
        vm.ActiveTimeZoneDisplayTitle.Should().Contain("Новосибирск");
    }

    [Fact]
    public void ThemeModal_выбор_предпросмотр_и_применение_сохраняется_в_store()
    {
        FakeProfiles profiles = NewProfiles();
        FakeAppSettingsStore store = new();
        SettingsViewModel vm = new(
            profiles,
            new FakeLinks(),
            new FakeAuth(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new UpdateProfileUseCase(profiles),
            NewDiagnostics(),
            TestFeedback.Instance,
            appSettings: store);

        // По умолчанию Clinical Cobalt Blue
        vm.CurrentTheme.Id.Should().Be("ClinicalCobalt");

        // Открытие модального окна
        vm.OpenThemeModalCommand.Execute(null);
        vm.IsThemeModalOpen.Should().BeTrue();
        vm.AvailableThemes.Should().HaveCount(5);

        // Предпросмотр темы Sage & Olive
        var sageTheme = vm.AvailableThemes.First(t => t.Id == "SageOlive");
        vm.PreviewThemeCommand.Execute(sageTheme);
        vm.SelectedPreviewTheme.Id.Should().Be("SageOlive");
        vm.CurrentTheme.Id.Should().Be("ClinicalCobalt"); // Ещё не применена

        // Применение темы
        vm.ApplyThemeCommand.Execute(null);
        vm.IsThemeModalOpen.Should().BeFalse();
        vm.CurrentTheme.Id.Should().Be("SageOlive");
        store.GetString("app_theme").Should().Be("SageOlive");
    }

    [Fact]
    public void SaveMealsCommand_НевалидноеВремя_ОтключаетКнопку()
    {
        SettingsViewModel vm = NewViewModel(NewProfiles());

        // По умолчанию 08:00, 13:00, 19:00 — валидно
        vm.CanSaveMeals.Should().BeTrue();
        vm.SaveMealsCommand.CanExecute(null).Should().BeTrue();

        // Пользователь ввёл некорректное значение (скриншот 5: "бурмм")
        vm.Breakfast = "бурмм";
        vm.CanSaveMeals.Should().BeFalse();
        vm.SaveMealsCommand.CanExecute(null).Should().BeFalse();
        vm.SaveProfileCommand.CanExecute(null).Should().BeFalse();

        // Возврат к корректному времени
        vm.Breakfast = "09:30";
        vm.CanSaveMeals.Should().BeTrue();
        vm.SaveMealsCommand.CanExecute(null).Should().BeTrue();
        vm.SaveProfileCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void SelectCityByCoordinates_находит_ближайший_город_и_выбирает_его()
    {
        SettingsViewModel vm = NewViewModel(NewProfiles());
        // Новосибирск имеет координаты (73.0, 20.2)
        vm.SelectCityByCoordinates(72.8, 20.5);
        vm.TempSelectedCity.Should().NotBeNull();
        vm.TempSelectedCity!.CityName.Should().Be("Новосибирск");
    }

    [Fact]
    public void ThemeOptions_переключение_темы_обновляет_IsSelected_только_у_выбранной()
    {
        FakeAppSettingsStore store = new();
        SettingsViewModel vm = new(
            NewProfiles(),
            new FakeLinks(),
            new FakeAuth(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new UpdateProfileUseCase(NewProfiles()),
            NewDiagnostics(),
            TestFeedback.Instance,
            appSettings: store);

        vm.ThemeOptions.Should().NotBeEmpty();
        var selectedBefore = vm.ThemeOptions.Where(t => t.IsSelected).ToList();
        selectedBefore.Should().ContainSingle();
        selectedBefore[0].Id.Should().Be("ClinicalCobalt");

        // Выбираем SageOlive
        var sage = vm.ThemeOptions.First(t => t.Id == "SageOlive");
        vm.PreviewTheme(sage);

        vm.ThemeOptions.Where(t => t.IsSelected).Select(t => t.Id).Should().Equal("SageOlive");
    }

    [Fact]
    public void ResetToInitialState_закрывает_все_модальные_окна()
    {
        SettingsViewModel vm = NewViewModel(NewProfiles());
        vm.OpenTimezoneModalCommand.Execute(null);
        vm.OpenThemeModalCommand.Execute(null);

        vm.IsTimezoneModalOpen.Should().BeTrue();
        vm.IsThemeModalOpen.Should().BeTrue();

        vm.ResetToInitialState();

        vm.IsTimezoneModalOpen.Should().BeFalse();
        vm.IsThemeModalOpen.Should().BeFalse();
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

    private sealed class FakeAuth(Guid userId, bool isLocalOnly = false) : IAuthService
    {
        public bool SignedOut { get; private set; }

        public bool IsLocalOnly => isLocalOnly;

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

        public Task UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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

    private sealed class FakeAppSettingsStore : IAppSettingsStore
    {
        private readonly Dictionary<string, string> _values = new();

        public string? GetString(string key) => _values.GetValueOrDefault(key);

        public void SetString(string key, string? value)
        {
            if (value is null) _values.Remove(key);
            else _values[key] = value;
        }

        public double GetDouble(string key, double defaultValue)
        {
            return _values.TryGetValue(key, out string? raw) &&
                   double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double val)
                ? val
                : defaultValue;
        }

        public void SetDouble(string key, double value) =>
            _values[key] = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }
}
