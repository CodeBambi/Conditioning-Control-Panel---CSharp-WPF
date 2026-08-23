using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R1 "drift" - a label quietly slides a few pixels out of alignment and back, over and over, like the
/// layout is loose. Slow enough that you only catch it in your peripheral vision.
/// </summary>
public sealed class DriftEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Label, PossessionRole.Title };

    public override string Id => "drift";
    public override PossessionRung MinRung => PossessionRung.Drift;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => false;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(5);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        var lease = TakeLease();
        if (lease == null) return Task.CompletedTask;

        // Wave 2 floor: 6 design units shrank to ~4 window px at the shipped window size and got lost
        // in the peripheral vision it was aiming for. 6-8 with the same slow sine keeps the "loose
        // layout" read and actually leaves alignment.
        double reach = Amp(Rand(6, 8)) * Sign();
        bool sideways = Rng.Next(100) < 70;

        PossAnim.Oscillate(
            lease.Translate,
            sideways ? TranslateTransform.XProperty : TranslateTransform.YProperty,
            0, reach, 2500, PossAnim.Sine);
        return Task.CompletedTask;
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        var lease = Lease;
        if (lease == null) return;

        double x = 0, y = 0;
        try { x = lease.Translate.X; y = lease.Translate.Y; } catch { }
        PossAnim.Settle(lease.Translate, TranslateTransform.XProperty, x);
        PossAnim.Settle(lease.Translate, TranslateTransform.YProperty, y);

        double ms = UndoMs(duration, 250, 700);
        if (ms <= 0) return;
        PossAnim.To(lease.Translate, TranslateTransform.XProperty, 0, ms, PossAnim.EaseInOut);
        PossAnim.To(lease.Translate, TranslateTransform.YProperty, 0, ms, PossAnim.EaseInOut);
        await PossAnim.DelayAsync(ms + 20, CancellationToken.None).ConfigureAwait(true);
    }
}
