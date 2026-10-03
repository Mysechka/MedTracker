using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Med.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Med.Infrastructure.Notifications;

/// <summary>
/// Кроссплатформенная реализация локальных уведомлений операционной системы (macOS, Linux, Windows).
/// </summary>
public sealed class LocalNotificationService : INotificationService, IDisposable
{
    private readonly ILogger<LocalNotificationService>? _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _scheduled = new();

    public LocalNotificationService(ILogger<LocalNotificationService>? logger = null)
    {
        _logger = logger;
    }

    public Task ShowAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("[LocalNotification] Showing notification: {Title} - {Body}", title, body);

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
                _scheduled.TryRemove(id, out _);
            }
        }, cts.Token);

        _logger?.LogInformation(
            "[LocalNotification] Scheduled notification #{Id} for {At:u} ({Delay:F1}s from now): {Title}",
            id, at, delay.TotalSeconds, title);

        return Task.CompletedTask;
    }

    public Task CancelAsync(string id, CancellationToken cancellationToken = default)
    {
        CancelScheduled(id);
        _logger?.LogInformation("[LocalNotification] Cancelled notification #{Id}", id);
        return Task.CompletedTask;
    }

    private void CancelScheduled(string id)
    {
        if (_scheduled.TryRemove(id, out CancellationTokenSource? cts))
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch (ObjectDisposedException) { }
        }
    }

    private void ShowMacNotification(string title, string body)
    {
        try
        {
            string safeTitle = EscapeAppleScript(title);
            string safeBody = EscapeAppleScript(body);
            string script = $"display notification \"{safeBody}\" with title \"{safeTitle}\" sound name \"default\"";

            var psi = new ProcessStartInfo("osascript")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);

            using var process = new Process { StartInfo = psi };
            process.Start();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Ошибка отображения уведомления macOS");
        }
    }

    private void ShowLinuxNotification(string title, string body)
    {
        try
        {
            var psi = new ProcessStartInfo("notify-send")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(title);
            psi.ArgumentList.Add(body);

            using var process = new Process { StartInfo = psi };
            process.Start();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Ошибка отображения уведомления Linux");
        }
    }

    private void ShowWindowsNotification(string title, string body)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell")
            {
                UseShellExecute = false,
                CreateNoWindow = true
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

            using var process = new Process { StartInfo = psi };
            process.Start();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Ошибка отображения уведомления Windows");
        }
    }

    private static string EscapeAppleScript(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");

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
