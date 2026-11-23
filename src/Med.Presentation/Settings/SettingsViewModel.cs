using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;
using Med.Presentation.Abstractions;
using Med.Presentation.Diagnostics;
using Med.Presentation.Feedback;
using Med.Presentation.Messaging;

namespace Med.Presentation.Settings;

public sealed partial class SettingsViewModel : ViewModelBase, IDisposable
{
    public const string MoscowTimeZoneId = MoscowOffset.MoscowTimeZoneId;
    public const string NovosibirskTimeZoneId = "Etc/GMT-7";

    private readonly IProfileRepository _profiles;
    private readonly IMessengerLinkRepository _links;
    private readonly IAuthService _auth;
    private readonly UpdateProfileUseCase _updateProfile;
    private readonly UserFeedback _feedback;
    private readonly IAppSettingsStore? _appSettings;
    private readonly IMessenger _messenger;
    private readonly IUiDispatcher _ui;
    private readonly Timer? _clockTimer;

    public SettingsViewModel(
        IProfileRepository profiles,
        IMessengerLinkRepository links,
        IAuthService auth,
        UpdateProfileUseCase updateProfile,
        DiagnosticsViewModel diagnostics,
        UserFeedback feedback,
        IMessenger? messenger = null,
        IUiDispatcher? ui = null,
        IAppSettingsStore? appSettings = null)
    {
        _profiles = profiles;
        _links = links;
        _auth = auth;
        _updateProfile = updateProfile;
        _feedback = feedback;
        Diagnostics = diagnostics;
        _appSettings = appSettings;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _ui = ui ?? new ImmediateUiDispatcher();

        if (_appSettings is not null)
        {
            _uiScale = Math.Clamp(_appSettings.GetDouble("ui_scale", 1.0), 0.75, 1.35);
        }

        string savedThemeId = _appSettings?.GetString("app_theme") ?? "ClinicalCobalt";
        _currentTheme = AppThemeInfo.AllThemes.FirstOrDefault(t => t.Id == savedThemeId) ?? AppThemeInfo.AllThemes[1];
        _selectedPreviewTheme = _currentTheme;

        foreach (var t in AppThemeInfo.AllThemes)
        {
            ThemeOptions.Add(new ThemeOptionViewModel(t, t.Id == _currentTheme.Id));
        }

        foreach (var city in DefaultCities)
        {
            FilteredCities.Add(city);
        }

        // Auto-detect local system timezone
        TimeSpan localOffset = TimeZoneInfo.Local.BaseUtcOffset;
        int localUtcHours = (int)Math.Round(localOffset.TotalHours);
        var autoCity = DefaultCities.FirstOrDefault(c => c.UtcOffsetHours == localUtcHours)
            ?? DefaultCities.First(c => c.CityName == "Москва");
        _tempSelectedCity = autoCity;
        _selectedTimeZone = autoCity.TimeZoneId;
        _resolvedTimeZoneId = autoCity.TimeZoneId;
        _moscowOffsetHours = autoCity.MoscowOffsetHours.ToString();

        UpdateTimePreviews();
        _clockTimer = new Timer(_ => _ui.Post(UpdateTimePreviews), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        _auth.AuthStateChanged += (s, e) =>
        {
            _ui.Post(() =>
            {
                OnPropertyChanged(nameof(CanLinkMessengers));
                OnPropertyChanged(nameof(IsLocalOnly));
                GenerateTelegramCodeCommand.NotifyCanExecuteChanged();
                GenerateDiscordCodeCommand.NotifyCanExecuteChanged();
            });
        };
    }

    public bool CanLinkMessengers => !_auth.IsLocalOnly;
    public bool IsLocalOnly => _auth.IsLocalOnly;

    /// <summary>Диагностика — вкладка настроек, отдельного пункта навигации у неё нет.</summary>
    public DiagnosticsViewModel Diagnostics { get; }

    public ObservableCollection<MessengerLink> Links { get; } = [];

    /// <summary>Масштаб интерфейса (от 0.80 до 1.25, по умолчанию 1.0).</summary>
    [ObservableProperty]
    private double _uiScale = 1.0;

    public string UiScalePercentText => $"{Math.Round(UiScale * 100):0}%";

    partial void OnUiScaleChanged(double value)
    {
        double clamped = Math.Clamp(Math.Round(value, 2), 0.75, 1.35);
        if (Math.Abs(value - clamped) > 0.001)
        {
            UiScale = clamped;
            return;
        }

        OnPropertyChanged(nameof(UiScalePercentText));
        _messenger.Send(new UiScaleChangedMessage(_uiScale));
        _appSettings?.SetDouble("ui_scale", _uiScale);
    }

    [RelayCommand]
    private void SetUiScale(string? scaleText)
    {
        if (!string.IsNullOrWhiteSpace(scaleText) &&
            double.TryParse(scaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            UiScale = parsed;
        }
    }

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _selectedTimeZone = MoscowTimeZoneId;

    [ObservableProperty]
    private bool _isMoscowSelected = true;

    [ObservableProperty]
    private bool _isNovosibirskSelected;

    [ObservableProperty]
    private string _moscowCurrentTime = "--:--";

    [ObservableProperty]
    private string _novosibirskCurrentTime = "--:--";

    public string SelectedTimeZoneTitle => IsNovosibirskSelected
        ? "Новосибирск (UTC+7, МСК+4)"
        : "Москва (UTC+3, базовое время)";

    /// <summary>Смещение от Москвы целым числом часов: 0 — Москва, «+4», «-2».</summary>
    [ObservableProperty]
    private string _moscowOffsetHours = "0";

    partial void OnMoscowOffsetHoursChanged(string value)
    {
        string text = value.Trim().TrimStart('+');
        if (int.TryParse(text, out int hours))
        {
            if (hours == 4)
            {
                _isNovosibirskSelected = true;
                _isMoscowSelected = false;
                _selectedTimeZone = NovosibirskTimeZoneId;
            }
            else if (hours == 0)
            {
                _isMoscowSelected = true;
                _isNovosibirskSelected = false;
                _selectedTimeZone = MoscowTimeZoneId;
            }
            else
            {
                _isMoscowSelected = false;
                _isNovosibirskSelected = false;
            }
        }
        else
        {
            _isMoscowSelected = false;
            _isNovosibirskSelected = false;
        }

        OnPropertyChanged(nameof(IsMoscowSelected));
        OnPropertyChanged(nameof(IsNovosibirskSelected));
        OnPropertyChanged(nameof(SelectedTimeZone));
        OnPropertyChanged(nameof(SelectedTimeZoneTitle));
        OnPropertyChanged(nameof(ActiveTimeZoneDisplayTitle));
        OnPropertyChanged(nameof(ActiveTimeZoneCurrentTime));
    }

    /// <summary>Итоговая зона профиля — только для чтения, чтобы видеть, что уходит в базу.</summary>
    [ObservableProperty]
    private string _resolvedTimeZoneId = MoscowOffset.MoscowTimeZoneId;

    public string OffsetHint =>
        $"Целое число часов от Москвы, от {MoscowOffset.MinHours} до {MoscowOffset.MaxHours}. 0 — московское время.";

    public static readonly IReadOnlyList<WorldTimezoneCity> DefaultCities =
    [
        new WorldTimezoneCity("Самоа", "Самоа", -11, -14, "Etc/GMT+11", 4.2, 40.0),
        new WorldTimezoneCity("Гонолулу", "США", -10, -13, "Etc/GMT+10", 8.3, 38.0),
        new WorldTimezoneCity("Анкоридж", "США", -9, -12, "Etc/GMT+9", 12.5, 18.0),
        new WorldTimezoneCity("Лос-Анджелес", "США", -8, -11, "Etc/GMT+8", 17.2, 35.0),
        new WorldTimezoneCity("Денвер", "США", -7, -10, "Etc/GMT+7", 20.8, 32.0),
        new WorldTimezoneCity("Чикаго", "США", -6, -9, "Etc/GMT+6", 25.0, 30.0),
        new WorldTimezoneCity("Нью-Йорк", "США", -5, -8, "Etc/GMT+5", 29.4, 30.3),
        new WorldTimezoneCity("Сантьяго", "Чили", -4, -7, "Etc/GMT+4", 33.3, 45.0),
        new WorldTimezoneCity("Буэнос-Айрес", "Аргентина", -3, -6, "Etc/GMT+3", 37.5, 46.0),
        new WorldTimezoneCity("Среднеатлантическое", "Атлантика", -2, -5, "Etc/GMT+2", 41.7, 38.0),
        new WorldTimezoneCity("Кабо-Верде", "Кабо-Верде", -1, -4, "Etc/GMT+1", 45.8, 32.0),
        new WorldTimezoneCity("Лондон", "Великобритания", 0, -3, "Etc/UTC", 50.0, 22.7),
        new WorldTimezoneCity("Берлин", "Германия", 1, -2, "Etc/GMT-1", 53.7, 21.9),
        new WorldTimezoneCity("Калининград", "Россия", 2, -1, "Etc/GMT-2", 55.7, 20.4),
        new WorldTimezoneCity("Москва", "Россия", 3, 0, MoscowTimeZoneId, 60.4, 19.7),
        new WorldTimezoneCity("Самара", "Россия", 4, 1, "Etc/GMT-4", 63.9, 21.5),
        new WorldTimezoneCity("Екатеринбург", "Россия", 5, 2, "Etc/GMT-5", 66.8, 18.9),
        new WorldTimezoneCity("Омск", "Россия", 6, 3, "Etc/GMT-6", 70.4, 20.2),
        new WorldTimezoneCity("Новосибирск", "Россия", 7, 4, NovosibirskTimeZoneId, 73.0, 20.2),
        new WorldTimezoneCity("Иркутск", "Россия", 8, 5, "Etc/GMT-8", 79.0, 22.1),
        new WorldTimezoneCity("Якутск", "Россия", 9, 6, "Etc/GMT-9", 86.0, 15.3),
        new WorldTimezoneCity("Владивосток", "Россия", 10, 7, "Etc/GMT-10", 86.6, 28.6),
        new WorldTimezoneCity("Магадан", "Россия", 11, 8, "Etc/GMT-11", 91.9, 17.0),
        new WorldTimezoneCity("Камчатка", "Россия", 12, 9, "Etc/GMT-12", 94.1, 21.6)
    ];

    public IReadOnlyList<WorldTimezoneCity> AllCities => DefaultCities;

    public ObservableCollection<WorldTimezoneCity> FilteredCities { get; } = [];

    [ObservableProperty]
    private string _timezoneSearchQuery = string.Empty;

    partial void OnTimezoneSearchQueryChanged(string value)
    {
        FilteredCities.Clear();
        string q = value.Trim().ToLowerInvariant();
        var matches = string.IsNullOrWhiteSpace(q)
            ? DefaultCities
            : DefaultCities.Where(c => c.CityName.ToLowerInvariant().Contains(q) ||
                                       c.Country.ToLowerInvariant().Contains(q) ||
                                       c.FormattedUtc.ToLowerInvariant().Contains(q) ||
                                       c.FormattedMsk.ToLowerInvariant().Contains(q) ||
                                       c.FullTitle.ToLowerInvariant().Contains(q));
        foreach (var c in matches)
        {
            FilteredCities.Add(c);
        }
    }

    [ObservableProperty]
    private bool _isTimezoneModalOpen;

    [ObservableProperty]
    private WorldTimezoneCity? _tempSelectedCity;

    [ObservableProperty]
    private string _currentTimeInSelectedCity = "--:--";

    public string ActiveTimeZoneDisplayTitle
    {
        get
        {
            int offset = int.TryParse(MoscowOffsetHours, out int h) ? h : 0;
            var city = DefaultCities.FirstOrDefault(c => c.MoscowOffsetHours == offset);
            return city != null
                ? $"{city.CityName} ({city.FormattedUtc} · {city.FormattedMsk})"
                : SelectedTimeZoneTitle;
        }
    }

    public string ActiveTimeZoneCurrentTime
    {
        get
        {
            int offset = int.TryParse(MoscowOffsetHours, out int h) ? h : 0;
            int utc = 3 + offset;
            return DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(utc)).ToString("HH:mm");
        }
    }

    [RelayCommand]
    public void OpenTimezoneModal()
    {
        TimezoneSearchQuery = string.Empty;
        int currentOffset = int.TryParse(MoscowOffsetHours, out int h) ? h : 0;
        TempSelectedCity = DefaultCities.FirstOrDefault(c => c.MoscowOffsetHours == currentOffset)
            ?? DefaultCities.First(c => c.CityName == "Москва");
        UpdateTimePreviews();
        IsTimezoneModalOpen = true;
    }

    [RelayCommand]
    public void CloseTimezoneModal()
    {
        IsTimezoneModalOpen = false;
    }

    [RelayCommand]
    public void SelectCity(WorldTimezoneCity? city)
    {
        if (city is null) return;
        TempSelectedCity = city;
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        CurrentTimeInSelectedCity = utcNow.ToOffset(TimeSpan.FromHours(city.UtcOffsetHours)).ToString("HH:mm");
    }

    [RelayCommand]
    public void SelectNextZone()
    {
        if (TempSelectedCity is null)
        {
            SelectCity(DefaultCities.First(c => c.CityName == "Москва"));
            return;
        }

        int currentIdx = -1;
        for (int i = 0; i < DefaultCities.Count; i++)
        {
            if (DefaultCities[i].UtcOffsetHours == TempSelectedCity.UtcOffsetHours)
            {
                currentIdx = i;
                break;
            }
        }

        if (currentIdx < 0) currentIdx = 0;
        int nextIdx = (currentIdx + 1) % DefaultCities.Count;
        SelectCity(DefaultCities[nextIdx]);
    }

    [RelayCommand]
    public void SelectPreviousZone()
    {
        if (TempSelectedCity is null)
        {
            SelectCity(DefaultCities.First(c => c.CityName == "Москва"));
            return;
        }

        int currentIdx = -1;
        for (int i = 0; i < DefaultCities.Count; i++)
        {
            if (DefaultCities[i].UtcOffsetHours == TempSelectedCity.UtcOffsetHours)
            {
                currentIdx = i;
                break;
            }
        }

        if (currentIdx < 0) currentIdx = 0;
        int prevIdx = (currentIdx - 1 + DefaultCities.Count) % DefaultCities.Count;
        SelectCity(DefaultCities[prevIdx]);
    }

    [RelayCommand]
    public void SelectUtcBand(string? utcOffsetString)
    {
        if (int.TryParse(utcOffsetString, out int utc))
        {
            var match = DefaultCities.FirstOrDefault(c => c.UtcOffsetHours == utc);
            if (match != null)
            {
                SelectCity(match);
            }
        }
    }

    public void SelectCityByCoordinates(double xPercent, double yPercent)
    {
        WorldTimezoneCity? closest = null;
        double minDistance = double.MaxValue;

        foreach (var city in DefaultCities)
        {
            double dx = city.XPercent - xPercent;
            double dy = city.YPercent - yPercent;
            // X (долгота/меридиан часового пояса) имеет основной вес для зонального выбора
            double dist = (dx * dx) * 2.0 + (dy * dy);
            if (dist < minDistance)
            {
                minDistance = dist;
                closest = city;
            }
        }

        if (closest != null)
        {
            SelectCity(closest);
        }
    }

    [RelayCommand]
    public async Task ApplyTimezoneAsync(CancellationToken cancellationToken)
    {
        if (TempSelectedCity != null)
        {
            MoscowOffsetHours = TempSelectedCity.MoscowOffsetHours.ToString();
            SelectedTimeZone = TempSelectedCity.TimeZoneId;
            ResolvedTimeZoneId = TempSelectedCity.TimeZoneId;
            IsNovosibirskSelected = TempSelectedCity.TimeZoneId == NovosibirskTimeZoneId ||
                                   TempSelectedCity.CityName.Contains("Новосибирск", StringComparison.OrdinalIgnoreCase);
            IsMoscowSelected = TempSelectedCity.TimeZoneId == MoscowTimeZoneId ||
                               TempSelectedCity.CityName.Contains("Москва", StringComparison.OrdinalIgnoreCase);
            IsTimezoneModalOpen = false;
            OnPropertyChanged(nameof(SelectedTimeZoneTitle));
            OnPropertyChanged(nameof(ActiveTimeZoneDisplayTitle));
            OnPropertyChanged(nameof(ActiveTimeZoneCurrentTime));
            await SaveProfileAsync(cancellationToken);
        }
        else
        {
            IsTimezoneModalOpen = false;
        }
    }

    public void ResetToInitialState()
    {
        IsTimezoneModalOpen = false;
        IsThemeModalOpen = false;
    }

    public IReadOnlyList<AppThemeInfo> AvailableThemes => AppThemeInfo.AllThemes;

    public ObservableCollection<ThemeOptionViewModel> ThemeOptions { get; } = [];

    [ObservableProperty]
    private AppThemeInfo _currentTheme;

    [ObservableProperty]
    private AppThemeInfo _selectedPreviewTheme;

    partial void OnSelectedPreviewThemeChanged(AppThemeInfo value)
    {
        foreach (var opt in ThemeOptions)
        {
            opt.IsSelected = opt.Id == value.Id;
        }
    }

    [ObservableProperty]
    private bool _isThemeModalOpen;

    [RelayCommand]
    public void OpenThemeModal()
    {
        SelectedPreviewTheme = CurrentTheme;
        IsThemeModalOpen = true;
    }

    [RelayCommand]
    public void CloseThemeModal()
    {
        IsThemeModalOpen = false;
    }

    [RelayCommand]
    public void PreviewTheme(object? arg)
    {
        if (arg is ThemeOptionViewModel opt)
        {
            SelectedPreviewTheme = opt.Theme;
        }
        else if (arg is AppThemeInfo info)
        {
            SelectedPreviewTheme = info;
        }
    }

    [RelayCommand]
    public void ApplyTheme()
    {
        if (SelectedPreviewTheme != null)
        {
            CurrentTheme = SelectedPreviewTheme;
            _messenger.Send(new AppThemeChangedMessage(CurrentTheme));
            _appSettings?.SetString("app_theme", CurrentTheme.Id);
            _feedback.Notify($"Применена тема «{CurrentTheme.DisplayName}»");
        }
        IsThemeModalOpen = false;
    }

    [RelayCommand]
    private void SelectMoscow()
    {
        SelectedTimeZone = MoscowTimeZoneId;
        IsMoscowSelected = true;
        IsNovosibirskSelected = false;
        MoscowOffsetHours = "0";
        ResolvedTimeZoneId = MoscowTimeZoneId;
        OnPropertyChanged(nameof(SelectedTimeZoneTitle));
        OnPropertyChanged(nameof(ActiveTimeZoneDisplayTitle));
        OnPropertyChanged(nameof(ActiveTimeZoneCurrentTime));
    }

    [RelayCommand]
    private void SelectNovosibirsk()
    {
        SelectedTimeZone = NovosibirskTimeZoneId;
        IsMoscowSelected = false;
        IsNovosibirskSelected = true;
        MoscowOffsetHours = "4";
        ResolvedTimeZoneId = NovosibirskTimeZoneId;
        OnPropertyChanged(nameof(SelectedTimeZoneTitle));
        OnPropertyChanged(nameof(ActiveTimeZoneDisplayTitle));
        OnPropertyChanged(nameof(ActiveTimeZoneCurrentTime));
    }

    public void UpdateTimePreviews()
    {
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        MoscowCurrentTime = utcNow.ToOffset(TimeSpan.FromHours(3)).ToString("HH:mm");
        NovosibirskCurrentTime = utcNow.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm");
        if (TempSelectedCity != null)
        {
            CurrentTimeInSelectedCity = utcNow.ToOffset(TimeSpan.FromHours(TempSelectedCity.UtcOffsetHours)).ToString("HH:mm");
        }
        OnPropertyChanged(nameof(ActiveTimeZoneCurrentTime));
        OnPropertyChanged(nameof(ActiveTimeZoneDisplayTitle));
    }

    public void Dispose()
    {
        _clockTimer?.Dispose();
    }

    [ObservableProperty]
    private string _breakfast = "08:00";

    [ObservableProperty]
    private string _lunch = "13:00";

    [ObservableProperty]
    private string _dinner = "19:00";

    partial void OnBreakfastChanged(string value)
    {
        OnPropertyChanged(nameof(CanSaveMeals));
        OnPropertyChanged(nameof(CanSaveProfile));
        SaveMealsCommand.NotifyCanExecuteChanged();
        SaveProfileCommand.NotifyCanExecuteChanged();
    }

    partial void OnLunchChanged(string value)
    {
        OnPropertyChanged(nameof(CanSaveMeals));
        OnPropertyChanged(nameof(CanSaveProfile));
        SaveMealsCommand.NotifyCanExecuteChanged();
        SaveProfileCommand.NotifyCanExecuteChanged();
    }

    partial void OnDinnerChanged(string value)
    {
        OnPropertyChanged(nameof(CanSaveMeals));
        OnPropertyChanged(nameof(CanSaveProfile));
        SaveMealsCommand.NotifyCanExecuteChanged();
        SaveProfileCommand.NotifyCanExecuteChanged();
    }

    public bool CanSaveMeals =>
        IsValidTime(Breakfast) &&
        IsValidTime(Lunch) &&
        IsValidTime(Dinner);

    public bool CanSaveProfile => CanSaveMeals;

    private static bool IsValidTime(string? time)
    {
        if (string.IsNullOrWhiteSpace(time)) return false;
        return TimeOnly.TryParse(time.Trim(), out _);
    }

    [ObservableProperty]
    private string _confirmationWindowMinutes = "180";

    [ObservableProperty]
    private string _telegramLinkCode = string.Empty;

    [ObservableProperty]
    private string _discordLinkCode = string.Empty;

    [ObservableProperty]
    private bool _isLinkCardVisible;

    [ObservableProperty]
    private string _activeLinkCode = string.Empty;

    [ObservableProperty]
    private string _activeChannelName = "Telegram";

    [RelayCommand]
    private void DismissLinkCard()
    {
        IsLinkCardVisible = false;
    }

    [ObservableProperty]
    private SettingsSection _selectedSection = SettingsSection.Menu;

    [ObservableProperty]
    private bool _isBusy;

    public bool ShowMenu => SelectedSection == SettingsSection.Menu;

    public bool ShowMeals => SelectedSection == SettingsSection.Meals;

    public bool ShowMessengers => SelectedSection == SettingsSection.Messengers;

    public bool ShowDiagnostics => SelectedSection == SettingsSection.Diagnostics;

    public bool ShowBack => SelectedSection != SettingsSection.Menu;

    partial void OnSelectedSectionChanged(SettingsSection value)
    {
        OnPropertyChanged(nameof(ShowMenu));
        OnPropertyChanged(nameof(ShowMeals));
        OnPropertyChanged(nameof(ShowMessengers));
        OnPropertyChanged(nameof(ShowDiagnostics));
        OnPropertyChanged(nameof(ShowBack));
    }

    /// <summary>Привязок нет — основание показать пустое состояние на вкладке мессенджеров.</summary>
    [ObservableProperty]
    private bool _isLinksEmpty;

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(() => LoadAsync(cancellationToken));
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Profile? profile = await _profiles.GetCurrentAsync(cancellationToken);
        if (profile is not null)
        {
            Username = profile.Username;
            ResolvedTimeZoneId = profile.TimeZoneId;
            MoscowOffset? offset = MoscowOffset.TryFromTimeZoneId(profile.TimeZoneId);
            MoscowOffsetHours = offset?.Hours.ToString() ?? string.Empty;

            if (offset?.Hours == 4
                || string.Equals(profile.TimeZoneId, NovosibirskTimeZoneId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(profile.TimeZoneId, "Asia/Novosibirsk", StringComparison.OrdinalIgnoreCase))
            {
                SelectedTimeZone = NovosibirskTimeZoneId;
                IsNovosibirskSelected = true;
                IsMoscowSelected = false;
            }
            else
            {
                SelectedTimeZone = MoscowTimeZoneId;
                IsMoscowSelected = true;
                IsNovosibirskSelected = false;
            }

            Breakfast = profile.Meals.Breakfast.ToString("HH:mm");
            Lunch = profile.Meals.Lunch.ToString("HH:mm");
            Dinner = profile.Meals.Dinner.ToString("HH:mm");
            ConfirmationWindowMinutes = ((int)profile.ConfirmationWindow.TotalMinutes).ToString();
            OnPropertyChanged(nameof(SelectedTimeZoneTitle));
        }

        UpdateTimePreviews();

        Links.Clear();
        foreach (MessengerLink link in await _links.ListAsync(cancellationToken))
        {
            Links.Add(link);
            if (link.ChannelType == MessengerChannelType.Telegram && link.LinkCode is not null)
            {
                TelegramLinkCode = link.LinkCode;
                if (string.IsNullOrEmpty(ActiveLinkCode))
                {
                    ActiveLinkCode = link.LinkCode;
                    ActiveChannelName = "Telegram";
                }
            }

            if (link.ChannelType == MessengerChannelType.Discord && link.LinkCode is not null)
            {
                DiscordLinkCode = link.LinkCode;
                if (string.IsNullOrEmpty(ActiveLinkCode))
                {
                    ActiveLinkCode = link.LinkCode;
                    ActiveChannelName = "Discord";
                }
            }
        }

        IsLinksEmpty = Links.Count == 0;
    }

    [RelayCommand]
    private void OpenMealsSection() => SelectedSection = SettingsSection.Meals;

    [RelayCommand]
    private void OpenMessengersSection() => SelectedSection = SettingsSection.Messengers;

    [RelayCommand]
    private void OpenDiagnosticsSection() => SelectedSection = SettingsSection.Diagnostics;

    [RelayCommand]
    private void BackToMenu() => SelectedSection = SettingsSection.Menu;

    [RelayCommand(CanExecute = nameof(CanSaveMeals))]
    private async Task SaveMealsAsync(CancellationToken cancellationToken)
    {
        await SaveProfileAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanSaveProfile))]
    private async Task SaveProfileAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            if (!TimeOnly.TryParse(Breakfast, out TimeOnly breakfast)
                || !TimeOnly.TryParse(Lunch, out TimeOnly lunch)
                || !TimeOnly.TryParse(Dinner, out TimeOnly dinner))
            {
                throw new InvalidOperationException("Некорректное время приёма пищи.");
            }

            if (!int.TryParse(ConfirmationWindowMinutes, out int minutes) || minutes <= 0)
            {
                throw new InvalidOperationException("Окно подтверждения должно быть > 0.");
            }

            MoscowOffset offset = IsNovosibirskSelected
                ? MoscowOffset.FromHours(4)
                : (IsMoscowSelected ? MoscowOffset.Moscow : ParseOffset(MoscowOffsetHours));
            string timeZoneId = offset.ToTimeZoneId();

            await _updateProfile.ExecuteAsync(
                username: Username,
                timeZoneId: timeZoneId,
                meals: new MealWindows(breakfast, lunch, dinner),
                confirmationWindow: TimeSpan.FromMinutes(minutes),
                cancellationToken: cancellationToken);

            ResolvedTimeZoneId = timeZoneId;
            SelectedTimeZone = timeZoneId;
            _messenger.Send(new ScheduleUpdatedMessage(Guid.Empty, Guid.Empty, ChangeSource.Local));
            _feedback.Notify("Настройки сохранены");
        });
    }

    [RelayCommand]
    private void SelectBreakfast(string time) => Breakfast = time;

    [RelayCommand]
    private void SelectLunch(string time) => Lunch = time;

    [RelayCommand]
    private void SelectDinner(string time) => Dinner = time;

    [RelayCommand]
    private async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            await _auth.SignOutAsync(cancellationToken);
            _feedback.Notify("Выход выполнен.");
        });
    }

    [RelayCommand(CanExecute = nameof(CanLinkMessengers))]
    private async Task GenerateTelegramCodeAsync(CancellationToken cancellationToken)
    {
        await GenerateCodeAsync(MessengerChannelType.Telegram, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanLinkMessengers))]
    private async Task GenerateDiscordCodeAsync(CancellationToken cancellationToken)
    {
        await GenerateCodeAsync(MessengerChannelType.Discord, cancellationToken);
    }

    private const string LinkCodeCharacters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private async Task GenerateCodeAsync(MessengerChannelType channel, CancellationToken cancellationToken)
    {
        if (_auth.IsLocalOnly)
        {
            _feedback.Notify("Привязка ботов недоступна в локальном режиме. Привяжите аккаунт к облаку в разделе «Аккаунт».");
            return;
        }

        await RunAsync(async () =>
        {
            Guid userId = _auth.CurrentUserId
                ?? throw new InvalidOperationException("Нужна сессия.");

            string code = RandomNumberGenerator.GetString(LinkCodeCharacters, 6);
            MessengerLink? existing = await _links.GetByChannelAsync(channel, cancellationToken);
            MessengerLink link = MessengerLink.Create(
                existing?.Id ?? Guid.NewGuid(),
                userId,
                channel,
                chatId: null,
                channelId: null,
                isConfirmed: false,
                linkCode: code);

            await _links.UpsertAsync(link, cancellationToken);

            if (channel == MessengerChannelType.Telegram)
            {
                TelegramLinkCode = code;
                ActiveChannelName = "Telegram";
            }
            else
            {
                DiscordLinkCode = code;
                ActiveChannelName = "Discord";
            }

            ActiveLinkCode = code;
            IsLinkCardVisible = true;

            await LoadAsync(cancellationToken);
        });
    }

    private static MoscowOffset ParseOffset(string raw)
    {
        string text = raw.Trim();
        if (text.StartsWith('+'))
        {
            text = text[1..];
        }

        if (!int.TryParse(text, out int hours))
        {
            throw new InvalidOperationException(
                $"Смещение от Москвы задаётся целым числом часов от {MoscowOffset.MinHours} до {MoscowOffset.MaxHours}.");
        }

        if (hours is < MoscowOffset.MinHours or > MoscowOffset.MaxHours)
        {
            throw new InvalidOperationException(
                $"Смещение от Москвы допустимо от {MoscowOffset.MinHours} до {MoscowOffset.MaxHours} часов.");
        }

        return MoscowOffset.FromHours(hours);
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
