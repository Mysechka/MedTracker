/** Общие типы Edge Functions MedTracker. Без внутренних id в текстах для пользователя. */

export type DoseEventState =
  | "Scheduled"
  | "Notified"
  | "Taken"
  | "Skipped"
  | "Missed"
  | "Cancelled";

export type NotificationChannelType = "telegram" | "discord_app" | "discord_webhook";

export type MessengerChannelType = "telegram" | "discord";

export interface DoseEventRow {
  id: string;
  user_id: string;
  course_id: string;
  schedule_id: string;
  scheduled_at: string;
  local_date: string;
  state: DoseEventState;
  taken_at: string | null;
  source: string | null;
}

export interface MedicationRow {
  id: string;
  name: string;
  form: string;
  dosage: string;
  unit: string;
}

export interface CourseRow {
  id: string;
  medication_id: string;
}

export interface MessengerLinkRow {
  id: string;
  user_id: string;
  channel_type: MessengerChannelType;
  chat_id: string | null;
  channel_id: string | null;
  is_confirmed: boolean;
  link_code: string | null;
}

export interface NotificationDeliveryRow {
  id: string;
  user_id: string;
  dose_event_id: string;
  channel_type: NotificationChannelType;
  message_id: string | null;
  status: "Pending" | "Sent" | "Failed" | "Edited";
  attempts: number;
  next_retry_at: string | null;
  last_error: string | null;
}

export interface SendReminderInput {
  deliveryId: string;
  doseEventId: string;
  medicationName: string;
  dosageLabel: string;
  scheduledAtIso: string;
  /** Telegram chat_id или Discord channel_id */
  targetId: string;
}

export interface SendReminderResult {
  ok: boolean;
  messageId?: string;
  retryAfterSeconds?: number;
  error?: string;
}

export interface EditReminderInput {
  messageId: string;
  targetId: string;
  text: string;
}

export interface EditReminderResult {
  ok: boolean;
  error?: string;
}

/** Канал доставки уведомлений. Реализации: Telegram / Discord App / Discord Webhook. */
export interface INotificationChannel {
  readonly channelType: NotificationChannelType;
  sendReminder(input: SendReminderInput): Promise<SendReminderResult>;
  editReminder(input: EditReminderInput): Promise<EditReminderResult>;
}

export function formatDoseLabel(med: MedicationRow): string {
  return `${med.name} · ${med.dosage} ${med.unit}`.trim();
}

export function formatTakenText(localTime: string): string {
  return `Принято в ${localTime}`;
}

export function formatSkippedText(): string {
  return "Пропущено";
}

export function formatMissedText(): string {
  return "Пропущено по времени";
}

export function formatReminderText(
  medicationName: string,
  dosageLabel: string,
  scheduledLocal: string,
): string {
  return `Пора принять: ${medicationName}\n${dosageLabel}\nВремя: ${scheduledLocal}`;
}

export function exponentialBackoffSeconds(attempts: number): number {
  const capped = Math.min(Math.max(attempts, 1), 8);
  return Math.min(60 * 2 ** (capped - 1), 3600);
}

export function requireEnv(name: string): string {
  const value = Deno.env.get(name);
  if (!value || value.trim().length === 0) {
    throw new Error(`Missing env ${name}`);
  }
  return value.trim();
}

export function optionalEnv(name: string): string | undefined {
  const value = Deno.env.get(name);
  if (!value || value.trim().length === 0) {
    return undefined;
  }
  return value.trim();
}
