using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>
/// THE HARDWARE GATE ON THE QUEST ROLL (ccp-bugs#1151). A machine with no webcam was still dealt
/// the blink-trainer quests (blink_drill_d, obedient_eyes_d, blink_century_w, eyes_trained_w):
/// impossible to move, and the only way out was to burn a reroll on a board that could deal
/// another one straight back. The roll now asks whether this machine HAS a camera first.
///
/// CAMERA ONLY, deliberately. No quest category needs the microphone: the one voice-adjacent
/// category is Mantra, and mantras are completed by TYPING them (MantraService.TryCompleteMantra)
/// - the mic-verified spoken path (CreditExternalMantra) is an extra way in, never the only one.
/// A future voice-only category needs a second predicate here and nothing else.
///
/// Three rules the callers depend on. ONE probe per roll: the answer is cached, so dealing three
/// daily seats enumerates devices once and a camera plugged in later is still noticed. NEVER
/// BLOCK: the roll can run on the UI thread (QuestService's constructor), so the probe runs on the
/// thread pool and the caller waits at most ProbeBudget for it. FAIL OPEN: a probe that throws,
/// times out or has never run reads as PRESENT, because narrowing the pool on an error would
/// silently take quests away from people who own the camera.
/// </summary>
internal sealed class QuestHardwareGate
{
    /// <summary>The instance the roll uses. Tests construct their own with a fake probe.</summary>
    public static readonly QuestHardwareGate Shared = new();

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ProbeBudget = TimeSpan.FromMilliseconds(400);

    private readonly Func<bool> _cameraProbe;
    private readonly object _lock = new();
    private Task? _inFlight;
    private bool _hasCamera = true;
    private DateTime _probedAtUtc = DateTime.MinValue;

    internal QuestHardwareGate(Func<bool>? cameraProbe = null) => _cameraProbe = cameraProbe ?? DetectCamera;

    /// <summary>Whether this machine has a camera, cached. Safe to call from the UI thread.</summary>
    public bool HasCamera()
    {
        Task probe;
        lock (_lock)
        {
            if (DateTime.UtcNow - _probedAtUtc < CacheTtl) return _hasCamera;
            probe = _inFlight ??= Task.Run(RunProbe);
        }
        try
        {
            // On timeout: answer "present" and let the probe land for the next roll.
            if (!probe.Wait(ProbeBudget)) return true;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "Quest camera probe failed; assuming a camera is present");
            return true;
        }
        lock (_lock) { return _hasCamera; }
    }

    private void RunProbe()
    {
        bool found;
        try { found = _cameraProbe(); }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "Quest camera probe threw; assuming a camera is present");
            found = true;
        }
        lock (_lock) { _hasCamera = found; _probedAtUtc = DateTime.UtcNow; _inFlight = null; }
        App.Logger?.Information("Quest hardware probe: camera={HasCamera}", found);
    }

    /// <summary>The enumeration the Lab tab shows "(no cameras detected)" from; the service's
    /// DirectShow + WinRT/MF pair is repeated for a roll that happens before it is up.</summary>
    private static bool DetectCamera()
    {
        var svc = App.Webcam;
        if (svc != null) return svc.EnumerateDevices().Count > 0;
        return WebcamDeviceEnumerator.Enumerate().Count > 0 || WebcamWinRtEnumerator.Enumerate().Count > 0;
    }

    /// <summary>Categories that cannot move without a webcam.</summary>
    internal static bool NeedsCamera(QuestCategory category) => category == QuestCategory.BlinkTrainer;

    /// <summary>Drops what this machine cannot serve - and hands the ungated pool back untouched if
    /// that would leave nothing to roll. A missing camera costs the player blink quests, never the
    /// day itself, so the gate is the first predicate dropped when the pool runs dry.</summary>
    internal static List<QuestDefinition> GateOrFallBack(List<QuestDefinition> pool, bool hasCamera)
    {
        if (hasCamera) return pool;
        var gated = pool.Where(q => !NeedsCamera(q.Category)).ToList();
        return gated.Count > 0 ? gated : pool;
    }
}
