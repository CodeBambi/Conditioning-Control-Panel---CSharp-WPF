using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R2 "wobble" - the countdown digits rock a couple of degrees, about 1.3 times a second, like the
/// clock is drunk. HARD RULE: the VALUE is never touched. The number stays true; only the glass moves.
/// </summary>
public sealed class WobbleEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Timer };

    /// <summary>1.3 Hz means a full there-and-back every ~770 ms, so a half swing is ~385 ms.</summary>
    private const double HalfPeriodMs = 385;

    public override string Id => "wobble";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => false;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(6);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        var lease = TakeLease();
        if (lease == null) return Task.CompletedTask;

        lease.SetOrigin(new Point(0.5, 0.5));
        double swing = Amp(2);
        PossAnim.Oscillate(lease.Rotate, RotateTransform.AngleProperty, -swing, swing, HalfPeriodMs, PossAnim.Sine);
        return Task.CompletedTask;
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        var lease = Lease;
        if (lease == null) return;

        double angle = 0;
        try { angle = lease.Rotate.Angle; } catch { }
        PossAnim.Settle(lease.Rotate, RotateTransform.AngleProperty, angle);

        double ms = UndoMs(duration, 250, 700);
        if (ms <= 0) return;
        PossAnim.To(lease.Rotate, RotateTransform.AngleProperty, 0, ms, PossAnim.EaseInOut);
        await PossAnim.DelayAsync(ms + 20, CancellationToken.None).ConfigureAwait(true);
    }
}
