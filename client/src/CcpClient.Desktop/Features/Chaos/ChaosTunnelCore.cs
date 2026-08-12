using System.Text.Json;

namespace CcpClient.Desktop.Features.Chaos;

/// <summary>
/// The tunnel page protocol state machine — the pure, headlessly testable half of the
/// WPF <c>ChaosTunnelService</c> behavior (ConditioningControlPanel/Chaos/ChaosTunnelService.cs):
/// ready latch + pending FIFO queue (`:55`, `:346-354`), streak dedup (`:120-128`), RunAgain
/// re-arm inside the exit window (`:289-294`), and the exit-done fast path vs watchdog force
/// (`:141-163`). JSON shapes match the payload's own switch (`Resources/web/tunnel/main.js:157-190`).
///
/// The <c>sfx</c> frame is HANDLED TYPED (counted, surfaced to the log as presence+shape):
/// the six upstream tunnel_*.mp3 cues exist in the WPF tree but the client has no chaos
/// sound-library port — a greenfield content gap owned by that row, NEVER a claimed WPF
/// parity (the SP-051 falsified framing; consult ruling 4).
/// </summary>
public sealed class ChaosTunnelCore
{
    private readonly List<string> _pending = new();
    private int _lastStreak = -1;

    public bool Ready { get; private set; }
    public bool RunActive { get; private set; }
    public int PendingCount => _pending.Count;
    public long SfxCueCount { get; private set; }

    /// <summary>What the host should do after <see cref="CloseActive"/>.</summary>
    public enum ClosePlan
    {
        /// <summary>Page never ready (or no window) — tear down immediately (WPF `:158-160`).</summary>
        DisposeImmediately,

        /// <summary><c>run-end</c> posted; close on the page's <c>exit-done</c> or the exit
        /// watchdog's force (WPF `:148-157`).</summary>
        RunEndPosted,
    }

    /// <summary>Typed outcome of one page→host frame (WPF `:273-312`).</summary>
    public abstract record PageOutcome
    {
        /// <summary>Page booted; flush these queued host→page frames in order.</summary>
        public sealed record Ready(IReadOnlyList<string> Flush) : PageOutcome;

        /// <summary>Exit animation finished. CloseNow=false means a RunAgain re-armed the
        /// run inside the exit window — cancel the watchdog, keep the window (WPF `:289-294`).</summary>
        public sealed record ExitDone(bool CloseNow) : PageOutcome;

        /// <summary>A one-shot SFX cue (presence+shape only — the cue NAME is a payload
        /// protocol token; the log line never carries a resolved path because nothing
        /// resolves here). Counted in <see cref="SfxCueCount"/>.</summary>
        public sealed record SfxCue(double Scale) : PageOutcome;

        /// <summary>A power-up click (raycast hit) — presence only.</summary>
        public sealed record PowerupClick : PageOutcome;

        /// <summary>A page log line (the payload's own text, e.g. "bloom disabled").</summary>
        public sealed record PageLog(string Message) : PageOutcome;

        /// <summary>A well-formed frame of a type this host does not handle.</summary>
        public sealed record Unknown(string Type) : PageOutcome;

        /// <summary>Unparseable frame — tolerated, typed, never a crash.</summary>
        public sealed record Malformed : PageOutcome;
    }

    /// <summary>Preload (WPF `:66-80`): claim the warm window for a coming run without
    /// posting run-start — the page idles behind its curtain until <see cref="Show"/>.</summary>
    public void Preload() => RunActive = true;

    /// <summary>Show (WPF `:82-99`): fresh run — the streak dedup resets so the first
    /// SetStreak(0) goes through; run-start is queued until the page says ready.</summary>
    public string? Show()
    {
        RunActive = true;
        _lastStreak = -1;
        return PostOrQueue("{\"type\":\"run-start\"}");
    }

    /// <summary>Fall speed follows the pop streak, deduped on combo (WPF `:120-128`).</summary>
    public string? SetStreak(int combo, double mult)
    {
        if (combo == _lastStreak)
        {
            return null;
        }

        _lastStreak = combo;
        return PostOrQueue(JsonSerializer.Serialize(new { type = "streak", combo, mult }));
    }

    public string? SendZoneHint(int depth, double intensity) =>
        PostOrQueue(JsonSerializer.Serialize(new { type = "zone-hint", depth, intensity }));

    public string? SetIntensity(double value) =>
        PostOrQueue(JsonSerializer.Serialize(new { type = "intensity", value }));

    /// <summary>Fullscreen video covers the tunnel — the page pauses its render loop
    /// (main.js:180). The ambient duck is N/A (no ambient bed — named limit).</summary>
    public string? SetVideoPlaying(bool on) =>
        PostOrQueue(JsonSerializer.Serialize(new { type = "video-playing", on }));

    public string? SpawnPowerup(string? id = null, double ahead = 90) =>
        PostOrQueue(JsonSerializer.Serialize(new { type = "spawn-powerup", id, ahead }));

    /// <summary>CloseActive (WPF `:141-163`, idempotent): run-end + bounded wait when the
    /// page is live; immediate teardown otherwise.</summary>
    public (ClosePlan Plan, string? RunEndJson) CloseActive()
    {
        RunActive = false;
        if (!Ready)
        {
            return (ClosePlan.DisposeImmediately, null);
        }

        return (ClosePlan.RunEndPosted, PostOrQueue("{\"type\":\"run-end\"}"));
    }

    /// <summary>One page→host frame through the real parse+dispatch path (WPF `:273-312`).</summary>
    public PageOutcome HandlePageMessage(string body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return new PageOutcome.Malformed();
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("type", out var typeEl)
                || typeEl.ValueKind != JsonValueKind.String)
            {
                return new PageOutcome.Malformed();
            }

            var type = typeEl.GetString()!;
            switch (type)
            {
                case "ready":
                    Ready = true;
                    var flush = _pending.ToArray();
                    _pending.Clear();
                    return new PageOutcome.Ready(flush);
                case "exit-done":
                    // A RunAgain inside the exit window re-arms RunActive — don't kill the
                    // window the new run is about to fade back in (WPF `:289-294`).
                    return new PageOutcome.ExitDone(CloseNow: !RunActive);
                case "sfx":
                    SfxCueCount++;
                    var scale = doc.RootElement.TryGetProperty("scale", out var s)
                        && s.ValueKind == JsonValueKind.Number
                        ? s.GetDouble()
                        : 0.6;
                    return new PageOutcome.SfxCue(scale);
                case "powerup-click":
                    return new PageOutcome.PowerupClick();
                case "log":
                    var msg = doc.RootElement.TryGetProperty("msg", out var m)
                        && m.ValueKind == JsonValueKind.String
                        ? m.GetString() ?? ""
                        : "";
                    return new PageOutcome.PageLog(msg);
                default:
                    return new PageOutcome.Unknown(type);
            }
        }
    }

    private string? PostOrQueue(string json)
    {
        if (Ready)
        {
            return json;
        }

        _pending.Add(json);
        return null;
    }
}
