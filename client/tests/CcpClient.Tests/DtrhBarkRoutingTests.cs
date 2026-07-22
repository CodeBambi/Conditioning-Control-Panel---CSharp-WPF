using System.Text.Json;
using CcpClient.Desktop.Features.Dtrh;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-032 q2 DTRH bark routing conformance: the full WPF RouteBark event table
/// (DtrhHostService.cs:618-650 → BarkService.cs:262-365), fill-key mapping, reused-voice
/// constants, and typed unrouted/malformed handling.
/// </summary>
public sealed class DtrhBarkRoutingTests
{
    private static readonly string[] AllEvents =
    [
        "ending-soon", "wave-cleared", "wave-escalated", "act-changed", "benign-popped",
        "defused", "detonated", "detonated-absorbed", "darter-caught", "freeze-caught",
        "combo-milestone", "combo-big", "boon-picked", "curse-picked", "boon-skipped",
        "draft-autopick", "focus-low", "defuse-first", "defuse-nofocus", "defuse-release",
        "click-detonate", "tease-debut", "tease-clicked", "tease-denied",
        "tease-denied-streak", "gold-first", "dollhouse-first-open", "reveal-flash",
        "lesson-complete", "duo-demo", "rabbit-caught", "crafted",
    ];

    [Fact]
    public void EveryWpfEvent_Routes()
    {
        // The full M4+M5+Wave2+crafting surface (32 events) — WPF RouteBark parity.
        foreach (var eventName in AllEvents)
        {
            var bark = Parse($"{{\"type\":\"bark\",\"event\":\"{eventName}\"}}");
            Assert.True(DtrhBarkRouting.TryRoute(bark, out var trigger, out _), eventName);
            Assert.StartsWith("Chaos", trigger);
        }

        Assert.Equal(32, AllEvents.Length); // WPF switch arms (RouteBark :618-650)
    }

    [Fact]
    public void Fills_CarryWpfKeys_FromPayloadFields()
    {
        var bark = Parse(
            "{\"type\":\"bark\",\"event\":\"detonated\",\"variant\":\"curse\",\"strength\":1.5,\"runDetonations\":3,\"combo\":7,\"difficulty\":\"Hard\"}");
        Assert.True(DtrhBarkRouting.TryRoute(bark, out var trigger, out var fills));
        Assert.Equal("ChaosBubbleDetonated", trigger);
        Assert.Equal("curse", fills["payload"]);
        Assert.Equal(1.5, (double)fills["strength"]!);
        Assert.Equal(3.0, (double)fills["run_detonations"]!);
        Assert.Equal(7.0, (double)fills["combo"]!);
        Assert.Equal("Hard", fills["difficulty"]);
    }

    [Fact]
    public void WaveCleared_FillKey_MatchesConditionSchema()
    {
        // The conditions in bark rules read "wave_gte" → field "wave" (WPF NotifyChaosWaveCleared).
        var bark = Parse("{\"type\":\"bark\",\"event\":\"wave-cleared\",\"wave\":3}");
        Assert.True(DtrhBarkRouting.TryRoute(bark, out var trigger, out var fills));
        Assert.Equal("ChaosWaveCleared", trigger);
        Assert.Equal(3.0, (double)fills["wave"]!);
    }

    [Fact]
    public void RabbitCaught_Constants_ReuseDarterVoice()
    {
        // WPF RouteBark :643 — rabbit-caught → NotifyChaosDarterCaught(gold, 0, true).
        var bark = Parse("{\"type\":\"bark\",\"event\":\"rabbit-caught\",\"gold\":25}");
        Assert.True(DtrhBarkRouting.TryRoute(bark, out var trigger, out var fills));
        Assert.Equal("ChaosDarterCaught", trigger);
        Assert.Equal(25.0, (double)fills["points"]!);
        Assert.Equal(0.0, (double)fills["combo"]!);
        Assert.Equal(true, fills["quick"]);
    }

    [Fact]
    public void Unrouted_And_Malformed_Typed_NeverThrown()
    {
        Assert.False(DtrhBarkRouting.TryRoute(
            Parse("{\"type\":\"bark\",\"event\":\"not-a-real-event\"}"), out _, out _));
        Assert.False(DtrhBarkRouting.TryRoute(
            new DtrhProtocol.DtrhPageMessage.Bark(null, JsonDocument.Parse("{}").RootElement), out _, out _));
    }

    private static DtrhProtocol.DtrhPageMessage.Bark Parse(string json) =>
        Assert.IsType<DtrhProtocol.DtrhPageMessage.Bark>(
            Assert.IsType<DtrhProtocol.DtrhPageParseResult.Parsed>(
                DtrhProtocol.ParsePageMessage(json)).Message);
}
