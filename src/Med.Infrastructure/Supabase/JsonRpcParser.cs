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
        return text is not null && Enum.TryParse(text, ignoreCase: false, out TEnum parsed) ? parsed : null;
    }
}
