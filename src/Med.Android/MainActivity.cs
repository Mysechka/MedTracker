using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia.Android;

namespace Med.Android;

[Activity(
    Label = "MedTracker",
    Icon = "@mipmap/ic_launcher",
    RoundIcon = "@mipmap/ic_launcher_round",
    Theme = "@style/Theme.AppCompat.Light.NoActionBar",
    MainLauncher = true,
    WindowSoftInputMode = SoftInput.AdjustResize,
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
