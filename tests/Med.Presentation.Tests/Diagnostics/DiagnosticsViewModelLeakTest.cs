using System.Runtime.CompilerServices;
using FluentAssertions;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Presentation.Abstractions;
using Med.Presentation.Diagnostics;
using Xunit;

namespace Med.Presentation.Tests.Diagnostics;

public sealed class DiagnosticsViewModelLeakTest
{
    [Fact]
    public void DiagnosticsViewModel_IsCollectedAfterDispose()
    {
        FakeRealtime realtime = new();
        WeakReference<DiagnosticsViewModel> weakRef = CreateAndDispose(realtime);

        for (int i = 0; i < 3; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
        }

        weakRef.TryGetTarget(out DiagnosticsViewModel? target).Should().BeFalse(
            "DiagnosticsViewModel must be collected by GC after Dispose");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<DiagnosticsViewModel> CreateAndDispose(FakeRealtime realtime)
    {
        var vm = new DiagnosticsViewModel(
            new FakeDeliveries(),
            new FakeTick(),
            realtime,
            new ImmediateDispatcher());

        WeakReference<DiagnosticsViewModel> weakRef = new(vm);
        vm.Dispose();
        return weakRef;
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }

    private sealed class FakeDeliveries : INotificationDeliveryRepository
    {
        public Task<IReadOnlyList<NotificationDelivery>> ListByDoseEventAsync(Guid doseEventId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NotificationDelivery>>([]);

        public Task<IReadOnlyList<NotificationDelivery>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NotificationDelivery>>([]);
    }

    private sealed class FakeTick : ITickInvoker
    {
        public Task<TickInvokeResult> InvokeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TickInvokeResult(true, "{\"ok\":true}"));
    }

    private sealed class FakeRealtime : IDoseEventRealtime
    {
        public event EventHandler<DoseEventChange>? Changed
        {
            add { }
            remove { }
        }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
