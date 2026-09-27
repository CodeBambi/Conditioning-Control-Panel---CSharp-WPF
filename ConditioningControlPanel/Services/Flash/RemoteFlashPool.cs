using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services;

// The caller holds its pool lock. A selection generation rejects stale downloads.
// Fresh clips are consumed on draw so prefetch keeps rotating channels. Every drawn clip moves to
// the SHOWN list; once fresh runs dry a burst reuses the least recently shown clip instead of
// shrinking to one picture (a burst of 8 against a 12-clip warm pool used to show 0-1 pictures).
internal sealed class RemoteFlashPool
{
    internal const int ShownMax = 256;

    private readonly List<string> _ready = new();
    private readonly LinkedList<string> _shown = new();
    private string _selection = "";
    internal int Generation { get; private set; }
    internal int Count => _ready.Count;
    internal int Available => _ready.Count + _shown.Count;
    internal bool Select(IEnumerable<string> channels)
    {
        var key = string.Join("|", channels.Select(x => x.ToLowerInvariant()).Distinct().OrderBy(x => x));
        if (key == _selection) return false;
        _selection = key;
        Clear();
        return true;
    }
    internal bool Add(int generation, string url)
    {
        if (generation != Generation || _ready.Contains(url)) return false;
        _shown.Remove(url);
        _ready.Add(url);
        return true;
    }
    internal string? Take(Random random, Func<string, bool> usable)
    {
        while (_ready.Count > 0)
        {
            int index = random.Next(_ready.Count);
            var url = _ready[index];
            _ready.RemoveAt(index);
            if (!usable(url)) continue;
            Remember(url);
            return url;
        }
        return null;
    }
    /// <summary>Least recently shown clip that is still usable; it moves to the back.</summary>
    internal string? TakeShown(Func<string, bool> usable)
    {
        while (_shown.First is { } node)
        {
            _shown.RemoveFirst();
            if (!usable(node.Value)) continue;
            _shown.AddLast(node);
            return node.Value;
        }
        return null;
    }
    internal void Clear() { Generation++; _ready.Clear(); _shown.Clear(); }

    private void Remember(string url)
    {
        _shown.Remove(url);
        _shown.AddLast(url);
        while (_shown.Count > ShownMax) _shown.RemoveFirst();
    }
}
