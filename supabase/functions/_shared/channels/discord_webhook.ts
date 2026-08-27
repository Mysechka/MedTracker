import type {
  EditReminderInput,
  EditReminderResult,
  INotificationChannel,
  NotificationChannelType,
  SendReminderInput,
  SendReminderResult,
} from "../types.ts";
import { formatReminderText, optionalEnv, requireEnv } from "../types.ts";

/**
 * Режим деградации: обычный Discord webhook.
 * Только текст — без кнопок. Подтверждение из Telegram или GUI.
 */
export class DiscordWebhookChannel implements INotificationChannel {
  readonly channelType: NotificationChannelType = "discord_webhook";

  async sendReminder(input: SendReminderInput): Promise<SendReminderResult> {
    const webhookUrl = requireEnv("DISCORD_WEBHOOK_URL");
    const text = formatReminderText(
      input.medicationName,
      input.dosageLabel,
      input.scheduledAtIso,
    ) + "\n(Подтвердите в приложении или Telegram — кнопки недоступны.)";

    const response = await fetch(`${webhookUrl}?wait=true`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ content: text }),
    });

    if (response.status === 429) {
      const payload = await response.json().catch(() => ({})) as { retry_after?: number };
      return {
        ok: false,
        retryAfterSeconds: Math.ceil(payload.retry_after ?? 60),
        error: "429",
      };
    }

    if (!response.ok) {
      return { ok: false, error: (await response.text()).slice(0, 300) };
    }

    const payload = await response.json() as { id?: string };
    return { ok: true, messageId: payload.id ?? `webhook:${input.deliveryId}` };
  }

  async editReminder(input: EditReminderInput): Promise<EditReminderResult> {
    const webhookUrl = requireEnv("DISCORD_WEBHOOK_URL");
    // webhook edit: /webhooks/{id}/{token}/messages/{message.id}
    const base = webhookUrl.replace(/\/$/, "");
    const response = await fetch(`${base}/messages/${input.messageId}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ content: input.text }),
    });

    if (!response.ok) {
      return { ok: false, error: (await response.text()).slice(0, 300) };
    }
    return { ok: true };
  }
}

export function isDiscordWebhookConfigured(): boolean {
  return optionalEnv("DISCORD_WEBHOOK_URL") != null;
}
