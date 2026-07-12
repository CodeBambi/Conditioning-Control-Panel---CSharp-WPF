namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// The single head-callback seam for the portable <see cref="DtrhHostOrchestrator"/>. Everything the
/// orchestrator cannot do in CCP.Core — real desktop conditioning effects, world-freeze of native
/// video/avatar audio, chaos SFX, voice barks, reveal-sync over the head reveal registry, building
/// the head <c>ChaosRunConfig</c>, and restoring the main window from the tray — is expressed here and
/// implemented by the Avalonia head (S2c-2). Every method has a no-op default so unit-test fakes need
/// only override what they assert, and a head impl supplies the rest. Mirrors the WPF
/// <c>DtrhHostService</c> native-call surface (DtrhHostService.cs:6 inventory).
/// </summary>
public interface IDtrhNativeEffects
{
    /// <summary>Play a chaos SFX by name at <paramref name="scale"/> gain. The head branches
    /// <c>wave_clear</c>/<c>ripple_cast</c>/generic (WPF DtrhHostService.cs:217-220).</summary>
    void PlaySfx(string name, float scale) { }

    /// <summary>Fire a real desktop conditioning payload. <paramref name="kind"/> is <c>video</c> or
    /// <c>audio</c>; strength 0-100, durationMult 0.1-10.0 (already clamped by the orchestrator).
    /// WPF <c>FirePayload</c> DtrhHostService.cs:368-391.</summary>
    void FirePayload(string kind, int strength, double durationMult) { }

    /// <summary>Freeze/unfreeze the native world (pause/resume primary video + avatar spoken audio).
    /// The orchestrator owns the on/off dedup and only calls this on a transition. The head marshals to
    /// the UI thread. WPF <c>ApplyWorldFreeze</c> DtrhHostService.cs:555-579.</summary>
    void SetWorldFrozen(bool frozen) { }

    /// <summary>Return input focus to the browser surface after a native video ends
    /// (WPF <c>_host.FocusWeb()</c> DtrhHostService.cs:654).</summary>
    void ReclaimBrowserFocus() { }

    /// <summary>Borderless-fullscreen the game window on the page's request. The page drives
    /// fullscreen over the bridge (<c>fullscreen-set</c>) instead of the browser HTML5 Fullscreen
    /// API, which would hijack Esc away from the game's Esc ladder. The head marshals to the UI
    /// thread and toggles the window state. WPF <c>ApplyHostFullscreen</c>
    /// DtrhHostService.cs:286-302 / <c>ChaosWebViewHost.SetFullscreen</c> ChaosWebViewHost.cs:163-175.</summary>
    void SetHostFullscreen(bool on) { }

    /// <summary>Route a <c>bark</c> page message to the head voice service. The head parses the JSON and
    /// dispatches the ~40 <c>App.Bark.NotifyChaos*</c> calls (WPF <c>RouteBark</c> DtrhHostService.cs:498-548).</summary>
    void RouteBark(string barkJson) { }

    /// <summary>Run-start voice cue (WPF <c>App.Bark.NotifyChaosRunStarted</c> DtrhHostService.cs:243).</summary>
    void NotifyRunStarted(string difficulty) { }

    /// <summary>Run-complete voice cue (WPF <c>App.Bark.NotifyChaosRunCompleted</c> DtrhHostService.cs:448).</summary>
    void NotifyRunCompleted(int finalXp, string difficulty) { }

    /// <summary>Recompute reveal unlocks after a run ends. MUST run before the meta rebroadcast so the
    /// page sees fresh pendingReveals (WPF <c>RevealService.Sync("run_end")</c> DtrhHostService.cs:441).</summary>
    void SyncReveals(string reason) { }

    /// <summary>Build the outbound <c>run-config</c> payload as a JSON object string. The head owns
    /// <c>ChaosRunConfig</c> construction (settings + owned upgrades + reveal clamps) — the orchestrator
    /// only decides scripted-vs-normal and wraps the result in the envelope. WPF <c>BuildRunConfig</c>
    /// DtrhHostService.cs:808-891.</summary>
    string BuildRunConfigJson(bool scripted) => "{}";

    /// <summary>Active mod id for the <c>init</c> handshake (WPF <c>App.Mods.ActiveModId</c>
    /// DtrhHostService.cs:911).</summary>
    string ActiveModId() => "builtin-sissyhypno";

    /// <summary>Restore the main window from the tray on teardown (WPF <c>ShowFromTray</c>
    /// DtrhHostService.cs:792).</summary>
    void RestoreMainWindow() { }
}
