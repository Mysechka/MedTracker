using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using Med.Application.Abstractions;
using Med.Presentation.Abstractions;
using Med.Presentation.Courses;
using Med.Presentation.Feedback;
using Med.Presentation.MedicalCard;
using Med.Presentation.Medications;
using Med.Presentation.Settings;
using Med.Presentation.Snackbar;
using Med.Presentation.Today;

namespace Med.Presentation.Shell;

/// <summary>Каркас навигации: switch по типу текущего ViewModel.</summary>
public sealed partial class ShellViewModel : ViewModelBase, IDisposable
{
    private readonly TodayViewModel _today;
    private readonly MedicationsViewModel _medications;
    private readonly CoursesViewModel _courses;
    private readonly MedicalCardViewModel _medicalCard;
    private readonly SettingsViewModel _settings;
    private readonly AuthViewModel _auth;
    private readonly IAuthService _authService;
    private readonly IUiDispatcher _ui;
    private readonly UserFeedback _feedback;

    public ShellViewModel(
        TodayViewModel today,
        MedicationsViewModel medications,
        CoursesViewModel courses,
        MedicalCardViewModel medicalCard,
        SettingsViewModel settings,
        AuthViewModel auth,
        IAuthService authService,
        IUiDispatcher ui,
        SnackbarViewModel snackbar,
        UserFeedback feedback)
    {
        _today = today;
        _medications = medications;
        _courses = courses;
        _medicalCard = medicalCard;
        _settings = settings;
        _auth = auth;
        _authService = authService;
        _ui = ui;
        Snackbar = snackbar;
        _feedback = feedback;
        _current = _today;
        _activeNav = ShellNav.Today;
        _authService.AuthStateChanged += OnAuthStateChanged;
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

    public SnackbarViewModel Snackbar { get; }

    public bool IsTodaySelected => ActiveNav == ShellNav.Today;

    public bool IsMedicationsSelected => ActiveNav == ShellNav.Medications;

    public bool IsMedicalCardSelected => ActiveNav == ShellNav.MedicalCard;

    partial void OnActiveNavChanged(ShellNav value)
    {
        OnPropertyChanged(nameof(IsTodaySelected));
        OnPropertyChanged(nameof(IsMedicationsSelected));
        OnPropertyChanged(nameof(IsMedicalCardSelected));
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
        ActiveNav = ShellNav.Medications;
        Show(_medications);
        RefreshIfAuthenticated(_medications.RefreshCommand);
        PromptLoginIfNeeded();
    }

    [RelayCommand]
    private void GoMedicalCard()
    {
        ActiveNav = ShellNav.MedicalCard;
        Show(_medicalCard);
        PromptLoginIfNeeded();
    }

    [RelayCommand]
    private void GoSettings()
    {
        ActiveNav = ShellNav.Settings;
        Show(_settings);
        RefreshIfAuthenticated(_settings.RefreshCommand);
    }

    private void OnAuthStateChanged(object? sender, AuthSession? session)
    {
        _ui.Post(() =>
        {
            if (session is null)
            {
                IsAuthenticated = false;
                AccountName = string.Empty;
                _feedback.ShowLoginRequired();
                return;
            }

            IsAuthenticated = true;
            AccountName = string.IsNullOrWhiteSpace(session.Email) ? "ИмяАккаунта" : session.Email;
            ShowToday();
        });
    }

    private void Show(ViewModelBase screen)
    {
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
    }
}
