using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>
/// "This spin was real" for Circe's tab (CONTRACT 10.24). A free demo spin fires the same
/// <c>fx.melt</c> and <c>fx.jackpot</c> as a paid one, so the tab can never book off an effect.
/// It books off the SERVER'S OWN outcomes instead: every slot tape and freeze the host relays is
/// written down here as it passes, and a page <c>landed</c> frame only names which of those
/// outcomes just played. The line comes from the host's copy, never from the page, so a page can
/// at worst book an outcome the server really dealt, once, in the order it chooses.
///
/// <para>Rules: an outcome books at most once per window; a main-tape outcome the server already
/// counted as played when it was relayed (a resumed tape) never books again; at most
/// <see cref="MaxPerSecond"/> landings are read in any rolling second. No WPF, no clock of its
/// own: the bridge passes the time in.</para>
/// </summary>
public sealed class BackRoomTabLedger
{
    public const string Slot = "slot";
    /// <summary>A reel stops about every three seconds; four a second is already a page misbehaving.</summary>
    public const int MaxPerSecond = 4;
    /// <summary>Tapes remembered per window. A tape is at most 20 spins and a sit-down buys a few.</summary>
    public const int MaxTapes = 8;
    public const int MaxOutcomes = 64;

    private sealed class Tape
    {
        public required string[] Lines;
        public int Played;
        public readonly HashSet<int> Landed = new();
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Tape> _tapes = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();
    private Tape? _side;
    private readonly Queue<DateTime> _recent = new();

    /// <summary>A station result the host just relayed. Only the slot's tapes and freezes are kept;
    /// a refusal body counts too (<c>tape_unplayed</c> carries the stored tape).</summary>
    public void Relayed(string station, JToken? body)
    {
        if (station != Slot || body is not JObject) return;
        lock (_gate)
        {
            if (body["tape"] is JObject tape && (string?)tape["id"] is { Length: > 0 and <= 64 } id
                && Lines(tape["outcomes"]) is { } lines)
            {
                var played = tape["played"]?.Type == JTokenType.Integer ? Math.Max(0, (int)tape["played"]!) : 0;
                if (_tapes.TryGetValue(id, out var known))
                    known.Played = Math.Max(known.Played, played);
                else
                {
                    _tapes[id] = new Tape { Lines = lines, Played = played };
                    _order.Add(id);
                    while (_order.Count > MaxTapes) { _tapes.Remove(_order[0]); _order.RemoveAt(0); }
                }
            }
            // A freeze tape (10.10) drains before the main one and only one is ever in play.
            if (body["freeze"] is JObject freeze && Lines(freeze["outcomes"]) is { } side)
                _side = new Tape { Lines = side };
        }
    }

    /// <summary>The page says outcome <paramref name="i"/> of a main tape (or of the freeze in play,
    /// <paramref name="side"/>) just landed. Returns that outcome's server line the first time, else null.</summary>
    public string? Land(string station, string? tapeId, bool side, int i, DateTime nowUtc)
    {
        if (station != Slot || i < 0) return null;
        lock (_gate)
        {
            while (_recent.Count > 0 && nowUtc - _recent.Peek() >= TimeSpan.FromSeconds(1)) _recent.Dequeue();
            if (_recent.Count >= MaxPerSecond) return null;
            _recent.Enqueue(nowUtc);

            var tape = side ? _side : tapeId != null && _tapes.TryGetValue(tapeId, out var t) ? t : null;
            if (tape == null || i >= tape.Lines.Length || (!side && i < tape.Played)) return null;
            return tape.Landed.Add(i) ? tape.Lines[i] : null;
        }
    }

    private static string[]? Lines(JToken? outcomes)
    {
        if (outcomes is not JArray a || a.Count == 0 || a.Count > MaxOutcomes) return null;
        return a.Select(o => o is JObject j && j["line"] is JValue { Type: JTokenType.String } l ? (string)l! : "none").ToArray();
    }
}
