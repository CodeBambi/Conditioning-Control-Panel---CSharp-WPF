using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The player's own figure on a price row: sign kept, size clamped on every read,
/// only fixed-figure rows, only while no lock runs.</summary>
public class TabPriceEditTests
{
    [Theory]
    [InlineData("2:30", 150)]
    [InlineData("0:05", 5)]
    [InlineData("5", 300)]
    [InlineData("1:05:00", 3900)]
    [InlineData("+3:00", 180)]
    [InlineData("-1:00", 60)]
    [InlineData("  10:00 ", 600)]
    [InlineData("", 0)]
    public void Parse_reads_clock_figures_and_bare_minutes(string text, int seconds) =>
        Assert.Equal(seconds, TabPriceEdit.Parse(text));

    [Theory]
    [InlineData("abc")]
    [InlineData("1:5")]
    [InlineData("1:75")]
    [InlineData("1:2:3:4")]
    [InlineData("1.5")]
    public void Parse_refuses_what_is_not_a_time(string text) => Assert.Null(TabPriceEdit.Parse(text));

    [Fact]
    public void The_way_out_the_misses_the_leash_stakes_and_day_end_rows_never_take_an_edit()
    {
        foreach (var id in new[] { "panic", "emergency_exit", "safeword", "unlink", "leash_cut", CircesMisses.EventId,
                     "leash", "leash_credit", Services.Stakes.StakeRules.LossRowId,
                     TabDayEnd.IdleEventId, TabDayEnd.DailiesEventId, TabDayEnd.StreakEventId, TabDayEnd.HeatId, "escape", "nope" })
            Assert.False(TabPriceEdit.Editable(id), id);
        foreach (var id in new[] { "typo", "lockcard", "attention", "session", "crash", "natasha" })
            Assert.True(TabPriceEdit.Editable(id), id);
    }

    /// <summary>TAB-4. The escape row's ceiling (three a day, so 9:00 at most) is a safety promise
    /// about the moment a player is trying to get out, so its figure never moves: the box refuses
    /// it, and a figure already written into the settings file is ignored on read.</summary>
    [Fact]
    public void The_escape_row_keeps_its_nine_minute_day_whatever_the_file_says()
    {
        Assert.False(TabPriceEdit.Editable("escape"));
        Assert.False(TabPriceEdit.With(null, "escape", 3600).ContainsKey("escape"));
        Assert.Equal(180, TabPriceEdit.Effective("escape", new Dictionary<string, int> { ["escape"] = 3600 }));

        var dir = Path.Combine(Path.GetTempPath(), "ccp-priceedit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var utc = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
            var store = new MemoryStore { Tokens = new ChasterStoredTokens("AT", "RT", utc.AddSeconds(300)) };
            var options = new ChasterOptions(true, "lock1", new HashSet<string> { "escape" },
                Limits: TabLimits.FromMinutes(720, 2880),
                PriceOverrides: new Dictionary<string, int> { ["escape"] = 3600 });
            using var service = new ChasterService(new ChasterClient(), store, Path.Combine(dir, "tab.json"),
                () => options, () => utc, () => utc.ToLocalTime());

            var total = 0;
            for (var i = 0; i < 5; i++) total += service.Note("escape").AppliedSeconds;
            Assert.Equal(9 * 60, total);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    /// <summary>TAB-11. Only an EMPTY box means "back to the default". A typed zero asks for the
    /// row as small as it goes, the 0:05 minimum, never for the full default back.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("0:00")]
    [InlineData("0:00:00")]
    [InlineData("-0")]
    public void A_typed_zero_is_the_smallest_figure_and_only_an_empty_box_is_the_default(string text)
    {
        Assert.Equal(TabPriceEdit.MinSeconds, TabPriceEdit.Parse(text));
        var small = TabPriceEdit.With(null, "program_skipped", TabPriceEdit.Parse(text));
        Assert.Equal(TabPriceEdit.MinSeconds, TabPriceEdit.Effective("program_skipped", small));
        var back = TabPriceEdit.With(small, "program_skipped", TabPriceEdit.Parse(""));
        Assert.Equal(TabPrices.Find("program_skipped")!.Seconds, TabPriceEdit.Effective("program_skipped", back));
    }

    [Fact]
    public void An_edit_keeps_the_rows_sign_and_is_clamped_on_every_read()
    {
        var o = new Dictionary<string, int> { ["typo"] = 90, ["lockcard"] = 600, ["attention"] = 999999, ["crash"] = 1, ["leash"] = 5 };
        Assert.Equal(90, TabPriceEdit.Effective("typo", o));
        Assert.Equal(-600, TabPriceEdit.Effective("lockcard", o));   // an earn-back stays an earn-back
        Assert.Equal(TabPriceEdit.MaxSeconds, TabPriceEdit.Effective("attention", o));
        Assert.Equal(TabPriceEdit.MinSeconds, TabPriceEdit.Effective("crash", o));
        Assert.Equal(TabPrices.Find("leash")!.Seconds, TabPriceEdit.Effective("leash", o)); // a hand-edited file is ignored there
        Assert.Equal(-600, TabPriceEdit.Effective("session", o));    // no edit, the table
        Assert.Equal(-600, TabPriceEdit.Effective("session", null));
    }

    [Fact]
    public void With_sets_resets_and_drops_a_figure_equal_to_the_default()
    {
        var a = TabPriceEdit.With(null, "typo", 90);
        Assert.Equal(90, a["typo"]);
        var b = TabPriceEdit.With(a, "typo", 30);        // the default's own size
        Assert.False(b.ContainsKey("typo"));
        var c = TabPriceEdit.With(a, "typo", 0);         // empty box
        Assert.False(c.ContainsKey("typo"));
        var d = TabPriceEdit.With(a, "leash", 60);       // not editable: nothing stored
        Assert.False(d.ContainsKey("leash"));
        Assert.Equal(90, d["typo"]);
        var e = TabPriceEdit.With(null, "attention", 99999);
        Assert.Equal(TabPriceEdit.MaxSeconds, e["attention"]);
        Assert.NotSame(a, TabPriceEdit.With(a, "typo", 90)); // a new map every time
    }

    [Fact]
    public void Figures_move_only_while_no_lock_runs()
    {
        Assert.True(TabPriceEdit.CanEdit(false, LockLookup.Unlinked));
        Assert.True(TabPriceEdit.CanEdit(true, LockLookup.None));
        Assert.False(TabPriceEdit.CanEdit(true, LockLookup.Chosen));
        Assert.False(TabPriceEdit.CanEdit(true, LockLookup.Ambiguous));
        Assert.False(TabPriceEdit.CanEdit(true, LockLookup.Away));
    }

    [Fact]
    public void Resolve_books_the_edited_figure_only_for_a_row_that_is_on()
    {
        var on = new HashSet<string> { "typo", "lockcard" };
        var o = new Dictionary<string, int> { ["typo"] = 90, ["lockcard"] = 120, ["attention"] = 60 };
        Assert.Equal(270, TabPrices.Resolve("typo", on, units: 3, overrides: o));
        Assert.Equal(-120, TabPrices.Resolve("lockcard", on, overrides: o));
        Assert.Equal(0, TabPrices.Resolve("attention", on, overrides: o)); // switched off: nothing
        Assert.Equal(0, TabPrices.Resolve("panic", new HashSet<string> { "panic" }, overrides: new Dictionary<string, int> { ["panic"] = 60 }));
        // still inside the per-event ceiling the table always had
        Assert.Equal(TabLimits.MaxDailySeconds, TabPrices.Resolve("typo", on, units: 1000, overrides: new Dictionary<string, int> { ["typo"] = 3600 }));
    }

    [Fact]
    public void Every_editable_row_is_a_row_on_the_page()
    {
        var ids = TabPrices.All.Select(p => p.Id).ToHashSet();
        Assert.All(ids.Where(TabPriceEdit.Editable), id => Assert.True(TabMenuCopy.HasFixedFigure(id)));
    }

    private sealed class MemoryStore : IChasterTokenStore
    {
        public ChasterStoredTokens? Tokens;
        public ChasterStoredTokens? Read() => Tokens;
        public void Write(ChasterStoredTokens tokens) => Tokens = tokens;
        public void Clear() => Tokens = null;
    }

    [Fact]
    public void The_service_books_the_players_figure_and_the_day_limit_still_holds()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-priceedit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var utc = new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);
            var store = new MemoryStore { Tokens = new ChasterStoredTokens("AT", "RT", utc.AddSeconds(300)) };
            var options = new ChasterOptions(true, "lock1", new HashSet<string> { "attention" },
                Limits: TabLimits.FromMinutes(15, 60),
                PriceOverrides: new Dictionary<string, int> { ["attention"] = 600 });
            using var service = new ChasterService(new ChasterClient(), store, Path.Combine(dir, "tab.json"),
                () => options, () => utc, () => utc.ToLocalTime());

            Assert.Equal(600, service.Note("attention").AppliedSeconds);
            // the second one meets the 15:00 day limit: only 5:00 of it lands
            Assert.Equal(300, service.Note("attention").AppliedSeconds);
            Assert.Equal(900, service.BalanceSeconds);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
