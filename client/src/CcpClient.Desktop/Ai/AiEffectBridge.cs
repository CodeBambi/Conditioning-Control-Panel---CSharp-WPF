using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Ai;

/// <summary>
/// The bridge between the c6 command executor and the LANDED effect rack — the thing that was
/// missing when <see cref="AiCommandExecutor"/> was constructed with an empty handler map.
///
/// <para><b>The registration rule, and it is the whole design.</b> A kind gets a handler only when
/// this build can honour EVERY field of its command data. A kind that could be half-applied gets
/// no handler and a NAMED ABSENCE instead, so the executor answers
/// <see cref="AiNotExecutedReason.EffectUnavailable"/> and the surface can say which effect this
/// build does not have. Upstream's dispatcher has the opposite failure mode — a gated or
/// unbuildable command is dropped with a log line and the user sees nothing
/// (<c>Services/Commands/AiCommandService.cs:44,:51</c>, <c>CommandFactory.cs:37</c> returns null
/// and <c>AiCommandService.cs:82-85</c> simply does not execute) — which is the defect this bridge
/// exists to not reproduce.</para>
///
/// <para><b>Why three and not eleven.</b> The rack's modules are driven by persisted DIALS plus
/// arm/disarm (<see cref="ISessionEffect"/>); upstream's commands that this build can express are
/// exactly the dial-shaped ones. The eight absences below each name the seam that is missing,
/// measured against the module's real public surface, and none of them is a guess: every one was
/// checked against the module that would have to carry it.</para>
/// </summary>
public static class AiEffectBridge
{
    /// <summary>
    /// Why a command kind has no handler in this build. Stable codes; the detail is what a user
    /// reads on the permissions grid and what a bug report quotes (the
    /// <see cref="EffectReasonCodes"/> convention).
    /// </summary>
    public static readonly IReadOnlyDictionary<AiCommandKind, CapabilityReason> Absences =
        new Dictionary<AiCommandKind, CapabilityReason>
        {
            [AiCommandKind.FlashImage] = new(
                "ai-effect-no-one-shot-flash",
                "flash_image asks for a burst of N images for D seconds at a given size "
                + "(WPF FlashImageCommand.cs:30 -> FlashService.TriggerFlashOnce). This build's "
                + "Flash Images module is a paced schedule with an on/off dial and no one-shot "
                + "entry point, so the amount, the duration and the size have nowhere to land"),
            [AiCommandKind.Subliminal] = new(
                "ai-effect-no-caller-supplied-phrase",
                "subliminal carries the text she chose (WPF SubliminalCommand.cs:27 -> "
                + "FlashSubliminalCustom). This build's Subliminals module draws from the user's "
                + "own phrase pool and has no seam for a caller-supplied phrase, so her words "
                + "would be silently replaced by the user's"),
            [AiCommandKind.MantraLockscreen] = new(
                "ai-effect-no-caller-supplied-phrase",
                "mantra_lockscreen carries a mantra and a repeat count (WPF "
                + "MantraLockScreenCommand.cs:27 -> ShowLockCard(phrase, amount, strict)). This "
                + "build's Lock Card module draws from the user's own phrase pool on a schedule "
                + "and has no show-this-now entry point"),
            [AiCommandKind.Bounce] = new(
                "ai-effect-no-caller-supplied-phrase",
                "bounce may carry words (WPF BounceCommand.cs:20 -> Start(true, words)). This "
                + "build's Bouncing Text module draws the user's own configured phrases and "
                + "exposes no phrase setter, so a bounce command could only ever half-apply"),
            [AiCommandKind.Haptic] = new(
                "ai-effect-no-haptic-route",
                "haptic drives a toy (WPF HapticCommand.cs:24 -> ApplyVibrationModeAsync). The "
                + "haptic sink in this build is not an effect module and nothing here has ever "
                + "driven a real device, so no AI command may reach it"),
            [AiCommandKind.Video] = new(
                "ai-effect-no-caller-supplied-media",
                "video names a clip or asks for a random one (WPF MediaCommand). This build's "
                + "Mandatory Video module plays from the user's own folder on a schedule and has "
                + "no play-this-file entry point"),
            [AiCommandKind.Audio] = new(
                "ai-effect-no-caller-supplied-media",
                "audio names a file or asks for a random one (WPF MediaCommand). This build has "
                + "no audio module an AI command could name a file to"),
            [AiCommandKind.GetBackToMe] = new(
                "ai-effect-no-scheduled-followup",
                "getbacktome schedules the companion to speak again after a delay (WPF "
                + "GetBackToMeCommand). Nothing in this build schedules an AI operation, so the "
                + "follow-up would never arrive"),
        };

    /// <summary>
    /// The handler map for a rack. Kinds absent from the result are exactly
    /// <see cref="Absences"/>'s keys, which <c>AiEffectBridgeTests</c> pins in both directions —
    /// a kind may never be silently missing from both.
    /// </summary>
    /// <param name="effects">The session's modules, as
    /// <see cref="SessionEngine.Effects"/> hands them out. A rack missing a module simply yields
    /// no handler for its kinds: composition never invents a module.</param>
    public static IReadOnlyDictionary<AiCommandKind, IAiEffectHandler> HandlersFor(
        IReadOnlyList<ISessionEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        var handlers = new Dictionary<AiCommandKind, IAiEffectHandler>();

        if (Find<SpiralOverlayEffect>(effects, SpiralOverlayEffect.EffectId) is { } spiral)
        {
            handlers[AiCommandKind.Spiral] = new OverlayHandler(spiral, spiral.SetOpacityPercent);
        }

        if (Find<PinkFilterEffect>(effects, PinkFilterEffect.EffectId) is { } pink)
        {
            handlers[AiCommandKind.Pink] = new OverlayHandler(pink, pink.SetOpacityPercent);
        }

        if (Find<BubblePopEffect>(effects, BubblePopEffect.EffectId) is { } bubbles)
        {
            handlers[AiCommandKind.Bubbles] = new BubblesHandler(bubbles);
        }

        return handlers;
    }

    private static T? Find<T>(IReadOnlyList<ISessionEffect> effects, string id)
        where T : class, ISessionEffect =>
        effects.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal)) as T;

    /// <summary>
    /// Turn a module on with the dials the command carries, or take it back down. Upstream's own
    /// shape for both overlays: write the opacity, write the enable flag, and START the overlay if
    /// it was not already up (<c>SpiralCommand.cs:26-39</c>, <c>PinkCommand.cs:26-39</c> — the
    /// identical body twice). <see cref="OwnedSessionEffect.Arm"/> is this port's counterpart of
    /// that start, and it is idempotent about the generation, so an AI spiral during a running
    /// session re-applies rather than starting a second one.
    ///
    /// <para>The OFF path is <see cref="OwnedSessionEffect.Refresh"/>, not
    /// <see cref="OwnedSessionEffect.Disarm"/>, and that is upstream's behaviour rather than a
    /// convenience: <c>SpiralCommand</c> clears the flag and calls <c>RefreshOverlays()</c>, which
    /// takes the LAYER down and leaves the overlay service running (<c>OverlayService.cs:451-483</c>).
    /// Disarming would cancel the module's generation, which is what STOP means here.</para>
    /// </summary>
    private sealed class OverlayHandler(OwnedSessionEffect effect, Action<int> setOpacityPercent) : IAiEffectHandler
    {
        public void Execute(AiCommand command)
        {
            var data = (AiCommandData.Overlay)command.Data;

            // The envelope already rejected anything outside 0-30 (contract §8 rule 5: a bound is
            // a rejection, never a silent clamp), which is upstream's SpiralCommand.MaxIntensity /
            // PinkCommand.MaxIntensity = 30 expressed one layer earlier. The module's own dial
            // clamp (0-100 spiral, 0-50 pink) is therefore never the thing that moves the value.
            setOpacityPercent(data.Intensity);
            effect.SetEnabled(data.On);
            if (data.On)
            {
                effect.Arm();
            }
            else
            {
                effect.Refresh();
            }
        }
    }

    /// <summary>
    /// Bubbles, with upstream's tolerant intent reading kept verbatim in meaning
    /// (<c>BubbleCommand.cs:20-23</c>): a frequency above zero MEANS start even when the model
    /// forgot <c>on</c>, and a frequency of zero with <c>on:false</c> means stop. A zero frequency
    /// leaves the user's own spawn rate alone — upstream passes <c>null</c> there rather than zero
    /// (<c>:32</c>), and this build's rate dial has a floor of 1/min
    /// (<c>Effects/BubblePopField.cs:123</c>), so writing the zero would be a silent invention.
    /// </summary>
    private sealed class BubblesHandler(BubblePopEffect effect) : IAiEffectHandler
    {
        public void Execute(AiCommand command)
        {
            var data = (AiCommandData.Bubbles)command.Data;
            var start = data.On || data.FrequencyPerMinute > 0;
            if (data.FrequencyPerMinute > 0)
            {
                effect.SetPerMinute(data.FrequencyPerMinute);
            }

            effect.SetEnabled(start);
            if (start)
            {
                effect.Arm();
            }
            else
            {
                effect.Refresh();
            }
        }
    }
}
