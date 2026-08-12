using System;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE TRI-STATE LAW, pinned.
///
/// The server ships the `descent` block only to accounts inside its rollout dial
/// (DESCENT_BLOCK + DESCENT_BLOCK_STAGE), so on desktop the overwhelmingly common
/// payload has NO block at all — and that case has to be indistinguishable from
/// the flag being off. These tests exist because "render nothing" is a security
/// property of a staged rollout, not a nicety: the moment a malformed payload
/// produces a drawable vat, every excluded user can tell they were excluded.
///
/// The expectations mirror the web client's reader (cclabs-web
/// src/lib/descent/data.ts) field for field. A divergence here is a divergence in
/// what two clients draw from one wire contract.
/// </summary>
public class DescentBlockReaderTests
{
    private static JObject? Block(string json) => DescentReader.ParseWire(json);

    // -------------------------------------------------- the date-coercion trap

    /// <summary>
    /// THE BUG THIS TEST WAS WRITTEN FOR. `JObject.Parse` rewrites any ISO-8601-
    /// shaped string into a JTokenType.Date, so a strict string reader — which is
    /// what the web client is and what this one has to match — read EVERY date
    /// field on the block as absent while the vat and the stage kept working. The
    /// fix is at the parse boundary (DescentReader.ParseWire), and this pins it:
    /// a day string stays a day string, an instant stays the instant that was sent.
    /// </summary>
    [Fact]
    public void ParseWire_KeepsDateShapedStringsAsStrings()
    {
        var block = DescentReader.Parse(Block("""
        {
          "devotion_days": 12,
          "devotion_last_day": "2026-08-12",
          "day_fill_start": "2026-08-09",
          "history_from": "2026-08-09",
          "relapse": { "multiplier": 1.2, "days_away": 3, "surge_active": true,
                       "surge_ends_at": "2026-08-15T00:00:00.000Z" }
        }
        """))!;

        Assert.Equal("2026-08-12", block.DevotionLastDay);
        Assert.Equal("2026-08-09", block.DayFillStart);
        Assert.Equal("2026-08-09", block.HistoryFrom);
        Assert.Equal("2026-08-15T00:00:00.000Z", block.Relapse!.SurgeEndsAt);
    }

    [Fact]
    public void ParseWire_JunkIsNothing()
    {
        Assert.Null(DescentReader.ParseWire(null));
        Assert.Null(DescentReader.ParseWire(""));
        Assert.Null(DescentReader.ParseWire("not json"));
        Assert.Null(DescentReader.ParseWire("[1,2,3]"));
    }

    // ------------------------------------------------------------ nothing at all

    [Fact]
    public void Parse_Null_IsNothing() => Assert.Null(DescentReader.Parse(null));

    [Fact]
    public void Parse_NotAnObject_IsNothing()
    {
        Assert.Null(DescentReader.Parse(new JValue("descent")));
        Assert.Null(DescentReader.Parse(new JArray()));
        Assert.Null(DescentReader.Parse(JValue.CreateNull()));
    }

    [Fact]
    public void Parse_WithoutDevotionDays_IsNothing()
        => Assert.Null(DescentReader.Parse(Block("{ \"vat\": { \"cap\": 5000, \"today_xp\": 100 } }")));

    [Fact]
    public void Parse_NegativeDevotionDays_IsNothing()
        => Assert.Null(DescentReader.Parse(Block("{ \"devotion_days\": -1 }")));

    /// <summary>
    /// A numeric STRING is not a number. The web reader tests `typeof v === 'number'`
    /// and a client more permissive than its siblings draws a different vat.
    /// </summary>
    [Fact]
    public void Parse_NumericStringDevotionDays_IsNothing()
        => Assert.Null(DescentReader.Parse(Block("{ \"devotion_days\": \"7\" }")));

    [Fact]
    public void ParseFromUserNode_NoDescentKey_IsNothing()
    {
        var user = Block("{ \"unified_id\": \"u_abc12345\", \"level\": 47, \"xp\": 1234 }");
        Assert.Null(DescentReader.ParseFromUserNode(user));
    }

    [Fact]
    public void ParseFromUserNode_NullNode_IsNothing() => Assert.Null(DescentReader.ParseFromUserNode(null));

    [Fact]
    public void ParseFromUserNode_ReadsTheBlockOffTheUserObject()
    {
        var user = Block("{ \"unified_id\": \"u_abc12345\", \"descent\": { \"devotion_days\": 12 } }");
        var block = DescentReader.ParseFromUserNode(user);
        Assert.NotNull(block);
        Assert.Equal(12, block!.DevotionDays);
    }

    // ------------------------------------------------------------------- the vat

    [Fact]
    public void Vat_ShipsDarkWhenCapIsMissingOrZero()
    {
        Assert.Null(DescentReader.Parse(Block("{ \"devotion_days\": 3 }"))!.Vat);
        Assert.Null(DescentReader.Parse(Block("{ \"devotion_days\": 3, \"vat\": {} }"))!.Vat);
        Assert.Null(DescentReader.Parse(Block("{ \"devotion_days\": 3, \"vat\": { \"cap\": 0 } }"))!.Vat);
        Assert.Null(DescentReader.Parse(Block("{ \"devotion_days\": 3, \"vat\": { \"cap\": -5 } }"))!.Vat);

        // The guard is on the FLOORED number, which is the one that ships: a cap of
        // 0.4 is "> 0" but becomes Cap = 0, i.e. a live vat with a zero cap.
        Assert.Null(DescentReader.Parse(Block("{ \"devotion_days\": 3, \"vat\": { \"cap\": 0.4 } }"))!.Vat);
        Assert.Equal(1, DescentReader.Parse(Block(
            "{ \"devotion_days\": 3, \"vat\": { \"cap\": 1.9 } }"))!.Vat!.Cap);
    }

    [Fact]
    public void Vat_TakesTheServersFillPct()
    {
        var vat = DescentReader.Parse(Block(
            "{ \"devotion_days\": 3, \"vat\": { \"cap\": 5000, \"today_xp\": 1200, \"fill_pct\": 24, \"fill_lip_pct\": 120 } }"))!.Vat!;
        Assert.Equal(5000, vat.Cap);
        Assert.Equal(1200, vat.TodayXp);
        Assert.Equal(24, vat.FillPct);
        Assert.Equal(120, vat.FillLipPct);
    }

    /// <summary>
    /// fill_pct absent falls back to today_xp/cap — the archived percent is the
    /// server's preferred number, but a block without it is still readable.
    /// </summary>
    [Fact]
    public void Vat_DerivesFillFromTodayXpWhenFillPctIsAbsent()
    {
        var vat = DescentReader.Parse(Block(
            "{ \"devotion_days\": 3, \"vat\": { \"cap\": 4000, \"today_xp\": 1000 } }"))!.Vat!;
        Assert.Equal(25, vat.FillPct, 6);
    }

    [Fact]
    public void Vat_LipDefaultsTo120WhenTheServerNamesNone()
    {
        var vat = DescentReader.Parse(Block(
            "{ \"devotion_days\": 3, \"vat\": { \"cap\": 4000, \"today_xp\": 0 } }"))!.Vat!;
        Assert.Equal(DescentReader.DefaultLipPct, vat.FillLipPct);
    }

    [Theory]
    [InlineData(125, 125)]   // stage 4 perk
    [InlineData(130, 130)]   // stage 6 perk
    [InlineData(90, 100)]    // under the cap would invert the meter -> floored
    [InlineData(500, 200)]   // runaway would put the cap line on the glass floor -> capped
    public void Vat_LipIsClampedToTheDrawableRange(double sent, double expected)
    {
        var vat = DescentReader.Parse(Block(
            $"{{ \"devotion_days\": 3, \"vat\": {{ \"cap\": 4000, \"today_xp\": 0, \"fill_lip_pct\": {sent} }} }}"))!.Vat!;
        Assert.Equal(expected, vat.FillLipPct);
    }

    /// <summary>
    /// A lip of exactly 100 is the legal "no lip" jar: no MAX tick, no band above
    /// the cap, and the fill stops at the cap instead of at a brim that is not there.
    /// </summary>
    [Fact]
    public void Vat_LipOf100MeansNoLipAndNoBrimHeadroom()
    {
        var vat = DescentReader.Parse(Block(
            "{ \"devotion_days\": 3, \"vat\": { \"cap\": 4000, \"today_xp\": 8000, \"fill_pct\": 200, \"fill_lip_pct\": 100 } }"))!.Vat!;
        Assert.False(vat.HasLip);
        Assert.Equal(1.0, vat.FillFraction, 6);
    }

    [Fact]
    public void Vat_FillFractionClampsToTheBrimHeadroom()
    {
        var vat = DescentReader.Parse(Block(
            "{ \"devotion_days\": 3, \"vat\": { \"cap\": 4000, \"today_xp\": 99999, \"fill_pct\": 400, \"fill_lip_pct\": 120 } }"))!.Vat!;
        Assert.Equal(1.22, vat.FillFraction, 6);
    }

    [Fact]
    public void Vat_NegativeFillReadsAsEmpty()
    {
        var vat = DescentReader.Parse(Block(
            "{ \"devotion_days\": 3, \"vat\": { \"cap\": 4000, \"today_xp\": 0, \"fill_pct\": -30 } }"))!.Vat!;
        Assert.Equal(0, vat.FillPct);
        Assert.Equal(0, vat.FillFraction);
    }

    // ----------------------------------------------------------------- the ladder

    [Fact]
    public void Stage_ReadsTheWholeActiveLadder()
    {
        var stage = DescentReader.Parse(Block(
            "{ \"devotion_days\": 12, \"stage\": { \"n\": 2, \"key\": \"stage_2\", \"banked_days\": 12, " +
            "\"next_at\": 21, \"days_to_next\": 9, \"thresholds\": [1,7,21,50,100,180,300] } }"))!.Stage!;
        Assert.Equal(2, stage.N);
        Assert.Equal("stage_2", stage.Key);
        Assert.Equal(12, stage.BankedDays);
        Assert.Equal(21, stage.NextAt);
        Assert.Equal(9, stage.DaysToNext);
        Assert.Equal(new[] { 1, 7, 21, 50, 100, 180, 300 }, stage.Thresholds);
    }

    [Fact]
    public void Stage_TopRungHasNoNextThreshold()
    {
        var stage = DescentReader.Parse(Block(
            "{ \"devotion_days\": 400, \"stage\": { \"n\": 7, \"next_at\": null, \"days_to_next\": null } }"))!.Stage!;
        Assert.Equal(7, stage.N);
        Assert.Null(stage.NextAt);
        Assert.Null(stage.DaysToNext);
    }

    /// <summary>
    /// A half-read ladder puts notches at days nobody is ranked against, which is
    /// worse than a client table that is merely out of date — so junk is null and
    /// the caller falls back to its own copy.
    /// </summary>
    [Theory]
    [InlineData("[1,7,7,50]")]                     // not strictly increasing
    [InlineData("[7,1]")]                          // descending
    [InlineData("[0,7,21]")]                       // zero is not a rung
    [InlineData("[1,7,21,50,100,180,300,400]")]    // longer than this client can draw
    [InlineData("[]")]
    [InlineData("[1,\"7\",21]")]                   // numeric string
    [InlineData("[1,7.5,21]")]                     // fractional day
    public void Stage_RejectsAnUnusableLadder(string thresholds)
    {
        var stage = DescentReader.Parse(Block(
            $"{{ \"devotion_days\": 12, \"stage\": {{ \"n\": 2, \"thresholds\": {thresholds} }} }}"))!.Stage!;
        Assert.Null(stage.Thresholds);
    }

    [Fact]
    public void Stage_ZeroIsARealRungNotAnAbsence()
    {
        var stage = DescentReader.Parse(Block("{ \"devotion_days\": 0, \"stage\": { \"n\": 0 } }"))!.Stage;
        Assert.NotNull(stage);
        Assert.Equal(0, stage!.N);
        Assert.Equal("stage_0", stage.Key);
    }

    // ----------------------------------------------------------------- relapse

    [Fact]
    public void Relapse_ReadsTheBonusAndTheSurge()
    {
        var relapse = DescentReader.Parse(Block(
            "{ \"devotion_days\": 5, \"relapse\": { \"multiplier\": 1.36, \"days_away\": 9, " +
            "\"surge_active\": true, \"surge_ends_at\": \"2026-08-15T00:00:00.000Z\", \"surge_multiplier\": 1.4 } }"))!.Relapse!;
        Assert.Equal(1.36, relapse.Multiplier, 6);
        Assert.Equal(9, relapse.DaysAway);
        Assert.True(relapse.SurgeActive);
        Assert.Equal("2026-08-15T00:00:00.000Z", relapse.SurgeEndsAt);
        Assert.Equal(1.4, relapse.SurgeMultiplier, 6);
    }

    [Fact]
    public void Relapse_SurgeIsNeverGuessedFromAMissingStamp()
    {
        var relapse = DescentReader.Parse(Block(
            "{ \"devotion_days\": 5, \"relapse\": { \"multiplier\": 1.2, \"days_away\": 5 } }"))!.Relapse!;
        Assert.False(relapse.SurgeActive);
        Assert.Null(relapse.SurgeEndsAt);
        Assert.Equal(1.0, relapse.SurgeMultiplier);
    }

    // ---------------------------------------------------------------- day fill

    [Fact]
    public void DayFill_UnreadableEntriesFallBackToTheBankThreshold()
    {
        var block = DescentReader.Parse(Block(
            "{ \"devotion_days\": 4, \"day_fill\": [30, null, \"x\", 118] }"))!;
        Assert.Equal(new[] { 30, (int)DescentReader.BankThresholdPct, (int)DescentReader.BankThresholdPct, 118 }, block.DayFill);
    }

    /// <summary>
    /// Ordinals are the server's, counted BACK from devotion_days — a veteran with
    /// 300 banked days and 4 recorded ones starts at 297, never at 1.
    /// </summary>
    [Fact]
    public void DayFill_StartOrdinalIsDerivedBackwardsWhenTheServerOmitsIt()
    {
        var block = DescentReader.Parse(Block(
            "{ \"devotion_days\": 300, \"day_fill\": [20, 40, 60, 80] }"))!;
        Assert.Equal(297, block.DayFillStartDayN);
    }

    [Fact]
    public void DayFill_StatedStartOrdinalIsNeverReDerived()
    {
        var block = DescentReader.Parse(Block(
            "{ \"devotion_days\": 300, \"day_fill\": [20, 40], \"day_fill_start_day_n\": 261 }"))!;
        Assert.Equal(261, block.DayFillStartDayN);
    }

    [Fact]
    public void DayFill_EmptyHistoryHasOrdinalZero()
    {
        var block = DescentReader.Parse(Block("{ \"devotion_days\": 12 }"))!;
        Assert.Empty(block.DayFill);
        Assert.Equal(0, block.DayFillStartDayN);
        Assert.Null(block.DayFillStart);
    }

    // ------------------------------------------------- a whole realistic payload

    [Fact]
    public void Parse_RealisticBlock_ReadsEverySectionItNeeds()
    {
        var block = DescentReader.Parse(Block("""
        {
          "devotion_days": 142,
          "devotion_last_day": "2026-08-12",
          "anchor": { "date": "2026-03-23T00:00:00.000Z", "source": "migration", "day_n": 142 },
          "stage": { "n": 5, "key": "stage_5", "banked_days": 142, "next_at": 180,
                     "days_to_next": 38, "thresholds": [1,7,21,50,100,180,300] },
          "relapse": { "multiplier": 1.0, "days_away": 0, "surge_active": false,
                       "surge_ends_at": null, "surge_multiplier": 1.0 },
          "chapter": { "id": "2026-08", "xp": 91234, "ends_at": "2026-08-31T23:59:59.000Z", "rank": null },
          "cycle": { "number": 0, "started_at": null },
          "vat": { "cap": 5200, "today_xp": 3016, "fill_pct": 58, "fill_lip_pct": 125 },
          "day_fill": [22, 100, 118, 47],
          "day_fill_start": "2026-08-09",
          "day_fill_start_day_n": 139,
          "history_from": "2026-08-09",
          "breaks": [{ "after_day": 140, "len": 2 }]
        }
        """))!;

        Assert.Equal(142, block.DevotionDays);
        Assert.Equal("2026-08-12", block.DevotionLastDay);
        Assert.Equal(5, block.Stage!.N);
        Assert.Equal(5200, block.Vat!.Cap);
        Assert.Equal(0.58, block.Vat.FillFraction, 6);
        Assert.Equal(1.25, block.Vat.LipFraction, 6);
        Assert.True(block.Vat.HasLip);
        Assert.Equal("2026-08-09", block.HistoryFrom);
        Assert.Equal(139, block.DayFillStartDayN);
    }
}
