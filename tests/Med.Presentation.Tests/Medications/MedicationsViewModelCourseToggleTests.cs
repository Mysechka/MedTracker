using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Entities;
using Med.Presentation.Abstractions;
using Med.Presentation.Feedback;
using Med.Presentation.Medications;
using Med.Presentation.Sync;
using Xunit;

namespace Med.Presentation.Tests.Medications;

public sealed class MedicationsViewModelCourseToggleTests
{
    private static readonly Guid TestUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task CourseMode_Off_CreatesPermanentCourse()
    {
        FakeMedicationRepo medRepo = new();
        FakeInventoryRepo invRepo = new();
        FakeCourseRepo courseRepo = new();
        FakeScheduleRepo schedRepo = new();
        FakeAuth auth = new(TestUserId);
        UserFeedback feedback = new(new FakeSnackbar());
        ImmediateUiDispatcher dispatcher = new();
        WeakReferenceMessenger messenger = new();
        EntityChangeDeduplicator deduplicator = new();

        MedicationsViewModel vm = new(
            medRepo,
            invRepo,
            courseRepo,
            schedRepo,
            auth,
            new RestockInventoryUseCase(new FakeInventoryCommands()),
            feedback,
            dispatcher,
            messenger,
            deduplicator);

        vm.StartNewCommand.Execute(null);
        vm.Name = "Аспирин";
        vm.TimeMorning = true; // Триггер HasScheduleTags
        vm.IsCourseMode = false;

        await vm.SaveCommand.ExecuteAsync(null);

        var savedCourses = await courseRepo.ListAsync(TestContext.Current.CancellationToken);
        savedCourses.Should().HaveCount(1);
        Course course = savedCourses[0];
        course.EndsOn.Should().BeNull();
        course.DurationDays.Should().BeNull();
        course.EffectiveEndsOn.Should().Be(DateOnly.MaxValue);
    }

    [Fact]
    public async Task CourseMode_On_CreatesTimedCourse()
    {
        FakeMedicationRepo medRepo = new();
        FakeInventoryRepo invRepo = new();
        FakeCourseRepo courseRepo = new();
        FakeScheduleRepo schedRepo = new();
        FakeAuth auth = new(TestUserId);
        UserFeedback feedback = new(new FakeSnackbar());
        ImmediateUiDispatcher dispatcher = new();
        WeakReferenceMessenger messenger = new();
        EntityChangeDeduplicator deduplicator = new();

        MedicationsViewModel vm = new(
            medRepo,
            invRepo,
            courseRepo,
            schedRepo,
            auth,
            new RestockInventoryUseCase(new FakeInventoryCommands()),
            feedback,
            dispatcher,
            messenger,
            deduplicator);

        DateOnly targetEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        vm.StartNewCommand.Execute(null);
        vm.Name = "Антибиотик";
        vm.TimeMorning = true;
        vm.IsCourseMode = true;
        vm.CourseEndsOn = targetEnd;

        await vm.SaveCommand.ExecuteAsync(null);

        var savedCourses = await courseRepo.ListAsync(TestContext.Current.CancellationToken);
        savedCourses.Should().HaveCount(1);
        Course course = savedCourses[0];
        course.EndsOn.Should().Be(targetEnd);
        course.DurationDays.Should().Be(11);
    }

    [Fact]
    public void CourseMode_Toggle_UpdatesFormVisibility()
    {
        FakeMedicationRepo medRepo = new();
        FakeInventoryRepo invRepo = new();
        FakeCourseRepo courseRepo = new();
        FakeScheduleRepo schedRepo = new();
        FakeAuth auth = new(TestUserId);
        UserFeedback feedback = new(new FakeSnackbar());

        MedicationsViewModel vm = new(
            medRepo,
            invRepo,
            courseRepo,
            schedRepo,
            auth,
            new RestockInventoryUseCase(new FakeInventoryCommands()),
            feedback);

        vm.IsCourseMode.Should().BeFalse();
        vm.IsCourseMode = true;
        vm.IsCourseMode.Should().BeTrue();
        vm.IsCourseMode = false;
        vm.IsCourseMode.Should().BeFalse();
    }

    [Fact]
    public async Task LoadItems_HidesExpiredMedications()
    {
        FakeMedicationRepo medRepo = new();
        FakeInventoryRepo invRepo = new();
        FakeCourseRepo courseRepo = new();
        FakeScheduleRepo schedRepo = new();
        FakeAuth auth = new(TestUserId);
        UserFeedback feedback = new(new FakeSnackbar());
        ImmediateUiDispatcher dispatcher = new();

        Medication expiredMed = Medication.Create(
            Guid.NewGuid(),
            TestUserId,
            "Истёкшее лекарство",
            "tablet",
            "1",
            "шт");
        await medRepo.UpsertAsync(expiredMed, TestContext.Current.CancellationToken);

        DateOnly pastStart = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30);
        DateOnly pastEnd = pastStart.AddDays(7);
        Course expiredCourse = Course.Create(
            Guid.NewGuid(),
            TestUserId,
            expiredMed.Id,
            pastStart,
            endsOn: pastEnd,
            durationDays: 8);
        await courseRepo.UpsertAsync(expiredCourse, TestContext.Current.CancellationToken);

        MedicationsViewModel vm = new(
            medRepo,
            invRepo,
            courseRepo,
            schedRepo,
            auth,
            new RestockInventoryUseCase(new FakeInventoryCommands()),
            feedback,
            dispatcher);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadItems_ShowsPermanentMedications()
    {
        FakeMedicationRepo medRepo = new();
        FakeInventoryRepo invRepo = new();
        FakeCourseRepo courseRepo = new();
        FakeScheduleRepo schedRepo = new();
        FakeAuth auth = new(TestUserId);
        UserFeedback feedback = new(new FakeSnackbar());
        ImmediateUiDispatcher dispatcher = new();

        Medication permanentMed = Medication.Create(
            Guid.NewGuid(),
            TestUserId,
            "Постоянное лекарство",
            "tablet",
            "1",
            "шт");
        await medRepo.UpsertAsync(permanentMed, TestContext.Current.CancellationToken);

        DateOnly pastStart = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30);
        Course permanentCourse = Course.Create(
            Guid.NewGuid(),
            TestUserId,
            permanentMed.Id,
            pastStart,
            endsOn: null,
            durationDays: null);
        await courseRepo.UpsertAsync(permanentCourse, TestContext.Current.CancellationToken);

        MedicationsViewModel vm = new(
            medRepo,
            invRepo,
            courseRepo,
            schedRepo,
            auth,
            new RestockInventoryUseCase(new FakeInventoryCommands()),
            feedback,
            dispatcher);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Should().HaveCount(1);
        vm.Items[0].Name.Should().Be("Постоянное лекарство");
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }

    private sealed class FakeSnackbar : ISnackbarService
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

    private sealed class FakeMedicationRepo : IMedicationRepository
    {
        private readonly List<Medication> _items = [];
        public Task<IReadOnlyList<Medication>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Medication>>(_items.ToList());
        public Task<Medication?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(i => i.Id == id));
        public Task UpsertAsync(Medication medication, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(i => i.Id == medication.Id);
            _items.Add(medication);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(i => i.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInventoryRepo : IInventoryRepository
    {
        private readonly List<Inventory> _items = [];
        public Task<Inventory?> GetByMedicationAsync(Guid medicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(i => i.MedicationId == medicationId));
        public Task UpsertAsync(Inventory inventory, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(i => i.Id == inventory.Id);
            _items.Add(inventory);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInventoryCommands : IInventoryCommandService
    {
        public Task<InventoryCommandResult> RestockAsync(Guid medicationId, decimal amount, string? note = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryCommandResult("Applied", Guid.NewGuid(), amount));
    }

    private sealed class FakeCourseRepo : ICourseRepository
    {
        private readonly List<Course> _items = [];
        public Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Course>>(_items.ToList());
        public Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(i => i.Id == id));
        public Task UpsertAsync(Course course, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(i => i.Id == course.Id);
            _items.Add(course);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(i => i.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeScheduleRepo : IScheduleRepository
    {
        private readonly List<Schedule> _items = [];
        public Task<IReadOnlyList<Schedule>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Schedule>>(_items.Where(s => s.CourseId == courseId).ToArray());
        public Task<Schedule?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(s => s.Id == id));
        public Task UpsertAsync(Schedule schedule, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(s => s.Id == schedule.Id);
            _items.Add(schedule);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(s => s.Id == id);
            return Task.CompletedTask;
        }
    }
}
