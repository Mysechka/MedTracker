import type { SupabaseClient } from "@supabase/supabase-js";
import { handleLinkCommand } from "../commands/link.js";
import { handleTodayCommand } from "../commands/today.js";
import { formatSkippedText, formatSnoozedText, formatTakenText } from "../utils/formatters.js";

export interface TelegramUser {
  id: number;
  is_bot?: boolean;
  first_name?: string;
  last_name?: string;
  username?: string;
}

export interface TelegramChat {
  id: number;
  type: string;
  title?: string;
  username?: string;
  first_name?: string;
}

export interface TelegramMessage {
  message_id: number;
  from?: TelegramUser;
  chat: TelegramChat;
  date?: number;
  text?: string;
}

export interface TelegramCallbackQuery {
  id: string;
  from: TelegramUser;
  message?: TelegramMessage;
  inline_message_id?: string;
  data?: string;
}

export interface TelegramUpdate {
  update_id: number;
  message?: TelegramMessage;
  callback_query?: TelegramCallbackQuery;
}

export interface TelegramServiceOptions {
  supabaseClient?: SupabaseClient | null;
  botToken?: string;
  webhookSecret?: string;
}

export interface ProcessTelegramResult {
  handled: boolean;
  action?: string;
  replyText?: string;
  outcome?: string;
}

export class TelegramService {
  private supabase: SupabaseClient | null;
  private botToken: string;
  private webhookSecret: string;

  constructor(options: TelegramServiceOptions = {}) {
    this.supabase = options.supabaseClient || null;
    this.botToken = options.botToken || process.env.TELEGRAM_BOT_TOKEN || "";
    this.webhookSecret = options.webhookSecret || process.env.TELEGRAM_WEBHOOK_SECRET || "";
  }

  /**
   * Валидация секретного токена вебхука Telegram из заголовка X-Telegram-Bot-Api-Secret-Token
   */
  public verifyWebhookSecret(headerSecret: string | null | undefined): boolean {
    if (!this.webhookSecret) {
      return true; // Если секрет не настроен, пропускаем
    }
    return headerSecret === this.webhookSecret;
  }

  /**
   * Обработка апдейта от Telegram Webhook
   */
  public async processUpdate(update: TelegramUpdate): Promise<ProcessTelegramResult> {
    if (update.message?.text && update.message.chat?.id != null) {
      return this.handleTextMessage(update.message);
    }

    if (update.callback_query) {
      return this.handleCallbackQuery(update.callback_query);
    }

    return { handled: false };
  }

  private async handleTextMessage(message: TelegramMessage): Promise<ProcessTelegramResult> {
    const rawText = message.text?.trim() || "";
    const chatId = String(message.chat.id);

    // Команда /today
    if (/^\/today$/i.test(rawText)) {
      const result = await handleTodayCommand(chatId, "telegram", this.supabase);
      await this.sendMessage(chatId, result.message);
      return { handled: true, action: "today", replyText: result.message };
    }

    // Команда /start с кодом или без
    if (/^\/start/i.test(rawText)) {
      const parts = rawText.split(/\s+/);
      const code = parts[1];
      if (code) {
        return this.processLink(code, chatId);
      }

      const welcome =
        "👋 Привет! Я бот приложения MedTracker.\n\n" +
        "Чтобы получать напоминания о приёме лекарств сюда:\n" +
        "1. Откройте MedTracker на вашем компьютере или телефоне.\n" +
        "2. Перейдите в **Настройки → Мессенджеры**.\n" +
        "3. Нажмите **[Получить код Telegram]**.\n" +
        "4. Отправьте полученный 6-значный код мне сюда в чат или выполните команду `/link <код>`.\n\n" +
        "💡 Список приёмов на сегодня: `/today`";

      await this.sendMessage(chatId, welcome);
      return { handled: true, action: "start", replyText: welcome };
    }

    // Команда /link <код> или просто 6-значный код
    if (/^\/link/i.test(rawText) || /^[0-9A-Z]{6}$/i.test(rawText)) {
      const parts = rawText.split(/\s+/);
      const code = parts[0].startsWith("/") ? parts[1] : parts[0];
      return this.processLink(code, chatId);
    }

    return { handled: false };
  }

  private async processLink(code: string | undefined, chatId: string): Promise<ProcessTelegramResult> {
    if (!code) {
      const errorMsg = "❌ Укажите 6-значный код привязки: `/link <код>` (например: `/link A8F3K2`)";
      await this.sendMessage(chatId, errorMsg);
      return { handled: true, action: "link", replyText: errorMsg };
    }

    if (this.supabase) {
      const { data, error } = await this.supabase.rpc("confirm_messenger_link", {
        p_link_code: code.trim().toUpperCase(),
        p_channel_type: "telegram",
        p_chat_id: chatId,
        p_channel_id: null,
      });

      const reply =
        data?.outcome === "Applied"
          ? "✅ Telegram успешно привязан! Напоминания о приёме лекарств будут приходить в этот чат."
          : `❌ Код \`${code}\` не найден или его срок действия истёк. Сгенерируйте новый код в MedTracker (Настройки → Мессенджеры).`;

      await this.sendMessage(chatId, reply);
      return {
        handled: true,
        action: "link",
        outcome: data?.outcome || (error ? "Error" : "Rejected"),
        replyText: reply,
      };
    }

    const offlineReply = `✅ Код \`${code}\` принят (автономный режим).`;
    await this.sendMessage(chatId, offlineReply);
    return { handled: true, action: "link", replyText: offlineReply };
  }

  /**
   * Обработка Callback Query от inline кнопок под напоминаниями:
   * - take:<id> или dose:taken:<id>
   * - skip:<id> или dose:skip:<id>
   * - snooze:<id>:15 или dose:snooze:<id>
   */
  private async handleCallbackQuery(callback: TelegramCallbackQuery): Promise<ProcessTelegramResult> {
    const data = callback.data || "";
    const callbackId = callback.id;
    const chatId = callback.message?.chat?.id ? String(callback.message.chat.id) : null;
    const messageId = callback.message?.message_id;

    const parsed = this.parseCallbackData(data);
    if (!parsed) {
      await this.answerCallback(callbackId, "Некорректное действие");
      return { handled: false };
    }

    const { action, doseEventId } = parsed;

    if (action === "snooze") {
      if (this.supabase) {
        await this.supabase.rpc("snooze_notification_delivery", {
          p_dose_event_id: doseEventId,
          p_channel_type: "telegram",
          p_minutes: 15,
        });
      }

      const ackText = "⏰ Напомню через 15 минут";
      const newText = formatSnoozedText(15);
      await this.answerCallback(callbackId, ackText);

      if (chatId && messageId) {
        await this.editMessageText(chatId, messageId, newText);
      }

      return { handled: true, action: "snooze", outcome: "Applied", replyText: newText };
    }

    const isTaken = action === "take" || action === "taken";
    const rpcName = isTaken ? "confirm_dose" : "skip_dose";
    const nowUtc = new Date().toISOString();

    let outcome = "Applied";
    if (this.supabase) {
      const { data: rpcData } = await this.supabase.rpc(rpcName, {
        p_dose_event_id: doseEventId,
        p_source: "Telegram",
        ...(isTaken ? { p_taken_at: nowUtc } : { p_skipped_at: nowUtc }),
      });
      outcome = rpcData?.outcome || "Applied";
    }

    const timeFormatted = nowUtc.slice(11, 16);
    const newText = isTaken ? formatTakenText(timeFormatted) : formatSkippedText();
    const ackText = outcome === "Rejected" ? "⚠️ Уже обработано ранее" : (isTaken ? `Принято в ${timeFormatted} ✅` : "Пропущено ❌");

    await this.answerCallback(callbackId, ackText);

    if (chatId && messageId) {
      await this.editMessageText(chatId, messageId, newText);
    }

    return { handled: true, action: isTaken ? "take" : "skip", outcome, replyText: newText };
  }

  private parseCallbackData(data: string): { action: "take" | "taken" | "skip" | "snooze"; doseEventId: string } | null {
    // 1. Формат take:<id>, skip:<id>, snooze:<id>:15
    const shortMatch = /^(take|skip|snooze):([0-9a-f-]{36})(?::\d+)?$/i.exec(data);
    if (shortMatch) {
      return {
        action: shortMatch[1].toLowerCase() as any,
        doseEventId: shortMatch[2],
      };
    }

    // 2. Формат dose:(taken|skip|snooze):<id>
    const longMatch = /^dose:(taken|skip|snooze):([0-9a-f-]{36})$/i.exec(data);
    if (longMatch) {
      return {
        action: longMatch[1].toLowerCase() as any,
        doseEventId: longMatch[2],
      };
    }

    return null;
  }

  private async sendMessage(chatId: string, text: string): Promise<void> {
    if (!this.botToken) return;
    try {
      await fetch(`https://api.telegram.org/bot${this.botToken}/sendMessage`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ chat_id: chatId, text, parse_mode: "Markdown" }),
      });
    } catch (err) {
      console.error("Telegram sendMessage error:", err);
    }
  }

  private async answerCallback(callbackId: string, text: string): Promise<void> {
    if (!this.botToken) return;
    try {
      await fetch(`https://api.telegram.org/bot${this.botToken}/answerCallbackQuery`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ callback_query_id: callbackId, text }),
      });
    } catch (err) {
      console.error("Telegram answerCallbackQuery error:", err);
    }
  }

  private async editMessageText(chatId: string, messageId: number, text: string): Promise<void> {
    if (!this.botToken) return;
    try {
      await fetch(`https://api.telegram.org/bot${this.botToken}/editMessageText`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          chat_id: chatId,
          message_id: messageId,
          text,
          reply_markup: { inline_keyboard: [] },
        }),
      });
    } catch (err) {
      console.error("Telegram editMessageText error:", err);
    }
  }
}
