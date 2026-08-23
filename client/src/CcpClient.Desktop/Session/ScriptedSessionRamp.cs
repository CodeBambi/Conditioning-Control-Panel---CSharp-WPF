using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Session;

/// <summary>
/// The values a scripted session moves CONTINUOUSLY while it runs — upstream's
/// <c>UpdateRampingValues</c> (<c>Services/Session/SessionEngine.cs:564-661</c>), arithmetic for
/// arithmetic, as a pure function of the clock.
///
/// <para><b>This is the second caller <see cref="RampCurves"/> was written for.</b> That class
/// already says so in as many words (<c>Effects/RampCurves.cs:52-56</c>): upstream shares one curve
/// helper between the manual Intensity Ramp and the scripted session's own ramps, and until this
/// slice the port had only the first. The curve is resolved once per tick and shapes EVERY ramp
/// below, which is upstream's <c>settings.RampCurve ?? App.Settings.Current.RampCurve</c>
/// (<c>:569</c>).</para>
///
/// <para><b>Why these numbers are NOT written into the user's dials.</b> Upstream parks them over
/// the user's values instead — the flash trio through
/// <c>AppSettings.SetSessionFlashRamp</c> (<c>Models/AppSettings.cs:908-913</c>, cleared by
/// <c>ClearSessionFlashRamp</c> <c>:916</c>), the two overlay opacities by driving the overlay
/// service directly (<c>Services/Session/SessionEngine.cs:604-618</c>). The reason is in upstream's
/// own comment at <c>:604-610</c>: a ramped value written into the persisted setting auto-saved to
/// disk, so an app kill mid-session froze the ramp's MAXIMUM into settings.json permanently — "the
/// screen keeps getting more pink and stays that way" (upstream issues #471, #476) — and
/// <c>RestoreSettings</c> only heals a CLEAN stop. The override is ephemeral by design and
/// deliberately silent (<c>Models/AppSettings.cs:905</c>: no change notification, so a running
/// session does not drag the user's sliders around mid-ramp).</para>
///
/// <para><b>Who reads it here.</b> <see cref="ScriptedSessionRun.Ramp"/> publishes it and nothing
/// consumes it yet — a pull, exactly as upstream's getters are a pull. The port's modules read
/// their dials when they ARM and it has no sustained-overlay hold to park a live value in
/// (<see cref="PinkFilterEffect"/> records <c>Services/Notifications/OverlayService.cs:900-965</c>
/// as unported), so a module that follows a live ramp is the next slice's work and is named here
/// rather than faked with a dial write, which would BE the #471 defect.</para>
/// </summary>
/// <param name="FlashOpacityPercent">The flash images' ramped opacity, or null when this session
/// does not ramp it (<c>:583-587</c>). Clamped as upstream clamps it at the sink
/// (<c>Models/AppSettings.cs:910</c>).</param>
/// <param name="FlashesPerHour">The flash frequency's ramped value, or null when the session does
/// not ramp it (<c>:590-593</c>, clamp <c>AppSettings.cs:911</c>).</param>
/// <param name="FlashScalePercent">The session's FIXED flash scale, or null when it leaves the
/// user's alone (<c>:596-599</c>, clamp <c>AppSettings.cs:912</c>). <b>Pinned, never lerped</b> —
/// see <see cref="Compute"/>.</param>
/// <param name="PinkOpacityPercent">The pink filter's ramped opacity in percent, or null before its
/// start minute (<c>:611-618</c>). A double, un-truncated, because upstream hands the overlay
/// <c>_currentPinkOpacity / 100.0</c> without rounding it (<c>:617</c>).</param>
/// <param name="SpiralOpacityPercent">The spiral's ramped opacity in percent, or null before its
/// start minute or when the session does not ramp it at all (<c>:625-633</c>).</param>
public readonly record struct ScriptedSessionRamp(
    int? FlashOpacityPercent,
    int? FlashesPerHour,
    int? FlashScalePercent,
    double? PinkOpacityPercent,
    double? SpiralOpacityPercent)
{
    /// <summary>Upstream's flash-scale "the session sets one" test: 100 means the session leaves
    /// the user's scale alone (<c>Services/Session/SessionEngine.cs:596</c>).</summary>
    public const int UnsetFlashScalePercent = 100;

    /// <summary>Upstream's clamp on a parked flash scale (<c>Models/AppSettings.cs:912</c>, the
    /// <c>ImageScale</c> setter's own range at <c>:875</c>). The port has no flash scale dial —
    /// <see cref="SessionPresetDocument"/> deliberately persists no draw dials — so the range is
    /// carried here with its citation rather than read off a document.</summary>
    public const int MinFlashScalePercent = 50;

    /// <inheritdoc cref="MinFlashScalePercent"/>
    public const int MaxFlashScalePercent = 250;

    /// <summary>How many minutes of a bubble ramp make one step (<c>:641</c>).</summary>
    public const double BubbleRampStepMinutes = 5;

    /// <summary>The jitter upstream puts on a delayed start, in minutes either way
    /// (<c>:784</c>, <c>:795</c>).</summary>
    public const double StartJitterMinutes = 3;

    /// <summary>Nothing parked: what a reader sees when no session is running, and what upstream's
    /// <c>ClearSessionFlashRamp</c> leaves behind (<c>Models/AppSettings.cs:916</c>).</summary>
    public static ScriptedSessionRamp None => default;

    /// <summary>True when this session is moving something right now.</summary>
    public bool Any =>
        FlashOpacityPercent.HasValue
        || FlashesPerHour.HasValue
        || FlashScalePercent.HasValue
        || PinkOpacityPercent.HasValue
        || SpiralOpacityPercent.HasValue;

    /// <summary>
    /// One tick's worth of ramping — upstream <c>UpdateRampingValues</c>
    /// (<c>Services/Session/SessionEngine.cs:564-633</c>).
    ///
    /// <para><b>Three things in here are load-bearing.</b></para>
    /// <list type="number">
    /// <item><b>The flash scale is PINNED, not lerped</b> (<c>:596-599</c>): when the session sets
    /// a scale other than 100 that scale applies for the whole session and never moves. It is the
    /// third value pushed through <c>SetSessionFlashRamp</c> (<c>:601</c>) — so it IS read at
    /// runtime, unlike <c>flashSmallSize</c>, which really is written by the definitions and read
    /// by nothing.</item>
    /// <item><b>The pink ramp's progress is measured over what is LEFT</b> (<c>:613-614</c>):
    /// <c>(elapsed - start) / (total - start)</c>, not over the whole session. A pink filter that
    /// begins at minute 10 of 30 reaches its end opacity at minute 30, not at minute 43.</item>
    /// <item><b>The spiral ramps only when it really ramps</b> (<c>:625</c>): a session whose
    /// spiral start and end opacities are equal is left alone, which is upstream's own #897 fix —
    /// driving the overlay with a constant parked a ramp hold that froze the user's slider for the
    /// whole session.</item>
    /// </list>
    ///
    /// <para><b>Truncation, not rounding</b> (<c>:586</c>, <c>:592</c>): upstream casts the lerped
    /// flash values to <c>int</c>, so 62.5 % is 62 %. Kept, because the cast is what a user's flash
    /// window is drawn with.</para>
    ///
    /// <para>The denominators cannot be zero for a running session: the tick that would reach
    /// <c>elapsed == total</c> ends the session first (<c>:512-517</c>,
    /// <see cref="ScriptedSessionRun.Tick"/>), and a start minute at or past the duration is never
    /// reached at all.</para>
    /// </summary>
    /// <param name="settings">The session's dials.</param>
    /// <param name="elapsedMinutes">Minutes served, from the run's guarded clock reading.</param>
    /// <param name="totalMinutes">The session's duration.</param>
    /// <param name="curve">The resolved easing curve (<c>:569</c>).</param>
    /// <param name="pinkStartMinute">The pink filter's start minute AFTER the jitter
    /// (<see cref="JitterStartMinute"/>), which is what upstream compares against
    /// (<c>:611</c>).</param>
    /// <param name="spiralStartMinute">The spiral's start minute after the jitter
    /// (<c>:626</c>).</param>
    public static ScriptedSessionRamp Compute(
        ScriptedSessionSettings settings,
        double elapsedMinutes,
        double totalMinutes,
        RampCurve curve,
        double pinkStartMinute,
        double spiralStartMinute)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var progress = RampCurves.ApplyCurve(elapsedMinutes / totalMinutes, curve);

        int? flashOpacity = null;
        int? flashFrequency = null;
        int? flashScale = null;

        // Flash opacity ramp (:583-587).
        if (settings.FlashEnabled && settings.FlashOpacity != settings.FlashOpacityEnd)
        {
            flashOpacity = Math.Clamp(
                (int)Lerp(settings.FlashOpacity, settings.FlashOpacityEnd, progress),
                VisualsPresetDocument.MinFlashOpacityPercent,
                VisualsPresetDocument.MaxFlashOpacityPercent);
        }

        // Flash frequency ramp (:590-593) — Good Girls Don't Cum ramps 180/hour to 600/hour, and
        // the clamp is what makes that land at the port's and upstream's shared ceiling of 180
        // rather than at 600.
        if (settings.FlashEnabled && settings.FlashPerHour != settings.FlashPerHourEnd)
        {
            flashFrequency = Math.Clamp(
                (int)Lerp(settings.FlashPerHour, settings.FlashPerHourEnd, progress),
                SessionPresetDocument.MinFlashesPerHour,
                SessionPresetDocument.MaxFlashesPerHour);
        }

        // Flash scale (:596-599): pinned for the whole session, never lerped.
        if (settings.FlashEnabled && settings.FlashScale != UnsetFlashScalePercent)
        {
            flashScale = Math.Clamp(settings.FlashScale, MinFlashScalePercent, MaxFlashScalePercent);
        }

        // Pink filter ramp, only after its start minute (:611-618).
        double? pinkOpacity = null;
        if (settings.PinkFilterEnabled && elapsedMinutes >= pinkStartMinute)
        {
            var pinkProgress = RampCurves.ApplyCurve(
                (elapsedMinutes - pinkStartMinute) / (totalMinutes - pinkStartMinute), curve);
            pinkOpacity = Lerp(settings.PinkFilterStartOpacity, settings.PinkFilterEndOpacity, pinkProgress);
        }

        // Spiral ramp, only after its start minute AND only when it really ramps (:625-633).
        double? spiralOpacity = null;
        if (settings.SpiralEnabled
            && settings.SpiralOpacity != settings.SpiralOpacityEnd
            && elapsedMinutes >= spiralStartMinute)
        {
            var spiralProgress = RampCurves.ApplyCurve(
                (elapsedMinutes - spiralStartMinute) / (totalMinutes - spiralStartMinute), curve);
            spiralOpacity = Lerp(settings.SpiralOpacity, settings.SpiralOpacityEnd, spiralProgress);
        }

        return new ScriptedSessionRamp(
            flashOpacity, flashFrequency, flashScale, pinkOpacity, spiralOpacity);
    }

    /// <summary>
    /// The bubble frequency this session should be spawning at, or null when it is not ramping the
    /// bubbles — upstream <c>:635-650</c>.
    ///
    /// <para><b>This one really does move the user's dial</b>, where every value above is parked
    /// beside it: upstream writes <c>App.Settings.Current.BubblesFrequency</c> and calls
    /// <c>RefreshFrequency()</c> (<c>:646-647</c>). That asymmetry is upstream's, not a port
    /// decision — and the snapshot taken at START is what gives the user's own rate back at the end
    /// (<see cref="ScriptedSessionDials.Restore"/>).</para>
    ///
    /// <para>The climb is a STEP, not a lerp, and the curve does not touch it: one extra bubble a
    /// minute per whole five minutes since the bubbles' own start minute (<c>:640-642</c>). The
    /// ramp is skipped entirely for an intermittent session, whose bursts are scheduled instead
    /// (<c>:636</c>), and for a session whose bubbles start at minute 0.</para>
    /// </summary>
    public static int? BubblesPerMinute(ScriptedSessionSettings settings, double elapsedMinutes)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.BubblesEnabled
            || settings.BubblesIntermittent
            || settings.BubblesStartMinute <= 0
            || elapsedMinutes < settings.BubblesStartMinute)
        {
            return null;
        }

        var steps = (int)((elapsedMinutes - settings.BubblesStartMinute) / BubbleRampStepMinutes);
        return settings.BubblesFrequency + steps;
    }

    /// <summary>
    /// A delayed start, jittered — upstream <c>RandomizeStartTimes</c>
    /// (<c>Services/Session/SessionEngine.cs:777-805</c>): ±3 minutes, floored at zero, and applied
    /// ONLY when the feature is enabled AND its start minute is greater than zero
    /// (<c>:782</c>, <c>:793</c>). A feature that starts with the session keeps minute 0 exactly,
    /// which is what lets <see cref="ScriptedSessionDials.Apply"/> decide "immediate" from the
    /// unjittered value and still agree with this.
    ///
    /// <para>The randomness is INJECTED for the reason the clock is: a start time nothing can pin
    /// is a start time no fact can check. This is the port's established shape for a module that
    /// needs randomness (<c>Effects/AudioCueEffect.cs:86</c>,
    /// <c>Effects/BubbleCountEffect.cs:168</c>).</para>
    /// </summary>
    public static double JitterStartMinute(bool enabled, int startMinute, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!enabled || startMinute <= 0)
        {
            return startMinute;
        }

        var offset = (random.NextDouble() * (StartJitterMinutes * 2)) - StartJitterMinutes;
        return Math.Max(0, startMinute + offset);
    }

    /// <summary>Upstream's <c>Lerp</c> (<c>Services/Session/SessionEngine.cs:1956-1958</c>), whose
    /// clamp on <paramref name="t"/> is what keeps every ramp inside its two endpoints.</summary>
    private static double Lerp(double a, double b, double t) => a + ((b - a) * Math.Clamp(t, 0, 1));
}
