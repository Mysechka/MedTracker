import { test, describe } from "node:test";
import assert from "node:assert/strict";
import type { SupabaseClient } from "@supabase/supabase-js";
import { TelegramService, type TelegramUpdate } from "../src/services/telegram.js";

describe("Telegram Bot & Webhook Tests", () => {
  describe("Webhook Secret Token Verification", () => {
    test("секретный токен совпадает — проверка успешна", () => {
      const service = new TelegramService({ webhookSecret: "super_secret_token_123" });
      assert.equal(service.verifyWebhookSecret("super_secret_token_123"), true);
    });

    test("неверный секретный токен — отказ (401)", () => {
      const service = new TelegramService({ webhookSecret: "super_secret_token_123" });
      assert.equal(service.verifyWebhookSecret("wrong_token"), false);
      assert.equal(service.verifyWebhookSecret(undefined), false);
    });

    test("если секрет не настроен — пропускает любые запросы", () => {
      const service = new TelegramService({ webhookSecret: "" });
      assert.equal(service.verifyWebhookSecret(undefined), true);
      assert.equal(service.verifyWebhookSecret("any_token"), true);
    });
  });

  describe("Command /link in Telegram", () => {
    test("валидный 6-значный код успешно привязывает Telegram", async () => {
      let rpcCalled = false;
      const mockSupabase = {
        rpc: async (fn: string, params: any) => {
          assert.equal(fn, "confirm_messenger_link");
          assert.equal(params.p_link_code, "A8F3K2");
          assert.equal(params.p_channel_type, "telegram");
          assert.equal(params.p_chat_id, "987654321");
          rpcCalled = true;
          return { data: { outcome: "Applied" }, error: null };
        },
      } as unknown as SupabaseClient;

      const service = new TelegramService({ supabaseClient: mockSupabase });
      const update: TelegramUpdate = {
        update_id: 1,
        message: {
          message_id: 100,
          chat: { id: 987654321, type: "private" },
          text: "/link A8F3K2",
        },
      };

      const result = await service.processUpdate(update);
      assert.equal(result.handled, true);
      assert.equal(result.action, "link");
      assert.equal(result.outcome, "Applied");
      assert.match(result.replyText!, /Telegram успешно привязан/);
      assert.equal(rpcCalled, true);
    });

    test("просроченный или ненайденный код возвращает сообщение об ошибке", async () => {
      const mockSupabase = {
        rpc: async () => ({ data: { outcome: "Rejected" }, error: null }),
      } as unknown as SupabaseClient;

      const service = new TelegramService({ supabaseClient: mockSupabase });
      const update: TelegramUpdate = {
        update_id: 2,
        message: {
          message_id: 101,
          chat: { id: 987654321, type: "private" },
          text: "A8F3K2", // ввод только кода
        },
      };

      const result = await service.processUpdate(update);
      assert.equal(result.handled, true);
      assert.equal(result.outcome, "Rejected");
      assert.match(result.replyText!, /не найден или его срок действия истёк/);
    });
  });

  describe("Command /today in Telegram", () => {
    test("непривязанный чат получает предупреждение и инструкцию", async () => {
      const mockSupabase = {
        from: (table: string) => ({
          select: () => ({
            eq: () => ({
              eq: () => ({
                eq: () => ({
                  maybeSingle: async () => ({ data: null, error: null }),
                }),
              }),
            }),
          }),
        }),
      } as unknown as SupabaseClient;

      const service = new TelegramService({ supabaseClient: mockSupabase });
      const update: TelegramUpdate = {
        update_id: 3,
        message: {
          message_id: 102,
          chat: { id: 987654321, type: "private" },
          text: "/today",
        },
      };

      const result = await service.processUpdate(update);
      assert.equal(result.handled, true);
      assert.equal(result.action, "today");
      assert.match(result.replyText!, /не привязан к аккаунту MedTracker/);
    });

    test("привязанный чат получает список сегодняшних лекарств со статусами", async () => {
      const userId = "11111111-1111-1111-1111-111111111111";
      const courseId = "22222222-2222-2222-2222-222222222222";
      const scheduleId = "33333333-3333-3333-3333-333333333333";
      const medId = "44444444-4444-4444-4444-444444444444";

      const mockSupabase = {
        from: (table: string) => {
          if (table === "messenger_links") {
            return {
              select: () => ({
                eq: () => ({
                  eq: () => ({
                    eq: () => ({
                      maybeSingle: async () => ({ data: { user_id: userId, is_confirmed: true }, error: null }),
                    }),
                  }),
                }),
              }),
            };
          }
          if (table === "profiles") {
            return {
              select: () => ({
                eq: () => ({
                  maybeSingle: async () => ({ data: { time_zone_id: "UTC" }, error: null }),
                }),
              }),
            };
          }
          if (table === "dose_events") {
            return {
              select: () => ({
                eq: () => ({
                  eq: () => ({
                    order: async () => ({
                      data: [
                        {
                          id: "dose-1",
                          scheduled_at: "2026-08-27T08:00:00Z",
                          state: "Taken",
                          course_id: courseId,
                          schedule_id: scheduleId,
                        },
                        {
                          id: "dose-2",
                          scheduled_at: "2026-08-27T12:00:00Z",
                          state: "Scheduled",
                          course_id: courseId,
                          schedule_id: scheduleId,
                        },
                      ],
                      error: null,
                    }),
                  }),
                }),
              }),
            };
          }
          if (table === "courses") {
            return {
              select: () => ({
                in: async () => ({ data: [{ id: courseId, medication_id: medId }], error: null }),
              }),
            };
          }
          if (table === "schedules") {
            return {
              select: () => ({
                in: async () => ({ data: [{ id: scheduleId, dose_amount: 1 }], error: null }),
              }),
            };
          }
          if (table === "medications") {
            return {
              select: () => ({
                in: async () => ({
                  data: [{ id: medId, name: "Магний B6", dosage: "500 мг", unit: "таб" }],
                  error: null,
                }),
              }),
            };
          }
          throw new Error(`Unexpected table ${table}`);
        },
      } as unknown as SupabaseClient;

      const service = new TelegramService({ supabaseClient: mockSupabase });
      const update: TelegramUpdate = {
        update_id: 4,
        message: {
          message_id: 103,
          chat: { id: 987654321, type: "private" },
          text: "/today",
        },
      };

      const result = await service.processUpdate(update);
      assert.equal(result.handled, true);
      assert.match(result.replyText!, /Магний B6/);
      assert.match(result.replyText!, /Принято/);
      assert.match(result.replyText!, /Запланировано/);
      assert.match(result.replyText!, /Прогресс: принято 1 из 2/);
    });
  });

  describe("Inline Buttons (take, skip, snooze)", () => {
    const doseId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    test("кнопка 'take:<id>' вызывает confirm_dose и формирует текст 'Принято в HH:mm ✅'", async () => {
      let rpcCalled = false;
      const mockSupabase = {
        rpc: async (fn: string, params: any) => {
          assert.equal(fn, "confirm_dose");
          assert.equal(params.p_dose_event_id, doseId);
          assert.equal(params.p_source, "Telegram");
          assert.ok(params.p_taken_at);
          rpcCalled = true;
          return { data: { outcome: "Applied", state: "Taken" }, error: null };
        },
      } as unknown as SupabaseClient;

      const service = new TelegramService({ supabaseClient: mockSupabase });
      const update: TelegramUpdate = {
        update_id: 5,
        callback_query: {
          id: "cb_1",
          from: { id: 123 },
          data: `take:${doseId}`,
          message: {
            message_id: 200,
            chat: { id: 987654321, type: "private" },
          },
        },
      };

      const result = await service.processUpdate(update);
      assert.equal(result.handled, true);
      assert.equal(result.action, "take");
      assert.equal(result.outcome, "Applied");
      assert.match(result.replyText!, /Принято в \d\d:\d\d ✅/);
      assert.equal(rpcCalled, true);
    });

    test("кнопка 'skip:<id>' вызывает skip_dose и формирует текст 'Пропущено ❌'", async () => {
      let rpcCalled = false;
      const mockSupabase = {
        rpc: async (fn: string, params: any) => {
          assert.equal(fn, "skip_dose");
          assert.equal(params.p_dose_event_id, doseId);
          assert.equal(params.p_source, "Telegram");
          assert.ok(params.p_skipped_at);
          rpcCalled = true;
          return { data: { outcome: "Applied", state: "Skipped" }, error: null };
        },
      } as unknown as SupabaseClient;

      const service = new TelegramService({ supabaseClient: mockSupabase });
      const update: TelegramUpdate = {
        update_id: 6,
        callback_query: {
          id: "cb_2",
          from: { id: 123 },
          data: `skip:${doseId}`,
          message: {
            message_id: 201,
            chat: { id: 987654321, type: "private" },
          },
        },
      };

      const result = await service.processUpdate(update);
      assert.equal(result.handled, true);
      assert.equal(result.action, "skip");
      assert.equal(result.replyText, "Пропущено ❌");
      assert.equal(rpcCalled, true);
    });

    test("кнопка 'snooze:<id>:15' вызывает snooze_notification_delivery и формирует '⏳ Отложено на 15 минут'", async () => {
      let rpcCalled = false;
      const mockSupabase = {
        rpc: async (fn: string, params: any) => {
          assert.equal(fn, "snooze_notification_delivery");
          assert.equal(params.p_dose_event_id, doseId);
          assert.equal(params.p_channel_type, "telegram");
          assert.equal(params.p_minutes, 15);
          rpcCalled = true;
          return { data: { outcome: "Applied" }, error: null };
        },
      } as unknown as SupabaseClient;

      const service = new TelegramService({ supabaseClient: mockSupabase });
      const update: TelegramUpdate = {
        update_id: 7,
        callback_query: {
          id: "cb_3",
          from: { id: 123 },
          data: `snooze:${doseId}:15`,
          message: {
            message_id: 202,
            chat: { id: 987654321, type: "private" },
          },
        },
      };

      const result = await service.processUpdate(update);
      assert.equal(result.handled, true);
      assert.equal(result.action, "snooze");
      assert.match(result.replyText!, /Отложено на 15 минут/);
      assert.equal(rpcCalled, true);
    });
  });
});
