using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.FirstShow;

// Media rotation and screen positions are separate choices, so pictures never own a fixed slot.
internal sealed class FirstShowDeck(int count, int? seed = null)
{
    private readonly Random _random = seed.HasValue ? new Random(seed.Value) : new Random();
    private readonly int[] _lastUse = new int[count];
    private readonly int[] _lastSlot = Enumerable.Repeat(-1,count).ToArray();
    private int _turn;
    internal (int Media, int Slot)? Next(IReadOnlyList<(int Media, int Slot)> visible)
    {
        var available = Enumerable.Range(0,count).Where(i => !visible.Any(v => v.Media == i))
            .OrderBy(i => _lastUse[i]).ThenBy(_ => _random.Next()).ToArray();
        if (available.Length == 0) return null;
        int media = available[0];
        int slot = Enumerable.Range(0,8).Where(i => i != _lastSlot[media])
            .OrderBy(i => visible.Count(v => v.Slot == i)).ThenBy(_ => _random.Next()).First();
        _lastUse[media] = ++_turn; _lastSlot[media] = slot;
        return (media,slot);
    }
}
