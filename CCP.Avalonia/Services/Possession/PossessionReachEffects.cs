// PORTED (k22, reach = WPF 7.1.5) from ConditioningControlPanel/Services/Possession/Effects/
// DodgeEffect, WobbleEffect, RelabelEffect, ToggleLieEffect and Scenes/RailSweepScene: the effects WPF
// plays on controls the user acts on. Each keeps WPF's id, rung, intensity, weight and hold.
//
// The owner's hard limits (10 Oct 2026) shape how each one is drawn here:
//   - PossessionTree.IsSafety is refused at the walk, at CanApply and again in Apply, and a live
//     effect on an acted-on control is watched (PossessionGuard): the frame its victim becomes a
//     safety control (Start turned into Stop), and the instant it is clicked, everything is put back.
//   - wobble and togglelie never touch the real control. They hide its picture (opacity, which does
//     not change hit testing) and draw a twin over it that takes no input: the timer keeps its
//     five-tap target and a toggle keeps its real state, and a click during the lie lands on the
//     real control and ends the lie.
//   - dodge takes a start control only (a Start that is a Stop right now is a safety control).
//   - relabel lays text over the face for the hold. Nothing is written to the control or a setting.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services.Possession.Effects;
using ConditioningControlPanel.Services.Possession.Scenes;

namespace ConditioningControlPanel.Services.Possession
{
    /// <summary>Watches a haunted control the user acts on. The restore runs synchronously when the
    /// control is clicked, and within one 30 fps beat of it becoming a safety control by any other
    /// road (a hotkey or a schedule started the engine, so Start is now Stop).</summary>
    internal static class PossessionGuard
    {
        internal const int BeatMs = 33;

        private sealed class Watcher : IDisposable
        {
            private readonly Control _victim;
            private Action? _restore;
            private readonly DispatcherTimer _timer;

            public Watcher(Control victim, Action restore)
            {
                _victim = victim;
                _restore = restore;
                if (victim is Button b) b.Click += OnClick;
                _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(BeatMs), DispatcherPriority.Render, (_, _) => Check());
                _timer.Start();
            }

            private void OnClick(object? sender, RoutedEventArgs e) => Fire();

            internal void Check()
            {
                if (PossessionTree.IsSafety(_victim)) Fire();
            }

            private void Fire()
            {
                var restore = _restore;
                Dispose();
                try { restore?.Invoke(); } catch { }
            }

            public void Dispose()
            {
                _restore = null;
                _timer.Stop();
                if (_victim is Button b) b.Click -= OnClick;
            }
        }

        public static IDisposable Watch(Control victim, Action restore) => new Watcher(victim, restore);

        /// <summary>Tests: run the beat's check now.</summary>
        internal static void CheckNow(IDisposable? watch) => (watch as Watcher)?.Check();
    }
}

namespace ConditioningControlPanel.Services.Possession.Effects
{
    /// <summary>R1 "dodge": a START button slips away from the pointer, three times at most, then
    /// holds still. Never a control that stops, exits or cancels anything: a Start that reads Stop is
    /// a safety control and is refused, and one that becomes Stop mid-dodge is put back at once.</summary>
    internal sealed class DodgeEffect : PossessionEffectBase
    {
        private static readonly PossessionRole[] _roles = { PossessionRole.Button };
        internal const double ProximityPx = 24, DodgeMs = 260;
        internal const int MaxDodges = 3;
        private TopLevel? _top;
        private EventHandler<PointerEventArgs>? _moved;

        public override string Id => "dodge";
        public override PossessionRung MinRung => PossessionRung.Drift;
        public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
        public override bool IsBig => true;
        public override double Weight => 3;
        public override TimeSpan HoldFor => TimeSpan.FromSeconds(20);
        public override IReadOnlyList<PossessionRole> Roles => _roles;
        protected override bool TakesInteractive => true;

        internal int Dodges { get; private set; }

        protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
            target?.Element is Control c && PossessionOffLimits.IsStartStopName(c.Name) && !PossessionTree.IsSafety(c);

        protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
        {
            if (Victim is not { } c) return;
            Dodges = 0;
            _top = TopLevel.GetTopLevel(c);
            if (_top == null) return;
            _moved = (_, e) =>
            {
                if (Victim is { } v) PointerAt(e.GetPosition(v));
            };
            _top.AddHandler(InputElement.PointerMovedEvent, _moved, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        /// <summary>The pointer is at <paramref name="p"/> in the victim's own space.</summary>
        internal void PointerAt(Point p)
        {
            if (!IsLive || Victim is not { } c) return;
            if (PossessionTree.IsSafety(c)) { _ = UndoAsync(TimeSpan.Zero); return; }
            if (Dodges >= MaxDodges) return;
            var near = new Rect(c.Bounds.Size).Inflate(ProximityPx);
            if (!near.Contains(p)) return;
            if (TakeLease(RelativePoint.Center) is not { } lease) return;
            Dodges++;
            // Away from the pointer along the row, never off the window.
            double away = p.X < c.Bounds.Width / 2 ? 1 : -1;
            double reach = Amp(Rand(36, 52)) * away;
            double room = RoomFor(c, away);
            double to = lease.Translate.X + Math.Sign(reach) * Math.Min(Math.Abs(reach), Math.Max(0, room));
            StopTweens();
            Tween(lease.Translate, DodgeMs, false, (0, TranslateTransform.XProperty, lease.Translate.X), (1, TranslateTransform.XProperty, to));
            if (Dodges >= MaxDodges) At(1400, () => SettleLeases(420));   // it gives up and comes home
        }

        private static double RoomFor(Control c, double away)
        {
            try
            {
                if (TopLevel.GetTopLevel(c) is not { } top || c.TranslatePoint(new Point(0, 0), top) is not { } at) return 0;
                return away > 0 ? top.Bounds.Width - (at.X + c.Bounds.Width) - 12 : at.X - 12;
            }
            catch { return 0; }
        }

        protected override bool SettleCore(double ms)
        {
            Unhook();
            return SettleLeases(ms);
        }

        protected override void RestoreCore() => Unhook();

        private void Unhook()
        {
            if (_top != null && _moved != null) _top.RemoveHandler(InputElement.PointerMovedEvent, _moved);
            _top = null;
            _moved = null;
        }
    }

    /// <summary>R2 "wobble": the timer rocks two degrees either way for six seconds. The digits are
    /// the real ones (the twin reads the timer's own text) and the real timer never moves: five taps
    /// on it still open the exit box, at the same place and size.</summary>
    internal sealed class WobbleEffect : PossessionEffectBase
    {
        private static readonly PossessionRole[] _roles = { PossessionRole.Timer };
        internal const double PeriodMs = 770;
        public override string Id => "wobble";
        public override PossessionRung MinRung => PossessionRung.Melt;
        public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
        public override bool IsBig => false;
        public override double Weight => 2;
        public override TimeSpan HoldFor => TimeSpan.FromSeconds(6);
        public override IReadOnlyList<PossessionRole> Roles => _roles;
        protected override bool TakesInteractive => true;
        protected override bool OutlineOnApply => false;
        internal TextBlock? Twin { get; private set; }

        protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
        {
            if (Victim is not TextBlock real) return;
            var rotate = new RotateTransform(0);
            var twin = new TextBlock
            {
                [!TextBlock.TextProperty] = real[!TextBlock.TextProperty],
                [!TextBlock.ForegroundProperty] = real[!TextBlock.ForegroundProperty],
                FontSize = real.FontSize, FontWeight = real.FontWeight, FontFamily = real.FontFamily,
                TextAlignment = real.TextAlignment, Padding = real.Padding,
                RenderTransformOrigin = RelativePoint.Center, RenderTransform = rotate,
            };
            if (!ShowTwin(real, twin)) return;
            Twin = twin;
            Overlay(real, Visual.OpacityProperty, 0d);   // hidden, not gone: it still takes every tap
            double swing = Amp(2);
            Tween(rotate, PeriodMs, true, Swing(RotateTransform.AngleProperty, -swing, swing));
        }

        protected override void RestoreCore() => Twin = null;
    }

    /// <summary>R2 "relabel": a button that offers to start says "Stay" for two and a half seconds.
    /// Only the shown text changes; a click still starts.</summary>
    internal sealed class RelabelEffect : PossessionEffectBase
    {
        private static readonly PossessionRole[] _roles = { PossessionRole.Button };
        public override string Id => "relabel";
        public override PossessionRung MinRung => PossessionRung.Melt;
        public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
        public override bool IsBig => true;
        public override double Weight => 3;
        public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(2500);
        public override IReadOnlyList<PossessionRole> Roles => _roles;
        protected override bool TakesInteractive => true;

        internal static string Stay => RewritePools.LocOr("lockdown_poss_stay", "Stay");

        /// <summary>The one worded face of a button (an icon glyph next to it does not count).</summary>
        internal static TextBlock? Face(Control? button)
        {
            if (button == null) return null;
            TextBlock? found = null;
            foreach (var d in button.GetVisualDescendants())
            {
                if (d is not TextBlock t || !PossessionTree.IsRewritable(t, 3)) continue;
                if (found != null) return null;
                found = t;
            }
            return found;
        }

        protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
            target?.Element is Control c && PossessionOffLimits.IsStartStopName(c.Name) && !PossessionTree.IsSafety(c) && Face(c) != null;

        protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
        {
            if (Face(Victim) is not { } face) return;
            Overlay(face, TextBlock.TextProperty, Stay);
        }
    }

    /// <summary>R2 "togglelie": a switch shows the other state for a second and a half. The real
    /// switch is never written: its picture is hidden and a twin in the other state is drawn over it,
    /// taking no input. A click lands on the real switch, changes the real state, and ends the lie.</summary>
    internal sealed class ToggleLieEffect : PossessionEffectBase
    {
        private static readonly PossessionRole[] _roles = { PossessionRole.Toggle };
        private ToggleButton? _real;
        private EventHandler<RoutedEventArgs>? _changed;

        public override string Id => "togglelie";
        public override PossessionRung MinRung => PossessionRung.Melt;
        public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
        public override bool IsBig => (Ctx?.Rung ?? PossessionRung.Settle) >= PossessionRung.Collapse;
        public override double Weight => 2;
        public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(1500);
        public override IReadOnlyList<PossessionRole> Roles => _roles;
        protected override bool TakesInteractive => true;
        internal ToggleButton? Twin { get; private set; }

        protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
            target?.Element is ToggleButton { IsChecked: not null } t && t.Bounds.Width > 10 && t.Bounds.Height > 6;

        protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
        {
            if (Victim is not ToggleButton real || real.IsChecked is not { } truth) return;
            ToggleButton twin = real is CheckBox ? new CheckBox() : new ToggleButton();
            if (real.Theme != null) twin.Theme = real.Theme;
            twin.Content = real.Content is string s ? s : null;
            twin.IsChecked = !truth;
            twin.IsTabStop = false;
            twin.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
            twin.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch;
            if (!ShowTwin(real, twin)) return;
            Twin = twin;
            _real = real;
            _changed = (_, _) => _ = UndoAsync(TimeSpan.Zero);   // the truth moved: stop lying, now
            real.IsCheckedChanged += _changed;
            Overlay(real, Visual.OpacityProperty, 0d);
        }

        protected override void RestoreCore()
        {
            if (_real != null && _changed != null) _real.IsCheckedChanged -= _changed;
            _real = null;
            _changed = null;
            Twin = null;
        }
    }

    /// <summary>R3 "reorderdoors": two rail doors trade places for ten seconds, then glide back.
    /// Nothing is reordered: each door rides a borrowed transform to the row of the other, so it is
    /// clickable exactly where it lands and opens its OWN section. One pair (WPF trades several).
    /// A door stops nothing and leads to no exit; the pair is put back the moment either is pressed.</summary>
    internal sealed class ReorderDoorsEffect : PossessionEffectBase
    {
        private static readonly PossessionRole[] _roles = { PossessionRole.TabHeader };
        internal const double TradeMs = 600;
        private PossessionTarget? _partner;
        private IDisposable? _partnerWatch;

        public override string Id => "reorderdoors";
        public override PossessionRung MinRung => PossessionRung.Collapse;
        public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
        public override bool IsBig => true;
        public override double Weight => 2;
        public override TimeSpan HoldFor => TimeSpan.FromSeconds(10);
        public override IReadOnlyList<PossessionRole> Roles => _roles;
        protected override bool TakesInteractive => true;
        internal PossessionTarget? Partner => _partner;

        private static PossessionTarget? FindPartner(PossessionContext ctx, PossessionTarget? mine)
        {
            var free = new List<PossessionTarget>();
            foreach (var t in ctx.Host.Targets())
            {
                if (t == null || ReferenceEquals(t, mine) || t.Role != PossessionRole.TabHeader || t.IsLive) continue;
                if (mine != null && ReferenceEquals(t.Element, mine.Element)) continue;
                if (t.CooldownUntil > DateTime.Now) continue;
                if (t.Element is not Control c || !c.IsEffectivelyVisible || !PossessionTree.MayTouch(c, PossessionRole.TabHeader, true)) continue;
                free.Add(t);
            }
            return free.Count == 0 ? null : free[ctx.Rng.Next(free.Count)];
        }

        protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) => FindPartner(ctx, target) != null;

        protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
        {
            if (Victim is not { } a || FindPartner(ctx, target) is not { Element: Control b } partner) return;
            if (TopLevel.GetTopLevel(a) is not { } top) return;
            if (a.TranslatePoint(new Point(0, 0), top) is not { } pa || b.TranslatePoint(new Point(0, 0), top) is not { } pb) return;
            _partner = partner;
            partner.IsLive = true;
            _partnerWatch = PossessionGuard.Watch(b, () => _ = UndoAsync(TimeSpan.Zero));
            var la = LeaseFor(a, RelativePoint.Center);
            var lb = LeaseFor(b, RelativePoint.Center);
            Tween(la.Translate, TradeMs, false,
                (0, TranslateTransform.XProperty, 0), (1, TranslateTransform.XProperty, pb.X - pa.X),
                (0, TranslateTransform.YProperty, 0), (1, TranslateTransform.YProperty, pb.Y - pa.Y));
            Tween(lb.Translate, TradeMs, false,
                (0, TranslateTransform.XProperty, 0), (1, TranslateTransform.XProperty, pa.X - pb.X),
                (0, TranslateTransform.YProperty, 0), (1, TranslateTransform.YProperty, pa.Y - pb.Y));
        }

        protected override bool SettleCore(double ms) => SettleLeases(ms);

        protected override void RestoreCore()
        {
            try { _partnerWatch?.Dispose(); } catch { }
            _partnerWatch = null;
            if (_partner != null) _partner.IsLive = false;
            _partner = null;
        }
    }
}

namespace ConditioningControlPanel.Services.Possession.Scenes
{
    /// <summary>"The rail sweep": up to three rail doors dip and lean one after another, each gaining
    /// a typo in its label, then the edge pulses. Three beats. A door goes nowhere near an exit and
    /// stops nothing; it still opens its section when pressed (and is put back the moment it is).</summary>
    internal sealed class RailSweepScene : PossessionSceneBase
    {
        internal const int MaxDoors = 3;
        public override string Id => "scene_rail_sweep";
        public override int Beats => 3;
        public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(3000);
        protected override bool TakesInteractive => true;
        private readonly List<IDisposable> _watches = new();

        protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
        {
            if (target != null) return false;
            int free = 0;
            foreach (var t in ctx.Host.Targets())
                if (t is { Role: PossessionRole.TabHeader, IsLive: false, Element: Control c } && c.IsEffectivelyVisible
                    && PossessionTree.MayTouch(c, PossessionRole.TabHeader, true)) free++;
            return free >= 2;
        }

        protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
        {
            var doors = new List<PossessionTarget>();
            for (int i = 0; i < MaxDoors; i++)
                if (Pick(ctx, PossessionRole.TabHeader, true) is { } t) doors.Add(t);
            if (doors.Count < 2) return;
            doors.Sort((a, b) => Top(a).CompareTo(Top(b)));
            ctx.Name(Id, "the doors");
            double at = 200;
            foreach (var door in doors)
            {
                double lean = Amp(1.2) * (ctx.Rng.Next(2) == 0 ? -1 : 1);
                At(at, () =>
                {
                    if (door.Element is not Control c || !c.IsEffectivelyVisible || !PossessionTree.MayTouch(c, PossessionRole.TabHeader, true)) return;
                    var lease = LeaseFor(c, RelativePoint.Center);
                    double dy = Amp(4.5);
                    _watches.Add(PossessionGuard.Watch(c, () => _ = UndoAsync(TimeSpan.Zero)));
                    Tween(lease.Translate, 340, false,
                        (0, TranslateTransform.YProperty, 0), (0.35, TranslateTransform.YProperty, dy * 1.4), (1, TranslateTransform.YProperty, dy));
                    Tween(lease.Rotate, 260, false, (0, RotateTransform.AngleProperty, 0), (1, RotateTransform.AngleProperty, lean));
                    if (RelabelEffect.Face(c) is { } face && TypoEffect.Mutate(face.Text!, ctx.Rng) is { } typo)
                        Overlay(face, TextBlock.TextProperty, typo);
                });
                at += 440;
            }
            At(at, () => ctx.Host.EdgePulse(ctx.Photosafe ? 0.35 : 0.55));
        }

        private static double Top(PossessionTarget t)
        {
            try
            {
                if (t.Element is Control c && TopLevel.GetTopLevel(c) is { } top && c.TranslatePoint(new Point(0, 0), top) is { } p) return p.Y;
            }
            catch { }
            return 0;
        }

        protected override void RestoreCore()
        {
            foreach (var w in _watches) { try { w.Dispose(); } catch { } }
            _watches.Clear();
            base.RestoreCore();
        }
    }
}
