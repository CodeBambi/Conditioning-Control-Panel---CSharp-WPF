using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R3 "crack" - the room itself settles. No victim, no ghost: an ember pulse around the window edge and
/// a short, low shake, like something heavy shifted in the next room. The edge pulse IS the attribution
/// here (same ember, same source), so this effect carries no separate charge ripple.
/// Photosafe: the pulse alone, no shake.
/// </summary>
public sealed class CrackEffect : PossessionEffectBase
{
    public override string Id => "crack";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => false;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(1);

    /// <summary>No target to charge over: the ember EdgePulse is the tell.</summary>
    protected override bool ChargeOnApply => false;

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        try
        {
            ctx.Attribution.EdgePulse(0.4);
            if (!Photosafe) App.ScreenShake?.Shake(0.25, 180);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession crack failed: {Error}", ex.Message); }
        return Task.CompletedTask;
    }

    protected override Task UndoCoreAsync(TimeSpan duration) => Task.CompletedTask;
}
