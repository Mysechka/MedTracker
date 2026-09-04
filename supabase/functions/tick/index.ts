import { resolveChannelsForLink, channelByType } from "../_shared/channels/index.ts";
import { createServiceClient, rpcJson } from "../_shared/supabase.ts";
import type {
  CourseRow,
  DoseEventRow,
  MedicationRow,
  MessengerLinkRow,
  NotificationDeliveryRow,
} from "../_shared/types.ts";
import {
  exponentialBackoffSeconds,
  formatMissedText,
  formatSkippedText,
  formatTakenText,
} from "../_shared/types.ts";

Deno.serve(async (req) => {
  if (req.method !== "POST" && req.method !== "GET") {
    return new Response("Method not allowed", { status: 405 });
  }

  const authHeader = req.headers.get("Authorization");
  if (!authHeader || !authHeader.startsWith("Bearer ")) {
    return new Response("Unauthorized", { status: 401 });
  }

  try {
    const client = createServiceClient();
    const now = new Date();

    const materialized = await rpcJson<number>(client, "materialize_upcoming_doses", {
      p_horizon_days: 14,
      p_user_id: null,
      p_now: now.toISOString(),
    });

    const missed = await rpcJson<number>(client, "mark_missed_doses", {
      p_now: now.toISOString(),
    });

    // Due: Scheduled и время пришло; плюс Pending/Failed с next_retry_at.
    const { data: dueEvents, error: dueError } = await client
      .from("dose_events")
      .select("*")
      .in("state", ["Scheduled", "Notified"])
      .lte("scheduled_at", now.toISOString())
      .order("scheduled_at", { ascending: true })
      .limit(200);

    if (dueError) {
      throw new Error(dueError.message);
    }

    let sent = 0;
    let failed = 0;
    let edited = 0;

    for (const event of (dueEvents ?? []) as DoseEventRow[]) {
      const { data: links, error: linksError } = await client
        .from("messenger_links")
        .select("*")
        .eq("user_id", event.user_id)
        .eq("is_confirmed", true);

      if (linksError) {
        throw new Error(linksError.message);
      }

      const med = await loadMedication(client, event.course_id);
      if (!med) {
        continue;
      }

      let anySent = false;

      for (const link of (links ?? []) as MessengerLinkRow[]) {
        for (const resolved of resolveChannelsForLink(link)) {
          const delivery = await rpcJson<NotificationDeliveryRow | null>(
            client,
            "claim_notification_delivery",
            {
              p_dose_event_id: event.id,
              p_user_id: event.user_id,
              p_channel_type: resolved.channelType,
              p_now: now.toISOString(),
            },
          );

          if (!delivery) {
            continue;
          }

          const result = await resolved.channel.sendReminder({
            deliveryId: delivery.id,
            doseEventId: event.id,
            medicationName: med.name,
            dosageLabel: `${med.dosage} ${med.unit}`,
            scheduledAtIso: event.scheduled_at,
            targetId: resolved.targetId,
          });

          if (result.ok && result.messageId) {
            await rpcJson(client, "complete_notification_delivery", {
              p_delivery_id: delivery.id,
              p_message_id: result.messageId,
              p_status: "Sent",
            });
            sent += 1;
            anySent = true;
          } else {
            const retrySeconds = result.retryAfterSeconds ??
              exponentialBackoffSeconds(delivery.attempts);
            const nextRetry = new Date(now.getTime() + retrySeconds * 1000);
            await rpcJson(client, "fail_notification_delivery", {
              p_delivery_id: delivery.id,
              p_error: result.error ?? "send failed",
              p_next_retry_at: nextRetry.toISOString(),
            });
            failed += 1;
          }
        }
      }

      if (anySent && event.state === "Scheduled") {
        await rpcJson(client, "mark_dose_notified", {
          p_dose_event_id: event.id,
          p_source: "System",
        });
      }
    }

    // Синхронизация текстов в мессенджерах для терминальных событий.
    edited += await syncTerminalMessages(client, now);

    return Response.json({
      ok: true,
      materialized,
      missed,
      sent,
      failed,
      edited,
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error("tick failed", message);
    return Response.json({ ok: false, error: message }, { status: 500 });
  }
});

async function loadMedication(
  // deno-lint-ignore no-explicit-any
  client: any,
  courseId: string,
): Promise<MedicationRow | null> {
  const { data: course, error: courseError } = await client
    .from("courses")
    .select("id, medication_id")
    .eq("id", courseId)
    .maybeSingle();

  if (courseError || !course) {
    return null;
  }

  const { data: med, error: medError } = await client
    .from("medications")
    .select("id, name, form, dosage, unit")
    .eq("id", (course as CourseRow).medication_id)
    .maybeSingle();

  if (medError || !med) {
    return null;
  }

  return med as MedicationRow;
}

async function syncTerminalMessages(
  // deno-lint-ignore no-explicit-any
  client: any,
  _now: Date,
): Promise<number> {
  const { data: deliveries, error } = await client
    .from("notification_deliveries")
    .select("*, dose_events!inner(id, state, taken_at, user_id)")
    .eq("status", "Sent")
    .in("dose_events.state", ["Taken", "Skipped", "Missed", "Cancelled"])
    .limit(100);

  if (error || !deliveries) {
    if (error) console.error("syncTerminalMessages", error.message);
    return 0;
  }

  let edited = 0;

  for (const row of deliveries as Array<NotificationDeliveryRow & {
    dose_events: Pick<DoseEventRow, "id" | "state" | "taken_at" | "user_id">;
  }>) {
    if (!row.message_id) {
      continue;
    }

    const channel = channelByType(row.channel_type);
    if (!channel) {
      continue;
    }

    const { data: link } = await client
      .from("messenger_links")
      .select("*")
      .eq("user_id", row.user_id)
      .eq(
        "channel_type",
        row.channel_type === "telegram" ? "telegram" : "discord",
      )
      .maybeSingle();

    const targetId = resolveTarget(row.channel_type, link as MessengerLinkRow | null);
    if (!targetId) {
      continue;
    }

    const text = terminalText(row.dose_events.state, row.dose_events.taken_at);
    const result = await channel.editReminder({
      messageId: row.message_id,
      targetId,
      text,
    });

    if (result.ok) {
      await rpcJson(client, "complete_notification_delivery", {
        p_delivery_id: row.id,
        p_message_id: row.message_id,
        p_status: "Edited",
      });
      edited += 1;
    }
  }

  return edited;
}

function resolveTarget(
  channelType: string,
  link: MessengerLinkRow | null,
): string | null {
  if (!link) {
    return channelType === "discord_webhook" ? "webhook" : null;
  }
  if (channelType === "telegram") {
    return link.chat_id;
  }
  if (channelType === "discord_app") {
    return link.channel_id;
  }
  return link.channel_id ?? "webhook";
}

function terminalText(state: string, takenAt: string | null): string {
  if (state === "Taken") {
    const time = takenAt
      ? new Date(takenAt).toISOString().slice(11, 16)
      : "--:--";
    return formatTakenText(time);
  }
  if (state === "Skipped") {
    return formatSkippedText();
  }
  if (state === "Missed") {
    return formatMissedText();
  }
  return "Отменено";
}
