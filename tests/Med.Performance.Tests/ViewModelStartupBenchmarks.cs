using BenchmarkDotNet.Attributes;
using Med.Application.Abstractions;
using Med.Application.DependencyInjection;
using Med.Application.UseCases;
using Med.Domain.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;
using Med.Infrastructure.Notifications;
using Med.Presentation.Abstractions;
using Med.Presentation.Courses;
using Med.Presentation.DependencyInjection;
using Med.Presentation.Feedback;
using Med.Presentation.Medications;
using Med.Presentation.Shell;
using Med.Presentation.Today;
using Microsoft.Extensions.DependencyInjection;

namespace Med.Performance.Tests;

[MemoryDiagnoser]
public class ViewModelStartupBenchmarks
{
    private ServiceProvider _serviceProvider = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMedApplication();
        services.AddMedPresentation();

        // Register lightweight fakes for abstractions usually provided by infrastructure
        services.AddSingleton<ISystemClock, FakeClock>();
        services.AddSingleton<INotificationService, NullNotificationService>();
        services.AddSingleton<IAuthService, FakeAuthService>();
        services.AddSingleton<IDoseEventRealtime, FakeRealtimeService>();
        services.AddSingleton<IDoseTransitionService, FakeTransitionService>();
        services.AddSingleton<IDoseEventMaterializer, FakeMaterializerService>();
        services.AddSingleton<IProfileRepository, FakeProfileRepo>();
        services.AddSingleton<IMedicationRepository, FakeMedicationRepo>();
        services.AddSingleton<ICourseRepository, FakeCourseRepo>();
        services.AddSingleton<IScheduleRepository, FakeScheduleRepo>();
        services.AddSingleton<IDoseEventRepository, FakeDoseEventRepo>();
        services.AddSingleton<IInventoryRepository, FakeInventoryRepo>();
        services.AddSingleton<IInventoryTransactionRepository, FakeInventoryTransactionRepo>();
        services.AddSingleton<IInventoryCommandService, FakeInventoryCommandService>();
        services.AddSingleton<IDiagnosisRepository, FakeDiagnosisRepo>();
        services.AddSingleton<IDocumentRepository, FakeDocumentRepo>();
        services.AddSingleton<IMessengerLinkRepository, FakeMessengerLinkRepo>();
        services.AddSingleton<INotificationDeliveryRepository, FakeNotificationDeliveryRepo>();
        services.AddSingleton<ITickInvoker, FakeTickInvoker>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Benchmark]
    public TodayViewModel Resolve_TodayViewModel() => _serviceProvider.GetRequiredService<TodayViewModel>();

    [Benchmark]
    public MedicationsViewModel Resolve_MedicationsViewModel() => _serviceProvider.GetRequiredService<MedicationsViewModel>();

    [Benchmark]
    public CoursesViewModel Resolve_CoursesViewModel() => _serviceProvider.GetRequiredService<CoursesViewModel>();

    [Benchmark]
    public ShellViewModel Resolve_ShellViewModel() => _serviceProvider.GetRequiredService<ShellViewModel>();

    private sealed class FakeClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class FakeAuthService : IAuthService
    {
        public AuthSession? CurrentSession => new(Guid.NewGuid(), "bench@user.com", "token", "refresh", DateTimeOffset.UtcNow.AddHours(1));
        public Guid? CurrentUserId => CurrentSession?.UserId;
        public event EventHandler<AuthSession?>? AuthStateChanged { add { } remove { } }
        public Task<AuthSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AuthSession> SignUpWithPasswordAsync(string email, string password, string? username = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeRealtimeService : IDoseEventRealtime
    {
        public event EventHandler<DoseEventChange>? Changed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeTransitionService : IDoseTransitionService
    {
        public Task<DoseTransitionResult> ConfirmAsync(Guid doseEventId, DoseEventSource source, DateTimeOffset? takenAt = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DoseTransitionResult("applied", doseEventId, DoseEventState.Taken));
        public Task<DoseTransitionResult> SkipAsync(Guid doseEventId, DoseEventSource source, DateTimeOffset? skippedAt = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DoseTransitionResult("applied", doseEventId, DoseEventState.Skipped));
        public Task<DoseTransitionResult> UndoConfirmAsync(Guid doseEventId, DateTimeOffset? undoneAt = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DoseTransitionResult("applied", doseEventId, DoseEventState.Scheduled));
    }

    private sealed class FakeMaterializerService : IDoseEventMaterializer
    {
        public Task<int> MaterializeAsync(int horizonDays = 14, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeProfileRepo : IProfileRepository
    {
        public Task<Profile?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Profile?>(Profile.Create(Guid.NewGuid(), "u", "Europe/Moscow", new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0))));
        public Task UpdateAsync(Profile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMedicationRepo : IMedicationRepository
    {
        public Task<IReadOnlyList<Medication>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Medication>>([]);
        public Task<Medication?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Medication?>(null);
        public Task UpsertAsync(Medication medication, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeCourseRepo : ICourseRepository
    {
        public Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Course>>([]);
        public Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Course?>(null);
        public Task UpsertAsync(Course course, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeScheduleRepo : IScheduleRepository
    {
        public Task<IReadOnlyList<Schedule>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Schedule>>([]);
        public Task<Schedule?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Schedule?>(null);
        public Task UpsertAsync(Schedule schedule, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeDoseEventRepo : IDoseEventRepository
    {
        public Task<IReadOnlyList<DoseEvent>> ListForLocalDateAsync(DateOnly localDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DoseEvent>>([]);
        public Task<IReadOnlyList<DoseEvent>> ListByScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DoseEvent>>([]);
        public Task UpsertManyAsync(IReadOnlyList<DoseEvent> events, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DoseEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<DoseEvent?>(null);
    }

    private sealed class FakeInventoryRepo : IInventoryRepository
    {
        public Task<Inventory?> GetByMedicationAsync(Guid medicationId, CancellationToken cancellationToken = default) => Task.FromResult<Inventory?>(null);
        public Task UpsertAsync(Inventory inventory, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeInventoryTransactionRepo : IInventoryTransactionRepository
    {
        public Task<IReadOnlyList<InventoryTransaction>> ListByMedicationAsync(Guid medicationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InventoryTransaction>>([]);
    }

    private sealed class FakeInventoryCommandService : IInventoryCommandService
    {
        public Task<InventoryCommandResult> RestockAsync(Guid medicationId, decimal amount, string? note = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryCommandResult("applied", Guid.NewGuid(), 10));
    }

    private sealed class FakeDiagnosisRepo : IDiagnosisRepository
    {
        public Task<IReadOnlyList<Diagnosis>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Diagnosis>>([]);
        public Task<Diagnosis?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Diagnosis?>(null);
        public Task UpsertAsync(Diagnosis diagnosis, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeDocumentRepo : IDocumentRepository
    {
        public Task<IReadOnlyList<Document>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Document>>([]);
        public Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(null);
        public Task UpsertAsync(Document document, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMessengerLinkRepo : IMessengerLinkRepository
    {
        public Task<IReadOnlyList<MessengerLink>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MessengerLink>>([]);
        public Task<MessengerLink?> GetByChannelAsync(MessengerChannelType channelType, CancellationToken cancellationToken = default) => Task.FromResult<MessengerLink?>(null);
        public Task UpsertAsync(MessengerLink link, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeNotificationDeliveryRepo : INotificationDeliveryRepository
    {
        public Task<IReadOnlyList<NotificationDelivery>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NotificationDelivery>>([]);
        public Task<IReadOnlyList<NotificationDelivery>> ListByDoseEventAsync(Guid doseEventId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NotificationDelivery>>([]);
    }

    private sealed class FakeTickInvoker : ITickInvoker
    {
        public Task<TickInvokeResult> InvokeAsync(CancellationToken cancellationToken = default) => Task.FromResult(new TickInvokeResult(true, "ok"));
    }
}
