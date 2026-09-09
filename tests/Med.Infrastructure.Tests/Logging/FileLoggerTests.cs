using FluentAssertions;
using Med.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Med.Infrastructure.Tests.Logging;

public sealed class FileLoggerTests : IDisposable
{
    private readonly string _testDir;

    public FileLoggerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "MedTrackerLogTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task FileLogger_пишет_структурированные_логи_и_исключения_в_файл()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using (FileLoggerProvider provider = new(_testDir, LogLevel.Information))
        {
            ILogger logger = provider.CreateLogger("TestCategory");

            logger.LogInformation("Информационное сообщение #{Id}", 42);

            try
            {
                throw new InvalidOperationException("Тестовая ошибка со стектрейсом");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Произошел сбой операции");
            }

            // Даем фоновому писателю сбросить очередь
            await Task.Delay(300, ct);
        }

        string[] logFiles = Directory.GetFiles(_testDir, "medtracker-*.log");
        logFiles.Should().NotBeEmpty();

        string content = await File.ReadAllTextAsync(logFiles[0], ct);
        content.Should().Contain("[INFO ] [TestCategory]");
        content.Should().Contain("Информационное сообщение #42");
        content.Should().Contain("[ERROR] [TestCategory]");
        content.Should().Contain("Произошел сбой операции");
        content.Should().Contain("InvalidOperationException: Тестовая ошибка со стектрейсом");
    }

    [Fact]
    public async Task FileLogger_фильтрует_сообщения_ниже_минимального_уровня()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using (FileLoggerProvider provider = new(_testDir, LogLevel.Warning))
        {
            ILogger logger = provider.CreateLogger("FilterCategory");

            logger.LogDebug("Отладочное сообщение — не должно быть записано");
            logger.LogInformation("Инфо сообщение — не должно быть записано");
            logger.LogWarning("Предупреждение — должно быть записано");

            await Task.Delay(300, ct);
        }

        string[] logFiles = Directory.GetFiles(_testDir, "medtracker-*.log");
        logFiles.Should().NotBeEmpty();

        string content = await File.ReadAllTextAsync(logFiles[0], ct);
        content.Should().NotContain("Отладочное сообщение");
        content.Should().NotContain("Инфо сообщение");
        content.Should().Contain("[WARN ] [FilterCategory]");
        content.Should().Contain("Предупреждение — должно быть записано");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
    }
}
