using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R0 "breathe" - the card is alive. A 1.5% swell in and out from the centre for the length of the
/// hold. You never catch it moving, you only notice that it is breathing.
/// </summary>
public sealed class BreatheEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Card, PossessionRole.Button };

    public override string Id => "breathe";
    public override PossessionRung MinRung => PossessionRung.Settle;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => false;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(3500);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        var lease = TakeLease();
        if (lease == null) return Task.CompletedTask;

        lease.SetOrigin(new Point(0.5, 0.5));
        double peak = 1.0 + Amp(0.015);

        PossAnim.Oscillate(lease.Scale, ScaleTransform.ScaleXProperty, 1.0, peak, 1600, PossAnim.Sine);
        PossAnim.Oscillate(lease.Scale, ScaleTransform.ScaleYProperty, 1.0, peak, 1600, PossAnim.Sine);
        return Task.CompletedTask;
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        var lease = Lease;
        if (lease == null) return;

        // Stop the ping-pong where it stands, then ease the remaining swell out instead of snapping.
        double sx = 1.0, sy = 1.0;
        try
        {
            sx = lease.Scale.ScaleX;
            sy = lease.Scale.ScaleY;
        }
        catch { }
        PossAnim.Settle(lease.Scale, ScaleTransform.ScaleXProperty, sx);
        PossAnim.Settle(lease.Scale, ScaleTransform.ScaleYProperty, sy);

        double ms = UndoMs(duration, 250, 700);
        if (ms <= 0) return;
        PossAnim.To(lease.Scale, ScaleTransform.ScaleXProperty, 1.0, ms, PossAnim.EaseInOut);
        PossAnim.To(lease.Scale, ScaleTransform.ScaleYProperty, 1.0, ms, PossAnim.EaseInOut);
        await PossAnim.DelayAsync(ms + 20, CancellationToken.None).ConfigureAwait(true);
    }
}
