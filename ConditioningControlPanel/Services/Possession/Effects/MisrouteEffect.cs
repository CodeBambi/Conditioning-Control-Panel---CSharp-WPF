using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R2 "misroute" - the next door you open is not the door you opened. The effect arms itself on the
/// nav rail and the FIRST door click after that lands somewhere else: the room you asked for is not
/// the room you get, once, and then the doors behave again.
///
/// <para><b>The seam.</b> <c>MainWindow.PossessionReroute</c> (declared in MainWindow.NavRail.cs) is a
/// single nullable delegate the rail consults on a door press. It knows nothing about Possession: it
/// takes the door key that was pressed and returns the key to open instead, or null for "carry on".
/// This effect is the only thing that ever sets it, and it clears it the moment it fires - a hook that
/// outlived its effect would be a rail that misroutes forever.</para>
///
/// <para><b>Why the ember arrives with the wrong room rather than before it.</b> A press has to feel
/// instant; holding the navigation back for a 400 ms ripple would read as the app hanging, which is the
/// one thing the haunt may never impersonate. So the reroute returns immediately and the charge fires
/// over the door that was pressed as the wrong page paints - the tell lands on the door you touched,
/// while you are still looking at it.</para>
/// </summary>
public sealed class MisrouteEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.TabHeader };

    /// <summary>The Web App door is a launcher, not a destination: it opens a browser. Rerouting TO it
    /// would take the user out of the app entirely, which is a different (and much worse) joke.</summary>
    private const string LauncherDoor = "webapp";

    private FrameworkElement? _door;
    private string? _fromDoor;
    private string? _toDoor;
    private Func<string, string?>? _hook;
    private bool _fired;

    public override string Id => "misroute";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => true;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(20);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    /// <summary>Nothing moves until a door is pressed, so nothing is charged or named until then
    /// either: an ember ripple on an armed door would give the whole trick away.</summary>
    protected override bool ChargeOnApply => false;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (MainWindow.PossessionReroute != null) return false;     // one misroute at a time
        var from = DoorKeyOf(target?.Element);
        if (string.IsNullOrEmpty(from)) return false;
        return PickOtherDoor(ctx, from!) != null;
    }

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        _door = target?.Element;
        _fromDoor = DoorKeyOf(_door);
        if (string.IsNullOrEmpty(_fromDoor)) return Task.CompletedTask;

        _toDoor = PickOtherDoor(ctx, _fromDoor!);
        if (_toDoor == null) return Task.CompletedTask;

        _hook = Reroute;
        MainWindow.PossessionReroute = _hook;
        return Task.CompletedTask;
    }

    /// <summary>Runs on the rail's press handler, on the UI thread, and must return NOW. Everything
    /// with a duration (the ripple, the bark, the self-undo) is started and left to run.</summary>
    private string? Reroute(string pressedDoor)
    {
        try
        {
            if (_fired || _toDoor == null) return null;

            // Whichever door was pressed, send them somewhere that is not it.
            var to = string.Equals(pressedDoor, _toDoor, StringComparison.OrdinalIgnoreCase)
                ? (_fromDoor != null && !string.Equals(pressedDoor, _fromDoor, StringComparison.OrdinalIgnoreCase)
                    ? _fromDoor
                    : null)
                : _toDoor;
            if (to == null) return null;

            _fired = true;
            ClearHook();

            var pressed = DoorElement(pressedDoor) ?? _door;
            NameOverrideText = DisplayNameOf(pressed) ?? Target?.DisplayName;

            // Charge + name + the possessed outline, over the door they actually pressed.
            if (pressed != null) _ = ChargeAndPossessAsync(pressed, Cts?.Token ?? CancellationToken.None);

            // The joke is over the moment it lands; let the ripple finish, then put everything back.
            _ = SelfUndoAsync();
            return to;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Possession misroute failed: {Error}", ex.Message);
            return null;
        }
    }

    private async Task SelfUndoAsync()
    {
        try
        {
            await Task.Delay(1400).ConfigureAwait(true);
            if (Application.Current?.Dispatcher == null
                || Application.Current.Dispatcher.HasShutdownStarted) return;
            await UndoAsync(TimeSpan.FromMilliseconds(300)).ConfigureAwait(true);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession misroute self-undo failed: {Error}", ex.Message); }
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        ClearHook();
        _door = null;
        _fromDoor = null;
        _toDoor = null;
        _fired = false;
        return Task.CompletedTask;
    }

    private void ClearHook()
    {
        try
        {
            // Only ever take back OUR hook: a later effect may already own the seam.
            if (_hook != null && ReferenceEquals(MainWindow.PossessionReroute, _hook))
                MainWindow.PossessionReroute = null;
        }
        catch { }
        _hook = null;
    }

    // ---- doors ---------------------------------------------------------------------------------

    /// <summary>The rail states a door's key in its Tag, and NavDoor_Click matches on exactly that -
    /// so the Tag is the door's identity, and reading it is how this effect stays owner-agnostic.</summary>
    private static string? DoorKeyOf(FrameworkElement? el)
    {
        try
        {
            var key = (el as FrameworkElement)?.Tag as string;
            if (string.IsNullOrWhiteSpace(key)) return null;
            if (string.Equals(key, LauncherDoor, StringComparison.OrdinalIgnoreCase)) return null;
            return key;
        }
        catch { return null; }
    }

    private string? PickOtherDoor(PossessionContext ctx, string from)
    {
        try
        {
            var doors = new List<string>();
            foreach (var t in ctx.Host.Targets)
            {
                if (t.Role != PossessionRole.TabHeader) continue;
                var key = DoorKeyOf(t.Element);
                if (key == null) continue;
                if (string.Equals(key, from, StringComparison.OrdinalIgnoreCase)) continue;
                if (!doors.Contains(key, StringComparer.OrdinalIgnoreCase)) doors.Add(key);
            }
            if (doors.Count == 0) return null;
            return doors[Rng.Next(doors.Count)];
        }
        catch { return null; }
    }

    private FrameworkElement? DoorElement(string doorKey)
    {
        try
        {
            var host = Ctx?.Host;
            if (host == null) return null;
            foreach (var t in host.Targets)
            {
                if (t.Role != PossessionRole.TabHeader) continue;
                if (string.Equals(DoorKeyOf(t.Element), doorKey, StringComparison.OrdinalIgnoreCase))
                    return t.Element;
            }
        }
        catch { }
        return null;
    }

    private string? DisplayNameOf(FrameworkElement? el)
    {
        try
        {
            if (el == null) return null;
            var name = Possession.GetName(el);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch { return null; }
    }
}
