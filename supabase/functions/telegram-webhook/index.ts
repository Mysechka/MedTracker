import { TelegramChannel } from "../_shared/channels/telegram.ts";
import { createServiceClient, rpcJson } from "../_shared/supabase.ts";
import {
  formatSkippedText,
  formatTakenText,
  optionalEnv,
  requireEnv,
} from "../_shared/types.ts";

interface TelegramUpdate {
  message?: {
    text?: string;
    chat?: { id: number };
  };
  callback_query?: {
    id: string;
    data?: string;
    from?: { id: number };
    message?: {
      message_id: number;
      chat: { id: number };
    };
  };
}

Deno.serve(async (req) => {
  if (req.method !== "POST") {
    return new Response("Method not allowed", { status: 405 });
  }

  const secret = optionalEnv("TELEGRAM_WEBHOOK_SECRET");
  if (secret) {
    const header = req.headers.get("X-Telegram-Bot-Api-Secret-Token");
    if (header !== secret) {
      return new Response("Unauthorized", { status: 401 });
    }
  } else {
    // Секрет обязателен в проде; без него отклоняем, чтобы не открыть endpoint.
    console.error("TELEGRAM_WEBHOOK_SECRET is not set");
    return new Response("Webhook secret not configured", { status: 503 });
  }

  // Токен бота должен быть задан для answer/edit.
  try {
    requireEnv("TELEGRAM_BOT_TOKEN");
  } catch {
    return new Response("Bot token not configured", { status: 503 });
  }

  let update: TelegramUpdate;
  try {
    update = await req.json() as TelegramUpdate;
  } catch {
    return new Response("Bad request", { status: 400 });
  }

  try {
    const client = createServiceClient();

    if (update.message?.text && update.message.chat?.id != null) {
      await handleLinkMessage(
        client,
        update.message.text.trim(),
        String(update.message.chat.id),
      );
      return Response.json({ ok: true });
    }

    if (update.callback_query) {
      await handleCallback(client, update.callback_query);
      return Response.json({ ok: true });
    }

    return Response.json({ ok: true });
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error("telegram-webhook failed", message);
    return Response.json({ ok: false, error: message }, { status: 500 });
  }
});

async function handleLinkMessage(
  // deno-lint-ignore no-explicit-any
  client: any,
  text: string,
  chatId: string,
): Promise<void> {
  const code = extractLinkCode(text);
  if (!code) {
    return;
  }

  const result = await rpcJson<{ outcome: string; reason?: string }>(
    client,
    "confirm_messenger_link",
    {
      p_link_code: code,
      p_channel_type: "telegram",
      p_chat_id: chatId,
      p_channel_id: null,
    },
  );

  const reply = result.outcome === "Applied"
    ? "Telegram привязан. Уведомления о приёмах будут приходить сюда."
    : "Код не найден или уже использован.";

  await fetch(
    `https://api.telegram.org/bot${requireEnv("TELEGRAM_BOT_TOKEN")}/sendMessage`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ chat_id: chatId, text: reply }),
    },
  );
}

function extractLinkCode(text: string): string | null {
  const trimmed = text.trim();
  const startMatch = /^\/start(?:\s+(.+))?$/i.exec(trimmed);
  if (startMatch) {
    const payload = startMatch[1]?.trim();
    return payload && payload.length > 0 ? payload : null;
  }
  // One-time код из приложения: буквы/цифры, 6–64 символа.
  if (/^[A-Za-z0-9_-]{6,64}$/.test(trimmed)) {
    return trimmed;
  }
  return null;
}

async function handleCallback(
  // deno-lint-ignore no-explicit-any
  client: any,
  callback: NonNullable<TelegramUpdate["callback_query"]>,
): Promise<void> {
  const data = callback.data ?? "";
  const match = /^dose:(taken|skip|snooze):([0-9a-f-]{36})$/i.exec(data);
  const chatId = callback.message?.chat.id;
  const messageId = callback.message?.message_id;

  if (!match || chatId == null || messageId == null) {
    await answerCallback(callback.id, "Некорректные данные");
    return;
  }

  const action = match[1].toLowerCase();
  const doseEventId = match[2];
  const channel = new TelegramChannel();
  const targetId = String(chatId);

  if (action === "snooze") {
    await rpcJson(client, "snooze_notification_delivery", {
      p_dose_event_id: doseEventId,
      p_channel_type: "telegram",
      p_minutes: 15,
    });
    await answerCallback(callback.id, "Напомню через 15 минут");
    await channel.editReminder({
      messageId: String(messageId),
      targetId,
      text: "Отложено на 15 минут",
    });
    return;
  }

  const rpcName = action === "taken" ? "confirm_dose" : "skip_dose";
  const result = await rpcJson<{
    outcome: string;
    state?: string;
    reason?: string;
  }>(client, rpcName, {
    p_dose_event_id: doseEventId,
    p_source: "Telegram",
    ...(action === "taken"
      ? { p_taken_at: new Date().toISOString() }
      : { p_skipped_at: new Date().toISOString() }),
  });

  if (result.outcome === "Rejected") {
    await answerCallback(callback.id, "Уже обработано");
  } else {
    await answerCallback(callback.id, action === "taken" ? "Принято" : "Пропущено");
  }

  const text = action === "taken"
    ? formatTakenText(new Date().toISOString().slice(11, 16))
    : formatSkippedText();

  await channel.editReminder({
    messageId: String(messageId),
    targetId,
    text,
  });

  if (result.outcome === "Applied" || result.outcome === "NoOp") {
    await client
      .from("notification_deliveries")
      .update({ status: "Edited", message_id: String(messageId) })
      .eq("dose_event_id", doseEventId)
      .eq("channel_type", "telegram");
  }
}

async function answerCallback(callbackId: string, text: string): Promise<void> {
  await fetch(
    `https://api.telegram.org/bot${requireEnv("TELEGRAM_BOT_TOKEN")}/answerCallbackQuery`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ callback_query_id: callbackId, text }),
    },
  );
}
