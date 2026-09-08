using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Entities;
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
    private readonly UserFeedback _feedback;
    private readonly IMessenger _messenger;
    private readonly IUiDispatcher _ui;

    public AccountViewModel(
        IProfileRepository profiles,
        IAuthService auth,
        UpdateProfileUseCase updateProfile,
        IFilePickerService filePicker,
        UserFeedback feedback,
        IMessenger? messenger = null,
        IUiDispatcher? ui = null)
    {
        _profiles = profiles;
        _auth = auth;
        _updateProfile = updateProfile;
        _filePicker = filePicker;
        _feedback = feedback;
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

    partial void OnUsernameChanged(string value)
    {
        UpdateAvatarInitial();
    }

    partial void OnAvatarPathChanged(string? value)
    {
        HasAvatar = !string.IsNullOrEmpty(value) && File.Exists(value);
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

        await RunAsync(async () =>
        {
            Guid userId = _auth.CurrentUserId ?? Guid.NewGuid();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string avatarDir = Path.Combine(appData, "MedTracker", "avatars");
            Directory.CreateDirectory(avatarDir);

            string ext = Path.GetExtension(selectedFile);
            string destination = Path.Combine(avatarDir, $"{userId}{ext}");
            File.Copy(selectedFile, destination, overwrite: true);

            AvatarPath = destination;
            HasAvatar = true;

            Profile? current = await _profiles.GetCurrentAsync(cancellationToken);
            if (current is not null)
            {
                _messenger.Send(new ProfileUpdatedMessage(current, AvatarPath));
            }

            _feedback.Notify("Аватар обновлен");
            await Task.CompletedTask;
        });
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
                AvatarPath = path;
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
