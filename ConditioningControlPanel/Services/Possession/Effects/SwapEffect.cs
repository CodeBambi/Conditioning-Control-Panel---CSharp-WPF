using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R1 "swap" - two buttons glide into each other's seats and STAY there, clickable where they land.
/// The glide is deliberately slow (550 ms, cubic) so nobody can mistake it for a layout pass: you
/// watch Start walk over to where Stop was.
///
/// <para><b>Wave 2 - who is a fair partner.</b> The old rule was "same Parent", which in a window
/// built out of cards, rails and templated rows almost never matched: the two buttons a user sees
/// side by side usually live in different panels. The rule is now what the EYE uses instead - close
/// together (centres within ~220 px) and about the same size (each dimension inside a 1.6 ratio) - so
/// a swap looks like two neighbours trading places rather than a button teleporting across the room.
/// A Start/Stop pair is preferred over every other candidate, because that is the swap that costs
/// something: the one where muscle memory presses the wrong one.</para>
///
/// <para>The window chrome is still excluded here (unlike dodge). A dodging X is friction you can see
/// and beat; an X sitting in the minimize slot means the button under your cursor quietly does
/// something else, which is the one thing POSSESSION.md never allows.</para>
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
        => target != null
           && !PossessionVisual.IsWindowChrome(target.Element)
           && FindPartner(ctx, target) != null;

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

    /// <summary>How far apart two buttons may be and still read as neighbours trading places.</summary>
    private const double MaxPairDistance = 220;

    /// <summary>How different two buttons may look and still read as a swap rather than a resize.</summary>
    private const double MaxSizeRatio = 1.6;

    /// <summary>
    /// A visible, idle button close to this one and roughly its size. A Start/Stop pair wins over any
    /// other candidate; after that, the nearest one does.
    /// </summary>
    private static PossessionTarget? FindPartner(PossessionContext ctx, PossessionTarget target)
    {
        try
        {
            var el = target.Element;
            if (el == null) return null;

            var origin = PossessionVisual.BoundsOf(ctx.Host, el);
            if (origin.IsEmpty || origin.Width <= 0 || origin.Height <= 0) return null;
            var originCentre = new Point(origin.X + origin.Width / 2, origin.Y + origin.Height / 2);
            bool originIsStartStop = LooksStartStop(el);

            PossessionTarget? best = null;
            double bestScore = double.MaxValue;

            foreach (var t in ctx.Host.Targets)
            {
                if (t == null || ReferenceEquals(t, target)) continue;
                if (t.Role != PossessionRole.Button) continue;
                if (t.IsLive) continue;
                var other = t.Element;
                if (other == null || ReferenceEquals(other, el)) continue;
                if (!other.IsVisible || other.ActualWidth <= 0 || other.ActualHeight <= 0) continue;
                if (PossessionVisual.IsWindowChrome(other)) continue;
                if (TransformLease.IsLeased(other)) continue;

                var p = PossessionVisual.BoundsOf(ctx.Host, other);
                if (p.IsEmpty || p.Width <= 0 || p.Height <= 0) continue;

                var centre = new Point(p.X + p.Width / 2, p.Y + p.Height / 2);
                double d = (centre - originCentre).Length;
                if (d < 8) continue;                    // stacked on top of each other: no visible swap
                if (d > MaxPairDistance) continue;
                if (Ratio(origin.Width, p.Width) > MaxSizeRatio) continue;
                if (Ratio(origin.Height, p.Height) > MaxSizeRatio) continue;

                // Distance is the score; a Start/Stop pair simply beats everything in range.
                double score = d;
                if (originIsStartStop && LooksStartStop(other)) score -= 10000;

                if (score < bestScore) { bestScore = score; best = t; }
            }
            return best;
        }
        catch { return null; }
    }

    /// <summary>
    /// "Is this the Start or the Stop button." What it SAYS first (including the localized words, via
    /// RelabelEffect), then what it is CALLED - an icon-only transport button has no text at all, but
    /// its x:Name or AutomationId almost always carries start / stop.
    /// </summary>
    private static bool LooksStartStop(FrameworkElement? el)
    {
        try
        {
            if (el == null) return false;
            if (RelabelEffect.IsStartOrStop(LabelOf(el))) return true;

            var names = new[]
            {
                el.Name,
                System.Windows.Automation.AutomationProperties.GetAutomationId(el),
                System.Windows.Automation.AutomationProperties.GetName(el),
            };
            foreach (var n in names)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                if (n.IndexOf("start", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
        }
        catch { }
        return false;
    }

    private static double Ratio(double a, double b)
    {
        if (a <= 0 || b <= 0) return double.MaxValue;
        return a > b ? a / b : b / a;
    }

    /// <summary>What the button says: its string Content first, then whatever text it renders, then
    /// the accessibility name a templated icon button carries instead.</summary>
    private static string? LabelOf(FrameworkElement? el)
    {
        try
        {
            if (el == null) return null;
            var content = RewriteEffect.StringContentOf(el);
            if (!string.IsNullOrWhiteSpace(content)) return content;

            var tb = PossessionVisual.FindTextBlock(el);
            if (tb != null && !string.IsNullOrWhiteSpace(tb.Text)) return tb.Text;

            var automation = System.Windows.Automation.AutomationProperties.GetName(el);
            if (!string.IsNullOrWhiteSpace(automation)) return automation;

            return string.IsNullOrWhiteSpace(el.Name) ? null : el.Name;
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
