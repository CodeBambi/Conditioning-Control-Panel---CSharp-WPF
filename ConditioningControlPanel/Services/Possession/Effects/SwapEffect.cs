using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R1 "swap" - two buttons that live in the same panel glide into each other's seats and STAY there,
/// clickable where they land. The glide is deliberately slow (550 ms, cubic) so nobody can mistake it
/// for a layout pass: you watch Start walk over to where Stop was.
/// </summary>
public sealed class SwapEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Button };

    private const double GlideMs = 550;

    private PossessionTarget? _partner;
    private TransformLease? _partnerLease;
    private double _dx, _dy;       // design units for the primary
    private double _dxB, _dyB;     // design units for the partner

    public override string Id => "swap";
    public override PossessionRung MinRung => PossessionRung.Drift;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => true;
    public override double Weight => 4;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(30);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    /// <summary>Both halves charge together, so the base sequence is driven by hand here.</summary>
    protected override bool ChargeOnApply => false;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
        => target != null && FindPartner(ctx, target) != null;

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (target?.Element == null) return;

        _partner = FindPartner(ctx, target);
        if (_partner?.Element == null) return;

        var a = target.Element;
        var b = _partner.Element;

        var ra = PossessionVisual.BoundsOf(ctx.Host, a);
        var rb = PossessionVisual.BoundsOf(ctx.Host, b);
        if (ra.IsEmpty || rb.IsEmpty) { _partner = null; return; }

        double layerDx = rb.X - ra.X;
        double layerDy = rb.Y - ra.Y;
        if (Math.Abs(layerDx) < 4 && Math.Abs(layerDy) < 4) { _partner = null; return; }

        // The seats were measured in layer pixels but a TransformLease moves the control in DESIGN
        // units (it lives inside the Viewbox), so the delta comes back down through each victim's own
        // scale before it is animated.
        var sa = PossessionVisual.ScaleOf(ctx.Host, a);
        var sb = PossessionVisual.ScaleOf(ctx.Host, b);
        _dx = layerDx / sa.X;
        _dy = layerDy / sa.Y;
        _dxB = -layerDx / sb.X;
        _dyB = -layerDy / sb.Y;

        _partner.IsLive = true;
        NameOverrideText = BuildName(target, _partner);

        // The charge fires over BOTH victims at once, then the grammar's name + outlines.
        var partnerCharge = SafeChargeAsync(ctx, b, ct);
        await ChargeAndPossessAsync(a, ct).ConfigureAwait(true);
        await partnerCharge.ConfigureAwait(true);
        if (ct.IsCancellationRequested) return;
        PossessAlso(b);

        var leaseA = TakeLease(a);
        _partnerLease = TransformLease.Take(b);
        if (leaseA == null || _partnerLease == null) return;

        PossAnim.To(leaseA.Translate, TranslateTransform.XProperty, _dx, GlideMs, PossAnim.EaseInOut);
        PossAnim.To(leaseA.Translate, TranslateTransform.YProperty, _dy, GlideMs, PossAnim.EaseInOut);
        PossAnim.To(_partnerLease.Translate, TranslateTransform.XProperty, _dxB, GlideMs, PossAnim.EaseInOut);
        PossAnim.To(_partnerLease.Translate, TranslateTransform.YProperty, _dyB, GlideMs, PossAnim.EaseInOut);

        await PossAnim.DelayAsync(GlideMs + 20, ct).ConfigureAwait(true);
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        double ms = UndoMs(duration, 300, 900);
        var leaseA = ms > 0 ? Lease : null;
        var leaseB = ms > 0 ? _partnerLease : null;

        if (leaseA != null)
        {
            PossAnim.To(leaseA.Translate, TranslateTransform.XProperty, 0, ms, PossAnim.EaseInOut);
            PossAnim.To(leaseA.Translate, TranslateTransform.YProperty, 0, ms, PossAnim.EaseInOut);
        }
        if (leaseB != null)
        {
            PossAnim.To(leaseB.Translate, TranslateTransform.XProperty, 0, ms, PossAnim.EaseInOut);
            PossAnim.To(leaseB.Translate, TranslateTransform.YProperty, 0, ms, PossAnim.EaseInOut);
        }
        if (leaseA != null || leaseB != null)
            await PossAnim.DelayAsync(ms + 20, CancellationToken.None).ConfigureAwait(true);

        try { _partnerLease?.ReleaseImmediate(); } catch { }
        _partnerLease = null;

        if (_partner != null) { try { _partner.IsLive = false; } catch { } }
        _partner = null;
    }

    private static async Task SafeChargeAsync(PossessionContext ctx, FrameworkElement el, CancellationToken ct)
    {
        try { await ctx.Attribution.ChargeAsync(el, ct).ConfigureAwait(true); }
        catch (Exception ex) { App.Logger?.Warning("Possession swap partner charge failed: {Error}", ex.Message); }
    }

    /// <summary>A visible, idle sibling button under the same panel. Nothing else is a fair swap.</summary>
    private static PossessionTarget? FindPartner(PossessionContext ctx, PossessionTarget target)
    {
        try
        {
            var el = target.Element;
            if (el == null) return null;
            var parent = el.Parent;
            if (parent == null) return null;

            var origin = PossessionVisual.BoundsOf(ctx.Host, el);
            if (origin.IsEmpty) return null;
            PossessionTarget? best = null;
            double bestDist = double.MaxValue;

            foreach (var t in ctx.Host.Targets)
            {
                if (t == null || ReferenceEquals(t, target)) continue;
                if (t.Role != PossessionRole.Button) continue;
                if (t.IsLive) continue;
                var other = t.Element;
                if (other == null || ReferenceEquals(other, el)) continue;
                if (!other.IsVisible || other.ActualWidth <= 0 || other.ActualHeight <= 0) continue;
                if (!ReferenceEquals(other.Parent, parent)) continue;
                if (PossessionVisual.IsWindowChrome(other)) continue;
                if (TransformLease.IsLeased(other)) continue;

                var p = PossessionVisual.BoundsOf(ctx.Host, other);
                if (p.IsEmpty) continue;
                double d = Math.Abs(p.X - origin.X) + Math.Abs(p.Y - origin.Y);
                if (d < 4) continue;              // stacked on top of each other: no visible swap
                if (d < bestDist) { bestDist = d; best = t; }
            }
            return best;
        }
        catch { return null; }
    }

    private static string BuildName(PossessionTarget a, PossessionTarget b)
    {
        bool hasA = !string.IsNullOrWhiteSpace(a.DisplayName);
        bool hasB = !string.IsNullOrWhiteSpace(b.DisplayName);
        if (hasA && hasB) return a.DisplayName + " and " + b.DisplayName;
        if (hasA) return a.DisplayName;
        if (hasB) return b.DisplayName;
        return "two buttons";
    }
}
