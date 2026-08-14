using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// §9 v2 — THE VERBATIM FORWARD, pinned end to end.
///
/// v1 shipped six numbers to the spiral canvas and the canvas drew a naked coil,
/// because DescentReader models a SUBSET of the block: day_fill, day_quest, stations,
/// breaks, dates and thresholds were parsed past and thrown away. v2 fixes that the
/// only way three clients can be fixed at once — by forwarding the server's own node
/// untouched and letting the embed validate it — so these tests are about FIDELITY and
/// PRESENCE, not about the block's meaning, which is the server's business.
///
/// Two properties, and both are load-bearing:
///   1. What comes out is byte-for-byte what went in. The date-coercion trap
///      (DescentBlockReaderTests) bites twice as hard here: a day string that
///      round-trips through a DateTime re-emits as an invented midnight instant, and
///      the canvas would draw a record the server never sent.
///   2. `descent` is ABSENT, never null, when there is nothing to forward. Absence IS
///      the v1 contract; an embed deployed before v2 must not be able to tell the
///      difference.
/// </summary>
public class SpiralEmbedStateV2Tests
{
    /// <summary>
    /// A full-fat block, including every section the typed reader does NOT model —
    /// day_quest, stations, breaks, anchor, chapter — because those are precisely the
    /// ones v2 exists to carry. If the forward ever narrows to "the fields we parse",
    /// this payload is what catches it.
    /// </summary>
    private const string FullFatBlock = """
    {
      "devotion_days": 142,
      "devotion_last_day": "2026-08-12",
      "anchor": { "date": "2026-03-23T00:00:00.000Z", "source": "migration", "day_n": 142 },
      "stage": { "n": 5, "key": "stage_5", "banked_days": 142, "next_at": 180,
                 "days_to_next": 38, "thresholds": [1,7,21,50,100,180,300] },
      "relapse": { "multiplier": 1.2, "days_away": 3, "surge_active": true,
                   "surge_ends_at": "2026-08-15T00:00:00.000Z", "surge_multiplier": 1.5 },
      "chapter": { "id": "2026-08", "xp": 91234, "ends_at": "2026-08-31T23:59:59.000Z", "rank": null },
      "vat": { "cap": 5200, "today_xp": 3016, "fill_pct": 58, "fill_lip_pct": 125 },
      "day_fill": [22, 100, 118, 47],
      "day_fill_start": "2026-08-09",
      "day_fill_start_day_n": 139,
      "day_quest": { "id": "bank_the_day", "done": false, "progress": 0.4 },
      "stations": [{ "id": "st_first_pour", "lit": true, "day_n": 1 },
                   { "id": "st_first_week", "lit": true, "day_n": 7 },
                   { "id": "st_hundred", "lit": false, "day_n": 100 }],
      "breaks": [{ "after_day": 140, "len": 2 }],
      "history_from": "2026-08-09"
    }
    """;

    /// <summary>Read a block the way the fetch path does — date coercion off.</summary>
    private static JObject Wire(string json) => DescentReader.ParseWire(json)!;

    // ======================================================= the reader keeps it

    [Fact]
    public void Reader_KeepsTheServersBlock_Verbatim()
    {
        var node = Wire(FullFatBlock);
        var block = DescentReader.Parse(node)!;

        Assert.NotNull(block.RawJson);
        // Not "equivalent" — the SAME tree, including the five sections the typed
        // reader has no property for.
        Assert.True(JToken.DeepEquals(DescentReader.ParseWire(block.RawJson), node));
    }

    /// <summary>
    /// The date-coercion trap, on the forwarding side. Re-serialising a token that a
    /// coercing parse turned into a DateTime emits "2026-08-12T00:00:00" with an
    /// invented offset, so this asserts against the STRING the reader stored rather
    /// than against a re-parse that could hide the damage.
    /// </summary>
    [Fact]
    public void Reader_RawJson_KeepsDateShapedStringsAsStrings()
    {
        var block = DescentReader.Parse(Wire(FullFatBlock))!;

        Assert.NotNull(block.RawJson);
        Assert.Contains("\"devotion_last_day\":\"2026-08-12\"", block.RawJson!);
        Assert.Contains("\"day_fill_start\":\"2026-08-09\"", block.RawJson!);
        Assert.Contains("\"surge_ends_at\":\"2026-08-15T00:00:00.000Z\"", block.RawJson!);

        var reread = DescentReader.ParseWire(block.RawJson)!;
        Assert.Equal(JTokenType.String, reread["devotion_last_day"]!.Type);
        Assert.Equal(JTokenType.String, reread["day_fill_start"]!.Type);
        Assert.Equal(JTokenType.String, reread["relapse"]!["surge_ends_at"]!.Type);
    }

    /// <summary>
    /// The tri-state law is not weakened by adding a field to carry: no block means no
    /// raw, because the raw is only ever captured past the well-formed bar.
    /// </summary>
    [Fact]
    public void Reader_MalformedBlock_IsStillNothing_AndCarriesNoRaw()
    {
        Assert.Null(DescentReader.Parse(null));
        Assert.Null(DescentReader.Parse(new JArray()));
        Assert.Null(DescentReader.Parse(Wire("{ \"vat\": { \"cap\": 5000 } }")));
        Assert.Null(DescentReader.Parse(Wire("{ \"devotion_days\": \"142\" }")));
        Assert.Null(DescentReader.Parse(Wire("{ \"devotion_days\": -1 }")));
    }

    // ==================================================== the payload forwards it

    [Fact]
    public void BuildState_ForwardsTheBlockUnchanged()
    {
        var node = Wire(FullFatBlock);
        var state = SpiralEmbedView.BuildState(DescentReader.Parse(node));

        Assert.True(JToken.DeepEquals(state["descent"], node));
    }

    /// <summary>The same, through the serialisation PostState actually writes to the wire.</summary>
    [Fact]
    public void BuildState_SurvivesSerialisationToTheWire()
    {
        var node = Wire(FullFatBlock);
        var json = SpiralEmbedView.BuildState(DescentReader.Parse(node)).ToString(Formatting.None);

        var onTheWire = DescentReader.ParseWire(json)!;
        Assert.True(JToken.DeepEquals(onTheWire["descent"], node));
        Assert.Equal("spiral:state", (string?)onTheWire["type"]);
    }

    /// <summary>
    /// ABSENT, NOT NULL. A null-valued key is a third state nobody specified, and an
    /// embed deployed before v2 must see exactly the v1 message.
    /// </summary>
    [Fact]
    public void BuildState_OmitsDescent_WhenThereIsNoBlock()
    {
        var state = SpiralEmbedView.BuildState(null);

        Assert.False(state.ContainsKey("descent"));
        Assert.DoesNotContain("descent", state.ToString(Formatting.None));
    }

    /// <summary>
    /// A hand-built block — a test fixture, or any future local construction — has no
    /// server node behind it, so it forwards nothing. This is the whole reason the
    /// forward is conditional rather than assumed.
    /// </summary>
    [Fact]
    public void BuildState_OmitsDescent_WhenTheBlockWasNotParsedFromAPayload()
    {
        var state = SpiralEmbedView.BuildState(new DescentBlock { DevotionDays = 9 });

        Assert.False(state.ContainsKey("descent"));
        Assert.Equal(9, (int)state["devotion_days"]!);
    }

    // ============================================== v1 fields, unchanged by v2

    [Fact]
    public void BuildState_V1Fields_AreUnchanged_WithABlock()
    {
        var state = SpiralEmbedView.BuildState(DescentReader.Parse(Wire(FullFatBlock)));

        Assert.Equal("spiral:state", (string?)state["type"]);
        Assert.Equal(142, (int)state["devotion_days"]!);
        Assert.Equal(5, (int)state["stage"]!);
        Assert.Equal(1.2, (double)state["relapse_multiplier"]!, 6);
        Assert.Equal(58.0, (double)state["vat_fill_pct"]!, 6);
        Assert.NotNull(state["reduced_motion"]);
        Assert.True((string?)state["perf_tier"] is "low" or "mid" or "high");

        // The station list stays desktop-silent even in v2 — what the block carries is
        // the server's stations, not a list this side composed.
        Assert.False(state.ContainsKey("lit_station_ids"));
    }

    [Fact]
    public void BuildState_V1Fields_AreUnchanged_WithNoBlock()
    {
        var state = SpiralEmbedView.BuildState(null);

        Assert.Equal("spiral:state", (string?)state["type"]);
        Assert.Equal(0, (int)state["devotion_days"]!);
        Assert.Equal(0, (int)state["stage"]!);
        Assert.Equal(1.0, (double)state["relapse_multiplier"]!, 6);
        Assert.Equal(0.0, (double)state["vat_fill_pct"]!, 6);
        Assert.True((string?)state["perf_tier"] is "low" or "mid" or "high");
    }
}
