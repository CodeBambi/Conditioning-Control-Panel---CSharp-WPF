using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R4 "glitchportrait" (Full Doki only) - for 200 ms the companion herself tears. Three horizontal
/// bands of the portrait slip a few pixels sideways under an ember tint, and then she is fine again.
///
/// <para>It is the smallest R4 effect on purpose. Everything else at "It knows" happens to the ROOM -
/// the title stops naming a feature, a dialog claims to be deleting things, the tube goes empty. This
/// one happens to the WARDEN, which is why it is over before you can be sure you saw it: a companion
/// who visibly glitches for a whole second is a broken asset, a companion who glitches for a fifth of
/// one is a companion who slipped.</para>
///
/// <para>UsesFlicker is true, so the director skips it outright under photosafe (POSSESSION.md: no
/// blinks, no strobes) - there is no reduced-motion variant, because a static torn portrait is not a
/// glitch, it is a rendering bug.</para>
///
/// <para>IsBig is false: the warden cannot name a thing that happened to the warden without breaking
/// the joke, and the tell is already in front of you (the ember tint on the bands is the charge).
/// AvatarTubeWindow.GlitchPortrait draws into its own overlay layer, so the real emote / pose /
/// crossfade pipeline is never written to and there is nothing that can survive the lockdown.</para>
/// </summary>
public sealed class GlitchPortraitEffect : PossessionEffectBase
{
    /// <summary>Long enough to register, short enough to doubt.</summary>
    private const int GlitchMs = 200;

    public override string Id => "glitchportrait";
    public override PossessionRung MinRung => PossessionRung.ItKnows;
    public override PossessionIntensity MinIntensity => PossessionIntensity.FullDoki;
    public override bool IsBig => false;
    public override bool UsesFlicker => true;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// No charge ripple: the victim is in another window entirely (the tube is its own layered window,
    /// often on its own thread), so the host GhostLayer cannot draw over it. The ember tint ON the torn
    /// bands is the attribution instead - same colour, same source, same one-second test.
    /// </summary>
    protected override bool ChargeOnApply => false;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        try
        {
            if (ctx.Photosafe) return false;              // belt: the director already filters UsesFlicker
            var tube = App.AvatarWindow;
            if (tube == null) return false;
            // Deliberately NOT tube.IsVisible: with AvatarOwnThread on, the tube lives on its own STA
            // thread and reading one of its dependency properties from here throws. TryGetTubeScreenRect
            // asks the HWND instead, which is thread-agnostic, and an off-screen / zero-size rect is
            // exactly the case we want to skip anyway.
            if (!tube.TryGetTubeScreenRect(out var rect)) return false;
            return rect.Width > 8 && rect.Height > 8;
        }
        catch { return false; }
    }

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        try { App.AvatarWindow?.GlitchPortrait(GlitchMs); }
        catch (Exception ex) { App.Logger?.Warning("Possession glitchportrait failed: {Error}", ex.Message); }
        return Task.CompletedTask;
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        // The tube takes its own glitch down on a timer; this is the belt for the case where the
        // lockdown ended inside those 200 ms and that timer never got to tick.
        try { App.AvatarWindow?.ClearGlitchPortrait(); }
        catch (Exception ex) { App.Logger?.Warning("Possession glitchportrait restore failed: {Error}", ex.Message); }
        return Task.CompletedTask;
    }
}
