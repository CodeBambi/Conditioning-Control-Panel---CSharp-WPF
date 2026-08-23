using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ConditioningControlPanel.Services.Possession;

// =====================================================================================================
//  PossessionEvents (B15) - the haunt REACTS.
//
//  The cadence ladder is a metronome: it fires every N seconds whether or not anyone is in the room.
//  That is what made the owner's first live run read as random glitching rather than as being watched -
//  nothing the user DID ever caused anything. This adapter closes that loop: click a card and it
//  breathes, change a setting and the label next to your cursor mis-types itself, reach for Stop and it
//  steps aside, open a door and a letter falls out of it.
//
//  It owns no choreography of its own. Every reaction goes through PossessionDirector.RequestReactive,
//  which applies the throttle (6 s), the concurrency cap, the rung gate and the per-target cooldown -
//  so this file can be as trigger-happy as the UI is chatty without ever being able to strobe the room.
//
//  Lifetime: subscribed from AttachHost, unsubscribed from DetachHost. Every handler is defensive; an
//  exception thrown out of a WPF input or PropertyChanged handler would take down the app, which is a
//  spectacularly bad way for a joke about a haunted UI to end.
// =====================================================================================================
public sealed class PossessionEvents
{
    private PossessionDirector? _director;
    private readonly List<IPossessionHost> _hosts = new();
    private INotifyPropertyChanged? _settings;
    private bool _hooked;

    public void Attach(PossessionDirector director, IPossessionHost host)
    {
        if (director == null || host == null) return;
        _director = director;
        if (!_hosts.Contains(host)) _hosts.Add(host);
        if (_hooked) return;
        _hooked = true;

        try
        {
            PossessionPointer.Pressed += OnPressed;
            PossessionPointer.HoverChanged += OnHoverChanged;

            _settings = App.Settings?.Current as INotifyPropertyChanged;
            if (_settings != null) _settings.PropertyChanged += OnSettingChanged;

            App.Logger?.Debug("Possession: reactive adapter armed");
        }
        catch (Exception ex) { App.Logger?.Warning("Possession: reactive adapter arm failed: {Error}", ex.Message); }
    }

    public void Detach(IPossessionHost host)
    {
        if (host != null) _hosts.Remove(host);
        if (_hosts.Count == 0) DetachAll();
    }

    public void DetachAll()
    {
        _hosts.Clear();
        _director = null;
        if (!_hooked) return;
        _hooked = false;
        try
        {
            PossessionPointer.Pressed -= OnPressed;
            PossessionPointer.HoverChanged -= OnHoverChanged;
            if (_settings != null) _settings.PropertyChanged -= OnSettingChanged;
        }
        catch { }
        _settings = null;
    }

    // ---------------------------------------------------------------------------------------------
    //  Triggers
    // ---------------------------------------------------------------------------------------------

    /// <summary>A press. A card answers by breathing (R0+); a nav door drops a letter (R3+, where
    /// letters falling out of things is already the ladder's vocabulary).</summary>
    private void OnPressed(FrameworkElement el)
    {
        var director = _director;
        if (director == null || el == null) return;
        try
        {
            // The door first: it is the more specific claim, and a door usually sits inside a card.
            var door = director.TargetFor(el, PossessionRole.TabHeader);
            if (door != null)
            {
                director.RequestReactive("drop", door, PossessionRung.Collapse);
                return;
            }

            var card = director.TargetFor(el, PossessionRole.Card);
            if (card != null) director.RequestReactive("breathe", card);
        }
        catch (Exception ex) { App.Logger?.Debug("Possession reactive press failed: {Error}", ex.Message); }
    }

    /// <summary>Reaching for Stop (or Start) is the most loaded gesture in the app during a lockdown,
    /// so that is the one button that steps aside as the cursor arrives. R1+, where the ladder has
    /// already established that things move.</summary>
    private void OnHoverChanged(FrameworkElement? el)
    {
        var director = _director;
        if (director == null || el == null) return;
        try
        {
            if (el is not ButtonBase || el is ToggleButton) return;
            if (!LooksLikeStartStop(el)) return;

            var target = director.TargetFor(el, PossessionRole.Button);
            if (target != null) director.RequestReactive("dodge", target, PossessionRung.Drift);
        }
        catch (Exception ex) { App.Logger?.Debug("Possession reactive hover failed: {Error}", ex.Message); }
    }

    /// <summary>Any setting flip. The label nearest the cursor is the one the user was reading when
    /// they did it, which makes the mis-typing land as an answer rather than as a coincidence.</summary>
    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        var director = _director;
        if (director == null) return;
        try
        {
            // The Possession settings themselves are exempt: reacting to "the user just turned the
            // haunt down" by haunting them is the one joke that reads as the app ignoring consent.
            var name = e?.PropertyName ?? "";
            if (name.StartsWith("LockdownPossession", StringComparison.Ordinal)
                || name.StartsWith("LockdownPhotosafe", StringComparison.Ordinal)
                || name.StartsWith("LockdownTripwires", StringComparison.Ordinal)
                || name.StartsWith("LockdownWarden", StringComparison.Ordinal))
                return;

            var label = director.NearestTarget(PossessionRole.Label);
            if (label != null) director.RequestReactive("typo", label, PossessionRung.Drift);
        }
        catch (Exception ex) { App.Logger?.Debug("Possession reactive setting failed: {Error}", ex.Message); }
    }

    /// <summary>Name or caption says Start / Stop. Deliberately loose (the label is localized, the
    /// x:Name is not) - a false positive here costs one dodge on a button that was not the scary one.</summary>
    private static bool LooksLikeStartStop(FrameworkElement el)
    {
        try
        {
            var name = el.Name ?? "";
            if (name.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            if (el is ContentControl cc && cc.Content is string s)
            {
                if (s.IndexOf("start", StringComparison.OrdinalIgnoreCase) >= 0
                    || s.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            var tag = Possession.GetName(el);
            return tag.IndexOf("start", StringComparison.OrdinalIgnoreCase) >= 0
                   || tag.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { return false; }
    }
}
