import type { SupabaseClient } from "@supabase/supabase-js";
import { formatTodayAgenda, type DoseAgendaItem } from "../utils/formatters.js";

export interface TodayCommandResult {
  success: boolean;
  message: string;
}

/**
 * Обработка команды /today для Telegram (chat_id) и Discord (channel_id).
 */
export async function handleTodayCommand(
  targetId: string | null | undefined,
  channelType: "telegram" | "discord",
  supabaseClient?: SupabaseClient | null,
): Promise<TodayCommandResult> {
  if (!targetId) {
    return {
      success: false,
      message: "❌ Не удалось определить идентификатор чата/канала.",
    };
  }

  if (!supabaseClient) {
    return {
      success: true,
      message: "📅 На сегодня приёмов лекарств не запланировано (автономный режим).",
    };
  }

  try {
    // 1. Поиск привязки мессенджера
    let query = supabaseClient
      .from("messenger_links")
      .select("user_id, is_confirmed")
      .eq("channel_type", channelType)
      .eq("is_confirmed", true);

    if (channelType === "telegram") {
      query = query.eq("chat_id", targetId);
    } else {
      query = query.eq("channel_id", targetId);
    }

    const { data: linkData, error: linkError } = await query.maybeSingle();

    if (linkError || !linkData) {
      return {
        success: false,
        message:
          `❌ Этот ${channelType === "telegram" ? "чат" : "канал"} не привязан к аккаунту MedTracker.\n` +
          "Привяжите его командой `/link <код>`, получив 6-значный код в приложении (Настройки → Мессенджеры).",
      };
    }

    const userId = linkData.user_id;

    // 2. Получение часового пояса профиля
    const { data: profile } = await supabaseClient
      .from("profiles")
      .select("time_zone_id")
      .eq("id", userId)
      .maybeSingle();

    const timeZone = profile?.time_zone_id || "UTC";

    // 3. Вычисление сегодняшней локальной даты в таймзоне пользователя
    const now = new Date();
    const localDateStr = getLocalDateString(now, timeZone);

    // 4. Загрузка dose_events на сегодня
    const { data: doseEvents, error: doseError } = await supabaseClient
      .from("dose_events")
      .select("id, scheduled_at, state, course_id, schedule_id")
      .eq("user_id", userId)
      .eq("local_date", localDateStr)
      .order("scheduled_at", { ascending: true });

    if (doseError) {
      console.error("Error fetching dose_events for /today:", doseError);
      return {
        success: false,
        message: "❌ Не удалось загрузить расписание на сегодня. Попробуйте позже.",
      };
    }

    if (!doseEvents || doseEvents.length === 0) {
      return {
        success: true,
        message: formatTodayAgenda([], localDateStr),
      };
    }

    // 5. Загрузка курсов, лекарств и расписаний
    const courseIds = [...new Set(doseEvents.map((d) => d.course_id))];
    const scheduleIds = [...new Set(doseEvents.map((d) => d.schedule_id))];

    const [{ data: courses }, { data: schedules }] = await Promise.all([
      supabaseClient.from("courses").select("id, medication_id").in("id", courseIds),
      supabaseClient.from("schedules").select("id, dose_amount").in("id", scheduleIds),
    ]);

    const medicationIds = [...new Set((courses || []).map((c) => c.medication_id))];
    const { data: medications } = await supabaseClient
      .from("medications")
      .select("id, name, dosage, unit")
      .in("id", medicationIds);

    const courseMap = new Map((courses || []).map((c) => [c.id, c]));
    const scheduleMap = new Map((schedules || []).map((s) => [s.id, s]));
    const medMap = new Map((medications || []).map((m) => [m.id, m]));

    const items: DoseAgendaItem[] = doseEvents.map((event) => {
      const course = courseMap.get(event.course_id);
      const med = course ? medMap.get(course.medication_id) : null;
      const sched = scheduleMap.get(event.schedule_id);

      const scheduledDate = new Date(event.scheduled_at);
      const timeFormatted = formatTimeInTimeZone(scheduledDate, timeZone);

      return {
        id: event.id,
        scheduledAt: event.scheduled_at,
        time: timeFormatted,
        medicationName: med?.name || "Лекарство",
        dosage: med?.dosage,
        unit: med?.unit,
        amount: sched?.dose_amount,
        state: event.state,
      };
    });

    return {
      success: true,
      message: formatTodayAgenda(items, localDateStr),
    };
  } catch (err) {
    console.error("handleTodayCommand error:", err);
    return {
      success: false,
      message: "❌ Произошла непредвиденная ошибка при получении расписания.",
    };
  }
}

function getLocalDateString(date: Date, timeZone: string): string {
  try {
    const formatter = new Intl.DateTimeFormat("en-CA", {
      timeZone,
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
    });
    return formatter.format(date); // returns YYYY-MM-DD
  } catch {
    return date.toISOString().slice(0, 10);
  }
}

function formatTimeInTimeZone(date: Date, timeZone: string): string {
  try {
    const formatter = new Intl.DateTimeFormat("ru-RU", {
      timeZone,
      hour: "2-digit",
      minute: "2-digit",
      hour12: false,
    });
    return formatter.format(date);
  } catch {
    const h = String(date.getUTCHours()).padStart(2, "0");
    const m = String(date.getUTCMinutes()).padStart(2, "0");
    return `${h}:${m}`;
  }
}
