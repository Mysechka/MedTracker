using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Entities;
using Med.Domain.ValueObjects;
using Med.Presentation.Abstractions;
using Med.Presentation.Feedback;
using Med.Presentation.Messaging;

namespace Med.Presentation.Account;

public sealed partial class AccountViewModel : ViewModelBase
{
    private readonly IProfileRepository _profiles;
    private readonly IAuthService _auth;
    private readonly UpdateProfileUseCase _updateProfile;
    private readonly IFilePickerService _filePicker;
    private readonly IImageCropService _cropService;
    private readonly UserFeedback _feedback;
    private readonly IMessenger _messenger;
    private readonly IUiDispatcher _ui;

    public AccountViewModel(
        IProfileRepository profiles,
        IAuthService auth,
        UpdateProfileUseCase updateProfile,
        IFilePickerService filePicker,
        UserFeedback feedback,
        IImageCropService? cropService = null,
        IMessenger? messenger = null,
        IUiDispatcher? ui = null)
    {
        _profiles = profiles;
        _auth = auth;
        _updateProfile = updateProfile;
        _filePicker = filePicker;
        _feedback = feedback;
        _cropService = cropService ?? new NullImageCropService();
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _ui = ui ?? new ImmediateUiDispatcher();
        _auth.AuthStateChanged += (s, e) =>
        {
            _ui.Post(() =>
            {
                OnPropertyChanged(nameof(IsLocalOnly));
                if (e is not null)
                {
                    Email = e.Email ?? string.Empty;
                    _initialEmail = Email;
                    if (!string.IsNullOrWhiteSpace(e.Username))
                    {
                        Username = e.Username;
                        _initialUsername = Username;
                        UpdateAvatarInitial();
                    }
                }
                UpdateHasChanges();
            });
        };
    }

    private string _initialUsername = string.Empty;
    private string _initialEmail = string.Empty;

    public bool IsLocalOnly => _auth.IsLocalOnly;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private bool _hasChanges;

    [ObservableProperty]
    private bool _hasEmailError;

    [ObservableProperty]
    private string _linkEmail = string.Empty;

    [ObservableProperty]
    private string _linkPassword = string.Empty;

    [ObservableProperty]
    private bool _hasLinkEmailError;

    [ObservableProperty]
    private bool _hasLinkPasswordError;

    [ObservableProperty]
    private string _linkErrorMessage = string.Empty;

    [ObservableProperty]
    private string _telegramCode = string.Empty;

    [ObservableProperty]
    private string _discordCode = string.Empty;

    [ObservableProperty]
    private bool _isTelegramLinked;

    [ObservableProperty]
    private bool _isDiscordLinked;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    public string Codeword
    {
        get => NewPassword;
        set => NewPassword = value;
    }

    [ObservableProperty]
    private string? _avatarPath;

    [ObservableProperty]
    private string _avatarInitial = "?";

    [ObservableProperty]
    private bool _hasAvatar;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasUsernameError;

    [ObservableProperty]
    private bool _hasNewPasswordError;

    public bool HasCodewordError
    {
        get => HasNewPasswordError;
        set => HasNewPasswordError = value;
    }

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isCropping;

    [ObservableProperty]
    private string? _cropSourcePath;

    [ObservableProperty]
    private double _cropZoom = 1.0;

    [ObservableProperty]
    private double _cropPanX;

    [ObservableProperty]
    private double _cropPanY;

    partial void OnUsernameChanged(string value)
    {
        UpdateAvatarInitial();
        UpdateHasChanges();
    }

    partial void OnEmailChanged(string value)
    {
        UpdateHasChanges();
    }

    partial void OnNewPasswordChanged(string value)
    {
        UpdateHasChanges();
    }

    private void UpdateHasChanges()
    {
        bool usernameChanged = !string.Equals(Username?.Trim(), _initialUsername?.Trim(), StringComparison.Ordinal);
        bool emailChanged = !IsLocalOnly && !string.Equals(Email?.Trim(), _initialEmail?.Trim(), StringComparison.OrdinalIgnoreCase);
        bool passwordChanged = !string.IsNullOrEmpty(NewPassword);

        HasChanges = usernameChanged || emailChanged || passwordChanged;
    }

    partial void OnAvatarPathChanged(string? value)
    {
        string? cleanPath = value;
        if (!string.IsNullOrEmpty(cleanPath))
        {
            int q = cleanPath.IndexOf('?');
            if (q >= 0)
            {
                cleanPath = cleanPath[..q];
            }
        }

        HasAvatar = !string.IsNullOrEmpty(cleanPath) && File.Exists(cleanPath);
    }

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            OnPropertyChanged(nameof(IsLocalOnly));
            Profile? profile = await _profiles.GetCurrentAsync(cancellationToken);
            if (profile is not null)
            {
                Username = profile.Username;
                UpdateAvatarInitial();
                LoadAvatar(profile.UserId);
            }

            Email = _auth.CurrentSession?.Email ?? string.Empty;
            _initialUsername = Username;
            _initialEmail = Email;
            NewPassword = string.Empty;
            UpdateHasChanges();
        });
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        HasUsernameError = string.IsNullOrWhiteSpace(Username);
        HasEmailError = !IsLocalOnly && (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@') || !Email.Contains('.'));
        HasNewPasswordError = !string.IsNullOrEmpty(NewPassword) && NewPassword.Length < 6;

        if (HasUsernameError)
        {
            Message = "Имя пользователя не может быть пустым.";
            return;
        }

        if (HasEmailError)
        {
            Message = "Введите корректный email (должен содержать @ и точку).";
            return;
        }

        if (HasNewPasswordError)
        {
            Message = "Пароль должен содержать не менее 6 символов.";
            return;
        }

        await RunAsync(async () =>
        {
            Profile? current = await _profiles.GetCurrentAsync(cancellationToken);
            if (current is null)
            {
                throw new InvalidOperationException("Профиль не найден.");
            }

            bool usernameChanged = !string.Equals(Username.Trim(), _initialUsername?.Trim(), StringComparison.Ordinal);
            bool emailChanged = !IsLocalOnly && !string.Equals(Email.Trim(), _initialEmail?.Trim(), StringComparison.OrdinalIgnoreCase);
            bool passwordChanged = !string.IsNullOrEmpty(NewPassword);

            if (usernameChanged)
            {
                Profile updated = await _updateProfile.ExecuteAsync(
                    username: Username.Trim(),
                    timeZoneId: current.TimeZoneId,
                    meals: current.Meals,
                    confirmationWindow: current.ConfirmationWindow,
                    cancellationToken: cancellationToken);

                _auth.UpdateSessionUsername(updated.Username);
                _messenger.Send(new ProfileUpdatedMessage(updated, AvatarPath));
            }

            if (emailChanged)
            {
                await _auth.UpdateEmailAsync(Email.Trim(), cancellationToken);
            }

            if (passwordChanged)
            {
                await _auth.UpdatePasswordAsync(NewPassword, cancellationToken);
                NewPassword = string.Empty;
            }

            _initialUsername = Username.Trim();
            _initialEmail = Email.Trim();
            UpdateHasChanges();

            _feedback.Notify("Данные профиля обновлены");
            Message = "Изменения успешно сохранены.";
        });
    }

    [RelayCommand]
    private async Task LinkAccountAsync(CancellationToken cancellationToken)
    {
        HasLinkEmailError = string.IsNullOrWhiteSpace(LinkEmail) || !LinkEmail.Contains('@') || !LinkEmail.Contains('.');
        HasLinkPasswordError = string.IsNullOrWhiteSpace(LinkPassword) || LinkPassword.Length < 6;

        if (HasLinkEmailError)
        {
            LinkErrorMessage = "Введите корректный email (должен содержать @ и точку).";
            return;
        }

        if (HasLinkPasswordError)
        {
            LinkErrorMessage = "Пароль должен содержать не менее 6 символов.";
            return;
        }

        LinkErrorMessage = string.Empty;

        await RunAsync(async () =>
        {
            await _auth.MigrateToCloudAsync(LinkEmail.Trim(), LinkPassword, cancellationToken);
            OnPropertyChanged(nameof(IsLocalOnly));
            Email = _auth.CurrentSession?.Email ?? LinkEmail.Trim();
            _initialEmail = Email;
            _initialUsername = Username;
            NewPassword = string.Empty;
            LinkEmail = string.Empty;
            LinkPassword = string.Empty;
            UpdateHasChanges();
            if (_auth.CurrentUserId is { } newUserId)
            {
                LoadAvatar(newUserId);
                Profile? cloudProfile = await _profiles.GetCurrentAsync(cancellationToken);
                if (cloudProfile is not null)
                {
                    _messenger.Send(new ProfileUpdatedMessage(cloudProfile, AvatarPath));
                }
            }
            _feedback.Notify("Аккаунт успешно привязан к облаку!");
            Message = "Аккаунт успешно привязан к облаку!";
        });
    }

    [RelayCommand]
    private void LinkTelegram()
    {
        if (string.IsNullOrWhiteSpace(TelegramCode))
        {
            _feedback.Notify("Введите код привязки Telegram.");
            return;
        }

        IsTelegramLinked = true;
        _feedback.Notify("Telegram успешно привязан!");
    }

    [RelayCommand]
    private void LinkDiscord()
    {
        if (string.IsNullOrWhiteSpace(DiscordCode))
        {
            _feedback.Notify("Введите код привязки Discord.");
            return;
        }

        IsDiscordLinked = true;
        _feedback.Notify("Discord успешно привязан!");
    }

    [RelayCommand]
    private async Task ChangeAvatarAsync(CancellationToken cancellationToken)
    {
        string? selectedFile = await _filePicker.PickImageFileAsync(cancellationToken);
        if (string.IsNullOrEmpty(selectedFile) || !File.Exists(selectedFile))
        {
            return;
        }

        CropSourcePath = selectedFile;
        CropZoom = 1.0;
        CropPanX = 0;
        CropPanY = 0;
        IsCropping = true;
    }

    [RelayCommand]
    private async Task ApplyCropAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CropSourcePath) || !File.Exists(CropSourcePath))
        {
            IsCropping = false;
            return;
        }

        await RunAsync(async () =>
        {
            Guid userId = _auth.CurrentUserId ?? Guid.NewGuid();
            string savedPath = await _cropService.CropAndSaveAvatarAsync(
                CropSourcePath,
                userId,
                CropZoom,
                CropPanX,
                CropPanY,
                targetSize: 256,
                cancellationToken: cancellationToken);

            string versionedPath = $"{savedPath}?v={DateTime.UtcNow.Ticks}";
            AvatarPath = versionedPath;
            HasAvatar = true;
            IsCropping = false;
            CropSourcePath = null;

            Profile? current = null;
            try
            {
                current = await _profiles.GetCurrentAsync(CancellationToken.None);
            }
            catch
            {
                // Fallback snapshot
            }

            current ??= Profile.Create(
                userId,
                !string.IsNullOrWhiteSpace(Username) ? Username.Trim() : "Пользователь",
                "Europe/Moscow",
                new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)));

            _messenger.Send(new ProfileUpdatedMessage(current, versionedPath));
            _feedback.Notify("Аватар обновлен");
        });
    }

    [RelayCommand]
    private void CancelCrop()
    {
        IsCropping = false;
        CropSourcePath = null;
        CropZoom = 1.0;
        CropPanX = 0;
        CropPanY = 0;
    }

    [RelayCommand]
    private void ResetCropPosition()
    {
        CropZoom = 1.0;
        CropPanX = 0;
        CropPanY = 0;
    }

    public void PanCrop(double deltaX, double deltaY)
    {
        CropPanX += deltaX;
        CropPanY += deltaY;
    }

    [RelayCommand]
    private async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            await _auth.SignOutAsync(cancellationToken);
            _feedback.Notify("Выход выполнен.");
        });
    }

    private void UpdateAvatarInitial()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            AvatarInitial = "?";
            return;
        }

        AvatarInitial = Username.Trim()[..1].ToUpperInvariant();
    }

    private void LoadAvatar(Guid userId)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string avatarDir = Path.Combine(appData, "MedTracker", "avatars");
        if (!Directory.Exists(avatarDir))
        {
            AvatarPath = null;
            HasAvatar = false;
            return;
        }

        string[] possible = [".png", ".jpg", ".jpeg", ".webp"];
        foreach (string ext in possible)
        {
            string path = Path.Combine(avatarDir, $"{userId}{ext}");
            if (File.Exists(path))
            {
                AvatarPath = $"{path}?v={File.GetLastWriteTimeUtc(path).Ticks}";
                HasAvatar = true;
                return;
            }
        }

        // Если для данного userId аватар не найден, берём последний сохранённый аватар на устройстве
        try
        {
            var files = Directory.GetFiles(avatarDir)
                .Where(f => possible.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
            if (files.Count > 0)
            {
                string best = files[0];
                string dest = Path.Combine(avatarDir, $"{userId}{Path.GetExtension(best)}");
                if (!string.Equals(best, dest, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(best, dest, overwrite: true);
                }
                AvatarPath = $"{dest}?v={File.GetLastWriteTimeUtc(dest).Ticks}";
                HasAvatar = true;
                return;
            }
        }
        catch { }

        AvatarPath = null;
        HasAvatar = false;
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
            HasUsernameError = false;
            HasEmailError = false;
            HasNewPasswordError = false;
            Message = string.Empty;
            await action();
        }
        catch (Exception ex)
        {
            string msg = ex.Message;
            if (msg.Contains("user_already_exists") || msg.Contains("User already registered"))
            {
                msg = "Пользователь с таким email уже зарегистрирован.";
            }
            else if (msg.Contains("invalid_credentials") || msg.Contains("Invalid login credentials") || msg.Contains("invalid_grant"))
            {
                msg = "Неверный пароль для существующего аккаунта.";
            }

            Message = msg;
            _feedback.Notify(msg);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
