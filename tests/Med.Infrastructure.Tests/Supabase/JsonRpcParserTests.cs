using FluentAssertions;
using Med.Application.Abstractions;
using Med.Domain.Enums;
using Med.Infrastructure.Supabase;
using Xunit;

namespace Med.Infrastructure.Tests.Supabase;

public sealed class JsonRpcParserTests
{
    [Fact]
    public void ParseDoseTransition_Applied()
    {
        Guid id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        Guid tx = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        string json =
            $"{{\"outcome\":\"Applied\",\"dose_event_id\":\"{id}\",\"state\":\"Taken\",\"transaction_id\":\"{tx}\",\"quantity_on_hand\":9}}";

        DoseTransitionResult result = JsonRpcParser.ParseDoseTransition(json, Guid.Empty);

        result.Outcome.Should().Be("Applied");
        result.DoseEventId.Should().Be(id);
        result.State.Should().Be(DoseEventState.Taken);
        result.TransactionId.Should().Be(tx);
        result.QuantityOnHand.Should().Be(9);
    }

    [Fact]
    public void ParseDoseTransition_NoOp_без_лишних_полей()
    {
        Guid id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        string json = $"{{\"outcome\":\"NoOp\",\"dose_event_id\":\"{id}\",\"state\":\"Taken\"}}";

        DoseTransitionResult result = JsonRpcParser.ParseDoseTransition(json, Guid.Empty);

        result.Outcome.Should().Be("NoOp");
        result.TransactionId.Should().BeNull();
    }

    [Fact]
    public void ParseInventoryCommand_Restock()
    {
        string json =
            "{\"outcome\":\"Applied\",\"transaction_id\":\"dddddddd-dddd-dddd-dddd-dddddddddddd\",\"quantity_on_hand\":42.5}";

        InventoryCommandResult result = JsonRpcParser.ParseInventoryCommand(json);

        result.Outcome.Should().Be("Applied");
        result.QuantityOnHand.Should().Be(42.5m);
    }
}
