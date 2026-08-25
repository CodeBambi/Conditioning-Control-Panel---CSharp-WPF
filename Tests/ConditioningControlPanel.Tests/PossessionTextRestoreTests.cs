using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Possession.Effects;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Possession's text effects and the one restore that could LOSE something.
///
/// <para>Every text effect snapshots the string it found and writes it back on Undo. That is correct
/// right up until something else writes the same label in between - and the labels these effects can
/// take are exactly the ones that do. A <c>{loc:Str}</c> label is a Binding, which
/// <c>PossessionVisual.IsRewritable</c> declines outright, so the only takeable labels left are the
/// CODE-DRIVEN ones: the level readout a level-up rewrites, a counter that ticks. Drop and Retitle hold
/// until reassembly, so that window is the whole rest of the lockdown.</para>
///
/// <para>The rule (modelled by <c>XpDrainEffect.RestoreTheLevel</c>): put the original back only while
/// the control still says what WE wrote. If the world moved on, leave it alone - a haunt that quietly
/// reverts the user's own progress on screen is a bug, not a scare.</para>
///
/// <para>These run against the real effects on an STA thread with a bare host: the effects under test
/// touch nothing but their victim, and the stub attribution never yields, so every await completes
/// inline and the dispatcher is never asked to pump while a test is blocking on it.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class PossessionTextRestoreTests
{
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    // ---- the smallest room an effect will run in -------------------------------------------------

    private sealed class BareAttribution : IPossessionAttribution
    {
        private sealed class Nothing : IDisposable { public void Dispose() { } }
        public Task ChargeAsync(FrameworkElement target, CancellationToken ct, int durationMs = 400)
            => Task.CompletedTask;
        public IDisposable Possess(FrameworkElement target) => new Nothing();
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
    }

    private sealed class BareHost : IPossessionHost
    {
        /// <summary>Never read on these paths: ApplyAsync only reaches for the window when the effect
        /// is targetless, and every effect here takes a victim.</summary>
        public Window Window => null!;
        public Canvas GhostLayer { get; } = new Canvas();
        public Canvas RubbleFloor { get; } = new Canvas();
        public IReadOnlyList<PossessionTarget> Targets => Array.Empty<PossessionTarget>();
        public Point PointOf(FrameworkElement element) => new Point(0, 0);
        public bool IsUsable => true;
    }

    private static PossessionContext Context() => new()
    {
        Host = new BareHost(),
        Attribution = new BareAttribution(),
        Rung = PossessionRung.Collapse,
        Intensity = PossessionIntensity.FullDoki,
        Photosafe = false,
        Rng = new Random(20260825),
        ElapsedFraction = 0.7,
        Remaining = TimeSpan.FromMinutes(10),
        Name = (_, _) => { },
    };

    private static PossessionTarget TargetFor(FrameworkElement el, PossessionRole role)
        => new() { Element = el, Role = role, Key = "test", DisplayName = "the label" };

    /// <summary>Safe here (and only here): nothing on these paths yields.</summary>
    private static void Run(Task t) => t.GetAwaiter().GetResult();

    // ---- typo (one write, 4 s hold) --------------------------------------------------------------

    [Fact]
    public void Typo_RestoresTheExactStringWhenNobodyElseTouchedIt()
    {
        OnStaThread(() =>
        {
            var tb = new TextBlock { Text = "Sessions" };
            var fx = new TypoEffect();

            Run(fx.ApplyAsync(Context(), TargetFor(tb, PossessionRole.Label), CancellationToken.None));
            Assert.NotEqual("Sessions", tb.Text);      // the letter slipped

            Run(fx.UndoAsync(TimeSpan.Zero));
            Assert.Equal("Sessions", tb.Text);
        });
    }

    [Fact]
    public void Typo_LeavesALabelAloneWhenSomethingElseRewroteIt()
    {
        OnStaThread(() =>
        {
            var tb = new TextBlock { Text = "Level 11" };
            var fx = new TypoEffect();

            Run(fx.ApplyAsync(Context(), TargetFor(tb, PossessionRole.Label), CancellationToken.None));

            // A level-up lands mid-hold and writes the label itself. That value is the truth now.
            tb.Text = "Level 12";

            Run(fx.UndoAsync(TimeSpan.Zero));
            Assert.Equal("Level 12", tb.Text);
        });
    }

    // ---- retitle (one write, held until reassembly) -----------------------------------------------

    [Fact]
    public void Retitle_RestoresTheExactStringWhenNobodyElseTouchedIt()
    {
        OnStaThread(() =>
        {
            var tb = new TextBlock { Text = "Conditioning Control Panel" };
            var fx = new RetitleEffect();

            Run(fx.ApplyAsync(Context(), TargetFor(tb, PossessionRole.Title), CancellationToken.None));
            Assert.NotEqual("Conditioning Control Panel", tb.Text);

            Run(fx.UndoAsync(TimeSpan.Zero));
            Assert.Equal("Conditioning Control Panel", tb.Text);
        });
    }

    [Fact]
    public void Retitle_LeavesATitleAloneWhenSomethingElseRewroteIt()
    {
        OnStaThread(() =>
        {
            var tb = new TextBlock { Text = "Conditioning Control Panel" };
            var fx = new RetitleEffect();

            Run(fx.ApplyAsync(Context(), TargetFor(tb, PossessionRole.Title), CancellationToken.None));
            tb.Text = "Session: Morning Drift";

            Run(fx.UndoAsync(TimeSpan.Zero));
            Assert.Equal("Session: Morning Drift", tb.Text);
        });
    }

    // ---- glyphrot (many intermediate writes) ------------------------------------------------------

    [Fact]
    public void GlyphRot_RestoresTheExactStringWhenNobodyElseTouchedIt()
    {
        OnStaThread(() =>
        {
            var tb = new TextBlock { Text = "Progression" };
            var fx = new GlyphRotEffect();

            // The rot walks the word one letter per 60 ms on the dispatcher; this test never lets the
            // dispatcher pump, so Undo lands with nothing painted yet - which is still "ours".
            Run(fx.ApplyAsync(Context(), TargetFor(tb, PossessionRole.Label), CancellationToken.None));
            Run(fx.UndoAsync(TimeSpan.Zero));

            Assert.Equal("Progression", tb.Text);
        });
    }

    [Fact]
    public void GlyphRot_LeavesALabelAloneWhenSomethingElseRewroteIt()
    {
        OnStaThread(() =>
        {
            var tb = new TextBlock { Text = "Progression" };
            var fx = new GlyphRotEffect();

            Run(fx.ApplyAsync(Context(), TargetFor(tb, PossessionRole.Label), CancellationToken.None));
            tb.Text = "Prestige";

            Run(fx.UndoAsync(TimeSpan.Zero));
            Assert.Equal("Prestige", tb.Text);
        });
    }
}
