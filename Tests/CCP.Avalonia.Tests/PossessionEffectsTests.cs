using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Possession.Effects;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Possession wave 2 (k18): the role attached property and the tree walk that replaced the
/// hand-built registry, the off-limits law on this head, and typo / drift / melt / crack / retitle:
/// each gives its victim back exactly, in the call, when asked with zero duration (panic, UndoAll).</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class PossessionEffectsTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static PossessionContext Ctx(PossessionHost host, bool photosafe = false, int seed = 3) => new()
    {
        Host = host, Rung = PossessionRung.ItKnows, Intensity = PossessionIntensity.FullDoki, Photosafe = photosafe,
        Rng = new Random(seed), ElapsedFraction = 0.9, Remaining = TimeSpan.FromMinutes(2), Name = (_, _) => { },
    };

    private static PossessionTarget Target(Control c, PossessionRole role) => new() { Element = c, Role = role, Key = c.Name ?? role.ToString() };

    private static T Tagged<T>(T c, PossessionRole role, string name) where T : Control
    {
        Possession.SetRole(c, role);
        Possession.SetName(c, name);
        return c;
    }

    [Fact]
    public void TheWalkEnrolsTaggedDisplayControls_AndNothingTheUserPressesOrMustReach() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var label = Tagged(new TextBlock { Name = "TxtPlain", Text = "plain words" }, PossessionRole.Label, "the words");
        var card = Tagged(new Border { Name = "CardPlain", Child = new TextBlock { Text = "inside" } }, PossessionRole.Card, "the card");
        var untagged = new TextBlock { Name = "TxtUntagged", Text = "never asked" };
        // Everything below is tagged and must still be refused.
        var button = Tagged(new Button { Name = "BtnGo", Content = "go" }, PossessionRole.Button, "the button");
        var toggle = Tagged(new ToggleSwitch { Name = "ChkStrict" }, PossessionRole.Toggle, "the Strict Lock safety");
        var timer = Tagged(new TextBlock { Name = "TxtClock", Text = "19:59" }, PossessionRole.Timer, "the timer");
        var holder = Tagged(new Border { Name = "CardWithExit", Child = new Button { Content = "Exit" } }, PossessionRole.Card, "the card with a way out");
        var face = Tagged(new TextBlock { Name = "TxtFace", Text = "Stop" }, PossessionRole.Label, "a button's face");
        var faceButton = new Button { Name = "BtnStop", Content = face };
        var reserved = Tagged(new TextBlock { Name = "TxtEmergencyHint", Text = "hold to leave" }, PossessionRole.Label, "the hint");
        var excluded = Tagged(new TextBlock { Name = "TxtInsideExcluded", Text = "safe word" }, PossessionRole.Label, "the safe word");
        var excludedPanel = new StackPanel { Children = { excluded } };
        Possession.SetExclude(excludedPanel, true);
        var phrase = Tagged(new TextBox { Name = "TxtPhrase" }, PossessionRole.TextBox, "the phrase box");

        var win = new Window
        {
            Width = 600, Height = 600,
            Content = new StackPanel { Children = { label, card, untagged, button, toggle, timer, holder, faceButton, reserved, excludedPanel, phrase } },
        };
        win.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var cache = new Dictionary<string, PossessionTarget>();
            var targets = PossessionTree.Collect(win, cache);
            Assert.Equal(new[] { "TxtPlain", "CardPlain" }, targets.Select(t => t.Key).ToArray());
            Assert.Equal("the words", targets[0].DisplayName);
            Assert.Equal(PossessionRole.Card, targets[1].Role);
            Assert.Same(targets[0], PossessionTree.Collect(win, cache)[0]);   // cached: a cooldown survives

            Assert.True(Possession.GetExclude(excluded));                     // Exclude inherits down
            foreach (var c in new Control[] { button, toggle, holder, face, reserved, excluded, phrase })
                Assert.True(PossessionTree.IsOffLimits(c), c.Name);
            Assert.True(PossessionTree.IsOffLimits(null));
            Assert.False(PossessionTree.MayEnrol(timer));                     // the timer is not a display role here

            // The law is asked again at each effect's door: a hand-built target cannot get past it.
            var host = new PossessionHost();
            var effects = MainShellWindow.PossessionHeadEffects();
            Assert.Equal(new[] { "nudge", "typo", "breathe", "drift", "melt", "crack", "retitle" }, effects.Select(e => e.Id).ToArray());
            Assert.DoesNotContain(effects, e => e.UsesFlicker);
            foreach (var e in effects.OfType<PossessionEffectBase>().Where(e => e.Roles.Count > 0))
            {
                foreach (var c in new Control[] { button, toggle, holder, face, reserved, excluded, phrase })
                    foreach (var role in e.Roles)
                        Assert.False(e.CanApply(Ctx(host), Target(c, role)), e.Id + " on " + c.Name);
                Assert.False(e.CanApply(Ctx(host), Target(timer, PossessionRole.Timer)), e.Id + " on the timer");
                // And Apply itself refuses, should a caller skip CanApply.
                e.ApplyAsync(Ctx(host), Target(holder, PossessionRole.Card), default).GetAwaiter().GetResult();
                Assert.False(e.IsLive);
            }
            Assert.Null(holder.RenderTransform);
            Assert.Equal("Stop", face.Text);
        }
        finally { win.Close(); }
    });

    [Fact]
    public void TheShellWalkFindsTheTitleAndTheLevelLabel_AndNoControl() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var savedMic = s.MicConsentGiven;
        s.MicConsentGiven = false;
        var shell = new MainShellWindow();
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var targets = shell.PossessionTargets();
            Assert.Contains(targets, t => t.Key == "TxtPlayerTitle" && t.Role == PossessionRole.Title && t.DisplayName == "the title");
            Assert.Contains(targets, t => t.Key == "TxtTitleBarVersion" && t.Role == PossessionRole.Label);
            Assert.All(targets, t =>
            {
                var c = (Control)t.Element;
                Assert.True(PossessionTree.IsDisplayRole(t.Role));
                Assert.False(PossessionTree.IsOffLimits(c), t.Key);
                Assert.False(PossessionTree.IsInteractive(c) || PossessionTree.HoldsProtected(c), t.Key);
                Assert.False(PossessionOffLimits.IsReservedName(t.Key));
            });
        }
        finally
        {
            s.MicConsentGiven = savedMic;
            CoreSettings.SaveImmediate();
            shell.RequestExit();
        }
    });

    [Fact]
    public void EveryEffectGivesItsVictimBackExactly_InTheCall() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var ownTransform = new RotateTransform(0);
        var ownOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        var label = Tagged(new TextBlock { Name = "TxtWords", Text = "hello there", RenderTransform = ownTransform, RenderTransformOrigin = ownOrigin }, PossessionRole.Label, "the words");
        var title = Tagged(new TextBlock { Name = "TxtHead", Text = "Basic Subject" }, PossessionRole.Title, "the title");
        var card = Tagged(new Border { Name = "CardPips", Width = 80, Height = 20, Background = Brushes.Gray }, PossessionRole.Card, "the pips");
        var win = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { label, title, card } } };
        win.Show();
        Dispatcher.UIThread.RunJobs();
        var pulses = new List<double>();
        var shakes = new List<(double, int)>();
        var host = new PossessionHost { EdgePulse = pulses.Add, Shake = (a, ms) => shakes.Add((a, ms)), IsUsable = () => true };
        try
        {
            // typo: one character wrong, never the first; the page's own text is untouched underneath.
            var typo = new TypoEffect();
            var lt = Target(label, PossessionRole.Label);
            Assert.True(typo.CanApply(Ctx(host), lt));
            typo.ApplyAsync(Ctx(host), lt, default).GetAwaiter().GetResult();
            Assert.True(typo.IsLive);
            Assert.NotEqual("hello there", label.Text);
            Assert.Equal('h', label.Text![0]);
            Assert.Equal("hello there".Length, label.Text.Length);
            Assert.False(typo.CanApply(Ctx(host), lt));                // never double-booked
            var undo = typo.UndoAsync(TimeSpan.Zero);
            Assert.True(undo.IsCompletedSuccessfully);                  // synchronous: panic relies on it
            Assert.Equal("hello there", label.Text);
            Assert.False(typo.IsLive);
            Assert.Equal(0, typo.OverlayCount);
            Assert.True(typo.UndoAsync(TimeSpan.Zero).IsCompletedSuccessfully);   // safe twice

            // A write from the page while haunted is what comes back, not a stale copy.
            typo.ApplyAsync(Ctx(host), lt, default).GetAwaiter().GetResult();
            label.Text = "new words here";
            Assert.NotEqual("new words here", label.Text);              // the ghost still holds the face
            typo.UndoAsync(TimeSpan.Zero);
            Assert.Equal("new words here", label.Text);

            // drift: borrows the transform over the control's own, gives the same instance back.
            var drift = new DriftEffect();
            drift.ApplyAsync(Ctx(host), lt, default).GetAwaiter().GetResult();
            Assert.True(drift.IsLive);
            var group = Assert.IsType<TransformGroup>(label.RenderTransform);
            Assert.Contains(ownTransform, group.Children);              // the control's own one stays composed
            Assert.True(drift.UndoAsync(TimeSpan.Zero).IsCompletedSuccessfully);
            Assert.Same(ownTransform, label.RenderTransform);
            Assert.Equal(ownOrigin, label.RenderTransformOrigin);
            Assert.Equal(1.0, label.Opacity);
            Assert.True(label.IsVisible);

            // An eased undo overtaken by a panic: the control is back at the panic, and stays back.
            drift.ApplyAsync(Ctx(host), lt, default).GetAwaiter().GetResult();
            var eased = drift.UndoAsync(TimeSpan.FromMilliseconds(300));
            Assert.False(eased.IsCompleted);
            Assert.True(drift.UndoAsync(TimeSpan.Zero).IsCompletedSuccessfully);
            Assert.Same(ownTransform, label.RenderTransform);
            Assert.False(drift.IsLive);

            // melt: sags only under the pointer, firms, and leaves nothing on the card.
            var melt = new MeltEffect();
            var ct = Target(card, PossessionRole.Card);
            melt.ApplyAsync(Ctx(host), ct, default).GetAwaiter().GetResult();
            Assert.True(melt.IsLive);
            Assert.Null(card.RenderTransform);                          // nothing until the pointer arrives
            melt.Melt();
            Assert.True(melt.IsMelted);
            Assert.IsType<TransformGroup>(card.RenderTransform);
            Assert.True(card.IsHitTestVisible);
            Assert.True(melt.UndoAsync(TimeSpan.Zero).IsCompletedSuccessfully);
            Assert.Null(card.RenderTransform);
            Assert.False(melt.IsMelted);
            melt.Melt();                                                // a late pointer event does nothing
            Assert.Null(card.RenderTransform);

            // crack: pulse and one jolt; photosafe keeps the pulse and never shakes.
            var crack = new CrackEffect();
            Assert.Empty(crack.Roles);
            crack.ApplyAsync(Ctx(host), null, default).GetAwaiter().GetResult();
            Assert.Equal(new[] { 0.4 }, pulses);
            Assert.Equal(new[] { (0.25, 180) }, shakes);
            Assert.True(crack.UndoAsync(TimeSpan.Zero).IsCompletedSuccessfully);
            crack.ApplyAsync(Ctx(host, photosafe: true), null, default).GetAwaiter().GetResult();
            Assert.Equal(2, pulses.Count);
            Assert.Single(shakes);
            crack.UndoAsync(TimeSpan.Zero);

            // retitle: Full Doki only, the title only, until the exit; then the real title is back.
            var retitle = new RetitleEffect();
            Assert.Equal(PossessionIntensity.FullDoki, retitle.MinIntensity);
            Assert.Equal(TimeSpan.Zero, retitle.HoldFor);
            Assert.False(retitle.CanApply(Ctx(host), lt));              // a label is not the title
            var tt = Target(title, PossessionRole.Title);
            Assert.True(retitle.CanApply(Ctx(host), tt));
            retitle.ApplyAsync(Ctx(host), tt, default).GetAwaiter().GetResult();
            Assert.Contains(title.Text, RetitleEffect.Lines);
            Assert.True(retitle.UndoAsync(TimeSpan.Zero).IsCompletedSuccessfully);
            Assert.Equal("Basic Subject", title.Text);

            // Photosafe halves a motion, it never adds one.
            var calm = new DriftEffect();
            calm.ApplyAsync(Ctx(host, photosafe: true), lt, default).GetAwaiter().GetResult();
            calm.UndoAsync(TimeSpan.Zero);
            Assert.Same(ownTransform, label.RenderTransform);
        }
        finally { win.Close(); }
    });

    [Fact]
    public void PanicThroughTheDirector_BringsEveryHauntedControlBackAtOnce_AndSoDoesClosing() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var label = Tagged(new TextBlock { Name = "TxtWords", Text = "hello there" }, PossessionRole.Label, "the words");
        var other = Tagged(new TextBlock { Name = "TxtOther", Text = "second label" }, PossessionRole.Label, "the other words");
        var win = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { label, other } } };
        win.Show();
        Dispatcher.UIThread.RunJobs();
        var s = CoreSettings.Current;
        var saved = (s.LockdownPossessionEnabled, s.LockdownPossessionIntensity, s.LockdownPhotosafe, s.LockdownTripwiresEnabled,
            s.LockdownForceStrictLock, s.LockdownDisablePanicKey, s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownDoseKeeperEnabled);
        (s.LockdownPossessionEnabled, s.LockdownPossessionIntensity, s.LockdownPhotosafe) = (true, (int)PossessionIntensity.Eerie, false);
        s.LockdownForceStrictLock = s.LockdownDisablePanicKey = s.LockdownDoseKeeperEnabled = false;
        var prevDirector = PossessionDirector.Current;
        var prevLockdown = LockdownService.Current;
        var start = new DateTime(2026, 10, 10, 12, 0, 0);
        var clock = start;
        var ld = LockdownService.Current = new LockdownService { UtcNow = () => clock.ToUniversalTime() };
        var typo = new TypoEffect();
        var cache = new Dictionary<string, PossessionTarget>();
        var director = PossessionDirector.Current = new PossessionDirector(ld, new IPossessionEffect[] { typo }, new PossessionHost
        {
            Targets = () => PossessionTree.Collect(win, cache),
            IsUsable = () => true,
        }, new Random(7)) { Now = () => clock };
        try
        {
            ld.Activate(TimeSpan.FromMinutes(20));
            clock = start.AddMinutes(1.8);                       // past every first delay
            director.Tick(TimeSpan.FromMinutes(18.2));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, director.LiveEffectCount);
            Assert.True(typo.IsLive);
            Assert.True(label.Text != "hello there" || other.Text != "second label");

            MainShellWindow.StopPossessionForPanic(null);        // the "possession" panic surface
            Assert.Equal("hello there", label.Text);             // in the call, no animation
            Assert.Equal("second label", other.Text);
            Assert.False(typo.IsLive);
            Assert.Equal(0, director.LiveEffectCount);
            director.Tick(TimeSpan.FromMinutes(18.2));           // the same second: quiet under the press
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("hello there", label.Text);

            clock = start.AddMinutes(9);                         // later it haunts again
            director.Tick(TimeSpan.FromMinutes(11));
            Dispatcher.UIThread.RunJobs();
            Assert.True(typo.IsLive);
            director.Dispose();                                  // the window closing
            Assert.Equal("hello there", label.Text);
            Assert.Equal("second label", other.Text);
            Assert.False(typo.IsLive);
        }
        finally
        {
            if (ld.IsActive) ld.Deactivate();
            director.Dispose();
            ld.Dispose();
            PossessionDirector.Current = prevDirector;
            LockdownService.Current = prevLockdown;
            (s.LockdownPossessionEnabled, s.LockdownPossessionIntensity, s.LockdownPhotosafe, s.LockdownTripwiresEnabled,
                s.LockdownForceStrictLock, s.LockdownDisablePanicKey, s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownDoseKeeperEnabled) = saved;
            CoreSettings.SaveImmediate();
            win.Close();
        }
    });
}
