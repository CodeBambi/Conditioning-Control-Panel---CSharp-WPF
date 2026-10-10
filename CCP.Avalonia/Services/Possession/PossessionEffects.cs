// PORTED from ConditioningControlPanel/Services/Possession/Effects/ (7.1.5): PossessionEffectBase (the
// part this head needs), TypoEffect, DriftEffect, MeltEffect (no blur: no Effect under looping FX),
// CrackEffect, RetitleEffect. Each has an IN and an OUT; UndoAsync(TimeSpan.Zero) restores in the call.
//
// How a victim comes back EXACTLY: nothing is written over the control's own values. Text, the render
// transform and its origin are laid OVER them at animation priority and the overlay is disposed on
// undo, so a binding, a local value and a later write from the page (the rung readout repainting, a
// language change) are all still there underneath.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using Serilog;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>The four transforms an effect may drive on a borrowed control (WPF TransformLease).</summary>
internal sealed class PossessionLease
{
    public ScaleTransform Scale { get; } = new(1, 1);
    public SkewTransform Skew { get; } = new(0, 0);
    public RotateTransform Rotate { get; } = new(0);
    public TranslateTransform Translate { get; } = new(0, 0);
    public TransformGroup Group { get; } = new();
}

internal abstract class PossessionEffectBase : IPossessionEffect
{
    private static readonly PossessionRole[] _noRoles = Array.Empty<PossessionRole>();
    private readonly List<IDisposable> _overlays = new();
    private readonly List<DispatcherTimer> _timers = new();
    private readonly List<PossessionLease> _leases = new();
    private readonly List<(DispatcherTimer Timer, Action Beat)> _beats = new();
    private int _epoch;

    protected CancellationTokenSource? Cts { get; private set; }
    protected PossessionContext? Ctx { get; private set; }
    protected Control? Victim { get; private set; }
    protected PossessionLease? Lease { get; private set; }
    protected Random Rng => Ctx?.Rng ?? Random.Shared;
    protected bool Photosafe => Ctx?.Photosafe == true;

    public abstract string Id { get; }
    public abstract PossessionRung MinRung { get; }
    public abstract PossessionIntensity MinIntensity { get; }
    public abstract bool IsBig { get; }
    public virtual bool UsesFlicker => false;
    public abstract double Weight { get; }
    public abstract TimeSpan HoldFor { get; }
    public virtual IReadOnlyList<PossessionRole> Roles => _noRoles;
    public bool IsLive { get; private set; }

    // The possessed outline (WPF EmberAttribution.Possess): a thin ember frame on the adorner layer
    // for as long as the victim is haunted, so the user can tell a ghost from a bug. It follows the
    // control's transform, takes no input and has no Effect and no animation (photosafe as it is).
    private static readonly IBrush EmberOutline = new SolidColorBrush(Color.FromArgb(191, 0xFF, 0x8A, 0x5C)).ToImmutable();
    private Border? _outline;
    protected virtual bool OutlineOnApply => Roles.Count > 0;
    internal bool HasOutline => _outline?.Parent != null;

    private void ShowOutline(Control c)
    {
        try
        {
            if (AdornerLayer.GetAdornerLayer(c) is not { } layer) return;
            _outline = new Border
            {
                BorderBrush = EmberOutline, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(6),
                Margin = new Thickness(-3), IsHitTestVisible = false, Focusable = false,
            };
            AdornerLayer.SetAdornedElement(_outline, c);
            layer.Children.Add(_outline);
        }
        catch (Exception ex) { Log.Debug("Possession {Id}: outline failed: {E}", Id, ex.Message); _outline = null; }
    }

    private void DropOutline()
    {
        try { (_outline?.Parent as Panel)?.Children.Remove(_outline!); }
        catch (Exception ex) { Log.Debug("Possession {Id}: outline drop failed: {E}", Id, ex.Message); }
        _outline = null;
    }

    /// <summary>Overlays in place right now (tests: nothing is left behind).</summary>
    internal int OverlayCount => _overlays.Count;

    public bool CanApply(PossessionContext ctx, PossessionTarget? target)
    {
        try
        {
            if (IsLive || ctx == null) return false;
            if (UsesFlicker && ctx.Photosafe) return false;
            if (Roles.Count > 0)
            {
                if (target?.Element is not Control c || !c.IsEffectivelyVisible) return false;
                if (PossessionTree.IsOffLimits(c)) return false;   // the law, asked again at the door
                if (!PossessionTree.IsDisplayRole(target.Role)) return false;
                bool mine = false;
                foreach (var r in Roles) if (r == target.Role) { mine = true; break; }
                if (!mine) return false;
            }
            return CanApplyCore(ctx, target);
        }
        catch { return false; }
    }

    public Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (IsLive || ctx == null) return Task.CompletedTask;
        if (Roles.Count > 0 && (target?.Element is not Control tc || PossessionTree.IsOffLimits(tc))) return Task.CompletedTask;
        Ctx = ctx;
        Victim = target?.Element as Control;
        Cts = new CancellationTokenSource();
        IsLive = true;
        try
        {
            ApplyCore(ctx, target);
            if (IsLive && OutlineOnApply && Victim is { } victim) ShowOutline(victim);
        }
        catch (Exception ex)
        {
            Log.Warning("Possession {Id}: apply failed: {E}", Id, ex.Message);
            Restore();
        }
        return Task.CompletedTask;
    }

    public async Task UndoAsync(TimeSpan duration)
    {
        if (!IsLive) return;
        int epoch = _epoch;
        // WPF UndoMs: zero is the synchronous path, no animation at all.
        double ms = duration <= TimeSpan.Zero ? 0 : Math.Clamp(duration.TotalMilliseconds, 220, 700);
        if (ms > 0)
        {
            try
            {
                StopMotion();
                if (SettleCore(ms))
                {
                    await Task.Delay((int)ms + 20).ConfigureAwait(true);
                    if (epoch != _epoch) return;   // a panic (or a new haunt) got here first
                }
            }
            catch (Exception ex) { Log.Debug("Possession {Id}: undo ease failed: {E}", Id, ex.Message); }
        }
        Restore();
    }

    /// <summary>Everything back, now, in this call. Safe twice and before Apply.</summary>
    private void Restore()
    {
        _epoch++;
        StopMotion();
        try { RestoreCore(); } catch (Exception ex) { Log.Warning("Possession {Id}: restore failed: {E}", Id, ex.Message); }
        for (int i = _overlays.Count - 1; i >= 0; i--)
        {
            try { _overlays[i].Dispose(); } catch (Exception ex) { Log.Warning("Possession {Id}: overlay drop failed: {E}", Id, ex.Message); }
        }
        _overlays.Clear();
        _leases.Clear();
        DropOutline();
        try { Cts?.Dispose(); } catch { }
        Cts = null;
        Lease = null;
        Victim = null;
        Ctx = null;
        IsLive = false;
    }

    private void StopMotion()
    {
        try { Cts?.Cancel(); } catch { }
        // A cancelled tween is stopped through its timer, or it would land on its end value.
        foreach (var t in _timers) { try { t.Stop(); } catch { } }
        _timers.Clear();
        foreach (var b in _beats) { try { b.Timer.Stop(); } catch { } }
        _beats.Clear();
    }

    /// <summary>A later beat of a choreography. Dropped, unplayed, by any undo.</summary>
    protected void At(double ms, Action beat)
    {
        DispatcherTimer? timer = null;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(Math.Max(1, ms)), DispatcherPriority.Normal, (_, _) =>
        {
            timer!.Stop();
            _beats.RemoveAll(b => ReferenceEquals(b.Timer, timer));
            if (!IsLive) return;
            try { beat(); } catch (Exception ex) { Log.Debug("Possession {Id}: beat failed: {E}", Id, ex.Message); }
        });
        _beats.Add((timer, beat));
        timer.Start();
    }

    /// <summary>Tests: play every beat still waiting, in order, now.</summary>
    internal void PlayBeatsNow()
    {
        var waiting = _beats.ToArray();
        _beats.Clear();
        foreach (var (timer, beat) in waiting)
        {
            timer.Stop();
            if (IsLive) beat();
        }
    }

    /// <summary>Ease every borrowed transform home over <paramref name="ms"/>.</summary>
    protected bool SettleLeases(double ms)
    {
        foreach (var l in _leases)
        {
            Tween(l.Translate, ms, false,
                (0, TranslateTransform.XProperty, l.Translate.X), (1, TranslateTransform.XProperty, 0),
                (0, TranslateTransform.YProperty, l.Translate.Y), (1, TranslateTransform.YProperty, 0));
            Tween(l.Scale, ms, false,
                (0, ScaleTransform.ScaleXProperty, l.Scale.ScaleX), (1, ScaleTransform.ScaleXProperty, 1),
                (0, ScaleTransform.ScaleYProperty, l.Scale.ScaleY), (1, ScaleTransform.ScaleYProperty, 1));
            Tween(l.Skew, ms, false,
                (0, SkewTransform.AngleXProperty, l.Skew.AngleX), (1, SkewTransform.AngleXProperty, 0),
                (0, SkewTransform.AngleYProperty, l.Skew.AngleY), (1, SkewTransform.AngleYProperty, 0));
            Tween(l.Rotate, ms, false, (0, RotateTransform.AngleProperty, l.Rotate.Angle), (1, RotateTransform.AngleProperty, 0));
        }
        return _leases.Count > 0;
    }

    protected virtual bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) => true;
    protected abstract void ApplyCore(PossessionContext ctx, PossessionTarget? target);
    /// <summary>Start the ease home over <paramref name="ms"/>; false = nothing to ease.</summary>
    protected virtual bool SettleCore(double ms) => false;
    protected virtual void RestoreCore() { }

    /// <summary>Photosafe halves every motion amplitude (WPF PossessionEffectBase.Amp).</summary>
    protected double Amp(double v) => Photosafe ? v * 0.5 : v;
    protected double Rand(double lo, double hi) => lo + Rng.NextDouble() * (hi - lo);
    protected double Sign() => Rng.Next(2) == 0 ? -1 : 1;

    /// <summary>Lay a value over the control's own. Disposed on undo: the control's value was never written.</summary>
    protected void Overlay<T>(AvaloniaObject target, StyledProperty<T> property, T value)
    {
        var d = target.SetValue(property, value, BindingPriority.Animation);
        if (d != null) _overlays.Add(d);
    }

    /// <summary>An overlay the effect replaces as it goes (a face that changes step by step).</summary>
    protected IDisposable? OverlayHandle<T>(AvaloniaObject target, StyledProperty<T> property, T value)
    {
        var d = target.SetValue(property, value, BindingPriority.Animation);
        if (d != null) _overlays.Add(d);
        return d;
    }

    protected void Drop(IDisposable? overlay)
    {
        if (overlay == null) return;
        _overlays.Remove(overlay);
        try { overlay.Dispose(); } catch (Exception ex) { Log.Debug("Possession {Id}: overlay drop failed: {E}", Id, ex.Message); }
    }

    /// <summary>A bounded beat for step effects; stopped with every other motion on undo.</summary>
    protected void Every(double ms, Action tick)
    {
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(ms), DispatcherPriority.Normal, (_, _) =>
        {
            try { tick(); } catch (Exception ex) { Log.Debug("Possession {Id}: step failed: {E}", Id, ex.Message); }
        });
        _timers.Add(timer);
        timer.Start();
    }

    /// <summary>Borrow the victim's render transform (its own one stays composed underneath).</summary>
    protected PossessionLease? TakeLease(RelativePoint origin)
    {
        if (Lease != null) return Lease;
        if (Victim is not { } c) return null;
        return Lease = LeaseFor(c, origin);
    }

    /// <summary>Borrow any control's render transform (a scene has several victims).</summary>
    protected PossessionLease LeaseFor(Control c, RelativePoint origin)
    {
        var lease = new PossessionLease();
        lease.Group.Children.Add(lease.Scale);
        lease.Group.Children.Add(lease.Skew);
        lease.Group.Children.Add(lease.Rotate);
        lease.Group.Children.Add(lease.Translate);
        if (c.RenderTransform is Transform own) lease.Group.Children.Add(own);
        Overlay(c, Visual.RenderTransformOriginProperty, origin);
        Overlay<ITransform?>(c, Visual.RenderTransformProperty, lease.Group);
        _leases.Add(lease);
        return lease;
    }

    /// <summary>Stop the tweens in flight (a new move on the same lease would fight them).</summary>
    protected void StopTweens()
    {
        foreach (var t in _timers) { try { t.Stop(); } catch { } }
        _timers.Clear();
    }

    protected void Tween(AvaloniaObject target, double ms, bool loop, params (double, AvaloniaProperty, double)[] keys)
    {
        // Callers pass Cts.Token for a haunt's motion; the settle home runs after that token is cancelled.
        _timers.Add(TransformTween.Run(target, TimeSpan.FromMilliseconds(ms), keys, loop: loop));
    }

    /// <summary>One full sine swing from..to..from as tween keys (WPF PossAnim.Oscillate).</summary>
    protected static (double, AvaloniaProperty, double)[] Swing(AvaloniaProperty p, double from, double to)
    {
        var keys = new (double, AvaloniaProperty, double)[17];
        for (int i = 0; i <= 16; i++)
        {
            double cue = i / 16.0;
            double w = (1 - Math.Cos(cue * 2 * Math.PI)) / 2;
            keys[i] = (cue, p, from + (to - from) * w);
        }
        return keys;
    }
}

/// <summary>R0. One character of a label goes wrong: a look-alike digit, or two letters trade places.</summary>
internal sealed class TypoEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Label, PossessionRole.TabHeader, PossessionRole.Title };
    private static readonly (char From, char To)[] _lookAlikes =
    {
        ('o', '0'), ('O', '0'), ('l', '1'), ('I', '1'), ('e', '3'), ('E', '3'),
        ('a', '4'), ('A', '4'), ('s', '5'), ('S', '5'), ('t', '7'), ('g', '9'),
    };

    public override string Id => "typo";
    public override PossessionRung MinRung => PossessionRung.Settle;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => false;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(4);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (target?.Role == PossessionRole.Timer) return false;   // the timer VALUE is never touched
        var tb = PossessionTree.FindTextBlock(target?.Element);
        return PossessionTree.IsRewritable(tb, 3) && CanMutate(tb!.Text!);
    }

    protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var tb = PossessionTree.FindTextBlock(target?.Element);
        if (!PossessionTree.IsRewritable(tb, 3)) return;
        var typo = Mutate(tb!.Text!, ctx.Rng);
        if (typo == null || string.Equals(typo, tb.Text, StringComparison.Ordinal)) return;
        Overlay(tb, TextBlock.TextProperty, typo);
    }

    internal static bool CanMutate(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 3) return false;
        for (int i = 1; i < text.Length; i++)
        {
            foreach (var (from, _) in _lookAlikes) if (text[i] == from) return true;
            if (i + 1 < text.Length && char.IsLetter(text[i]) && char.IsLetter(text[i + 1]) && text[i] != text[i + 1]) return true;
        }
        return false;
    }

    /// <summary>Never the first character (WPF: a wrong capital reads as a bug, not a ghost).</summary>
    internal static string? Mutate(string text, Random rng)
    {
        var swaps = new List<(int Index, char To)>();
        var trades = new List<int>();
        for (int i = 1; i < text.Length; i++)
        {
            foreach (var (from, to) in _lookAlikes) if (text[i] == from) swaps.Add((i, to));
            if (i + 1 < text.Length && char.IsLetter(text[i]) && char.IsLetter(text[i + 1]) && text[i] != text[i + 1]) trades.Add(i);
        }
        bool preferSwap = swaps.Count > 0 && (trades.Count == 0 || rng.Next(100) < 65);
        var sb = new StringBuilder(text);
        if (preferSwap)
        {
            var pick = swaps[rng.Next(swaps.Count)];
            sb[pick.Index] = pick.To;
            return sb.ToString();
        }
        if (trades.Count > 0)
        {
            int i = trades[rng.Next(trades.Count)];
            (sb[i], sb[i + 1]) = (sb[i + 1], sb[i]);
            return sb.ToString();
        }
        return null;
    }
}

/// <summary>R1. A label slides 6 to 8 px off its mark and back, slowly, for five seconds.</summary>
internal sealed class DriftEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Label, PossessionRole.Title };
    public override string Id => "drift";
    public override PossessionRung MinRung => PossessionRung.Drift;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => false;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(5);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (TakeLease(RelativePoint.Center) is not { } lease) return;
        double reach = Amp(Rand(6, 8)) * Sign();
        bool sideways = Rng.Next(100) < 70;
        Tween(lease.Translate, 5000, true, Swing(sideways ? TranslateTransform.XProperty : TranslateTransform.YProperty, 0, reach));
    }

    protected override bool SettleCore(double ms)
    {
        if (Lease is not { } lease) return false;
        Tween(lease.Translate, ms, false,
            (0, TranslateTransform.XProperty, lease.Translate.X), (1, TranslateTransform.XProperty, 0),
            (0, TranslateTransform.YProperty, lease.Translate.Y), (1, TranslateTransform.YProperty, 0));
        return true;
    }
}

/// <summary>R2. A card sags from its bottom edge while the pointer is on it, and firms up when it
/// leaves. WPF also blurs it; this head never puts an Effect under looping FX, so the sag is the whole
/// of it. Pointer events are only listened to, never handled.</summary>
internal sealed class MeltEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Card, PossessionRole.Button };
    private const double MeltMs = 900, FirmMs = 600;
    protected override bool OutlineOnApply => false;   // WPF: melt marks its victim only under the pointer
    private EventHandler<PointerEventArgs>? _enter, _leave;
    private Control? _hooked;

    public override string Id => "melt";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => true;
    public override double Weight => 4;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(25);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    /// <summary>True while the card is sagging (tests).</summary>
    internal bool IsMelted { get; private set; }

    protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (Victim is not { } c) return;
        _hooked = c;
        _enter = (_, _) => Melt();
        _leave = (_, _) => Firm(FirmMs);
        c.PointerEntered += _enter;
        c.PointerExited += _leave;
        if (c.IsPointerOver) Melt();
    }

    internal void Melt()
    {
        if (!IsLive || IsMelted) return;
        if (TakeLease(new RelativePoint(0.5, 1.0, RelativeUnit.Relative)) is not { } lease) return;
        IsMelted = true;
        StopTweens();
        Tween(lease.Skew, MeltMs, false, (0, SkewTransform.AngleYProperty, lease.Skew.AngleY), (1, SkewTransform.AngleYProperty, Amp(6)));
        Tween(lease.Scale, MeltMs, false, (0, ScaleTransform.ScaleYProperty, lease.Scale.ScaleY), (1, ScaleTransform.ScaleYProperty, 1.0 - Amp(0.08)));
        Tween(lease.Translate, MeltMs, false, (0, TranslateTransform.YProperty, lease.Translate.Y), (1, TranslateTransform.YProperty, Amp(6)));
    }

    internal void Firm(double ms)
    {
        if (!IsLive || Lease is not { } lease) return;
        IsMelted = false;
        StopTweens();
        Tween(lease.Skew, ms, false, (0, SkewTransform.AngleYProperty, lease.Skew.AngleY), (1, SkewTransform.AngleYProperty, 0));
        Tween(lease.Scale, ms, false, (0, ScaleTransform.ScaleYProperty, lease.Scale.ScaleY), (1, ScaleTransform.ScaleYProperty, 1.0));
        Tween(lease.Translate, ms, false, (0, TranslateTransform.YProperty, lease.Translate.Y), (1, TranslateTransform.YProperty, 0));
    }

    protected override bool SettleCore(double ms)
    {
        Unhook();
        if (Lease == null) return false;
        Firm(ms);
        return true;
    }

    protected override void RestoreCore()
    {
        Unhook();
        IsMelted = false;
    }

    private void Unhook()
    {
        if (_hooked is { } c)
        {
            if (_enter != null) c.PointerEntered -= _enter;
            if (_leave != null) c.PointerExited -= _leave;
        }
        _hooked = null;
        _enter = _leave = null;
    }
}

/// <summary>R3. No victim: the window edge pulses and the room jolts once. Photosafe: the pulse only
/// (the shell softens it), never the shake.</summary>
internal sealed class CrackEffect : PossessionEffectBase
{
    public override string Id => "crack";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => false;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(1);

    protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        ctx.Host.EdgePulse(0.4);
        if (!ctx.Photosafe) ctx.Host.Shake(0.25, 180);
    }
}

/// <summary>R4, Full Doki only: the companion portrait tears into three ember bands for 200 ms and
/// snaps back (the tube draws it on its own glitch layer, never on the real portrait). It is a flicker:
/// skipped entirely when photosafe.</summary>
internal sealed class GlitchPortraitEffect : PossessionEffectBase
{
    internal const int GlitchMs = 200;
    /// <summary>The tube to tear (the live one; tests hand their own or none).</summary>
    internal Func<global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow?> Tube =
        () => global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow.Live;

    public override string Id => "glitchportrait";
    public override PossessionRung MinRung => PossessionRung.ItKnows;
    public override PossessionIntensity MinIntensity => PossessionIntensity.FullDoki;
    public override bool IsBig => false;
    public override bool UsesFlicker => true;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(250);

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (ctx.Photosafe) return false;   // belt: the deck already filters UsesFlicker
        return Tube() is { IsVisible: true } tube && tube.Bounds.Width > 8 && tube.Bounds.Height > 8;
    }

    protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (ctx.Photosafe) return;
        Tube()?.GlitchPortrait(GlitchMs);
    }

    protected override void RestoreCore() => Tube()?.ClearGlitchPortrait();
}

/// <summary>R3, Full Doki only, big. The title says something else until the lockdown ends. The
/// lines are the warden's own voice and are not loc keys in WPF either.</summary>
internal sealed class RetitleEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Title };
    internal static readonly string[] Lines =
    {
        "still here?",
        "there is no exit",
        "i can see you reading this",
        "the timer is not the way out",
        "you already know how this ends",
    };

    public override string Id => "retitle";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override PossessionIntensity MinIntensity => PossessionIntensity.FullDoki;
    public override bool IsBig => true;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.Zero;
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (target?.Role == PossessionRole.Timer) return false;
        return PossessionTree.IsRewritable(PossessionTree.FindTextBlock(target?.Element), 2);
    }

    protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var tb = PossessionTree.FindTextBlock(target?.Element);
        if (!PossessionTree.IsRewritable(tb, 2)) return;
        var line = Lines[ctx.Rng.Next(Lines.Length)];
        if (string.Equals(line, tb!.Text, StringComparison.OrdinalIgnoreCase))
            line = Lines[(Array.IndexOf(Lines, line) + 1) % Lines.Length];
        Overlay(tb, TextBlock.TextProperty, line);
    }
}
