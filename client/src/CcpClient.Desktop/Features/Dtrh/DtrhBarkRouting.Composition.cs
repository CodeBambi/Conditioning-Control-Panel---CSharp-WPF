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
/// executed lines — <see cref="SystemSoundClock"/>, <see cref="UnavailableDuckSink"/>,
/// <see cref="DirectoryBarkAudioResolver"/> and the window's own private log adapter — inside an
/// 833-line <c>Window</c> that needs a real <c>ApplicationHost</c> carrying a
/// <see cref="DtrhParticipant"/> and a <see cref="DtrhSaveSlots"/>, an Avalonia
/// <c>InitializeComponent()</c>, and an <c>Opened</c> handler that boots a real audio device. That
/// window is not drivable, so the WIRING inside it never was either — and the wiring is where the
/// silent mistakes live: a mistyped audio folder makes every bark resolve to a miss and surface
/// text-only for a reason indistinguishable from "no voice assets shipped yet", which is the port's
/// real current state.</para>
///
/// <para><b>Why it is a PARTIAL of <c>DtrhBarkRouting</c> and not a type of its own.</b> Two
/// reasons, one of them mechanical and load-bearing. Mechanically: a new shipped type changes the
/// authored-type count of <c>CcpClient.Desktop</c>, and that count is ANCHORED — <c>census.mjs</c>
/// publishes it and <c>ExecutionCensusTests.Census_DenominatorIsAnchoredToTheShippedAssembly</c>
/// recomputes it by reflection and requires exact agreement, so adding a type reds the floor until
/// the census is regenerated. Measured, not assumed: as a standalone type this code failed that
/// guard with 885 against the published 884. Semantically: this IS the DTRH bark boundary, which is
/// what <c>DtrhBarkRouting</c> already is — one half names the events that become barks, the other
/// names the machine that speaks them.</para>
///
/// <para><b>What this is NOT.</b> It is not a redesign and it constructs nothing new. Every object,
/// every argument and every order is exactly what <c>InitBarkPipeline</c> built before the lift; the
/// window keeps the backend construction, the device <c>Initialize</c> and its log line, the
/// persistence store with its host log adapter, the <c>BarkSurfaced</c> subscription, the m2-test
/// early return and the whole teardown. A refactor with no behaviour in it.</para>
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

    /// <summary>
    /// q1's arbitration over the supplied backend: the named-limit duck sink
    /// (<see cref="UnavailableDuckSink"/> — cross-app session ducking is a pending-owner platform
    /// decision, so a duck is refused TYPED rather than pretended), the real clock, and stock
    /// options.
    /// </summary>
    /// <param name="backend">The audio backend. Product = a second SoundFlow engine (miniaudio
    /// devices coexist — the DTRH audio boundary); facts = a recording fake.</param>
    /// <param name="log">The host diagnostic log; also the clock's callback-fault reporter, so a
    /// faulting pacing/recovery/duck-watchdog callback is contained and named instead of ending the
    /// process from a pool thread.</param>
    public static SoundArbitration CreateArbitration(IAudioBackend backend, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(log);
        return new SoundArbitration(
            backend,
            new UnavailableDuckSink(), // q1 named limit: cross-app duck sink not admitted
            new SystemSoundClock(ex => log(
                $"dtrh: bark arbitration — a scheduled callback faulted and was contained "
                + $"({ex.GetType().Name}: {ex.Message})")),
            new SoundArbitrationOptions(),
            log);
    }

    /// <summary>
    /// The bark content pipeline over the compact built-in rule set, resolving voice assets out of
    /// <see cref="CompanionAudioFolder"/> beneath <paramref name="dataDirectory"/>.
    /// </summary>
    /// <param name="arbitration">The arbitration from <see cref="CreateArbitration"/>.</param>
    /// <param name="store">The companion-state document on persistence machinery.</param>
    /// <param name="dataDirectory">The DTRH data directory (<c>DtrhParticipant.DataDirectory</c>).</param>
    /// <param name="log">The host diagnostic log (rule-parse diagnostics + pipeline lines).</param>
    public static BarkPipeline CreatePipeline(
        SoundArbitration arbitration,
        PersistenceStore<CompanionStateDocument> store,
        string dataDirectory,
        Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(arbitration);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dataDirectory);
        ArgumentNullException.ThrowIfNull(log);
        return new BarkPipeline(
            arbitration,
            store,
            new DirectoryBarkAudioResolver(Path.Combine(dataDirectory, CompanionAudioFolder)),
            BarkRuleLoader.Parse(DefaultBarkRules.ManifestJson, log),
            new BarkPipelineOptions(),
            log);
    }
}
