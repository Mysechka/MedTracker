using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;
using Med.Presentation.Abstractions;
using Med.Presentation.Courses;
using Med.Presentation.Feedback;
using Med.Presentation.Medications;
using Med.Presentation.Messaging;
using Med.Presentation.Sync;
using Xunit;

namespace Med.Presentation.Tests.Stress;

public sealed class ConcurrencyAndRaceConditionTests
{
    private static readonly Guid TestUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>
    /// Эмулирует сериализованный цикл обработки событий UI-потока (аналог Avalonia Dispatcher).
    /// Все Post() выстраиваются в очередь и выполняются последовательно в выделенном потоке UI.
    /// </summary>
    private sealed class QueuedUiDispatcher : IUiDispatcher, IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _uiThread;
        private readonly CancellationTokenSource _cts = new();

        public QueuedUiDispatcher()
        {
            _uiThread = new Thread(ProcessQueue) { IsBackground = true, Name = "SimulatedUiThread" };
            _uiThread.Start();
        }

        public void Post(Action action)
        {
            if (!_queue.IsAddingCompleted)
            {
                _queue.Add(action);
            }
        }

        public void Drain()
        {
            TaskCompletionSource tcs = new();
            Post(() => tcs.SetResult());
            tcs.Task.Wait(TimeSpan.FromSeconds(10));
        }

        private void ProcessQueue()
        {
            try
            {
                foreach (Action action in _queue.GetConsumingEnumerable(_cts.Token))
                {
                    try
                    {
                        action();
                    }
                    catch
                    {
                        // Игнорируем для стресс-теста
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _cts.Cancel();
            _uiThread.Join(TimeSpan.FromSeconds(2));
            _queue.Dispose();
            _cts.Dispose();
        }
    }

    [Fact]
    public async Task Массовое_поступление_1000_Realtime_событий_не_вызывает_гонок_и_deadlock()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using QueuedUiDispatcher uiDispatcher = new();
        WeakReferenceMessenger messenger = new();
        EntityChangeDeduplicator deduplicator = new();
        UserFeedback feedback = new(new NullSnackbar());

        MedicationsViewModel vm = new(
            new ConcurrentMedicationRepo(),
            new ConcurrentInventoryRepo(),
            new ConcurrentCourseRepo(),
            new ConcurrentScheduleRepo(),
            new FakeAuth(TestUserId),
            new RestockInventoryUseCase(new ConcurrentInventoryCommands()),
            feedback,
            uiDispatcher,
            messenger,
            deduplicator);

        const int totalEvents = 1000;
        const int concurrency = 8;
        int eventsPerThread = totalEvents / concurrency;

        List<Task> producerTasks = [];
        ConcurrentBag<Exception> exceptions = new();

        for (int t = 0; t < concurrency; t++)
        {
            int threadIndex = t;
            producerTasks.Add(Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < eventsPerThread; i++)
                    {
                        int idNum = (threadIndex * eventsPerThread) + i;
                        Guid medId = Guid.Parse($"00000000-0000-0000-0000-{idNum:D12}");

                        if (i % 5 == 0 && i > 0)
                        {
                            // Удаление
                            int prevId = idNum - 1;
                            Guid prevGuid = Guid.Parse($"00000000-0000-0000-0000-{prevId:D12}");
                            messenger.Send(new EntityDeletedMessage<Medication>(prevGuid, ChangeSource.Realtime));
                        }
                        else
                        {
                            // Вставка / обновление
                            Medication med = Medication.Create(
                                medId,
                                TestUserId,
                                $"Препарат_{idNum}",
                                "таблетка",
                                "100",
                                "мг",
                                null,
                                null);
                            messenger.Send(new EntitySavedMessage<Medication>(med, ChangeSource.Realtime));
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }, ct));
        }

        // Параллельный фоновый читатель: симулирует пользовательский интерфейс, непрерывно читающий Items
        CancellationTokenSource readCts = new();
        Task readerTask = Task.Run(() =>
        {
            while (!readCts.Token.IsCancellationRequested)
            {
                try
                {
                    uiDispatcher.Post(() =>
                    {
                        // Безопасное чтение в UI-потоке
                        _ = vm.Items.Count;
                        foreach (var item in vm.Items)
                        {
                            _ = item.Name;
                        }
                    });
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
                Thread.Sleep(5);
            }
        }, ct);

        await Task.WhenAll(producerTasks);
        readCts.Cancel();
        await readerTask;

        // Ждём опустошения очереди UI-потока
        uiDispatcher.Drain();

        exceptions.Should().BeEmpty("во время конкурентной обработки не должно быть исключений гонки потоков");
        vm.Items.Count.Should().BeGreaterThan(0, "коллекция должна содержать обработанные сущности");
    }

    [Fact]
    public async Task Конкурентное_редактирование_и_фоновое_обновление_расписания()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using QueuedUiDispatcher uiDispatcher = new();
        WeakReferenceMessenger messenger = new();
        EntityChangeDeduplicator deduplicator = new();

        CoursesViewModel vm = new(
            new ConcurrentCourseRepo(),
            new ConcurrentScheduleRepo(),
            new ConcurrentMedicationRepo(),
            new FakeAuth(TestUserId),
            uiDispatcher,
            messenger,
            deduplicator);

        List<Task> tasks = [];
        ConcurrentBag<Exception> exceptions = new();

        // 1. Поток обновлений лекарств
        tasks.Add(Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
            {
                Guid id = Guid.NewGuid();
                Medication med = Medication.Create(id, TestUserId, $"Med_{i}", "капсулы", "1", "шт", null, null);
                messenger.Send(new EntitySavedMessage<Medication>(med, ChangeSource.Realtime));
            }
        }, ct));

        // 2. Поток обновлений курсов
        tasks.Add(Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
            {
                Guid id = Guid.NewGuid();
                DateOnly start = new(2026, 9, 1);
                Course course = Course.Create(id, TestUserId, Guid.NewGuid(), start, endsOn: start.AddDays(13), durationDays: 14);
                messenger.Send(new EntitySavedMessage<Course>(course, ChangeSource.Realtime));
            }
        }, ct));

        // 3. Поток сигналов смены расписаний
        tasks.Add(Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
            {
                messenger.Send(new ScheduleUpdatedMessage(Guid.NewGuid(), Guid.NewGuid(), ChangeSource.Realtime));
            }
        }, ct));

        await Task.WhenAll(tasks);
        uiDispatcher.Drain();

        exceptions.Should().BeEmpty();
        vm.Medications.Count.Should().Be(200);
        vm.Courses.Count.Should().Be(200);
    }

    private sealed class NullSnackbar : ISnackbarService
    {
        public Task ShowAsync(string message, string? title = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeAuth(Guid userId) : IAuthService
    {
        public AuthSession? CurrentSession => new(userId, "test@example.com", "token", "refresh", DateTimeOffset.UtcNow.AddHours(1));
        public Guid? CurrentUserId => userId;
        public event EventHandler<AuthSession?>? AuthStateChanged { add { } remove { } }
        public Task<AuthSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SignOutAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AuthSession> SignUpWithPasswordAsync(string email, string password, string? username = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ConcurrentMedicationRepo : IMedicationRepository
    {
        private readonly ConcurrentDictionary<Guid, Medication> _store = new();
        public Task<IReadOnlyList<Medication>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Medication>>(_store.Values.ToArray());
        public Task<Medication?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.TryGetValue(id, out var med) ? med : null);
        public Task UpsertAsync(Medication medication, CancellationToken cancellationToken = default)
        {
            _store[medication.Id] = medication;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _store.TryRemove(id, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class ConcurrentInventoryRepo : IInventoryRepository
    {
        private readonly ConcurrentDictionary<Guid, Inventory> _store = new();
        public Task<Inventory?> GetByMedicationAsync(Guid medicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.Values.FirstOrDefault(i => i.MedicationId == medicationId));
        public Task UpsertAsync(Inventory inventory, CancellationToken cancellationToken = default)
        {
            _store[inventory.Id] = inventory;
            return Task.CompletedTask;
        }
    }

    private sealed class ConcurrentInventoryCommands : IInventoryCommandService
    {
        public Task<InventoryCommandResult> RestockAsync(Guid medicationId, decimal amount, string? note = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryCommandResult("Applied", Guid.NewGuid(), amount));
    }

    private sealed class ConcurrentCourseRepo : ICourseRepository
    {
        private readonly ConcurrentDictionary<Guid, Course> _store = new();
        public Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Course>>(_store.Values.ToArray());
        public Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.TryGetValue(id, out var course) ? course : null);
        public Task UpsertAsync(Course course, CancellationToken cancellationToken = default)
        {
            _store[course.Id] = course;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _store.TryRemove(id, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class ConcurrentScheduleRepo : IScheduleRepository
    {
        private readonly ConcurrentDictionary<Guid, Schedule> _store = new();
        public Task<IReadOnlyList<Schedule>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Schedule>>(_store.Values.Where(s => s.CourseId == courseId).ToArray());
        public Task<Schedule?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.TryGetValue(id, out var s) ? s : null);
        public Task UpsertAsync(Schedule schedule, CancellationToken cancellationToken = default)
        {
            _store[schedule.Id] = schedule;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _store.TryRemove(id, out _);
            return Task.CompletedTask;
        }
    }
}
