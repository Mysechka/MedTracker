using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Med.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Med.Android;

/// <summary>
/// Нативная реализация локальных уведомлений для платформы Android на базе AlarmManager.
/// </summary>
public sealed class AndroidNotificationService : INotificationService, IDisposable
{
    private const string ChannelId = NotificationReceiver.ChannelId;
    private const string ChannelName = "Напоминания о приёме лекарств";
    private readonly Context _context;
    private readonly ILogger<AndroidNotificationService>? _logger;

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

            PendingIntentFlags pendingFlags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            {
                pendingFlags |= PendingIntentFlags.Immutable;
            }

            var pendingIntent = PendingIntent.GetActivity(_context, 0, intent, pendingFlags);

            int iconId = _context.ApplicationInfo is { Icon: not 0 } appInfo ? appInfo.Icon : global::Android.Resource.Drawable.IcDialogInfo;
            var builder = new NotificationCompat.Builder(_context, ChannelId);
            builder.SetContentTitle(title);
            builder.SetContentText(body);
            builder.SetSmallIcon(iconId);
            builder.SetAutoCancel(true);
            builder.SetPriority(NotificationCompat.PriorityHigh);
            if (pendingIntent is not null)
            {
                builder.SetContentIntent(pendingIntent);
            }

            var notificationManager = (NotificationManager?)_context.GetSystemService(Context.NotificationService);
            int notificationId = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % int.MaxValue);
            if (notificationManager is not null)
            {
                using var notification = builder.Build();
                if (notification is not null)
                {
                    notificationManager.Notify(notificationId, notification);
                }
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

        long triggerAtMillis = at.ToUnixTimeMilliseconds();
        long nowMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (triggerAtMillis <= nowMillis)
        {
            return ShowAsync(title, body, cancellationToken);
        }

        int notificationId = GetNotificationId(id);
        var intent = new Intent(_context, typeof(NotificationReceiver));
        intent.PutExtra("title", title);
        intent.PutExtra("body", body);
        intent.PutExtra("id", notificationId);

        PendingIntentFlags pendingFlags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            pendingFlags |= PendingIntentFlags.Immutable;
        }

        var pending = PendingIntent.GetBroadcast(_context, notificationId, intent, pendingFlags);
        var alarmManager = (AlarmManager?)_context.GetSystemService(Context.AlarmService);

        if (alarmManager is not null && pending is not null)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            {
                alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMillis, pending);
            }
            else
            {
                alarmManager.SetExact(AlarmType.RtcWakeup, triggerAtMillis, pending);
            }
        }

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
        try
        {
            int notificationId = GetNotificationId(id);
            var intent = new Intent(_context, typeof(NotificationReceiver));
            PendingIntentFlags pendingFlags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            {
                pendingFlags |= PendingIntentFlags.Immutable;
            }

            var pending = PendingIntent.GetBroadcast(_context, notificationId, intent, pendingFlags);
            if (pending is not null)
            {
                var alarmManager = (AlarmManager?)_context.GetSystemService(Context.AlarmService);
                alarmManager?.Cancel(pending);
                pending.Cancel();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Ошибка отмены будильника для уведомления #{Id}", id);
        }
    }

    public void Dispose()
    {
        // Будильники AlarmManager сохраняются в операционной системе для фонового пробуждения
    }

    private static int GetNotificationId(string id)
    {
        if (Guid.TryParse(id, out var guid))
        {
            return BitConverter.ToInt32(guid.ToByteArray(), 0) & 0x7FFFFFFF;
        }
        return (id.Aggregate(0, (hash, c) => (hash * 31) + c)) & 0x7FFFFFFF;
    }
}
