using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Circe's mood (tab wave 1, 2026-09-29): the heat modifier drawn as CALM / WARM / HOT / SMOKING,
/// the factor the next repeat pays. A credit cools her one step; midnight resets her.
/// </summary>
public class CircesMoodTests : IDisposable
{
    private sealed class MemoryStore : IChasterTokenStore
    {
        public ChasterStoredTokens? Tokens;
        public ChasterStoredTokens? Read() => Tokens;
        public void Write(ChasterStoredTokens tokens) => Tokens = tokens;
        public void Clear() => Tokens = null;
    }

    private sealed class NoContent : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(r.RequestUri!.AbsolutePath == "/chaster/refresh"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"AT\",\"expires_in\":300}") }
                : new HttpResponseMessage(HttpStatusCode.NoContent));
    }

    private const string Today = "2026-09-29";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-mood-" + Guid.NewGuid().ToString("N"));
    private readonly MemoryStore _store = new();
    private DateTime _local = new(2026, 9, 29, 10, 0, 0, DateTimeKind.Local);
    private ChasterOptions _options = new(true, "lock1",
        new HashSet<string> { "typo", "attention", "quest", "session", "escape", TabDayEnd.HeatId });

    public CircesMoodTests()
    {
        Directory.CreateDirectory(_dir);
        _store.Tokens = new ChasterStoredTokens("AT", "RT", DateTime.UtcNow.AddYears(1));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { } // swallow: temp dir, best effort
    }

    private ChasterService Make() =>
        new(new ChasterClient(new NoContent()), _store, Path.Combine(_dir, "tab.json"), () => _options,
            () => _local.ToUniversalTime(), () => _local);

    private static TabState Heat(int hottest, int cool = 0, string day = Today) => new()
    {
        HeatDay = day,
        Heat = new Dictionary<string, int> { ["attention"] = hottest, ["typo"] = Math.Min(1, hottest) },
        HeatCool = cool,
    };

    // ---------------- the pure part ----------------

    [Theory]
    [InlineData(0, MoodLevel.Calm, 1.0)]
    [InlineData(1, MoodLevel.Warm, 1.5)]
    [InlineData(2, MoodLevel.Hot, 2.25)]
    [InlineData(3, MoodLevel.Smoking, 3.0)]
    [InlineData(9, MoodLevel.Smoking, 3.0)]
    public void The_mood_is_the_factor_the_next_repeat_pays(int hottest, MoodLevel level, double factor)
    {
        var mood = CircesMood.Of(Heat(hottest), Today);
        Assert.Equal(level, mood.Level);
        Assert.Equal(factor, mood.Factor, 3);
        // The meter and the price agree: a repeat at this mood costs exactly the factor.
        Assert.Equal((int)Math.Round(300 * factor), TabDayEnd.Heated(300, Math.Min(hottest, 3)));
    }

    [Fact]
    public void The_factor_reads_with_the_times_sign()
    {
        Assert.Equal("×1", CircesMood.FromStep(0).FactorText);
        Assert.Equal("×1.5", CircesMood.FromStep(1).FactorText);
        Assert.Equal("×2.25", CircesMood.FromStep(2).FactorText);
        Assert.Equal("×3", CircesMood.FromStep(3).FactorText);
        Assert.Equal("chaster_mood_smoking", CircesMood.FromStep(3).WordKey);
    }

    [Fact]
    public void Rows_heat_never_touches_do_not_make_her_hot()
    {
        var state = new TabState
        {
            HeatDay = Today,
            Heat = new Dictionary<string, int> { ["escape"] = 3, [TabDayEnd.IdleEventId] = 2, [CircesMisses.EventId] = 5 },
        };
        Assert.Equal(MoodLevel.Calm, CircesMood.Of(state, Today).Level);
    }

    [Fact]
    public void Yesterdays_heat_is_calm_today() =>
        Assert.Equal(MoodLevel.Calm, CircesMood.Of(Heat(3, day: "2026-09-28"), Today).Level);

    [Fact]
    public void A_credit_cools_one_step_and_never_past_the_hottest_row()
    {
        var state = Heat(2);
        CircesMood.Cool(state, Today);
        Assert.Equal(1, state.HeatCool);
        Assert.Equal(MoodLevel.Warm, CircesMood.Of(state, Today).Level);
        CircesMood.Cool(state, Today);
        CircesMood.Cool(state, Today);
        CircesMood.Cool(state, Today);
        Assert.Equal(2, state.HeatCool);
        Assert.Equal(MoodLevel.Calm, CircesMood.Of(state, Today).Level);
    }

    [Fact]
    public void A_credit_on_a_fresh_day_cools_nothing()
    {
        var state = Heat(3, cool: 2, day: "2026-09-28");
        CircesMood.Cool(state, Today);
        Assert.Equal(Today, state.HeatDay);
        Assert.Equal(0, state.HeatCool);
        Assert.Empty(state.Heat!);
    }

    [Fact]
    public void A_cool_read_off_disk_below_zero_is_not_believed()
    {
        Assert.Equal(2, CircesMood.Effective(2, -5));
        Assert.Equal(MoodLevel.Hot, CircesMood.Of(Heat(2, cool: -5), Today).Level);
        Assert.Equal(0, CircesMood.Effective(1, 3));
    }

    // ---------------- through the service ----------------

    [Fact]
    public void Repeats_warm_her_up_a_step_at_a_time()
    {
        using var service = Make();
        Assert.Equal(MoodLevel.Calm, service.Mood!.Value.Level);
        service.Note("attention");
        Assert.Equal(MoodLevel.Warm, service.Mood!.Value.Level);
        service.Note("attention");
        Assert.Equal(MoodLevel.Hot, service.Mood!.Value.Level);
        service.Note("attention");
        Assert.Equal(MoodLevel.Smoking, service.Mood!.Value.Level);
    }

    [Fact]
    public void A_credit_cools_her_and_the_next_repeat_is_cheaper()
    {
        using var service = Make();
        service.Note("attention"); // 300, WARM
        service.Note("attention"); // 450, HOT
        service.Note("quest");     // -300, cools a step: WARM
        Assert.Equal(MoodLevel.Warm, service.Mood!.Value.Level);
        var before = service.BalanceSeconds;
        service.Note("attention"); // priced at x1.5, not x2.25
        Assert.Equal(450, service.BalanceSeconds - before);
    }

    [Fact]
    public void She_resets_at_midnight()
    {
        using var service = Make();
        service.Note("attention");
        service.Note("attention");
        service.Note("quest");
        _local = _local.AddDays(1);
        Assert.Equal(MoodLevel.Calm, service.Mood!.Value.Level);
        var before = service.BalanceSeconds;
        service.Note("attention");
        Assert.Equal(300, service.BalanceSeconds - before);
        Assert.Equal(MoodLevel.Warm, service.Mood!.Value.Level);
    }

    [Fact]
    public void Heat_off_means_no_mood_at_all()
    {
        _options = _options with { Prices = new HashSet<string> { "attention", "quest" } };
        using var service = Make();
        Assert.Null(service.Mood);
        service.Note("attention");
        Assert.Null(service.Mood);
    }

    [Fact]
    public void No_mood_when_the_tab_is_off_or_unlinked()
    {
        using (var service = Make())
        {
            _options = _options with { TabEnabled = false };
            Assert.Null(service.Mood);
        }
        _options = _options with { TabEnabled = true };
        _store.Tokens = null;
        using var unlinked = Make();
        Assert.Null(unlinked.Mood);
    }

    [Fact]
    public void A_change_raises_before_and_after_and_a_repeat_at_the_top_raises_nothing()
    {
        using var service = Make();
        var seen = new List<(MoodLevel, MoodLevel)>();
        service.MoodChanged += (a, b) => seen.Add((a.Level, b.Level));
        service.Note("attention");
        service.Note("attention");
        service.Note("attention");
        service.Note("attention"); // four counts, still SMOKING: nothing raised
        service.Note("quest");     // four counts less one cool is three: still SMOKING
        service.Note("quest");     // less two: HOT
        Assert.Equal(new[]
        {
            (MoodLevel.Calm, MoodLevel.Warm),
            (MoodLevel.Warm, MoodLevel.Hot),
            (MoodLevel.Hot, MoodLevel.Smoking),
            (MoodLevel.Smoking, MoodLevel.Hot),
        }, seen);
    }

    [Fact]
    public void Heat_off_raises_no_mood_change()
    {
        _options = _options with { Prices = new HashSet<string> { "attention" } };
        using var service = Make();
        var raised = 0;
        service.MoodChanged += (_, _) => raised++;
        service.Note("attention");
        service.Note("attention");
        Assert.Equal(0, raised);
    }

    /// <summary>TAB-5. The rows that name their own size (the leash, a lost stake, an Awareness
    /// watcher) book through NoteSeconds and are never heated, so they must not warm her either:
    /// the meter would read SMOKING x3 while nothing costs more.</summary>
    [Fact]
    public void Rows_that_name_their_own_size_never_warm_her()
    {
        _options = _options with
        {
            Prices = new HashSet<string> { "typo", "leash", "watcher", Services.Stakes.StakeRules.LossRowId, TabDayEnd.HeatId },
            Limits = TabLimits.FromMinutes(720, 2880),
        };
        using var service = Make();
        var seen = new List<(MoodLevel, MoodLevel)>();
        service.MoodChanged += (a, b) => seen.Add((a.Level, b.Level));

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(900, service.NoteSeconds("leash", 900).AppliedSeconds);
            Assert.Equal(600, service.NoteSeconds("watcher", 600).AppliedSeconds);
            Assert.Equal(900, service.NoteSeconds(Services.Stakes.StakeRules.LossRowId, 900).AppliedSeconds);
        }

        Assert.Equal(MoodLevel.Calm, service.Mood!.Value.Level);
        Assert.Empty(seen);
        // A row off the table still warms her from its first slip, at its list price.
        Assert.Equal(30, service.Note("typo").AppliedSeconds);
        Assert.Equal(MoodLevel.Warm, service.Mood!.Value.Level);
    }
}

/// <summary>The meter as it builds: hidden with no mood, the word and the fill for each level.</summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class CircesMoodMeterRenderTests
{
    private static void OnMeter(Action<CircesMoodMeter> body) => WpfRenderHarness.OnStaThread(() =>
    {
        var meter = new CircesMoodMeter();
        var host = new Grid { Width = 120, Height = 220 };
        host.Children.Add(meter);
        host.Measure(new Size(120, 220));
        host.Arrange(new Rect(new Point(0, 0), new Size(120, 220)));
        host.UpdateLayout();
        body(meter);
    });

    [Fact]
    public void No_mood_hides_the_meter() => OnMeter(meter =>
    {
        meter.Apply(null);
        Assert.Equal(Visibility.Collapsed, meter.Visibility);
    });

    [Fact]
    public void Each_level_shows_and_fills_further() => OnMeter(meter =>
    {
        var covers = new List<double>();
        for (var step = 0; step <= CircesMood.MaxStep; step++)
        {
            var mood = CircesMood.FromStep(step);
            meter.Apply(mood);
            Assert.Equal(Visibility.Visible, meter.Visibility);
            Assert.False(string.IsNullOrEmpty(meter.Word));
            covers.Add(meter.CoverHeight(mood));
        }
        for (var i = 1; i < covers.Count; i++) Assert.True(covers[i] < covers[i - 1]);
        Assert.Equal(0, covers[^1], 3);
        meter.Apply(null);
        Assert.Equal(Visibility.Collapsed, meter.Visibility);
    });
}
