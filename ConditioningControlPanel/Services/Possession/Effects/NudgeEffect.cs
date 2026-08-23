using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R0 "nudge" - the smallest tic on the ladder: the control jumps a few pixels, overshoots, settles
/// crooked for a breath, then eases back. Deniable in WHAT (did that move?), never in WHO (the ember
/// charge fired first).
///
/// <para>Wave 2 floor: the original was 1-2 px with no overshoot, which on a 1585x901 design canvas
/// scaled into a fractional window pixel - literally invisible on the owner's first live run. A tic
/// that cannot be seen is not deniable, it is absent. 3-4 px with a 35% overshoot is still small
/// enough to doubt and large enough to catch.</para>
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

        double dx = Amp(Rand(3.0, 4.5)) * Sign();
        double dy = Amp(Rand(2.0, 3.5)) * Sign();

        // Overshoot then settle: a straight ease-out reads as a layout pass, a snap-back-past reads as
        // something SHOVED it. Same distance, completely different author.
        PossAnim.Pulse(lease.Translate, TranslateTransform.XProperty, dx * 1.35, 130, 190, 0, dx);
        PossAnim.Pulse(lease.Translate, TranslateTransform.YProperty, dy * 1.35, 130, 190, 0, dy);
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
