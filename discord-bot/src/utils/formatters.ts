export interface DoseAgendaItem {
  id: string;
  scheduledAt: string;
  time: string;
  medicationName: string;
  dosage?: string;
  unit?: string;
  amount?: number;
  state: string;
}

export function formatTakenText(timeStr?: string): string {
  if (timeStr) {
    return `Принято в ${timeStr} ✅`;
  }
  const now = new Date();
  const hours = String(now.getUTCHours()).padStart(2, "0");
  const mins = String(now.getUTCMinutes()).padStart(2, "0");
  return `Принято в ${hours}:${mins} ✅`;
}

export function formatSkippedText(): string {
  return "Пропущено ❌";
}

export function formatSnoozedText(minutes: number = 15): string {
  return `⏳ Отложено на ${minutes} минут`;
}

export function describeDoseState(state: string): string {
  switch (state) {
    case "Taken":
      return "✅ Принято";
    case "Skipped":
      return "❌ Пропущено";
    case "Scheduled":
      return "⏳ Запланировано";
    case "Notified":
      return "🔔 Напоминание отправлено";
    case "Missed":
      return "⚠️ Просрочено";
    case "Cancelled":
      return "⛔ Отменено";
    default:
      return state;
  }
}

export function formatTodayAgenda(items: DoseAgendaItem[], dateStr: string): string {
  if (items.length === 0) {
    return `📅 **Напоминания на сегодня (${dateStr}):**\n\nПриёмов лекарств на сегодня не запланировано. Отличного дня! 🎉`;
  }

  const takenCount = items.filter((i) => i.state === "Taken").length;
  const lines = items.map((item) => {
    const details: string[] = [];
    if (item.amount && item.unit) {
      details.push(`${item.amount} ${item.unit}`);
    }
    if (item.dosage) {
      details.push(item.dosage);
    }
    const detailsStr = details.length > 0 ? ` (${details.join(" · ")})` : "";
    return `▫️ **${item.time}** — ${item.medicationName}${detailsStr} — ${describeDoseState(item.state)}`;
  });

  return (
    `📅 **Напоминания на сегодня (${dateStr}):**\n\n` +
    lines.join("\n") +
    `\n\n📊 *Прогресс: принято ${takenCount} из ${items.length}*`
  );
}
