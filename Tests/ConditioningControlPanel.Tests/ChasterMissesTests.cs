using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// "Circe misses you": only full days away count, the charge doubles from 5:00 and never passes
/// 60:00 for a day or the backlog cap in total, it lands on the tab and never on the lock the
/// same day, linking never charges for the past, the row is opt-in like every other, and the
/// first finished session after coming back forgives half.
/// </summary>
public class ChasterMissesTests : IDisposable
{
    private sealed class MemoryStore : IChasterTokenStore
    {
        public ChasterStoredTokens? Tokens = new("AT", "RT", DateTime.MaxValue);
        public ChasterStoredTokens? Read() => Tokens;
        public void Write(ChasterStoredTokens tokens) => Tokens = tokens;
        public void Clear() => Tokens = null;
    }

    private sealed class Chaster : HttpMessageHandler
    {
        public readonly List<string> Seen = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add(r.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-chaster-misses-" + Guid.NewGuid().ToString("N"));
    private readonly Chaster _http = new();
    private readonly MemoryStore _store = new();
    private DateTime _local = new(2026, 9, 21, 10, 0, 0);
    private ChasterOptions _options = new(true, "lock1", new HashSet<string> { CircesMisses.EventId });

    public ChasterMissesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { } // swallow: temp dir, best effort
    }

    private ChasterService Make() =>
        new(new ChasterClient(_http), _store, Path.Combine(_dir, "tab.json"), () => _options, () => _local.ToUniversalTime(), () => _local);

    [Theory]
    [InlineData("2026-09-21", 0)] // today
    [InlineData("2026-09-20", 0)] // yesterday: no full day missed
    [InlineData("2026-09-19", 1)]
    [InlineData("2026-09-14", 6)]
    [InlineData("2026-01-01", CircesMisses.MaxDaysCounted)]
    [InlineData("2026-09-25", 0)] // a clock set backwards
    [InlineData("not a day", 0)]
    [InlineData(null, 0)]
    public void Only_full_days_away_count(string? lastSeen, int expected)
    {
        Assert.Equal(expected, CircesMisses.DaysAway(lastSeen, new DateTime(2026, 9, 21, 10, 0, 0)));
    }

    [Fact]
    public void It_doubles_from_five_minutes_and_never_passes_an_hour_for_one_day()
    {
        Assert.Equal(new[] { 300, 600, 1200, 2400, 3600, 3600 }, CircesMisses.Charges(6));
        Assert.Empty(CircesMisses.Charges(0));
        Assert.Equal(450, CircesMisses.Forgivable(900));
    }

    [Fact]
    public void Coming_back_books_every_day_away_and_a_session_forgives_half()
    {
        using (var before = Make()) before.NoteSeen();
        _local = _local.AddDays(4); // three full days away

        using var service = Make();
        var seen = new List<(string, int)>();
        service.Booked += (id, b) => seen.Add((id, b.AppliedSeconds));

        Assert.Equal(300 + 600 + 1200, service.NoteSeen());
        Assert.Equal(0, service.NoteSeen()); // once a day
        Assert.Equal(2100, service.BalanceSeconds);

        service.Note("session"); // the session row itself is off; forgiveness does not need it
        Assert.Equal(1050, service.BalanceSeconds);
        service.Note("session");
        Assert.Equal(1050, service.BalanceSeconds);
        Assert.Equal(new[] { (CircesMisses.EventId, 2100), (CircesMisses.ForgivenEventId, -1050) }, seen);
    }

    [Fact]
    public void A_month_away_is_the_backlog_cap_and_not_a_month()
    {
        using (var before = Make()) before.NoteSeen();
        _local = _local.AddDays(40);
        using var service = Make();

        service.NoteSeen();

        Assert.Equal(CircesTab.BacklogCapSeconds, service.BalanceSeconds);
    }

    [Fact]
    public void The_row_is_opt_in_and_linking_never_charges_for_the_past()
    {
        _options = _options with { Prices = new HashSet<string>() };
        using (var before = Make()) before.NoteSeen();
        _local = _local.AddDays(10);
        using (var off = Make()) Assert.Equal(0, off.NoteSeen());

        // Switched on today: the ten days before it stay free, and so does tomorrow.
        _options = _options with { Prices = new HashSet<string> { CircesMisses.EventId } };
        _local = _local.AddDays(1);
        using var on = Make();
        Assert.Equal(0, on.NoteSeen());
        Assert.False(on.Note(CircesMisses.EventId).Booked); // nothing but NoteSeen books this row
    }

    [Fact]
    public async Task What_being_away_cost_waits_on_the_tab_until_tomorrow_even_across_a_restart()
    {
        using (var before = Make()) before.NoteSeen();
        _local = _local.AddDays(3);

        using (var back = Make())
        {
            Assert.Equal(SettleOutcome.Nothing, await back.SettleIfNewDayAsync());
            Assert.Equal(900, back.BalanceSeconds);
        }
        using (var restarted = Make())
        {
            Assert.Equal(SettleOutcome.Nothing, await restarted.SettleIfNewDayAsync());
            Assert.Empty(_http.Seen);
        }

        _local = _local.AddDays(1);
        using var tomorrow = Make();
        Assert.Equal(SettleOutcome.Pushed, await tomorrow.SettleIfNewDayAsync());
        Assert.Contains("/locks/lock1/update-time", _http.Seen);
    }

    [Fact]
    public void Nobody_linked_means_nothing_is_tracked_at_all()
    {
        _store.Tokens = null;
        using var service = Make();

        Assert.Equal(0, service.NoteSeen());
        Assert.False(File.Exists(Path.Combine(_dir, "tab.json")));
    }
}
