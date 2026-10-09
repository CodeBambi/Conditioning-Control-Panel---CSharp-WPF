using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A Strict leash offers the holder "Chaster time" when the leashed side's day report says
/// <c>chaster_linked</c> (LeashUiRules.VisiblePunishments). The leashed side books it on its own
/// "leash" row (ChasterService.NoteSeconds), which no preset carries and nothing switches on. So the
/// report must say linked only when that time can land: Chaster linked, Circe's Tab on, the "leash"
/// row on. Otherwise the holder sends 15:00, reads it done, and the lock never moves.
/// </summary>
[Collection(AppChasterCollection.Name)]
public class LeashChasterOfferTests
{
    private sealed class MemoryStore : IChasterTokenStore
    {
        public ChasterStoredTokens? Tokens;
        public ChasterStoredTokens? Read() => Tokens;
        public void Write(ChasterStoredTokens tokens) => Tokens = tokens;
        public void Clear() => Tokens = null;
    }

    [Theory]
    [InlineData(TabPresets.Gentle, false)]
    [InlineData(TabPresets.Strict, false)]
    [InlineData(TabPresets.Circe, false)]
    [InlineData(TabPresets.Gentle, true)]
    [InlineData(TabPresets.Strict, true)]
    [InlineData(TabPresets.Circe, true)]
    public void The_holder_is_offered_chaster_time_only_when_it_books(string presetId, bool leashRowOn)
    {
        var rows = new HashSet<string>(TabPresets.Find(presetId)!.PriceIds, StringComparer.Ordinal);
        if (leashRowOn) rows.Add("leash");
        WithAppChaster(new ChasterOptions(true, "lock1", rows), linked: true, service =>
        {
            var offered = LeashServiceApp.AppDayInputs()!.Value.ChasterLinked;
            var booked = service.NoteSeconds("leash", LeashChasterRule.PunishSeconds(900, LeashIntensity.Strict)).AppliedSeconds;

            Assert.Equal(leashRowOn, offered);
            Assert.True(offered == booked > 0,
                $"preset {presetId}, leash row {(leashRowOn ? "on" : "off")}: the holder is offered Chaster time {offered}, a 15:00 books {booked}s");
        });
    }

    [Fact]
    public void The_tab_switched_off_offers_no_chaster_time()
    {
        WithAppChaster(new ChasterOptions(false, "lock1", new HashSet<string> { "leash" }), linked: true,
            _ => Assert.False(LeashServiceApp.AppDayInputs()!.Value.ChasterLinked));
    }

    [Fact]
    public void An_unlinked_tab_offers_no_chaster_time()
    {
        WithAppChaster(new ChasterOptions(true, "lock1", new HashSet<string> { "leash" }), linked: false,
            _ => Assert.False(LeashServiceApp.AppDayInputs()!.Value.ChasterLinked));
    }

    /// <summary>Runs <paramref name="body"/> with a real, linked (or not) ChasterService standing in
    /// for <c>App.Chaster</c>, which is what the day report reads. Nothing is touched outside a temp folder.</summary>
    private static void WithAppChaster(ChasterOptions options, bool linked, Action<ChasterService> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-leash-offer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var prop = typeof(App).GetProperty(nameof(App.Chaster))!;
        var before = prop.GetValue(null);
        try
        {
            var utc = new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);
            var store = new MemoryStore { Tokens = linked ? new ChasterStoredTokens("AT", "RT", utc.AddSeconds(300)) : null };
            using var service = new ChasterService(new ChasterClient(), store, Path.Combine(dir, "tab.json"),
                () => options, () => utc, () => utc.ToLocalTime());
            prop.SetValue(null, service);
            body(service);
        }
        finally
        {
            prop.SetValue(null, before);
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }
}

/// <summary><c>App.Chaster</c> is process-wide; the suite that stands a service in for it runs alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class AppChasterCollection
{
    public const string Name = "App.Chaster static";
}
