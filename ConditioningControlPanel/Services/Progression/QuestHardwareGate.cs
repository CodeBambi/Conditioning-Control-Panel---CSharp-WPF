using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>
/// What this machine actually has, as one answer read once per roll. <see cref="Resolved"/> is
/// false when a probe has not landed yet (it timed out, or it is still running): the presence
/// flags then read PRESENT - fail open - and the caller arms a recheck so the board is looked at
/// again once the real answer is in. Without that flag the 400ms budget WAS half the bug: a cold
/// DirectShow enumeration at startup rarely finishes inside it, so the launch roll answered
/// "camera present", dealt the blink quest, and nothing ever revisited the decision.
/// </summary>
internal readonly record struct QuestHardwareState(bool HasCamera, bool HasMicrophone, bool Resolved);

/// <summary>
/// THE HARDWARE GATE ON THE QUEST ROLL (ccp-bugs#1151). A machine with no webcam was still dealt
/// the blink-trainer quests (blink_drill_d, obedient_eyes_d, blink_century_w, eyes_trained_w):
/// impossible to move, and the only way out was to burn a reroll on a board that could deal
/// another one straight back. The roll asks whether this machine HAS the hardware first, and -
/// since 6.9.4 - so does every pass that decides whether to KEEP a quest already on the board.
///
/// BOTH HALVES ARE NEEDED, which is why the first fix did not close the report. Gating only the
/// roll left three ways for an impossible quest to sit there anyway: a board dealt by a build
/// older than the gate, a weekly slot that survives a whole week unexamined, and a roll that ran
/// before the probe answered. The keep gate (QuestService.ReconcileDailySlots step 4, and the
/// weekly block in CheckAndGenerateQuests) closes all three.
///
/// Two devices, two probes, one discipline each. ONE probe per roll: answers are cached, so three
/// daily seats enumerate devices once and hardware plugged in later is still noticed. NEVER BLOCK:
/// a roll can run on the UI thread, so a probe runs on the thread pool and the caller waits at
/// most ProbeBudget. FAIL OPEN: a probe that throws, times out or has never run reads as PRESENT -
/// narrowing the pool on an error would take quests from people who own the device - but it also
/// reads as UNRESOLVED, which is what makes the fail-open temporary instead of permanent.
/// </summary>
internal sealed class QuestHardwareGate
{
    /// <summary>The instance the roll uses. Tests construct their own with fake probes.</summary>
    public static readonly QuestHardwareGate Shared = new();

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ProbeBudget = TimeSpan.FromMilliseconds(400);

    private readonly CachedProbe _camera;
    private readonly CachedProbe _microphone;

    internal QuestHardwareGate(Func<bool>? cameraProbe = null, Func<bool>? microphoneProbe = null)
    {
        _camera = new CachedProbe("camera", cameraProbe ?? DetectCamera);
        _microphone = new CachedProbe("microphone", microphoneProbe ?? DetectMicrophone);
    }

    /// <summary>Whether this machine has a camera, cached. Safe to call from the UI thread.</summary>
    public bool HasCamera() => _camera.IsPresent(out _);

    /// <summary>Whether this machine has an audio capture device, cached.</summary>
    public bool HasMicrophone() => _microphone.IsPresent(out _);

    /// <summary>Both answers plus whether they can be trusted, for one roll. Read ONCE per roll
    /// and pass the struct down, or a probe landing mid-roll can split the answer.</summary>
    public QuestHardwareState Snapshot()
    {
        var hasCamera = _camera.IsPresent(out var cameraResolved);
        var hasMic = _microphone.IsPresent(out var micResolved);
        return new QuestHardwareState(hasCamera, hasMic, cameraResolved && micResolved);
    }

    /// <summary>Kick both probes off without waiting. Called early in startup so the first roll
    /// reads a cached answer instead of losing the race against the 400ms budget.</summary>
    public void Prime()
    {
        _camera.Start();
        _microphone.Start();
    }

    /// <summary>The enumeration the Lab tab shows "(no cameras detected)" from; the service's
    /// DirectShow + WinRT/MF pair is repeated for a roll that happens before it is up.</summary>
    private static bool DetectCamera()
    {
        var svc = App.Webcam;
        if (svc != null) return svc.EnumerateDevices().Count > 0;
        return WebcamDeviceEnumerator.Enumerate().Count > 0 || WebcamWinRtEnumerator.Enumerate().Count > 0;
    }

    /// <summary>The same WaveIn enumeration the speech mic picker is built from - no new probe.</summary>
    private static bool DetectMicrophone() => Speech.SpeechService.HasCaptureDevice;

    /// <summary>Categories that cannot move without a webcam.</summary>
    internal static bool NeedsCamera(QuestCategory category) => category == QuestCategory.BlinkTrainer;

    /// <summary>
    /// Categories that cannot move without a microphone. EMPTY TODAY, and not an oversight: the
    /// one voice-adjacent embedded category is Mantra, and mantras are completed by TYPING them
    /// (MantraService.TryCompleteMantra) - the mic-verified path (CreditExternalMantra) is an
    /// extra way in, never the only one. The predicate exists because the definitions channel can
    /// publish a quest that DOES need one, declared with
    /// <see cref="QuestDefinition.RequiresHardware"/>. A voice-only category is one line here.
    /// </summary>
    internal static bool NeedsMicrophone(QuestCategory category) => false;

    /// <summary>Camera requirement of a whole definition: its category, or what it declares.</summary>
    internal static bool NeedsCamera(QuestDefinition quest) =>
        NeedsCamera(quest.Category) || DeclaresHardware(quest.RequiresHardware, "camera", "webcam");

    /// <summary>Microphone requirement of a whole definition: its category, or what it declares.</summary>
    internal static bool NeedsMicrophone(QuestDefinition quest) =>
        NeedsMicrophone(quest.Category) || DeclaresHardware(quest.RequiresHardware, "microphone", "mic");

    /// <summary>Reads the server's optional `requiresHardware` field. Comma or space separated,
    /// case insensitive, unknown words ignored - a typo in hand-authored JSON must cost the gate,
    /// never the player's quest.</summary>
    private static bool DeclaresHardware(string? declared, params string[] names)
    {
        if (string.IsNullOrWhiteSpace(declared)) return false;
        foreach (var part in declared.Split(new[] { ',', ';', ' ', '+', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
                if (string.Equals(part.Trim(), name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>True when this quest needs a device this machine does not have. The one predicate
    /// both the roll filters and the keep gates are built on, so they cannot drift apart.</summary>
    internal static bool NeedsAbsentHardware(QuestDefinition quest, bool hasCamera, bool hasMicrophone)
        => (!hasCamera && NeedsCamera(quest)) || (!hasMicrophone && NeedsMicrophone(quest));

    /// <summary>Drops what this machine cannot serve - and hands the ungated pool back untouched if
    /// that would leave nothing to roll. Missing hardware costs the player those quests, never the
    /// day itself, so the gate is the first predicate dropped when the pool runs dry.</summary>
    internal static List<QuestDefinition> GateOrFallBack(
        List<QuestDefinition> pool, bool hasCamera, bool hasMicrophone = true)
    {
        if (hasCamera && hasMicrophone) return pool;
        var gated = pool.Where(q => !NeedsAbsentHardware(q, hasCamera, hasMicrophone)).ToList();
        return gated.Count > 0 ? gated : pool;
    }

    /// <summary>One device's answer, probed off the UI thread and cached.</summary>
    private sealed class CachedProbe
    {
        private readonly string _label;
        private readonly Func<bool> _probe;
        private readonly object _lock = new();
        private Task? _inFlight;
        private bool _present = true;
        private bool _resolved;
        private DateTime _probedAtUtc = DateTime.MinValue;

        internal CachedProbe(string label, Func<bool> probe)
        {
            _label = label;
            _probe = probe;
        }

        /// <summary>Kick the probe off without waiting for it. Idempotent.</summary>
        internal void Start()
        {
            lock (_lock)
            {
                if (DateTime.UtcNow - _probedAtUtc < CacheTtl) return;
                _inFlight ??= Task.Run(Run);
            }
        }

        /// <summary>
        /// Present/absent, with <paramref name="resolved"/> false when the answer is the fail-open
        /// default rather than something a probe actually returned.
        /// </summary>
        internal bool IsPresent(out bool resolved)
        {
            Task probe;
            lock (_lock)
            {
                if (DateTime.UtcNow - _probedAtUtc < CacheTtl)
                {
                    resolved = _resolved;
                    return _present;
                }
                probe = _inFlight ??= Task.Run(Run);
            }
            try
            {
                // On timeout: answer "present", UNRESOLVED, and let the probe land for the recheck.
                if (!probe.Wait(ProbeBudget)) { resolved = false; return true; }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Quest {Device} probe failed; assuming the device is present", _label);
                resolved = false;
                return true;
            }
            lock (_lock) { resolved = _resolved; return _present; }
        }

        private void Run()
        {
            bool found;
            bool resolved = true;
            try { found = _probe(); }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Quest {Device} probe threw; assuming the device is present", _label);
                found = true;
                resolved = false;
            }
            lock (_lock) { _present = found; _resolved = resolved; _probedAtUtc = DateTime.UtcNow; _inFlight = null; }
            App.Logger?.Information("Quest hardware probe: {Device}={Present}", _label, found);
        }
    }
}
