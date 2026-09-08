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
    }

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _codeword = string.Empty;

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
    private bool _hasCodewordError;

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
            Profile? profile = await _profiles.GetCurrentAsync(cancellationToken);
            if (profile is not null)
            {
                Username = profile.Username;
                UpdateAvatarInitial();
                LoadAvatar(profile.UserId);
            }
        });
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        HasUsernameError = string.IsNullOrWhiteSpace(Username);
        HasCodewordError = !string.IsNullOrEmpty(Codeword) && Codeword.Length < 6;

        if (HasUsernameError || HasCodewordError)
        {
            return;
        }

        await RunAsync(async () =>
        {
            Profile? current = await _profiles.GetCurrentAsync(cancellationToken);
            if (current is null)
            {
                throw new InvalidOperationException("Профиль не найден.");
            }

            Profile updated = await _updateProfile.ExecuteAsync(
                username: Username.Trim(),
                timeZoneId: current.TimeZoneId,
                meals: current.Meals,
                confirmationWindow: current.ConfirmationWindow,
                cancellationToken: cancellationToken);

            if (!string.IsNullOrEmpty(Codeword))
            {
                await _auth.UpdatePasswordAsync(Codeword, cancellationToken);
                Codeword = string.Empty;
            }

            _messenger.Send(new ProfileUpdatedMessage(updated, AvatarPath));
            _feedback.Notify("Данные профиля обновлены");
            Message = "Изменения успешно сохранены.";
        });
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
            HasCodewordError = false;
            Message = string.Empty;
            await action();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            _feedback.Notify(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
