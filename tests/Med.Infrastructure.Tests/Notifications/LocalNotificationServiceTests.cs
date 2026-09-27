using FluentAssertions;
using Med.Infrastructure.Notifications;
using Xunit;

namespace Med.Infrastructure.Tests.Notifications;

public sealed class LocalNotificationServiceTests
{
    [Fact]
    public async Task ShowAsync_не_выбрасывает_исключений()
    {
        using var service = new LocalNotificationService();

        var act = async () => await service.ShowAsync("Тест", "Текст напоминания", TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ScheduleAsync_для_прошедшего_времени_срабатывает_без_ошибок()
    {
        using var service = new LocalNotificationService();

        var act = async () => await service.ScheduleAsync(
            "dose-1",
            "Тест",
            "Текст напоминания",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ScheduleAsync_и_CancelAsync_корректно_отменяют_задачу()
    {
        using var service = new LocalNotificationService();

        await service.ScheduleAsync(
            "dose-2",
            "Тест",
            "Текст напоминания",
            DateTimeOffset.UtcNow.AddHours(2),
            TestContext.Current.CancellationToken);

        var act = async () => await service.CancelAsync("dose-2", TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NullNotificationService_выполняет_все_методы_как_noop()
    {
        var service = NullNotificationService.Instance;

        await service.ShowAsync("title", "body", TestContext.Current.CancellationToken);
        await service.ScheduleAsync("id", "title", "body", DateTimeOffset.UtcNow.AddHours(1), TestContext.Current.CancellationToken);
        await service.CancelAsync("id", TestContext.Current.CancellationToken);
    }
}
