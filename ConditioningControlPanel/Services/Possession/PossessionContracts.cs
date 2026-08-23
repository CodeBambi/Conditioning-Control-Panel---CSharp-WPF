using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Services.Possession;

// =====================================================================================================
//  POSSESSION - the haunted-UI layer of Lockdown. Read Services/Possession/POSSESSION.md first.
//  This file is the CONTRACT between the director (PossessionDirector), the effects (Effects/*),
//  the attribution grammar (EmberAttribution) and the host (MainWindow). Keep it small and stable.
// =====================================================================================================

/// <summary>The five rungs of the Possession Ladder, keyed to the ELAPSED FRACTION of the lockdown
/// timer (0-10% Settle, 10-35% Drift, 35-60% Melt, 60-85% Collapse, 85-100% It knows). Gentle caps at
/// Melt; Eerie (default) caps at Collapse; only Full Doki reaches It knows.</summary>
public enum PossessionRung { Settle = 0, Drift = 1, Melt = 2, Collapse = 3, ItKnows = 4 }

/// <summary>Intensity preset chosen on the Lockdown card. Stored as int in AppSettings.LockdownPossessionIntensity.</summary>
public enum PossessionIntensity { Gentle = 0, Eerie = 1, FullDoki = 2 }

/// <summary>What a registered target IS, so effects can pick fitting victims (a toggle dissolves, a
/// card falls, a title loses letters, buttons swap).</summary>
public enum PossessionRole { None = 0, Button, Card, Toggle, Title, Label, TabHeader, Timer, Slider, Combo, Image, Scroll, Progress, TextBox }

/// <summary>Escape-attempt kinds raised through LockdownService.NotifyEscapeAttempt (tripwires).</summary>
public static class EscapeKinds
{
    public const string Close = "close";              // Alt+F4 / window X / OnClosing
    public const string Minimize = "minimize";        // minimize button (ALLOWED - still a tripwire)
    public const string SystemKey = "syskey";         // a suppressed Win / Alt+Tab / Ctrl+Esc combo
    public const string Stop = "stop";                // Stop button, tube quick-menu Stop, voice stop
    public const string WrongPhrase = "wrong_phrase"; // a wrong secret phrase typed into the timer box
    public const string Settings = "settings";        // trying to flip a greyed safety toggle
    public const string EmergencyExit = "emergency_exit"; // pressed the big Emergency Exit button (minigame launched)
}

/// <summary>One escape attempt. Repeat = how many times THIS kind fired during this lockdown (1-based);
/// Total = every kind combined. The director scales the scare by both.</summary>
public readonly record struct EscapeAttempt(string Kind, int Repeat, int Total, DateTime At);

/// <summary>A possessable control the host registered (via the poss:Possession.Role attached property,
/// or explicit registration). Key is stable across the lockdown (x:Name or a role+index fallback) and
/// is what per-target cooldowns hang off.</summary>
public sealed class PossessionTarget
{
    public required FrameworkElement Element { get; init; }
    public required PossessionRole Role { get; init; }
    public required string Key { get; init; }
    /// <summary>Friendly name the warden uses when it names a big effect ("the Stop button").</summary>
    public string DisplayName { get; init; } = "";
    public DateTime CooldownUntil { get; set; } = DateTime.MinValue;
    public bool IsLive { get; set; }   // currently possessed by some effect - never double-book
}

/// <summary>What the director needs from a window it haunts. MainWindow implements it in
/// MainWindow.Possession.cs (Phase 1); other rooms (dashboard wall, Settings palette, Lock Card) follow.</summary>
public interface IPossessionHost
{
    Window Window { get; }
    /// <summary>Transparent, non-hit-testable Canvas stretched over the whole window (last child of
    /// RootGrid, above everything). Ghosts, ember overlays and the cursor ring live here.</summary>
    Canvas GhostLayer { get; }
    /// <summary>Canvas strip along the bottom of the window where fallen pieces come to rest.</summary>
    Canvas RubbleFloor { get; }
    /// <summary>Live registry of possessable controls; refreshed by the director before each pick
    /// (controls in collapsed tabs are skipped - IsVisible false).</summary>
    IReadOnlyList<PossessionTarget> Targets { get; }
    /// <summary>Top-left of the element in GhostLayer coordinates.</summary>
    Point PointOf(FrameworkElement element);
    /// <summary>False while the window is minimized, not loaded, or covered by a playback takeover -
    /// the director never STARTS an effect then (live ones keep running).</summary>
    bool IsUsable { get; }
}

/// <summary>The attribution grammar ("clarity in front"). Every effect speaks it; nothing moves
/// without ChargeAsync first, nothing stays moved without Possess, and the cursor ring is on while
/// anything is possessed. Ember #FF8A5C is reserved for Possession - never reuse it for theme.</summary>
public interface IPossessionAttribution
{
    /// <summary>The charge: ~400ms ember ripple over the target's bounds BEFORE it moves. Awaits the
    /// ripple. Photosafe/reduced-motion: a 600ms static ember tint instead of a ripple.</summary>
    Task ChargeAsync(FrameworkElement target, CancellationToken ct, int durationMs = 400);
    /// <summary>The possessed outline: thin ember outline + faint tint while the ghost is live, plus the
    /// cursor ember ring (refcounted). Dispose releases both.</summary>
    IDisposable Possess(FrameworkElement target);
    /// <summary>Window-edge ember pulse used by tripwires and rung changes. Never a hard white/crimson
    /// blink when photosafe; <paramref name="strength"/> 0..1.</summary>
    void EdgePulse(double strength);
    /// <summary>True while at least one Possess handle is alive.</summary>
    bool AnyPossessed { get; }
}

/// <summary>Everything an effect may read. Built fresh by the director for each Apply.</summary>
public sealed class PossessionContext
{
    public required IPossessionHost Host { get; init; }
    public required IPossessionAttribution Attribution { get; init; }
    public required PossessionRung Rung { get; init; }
    public required PossessionIntensity Intensity { get; init; }
    public required bool Photosafe { get; init; }
    public required Random Rng { get; init; }
    public required double ElapsedFraction { get; init; }
    public required TimeSpan Remaining { get; init; }
    /// <summary>Ask the warden to name what just happened: (effectId, target DisplayName). The director
    /// routes it to barks (PossessionEffect trigger) - effects never talk to BarkService directly.</summary>
    public required Action<string, string?> Name { get; init; }
}

/// <summary>One haunt. Stateless until Apply; Undo MUST be safe to call when Apply never ran, was
/// cancelled half-way, or already undid itself. Undo restores the real control EXACTLY (transform,
/// opacity, hit-testing, text) - the reassembly exit is just UndoAsync over every live effect.</summary>
public interface IPossessionEffect
{
    string Id { get; }
    PossessionRung MinRung { get; }
    /// <summary>Gentle-intensity effects say Gentle; themed Doki dialogs say FullDoki.</summary>
    PossessionIntensity MinIntensity { get; }
    /// <summary>Big effects (moves, falls, dissolves, retitles, dialogs) are named by the warden;
    /// R0/R1 micro-tics stay silent but still carry charge + tint.</summary>
    bool IsBig { get; }
    /// <summary>Skipped entirely when Photosafe (hard blinks / strobing flicker).</summary>
    bool UsesFlicker { get; }
    /// <summary>Deck weight at the effect's MinRung; the director scales it up per rung above.</summary>
    double Weight { get; }
    /// <summary>How long the haunt stays before the director auto-undoes it. Zero = until exit.</summary>
    TimeSpan HoldFor { get; }
    /// <summary>Roles this effect can take a victim from (empty = no target needed, e.g. title effects).</summary>
    IReadOnlyList<PossessionRole> Roles { get; }
    bool IsLive { get; }
    bool CanApply(PossessionContext ctx, PossessionTarget? target);
    Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct);
    Task UndoAsync(TimeSpan duration);
}

/// <summary>The companion as warden. Implemented by Services/Possession/Warden.cs over the avatar
/// tube's glide API; the director calls it (knock at R3, stare on tripwire repeat 3+ / R4, leave at
/// R4, return on reassembly). Every method must be safe when no avatar window exists (no-op).</summary>
public interface IPossessionWarden
{
    /// <summary>False when the tube is absent, busy, hidden, or on cooldown - the director then skips the verb.</summary>
    bool IsAvailable { get; }
    /// <summary>Glide beside the target, a beat, then the caller drops the target (returns when the knock landed).</summary>
    Task KnockAsync(PossessionTarget target, CancellationToken ct);
    /// <summary>Glide to the window, say one line (reason = tripwire kind or "r4"), linger, go home.</summary>
    Task StareAsync(string reason, CancellationToken ct);
    /// <summary>R4: the tube empties / the companion leaves the frame.</summary>
    Task LeaveAsync(CancellationToken ct);
    /// <summary>Reassembly: the companion comes back to where it was. Must be safe to call twice.</summary>
    Task ReturnAsync(CancellationToken ct);
}

// Director surface other layers code against (implemented in PossessionDirector.cs, exposed as
// App.Possession):
//   PossessionRung CurrentRung { get; }            event Action<PossessionRung>? RungChanged;
//   bool IsHaunting { get; }                        IPossessionWarden? Warden { get; set; }
//   int LiveEffectCount { get; }                    void UndoAll();   // sync, crash-safe
//   void AttachHost(IPossessionHost host);          void DetachHost(IPossessionHost host);

/// <summary>Bark trigger names raised by the Possession layer (BarkService.NotifyPossession*).</summary>
public static class PossessionBarkTriggers
{
    public const string RungChanged = "PossessionRungChanged";   // ctx: rung (0-4)
    public const string Effect = "PossessionEffect";             // ctx: effect (id), target (display name)
    public const string Tripwire = "PossessionTripwire";         // ctx: kind, repeat, total
    public const string Warden = "PossessionWarden";             // ctx: verb (knock|stare|leave|return)
    public const string Rules = "PossessionRules";               // first-run: the warden states the rules
    public const string TimerRestarted = "PossessionTimerRestarted"; // ctx: reason, restart (count) - Emergency Exit sent them back in
    public const string Remember = "PossessionRemember";             // next launch after a Full Doki lockdown: "I remember."
}
