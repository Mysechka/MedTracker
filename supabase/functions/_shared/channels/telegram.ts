import type {
  EditReminderInput,
  EditReminderResult,
  INotificationChannel,
  NotificationChannelType,
  SendReminderInput,
  SendReminderResult,
} from "./types.ts";
import { formatReminderText, optionalEnv, requireEnv } from "./types.ts";

function telegramApi(method: string): string {
  return `https://api.telegram.org/bot${requireEnv("TELEGRAM_BOT_TOKEN")}/${method}`;
}

export class TelegramChannel implements INotificationChannel {
  readonly channelType: NotificationChannelType = "telegram";

  async sendReminder(input: SendReminderInput): Promise<SendReminderResult> {
    const text = formatReminderText(
      input.medicationName,
      input.dosageLabel,
      input.scheduledAtIso,
    );

    const body = {
      chat_id: input.targetId,
      text,
      reply_markup: {
        inline_keyboard: [[
          { text: "Принял", callback_data: `dose:taken:${input.doseEventId}` },
          { text: "Пропустить", callback_data: `dose:skip:${input.doseEventId}` },
          { text: "Отложить 15 мин", callback_data: `dose:snooze:${input.doseEventId}` },
        ]],
      },
    };

    const response = await fetch(telegramApi("sendMessage"), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });

    if (response.status === 429) {
      const retryAfter = Number(response.headers.get("Retry-After") ?? "60");
      return { ok: false, retryAfterSeconds: Number.isFinite(retryAfter) ? retryAfter : 60, error: "429" };
    }

    const payload = await response.json() as {
      ok?: boolean;
      description?: string;
      result?: { message_id?: number };
    };

    if (!response.ok || !payload.ok || payload.result?.message_id == null) {
      return { ok: false, error: payload.description ?? `HTTP ${response.status}` };
    }

    return { ok: true, messageId: String(payload.result.message_id) };
  }

  async editReminder(input: EditReminderInput): Promise<EditReminderResult> {
    const response = await fetch(telegramApi("editMessageText"), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        chat_id: input.targetId,
        message_id: Number(input.messageId),
        text: input.text,
        reply_markup: { inline_keyboard: [] },
      }),
    });

    const payload = await response.json() as { ok?: boolean; description?: string };
    if (!response.ok || !payload.ok) {
      return { ok: false, error: payload.description ?? `HTTP ${response.status}` };
    }
    return { ok: true };
  }
}

export function isTelegramConfigured(): boolean {
  return optionalEnv("TELEGRAM_BOT_TOKEN") != null;
}
