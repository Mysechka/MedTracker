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

    private static void ShowMacNotification(string title, string body)
    {
        try
        {
            string safeTitle = EscapeAppleScript(title);
            string safeBody = EscapeAppleScript(body);
            string script = $"display notification \"{safeBody}\" with title \"{safeTitle}\" sound name \"default\"";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = $"-e '{script}'",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
        }
        catch
        {
            // Ошибки запуска нативного процесса не должны приводить к падению приложения
        }
    }

    private static void ShowLinuxNotification(string title, string body)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "notify-send",
                    Arguments = $"\"{title.Replace("\"", "\\\"")}\" \"{body.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
        }
        catch { }
    }

    private static void ShowWindowsNotification(string title, string body)
    {
        try
        {
            string escapedTitle = title.Replace("'", "''");
            string escapedBody = body.Replace("'", "''");
            string command = $"[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null; $template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02); $textNodes = $template.GetElementsByTagName('text'); $textNodes.Item(0).AppendChild($template.CreateTextNode('{escapedTitle}')) > $null; $textNodes.Item(1).AppendChild($template.CreateTextNode('{escapedBody}')) > $null; $toast = [Windows.UI.Notifications.ToastNotification]::new($template); [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('MedTracker').Show($toast);";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
        }
        catch { }
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
