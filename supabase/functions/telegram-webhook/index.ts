import { TelegramChannel } from "../_shared/channels/telegram.ts";
import { createServiceClient, rpcJson } from "../_shared/supabase.ts";
import {
  formatSkippedText,
  formatTakenText,
  optionalEnv,
  requireEnv,
} from "../_shared/types.ts";
import type { SupabaseClient } from "https://esm.sh/@supabase/supabase-js@2.49.1";

interface TelegramUser {
  id: number;
  is_bot?: boolean;
  first_name?: string;
  last_name?: string;
  username?: string;
}

interface TelegramChat {
  id: number;
  type: string;
  title?: string;
  username?: string;
  first_name?: string;
}

interface TelegramMessage {
  message_id: number;
  from?: TelegramUser;
  chat: TelegramChat;
  date?: number;
  text?: string;
}

interface TelegramCallbackQuery {
  id: string;
  from: TelegramUser;
  message?: TelegramMessage;
  inline_message_id?: string;
  chat_instance?: string;
  data?: string;
}

interface TelegramUpdate {
  update_id: number;
  message?: TelegramMessage;
  callback_query?: TelegramCallbackQuery;
}

interface ConfirmLinkRpcResult {
  outcome: "Applied" | "Rejected";
  reason?: string;
  user_id?: string;
  channel_type?: string;
}

interface DoseRpcResult {
  outcome: "Applied" | "NoOp" | "Rejected";
  state?: string;
  reason?: string;
  dose_event_id?: string;
}

Deno.serve(async (req: Request): Promise<Response> => {
  if (req.method !== "POST") {
    return new Response("Method not allowed", { status: 405 });
  }

  // 1. Валидация секретного токена вебхука
  const secret = optionalEnv("TELEGRAM_WEBHOOK_SECRET");
  if (!secret) {
    console.error("TELEGRAM_WEBHOOK_SECRET is not configured");
    return new Response("Webhook secret not configured", { status: 503 });
  }

  const headerSecret = req.headers.get("X-Telegram-Bot-Api-Secret-Token");
  if (headerSecret !== secret) {
    return new Response("Unauthorized", { status: 401 });
  }

  // 2. Валидация токена бота
  let botToken: string;
  try {
    botToken = requireEnv("TELEGRAM_BOT_TOKEN");
  } catch {
    return new Response("Bot token not configured", { status: 503 });
  }

  // 3. Парсинг тела запроса
  let update: TelegramUpdate;
  try {
    update = await req.json() as TelegramUpdate;
  } catch {
    return new Response("Bad request", { status: 400 });
  }

  try {
    const client = createServiceClient();

    // 4. Обработка обычных сообщений и команды /start
    if (update.message?.text && update.message.chat.id != null) {
      await handleMessage(
        client,
        botToken,
        update.message.text.trim(),
        String(update.message.chat.id),
      );
      return Response.json({ ok: true });
    }

    // 5. Обработка Callback Query от inline-кнопок
    if (update.callback_query) {
      await handleCallback(client, botToken, update.callback_query);
      return Response.json({ ok: true });
    }

    return Response.json({ ok: true });
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error("telegram-webhook error:", message);
    return Response.json({ ok: false, error: message }, { status: 500 });
  }
});

/**
 * Обрабатывает входящее текстовое сообщение, связывание аккаунта по коду или команду /start.
 */
async function handleMessage(
  client: SupabaseClient,
  botToken: string,
  text: string,
  chatId: string,
): Promise<void> {
  const code = extractLinkCode(text);

  if (code) {
    const result = await rpcJson<ConfirmLinkRpcResult>(
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
      ? "✅ Telegram успешно привязан! Напоминания о приёме лекарств будут приходить в этот чат."
      : "❌ Код привязки не найден или его срок действия истёк. Сгенерируйте новый код в приложении MedTracker (Настройки → Мессенджеры).";

    await sendTelegramMessage(botToken, chatId, reply);
    return;
  }

  // Если просто /start без кода или любое другое сообщение
  if (/^\/start$/i.test(text)) {
    const welcome =
      "👋 Привет! Я бот приложения MedTracker.\n\n" +
      "Чтобы получать уведомления о приёмах лекарств сюда:\n" +
      "1. Откройте приложение MedTracker на вашем устройстве.\n" +
      "2. Перейдите в Настройки → Мессенджеры.\n" +
      "3. Нажмите «Привязать Telegram» и скопируйте одноразовый код.\n" +
      "4. Отправьте этот код мне сюда в чат.";

    await sendTelegramMessage(botToken, chatId, welcome);
  }
}

function extractLinkCode(text: string): string | null {
  const trimmed = text.trim();
  const startMatch = /^\/start(?:\s+(.+))?$/i.exec(trimmed);
  if (startMatch) {
    const payload = startMatch[1]?.trim();
    return payload && payload.length > 0 ? payload : null;
  }

  // One-time код из приложения: буквы/цифры, дефис, подчёркивание
  if (/^[A-Za-z0-9_-]{6,64}$/.test(trimmed)) {
    return trimmed;
  }

  return null;
}

/**
 * Обрабатывает нажатия на inline-кнопки «Принял», «Пропуск», «Отложить 15 мин».
 */
async function handleCallback(
  client: SupabaseClient,
  botToken: string,
  callback: TelegramCallbackQuery,
): Promise<void> {
  const data = callback.data ?? "";
  const match = /^dose:(taken|skip|snooze):([0-9a-f-]{36})$/i.exec(data);
  const chatId = callback.message?.chat.id;
  const messageId = callback.message?.message_id;

  if (!match || chatId == null || messageId == null) {
    await answerCallback(botToken, callback.id, "Некорректные данные действия");
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

    await answerCallback(botToken, callback.id, "⏰ Напомню через 15 минут");
    await channel.editReminder({
      messageId: String(messageId),
      targetId,
      text: "⏳ Отложено на 15 минут",
    });
    return;
  }

  const rpcName = action === "taken" ? "confirm_dose" : "skip_dose";
  const nowUtc = new Date().toISOString();

  const result = await rpcJson<DoseRpcResult>(client, rpcName, {
    p_dose_event_id: doseEventId,
    p_source: "Telegram",
    ...(action === "taken"
      ? { p_taken_at: nowUtc }
      : { p_skipped_at: nowUtc }),
  });

  if (result.outcome === "Rejected") {
    await answerCallback(botToken, callback.id, "Уже обработано ранее");
  } else {
    const ack = action === "taken"
      ? `Принято в ${nowUtc.slice(11, 16)}`
      : "Приём пропущен";
    await answerCallback(botToken, callback.id, ack);
  }

  const text = action === "taken"
    ? formatTakenText(nowUtc.slice(11, 16))
    : formatSkippedText();

  // Обновляем исходное сообщение с удалением кнопок
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

async function answerCallback(
  botToken: string,
  callbackId: string,
  text: string,
): Promise<void> {
  await fetch(
    `https://api.telegram.org/bot${botToken}/answerCallbackQuery`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ callback_query_id: callbackId, text }),
    },
  );
}

async function sendTelegramMessage(
  botToken: string,
  chatId: string,
  text: string,
): Promise<void> {
  await fetch(
    `https://api.telegram.org/bot${botToken}/sendMessage`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ chat_id: chatId, text }),
    },
  );
}
