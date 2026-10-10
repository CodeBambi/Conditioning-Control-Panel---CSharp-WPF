// PORTED from ConditioningControlPanel/Services/Possession/PossessionContracts.cs (7.1.5).
// The WPF file declares the host, target and effect against FrameworkElement / Window / Canvas. This is
// the head-neutral twin: the same names and members, with the control behind an opaque Element and the
// window behind PossessionHost (delegates, like LockdownDoseHost). EscapeKinds / EscapeAttempt live in
// EscapeKinds.cs. Not carried: IPossessionAttribution (charge ripple, possessed outline, cursor ring) and
// IPossessionWarden (the tube glide verbs) - no head here has either; the edge pulse is on the host.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Possession;

/// <summary>The five rungs of the Possession Ladder, keyed to the ELAPSED FRACTION of the lockdown
/// timer (0-10% Settle, 10-35% Drift, 35-60% Melt, 60-85% Collapse, 85-100% It knows). Gentle caps at
/// Melt; Eerie (default) caps at Collapse; only Full Doki reaches It knows.</summary>
public enum PossessionRung { Settle = 0, Drift = 1, Melt = 2, Collapse = 3, ItKnows = 4 }

/// <summary>Intensity preset chosen on the Lockdown card. Stored as int in AppSettings.LockdownPossessionIntensity.</summary>
public enum PossessionIntensity { Gentle = 0, Eerie = 1, FullDoki = 2 }

/// <summary>What a registered target IS, so effects can pick fitting victims.</summary>
public enum PossessionRole { None = 0, Button, Card, Toggle, Title, Label, TabHeader, Timer, Slider, Combo, Image, Scroll, Progress, TextBox }

/// <summary>A possessable control the host registered. Key is stable across the lockdown and is what
/// per-target cooldowns hang off.</summary>
public sealed class PossessionTarget
{
    /// <summary>The head's control (WPF FrameworkElement, Avalonia Control). Core never looks inside.</summary>
    public required object Element { get; init; }
    public required PossessionRole Role { get; init; }
    public required string Key { get; init; }
    /// <summary>Friendly name the warden uses when it names a big effect ("the Stop button").</summary>
    public string DisplayName { get; init; } = "";
    /// <summary>WPF reads Element.IsVisible; the head answers for its own control type.</summary>
    public Func<bool> IsVisible { get; init; } = () => true;
    public DateTime CooldownUntil { get; set; } = DateTime.MinValue;
    public bool IsLive { get; set; }   // currently possessed by some effect - never double-book
}

/// <summary>What the director needs from the window it haunts (WPF IPossessionHost + the one
/// IPossessionAttribution verb every head can draw).</summary>
public sealed class PossessionHost
{
    /// <summary>Live registry of possessable controls; read before each pick.</summary>
    public Func<IReadOnlyList<PossessionTarget>> Targets = () => Array.Empty<PossessionTarget>();
    /// <summary>False while the window is minimized, not loaded, or covered by a playback takeover -
    /// the director never STARTS an effect then (live ones keep running).</summary>
    public Func<bool> IsUsable = () => false;
    /// <summary>Window-edge ember pulse, strength 0..1 (tripwires, rung changes, the Dose).</summary>
    public Action<double> EdgePulse = _ => { };
    /// <summary>WPF App.ScreenShake.Shake(amplitude, ms): the second tripwire repeat. Never when photosafe.</summary>
    public Action<double, int> Shake = (_, _) => { };
    /// <summary>Run on the UI thread (inline by default: tests).</summary>
    public Action<Action> OnUi = a => a();
}

/// <summary>Everything an effect may read. Built fresh by the director for each Apply.</summary>
public sealed class PossessionContext
{
    public required PossessionHost Host { get; init; }
    public required PossessionRung Rung { get; init; }
    public required PossessionIntensity Intensity { get; init; }
    public required bool Photosafe { get; init; }
    public required Random Rng { get; init; }
    public required double ElapsedFraction { get; init; }
    public required TimeSpan Remaining { get; init; }
    /// <summary>Ask the warden to name what just happened: (effectId, target DisplayName).</summary>
    public required Action<string, string?> Name { get; init; }
}

/// <summary>One haunt. Stateless until Apply; Undo MUST be safe to call when Apply never ran, was
/// cancelled half-way, or already undid itself. Undo restores the real control EXACTLY.</summary>
public interface IPossessionEffect
{
    string Id { get; }
    PossessionRung MinRung { get; }
    PossessionIntensity MinIntensity { get; }
    /// <summary>Big effects are named by the warden; micro-tics stay silent.</summary>
    bool IsBig { get; }
    /// <summary>Skipped entirely when Photosafe (hard blinks / strobing flicker).</summary>
    bool UsesFlicker { get; }
    /// <summary>Deck weight at the effect's MinRung; the deck scales it up per rung above.</summary>
    double Weight { get; }
    /// <summary>How long the haunt stays before the director auto-undoes it. Zero = until exit.</summary>
    TimeSpan HoldFor { get; }
    /// <summary>Roles this effect can take a victim from (empty = no target needed).</summary>
    IReadOnlyList<PossessionRole> Roles { get; }
    bool IsLive { get; }
    bool CanApply(PossessionContext ctx, PossessionTarget? target);
    Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct);
    /// <summary>Zero duration = snap back now, synchronously (panic, UndoAll).</summary>
    Task UndoAsync(TimeSpan duration);
}

/// <summary>A choreography (WPF IPossessionScene behind its PossessionSceneEffect adapter): a haunt
/// the director starts with NO target. It reads the host registry itself, books each victim it takes
/// (PossessionTarget.IsLive) and gives every one back on undo, under the same rules as any effect.</summary>
public interface IPossessionScene : IPossessionEffect
{
    /// <summary>How many live slots the scene takes while it plays (PossessionDeck.FitsConcurrency).</summary>
    int Beats { get; }
}

/// <summary>Bark trigger names raised by the Possession layer (WPF BarkService.NotifyPossession*).</summary>
public static class PossessionBarkTriggers
{
    public const string RungChanged = "PossessionRungChanged";   // ctx: rung (0-4)
    public const string Effect = "PossessionEffect";             // ctx: effect (id), target (display name)
    public const string Tripwire = "PossessionTripwire";         // ctx: kind, repeat, total
    public const string Warden = "PossessionWarden";             // ctx: verb (knock|stare|leave|return)
    public const string Rules = "PossessionRules";               // first-run: the warden states the rules
    public const string TimerRestarted = "PossessionTimerRestarted"; // ctx: reason, restart (count)
    public const string Remember = "PossessionRemember";
    public const string Conscript = "LockdownConscript";
}
