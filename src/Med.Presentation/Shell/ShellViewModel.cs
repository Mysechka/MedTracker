using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Med.Domain.Abstractions;

namespace Med.Presentation.Shell;

/// <summary>
/// Каркас навигации стадии 0: проверяет, что цепочка
/// Domain → Application → Infrastructure → Presentation → Ui собрана через DI.
/// Экраны «Сегодня», «Лекарства» и остальные появятся на стадиях 5–6.
/// </summary>
public sealed partial class ShellViewModel(ISystemClock clock) : ViewModelBase
{
    [ObservableProperty]
    private string _clockReading = string.Empty;

    public string Title => "MedTracker — технический каркас";

    [RelayCommand]
    private void ReadClock() =>
        ClockReading = clock.UtcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
