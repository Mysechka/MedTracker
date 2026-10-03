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
    IRecipient<NavigateToSectionMessage>,
    IDisposable
{
    private readonly TodayViewModel _today;
    private readonly MedicationsViewModel _medications;
    private readonly CoursesViewModel? _courses;
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
        MedicalCardViewModel medicalCard,
        SettingsViewModel settings,
        AuthViewModel auth,
        AccountViewModel account,
        IAuthService authService,
        IUiDispatcher ui,
        SnackbarViewModel snackbar,
        UserFeedback feedback,
        IMessenger? messenger = null,
        CoursesViewModel? courses = null)
    {
        _today = today;
        _medications = medications;
        _medicalCard = medicalCard;
        _settings = settings;
        _auth = auth;
        _account = account;
        _courses = courses;
        _authService = authService;
        _ui = ui;
        Snackbar = snackbar;
        _feedback = feedback;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _current = _today;
        _activeNav = ShellNav.Today;
        _authService.AuthStateChanged += OnAuthStateChanged;
        _account.PropertyChanged += OnAccountPropertyChanged;
        _messenger.RegisterAll(this);

        if (_authService.CurrentSession is not null)
        {
            var session = _authService.CurrentSession;
            IsAuthenticated = true;
            IsLocalAccount = session.IsLocalOnly;
            AccountName = !string.IsNullOrWhiteSpace(session.Username)
                ? session.Username
                : (session.IsLocalOnly
                    ? "Локальный аккаунт"
                    : (!string.IsNullOrWhiteSpace(session.Email) ? session.Email.Split('@')[0] : "Аккаунт"));
            UpdateAvatarInitial();
            LoadAvatar(session.UserId);
        }

        PromptOnboardingIfNeeded();
    }

    [ObservableProperty]
    private ViewModelBase _current;

    [ObservableProperty]
    private ShellNav _activeNav;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private bool _isLocalAccount;

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

    public bool IsCoursesSelected => ActiveNav == ShellNav.Courses;

    public bool IsMedicalCardSelected => ActiveNav == ShellNav.MedicalCard;

    public bool IsSettingsSelected => ActiveNav == ShellNav.Settings;

    public bool IsAccountSelected => ActiveNav == ShellNav.Account;

    partial void OnAccountNameChanged(string value)
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

    partial void OnActiveNavChanged(ShellNav value)
    {
        OnPropertyChanged(nameof(IsTodaySelected));
        OnPropertyChanged(nameof(IsMedicationsSelected));
        OnPropertyChanged(nameof(IsCoursesSelected));
        OnPropertyChanged(nameof(IsMedicalCardSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
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
    private void GoCourses()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        ActiveNav = ShellNav.Courses;
        if (_courses is not null)
        {
            Show(_courses);
            RefreshIfAuthenticated(_courses.RefreshCommand);
        }
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
                IsLocalAccount = false;
                AccountName = string.Empty;
                AvatarPath = null;
                HasAvatar = false;
                AvatarInitial = "?";
                _feedback.ShowLoginRequired();
                ShowToday();
                PromptOnboardingIfNeeded();
                return;
            }

            IsAuthenticated = true;
            IsLocalAccount = session.IsLocalOnly;
            AccountName = !string.IsNullOrWhiteSpace(session.Username)
                ? session.Username
                : (session.IsLocalOnly
                    ? "Локальный аккаунт"
                    : (!string.IsNullOrWhiteSpace(session.Email) ? session.Email.Split('@')[0] : "Аккаунт"));
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
            }
        });
    }

    public void Receive(NavigateToSectionMessage message)
    {
        _ui.Post(() =>
        {
            switch (message.Section)
            {
                case ShellNav.Today:
                    GoToday();
                    break;
                case ShellNav.Medications:
                    GoMedications();
                    break;
                case ShellNav.Courses:
                    GoCourses();
                    break;
                case ShellNav.MedicalCard:
                    GoMedicalCard();
                    break;
                case ShellNav.Settings:
                    GoSettings();
                    break;
                case ShellNav.Auth:
                    GoAuth();
                    break;
                case ShellNav.Account:
                    GoAccount();
                    break;
            }
        });
    }

    private void OnAccountPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountViewModel.AvatarPath))
        {
            _ui.Post(() =>
            {
                AvatarPath = _account.AvatarPath;
            });
        }
        else if (e.PropertyName == nameof(AccountViewModel.Username))
        {
            _ui.Post(() =>
            {
                if (!string.IsNullOrWhiteSpace(_account.Username))
                {
                    AccountName = _account.Username;
                    UpdateAvatarInitial();
                }
            });
        }
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

    private void Show(ViewModelBase screen)
    {
        if (!IsAuthenticated && screen != _auth && screen != _today)
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

    private void PromptOnboardingIfNeeded()
    {
        if (!IsAuthenticated)
        {
            _feedback.ShowLoginRequired();
            ActiveNav = ShellNav.Auth;
            Current = _auth;
        }
    }

    public void Dispose()
    {
        _account.PropertyChanged -= OnAccountPropertyChanged;
        _authService.AuthStateChanged -= OnAuthStateChanged;
        _messenger.UnregisterAll(this);
    }
}
