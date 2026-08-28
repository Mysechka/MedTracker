using System.Collections.ObjectModel;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;

namespace Med.Presentation.Settings;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly IProfileRepository _profiles;
    private readonly IMessengerLinkRepository _links;
    private readonly IAuthService _auth;
    private readonly UpdateProfileUseCase _updateProfile;

    public SettingsViewModel(
        IProfileRepository profiles,
        IMessengerLinkRepository links,
        IAuthService auth,
        UpdateProfileUseCase updateProfile)
    {
        _profiles = profiles;
        _links = links;
        _auth = auth;
        _updateProfile = updateProfile;
    }

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
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
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
                }

                if (link.ChannelType == MessengerChannelType.Discord && link.LinkCode is not null)
                {
                    DiscordLinkCode = link.LinkCode;
                }
            }

            Message = MoscowOffsetHours.Length == 0
                ? $"Зона профиля «{ResolvedTimeZoneId}» вне схемы «Москва ± N»: задайте смещение и сохраните."
                : "Настройки загружены.";
        });
    }

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
            Message = $"Профиль сохранён. Часовой пояс: {offset} ({timeZoneId}).";
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

    private async Task GenerateCodeAsync(MessengerChannelType channel, CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            Guid userId = _auth.CurrentUserId
                ?? throw new InvalidOperationException("Нужна сессия.");

            string code = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
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
            }
            else
            {
                DiscordLinkCode = code;
            }

            await RefreshAsync(cancellationToken);
            Message = $"{channel}: отправьте код боту / выполните /link.";
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
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
