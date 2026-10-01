using System;
using System.Windows;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Compositor;

namespace ConditioningControlPanel.Services.Super;

/// <summary>
/// Owns Super Creep's on/off: the fog runs while the overlay engine runs AND the Creep switch is on
/// (it does not need the pink filter itself; both may run). Rides the overlay service's lifecycle,
/// so the engine stop that panic, Lockdown's end and the emergency exit all go through takes it down
/// at once; <see cref="Stop"/> is also called straight from the panic stop pass.
/// Without the unified overlay host there is no layer to draw on, so Creep simply does not show.
/// </summary>
public static class CreepController
{
    private static CreepLayer? _layer;
    private static GlobalMouseHook? _hook;
    private static bool _subscribed;

    /// <summary>Is the fog on screen right now.</summary>
    public static bool IsShowing => _layer?.IsActive == true;

    /// <summary>Show or hide to match the engine and the switch. Any thread.</summary>
    public static void Sync()
    {
        if (!_subscribed)
        {
            _subscribed = true;
            SuperAccess.Changed += e => { if (e == SuperEffect.Creep) Sync(); };
        }
        DispatcherHelper.RunOnUI(() =>
        {
            try
            {
                var want = App.Overlay?.IsRunning == true
                           && App.CompositorEnabled
                           && SuperAccess.IsOn(SuperEffect.Creep);
                if (want) Show();
                else Stop();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Creep: sync failed");
            }
        });
    }

    /// <summary>Gone now. Safe to call any number of times, from the UI thread.</summary>
    public static void Stop()
    {
        try { _hook?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _hook = null;
        if (_layer?.IsActive == true)
        {
            _layer.Hide();
            App.Logger?.Debug("Creep: off");
        }
    }

    private static void Show()
    {
        if (_layer?.IsActive == true) return;
        if (_layer == null)
        {
            _layer = new CreepLayer(App.Compositor!);
            App.Compositor!.RegisterLayer(_layer);
        }
        _layer.Show();
        StartHook();
        App.Logger?.Debug("Creep: on");
    }

    private static void StartHook()
    {
        if (_hook != null) return;
        try
        {
            // Notification only: never swallows, so every click still lands where it was aimed.
            _hook = new GlobalMouseHook
            {
                LeftDown = OnDown,
                RightDown = OnDown,
            };
            _hook.Start();
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Creep hook: {E}", ex.Message);
            _hook = null;
        }
    }

    /// <summary>HOOK THREAD: hand the click to the UI thread and let it pass through.</summary>
    private static bool OnDown(Point px)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return false;
        disp.BeginInvoke(new Action(() => _layer?.Click(px.X, px.Y)));
        return false;
    }
}
