using System.Collections.Concurrent;
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Med.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Med.Android;

/// <summary>
/// Нативная реализация локальных уведомлений для платформы Android.
/// </summary>
public sealed class AndroidNotificationService : INotificationService, IDisposable
{
    private const string ChannelId = "medtracker_reminders";
    private const string ChannelName = "Напоминания о приёме лекарств";
    private readonly Context _context;
    private readonly ILogger<AndroidNotificationService>? _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _scheduled = new();

    public AndroidNotificationService(Context context, ILogger<AndroidNotificationService>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
        CreateNotificationChannel();
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.High)
            {
                Description = "Уведомления о времени приёма лекарственных препаратов"
            };
            channel.EnableVibration(true);
            var notificationManager = (NotificationManager?)_context.GetSystemService(Context.NotificationService);
            notificationManager?.CreateNotificationChannel(channel);
        }
    }

    public Task ShowAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        try
        {
            var intent = new Intent(_context, typeof(MainActivity));
            intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            var pendingIntent = PendingIntent.GetActivity(
                _context,
                0,
                intent,
                PendingIntentFlags.UpdateCurrent | (Build.VERSION.SdkInt >= BuildVersionCodes.M ? PendingIntentFlags.Immutable : 0));

            int iconId = _context.ApplicationInfo?.Icon ?? global::Android.Resource.Drawable.IcDialogInfo;
            var builder = new NotificationCompat.Builder(_context, ChannelId);
            builder.SetContentTitle(title);
            builder.SetContentText(body);
            builder.SetSmallIcon(iconId);
            builder.SetAutoCancel(true);
            builder.SetPriority(NotificationCompat.PriorityHigh);
            builder.SetContentIntent(pendingIntent);

            var notificationManager = (NotificationManager?)_context.GetSystemService(Context.NotificationService);
            int notificationId = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % int.MaxValue);
            using var notification = builder.Build();
            if (notification is not null && notificationManager is not null)
            {
                notificationManager.Notify(notificationId, notification);
            }
            _logger?.LogInformation("[AndroidNotification] Displayed notification: {Title} - {Body}", title, body);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[AndroidNotification] Failed to display notification: {Message}", ex.Message);
        }

        return Task.CompletedTask;
    }

    public Task ScheduleAsync(string id, string title, string body, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        CancelScheduled(id);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeSpan delay = at - now;
        if (delay <= TimeSpan.Zero)
        {
            return ShowAsync(title, body, cancellationToken);
        }

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _scheduled[id] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                if (!cts.Token.IsCancellationRequested)
                {
                    await ShowAsync(title, body, cts.Token).ConfigureAwait(false);
                }
            }
            catch (System.OperationCanceledException) { }
            finally
            {
                _scheduled.TryRemove(id, out _);
            }
        }, cts.Token);

        _logger?.LogInformation("[AndroidNotification] Scheduled notification #{Id} for {At:u}: {Title}", id, at, title);
        return Task.CompletedTask;
    }

    public Task CancelAsync(string id, CancellationToken cancellationToken = default)
    {
        CancelScheduled(id);
        _logger?.LogInformation("[AndroidNotification] Cancelled notification #{Id}", id);
        return Task.CompletedTask;
    }

    private void CancelScheduled(string id)
    {
        if (_scheduled.TryRemove(id, out var cts))
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch (ObjectDisposedException) { }
        }
    }

    public void Dispose()
    {
        foreach (var kvp in _scheduled)
        {
            try
            {
                kvp.Value.Cancel();
                kvp.Value.Dispose();
            }
            catch (ObjectDisposedException) { }
        }
        _scheduled.Clear();
    }
}
