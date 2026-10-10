using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Possession.Effects;
using ConditioningControlPanel.Services.Possession.Scenes;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Possession reach = WPF 7.1.5 (k22), inside the owner's hard limits of 10 Oct 2026: the
/// roles WPF tags are enrolled, and a SAFETY control is refused at the walk, at CanApply and in Apply
/// by every effect in the deck; the lockdown card only glows; a Start that is a Stop is never taken;
/// a lie never writes the control.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class PossessionReachTests
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

    /// <summary>Runs the body in a shown window with "is anything running" under the test's hand.</summary>
    private static void InRoom(Action<Room, Window, Action<bool>> body) => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var was = PossessionTree.SomethingRunning;
        bool running = false;
        PossessionTree.SomethingRunning = () => running;
        var win = new Window { Width = 700, Height = 900, Content = BuildRoom(out var room) };
        win.Show();
        Dispatcher.UIThread.RunJobs();
        try { body(room, win, r => running = r); }
        finally
        {
            PossessionTree.SomethingRunning = was;
            win.Close();
        }
    });

    private static Rect InWindow(Control c, Window win) =>
        new(c.TranslatePoint(new Point(0, 0), win) ?? default, c.Bounds.Size);

    private static IEnumerable<IPossessionEffect> WholeDeck() =>
        MainShellWindow.PossessionHeadEffects().Concat(MainShellWindow.PossessionHeadScenes());

    [Fact]
    public void TheWalkEnrolsWhatWpfTags_AndNoSafetyControl() =>
        InRoom((r, win, setRunning) =>
        {
            var keys = PossessionTree.Collect(win, new Dictionary<string, PossessionTarget>()).Select(t => t.Key).ToArray();
            Assert.Contains("DoorHome", keys);
            Assert.Contains("DoorPlay", keys);
            Assert.Contains("BtnStart", keys);
            Assert.Contains("LockdownCardBorder", keys);
            Assert.Contains("ChkLockdownHideTimer", keys);
            Assert.Contains("TxtLockdownTimer", keys);
            // Tagged as WPF tags them, and refused all the same.
            foreach (var safety in new[] { "ChkLockdownStrict", "ChkLockdownNoPanic", "BtnEmergencyExit", "TxtLockdownExit", "BtnStopAll", "BtnLeashCut" })
                Assert.DoesNotContain(safety, keys);

            setRunning(true);   // Start is a Stop now
            keys = PossessionTree.Collect(win, new Dictionary<string, PossessionTarget>()).Select(t => t.Key).ToArray();
            Assert.DoesNotContain("BtnStart", keys);
            Assert.Contains("DoorHome", keys);
            Assert.True(PossessionTree.IsSafety(r.Start));
        });

    [Fact]
    public void EveryEffectRefusesEverySafetyControl_AtCanApplyAndInApply() =>
        InRoom((r, win, setRunning) =>
        {
            setRunning(true);
            var host = new PossessionHost { IsUsable = () => true };
            var safety = new Control[] { r.Exit, r.Phrase, r.Strict, r.NoPanic, r.StopAll, r.LeashCut, r.Start };
            var roles = new[] { PossessionRole.Button, PossessionRole.Toggle, PossessionRole.Label, PossessionRole.Card,
                                PossessionRole.Title, PossessionRole.TabHeader, PossessionRole.Timer, PossessionRole.TextBox };
            foreach (var c in safety)
            {
                Assert.True(PossessionTree.IsSafety(c), c.Name);
                var at = InWindow(c, win);
                var rest = c.RenderTransform;
                string? said = (c as Button)?.Content as string;
                foreach (var effect in WholeDeck())
                    foreach (var role in roles)
                    {
                        var target = Target(c, role);
                        if (effect.Roles.Count > 0)
                        {
                            Assert.False(effect.CanApply(Ctx(host), target), effect.Id + " " + c.Name + " " + role);
                            effect.ApplyAsync(Ctx(host), target, default);   // a caller that skipped the question
                            Assert.False(effect.IsLive, effect.Id + " " + c.Name + " " + role);
                        }
                        else
                        {
                            // No victim of its own (a scene, the crack, the toast): handed one anyway, it leaves it alone.
                            effect.ApplyAsync(Ctx(host), target, default);
                            effect.UndoAsync(TimeSpan.Zero);
                        }
                        Dispatcher.UIThread.RunJobs();
                        Assert.Equal(at, InWindow(c, win));
                        Assert.Same(rest, c.RenderTransform);
                        Assert.Equal(1, c.Opacity);
                        Assert.True(c.IsHitTestVisible && c.IsVisible && c.IsEnabled);
                        Assert.Equal(said, (c as Button)?.Content as string);
                    }
            }
        });

    [Fact]
    public void TheLockdownCardOnlyGlows_AndTheEmergencyExitNeverMoves() =>
        InRoom((r, win, _) =>
        {
            var host = new PossessionHost { IsUsable = () => true };
            var card = Target(r.Card, PossessionRole.Card);
            Assert.True(PossessionTree.IsGuardedContainer(r.Card));

            // Nothing but the breath may take it.
            foreach (var effect in WholeDeck().Where(e => e.Id != "breathe" && e.Roles.Count > 0))
            {
                Assert.False(effect.CanApply(Ctx(host), card), effect.Id);
                effect.ApplyAsync(Ctx(host), card, default);
                Assert.False(effect.IsLive, effect.Id);
            }

            var breathe = new PossessionBreathe();
            var exitAt = InWindow(r.Exit, win);
            var phraseAt = InWindow(r.Phrase, win);
            var cardAt = InWindow(r.Card, win);
            var exitRest = r.Exit.RenderTransform;
            Assert.True(breathe.CanApply(Ctx(host), card));
            breathe.ApplyAsync(Ctx(host), card, default);
            Assert.True(breathe.IsLive && breathe.IsGlowing);

            bool glowed = false;
            for (double ms = 0; ms <= PossessionBreathe.GlowPeriodMs * 2; ms += 33)
            {
                breathe.PaintGlow(ms);
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.Equal(exitAt, InWindow(r.Exit, win));       // rest position and size, every frame
                Assert.Equal(phraseAt, InWindow(r.Phrase, win));
                Assert.Equal(cardAt, InWindow(r.Card, win));
                Assert.Null(r.Card.RenderTransform);
                Assert.Same(exitRest, r.Exit.RenderTransform);
                Assert.Equal(1, r.Card.Opacity);
                Assert.True(r.Card.IsHitTestVisible && r.Exit.IsHitTestVisible && r.Exit.IsEnabled);
                Assert.True(new Rect(win.ClientSize).Contains(exitAt));
                var hit = r.Exit.InputHitTest(new Point(exitAt.Width / 2, exitAt.Height / 2)) as Visual;
                Assert.Same(r.Exit, hit == null ? null : FindButton(hit));
                if (r.Card.BoxShadow.Count > 0 && r.Card.BoxShadow[0].Color.A > 40) glowed = true;
            }
            Assert.True(glowed);

            breathe.UndoAsync(TimeSpan.Zero);   // panic: in the call
            Assert.False(breathe.IsLive || breathe.IsGlowing);
            Assert.Equal(0, r.Card.BoxShadow.Count);
        });

    private static Button? FindButton(Visual v)
    {
        for (Visual? n = v; n != null; n = global::Avalonia.VisualTree.VisualExtensions.GetVisualParent(n))
            if (n is Button b) return b;
        return null;
    }

    [Fact]
    public void DodgeTakesAStartOnly_ThreeTimesAtMost_AndLetsGoTheMomentItIsAStop() =>
        InRoom((r, win, setRunning) =>
        {
            var host = new PossessionHost { IsUsable = () => true };
            var dodge = new DodgeEffect();
            Assert.False(dodge.CanApply(Ctx(host), Target(r.Door, PossessionRole.Button)));     // not a start
            Assert.False(dodge.CanApply(Ctx(host), Target(r.StopAll, PossessionRole.Button)));  // a stop
            Assert.False(dodge.CanApply(Ctx(host), Target(r.Exit, PossessionRole.Button)));

            var start = Target(r.Start, PossessionRole.Button);
            var rest = r.Start.RenderTransform;
            Assert.True(dodge.CanApply(Ctx(host), start));
            dodge.ApplyAsync(Ctx(host), start, default);
            Assert.True(dodge.IsLive);
            for (int i = 0; i < 6; i++) dodge.PointerAt(new Point(4, 4));
            Assert.Equal(DodgeEffect.MaxDodges, dodge.Dodges);
            Assert.IsType<TransformGroup>(r.Start.RenderTransform);

            setRunning(true);                    // the engine started by another road: Start reads Stop
            dodge.PointerAt(new Point(4, 4));
            Assert.False(dodge.IsLive);
            Assert.Same(rest, r.Start.RenderTransform);
            Assert.False(dodge.CanApply(Ctx(host), start));

            // The guard itself: one beat after the victim turns into a safety control, and on a click.
            setRunning(false);
            int restored = 0;
            var watch = PossessionGuard.Watch(r.Start, () => restored++);
            PossessionGuard.CheckNow(watch);
            Assert.Equal(0, restored);
            setRunning(true);
            PossessionGuard.CheckNow(watch);
            Assert.Equal(1, restored);
            setRunning(false);
            PossessionGuard.Watch(r.Start, () => restored++);
            r.Start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, restored);
        });

    [Fact]
    public void RelabelShowsStayForTheHold_WritesNothing_AndAClickStillStarts() =>
        InRoom((r, win, setRunning) =>
        {
            var host = new PossessionHost { IsUsable = () => true };
            var relabel = new RelabelEffect();
            var start = Target(r.Start, PossessionRole.Button);
            var face = RelabelEffect.Face(r.Start)!;
            Assert.Equal("Start", face.Text);
            Assert.False(relabel.CanApply(Ctx(host), Target(r.Door, PossessionRole.Button)));
            Assert.False(relabel.CanApply(Ctx(host), Target(r.StopAll, PossessionRole.Button)));

            int started = 0;
            r.Start.Click += (_, _) => started++;
            relabel.ApplyAsync(Ctx(host), start, default);
            Assert.True(relabel.IsLive);
            Assert.Equal(RelabelEffect.Stay, face.Text);

            r.Start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // a click during the lie
            Assert.Equal(1, started);                                     // it started
            Assert.False(relabel.IsLive);                                 // and the lie ended in the call
            Assert.Equal("Start", face.Text);

            relabel.ApplyAsync(Ctx(host), start, default);
            relabel.UndoAsync(TimeSpan.Zero);
            Assert.Equal("Start", face.Text);
            Assert.Equal(0, relabel.OverlayCount);

            setRunning(true);
            Assert.False(relabel.CanApply(Ctx(host), start));
        });

    [Fact]
    public void ToggleLieNeverWritesTheSwitch_AndAClickActsOnTheRealState() =>
        InRoom((r, win, _) =>
        {
            var host = new PossessionHost { IsUsable = () => true };
            var lie = new ToggleLieEffect();
            int changes = 0;
            r.HideTimer.IsChecked = false;
            r.HideTimer.IsCheckedChanged += (_, _) => changes++;
            var toggle = Target(r.HideTimer, PossessionRole.Toggle);
            Assert.False(lie.CanApply(Ctx(host), Target(r.Strict, PossessionRole.Toggle)));
            Assert.False(lie.CanApply(Ctx(host), Target(r.NoPanic, PossessionRole.Toggle)));
            Assert.True(lie.CanApply(Ctx(host), toggle));

            var at = InWindow(r.HideTimer, win);
            var rest = r.HideTimer.RenderTransform;
            lie.ApplyAsync(Ctx(host), toggle, default);
            Dispatcher.UIThread.RunJobs();
            Assert.True(lie.IsLive);
            Assert.False(r.HideTimer.IsChecked);             // the truth is untouched
            Assert.Equal(0, changes);                         // and nothing heard a change
            Assert.True(lie.Twin!.IsChecked);                 // the picture lies
            Assert.False(lie.Twin.IsHitTestVisible || lie.Twin.Focusable);
            Assert.Equal(at, InWindow(r.HideTimer, win));
            Assert.Same(rest, r.HideTimer.RenderTransform);
            Assert.True(r.HideTimer.IsHitTestVisible && r.HideTimer.IsEnabled);

            r.HideTimer.IsChecked = true;                     // the user clicked the real switch
            Assert.Equal(1, changes);
            Assert.False(lie.IsLive);                         // the lie ended in the call
            Assert.Equal(1, r.HideTimer.Opacity);
            Assert.Equal(0, lie.TwinCount);

            lie.ApplyAsync(Ctx(host), toggle, default);
            lie.UndoAsync(TimeSpan.Zero);
            Assert.True(r.HideTimer.IsChecked);
            Assert.Equal(1, changes);
            Assert.Equal(1, r.HideTimer.Opacity);
        });

    [Fact]
    public void WobbleRocksATwin_TheRealTimerKeepsItsTapTargetAndItsDigits() =>
        InRoom((r, win, _) =>
        {
            var host = new PossessionHost { IsUsable = () => true };
            var wobble = new WobbleEffect();
            var timer = Target(r.Timer, PossessionRole.Timer);
            var at = InWindow(r.Timer, win);
            Assert.True(wobble.CanApply(Ctx(host), timer));
            wobble.ApplyAsync(Ctx(host), timer, default);
            Dispatcher.UIThread.RunJobs();
            Assert.True(wobble.IsLive);
            Assert.Null(r.Timer.RenderTransform);
            Assert.Equal(at, InWindow(r.Timer, win));
            Assert.True(r.Timer.IsHitTestVisible);
            Assert.Equal("19:59", wobble.Twin!.Text);
            r.Timer.Text = "19:58";                           // the digits never lie
            Assert.Equal("19:58", wobble.Twin.Text);
            Assert.False(wobble.Twin.IsHitTestVisible);
            Assert.False(wobble.UsesFlicker);

            wobble.UndoAsync(TimeSpan.Zero);
            Assert.False(wobble.IsLive);
            Assert.Equal(1, r.Timer.Opacity);
            Assert.Equal(0, wobble.TwinCount);
            Assert.Equal("19:58", r.Timer.Text);
        });

    [Fact]
    public void TheRailSweepLeansDoors_GivesEachBack_AndADoorPressedIsPutBackAtOnce() =>
        InRoom((r, win, _) =>
        {
            var cache = new Dictionary<string, PossessionTarget>();
            var host = new PossessionHost { IsUsable = () => true, Targets = () => PossessionTree.Collect(win, cache) };
            var sweep = new RailSweepScene();
            var rest = r.Door.RenderTransform;
            Assert.True(sweep.CanApply(Ctx(host), null));
            sweep.ApplyAsync(Ctx(host), null, default);
            Assert.True(sweep.IsLive);
            Assert.All(sweep.Booked, t => Assert.Equal(PossessionRole.TabHeader, t.Role));
            Assert.InRange(sweep.Booked.Count, 2, RailSweepScene.MaxDoors);
            sweep.PlayBeatsNow();
            var leaned = sweep.Booked.Select(t => (Control)t.Element).ToArray();
            Assert.All(leaned, d => Assert.IsType<TransformGroup>(d.RenderTransform));

            leaned[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // the user opens that section
            Assert.False(sweep.IsLive);
            Assert.All(leaned, d => Assert.Equal(rest, d.RenderTransform));
            Assert.All(host.Targets(), t => Assert.False(t.IsLive));

            sweep.ApplyAsync(Ctx(host), null, default);
            sweep.PlayBeatsNow();
            sweep.UndoAsync(TimeSpan.Zero);                                  // panic
            Assert.All(new[] { r.Door, r.Door2, r.Door3 }, d => Assert.Equal(rest, d.RenderTransform));
        });

    [Fact]
    public void NothingNewBlinks() =>
        Assert.All(new IPossessionEffect[] { new DodgeEffect(), new WobbleEffect(), new RelabelEffect(), new ToggleLieEffect(), new RailSweepScene() },
            e => Assert.False(e.UsesFlicker));

    // ---- the room ------------------------------------------------------------------------------

    private sealed class Room
    {
        public Button Door = null!, Door2 = null!, Door3 = null!, Start = null!, Exit = null!, StopAll = null!, LeashCut = null!;
        public CheckBox HideTimer = null!, Strict = null!, NoPanic = null!;
        public TextBlock Timer = null!;
        public TextBox Phrase = null!;
        public Border Card = null!;
    }

    private static Control BuildRoom(out Room room)
    {
        var r = room = new Room();
        r.Door = Tagged(new Button { Name = "DoorHome", Content = "Home" }, PossessionRole.TabHeader, "the Home door");
        r.Door2 = Tagged(new Button { Name = "DoorPlay", Content = "Play" }, PossessionRole.TabHeader, "the Play door");
        r.Door3 = Tagged(new Button { Name = "DoorStudio", Content = "Studio" }, PossessionRole.TabHeader, "the Studio door");
        r.Start = Tagged(new Button
        {
            Name = "BtnStart",
            Content = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Children = { new TextBlock { Text = "▶" }, new TextBlock { Text = "Start" } } },
        }, PossessionRole.Button, "the Start button");
        r.StopAll = Tagged(new Button { Name = "BtnStopAll", Content = "Stop" }, PossessionRole.Button, "the stop");
        r.LeashCut = Tagged(new Button { Name = "BtnLeashCut", Content = "Cut" }, PossessionRole.Button, "the cut");
        r.HideTimer = Tagged(new CheckBox { Name = "ChkLockdownHideTimer", Width = 44, Height = 22 }, PossessionRole.Toggle, "the hidden clock toggle");
        r.Strict = Tagged(new CheckBox { Name = "ChkLockdownStrict", Width = 44, Height = 22 }, PossessionRole.Toggle, "the Strict Lock safety");
        r.NoPanic = Tagged(new CheckBox { Name = "ChkLockdownNoPanic", Width = 44, Height = 22 }, PossessionRole.Toggle, "the panic key safety");
        r.Timer = Tagged(new TextBlock { Name = "TxtLockdownTimer", Text = "19:59", FontSize = 36 }, PossessionRole.Timer, "the timer");
        r.Exit = Tagged(new Button { Name = "BtnEmergencyExit", Content = "Emergency Exit" }, PossessionRole.Button, "the exit");
        r.Phrase = Tagged(new TextBox { Name = "TxtLockdownExit", Width = 200 }, PossessionRole.TextBox, "the phrase box");
        r.Card = Tagged(new Border
        {
            Name = "LockdownCardBorder", Padding = new Thickness(12),
            Child = new StackPanel { Children = { r.HideTimer, r.Strict, r.NoPanic, r.Timer, r.Exit, r.Phrase } },
        }, PossessionRole.Card, "the lockdown card");
        return new StackPanel { Children = { r.Door, r.Door2, r.Door3, r.Start, r.StopAll, r.LeashCut, r.Card } };
    }
}
