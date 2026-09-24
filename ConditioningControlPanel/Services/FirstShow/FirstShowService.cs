using System;
using System.Windows;

namespace ConditioningControlPanel.Services.FirstShow;

/// <summary>Owns a disposable show using the player's actual desktop Emi.</summary>
internal static class FirstShowService
{
    private static FirstShowDesktopWindow? _window;
    public static bool IsActive => _window != null;
    public static void Open(Window? owner = null, bool preview = false)
    {
        if (_window != null) { _window.Activate(); return; }
        var desk = App.EmiDesk?.BeginPresentation();
        if (desk == null) return;
        var window = new FirstShowDesktopWindow(preview, desk, owner as MainWindow ?? Application.Current.MainWindow as MainWindow);
        _window = window;
        if (preview) Application.Current.MainWindow = window;
        if (owner?.IsVisible == true) window.Owner = owner;
        window.Closing += (_, _) => desk.EndPresentation();
        window.Closed += (_, _) => { _window = null; if (preview) desk.ShutDown(); };
        try { window.Show(); desk.BeginPresentation(window, () => Stop()); window.BeginEntrance(); }
        catch { window.Close(); desk.EndPresentation(); _window = null; throw; }
    }
    public static bool Stop()
    {
        if (_window == null) return false;
        var window = _window; _window = null; window.Close(); return true;
    }
}
