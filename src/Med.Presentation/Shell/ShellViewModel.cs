using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Med.Application.Abstractions;
using Med.Presentation.Abstractions;
using Med.Presentation.Courses;
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
    private readonly AuthViewModel _auth;
    private readonly IAuthService _authService;
    private readonly IUiDispatcher _ui;

    public ShellViewModel(
        TodayViewModel today,
        MedicationsViewModel medications,
        CoursesViewModel courses,
        MedicalCardViewModel medicalCard,
        SettingsViewModel settings,
        AuthViewModel auth,
        IAuthService authService,
        IUiDispatcher ui)
    {
        _today = today;
        _medications = medications;
        _courses = courses;
        _medicalCard = medicalCard;
        _settings = settings;
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

    /// <summary>Заголовок верхней панели — название открытого экрана.</summary>
    [ObservableProperty]
    private string _screenTitle = "Вход";

    [RelayCommand]
    private void GoAuth() => Show(_auth, "Вход");

    [RelayCommand]
    private void GoToday() => ShowToday();

    [RelayCommand]
    private void GoMedications() => Show(_medications, "Лекарства");

    [RelayCommand]
    private void GoCourses() => Show(_courses, "Курсы и расписания");

    [RelayCommand]
    private void GoMedicalCard() => Show(_medicalCard, "Медкарта");

    [RelayCommand]
    private void GoSettings() => Show(_settings, "Настройки");

    private void OnAuthStateChanged(object? sender, AuthSession? session)
    {
        // Событие приходит из потока Supabase-клиента, а смена Current перестраивает визуальное дерево.
        _ui.Post(() =>
        {
            if (session is null)
            {
                Show(_auth, "Вход");
                StatusMessage = "Вы вышли из аккаунта.";
                return;
            }

            StatusMessage = session.Email;
            ShowToday();
        });
    }

    private void Show(ViewModelBase screen, string title)
    {
        Current = screen;
        ScreenTitle = title;
    }

    /// <summary>Экран дня сам данные не тянет: загрузку запускает переход на него.</summary>
    private void ShowToday()
    {
        Show(_today, "Сегодня");
        if (_today.RefreshCommand.CanExecute(null))
        {
            _today.RefreshCommand.Execute(null);
        }
    }
}
