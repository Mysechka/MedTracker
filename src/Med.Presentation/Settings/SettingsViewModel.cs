using System.Collections.ObjectModel;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;
using Med.Presentation.Diagnostics;
using Med.Presentation.Feedback;

namespace Med.Presentation.Settings;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly IProfileRepository _profiles;
    private readonly IMessengerLinkRepository _links;
    private readonly IAuthService _auth;
    private readonly UpdateProfileUseCase _updateProfile;
    private readonly UserFeedback _feedback;

    public SettingsViewModel(
        IProfileRepository profiles,
        IMessengerLinkRepository links,
        IAuthService auth,
        UpdateProfileUseCase updateProfile,
        DiagnosticsViewModel diagnostics,
        UserFeedback feedback)
    {
        _profiles = profiles;
        _links = links;
        _auth = auth;
        _updateProfile = updateProfile;
        _feedback = feedback;
        Diagnostics = diagnostics;
    }

    /// <summary>Диагностика — вкладка настроек, отдельного пункта навигации у неё нет.</summary>
    public DiagnosticsViewModel Diagnostics { get; }

    public ObservableCollection<MessengerLink> Links { get; } = [];

    [ObservableProperty]
    private string _username = string.Empty;

    /// <summary>Смещение от Москвы целым числом часов: 0 — Москва, «+4», «-2».</summary>
    [ObservableProperty]
    private string _moscowOffsetHours = "0";

    /// <summary>Итоговая зона профиля — только для чтения, чтобы видеть, что уходит в базу.</summary>
    [ObservableProperty]
    private string _resolvedTimeZoneId = MoscowOffset.MoscowTimeZoneId;

    public string OffsetHint =>
        $"Целое число часов от Москвы, от {MoscowOffset.MinHours} до {MoscowOffset.MaxHours}. 0 — московское время.";

    [ObservableProperty]
    private string _breakfast = "08:00";

    [ObservableProperty]
    private string _lunch = "13:00";

    [ObservableProperty]
    private string _dinner = "19:00";

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
            Breakfast = profile.Meals.Breakfast.ToString("HH:mm");
            Lunch = profile.Meals.Lunch.ToString("HH:mm");
            Dinner = profile.Meals.Dinner.ToString("HH:mm");
            ConfirmationWindowMinutes = ((int)profile.ConfirmationWindow.TotalMinutes).ToString();
        }

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

    [RelayCommand]
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

            MoscowOffset offset = ParseOffset(MoscowOffsetHours);
            string timeZoneId = offset.ToTimeZoneId();

            await _updateProfile.ExecuteAsync(
                username: Username,
                timeZoneId: timeZoneId,
                meals: new MealWindows(breakfast, lunch, dinner),
                confirmationWindow: TimeSpan.FromMinutes(minutes),
                cancellationToken: cancellationToken);

            ResolvedTimeZoneId = timeZoneId;
            _feedback.Notify("Время приёма пищи сохранено");
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

    [RelayCommand]
    private async Task GenerateTelegramCodeAsync(CancellationToken cancellationToken)
    {
        await GenerateCodeAsync(MessengerChannelType.Telegram, cancellationToken);
    }

    [RelayCommand]
    private async Task GenerateDiscordCodeAsync(CancellationToken cancellationToken)
    {
        await GenerateCodeAsync(MessengerChannelType.Discord, cancellationToken);
    }

    private const string LinkCodeCharacters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private async Task GenerateCodeAsync(MessengerChannelType channel, CancellationToken cancellationToken)
    {
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

            // Именно LoadAsync, а не RefreshAsync: вложенный RunAsync упёрся бы в IsBusy
            // и список привязок остался бы старым.
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
