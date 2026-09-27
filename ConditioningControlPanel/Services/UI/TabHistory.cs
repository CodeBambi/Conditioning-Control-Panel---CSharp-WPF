using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.UI;

/// <summary>
/// Back and forward between tabs, the browser way (mouse side buttons, Alt+Left / Alt+Right).
/// Tab keys are opaque strings: this class never interprets them, it only remembers the order.
/// Pure, so it is testable without WPF.
/// </summary>
public sealed class TabHistory
{
    public const int DefaultCap = 30;

    private readonly LinkedList<string> _back = new();
    private readonly Stack<string> _forward = new();
    private readonly int _cap;

    public TabHistory(string? current = null, int cap = DefaultCap)
    {
        _cap = Math.Max(1, cap);
        Current = string.IsNullOrEmpty(current) ? null : current;
    }

    /// <summary>The tab on screen, as far as the history knows.</summary>
    public string? Current { get; private set; }

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public int BackCount => _back.Count;
    public int ForwardCount => _forward.Count;

    /// <summary>
    /// A fresh navigation. Pushes the tab we leave and clears the forward stack. Showing the tab
    /// already on screen changes nothing (a re-click must not eat the forward stack).
    /// </summary>
    public void Navigate(string? tab)
    {
        if (string.IsNullOrEmpty(tab)) return;
        if (string.Equals(tab, Current, StringComparison.Ordinal)) return;
        if (Current != null)
        {
            _back.AddLast(Current);
            while (_back.Count > _cap) _back.RemoveFirst();
        }
        _forward.Clear();
        Current = tab;
    }

    /// <summary>Steps back. Returns the tab to show, or null when there is nowhere to go.</summary>
    public string? Back()
    {
        if (_back.Count == 0) return null;
        var target = _back.Last!.Value;
        _back.RemoveLast();
        if (Current != null) _forward.Push(Current);
        Current = target;
        return target;
    }

    /// <summary>Steps forward. Returns the tab to show, or null when there is nowhere to go.</summary>
    public string? Forward()
    {
        if (_forward.Count == 0) return null;
        var target = _forward.Pop();
        if (Current != null)
        {
            _back.AddLast(Current);
            while (_back.Count > _cap) _back.RemoveFirst();
        }
        Current = target;
        return target;
    }
}
