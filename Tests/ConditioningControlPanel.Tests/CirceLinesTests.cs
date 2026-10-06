using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services.Chaster;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Circe's lines (tab wave 1, 2026-09-29): one line per moment, never the same twice in a row,
/// at most one every 20 s, a landed push and the bill always speak.
/// </summary>
public class CirceLinesTests
{
    private static readonly DateTime T0 = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CirceMoment[] Moments = (CirceMoment[])Enum.GetValues(typeof(CirceMoment));

    [Fact]
    public void A_moment_never_says_the_same_line_twice_in_a_row_and_uses_every_line()
    {
        foreach (var moment in Moments)
        {
            var lines = new CirceLines(new Random(7));
            var seen = new HashSet<string>();
            string? last = null;
            for (var i = 0; i < 200; i++)
            {
                var key = lines.Pick(moment, T0.AddSeconds(i * 30));
                Assert.NotNull(key);
                Assert.NotEqual(last, key);
                seen.Add(key!);
                last = key;
            }
            Assert.Equal(CirceLines.Variants(moment), seen.Count);
        }
    }

    [Fact]
    public void The_same_seed_says_the_same_lines()
    {
        var a = new CirceLines(new Random(42));
        var b = new CirceLines(new Random(42));
        for (var i = 0; i < 30; i++)
        {
            var moment = Moments[i % Moments.Length];
            Assert.Equal(a.Pick(moment, T0.AddMinutes(i)), b.Pick(moment, T0.AddMinutes(i)));
        }
    }

    [Fact]
    public void One_line_every_twenty_seconds_and_landed_always_speaks()
    {
        var lines = new CirceLines(new Random(1));
        Assert.NotNull(lines.Pick(CirceMoment.Popped, T0));
        Assert.Null(lines.Pick(CirceMoment.Warmer, T0.AddSeconds(1)));
        Assert.Null(lines.Pick(CirceMoment.Held, T0.AddSeconds(19.9)));
        Assert.NotNull(lines.Pick(CirceMoment.Landed, T0.AddSeconds(5)));
        // Landed spoke at +5 s, so the clock starts again from there.
        Assert.Null(lines.Pick(CirceMoment.Held, T0.AddSeconds(24)));
        Assert.NotNull(lines.Pick(CirceMoment.Held, T0.AddSeconds(25)));
    }

    [Fact]
    public void The_bill_always_speaks()
    {
        var lines = new CirceLines(new Random(1));
        Assert.NotNull(lines.Pick(CirceMoment.Popped, T0));
        foreach (var bill in new[] { CirceMoment.BillCredit, CirceMoment.BillEven, CirceMoment.BillOwed, CirceMoment.BillBig })
        {
            Assert.True(CirceLines.AlwaysWins(bill));
            Assert.NotNull(lines.Pick(bill, T0.AddSeconds(1)));
        }
        Assert.False(CirceLines.AlwaysWins(CirceMoment.Popped));
    }

    [Theory]
    [InlineData(-3600, CirceMoment.BillCredit)]
    [InlineData(-60, CirceMoment.BillCredit)]
    [InlineData(-59, CirceMoment.BillEven)]
    [InlineData(0, CirceMoment.BillEven)]
    [InlineData(59, CirceMoment.BillEven)]
    [InlineData(60, CirceMoment.BillOwed)]
    [InlineData(1799, CirceMoment.BillOwed)]
    [InlineData(1800, CirceMoment.BillBig)]
    [InlineData(43200, CirceMoment.BillBig)]
    public void The_verdict_comes_from_the_net(int net, CirceMoment verdict) =>
        Assert.Equal(verdict, CirceLines.Verdict(net));

    [Fact]
    public void Only_a_held_credit_and_a_popped_cost_have_a_line()
    {
        Assert.Equal(CirceMoment.Held, CirceLines.ForBooking("natasha_held", -60));
        Assert.Equal(CirceMoment.Popped, CirceLines.ForBooking("natasha", 300));
        Assert.Null(CirceLines.ForBooking("natasha_held", 60));
        Assert.Null(CirceLines.ForBooking("natasha", -300));
        Assert.Null(CirceLines.ForBooking("typo", 30));
        Assert.Null(CirceLines.ForBooking(null, 30));
    }

    /// <summary>TAB-9. A red flash whose ring ran out books the same row as a popped red bubble,
    /// but the player never touched it, so her "popped" lines would mock a pop nobody made. A
    /// booking nobody prompted has no line.</summary>
    [Fact]
    public void A_ring_that_ran_out_has_no_line()
    {
        Assert.Null(CirceLines.ForBooking("natasha", new TabBooking(300, TabRefusal.None) { Unprompted = true }));
        Assert.Equal(CirceMoment.Popped, CirceLines.ForBooking("natasha", new TabBooking(300, TabRefusal.None)));
        Assert.Equal(CirceMoment.Held, CirceLines.ForBooking("natasha_held", new TabBooking(-60, TabRefusal.None)));
    }

    [Fact]
    public void A_mood_that_moves_up_is_warmer_and_down_is_cooler()
    {
        Assert.Equal(CirceMoment.Warmer, CirceLines.ForMood(CircesMood.FromStep(0), CircesMood.FromStep(1)));
        Assert.Equal(CirceMoment.Cooler, CirceLines.ForMood(CircesMood.FromStep(3), CircesMood.FromStep(2)));
        Assert.Null(CirceLines.ForMood(CircesMood.FromStep(2), CircesMood.FromStep(2)));
    }

    // ---------------- the copy ----------------

    private static string LanguagesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("repo root not found"),
            "ConditioningControlPanel", "Localization", "Languages");
    }

    [Fact]
    public void Every_line_is_in_all_nine_languages()
    {
        var files = Directory.GetFiles(LanguagesDir(), "*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
        {
            var json = JObject.Parse(File.ReadAllText(file));
            foreach (var key in CirceLines.AllKeys())
                Assert.False(string.IsNullOrWhiteSpace((string?)json[key]), $"{Path.GetFileName(file)} is missing {key}");
        }
    }

    [Fact]
    public void Circe_keeps_it_short_neutral_and_calm()
    {
        var json = JObject.Parse(File.ReadAllText(Path.Combine(LanguagesDir(), "en.json")));
        var gendered = new Regex(@"\b(girl|boy|he|she|him|her|his|hers|good girl|good boy|sissy|bimbo)\b", RegexOptions.IgnoreCase);
        foreach (var key in CirceLines.AllKeys())
        {
            var line = (string)json[key]!;
            Assert.True(line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 8, $"{key} runs long: {line}");
            Assert.DoesNotContain("!", line);
            Assert.DoesNotContain("—", line);
            Assert.DoesNotContain("–", line);
            Assert.False(gendered.IsMatch(line), $"{key} is not neutral: {line}");
        }
    }
}

/// <summary>A push that reaches the lock raises PushLanded, which is what the landed line hangs off.</summary>
public class CirceLandedTests : IDisposable
{
    private sealed class MemoryStore : IChasterTokenStore
    {
        public ChasterStoredTokens? Tokens = new("AT", "RT", DateTime.UtcNow.AddYears(1));
        public ChasterStoredTokens? Read() => Tokens;
        public void Write(ChasterStoredTokens tokens) => Tokens = tokens;
        public void Clear() => Tokens = null;
    }

    private sealed class Answer : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.NoContent;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(Status));
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-landed-" + Guid.NewGuid().ToString("N"));
    private readonly Answer _answer = new();

    public CirceLandedTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { } // swallow: temp dir, best effort
    }

    private ChasterService Make() => new(new ChasterClient(_answer), new MemoryStore(), Path.Combine(_dir, "tab.json"),
        () => new ChasterOptions(true, "lock1", new HashSet<string> { "attention" }));

    [Fact]
    public async Task A_push_that_lands_is_announced_with_its_seconds()
    {
        using var service = Make();
        var landed = new List<int>();
        service.PushLanded += s => landed.Add(s);
        service.Note("attention");
        Assert.Equal(SettleOutcome.Pushed, await service.SettleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new[] { 300 }, landed);
    }

    [Fact]
    public async Task A_push_Chaster_refuses_is_not()
    {
        _answer.Status = HttpStatusCode.BadRequest;
        using var service = Make();
        var landed = 0;
        service.PushLanded += _ => landed++;
        service.Note("attention");
        Assert.NotEqual(SettleOutcome.Pushed, await service.SettleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, landed);
    }
}

/// <summary>The bubble and the bill's verdict as they build.</summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class CirceSaysRenderTests
{
    [Fact]
    public void The_bubble_says_a_line_and_hushes() => WpfRenderHarness.OnStaThread(() =>
    {
        var says = new CirceSays();
        Assert.Equal(Visibility.Collapsed, says.Visibility);
        says.Say("Look at you, resisting.");
        Assert.Equal(Visibility.Visible, says.Visibility);
        Assert.Equal("Look at you, resisting.", says.Text);
        var host = new Grid { Width = 400, Height = 200 };
        host.Children.Add(says);
        host.Measure(new Size(400, 200));
        host.Arrange(new Rect(0, 0, 400, 200));
        Assert.True(says.ActualWidth > 0 && says.ActualWidth <= 230);
    });

    [Fact]
    public void The_bill_carries_a_verdict_that_holds_still_across_a_repaint() => WpfRenderHarness.OnStaThread(() =>
    {
        var start = new DateTime(2026, 9, 29, 8, 0, 0, DateTimeKind.Utc);
        var owed = TabBill.Build(new[] { new TabEntry { AtUtc = start.AddMinutes(1), EventId = "attention", Seconds = 300 } }, start, 0);
        var receipt = new ChasterReceiptView();
        receipt.Show(owed);
        var first = receipt.VerdictText;
        Assert.False(string.IsNullOrEmpty(first));
        for (var i = 0; i < 5; i++)
        {
            receipt.Show(owed);
            Assert.Equal(first, receipt.VerdictText);
        }
        receipt.Show(TabBill.Build(Array.Empty<TabEntry>(), start, 0));
        Assert.Equal(string.Empty, receipt.VerdictText);
    });
}
