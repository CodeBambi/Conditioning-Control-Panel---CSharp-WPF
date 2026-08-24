using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Companion;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The DTRH host's bark COMPOSITION — what <c>DtrhBarkRouting</c>'s table routes into — lifted out
/// of <see cref="DtrhHostWindow"/> so it can be DRIVEN.
///
/// <para><b>Why it exists.</b> The execution census found that
/// <c>DtrhHostWindow.InitBarkPipeline</c> was the SOLE construction site of four types with zero
/// executed lines — <c>SystemSoundClock</c>, <c>UnavailableDuckSink</c> (both of which have since
/// moved on to <see cref="Audio.AudioParticipant"/> with the arbitration itself),
/// <see cref="DirectoryBarkAudioResolver"/> and the window's own private log adapter — inside an
/// 833-line <c>Window</c> that needs a real <c>ApplicationHost</c> carrying a
/// <see cref="DtrhParticipant"/> and a <see cref="DtrhSaveSlots"/>, an Avalonia
/// <c>InitializeComponent()</c>, and an <c>Opened</c> handler that boots a real audio device. That
/// window is not drivable, so the WIRING inside it never was either — and the wiring is where the
/// silent mistakes live: a mistyped audio folder makes every bark resolve to a miss and surface
/// text-only for a reason indistinguishable from "no voice assets shipped yet", which is the port's
/// real current state.</para>
///
/// <para><b>Why it is a PARTIAL of <c>DtrhBarkRouting</c> and not a type of its own.</b> Because
/// this IS the DTRH bark boundary, which is what <c>DtrhBarkRouting</c> already is — one half names
/// the events that become barks, the other names the machine that speaks them. (The mechanical half
/// of the original reason has since expired: adding a shipped type used to red a census guard that
/// compared reflection against a scalar stored in <c>execution-census.md</c>, and both halves of
/// that comparison now run at RUNTIME — see <c>ExecutionCensusTests</c>'s own summary. A type is
/// free to be a type again.)</para>
///
/// <para><b>What this is NOT.</b> It is not a redesign and it constructs nothing new. Every object,
/// every argument and every order is exactly what <c>InitBarkPipeline</c> built before the lift; the
/// window keeps the persistence store with its host log adapter, the <c>BarkSurfaced</c>
/// subscription, the m2-test early return and the whole teardown.</para>
///
/// <para><b>What the APP-WIDE AUDIO LIFT took out of here.</b> <c>CreateArbitration</c> is gone: the
/// one <see cref="SoundArbitration"/> is now built by <see cref="Audio.AudioParticipant"/> at app
/// lifetime and the window consumes it. The bark CONTENT pipeline stayed, because it is DTRH's and
/// not the application's — its document is <c>&lt;DTRH data directory&gt;/companion.json</c>, its
/// triggers are the DTRH page's own events (the routing table in <c>DtrhBarkRouting.cs</c>), and its
/// only surface is the DTRH host window. What had to become app-wide was the shared DEVICE, not the
/// thing that decides which line the companion says.</para>
/// </summary>
public static partial class DtrhBarkRouting
{
    /// <summary>
    /// The voice-asset folder under the DTRH data directory (WPF's own folder name —
    /// <c>ConditioningControlPanel/Services/Companion/BarkService.cs:1419</c> combines
    /// <c>resources/sounds/companion_audio</c>).
    /// </summary>
    public const string CompanionAudioFolder = "companion_audio";

    // NOT hoisted here: the companion-state file name. It is used only by the window, which builds
    // the persistence store, so a constant for it would be an indirection no fact can drive — and a
    // constant only the untestable side reads pins nothing at all. It stays a literal at its single
    // use site (DtrhHostWindow.axaml.cs), which is the honest shape.

    // The ARBITRATION is no longer composed here. It moved to Audio/AudioParticipant.cs, which is
    // the application's audio owner: the same objects in the same order (the named-limit duck sink,
    // the real clock with its contained-fault callback, stock options), built once for the app
    // instead of once per host window. What stays here is the half that is really DTRH's — the bark
    // CONTENT pipeline, whose document, rule set and surface all belong to this feature.

    /// <summary>
    /// The bark content pipeline over the compact built-in rule set, resolving voice assets out of
    /// <see cref="CompanionAudioFolder"/> beneath <paramref name="dataDirectory"/>.
    /// </summary>
    /// <param name="arbitration">The app-wide arbitration
    /// (<c>Audio.AudioParticipant.Arbitration</c>); facts pass one over a recording backend.</param>
    /// <param name="store">The companion-state document on persistence machinery.</param>
    /// <param name="dataDirectory">The DTRH data directory (<c>DtrhParticipant.DataDirectory</c>).</param>
    /// <param name="log">The host diagnostic log (rule-parse diagnostics + pipeline lines).</param>
    /// <param name="masterVolume">
    /// The app's persisted master volume, 0..100 (<c>Audio.AudioSettingsDocument.MasterVolume</c>).
    /// Null leaves <see cref="BarkPipeline.MasterVolume"/> at its own default, which is what every
    /// caller had before an app-wide document existed to read. It is a parameter rather than an
    /// assignment at the call site so the document→pipeline wire is drivable: a bark speaking at
    /// full volume for a user who set 0 is silent to every other kind of check.
    /// </param>
    public static BarkPipeline CreatePipeline(
        SoundArbitration arbitration,
        PersistenceStore<CompanionStateDocument> store,
        string dataDirectory,
        Action<string> log,
        int? masterVolume = null)
    {
        ArgumentNullException.ThrowIfNull(arbitration);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dataDirectory);
        ArgumentNullException.ThrowIfNull(log);
        var pipeline = new BarkPipeline(
            arbitration,
            store,
            new DirectoryBarkAudioResolver(Path.Combine(dataDirectory, CompanionAudioFolder)),
            BarkRuleLoader.Parse(DefaultBarkRules.ManifestJson, log),
            new BarkPipelineOptions(),
            log);
        if (masterVolume is { } master)
        {
            pipeline.MasterVolume = master;
        }

        return pipeline;
    }

    /// <summary>
    /// STOP what <see cref="CreatePipeline"/> put on the app-wide arbitration, and NOTHING ELSE —
    /// the mirror of the composition above, called from the host window's teardown
    /// (<c>DtrhHostWindow.TeardownBarkPipeline</c>). Null-tolerant and idempotent, because that
    /// teardown is best-effort and runs on paths where the pipeline was never built (m2 test mode,
    /// a host with no audio owner).
    ///
    /// <para><b>Voice, and only voice.</b> A bark is the only thing this window plays on the shared
    /// arbitration: <c>BarkPipeline</c> reaches it through <c>PlayVoicePriority</c>/<c>QueueVoice</c>
    /// and nothing else (<c>Companion/BarkPipeline.cs:617-618</c>). The window's OTHER sounds — the
    /// page's whispers and one-shots — are the DTRH-local engine on its own device
    /// (<c>DtrhNativeEffects</c> over <c>SoundFlowDtrhAudio</c>), which
    /// <c>TeardownNativeEffects</c> owns, and which <c>Audio/SoundArbitration.cs:104-106</c> keeps
    /// deliberately outside this core.</para>
    ///
    /// <para><b>Why this is a call and not a line inside the window.</b> It used to be
    /// <c>PanicReset()</c>, which stops EVERY channel — correct while DTRH was the only consumer of
    /// an arbitration it built itself, and a live regression the day the flash clip and the bubble
    /// pops started using the same app-wide one (<c>Effects/EffectSounds.cs:212,:247</c>): closing
    /// this window cut off a whisper and a pop that were never its own. The window is not drivable
    /// (see this file's type summary for why), so the choice of channel lives here, where a fact can
    /// make it.</para>
    /// </summary>
    /// <param name="arbitration">The app-wide arbitration the window consumed, or null if it never
    /// got one.</param>
    public static void StopPipelineAudio(SoundArbitration? arbitration) =>
        arbitration?.StopChannel(SoundChannel.Voice, "dtrh host window closed");
}
