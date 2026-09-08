import { test, describe } from "node:test";
import assert from "node:assert/strict";
import type { SupabaseClient } from "@supabase/supabase-js";
import {
  DiscordInteractionsService,
  DiscordInteractionType,
  DiscordCallbackType,
  type DiscordInteraction,
} from "../src/services/discord-interactions.js";
import { verifyDiscordSignature, createTestEd25519KeyPair } from "../src/utils/crypto.js";

describe("Discord Interactions REST API Tests", () => {
  describe("Ed25519 Cryptographic Signature Verification", () => {
    const keyPair = createTestEd25519KeyPair();
    const timestamp = "1700000000";
    const body = JSON.stringify({ type: 1 });

    test("валидная Ed25519 подпись успешно проходит проверку", () => {
      const validSig = keyPair.sign(timestamp, body);
      const isValid = verifyDiscordSignature(keyPair.publicKeyHex, validSig, timestamp, body);
      assert.equal(isValid, true);
    });

    test("невалидная подпись отклоняется (401 Unauthorized)", () => {
      const invalidSig = "0".repeat(128);
      const isValid = verifyDiscordSignature(keyPair.publicKeyHex, invalidSig, timestamp, body);
      assert.equal(isValid, false);
    });

    test("измененное тело запроса не проходит валидацию подписи", () => {
      const validSig = keyPair.sign(timestamp, body);
      const tamperedBody = JSON.stringify({ type: 2 });
      const isValid = verifyDiscordSignature(keyPair.publicKeyHex, validSig, timestamp, tamperedBody);
      assert.equal(isValid, false);
    });

    test("измененный timestamp не проходит валидацию", () => {
      const validSig = keyPair.sign(timestamp, body);
      const isValid = verifyDiscordSignature(keyPair.publicKeyHex, validSig, "1700000001", body);
      assert.equal(isValid, false);
    });
  });

  describe("Interaction Type 1: PING -> PONG", () => {
    test("возвращает Type 1 (PONG)", async () => {
      const service = new DiscordInteractionsService();
      const interaction: DiscordInteraction = {
        id: "int_1",
        application_id: "app_1",
        type: DiscordInteractionType.PING,
        token: "tok_1",
        version: 1,
      };

      const response = await service.handleInteraction(interaction);
      assert.equal(response.type, DiscordCallbackType.PONG);
    });
  });

  describe("Interaction Type 2: Slash Commands (/link, /today)", () => {
    test("слэш-команда /link с валидным кодом подтверждает привязку", async () => {
      let rpcCalled = false;
      const mockSupabase = {
        rpc: async (fn: string, params: any) => {
          assert.equal(fn, "confirm_messenger_link");
          assert.equal(params.p_link_code, "A8F3K2");
          assert.equal(params.p_channel_type, "discord");
          assert.equal(params.p_channel_id, "chan_123");
          rpcCalled = true;
          return { data: { outcome: "Applied" }, error: null };
        },
      } as unknown as SupabaseClient;

      const service = new DiscordInteractionsService({ supabaseClient: mockSupabase });
      const interaction: DiscordInteraction = {
        id: "int_2",
        application_id: "app_1",
        type: DiscordInteractionType.APPLICATION_COMMAND,
        channel_id: "chan_123",
        data: {
          name: "link",
          options: [{ name: "code", value: "A8F3K2", type: 3 }],
        },
        token: "tok_2",
        version: 1,
      };

      const response = await service.handleInteraction(interaction);
      assert.equal(response.type, DiscordCallbackType.CHANNEL_MESSAGE_WITH_SOURCE);
      assert.match(response.data?.content!, /Discord успешно привязан/);
      assert.equal(rpcCalled, true);
    });

    test("слэш-команда /today выводит лекарства на сегодня", async () => {
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
                          scheduled_at: "2026-08-27T09:00:00Z",
                          state: "Taken",
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
                  data: [{ id: medId, name: "Омепразол", dosage: "20 мг", unit: "капс" }],
                  error: null,
                }),
              }),
            };
          }
          throw new Error(`Unexpected table ${table}`);
        },
      } as unknown as SupabaseClient;

      const service = new DiscordInteractionsService({ supabaseClient: mockSupabase });
      const interaction: DiscordInteraction = {
        id: "int_3",
        application_id: "app_1",
        type: DiscordInteractionType.APPLICATION_COMMAND,
        channel_id: "chan_123",
        data: {
          name: "today",
        },
        token: "tok_3",
        version: 1,
      };

      const response = await service.handleInteraction(interaction);
      assert.equal(response.type, DiscordCallbackType.CHANNEL_MESSAGE_WITH_SOURCE);
      assert.match(response.data?.content!, /Омепразол/);
      assert.match(response.data?.content!, /Принято/);
    });
  });

  describe("Interaction Type 3: Message Component Buttons (take, skip, snooze)", () => {
    const doseId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    test("кнопка 'Принял' (take:<id>) обновляет сообщение и удаляет кнопки (Type 7 UPDATE_MESSAGE)", async () => {
      let rpcCalled = false;
      const mockSupabase = {
        rpc: async (fn: string, params: any) => {
          assert.equal(fn, "confirm_dose");
          assert.equal(params.p_dose_event_id, doseId);
          assert.equal(params.p_source, "Discord");
          assert.ok(params.p_taken_at);
          rpcCalled = true;
          return { data: { outcome: "Applied", state: "Taken" }, error: null };
        },
      } as unknown as SupabaseClient;

      const service = new DiscordInteractionsService({ supabaseClient: mockSupabase });
      const interaction: DiscordInteraction = {
        id: "int_4",
        application_id: "app_1",
        type: DiscordInteractionType.MESSAGE_COMPONENT,
        channel_id: "chan_123",
        data: {
          custom_id: `take:${doseId}`,
          component_type: 2,
        },
        token: "tok_4",
        version: 1,
      };

      const response = await service.handleInteraction(interaction);
      assert.equal(response.type, DiscordCallbackType.UPDATE_MESSAGE);
      assert.match(response.data?.content!, /Принято в \d\d:\d\d ✅/);
      assert.deepEqual(response.data?.components, []);
      assert.equal(rpcCalled, true);
    });

    test("кнопка 'Пропустить' (skip:<id>) переводит в статус 'Пропущено ❌'", async () => {
      let rpcCalled = false;
      const mockSupabase = {
        rpc: async (fn: string, params: any) => {
          assert.equal(fn, "skip_dose");
          assert.equal(params.p_dose_event_id, doseId);
          assert.equal(params.p_source, "Discord");
          assert.ok(params.p_skipped_at);
          rpcCalled = true;
          return { data: { outcome: "Applied", state: "Skipped" }, error: null };
        },
      } as unknown as SupabaseClient;

      const service = new DiscordInteractionsService({ supabaseClient: mockSupabase });
      const interaction: DiscordInteraction = {
        id: "int_5",
        application_id: "app_1",
        type: DiscordInteractionType.MESSAGE_COMPONENT,
        channel_id: "chan_123",
        data: {
          custom_id: `skip:${doseId}`,
          component_type: 2,
        },
        token: "tok_5",
        version: 1,
      };

      const response = await service.handleInteraction(interaction);
      assert.equal(response.type, DiscordCallbackType.UPDATE_MESSAGE);
      assert.equal(response.data?.content, "Пропущено ❌");
      assert.deepEqual(response.data?.components, []);
      assert.equal(rpcCalled, true);
    });

    test("кнопка 'Отложить' (snooze:<id>:15) вызывает snooze RPC", async () => {
      let rpcCalled = false;
      const mockSupabase = {
        rpc: async (fn: string, params: any) => {
          assert.equal(fn, "snooze_notification_delivery");
          assert.equal(params.p_dose_event_id, doseId);
          assert.equal(params.p_channel_type, "discord_app");
          assert.equal(params.p_minutes, 15);
          rpcCalled = true;
          return { data: { outcome: "Applied" }, error: null };
        },
      } as unknown as SupabaseClient;

      const service = new DiscordInteractionsService({ supabaseClient: mockSupabase });
      const interaction: DiscordInteraction = {
        id: "int_6",
        application_id: "app_1",
        type: DiscordInteractionType.MESSAGE_COMPONENT,
        channel_id: "chan_123",
        data: {
          custom_id: `snooze:${doseId}:15`,
          component_type: 2,
        },
        token: "tok_6",
        version: 1,
      };

      const response = await service.handleInteraction(interaction);
      assert.equal(response.type, DiscordCallbackType.UPDATE_MESSAGE);
      assert.match(response.data?.content!, /Отложено на 15 минут/);
      assert.deepEqual(response.data?.components, []);
      assert.equal(rpcCalled, true);
    });
  });
});
