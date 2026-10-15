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
    const nowIso = now.toISOString();

    const [materialized, missed] = await Promise.all([
      rpcJson<number>(client, "materialize_upcoming_doses", {
        p_horizon_days: 14,
        p_user_id: null,
        p_now: nowIso,
      }),
      rpcJson<number>(client, "mark_missed_doses", {
        p_now: nowIso,
      }),
    ]);

    const { data: dueEvents, error: dueError } = await client
      .from("dose_events")
      .select("*")
      .in("state", ["Scheduled", "Notified"])
      .lte("scheduled_at", nowIso)
      .order("scheduled_at", { ascending: true })
      .limit(200);

    if (dueError) {
      throw new Error(dueError.message);
    }

    const events = (dueEvents ?? []) as DoseEventRow[];
    let sent = 0;
    let failed = 0;

    if (events.length > 0) {
      const userIds = [...new Set(events.map((e) => e.user_id))];
      const courseIds = [...new Set(events.map((e) => e.course_id))];

      const [{ data: allLinks, error: linksError }, { data: allCourses, error: coursesError }] =
        await Promise.all([
          client
            .from("messenger_links")
            .select("*")
            .in("user_id", userIds)
            .eq("is_confirmed", true),
          client
            .from("courses")
            .select("id, medication_id")
            .in("id", courseIds),
        ]);

      if (linksError) throw new Error(linksError.message);
      if (coursesError) throw new Error(coursesError.message);

      const courses = (allCourses ?? []) as CourseRow[];
      const medIds = [...new Set(courses.map((c) => c.medication_id))];
      const { data: allMeds, error: medsError } = medIds.length > 0
        ? await client
          .from("medications")
          .select("id, name, form, dosage, unit")
          .in("id", medIds)
        : { data: [], error: null };

      if (medsError) throw new Error(medsError.message);

      const linksByUser = new Map<string, MessengerLinkRow[]>();
      for (const link of (allLinks ?? []) as MessengerLinkRow[]) {
        const list = linksByUser.get(link.user_id) ?? [];
        list.push(link);
        linksByUser.set(link.user_id, list);
      }

      const medById = new Map<string, MedicationRow>(
        ((allMeds ?? []) as MedicationRow[]).map((m) => [m.id, m]),
      );
      const medByCourseId = new Map<string, MedicationRow>();
      for (const c of courses) {
        const med = medById.get(c.medication_id);
        if (med) medByCourseId.set(c.id, med);
      }

      for (const event of events) {
        const med = medByCourseId.get(event.course_id);
        const links = linksByUser.get(event.user_id);
        if (!med || !links || links.length === 0) {
          continue;
        }

        let anySent = false;

        for (const link of links) {
          for (const resolved of resolveChannelsForLink(link)) {
            const delivery = await rpcJson<NotificationDeliveryRow | null>(
              client,
              "claim_notification_delivery",
              {
                p_dose_event_id: event.id,
                p_user_id: event.user_id,
                p_channel_type: resolved.channelType,
                p_now: nowIso,
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
    }

    const edited = await syncTerminalMessages(client, now);

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

  if (error || !deliveries || deliveries.length === 0) {
    if (error) console.error("syncTerminalMessages", error.message);
    return 0;
  }

  const rows = deliveries as Array<NotificationDeliveryRow & {
    dose_events: Pick<DoseEventRow, "id" | "state" | "taken_at" | "user_id">;
  }>;

  const userIds = [...new Set(rows.map((r) => r.user_id))];
  const { data: allLinks } = await client
    .from("messenger_links")
    .select("*")
    .in("user_id", userIds);

  const linkMap = new Map<string, MessengerLinkRow>();
  for (const link of (allLinks ?? []) as MessengerLinkRow[]) {
    linkMap.set(`${link.user_id}:${link.channel_type}`, link);
  }

  let edited = 0;

  for (const row of rows) {
    if (!row.message_id) {
      continue;
    }

    const channel = channelByType(row.channel_type);
    if (!channel) {
      continue;
    }

    const linkType = row.channel_type === "telegram" ? "telegram" : "discord";
    const link = linkMap.get(`${row.user_id}:${linkType}`) ?? null;
    const targetId = resolveTarget(row.channel_type, link);
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
