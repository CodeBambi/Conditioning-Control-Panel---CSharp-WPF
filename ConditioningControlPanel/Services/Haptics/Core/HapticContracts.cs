using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Haptics.Core
{
    // ============================================================================
    // Haptics v2 core contracts. Written by the coordinator; providers, the mixer
    // and the device manager are implemented AGAINST these types. Do not redesign —
    // extend only when genuinely required, and record the change in
    // docs/HAPTICS_OVERHAUL_PLAN.md.
    // ============================================================================

    /// <summary>What a single controllable channel on a toy can do.</summary>
    public enum ActuatorType
    {
        Vibrate,    // 0-20 native on Lovense
        Rotate,     // 0-20
        Thrust,     // 0-20 (Lovense "Thrusting")
        Finger,     // 0-20 (Lovense "Fingering")
        Suction,    // 0-20
        Oscillate,  // 0-20
        Pump,       // 0-3
        Depth,      // 0-3 (Solace)
        Position,   // 0-100 absolute (Solace Pro); coasts ~300ms, 1-2s spin-up
        Stroke,     // 0-100 range pair (with Thrust); span must be >= 20
        Constrict
    }

    /// <summary>One controllable channel. Index disambiguates same-type motors (Edge=2 vibes, Lapis=3).</summary>
    public sealed class HapticActuator
    {
        public ActuatorType Type { get; set; }
        public int Index { get; set; }
        /// <summary>Native resolution (20 for Lovense vibe/rotate/thrust, 3 pump/depth, 100 position).
        /// Providers quantize 0..1 intensities to this and suppress unchanged sends.</summary>
        public int Steps { get; set; } = 20;
    }

    /// <summary>User-assigned function of a toy; the routing matrix targets roles, not toys.</summary>
    public enum ToyRole
    {
        All,      // participates in everything (default)
        Reward,
        Punish,
        Ambient
    }

    /// <summary>A connected (or remembered) device. Identity key for persisted per-toy
    /// config is $"{ProviderKey}:{Id}".</summary>
    public sealed class HapticDevice
    {
        public string Id { get; set; } = "";           // provider-scoped stable id
        public string ProviderKey { get; set; } = "";  // "lovense" | "buttplug" | "mock"
        public string Name { get; set; } = "";         // model name from provider
        public string Nickname { get; set; } = "";     // Lovense nickname or user-set
        public List<HapticActuator> Actuators { get; set; } = new();
        public int? BatteryPercent { get; set; }
        public bool IsConnected { get; set; }

        // User config, persisted by DeviceKey (mirrored from settings by the device manager):
        public ToyRole Role { get; set; } = ToyRole.All;
        public double IntensityTrim { get; set; } = 1.0;  // 0..1 multiplier applied last
        public bool Enabled { get; set; } = true;

        public string DeviceKey => ProviderKey + ":" + Id;
    }

    /// <summary>Latest desired level for one actuator, 0..1. Level-set semantics: the mixer
    /// calls SetOutputsAsync at most ~10Hz per device with the CURRENT target; providers
    /// hold the level until the next call (Lovense: timeSec 0 + keep-alive refresh, or
    /// short-timeSec repeats — provider's choice, documented in the provider).</summary>
    public readonly struct ActuatorOutput
    {
        public ActuatorOutput(ActuatorType type, int index, double intensity)
        {
            Type = type; Index = index; Intensity = intensity;
        }
        public ActuatorType Type { get; }
        public int Index { get; }
        public double Intensity { get; }  // 0..1, already trimmed/capped by the mixer
    }

    public enum ToyEventKind
    {
        ButtonDown,
        ButtonUp,
        ButtonPressed,       // discrete press (Lovense "button-pressed")
        StrengthChanged,     // user changed intensity on toy/app -> mixer should back off
        BatteryChanged,
        Shake,
        MotionChanged
    }

    /// <summary>Two-way input from toys (Lovense Toy Events WS; other providers may never raise these).</summary>
    public sealed class HapticToyEvent : EventArgs
    {
        public string DeviceKey { get; set; } = "";
        public ToyEventKind Kind { get; set; }
        /// <summary>Kind-specific payload: battery %, strength value, etc.</summary>
        public double Value { get; set; }
    }

    /// <summary>v2 provider contract. Multiple providers are connected CONCURRENTLY by the
    /// device manager. Implementations must be thread-safe: SetOutputsAsync is called from
    /// the mixer loop, Connect/Disconnect from the UI thread, events raised from IO threads.</summary>
    public interface IHapticProviderV2 : IDisposable
    {
        /// <summary>Stable lowercase key: "lovense", "buttplug", "mock".</summary>
        string Key { get; }
        string DisplayName { get; }
        bool IsConnected { get; }

        /// <summary>Snapshot copy — callers may enumerate without locking.</summary>
        IReadOnlyList<HapticDevice> Devices { get; }

        event EventHandler? DevicesChanged;
        event EventHandler<HapticToyEvent>? ToyEvent;
        event EventHandler<string>? Error;

        Task<bool> ConnectAsync(CancellationToken ct);
        Task DisconnectAsync();

        /// <summary>Apply the current target levels for one device. Quantize to native steps,
        /// suppress sends when the quantized values are unchanged. Must not throw on device
        /// loss — raise Error/DevicesChanged instead.</summary>
        Task SetOutputsAsync(string deviceId, IReadOnlyList<ActuatorOutput> outputs, CancellationToken ct);

        /// <summary>Immediate all-stop for every device. Bypasses throttles and unchanged-send
        /// suppression. Must be safe to call at any time, including during shutdown.</summary>
        Task StopAllAsync();

        /// <summary>Cheap liveness check that actually touches the wire (IsConnected can lie
        /// after routing changes, e.g. VPN enable).</summary>
        Task<bool> PingAsync();
    }

    // ---------------------------------------------------------------------------
    // Mixer-facing types
    // ---------------------------------------------------------------------------

    /// <summary>Continuous 0..1 sources, combined per actuator-channel by MAX.</summary>
    public enum HapticLayer
    {
        Video,       // background vibe while a video plays
        AudioSync,   // AudioSyncService envelope
        Luminance,   // flash/subliminal brightness sync (Phase F)
        Dtrh,        // DtrhHapticDirector ambient depth
        Session,     // session ramp (future)
        Manual,      // live slider / test
        /// <summary>Authored keyframe envelopes (Deeper runtime patterns + its editor preview,
        /// i.e. HapticService.SetSyncPatternAsync). Added in Phase A — see
        /// docs/HAPTICS_OVERHAUL_PLAN.md "Contract extensions". It needs its own layer so a
        /// Deeper pattern cannot stomp the AudioSync or Manual layer.</summary>
        Pattern
    }

    /// <summary>Semantic transient events; the routing matrix maps each to enabled +
    /// intensity + pattern + target role. Names are stable — they become settings keys.</summary>
    public enum HapticEventKind
    {
        FlashClick,
        FlashDecay,
        SubliminalTrigger,
        KeywordTrigger,
        BubblePop,
        BouncingTextBounce,
        BlinkPulse,
        VideoTargetHit,
        Achievement,
        QuestComplete,
        LevelUp,
        GazeReward,
        AvatarEasterEgg,
        AiCommand,
        DtrhAccent,
        ToyButtonReward   // Phase F: reward for toy-button interactions
    }

    /// <summary>A transient envelope layered over the continuous floor. Higher Priority wins
    /// when the concurrency cap is hit; equal priorities coexist (summed then capped).</summary>
    public readonly struct HapticPulse
    {
        public HapticPulse(double intensity, int attackMs, int holdMs, int decayMs,
                           int priority = 0, ToyRole target = ToyRole.All)
        {
            Intensity = intensity; AttackMs = attackMs; HoldMs = holdMs;
            DecayMs = decayMs; Priority = priority; Target = target;
        }
        public double Intensity { get; }
        public int AttackMs { get; }
        public int HoldMs { get; }
        public int DecayMs { get; }
        public int Priority { get; }
        public ToyRole Target { get; }
    }
}
