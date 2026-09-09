using FluentAssertions;
using Med.Presentation.Abstractions;
using Med.Presentation.Snackbar;
using Xunit;

namespace Med.Presentation.Tests.Snackbar;

public sealed class SnackbarServiceTests
{
    private readonly ImmediateUiDispatcher _ui = new();

    [Fact]
    public async Task ShowAsync_открывает_snackbar_и_закрывает_после_таймаута()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SnackbarViewModel vm = new();
        using SnackbarService service = new(vm, _ui);

        Task showTask = service.ShowAsync("Тестовое сообщение", "Заголовок", TimeSpan.FromMilliseconds(50), cancellationToken: ct);

        vm.IsOpen.Should().BeTrue();
        vm.Message.Should().Be("Тестовое сообщение");
        vm.Title.Should().Be("Заголовок");

        await showTask;

        vm.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Dismiss_мгновенно_закрывает_snackbar_и_предотвращает_повторное_закрытие()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SnackbarViewModel vm = new();
        using SnackbarService service = new(vm, _ui);

        _ = service.ShowAsync("Длинное сообщение", duration: TimeSpan.FromSeconds(5), cancellationToken: ct);

        vm.IsOpen.Should().BeTrue();

        service.Dismiss();

        vm.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void DismissCommand_на_ViewModel_закрывает_snackbar()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SnackbarViewModel vm = new();
        using SnackbarService service = new(vm, _ui);

        _ = service.ShowAsync("Сообщение для закрытия", duration: TimeSpan.FromSeconds(5), cancellationToken: ct);

        vm.IsOpen.Should().BeTrue();

        vm.DismissCommand.Execute(null);

        vm.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Pause_и_Resume_корректно_управляют_жизненным_циклом_таймера()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SnackbarViewModel vm = new();
        using SnackbarService service = new(vm, _ui);

        _ = service.ShowAsync("Сообщение с паузой", duration: TimeSpan.FromMilliseconds(100), cancellationToken: ct);

        vm.IsOpen.Should().BeTrue();

        // Имитируем наведение мыши (пауза)
        vm.PointerEnteredCommand.Execute(null);

        // Ждем дольше начального таймаута — сообщение должно оставаться открытым
        await Task.Delay(150, ct);
        vm.IsOpen.Should().BeTrue();

        // Убираем курсор (возобновление)
        vm.PointerExitedCommand.Execute(null);

        // Ждем завершения таймера
        await Task.Delay(600, ct);
        vm.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Новое_сообщение_отменяет_таймер_предыдущего_и_не_закрывает_новое_раньше_времени()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SnackbarViewModel vm = new();
        using SnackbarService service = new(vm, _ui);

        // Первое сообщение на 50 мс
        _ = service.ShowAsync("Первое", duration: TimeSpan.FromMilliseconds(50), cancellationToken: ct);

        await Task.Delay(10, ct);

        // Второе сообщение на 200 мс перебивает первое
        _ = service.ShowAsync("Второе", duration: TimeSpan.FromMilliseconds(200), cancellationToken: ct);

        vm.Message.Should().Be("Второе");
        vm.IsOpen.Should().BeTrue();

        // Ждем 70 мс (время, когда первое бы уже закрылось)
        await Task.Delay(70, ct);

        // Второе сообщение ДОЛЖНО оставаться открытым!
        vm.IsOpen.Should().BeTrue();
        vm.Message.Should().Be("Второе");

        // Ждем завершения второго сообщения
        await Task.Delay(200, ct);
        vm.IsOpen.Should().BeFalse();
    }
}
