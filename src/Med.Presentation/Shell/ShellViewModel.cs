using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Med.Application.Abstractions;
using Med.Presentation.Abstractions;
using Med.Presentation.Courses;
using Med.Presentation.Diagnostics;
using Med.Presentation.MedicalCard;
using Med.Presentation.Medications;
using Med.Presentation.Settings;
using Med.Presentation.Today;

namespace Med.Presentation.Shell;

/// <summary>Каркас навигации: switch по типу текущего ViewModel.</summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly TodayViewModel _today;
    private readonly MedicationsViewModel _medications;
    private readonly CoursesViewModel _courses;
    private readonly MedicalCardViewModel _medicalCard;
    private readonly SettingsViewModel _settings;
    private readonly DiagnosticsViewModel _diagnostics;
    private readonly AuthViewModel _auth;
    private readonly IAuthService _authService;
    private readonly IUiDispatcher _ui;

    public ShellViewModel(
        TodayViewModel today,
        MedicationsViewModel medications,
        CoursesViewModel courses,
        MedicalCardViewModel medicalCard,
        SettingsViewModel settings,
        DiagnosticsViewModel diagnostics,
        AuthViewModel auth,
        IAuthService authService,
        IUiDispatcher ui)
    {
        _today = today;
        _medications = medications;
        _courses = courses;
        _medicalCard = medicalCard;
        _settings = settings;
        _diagnostics = diagnostics;
        _auth = auth;
        _authService = authService;
        _ui = ui;
        // Не читаем CurrentSession в ctor: AuthService может инициализировать
        // Supabase-клиент (Realtime) — это недопустимо в unit-composition тестах
        // и на старте UI до конфигурации. Стартуем с Auth; сессия переключит экран.
        _current = auth;
        _authService.AuthStateChanged += OnAuthStateChanged;
    }

    [ObservableProperty]
    private ViewModelBase _current;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string Title => "MedTracker — технический каркас";

    [RelayCommand]
    private void GoAuth() => Current = _auth;

    [RelayCommand]
    private void GoToday() => Current = _today;

    [RelayCommand]
    private void GoMedications() => Current = _medications;

    [RelayCommand]
    private void GoCourses() => Current = _courses;

    [RelayCommand]
    private void GoMedicalCard() => Current = _medicalCard;

    [RelayCommand]
    private void GoSettings() => Current = _settings;

    [RelayCommand]
    private void GoDiagnostics() => Current = _diagnostics;

    private void OnAuthStateChanged(object? sender, AuthSession? session)
    {
        // Событие приходит из потока Supabase-клиента, а смена Current перестраивает визуальное дерево.
        _ui.Post(() =>
        {
            Current = session is null ? _auth : _today;
            StatusMessage = session is null ? "Выход" : $"Сессия: {session.Email}";
        });
    }
}
