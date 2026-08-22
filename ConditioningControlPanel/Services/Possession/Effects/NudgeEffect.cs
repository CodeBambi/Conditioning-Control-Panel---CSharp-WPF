using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R0 "nudge" - the smallest tic on the ladder. One or two pixels, held for a breath, eased back.
/// Deniable in WHAT (did that move?), never in WHO (the ember charge fired first).
/// </summary>
public sealed class NudgeEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles =
        { PossessionRole.Label, PossessionRole.Button, PossessionRole.Toggle };

    public override string Id => "nudge";
    public override PossessionRung MinRung => PossessionRung.Settle;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => false;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(2);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        var lease = TakeLease();
        if (lease == null) return Task.CompletedTask;

        double dx = Amp(Rand(1.0, 2.0)) * Sign();
        double dy = Amp(Rand(0.6, 1.6)) * Sign();

        PossAnim.To(lease.Translate, TranslateTransform.XProperty, dx, 320, PossAnim.EaseOut);
        PossAnim.To(lease.Translate, TranslateTransform.YProperty, dy, 320, PossAnim.EaseOut);
        return Task.CompletedTask;
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        var lease = Lease;
        if (lease == null) return;

        double ms = UndoMs(duration, 220, 600);
        if (ms <= 0) return;
        PossAnim.To(lease.Translate, TranslateTransform.XProperty, 0, ms, PossAnim.EaseInOut);
        PossAnim.To(lease.Translate, TranslateTransform.YProperty, 0, ms, PossAnim.EaseInOut);
        await PossAnim.DelayAsync(ms + 20, CancellationToken.None).ConfigureAwait(true);
    }
}
