using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Windows.Input;
using Med.Application.Abstractions;
using Med.Presentation.Abstractions;
using Med.Presentation.Account;
using Med.Presentation.Courses;
using Med.Presentation.Feedback;
using Med.Presentation.MedicalCard;
using Med.Presentation.Medications;
using Med.Presentation.Messaging;
using Med.Presentation.Settings;
using Med.Presentation.Snackbar;
using Med.Presentation.Today;

namespace Med.Presentation.Shell;

/// <summary>Каркас навигации: switch по типу текущего ViewModel.</summary>
public sealed partial class ShellViewModel : ViewModelBase,
    IRecipient<ProfileUpdatedMessage>,
    IDisposable
{
    private readonly TodayViewModel _today;
    private readonly MedicationsViewModel _medications;
    private readonly CoursesViewModel _courses;
    private readonly MedicalCardViewModel _medicalCard;
    private readonly SettingsViewModel _settings;
    private readonly AuthViewModel _auth;
    private readonly AccountViewModel _account;
    private readonly IAuthService _authService;
    private readonly IUiDispatcher _ui;
    private readonly UserFeedback _feedback;
    private readonly IMessenger _messenger;

    public ShellViewModel(
        TodayViewModel today,
        MedicationsViewModel medications,
        CoursesViewModel courses,
        MedicalCardViewModel medicalCard,
        SettingsViewModel settings,
        AuthViewModel auth,
        AccountViewModel account,
        IAuthService authService,
        IUiDispatcher ui,
        SnackbarViewModel snackbar,
        UserFeedback feedback,
        IMessenger? messenger = null)
    {
        _today = today;
        _medications = medications;
        _courses = courses;
        _medicalCard = medicalCard;
        _settings = settings;
        _auth = auth;
        _account = account;
        _authService = authService;
        _ui = ui;
        Snackbar = snackbar;
        _feedback = feedback;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _current = _today;
        _activeNav = ShellNav.Today;
        _authService.AuthStateChanged += OnAuthStateChanged;
        _messenger.RegisterAll(this);
        PromptLoginIfNeeded();
    }

    [ObservableProperty]
    private ViewModelBase _current;

    [ObservableProperty]
    private ShellNav _activeNav;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private string _accountName = string.Empty;

    [ObservableProperty]
    private string? _avatarPath;

    [ObservableProperty]
    private string _avatarInitial = "?";

    [ObservableProperty]
    private bool _hasAvatar;

    public SnackbarViewModel Snackbar { get; }

    public bool IsTodaySelected => ActiveNav == ShellNav.Today;

    public bool IsMedicationsSelected => ActiveNav == ShellNav.Medications;

    public bool IsMedicalCardSelected => ActiveNav == ShellNav.MedicalCard;

    public bool IsAccountSelected => ActiveNav == ShellNav.Account;

    partial void OnAccountNameChanged(string value)
    {
        UpdateAvatarInitial();
    }

    partial void OnAvatarPathChanged(string? value)
    {
        HasAvatar = !string.IsNullOrEmpty(value) && File.Exists(value);
    }

    partial void OnActiveNavChanged(ShellNav value)
    {
        OnPropertyChanged(nameof(IsTodaySelected));
        OnPropertyChanged(nameof(IsMedicationsSelected));
        OnPropertyChanged(nameof(IsMedicalCardSelected));
        OnPropertyChanged(nameof(IsAccountSelected));
    }

    [RelayCommand]
    private void GoAccount()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        ActiveNav = ShellNav.Account;
        Show(_account);
        RefreshIfAuthenticated(_account.RefreshCommand);
    }

    [RelayCommand]
    private void GoAuth()
    {
        ActiveNav = ShellNav.Auth;
        Show(_auth);
    }

    [RelayCommand]
    private void GoToday() => ShowToday();

    [RelayCommand]
    private void GoMedications()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        ActiveNav = ShellNav.Medications;
        Show(_medications);
        RefreshIfAuthenticated(_medications.RefreshCommand);
    }

    [RelayCommand]
    private void GoMedicalCard()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        ActiveNav = ShellNav.MedicalCard;
        Show(_medicalCard);
    }

    [RelayCommand]
    private void GoSettings()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        ActiveNav = ShellNav.Settings;
        Show(_settings);
        RefreshIfAuthenticated(_settings.RefreshCommand);
    }

    private bool EnsureAuthenticated()
    {
        if (IsAuthenticated)
        {
            return true;
        }

        _feedback.ShowLoginRequired();
        GoAuth();
        return false;
    }

    private void OnAuthStateChanged(object? sender, AuthSession? session)
    {
        _ui.Post(() =>
        {
            if (session is null)
            {
                IsAuthenticated = false;
                AccountName = string.Empty;
                AvatarPath = null;
                HasAvatar = false;
                AvatarInitial = "?";
                _feedback.ShowLoginRequired();
                ShowToday();
                return;
            }

            IsAuthenticated = true;
            AccountName = string.IsNullOrWhiteSpace(session.Email) ? "ИмяАккаунта" : session.Email.Split('@')[0];
            UpdateAvatarInitial();
            LoadAvatar(session.UserId);
            ShowToday();
        });
    }

    public void Receive(ProfileUpdatedMessage message)
    {
        _ui.Post(() =>
        {
            AccountName = message.Value.Profile.Username;
            UpdateAvatarInitial();
            if (message.Value.AvatarPath is not null)
            {
                AvatarPath = message.Value.AvatarPath;
                HasAvatar = File.Exists(AvatarPath);
            }
        });
    }

    private void UpdateAvatarInitial()
    {
        if (string.IsNullOrWhiteSpace(AccountName))
        {
            AvatarInitial = "?";
            return;
        }

        AvatarInitial = AccountName.Trim()[..1].ToUpperInvariant();
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

    private void Show(ViewModelBase screen)
    {
        // Без аккаунта доступ разрешен только к стартовой странице (Today) и странице авторизации/верификации (Auth)
        if (!IsAuthenticated && screen != _today && screen != _auth)
        {
            _feedback.ShowLoginRequired();
            ActiveNav = ShellNav.Auth;
            Current = _auth;
            return;
        }

        Current = screen;
    }

    private void ShowToday()
    {
        ActiveNav = ShellNav.Today;
        Show(_today);
        RefreshIfAuthenticated(_today.RefreshCommand);
        PromptLoginIfNeeded();
    }

    private void RefreshIfAuthenticated(ICommand command)
    {
        if (!IsAuthenticated || !command.CanExecute(null))
        {
            return;
        }

        if (command is IAsyncRelayCommand asyncCommand)
        {
            _ = asyncCommand.ExecuteAsync(null);
            return;
        }

        command.Execute(null);
    }

    private void PromptLoginIfNeeded()
    {
        if (!IsAuthenticated)
        {
            _feedback.ShowLoginRequired();
        }
    }

    public void Dispose()
    {
        _authService.AuthStateChanged -= OnAuthStateChanged;
        _messenger.UnregisterAll(this);
    }
}
