using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

namespace Med.Android;

[Activity(
    Label = "MedTracker",
    Theme = "@style/Theme.AppCompat.Light.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RequestNotificationPermission();
    }

    private void RequestNotificationPermission()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            const string postNotifications = "android.permission.POST_NOTIFICATIONS";
            if (CheckSelfPermission(postNotifications) != Permission.Granted)
            {
                RequestPermissions([postNotifications], 1001);
            }
        }
    }
}
