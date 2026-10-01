using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace Med.Android;

/// <summary>
/// Приёмник сообщений будильника (AlarmManager) для гарантированного показа уведомлений даже после выгрузки приложения.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class NotificationReceiver : BroadcastReceiver
{
    public const string ChannelId = "medtracker_reminders";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }

        string title = intent.GetStringExtra("title") ?? "MedTracker";
        string body = intent.GetStringExtra("body") ?? string.Empty;
        int id = intent.GetIntExtra("id", 0);

        var launchIntent = new Intent(context, typeof(MainActivity));
        launchIntent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);

        PendingIntentFlags pendingFlags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            pendingFlags |= PendingIntentFlags.Immutable;
        }

        var pendingIntent = PendingIntent.GetActivity(context, id, launchIntent, pendingFlags);
        int iconId = context.ApplicationInfo is { Icon: not 0 } appInfo ? appInfo.Icon : global::Android.Resource.Drawable.IcDialogInfo;

        var builder = new NotificationCompat.Builder(context, ChannelId);
        builder.SetContentTitle(title);
        builder.SetContentText(body);
        builder.SetSmallIcon(iconId);
        builder.SetAutoCancel(true);
        builder.SetPriority(NotificationCompat.PriorityHigh);
        if (pendingIntent is not null)
        {
            builder.SetContentIntent(pendingIntent);
        }

        var notificationManager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        if (notificationManager is not null)
        {
            using var notification = builder.Build();
            if (notification is not null)
            {
                notificationManager.Notify(id, notification);
            }
        }
    }
}
