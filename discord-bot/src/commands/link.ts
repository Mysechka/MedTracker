import type { SupabaseClient } from "@supabase/supabase-js";

/**
 * Регулярное выражение для 6-значного буквенно-цифрового кода привязки MedTracker
 */
export const LINK_CODE_REGEX = /^[0-9A-Z]{6}$/i;

/**
 * Валидация формата кода привязки (ровно 6 символов, латиница и цифры).
 * @param code
 * @returns {boolean}
 */
export function isValidLinkCode(code: string | null | undefined): boolean {
  if (!code || typeof code !== "string") return false;
  return LINK_CODE_REGEX.test(code.trim());
}

export interface LinkCommandResult {
  success: boolean;
  message: string;
}

/**
 * Обработка команды привязки канала Discord к аккаунту MedTracker.
 * @param rawCode - 6-значный код привязки
 * @param channelId - ID канала Discord
 * @param supabaseClient - Опциональный клиент Supabase
 * @returns {Promise<LinkCommandResult>}
 */
export async function handleLinkCommand(
  rawCode: string | null | undefined,
  channelId: string | null | undefined,
  supabaseClient?: SupabaseClient | null,
): Promise<LinkCommandResult> {
  const code = (rawCode || "").trim().toUpperCase();

  if (!isValidLinkCode(code)) {
    return {
      success: false,
      message: "❌ Некорректный формат кода. Код привязки должен состоять ровно из 6 символов (например: `A8F3K2`).\nИспользование: `/link <код>` или `!link <код>`",
    };
  }

  if (!channelId) {
    return {
      success: false,
      message: "❌ Не удалось определить ID канала для привязки.",
    };
  }

  if (supabaseClient) {
    try {
      const { data, error } = await supabaseClient.rpc("confirm_messenger_link", {
        p_link_code: code,
        p_channel_type: "discord",
        p_chat_id: null,
        p_channel_id: channelId,
      });

      if (error) {
        console.error("Supabase RPC confirm_messenger_link error:", error);
        return {
          success: false,
          message: "❌ Ошибка сервера при подтверждении кода привязки. Попробуйте позже.",
        };
      }

      if (data?.outcome === "Applied") {
        return {
          success: true,
          message: `✅ Discord успешно привязан к MedTracker! Код \`${code}\` подтвержден. Напоминания о приёме лекарств будут приходить в этот канал.`,
        };
      }

      return {
        success: false,
        message: `❌ Код \`${code}\` не найден или его срок действия истёк. Сгенерируйте новый 6-значный код в MedTracker (Настройки → Мессенджеры).`,
      };
    } catch (err) {
      console.error("Link RPC invocation error:", err);
      return {
        success: false,
        message: "❌ Ошибка соединения с базой данных MedTracker.",
      };
    }
  }

  // Автономный режим ответа
  return {
    success: true,
    message: `✅ Код \`${code}\` принят в обработку для канала \`${channelId}\`.`,
  };
}
