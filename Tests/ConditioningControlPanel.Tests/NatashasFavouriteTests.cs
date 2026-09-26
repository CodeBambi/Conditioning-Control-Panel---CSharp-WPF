using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Natasha's favourite: about one bubble in ten and one flash in ten wears a faint red, and the
/// one popped or seen is 5:00 on the tab. The roll, the price row, the presets that carry it,
/// the blink envelope and the "can this even charge" gate the cue is dealt behind.
/// </summary>
public class NatashasFavouriteTests
{
    [Fact]
    public void One_in_ten_over_a_long_run()
    {
        var rng = new Random(20260922);
        var hits = Enumerable.Range(0, 40_000).Count(_ => NatashasFavourite.Roll(rng));
        var rate = hits / 40_000.0;
        Assert.InRange(rate, 0.085, 0.115);
    }

    [Fact]
    public void The_row_is_three_minutes_free_and_opt_in_like_every_other()
    {
        var row = TabPrices.Find("natasha");
        Assert.NotNull(row);
        Assert.Equal(300, row!.Seconds);
        Assert.Equal(TabPriceGate.Free, row.Gate);
        Assert.False(row.PerUnit);

        Assert.Equal(0, TabPrices.Resolve("natasha", new HashSet<string>()));
        Assert.Equal(300, TabPrices.Resolve("natasha", new HashSet<string> { "natasha" }));
    }

    [Fact]
    public void Strict_and_Circe_carry_it_Gentle_does_not()
    {
        Assert.Contains("natasha", TabPresets.Find(TabPresets.Strict)!.PriceIds);
        Assert.Contains("natasha", TabPresets.Find(TabPresets.Circe)!.PriceIds);
        Assert.DoesNotContain("natasha", TabPresets.Find(TabPresets.Gentle)!.PriceIds);
    }

    [Fact]
    public void The_blink_is_short_and_subtle()
    {
        // Sample one period finely: the wash is lit for a small slice of it, never past its
        // peak, never negative, and dark for the whole tail.
        const int steps = 2700;
        var lit = 0;
        for (var i = 0; i < steps; i++)
        {
            var t = i * NatashasFavourite.BlinkPeriodSec / steps;
            var a = NatashasFavourite.WashAlphaAt(t);
            Assert.InRange(a, 0, NatashasFavourite.WashPeak + 1e-9);
            if (a > 0.003) lit++;
        }
        var litShare = lit / (double)steps;
        Assert.InRange(litShare, 0.04, 0.12);
        Assert.Equal(0, NatashasFavourite.WashAlphaAt(1.5));
        Assert.Equal(0, NatashasFavourite.WashAlphaAt(-1));
        Assert.Equal(0, NatashasFavourite.WashAlphaAt(double.NaN));

        // Two pulses, each peaking at the wash peak.
        Assert.Equal(NatashasFavourite.WashPeak, NatashasFavourite.WashAlphaAt(NatashasFavourite.PulseSec / 2), 6);
        Assert.Equal(NatashasFavourite.WashPeak,
            NatashasFavourite.WashAlphaAt(NatashasFavourite.PulseSec + NatashasFavourite.PulseGapSec + NatashasFavourite.PulseSec / 2), 6);
        // And the halo stays under the lucky gold, so it never reads as a prize.
        Assert.True(NatashasFavourite.HaloOpacity < 0.55);
    }

    [Fact]
    public void The_blink_never_lines_up_with_the_other_pulses()
    {
        // The drain breathes and the magnet pulses on their own periods; a field of all three
        // must not blink in step. A period that is not a small-integer multiple of 0.8 or 1.0.
        var p = NatashasFavourite.BlinkPeriodSec;
        Assert.NotEqual(0, Math.Round(p / 0.8 % 1, 3));
        Assert.NotEqual(0, Math.Round(p / 1.0 % 1, 3));
    }

    private sealed class MemoryStore : IChasterTokenStore
    {
        public ChasterStoredTokens? Tokens;
        public ChasterStoredTokens? Read() => Tokens;
        public void Write(ChasterStoredTokens tokens) => Tokens = tokens;
        public void Clear() => Tokens = null;
    }

    [Fact]
    public void The_cue_is_only_dealt_when_the_price_can_land()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-natasha-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var utc = new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);
            var store = new MemoryStore { Tokens = new ChasterStoredTokens("AT", "RT", utc.AddSeconds(300)) };
            var options = new ChasterOptions(true, "lock1", new HashSet<string> { "natasha", "typo" });
            using var service = new ChasterService(new ChasterClient(), store, Path.Combine(dir, "tab.json"),
                () => options, () => utc, () => utc.ToLocalTime());

            Assert.True(service.CanBook("natasha"));
            Assert.True(service.CanBook("typo"));
            Assert.False(service.CanBook("session"));      // row off
            Assert.False(service.CanBook("panic"));        // never priced
            Assert.False(service.CanBook("nonesuch"));     // not on the table
            Assert.False(service.CanBook(""));

            // A panic press: nothing wears red for the next ten minutes.
            service.NoteSafetyExit();
            Assert.False(service.CanBook("natasha"));
            utc = utc.AddMinutes(11);
            Assert.True(service.CanBook("natasha"));

            // Popping the red one is the row's price, once, like any other event.
            var booking = service.Note("natasha");
            Assert.True(booking.Booked);
            Assert.Equal(300, service.BalanceSeconds);

            options = options with { TabEnabled = false };
            Assert.False(service.CanBook("natasha"));
            options = options with { TabEnabled = true };
            store.Tokens = null;
            Assert.False(service.CanBook("natasha"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
