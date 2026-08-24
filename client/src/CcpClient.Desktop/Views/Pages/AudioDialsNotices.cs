using CcpClient.Desktop.Audio;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The AUDIO row's sentences — the app-wide volumes, the output endpoint and the Test button —
/// in their own file for the reason <see cref="AudioPanelNotices"/> is in its:
/// <c>StudioPage.axaml.cs</c> carries every landed module's rendered claims and does not scale.
///
/// <para><b>This row is not a module and its notices are a different KIND of sentence.</b> Every
/// other panel on the rack reports what a module DID — a flash was placed, a cue played, a card is
/// up. This one reports what the OPERATING SYSTEM answered when this app asked for an endpoint, and
/// what a number on this page actually reaches. Both halves matter here more than anywhere else in
/// the rack, because <b>a volume slider is the easiest control in any application to ship dead</b>:
/// it moves, it persists, it looks exactly like a working one, and nothing about the picture
/// distinguishes "quieter" from "connected to nothing".</para>
///
/// <para><b>So each dial's sentence names its reader, or says it has none.</b>
/// <see cref="DescribeMaster"/> names the one thing in this build that reads master volume and says
/// WHEN it reads it; <see cref="DescribeVideo"/> says plainly that nothing reads video volume yet.
/// A user is entitled to know which of the two they are moving before they wonder why the room did
/// not get quieter.</para>
///
/// <para><b>And nothing here claims a sound was heard.</b> The strongest sentence in this file says
/// the operating system accepted a cue on this app's own stream. Whether a speaker was on, plugged
/// in, muted at the endpoint or held in exclusive mode by something else is outside this process
/// and is not asserted (<c>client/docs/verification-harness.md</c>, audio evidence class).</para>
/// </summary>
public static class AudioDialsNotices
{
    /// <summary>Entry 0 of the picker, and upstream's own label for it
    /// (<c>MainWindow/MainWindow.UiUpdates.cs:1124</c>, <i>"index 0 = System default"</i>; the stored
    /// meaning is the empty string, <c>Models/AppSettings.cs:1238-1240</c>).</summary>
    public const string SystemDefaultLabel = "System default";

    /// <summary>The test cue's gain, and it is FIXED rather than scaled by
    /// <see cref="AudioSettingsDocument.MasterVolume"/> — upstream's own decision, in upstream's own
    /// words: <c>_soundFile.Volume = 0.5f; // Fixed 50% for test — bypasses curve</c>
    /// (<c>Services/AudioService.cs:625</c>). It is the whole point of a diagnostic: a user whose
    /// master is at 0 still gets to find out whether the endpoint works.</summary>
    public const float TestGain = 0.5f;

    /// <summary>What the row is, said before any of it is touched.</summary>
    public static string DescribeWhatItIs() =>
        "How loud this app is, and which output it plays through. These are app-wide settings, not "
        + "session ones: a scripted session never borrows them.";

    /// <summary>
    /// The MASTER volume's reader, named. In this build there is exactly one
    /// (<c>Features/Dtrh/DtrhHostWindow.axaml.cs:268</c> hands it to
    /// <c>Companion/BarkPipeline.cs:613</c>, which composes it as
    /// <c>pow(master/100, 1.5) * scale</c> — upstream's own curve law, applied at the play site
    /// rather than by a mixer, <c>Services/AudioService.cs:643</c>), and it is read WHEN THE WINDOW
    /// IS BUILT rather than continuously. Saying so is the difference between a user believing a
    /// slider is broken and knowing it applies next time.
    ///
    /// <para>Zero is called out because upstream treats it as a real state rather than an unset one
    /// (<c>AudioService.cs:535-536</c>, <i>"ALL audio will be silent"</i>;
    /// <c>BarkPipeline.cs:365</c> surfaces the text with no voice).</para>
    /// </summary>
    public static string DescribeMaster(int master) =>
        master == 0
            ? "Master is 0%. That is a real setting, not an unset one: this app stays silent and the "
                + "companion still shows her lines as text."
            : $"Master is {master}%. One thing in this build reads it — the companion's spoken lines, "
                + "which take it when their window opens, so a change here reaches the next one rather "
                + "than the one already up.";

    /// <summary>
    /// The VIDEO volume's honest line. <b>Nothing in this build reads it</b>: the mandatory-video
    /// row deliberately plays no soundtrack, and its own document says so
    /// (<c>Session/MandatoryVideoPresetDocument.cs:17-19</c> — <i>"a volume slider over silence is
    /// the dead dial §9 D7 refuses"</i>).
    ///
    /// <para>The dial is still here because the SETTING is real and app-wide
    /// (<c>Models/AppSettings.cs:1134</c>), it is persisted with the other two, and a user who sets
    /// it now keeps it. What it must not do is imply it changes playback, so it says which it is.</para>
    /// </summary>
    public static string DescribeVideo(int video) =>
        $"Video is {video}%. It is stored and it is kept, but nothing in this build plays a video "
        + "soundtrack yet, so today it changes what the app remembers rather than what you hear.";

    /// <summary>
    /// Which endpoint this app is set to, and whether that choice is CONNECTED right now.
    ///
    /// <para><b>The stale-choice sentence is the one that matters</b>, and it is a divergence from
    /// upstream in the honest direction. Upstream silently selects entry 0 when the remembered
    /// device is absent from the fresh enumeration (<c>MainWindow.UiUpdates.cs:1117-1124</c>), so a
    /// user whose headset is unplugged sees "System default" and is never told their choice is still
    /// stored. This port keeps the choice, falls back exactly as upstream's audio service does
    /// (<c>Audio/SoundArbitration.cs:325-328</c>, WPF <c>AudioService.cs:292-293</c>) and SAYS
    /// both.</para>
    ///
    /// <para>The port stores a NAME and no id, which is why a missing name is a survivable state at
    /// all: a miniaudio device id is a process-lifetime native pointer and passing a stored one back
    /// to the driver is this build's F1 process-fatal crash class
    /// (<c>Audio/SoundFlowAudioBackend.cs</c> header).</para>
    /// </summary>
    /// <param name="chosen">The stored endpoint name, or null/empty for the system default.</param>
    /// <param name="connected">Whether a FRESH enumeration contains it —
    /// <b>null when no enumeration has been done yet</b>, which is not the same as "absent" and must
    /// not be rendered as one. Nothing is enumerated until this panel is opened.</param>
    public static string DescribeChoice(string? chosen, bool? connected)
    {
        if (string.IsNullOrEmpty(chosen))
        {
            return "Output: whatever Windows or your desktop calls the default. Pick a specific one "
                + "to send this app somewhere else — a private headset, say, while everything else "
                + "stays on the speakers.";
        }

        return connected switch
        {
            null => $"Output: '{chosen}', remembered from last time. Nothing has asked this machine "
                + "which endpoints it has yet.",
            true => $"Output: '{chosen}', and it is connected.",
            false => $"Output: '{chosen}' — remembered, but not in the list of endpoints this machine "
                + "reports right now. Sound falls back to the system default until it comes back, "
                + "and your choice is kept rather than quietly replaced.",
        };
    }

    /// <summary>
    /// What the operating system last answered about a device, and HOW MANY TIMES this launch asked.
    ///
    /// <para><b>A null outcome is "not asked", never "unavailable"</b>, and the two must not be
    /// collapsed (<c>Audio/AudioParticipant.DeviceOutcome</c>). A whole launch stays in that state
    /// if nothing plays, which is the participant's deliberate design rather than an accident: phase
    /// 3 opens no device, so starting this app does not seize a render endpoint from anything
    /// else.</para>
    ///
    /// <para><b>A FAILED answer is REMEMBERED and this line says so out loud.</b>
    /// <c>EnsureDevice</c> returns the first attempt's outcome rather than re-initialising per
    /// consumer, because retrying there would put a native device attempt on every window that opens
    /// while an endpoint is down. Recovery is the arbitration's own cooldown re-probe, scheduled by
    /// a PLAY attempt (WPF #779, <c>AudioService.cs:163-166</c>) — so the honest instruction is
    /// "press Test", not "restart".</para>
    /// </summary>
    public static string DescribeDeviceOutcome(SoundOutcome? outcome, int attempts)
    {
        var attemptsLine = attempts == 0
            ? " Nothing has brought a device up this launch."
            : $" Device attempts this launch: {attempts}.";

        return outcome switch
        {
            null => "Nothing has been asked of the operating system yet. Opening this panel lists "
                + "endpoints but starts nothing; choosing one, or pressing Test, is what brings a "
                + "device up." + attemptsLine,
            SoundOutcome.Ready ready => $"The operating system opened '{ready.DeviceName}' for this app."
                + attemptsLine,
            SoundOutcome.Unavailable unavailable => $"No audio output: {unavailable.Reason}."
                + attemptsLine + " A play attempt re-probes after the cooldown, so pressing Test is "
                + "what tries again.",
            SoundOutcome.Failed failed => $"The device would not open: {failed.Error}." + attemptsLine
                + " That answer is remembered rather than retried by every module that wants sound; "
                + "pressing Test re-probes it.",
            // The hierarchy is closed and Initialize returns only the three above, so this arm is
            // unreachable today. It prints the state rather than inventing a sentence, which is this
            // page's own convention for these switches (AudioPanelNotices.DescribeAudioCapability).
            _ => outcome.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    /// The Test button's report — the port of upstream's diagnostic MessageBox
    /// (<c>MainWindow/MainWindow.UiUpdates.cs:1065-1091</c> over
    /// <c>Services/AudioService.TestAudioPlayback</c>, <c>:553-643</c>), as a line on the panel
    /// rather than a modal.
    ///
    /// <para>The no-clip case is <see cref="DescribeTestRefusal"/>. It is upstream's own branch, not
    /// a port shortfall dressed up: <c>AudioService.cs:600-607</c> ends the diagnostic with
    /// <i>"WARNING: No test sound files found to play"</i> when none of its three candidates exists.
    /// It is reached far more often here, because this build ships no sound resources at all (the
    /// payload rule; the same absence <c>Effects/PopQuizEffect.cs:96-99</c> records for its
    /// chime).</para>
    /// </summary>
    /// <param name="device">What <c>EnsureDevice</c> answered.</param>
    /// <param name="play">What the cue answered.</param>
    /// <param name="clip">The clip's file name.</param>
    /// <param name="master">The master volume, quoted because the test deliberately ignores it.</param>
    public static string DescribeTest(SoundOutcome device, SoundOutcome play, string clip, int master)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(play);

        var head = device switch
        {
            SoundOutcome.Ready ready => $"Device: '{ready.DeviceName}' is open.",
            SoundOutcome.Unavailable unavailable => $"Device: none — {unavailable.Reason}.",
            SoundOutcome.Failed failed => $"Device: would not open — {failed.Error}.",
            _ => $"Device: {device}.",
        };

        return play switch
        {
            SoundOutcome.Started => head + $" Cued '{clip}' at a fixed 50%, which deliberately ignores "
                + $"the master volume (yours is {master}%) so a muted app can still prove its endpoint. "
                + "If you heard nothing, the endpoint's own volume, its mute, or whatever is plugged "
                + "into it is where to look — this app cannot see any of that.",
            SoundOutcome.Unavailable unavailable => head + $" Nothing played: {unavailable.Reason}.",
            SoundOutcome.Dropped dropped => head + $" The cue was dropped ({dropped.Reason}).",
            SoundOutcome.Failed failed => head + $" Playback failed: {failed.Error}.",
            _ => head + $" {play}.",
        };
    }

    /// <summary>What the Test button says before it has been pressed. It describes the gesture
    /// rather than claiming anything, because an empty notice box beside a button is indistinguishable
    /// from a button that ran and reported nothing.</summary>
    public static string DescribeTestNotRun() =>
        "Not tested yet. Test brings up an output device and cues one clip at a fixed half volume, "
        + "then says exactly what the operating system answered.";

    /// <summary>
    /// <b>The refusal, and it is raised BEFORE a device is asked for.</b> There is no clip to cue, so
    /// this build declines to open a render endpoint at all — the same reasoning
    /// <c>AudioParticipant</c> uses to keep phase 3 device-free, applied to a diagnostic that cannot
    /// proceed. <see cref="AudioParticipant.DeviceInitAttempts"/> therefore does NOT move on this
    /// path, which is what a fact can check.
    ///
    /// <para><b>Upstream probes the device first and reports the clip absence second</b>
    /// (<c>Services/AudioService.cs:578-607</c>) because its diagnostic's own content IS the device
    /// probe. This port has that content permanently on the panel already
    /// (<see cref="DescribeDeviceOutcome"/>), so nothing is lost by not seizing an endpoint to
    /// re-obtain it. The user-visible outcome is upstream's: a named refusal naming what is
    /// missing.</para>
    /// </summary>
    public static string DescribeTestRefusal(string folders) =>
        "Nothing to play, so nothing was opened. This build ships no test sound of its own, and "
        + $"there is no .mp3, .wav or .ogg in {folders}. Drop one into either folder and press Test "
        + "again — no audio device was brought up for this, so nothing else on your machine was "
        + "disturbed.";

    /// <summary>The catch-all, which upstream also has and for the same reason: its own handler ends
    /// in <c>catch (Exception ex)</c> showing <i>"Audio diagnostics failed: {message}"</i>
    /// (<c>MainWindow.UiUpdates.cs:1081-1086</c>). The seams below are documented never to throw into
    /// a caller, so this is the belt on top of the braces — and a diagnostic that died silently
    /// would be the worst possible failure for the one button whose job is to tell the truth about
    /// sound.</summary>
    public static string DescribeTestFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return $"The audio test itself failed: {error.GetType().Name}: {error.Message}";
    }
}
