using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The three rules of EMI's line engine that a human can never test by playing (LINES-SCHEMA 5).
///
/// Every engine here is built from an in-memory lines file through <c>EmiLineEngine.FromJson</c>,
/// which also keeps the rotation ledger in memory. Nothing in this class can read or write the
/// shipped <c>Resources/emi/desk-lines.json</c>, the user's <c>emi-desk.json</c>, or any other
/// process state: a test that burned the real bags would make the very repeats it is here to catch.
/// </summary>
public class EmiLineEngineRotationTests
{
    private const string Pool = "test.pool";

    /// <summary>Ten plain rows, all spice 0, no gates: the simplest bag there is.</summary>
    private static string TenLineFile()
    {
        var rows = string.Join(",", Enumerable.Range(1, 10)
            .Select(i => $"{{\"id\":\"t{i}\",\"t\":\"line {i}\",\"face\":\"^_^\",\"spice\":0}}"));
        return "{\"version\":1,\"moments\":{},\"pools\":{\"" + Pool + "\":[" + rows + "]},\"asks\":[]}";
    }

    /// <summary>
    /// THE ROTATION RULE. A pool is a shuffle bag, not a dice roll: across a thousand draws over a
    /// ten-line pool she must deal all ten before any one of them comes back. This is the single
    /// most load-bearing promise in the whole feature, because a repeat inside one sitting is what
    /// makes a companion read as a script.
    /// </summary>
    [Fact]
    public void NoLineRepeatsBeforeThePoolIsExhausted()
    {
        var engine = EmiLineEngine.FromJson(TenLineFile());
        Assert.Equal(10, engine.PoolSizeForTests(Pool));

        var window = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 1000; i++)
        {
            var id = engine.DealForTests(Pool);
            Assert.NotNull(id);

            if (window.Count == 10) window.Clear();
            Assert.True(window.Add(id!),
                $"draw {i}: {id} came back after only {window.Count} of 10 lines had been dealt");
        }
    }

    /// <summary>
    /// Ten draws is exactly one full bag, so the ids dealt must be the whole pool with nothing
    /// missing and nothing doubled. The test above proves no early repeat; this proves the bag is
    /// actually being emptied rather than quietly reshuffled part-way.
    /// </summary>
    [Fact]
    public void OneFullBagDealsEveryLineExactlyOnce()
    {
        var engine = EmiLineEngine.FromJson(TenLineFile());

        var dealt = Enumerable.Range(0, 10).Select(_ => engine.DealForTests(Pool)).ToList();

        Assert.All(dealt, Assert.NotNull);
        Assert.Equal(10, dealt.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            Enumerable.Range(1, 10).Select(i => "t" + i).OrderBy(s => s, StringComparer.Ordinal),
            dealt.OrderBy(s => s, StringComparer.Ordinal));
    }

    /// <summary>
    /// THE SPICE CEILING. The per-fire ceiling is the floor of the moment's own ceiling and the
    /// user's setting, and a line above it is not "less likely", it is unreachable. A user who set
    /// Innocent must never see a suggestive line, not once in a thousand draws.
    /// </summary>
    [Fact]
    public void SpiceCeilingIsAHardWall()
    {
        const string json =
            "{\"version\":1,\"moments\":{},\"pools\":{\"" + Pool + "\":[" +
            "{\"id\":\"s0a\",\"t\":\"mild\",\"spice\":0}," +
            "{\"id\":\"s0b\",\"t\":\"also mild\",\"spice\":0}," +
            "{\"id\":\"s1\",\"t\":\"suggestive\",\"spice\":1}," +
            "{\"id\":\"s2\",\"t\":\"anything\",\"spice\":2}]},\"asks\":[]}";

        var innocent = EmiLineEngine.FromJson(json);
        for (int i = 0; i < 200; i++)
        {
            var id = innocent.DealForTests(Pool, ctx: null, ceiling: 0);
            Assert.NotNull(id);
            Assert.StartsWith("s0", id!, StringComparison.Ordinal);
        }

        var suggestive = EmiLineEngine.FromJson(json);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 200; i++)
        {
            var id = suggestive.DealForTests(Pool, ctx: null, ceiling: 1);
            Assert.NotNull(id);
            Assert.NotEqual("s2", id);
            seen.Add(id!);
        }

        // The ceiling must OPEN the tier, not just close the one above it.
        Assert.Contains("s1", seen);
    }

    /// <summary>
    /// THE <c>when</c> GATES (LINES-SCHEMA 5.4). A bare name is "this ctx key is truthy", a leading
    /// <c>!</c> is "missing or falsy", and <c>key:value</c> is case-insensitive equality. A gated
    /// line that leaks into the wrong context is EMI describing something that did not happen.
    /// </summary>
    [Fact]
    public void WhenGatesDecideWhichLinesAreEvenInTheBag()
    {
        const string json =
            "{\"version\":1,\"moments\":{},\"pools\":{\"" + Pool + "\":[" +
            "{\"id\":\"plain\",\"t\":\"plain\",\"spice\":0}," +
            "{\"id\":\"gated\",\"t\":\"gated\",\"spice\":0,\"when\":[\"first\"]}," +
            "{\"id\":\"negated\",\"t\":\"negated\",\"spice\":0,\"when\":[\"!first\"]}," +
            "{\"id\":\"viaHotkey\",\"t\":\"hotkey\",\"spice\":0,\"when\":[\"via:hotkey\"]}]},\"asks\":[]}";

        // Nothing set: the bare gate is shut, the negated gate is open.
        var bare = EmiLineEngine.FromJson(json);
        var bareSeen = Drain(bare, 120, new Dictionary<string, object?>());
        Assert.DoesNotContain("gated", bareSeen);
        Assert.DoesNotContain("viaHotkey", bareSeen);
        Assert.Contains("plain", bareSeen);
        Assert.Contains("negated", bareSeen);

        // first = true: the bare gate opens and the negated one shuts.
        var flagged = EmiLineEngine.FromJson(json);
        var flaggedSeen = Drain(flagged, 120, new Dictionary<string, object?> { ["first"] = true });
        Assert.Contains("gated", flaggedSeen);
        Assert.DoesNotContain("negated", flaggedSeen);

        // key:value, matched without regard to case.
        var keyed = EmiLineEngine.FromJson(json);
        var keyedSeen = Drain(keyed, 120, new Dictionary<string, object?> { ["via"] = "HOTKEY" });
        Assert.Contains("viaHotkey", keyedSeen);

        var wrongValue = EmiLineEngine.FromJson(json);
        var wrongSeen = Drain(wrongValue, 120, new Dictionary<string, object?> { ["via"] = "rail" });
        Assert.DoesNotContain("viaHotkey", wrongSeen);
    }

    /// <summary>
    /// A line whose text carries a token the ctx cannot fill is skipped, never spoken with a hole
    /// in it (LINES-SCHEMA 5.3). Every token pool ships plain siblings for exactly this reason.
    /// </summary>
    [Fact]
    public void LinesWithUnfillableTokensAreSkipped()
    {
        const string json =
            "{\"version\":1,\"moments\":{},\"pools\":{\"" + Pool + "\":[" +
            "{\"id\":\"plain\",\"t\":\"no tokens here\",\"spice\":0}," +
            "{\"id\":\"tokened\",\"t\":\"level {level} already\",\"spice\":0}]},\"asks\":[]}";

        var without = EmiLineEngine.FromJson(json);
        var withoutSeen = Drain(without, 60, new Dictionary<string, object?>());
        Assert.DoesNotContain("tokened", withoutSeen);
        Assert.Contains("plain", withoutSeen);

        var with = EmiLineEngine.FromJson(json);
        var withSeen = Drain(with, 60, new Dictionary<string, object?> { ["level"] = 42 });
        Assert.Contains("tokened", withSeen);
    }

    private static HashSet<string> Drain(EmiLineEngine engine, int draws, IReadOnlyDictionary<string, object?> ctx)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < draws; i++)
        {
            var id = engine.DealForTests(Pool, ctx);
            if (id != null) seen.Add(id);
        }
        return seen;
    }
}
