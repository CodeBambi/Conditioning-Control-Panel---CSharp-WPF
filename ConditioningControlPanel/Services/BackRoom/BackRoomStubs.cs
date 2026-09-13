using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.BackRoom;

// Null objects for the two seams C3 (fx) and C4 (media) fill in. The host runs on these until those
// lanes land, and they are what the room falls back to if a real implementation throws: a page that
// fires effects is told honestly that nothing played, and a sit-down always gets a full deal.

/// <summary>Acks every effect as skipped <c>unknown</c>. Plays nothing, cancels nothing.</summary>
public sealed class NullBackRoomFx : IBackRoomFx
{
    public BackRoomFxAck Fire(string fxId, string station, IReadOnlyList<string> symbolKeys, BackRoomMediaDeal deal) =>
        new(Array.Empty<string>(), new[] { new BackRoomFxSkip(string.IsNullOrEmpty(fxId) ? "?" : fxId, BackRoomFxSkipReason.Unknown) });

    public void CancelAll() { }
}

/// <summary>Deals the fallback presets only: four built-in GIF slots and the four preset words
/// (CONTRACT section 5, in that order), with preset text resolved through the lexicon.</summary>
public sealed class NullBackRoomMedia : IBackRoomMedia
{
    /// <summary>Preset words, in contract order, as (lexicon key, neutral fallback).</summary>
    public static readonly (string Key, string Fallback)[] Presets =
    {
        ("br_preset_drop", "Drop"), ("br_preset_relax", "Relax"), ("br_preset_let_go", "Let Go"), ("br_preset_sink", "Sink"),
    };

    private readonly Func<string, string>? _lex;

    /// <param name="lex">Lexicon lookup returning the key itself on a miss (Loc.Get's contract). Null = fallbacks.</param>
    public NullBackRoomMedia(Func<string, string>? lex) => _lex = lex;

    public BackRoomMediaDeal Deal(string station, int seed)
    {
        var gifs = Enumerable.Range(0, 4)
            .Select(i => new BackRoomGif("g" + i, $"https://ccp.game/backroom/stations/slot/fallback/gif{i}.webp", 0, 0, "fallback"))
            .ToList();
        var words = Presets.Select((p, i) => new BackRoomWord("s" + i, Word(p.Key, p.Fallback), "preset")).ToList();
        return new BackRoomMediaDeal(seed, gifs, words);
    }

    private string Word(string key, string fallback)
    {
        try
        {
            var s = _lex?.Invoke(key);
            return string.IsNullOrWhiteSpace(s) || s == key ? fallback : s;
        }
        catch { return fallback; }
    }
}
