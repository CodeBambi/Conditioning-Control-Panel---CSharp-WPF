using System.Globalization;
using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The two audio panels' sentences, in their own file for the reason
/// <see cref="RampPanelNotices"/> is in its: <c>StudioPage.axaml.cs</c> carries every landed module's
/// rendered claims and does not scale, and the extraction that fixes it is not this packet's risk to
/// take. New text starts where the extraction would put it.
///
/// <para><b>These panels have no surface line, and they have something harder instead.</b> A drawing
/// module's panel can end with "the last flash was placed on an overlay" because a user who doubts it
/// can look. Silence and a working clip at a volume you cannot hear are the SAME observation, so the
/// audio panel's closing line reports what the OPERATING SYSTEM said — the endpoint it named, whether
/// it holds a render session for this process, and, where the meter is readable, the level it last
/// measured on that stream. It is the one line on this page that quotes a fact from outside the
/// process.</para>
///
/// <para><b>And it stops where the evidence stops.</b> No sentence here says a sound was heard, or
/// even that it left the machine. The strongest thing any of them claims is that Windows metered
/// non-silent samples on this process's stream, and the words say so.</para>
/// </summary>
public static class AudioPanelNotices
{
    /// <summary>
    /// The live-state line for a rolled audio module, off the row's own dot plus its counters.
    ///
    /// <para>The <see cref="EffectDotState.Armed"/> arms are deliberately several sentences rather
    /// than one, on a finding this port keeps re-learning: a module that is Armed because no session is running and a
    /// module that is Armed <b>during</b> a session because the OS confirms no render session are
    /// completely different situations, and telling the second one to "start a session" — which they
    /// already did — is the exact message that had to be split apart for Linux users.</para>
    /// </summary>
    public static string DescribeCueState(
        EffectDotState dot, string noun, int cueCount, AudioCueEvent? last, bool sessionRunning, bool rendering)
    {
        if (dot == EffectDotState.Off)
        {
            return $"Switched off. No {noun} will play, session or no session.";
        }

        if (dot == EffectDotState.Armed && !sessionRunning)
        {
            return "Armed. Nothing plays until the session starts.";
        }

        if (dot == EffectDotState.Armed && !rendering)
        {
            // The session IS running and this module is not. Never "start a session" — see the
            // remarks. The reason itself is on the capability line below, verbatim.
            return $"Running, but silent: the operating system does not report an audio output this "
                + $"process can render to, so no {noun} is being scheduled to play. See below for what "
                + "was asked and what came back.";
        }

        if (dot == EffectDotState.Armed)
        {
            // Session running, audio confirmed, and this module's schedule is still not on the clock.
            // AN EARLIER VERSION REPEATED THE "nothing plays until the session starts" SENTENCE HERE
            // — which is false in front of a user who has already started one, and is precisely the
            // instruction that had to be split apart for another module. Found by writing this
            // branch's fact rather than by reading. It is reachable: a module whose generation was
            // cancelled, or one switched on between the arm and the repaint, lands here.
            return $"This module is not scheduled right now, though the session is running and audio "
                + $"output is confirmed. Switching it off and on again re-arms it.";
        }

        var head = $"Running: the next {noun} is on the clock.";
        if (cueCount == 0)
        {
            return head;
        }

        var ordinal = last is null ? string.Empty : $" The last one was #{last.Ordinal}.";
        return $"{head} {cueCount} {noun}{(cueCount == 1 ? string.Empty : "s")} played so far.{ordinal}";
    }

    /// <summary>Where the clips come from, and what is in there. The port's standing answer to
    /// "where do I put them" — the same shape the flash panel uses for its images folder.</summary>
    public static string DescribeClipPool(int clipCount, string folder) =>
        clipCount == 0
            ? $"No clips found. Put .mp3, .wav or .ogg files in {folder} — they are picked up while "
                + "the session runs, without restarting."
            : $"{clipCount} clip{(clipCount == 1 ? string.Empty : "s")} in {folder}.";

    /// <summary>
    /// <b>The line that quotes the operating system.</b> It reports the capability's own last typed
    /// outcome — reason code, detail and all — never a sentence this page made up about a platform,
    /// which is the same rule the four drawing panels' surface lines follow.
    ///
    /// <para>Before anything has been attempted it names the MECHANISM and says nothing has been
    /// asked yet: a user must not have to press START and listen to find out how this module reaches
    /// their speakers, and "nothing has been asked yet" is a fact about this session rather than a
    /// claim about the machine.</para>
    /// </summary>
    public static string DescribeAudioCapability(CapabilityState? open, AudioRenderObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var head = open switch
        {
            null => "Clips play on a shared audio output device. Nothing has been asked of the "
                + "operating system yet.",
            CapabilityState.Available a => $"Audio output confirmed by the operating system: {a.Detail}",
            CapabilityState.Unavailable u => $"No audio output: {u.Reason.Detail}",
            CapabilityState.DependencyMissing m => $"No audio output: {m.Reason.Detail}",
            CapabilityState.Degraded d => $"Partly available: {d.SurvivingSemantics}. {d.Reason.Detail}",
            CapabilityState.PermissionRequired p => $"No audio output: {p.Reason.Detail}",
            CapabilityState.Faulted f => $"No audio output: {f.Reason.Detail}",
            // The hierarchy is closed (CapabilityState's ctor is private), so this arm is unreachable
            // today; it is this codebase's own convention for these switches and prints the state
            // rather than inventing a sentence.
            _ => open.ToString() ?? string.Empty,
        };

        // INVARIANT, deliberately, where every other number on this page is CurrentCulture: this one
        // is quoted from an operating-system API and travels into bug reports, and the same
        // measurement rendering as "0.405" in one locale and "0,405" in another makes one fact two
        // strings to search for.
        var peak = observation.Peak.ToString("0.000", CultureInfo.InvariantCulture);
        return observation.MeterReadable
            ? $"{head} Windows last metered a peak of {peak} on this process's own "
                + "audio stream — which proves samples reached its mixer, and does NOT prove your "
                + "speakers were on."
            : head;
    }
}
