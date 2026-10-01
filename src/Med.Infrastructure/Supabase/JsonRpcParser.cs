using System.Globalization;
using System.Text.Json;
using Med.Application.Abstractions;
using Med.Domain.Enums;

namespace Med.Infrastructure.Supabase;

internal static class JsonRpcParser
{
    internal static DoseTransitionResult ParseDoseTransition(string content, Guid fallbackDoseEventId)
    {
        using JsonDocument document = JsonDocument.Parse(content);
        JsonElement root = document.RootElement;

        string outcome = root.GetProperty("outcome").GetString()
            ?? throw new InvalidOperationException("RPC confirm/skip/undo: отсутствует outcome.");

        Guid doseEventId = TryGetGuid(root, "dose_event_id") ?? fallbackDoseEventId;
        DoseEventState? state = TryGetEnum<DoseEventState>(root, "state");
        string? reason = TryGetString(root, "reason");
        Guid? transactionId = TryGetGuid(root, "transaction_id");
        decimal? quantityOnHand = TryGetDecimal(root, "quantity_on_hand");

        return new DoseTransitionResult(outcome, doseEventId, state, reason, transactionId, quantityOnHand);
    }

    internal static InventoryCommandResult ParseInventoryCommand(string content)
    {
        using JsonDocument document = JsonDocument.Parse(content);
        JsonElement root = document.RootElement;

        string outcome = root.GetProperty("outcome").GetString()
            ?? throw new InvalidOperationException("RPC restock_inventory: отсутствует outcome.");

        Guid? transactionId = TryGetGuid(root, "transaction_id");
        decimal? quantityOnHand = TryGetDecimal(root, "quantity_on_hand");
        string? reason = TryGetString(root, "reason");

        return new InventoryCommandResult(outcome, transactionId, quantityOnHand, reason);
    }

    internal static int ParseInt(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("RPC вернул пустой ответ.");
        }

        string trimmed = content.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int direct))
        {
            return direct;
        }

        using JsonDocument document = JsonDocument.Parse(trimmed);
        JsonElement root = document.RootElement;
        return root.ValueKind switch
        {
            JsonValueKind.Number => root.GetInt32(),
            JsonValueKind.String when int.TryParse(
                root.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed) => parsed,
            _ => throw new InvalidOperationException($"RPC: ожидалось целое, получено {root.ValueKind}."),
        };
    }

    private static string? TryGetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Guid? TryGetGuid(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        return text is not null && Guid.TryParse(text, out Guid parsed) ? parsed : null;
    }

    private static decimal? TryGetDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(
                value.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal parsed) => parsed,
            _ => null,
        };
    }

    private static TEnum? TryGetEnum<TEnum>(JsonElement root, string propertyName)
        where TEnum : struct, Enum
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        string? text = value.GetString();
        return text is not null && Enum.TryParse(text, ignoreCase: true, out TEnum parsed) ? parsed : null;
    }
}
