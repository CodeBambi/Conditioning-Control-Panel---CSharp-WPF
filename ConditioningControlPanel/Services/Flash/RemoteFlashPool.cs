using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services;

// The caller holds its pool lock. A selection generation rejects stale downloads.
internal sealed class RemoteFlashPool
{
    private readonly List<string> _ready = new();
    private string _selection = "";
    internal int Generation { get; private set; }
    internal int Count => _ready.Count;
    internal bool Select(IEnumerable<string> channels)
    {
        var key = string.Join("|", channels.Select(x => x.ToLowerInvariant()).Distinct().OrderBy(x => x));
        if (key == _selection) return false;
        _selection = key;
        Generation++;
        _ready.Clear();
        return true;
    }
    internal bool Add(int generation, string url)
    {
        if (generation != Generation || _ready.Contains(url)) return false;
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
            if (usable(url)) return url;
        }
        return null;
    }
    internal void Clear() { Generation++; _ready.Clear(); }
}
