namespace CcpClient.Desktop.Haptics;

/// <summary>
/// <b>The haptic limb: the five MOMENTS a ported effect module can tell the haptic sink about.</b>
///
/// <para>SP-126. Upstream drives haptics from eighteen command sites in the
/// <c>Services/{Flash,Video,Subliminal}</c> family; ten of them have a port trigger point and those
/// ten reach exactly FIVE statements in this build (<c>client/docs/haptic-limb-census.md</c> §3.1).
/// This interface is those five statements' vocabulary and nothing else.</para>
///
/// <para><b>Every verb names a MOMENT, never a level, a duration, a shape or a priority.</b> That is
/// the whole reason a module may hold one: a module that passed an intensity would be deciding
/// haptic policy from inside a drawing module, which is how upstream ended up with the same 0.06
/// floor and the same 0.70 cap re-derived at four call sites. The envelope each moment carries lives
/// in <see cref="HapticEnvelopes"/>, beside the upstream line it is ported from, and the arbitration
/// between overlapping envelopes lives in <see cref="HapticLimb"/>.</para>
///
/// <para><b>A module with no limb is ABSENT, not silent.</b> There is deliberately no no-op
/// implementation of this interface: consumers hold <c>IHapticLimb?</c> and call through <c>?.</c>,
/// so "this build has no limb here" and "the limb decided nothing should happen" stay different
/// facts. A no-op double is exactly the shape that lets a wiring regression pass every test.</para>
///
/// <para><b>NOTHING MOVES, and that is correct.</b> No provider client is admitted
/// (<see cref="HapticSinkFactory.AdmittedRoutes"/> is empty), so the sink behind this refuses every
/// call and there is no device key to address. These verbs command; whether anything is delivered is
/// the sink's business and today the honest answer is no.</para>
/// </summary>
public interface IHapticLimb
{
    /// <summary>
    /// Census sites 1, 2 and 3 — <b>one flash image has been placed on screen</b>
    /// (<c>Services/Flash/FlashService.cs:1453</c>, <c>:1480</c>, <c>:1516</c>, all three of
    /// <c>SpawnFlashWindow</c>'s mutually exclusive spawn arms calling
    /// <c>FlashDecayVibeAsync()</c>).
    ///
    /// <para>Once per PLACED IMAGE, not once per flash: upstream fires inside the per-window spawn,
    /// and each call REPLACES any decay already running (<c>HapticService.cs:774-776</c>) so a
    /// ladder cannot stack on itself. A flash of three images therefore restarts the ladder twice
    /// and decays fully from the last one.</para>
    /// </summary>
    void FlashPlaced();

    /// <summary>
    /// Census sites 6 and 7 — <b>a clip is really playing</b>
    /// (<c>Services/Video/VideoService.cs:2580</c> on the LibVLC engine and
    /// <c>Services/Video/VideoService.Browser.cs:452</c> on the default one; this port has one video
    /// path, so both engines' starts are one moment here).
    ///
    /// <para>Upstream fires this immediately under its own <i>"Playback is REAL from here"</i>
    /// (<c>VideoService.cs:2567-2576</c>), so a caller must only report the moment once the surface
    /// has confirmed it — never on the strength of having asked.</para>
    /// </summary>
    void VideoStarted();

    /// <summary>
    /// Census site 12 — <b>the clip is off the screen</b>
    /// (<c>Services/Video/VideoService.cs:6580</c>).
    ///
    /// <para><b>Call this on EVERY path that takes a clip down, and that is a deliberate divergence
    /// from upstream</b> (D203): upstream's own stop is reached only through <c>Cleanup()</c>, while
    /// a comment three lines below it claims it <i>"Runs on every teardown path (natural end, skip,
    /// panic, attention retry)"</i> — and <c>ForceCleanup</c>, the panic-key path, carries no haptic
    /// reference at all. The start passes no auto-zero, so the layer latches unbounded: panic-key a
    /// clip upstream and the toy keeps humming.</para>
    /// </summary>
    void VideoStopped();

    /// <summary>
    /// Census sites 14 and 15 — <b>a subliminal card is on screen</b>
    /// (<c>Services/Subliminal/SubliminalService.cs:230</c>, the with-whisper branch, and
    /// <c>:588</c>, the silent one; this port has no whisper audio, so the two are one moment).
    /// </summary>
    /// <param name="phrase">The card's own words. They are the input to the pulse's duration
    /// (<c>HapticService.cs:899-909</c>), which is why the text travels rather than a number.</param>
    void SubliminalShown(string phrase);

    /// <summary>
    /// Census site 18 — <b>a bouncing word has hit a screen edge</b>
    /// (<c>Services/Subliminal/BouncingTextService.cs:516</c>), in a module this port shipped at
    /// SP-115.
    ///
    /// <para>Upstream fires between the bounce bookkeeping and the 10 % text re-roll (<c>:516</c>
    /// against <c>:519</c>), and this port's trigger point sits in the same place in the same
    /// sequence.</para>
    /// </summary>
    void BounceHit();
}
