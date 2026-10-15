using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Med.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Med.Infrastructure.Notifications;

/// <summary>
/// Кроссплатформенная реализация локальных уведомлений операционной системы (macOS, Linux, Windows).
/// </summary>
public sealed class LocalNotificationService(ILogger<LocalNotificationService>? logger = null) : INotificationService, IDisposable
{
    private static readonly TimeSpan MaxTaskDelay = TimeSpan.FromMilliseconds(int.MaxValue - 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _scheduled = new();

    public Task ShowAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[LocalNotification] Showing notification: {Title} - {Body}", title, body);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            ShowMacNotification(title, body);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ShowLinuxNotification(title, body);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ShowWindowsNotification(title, body);
        }

        return Task.CompletedTask;
    }

    public Task ScheduleAsync(string id, string title, string body, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        CancelScheduled(id);

        TimeSpan delay = at - DateTimeOffset.UtcNow;
        if (delay <= TimeSpan.Zero)
        {
            return ShowAsync(title, body, cancellationToken);
        }

        if (delay > MaxTaskDelay)
        {
            delay = MaxTaskDelay;
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
            catch (OperationCanceledException) { }
            finally
            {
                ((ICollection<KeyValuePair<string, CancellationTokenSource>>)_scheduled)
                    .Remove(new KeyValuePair<string, CancellationTokenSource>(id, cts));
                cts.Dispose();
            }
        }, CancellationToken.None);

        logger?.LogInformation(
            "[LocalNotification] Scheduled notification #{Id} for {At:u} ({Delay:F1}s from now): {Title}",
            id, at, delay.TotalSeconds, title);

        return Task.CompletedTask;
    }

    public Task CancelAsync(string id, CancellationToken cancellationToken = default)
    {
        CancelScheduled(id);
        logger?.LogInformation("[LocalNotification] Cancelled notification #{Id}", id);
        return Task.CompletedTask;
    }

    private void CancelScheduled(string id)
    {
        if (_scheduled.TryRemove(id, out CancellationTokenSource? cts))
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException) { }
        }
    }

    private void ShowMacNotification(string title, string body)
    {
        try
        {
            string script = $"display notification \"{EscapeAppleScript(body)}\" with title \"{EscapeAppleScript(title)}\" sound name \"default\"";
            var psi = new ProcessStartInfo("osascript")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Ошибка отображения уведомления macOS");
        }
    }

    private void ShowLinuxNotification(string title, string body)
    {
        try
        {
            var psi = new ProcessStartInfo("notify-send")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(title);
            psi.ArgumentList.Add(body);
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Ошибка отображения уведомления Linux");
        }
    }

    private void ShowWindowsNotification(string title, string body)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(@"
$t = $args[0]
$b = $args[1]
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null
$template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
$textNodes = $template.GetElementsByTagName('text')
$textNodes.Item(0).AppendChild($template.CreateTextNode($t)) > $null
$textNodes.Item(1).AppendChild($template.CreateTextNode($b)) > $null
$toast = [Windows.UI.Notifications.ToastNotification]::new($template)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('MedTracker').Show($toast)
");
            psi.ArgumentList.Add(title);
            psi.ArgumentList.Add(body);
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Ошибка отображения уведомления Windows");
        }
    }

    private static string EscapeAppleScript(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");

    public void Dispose()
    {
        foreach (string key in _scheduled.Keys)
        {
            CancelScheduled(key);
        }
    }
}
