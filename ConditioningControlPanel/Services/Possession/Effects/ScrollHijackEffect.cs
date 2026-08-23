using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R2 "scrollhijack" - the page reads ahead without you. The scroll viewer eases forty pixels down over
/// 600 ms and then comes back to exactly where it was, like something else glanced at the rest of the
/// list. Three seconds, no state, nothing broken.
///
/// <para><b>It never fights the user.</b> VerticalOffset is not settable and not animatable, so this
/// steps ScrollToVerticalOffset on a timer - which means the user's own wheel can land between two
/// steps. Every step compares where the viewer actually IS against where we last put it: any
/// difference is the user, and their scroll becomes the new home rather than something to correct.
/// Whatever they did, the offset they end on is the offset they chose.</para>
/// </summary>
public sealed class ScrollHijackEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Scroll };

    private const double PushPx = 40;
    private const double LegMs = 600;
    private const int Steps = 24;

    private ScrollViewer? _sv;
    private double _home;        // where the page belongs; moves when the USER moves it
    private double _lastSet;     // where we last put it, so we can tell our motion from theirs

    public override string Id => "scrollhijack";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => true;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(3);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        NameOverrideText = "the page";
        var sv = Resolve(target?.Element);
        if (sv == null) return false;
        // Nothing to hijack on a page that does not scroll, and a page with less than the push left
        // would just clamp: the motion has to be visible or the effect is a silent no-op.
        return sv.ScrollableHeight - sv.VerticalOffset >= PushPx;
    }

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        _sv = Resolve(target?.Element);
        if (_sv == null) return;

        _home = _sv.VerticalOffset;
        _lastSet = _home;

        if (!await LegAsync(0, PushPx, ct).ConfigureAwait(true)) return;
        await LegAsync(PushPx, 0, ct).ConfigureAwait(true);
    }

    /// <summary>One eased leg from home+<paramref name="from"/> to home+<paramref name="to"/>.</summary>
    private async Task<bool> LegAsync(double from, double to, CancellationToken ct)
    {
        var sv = _sv;
        if (sv == null) return false;
        var ease = PossAnim.EaseInOut;

        for (int i = 1; i <= Steps; i++)
        {
            if (ct.IsCancellationRequested) return false;

            AdoptUserScroll(sv);

            double t = ease.Ease(i / (double)Steps);
            double want = Clamp(sv, _home + from + (to - from) * t);
            try { sv.ScrollToVerticalOffset(want); } catch { return false; }
            _lastSet = want;

            if (!await PossAnim.DelayAsync(LegMs / Steps, ct).ConfigureAwait(true)) return false;
        }
        return true;
    }

    /// <summary>The user moved the page while we were moving it: take their offset as the new home and
    /// carry on from there. Two pixels of slack, because a smooth-scrolling viewer settles lazily.</summary>
    private void AdoptUserScroll(ScrollViewer sv)
    {
        try
        {
            double drift = sv.VerticalOffset - _lastSet;
            if (Math.Abs(drift) > 2) _home += drift;
        }
        catch { }
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        var sv = _sv;
        _sv = null;
        if (sv == null) return Task.CompletedTask;

        try
        {
            AdoptUserScroll(sv);
            double want = Clamp(sv, _home);
            if (Math.Abs(sv.VerticalOffset - want) > 0.5) sv.ScrollToVerticalOffset(want);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession scrollhijack restore failed: {Error}", ex.Message); }

        return Task.CompletedTask;
    }

    private static double Clamp(ScrollViewer sv, double value)
    {
        try { return Math.Clamp(value, 0, Math.Max(0, sv.ScrollableHeight)); }
        catch { return value; }
    }

    /// <summary>The victim itself when the auto-tagger tagged the ScrollViewer, else the first one
    /// inside whatever it did tag.</summary>
    private static ScrollViewer? Resolve(DependencyObject? node) => Find(node, 0);

    private static ScrollViewer? Find(DependencyObject? node, int depth)
    {
        if (node == null || depth > 10) return null;
        try
        {
            if (node is ScrollViewer sv) return sv;
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                var found = Find(VisualTreeHelper.GetChild(node, i), depth + 1);
                if (found != null) return found;
            }
        }
        catch { }
        return null;
    }
}
