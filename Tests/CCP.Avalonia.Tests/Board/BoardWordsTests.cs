using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Board;
using Xunit;

namespace CCP.Avalonia.Tests.Board;

/// <summary>
/// The card's words (owner, 2026-10-09: the Tip title "The app can read t..." was cut to one line
/// and the words flickered in as the card arrived). A title that needs two lines gets two.
/// </summary>
public sealed class BoardWordsTests
{
    private sealed class OneProvider : IBillboardProvider
    {
        public List<BillboardCardSpec> Cards = new();
        public string Id => "fake";
        public IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Cards;
        public void Invoke(string actionTarget) { }
        public event EventHandler? Changed { add { } remove { } }
    }

    private static BillboardCardSpec Tip(string id, string title) =>
        new(id, BillboardCardKind.Tip, 0, "did you know", title,
            "keyword triggers fire when a word shows up on your screen", "#9a7cff", "nothing-registered", null,
            new BillboardAction(BillboardActionKind.Callback, "go:" + id, "Show me"));

    [Fact]
    public Task A_two_line_title_keeps_both_lines() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        BoardHeadTests.EnsureApp();
        BoardHeadTests.Pin(particles: false);
        try
        {
            var p = new OneProvider();
            p.Cards.Add(Tip("tip.a", "The app can read the room"));
            p.Cards.Add(Tip("tip.b", "The app can read your screen too"));
            var deck = new BillboardDeck(() => new IBillboardProvider[] { p }, () => new BillboardContext(BillboardTier.Free, DateTime.UtcNow, DateTime.Now));
            var host = new BillboardDeckView(deck);
            host.Tweens.NowForTests = 0;
            // A Home billboard narrow enough that the title needs two lines.
            var w = new Window { Width = 820, Height = 460, Content = host };
            w.Show();
            host.Begin();
            Dispatcher.UIThread.RunJobs();

            var title = host.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "The app can read the room");
            var layout = title.TextLayout;
            Assert.True(layout.TextLines.Count >= 2, $"title laid out in {layout.TextLines.Count} line(s) at {title.FontSize:0} px");
            Assert.DoesNotContain(layout.TextLines, l => l.HasCollapsed);

            // The arrival of the next card: the words rise with no frame where a line pops in at full
            // opacity before its fade starts.
            var dir = Environment.GetEnvironmentVariable("CCP_BOARD_FRAMES");
            host.Tweens.NowForTests = DashboardBillboard.HoldSeconds * 1000.0 + 5;
            host.Tweens.Step();
            Dispatcher.UIThread.RunJobs();
            var words = host.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Text is "did you know" or "The app can read your screen too" || (t.Text ?? "").StartsWith("keyword")).ToList();
            var seen = words.ToDictionary(t => t, _ => new List<double>());
            for (int f = 0; f < 50; f++)
            {
                host.Tweens.NowForTests = DashboardBillboard.HoldSeconds * 1000.0 + 5 + f * 16.7;
                host.Tweens.Step();
                Dispatcher.UIThread.RunJobs();
                foreach (var t in words) seen[t].Add(t.Opacity);
                if (dir != null)
                {
                    var sb = new System.Text.StringBuilder($"f{f:00}");
                    foreach (var t in words)
                    {
                        double eff = 1; global::Avalonia.Visual? v = t;
                        while (v != null && v != host) { eff *= v.Opacity; v = v.GetVisualParent(); }
                        sb.Append($" | {(t.Text ?? "").Substring(0, 4)} own {t.Opacity:0.00} eff {eff:0.00} vis {t.IsEffectivelyVisible}");
                    }
                    File.AppendAllText(Path.Combine(dir, "ops.txt"), sb + Environment.NewLine);
                }
                if (dir != null && f % 3 == 0)
                    w.CaptureRenderedFrame()?.Save(Path.Combine(dir, $"board-{f:00}.png"));
            }
            foreach (var (t, ops) in seen)
            {
                // Once a line starts to show it never drops back (a flicker).
                // Opacity above 1 draws nearly transparent in Avalonia (the overshoot flicker).
                Assert.All(ops, o => Assert.InRange(o, 0, 1));
                for (int i = 1; i < ops.Count; i++)
                    Assert.True(ops[i] >= ops[i - 1] - 0.02, $"'{t.Text}' opacity fell {ops[i - 1]:0.00} -> {ops[i]:0.00} at frame {i}");
            }
            w.Close();
        }
        finally { BoardHeadTests.Unpin(); }
        return Task.CompletedTask;
    });
}
