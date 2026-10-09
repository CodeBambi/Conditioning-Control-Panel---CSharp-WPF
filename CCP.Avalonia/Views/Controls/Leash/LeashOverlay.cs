// PORTED from WPF 7.1.5 Controls/Leash/LeashSurfaces.cs (Open / CloseOverlay) + LeashOverlayWindow:
// ONE borderless transparent overlay window over the owner for the ask card and the snap card.
// Opening a new card closes the last one; Escape runs the card's own "later" / close.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal static class LeashOverlay
{
    private static Window? _overlay;

    /// <summary>Test seam: receives every card instead of a window being shown.</summary>
    internal static Action<Control>? ShowOverride { get; set; }

    /// <summary>The card up now (null when nothing is open).</summary>
    internal static Control? Current { get; private set; }

    /// <summary>WPF LeashSurfaces.Me: this account as the snap draws it.</summary>
    internal static LeashPerson Me()
    {
        string name;
        try
        {
            var n = CoreSettings.Current?.UserDisplayName;
            name = string.IsNullOrWhiteSpace(n) ? Loc.Get("leash_you") : n!.Trim();
        }
        catch { name = "you"; }
        return new LeashPerson("me", name, null);
    }

    internal static void Open(Control card, Action onEscape, Window? owner)
    {
        CloseQuiet();
        Current = card;
        // WPF LeashOverlayWindow.Loaded: the card lands with a small THUD.
        card.AttachedToVisualTree += (_, _) => { if (LeashFx.Amount > 0) LeashFx.Thud(card, 1.08); };
        if (ShowOverride is { } o) { o(card); return; }
        var w = new Window
        {
            WindowDecorations = WindowDecorations.None,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Background = Brushes.Transparent,
            CanResize = false,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Title = Loc.Get("leash_title"),
            Content = new Grid { Margin = new Thickness(40), Children = { card } },
        };
        w.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            onEscape();
        };
        _overlay = w;
        try
        {
            if (owner is { IsVisible: true }) w.Show(owner); else w.Show();
            w.Activate();
        }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] overlay failed: {E}", ex.Message); _overlay = null; }
    }

    /// <summary>WPF CloseOverlay: closes the card, then the next waiting ask may show.</summary>
    internal static void Close()
    {
        CloseQuiet();
        LeashSurfaces.OverlayClosed();
    }

    private static void CloseQuiet()
    {
        var o = _overlay;
        _overlay = null;
        Current = null;
        try
        {
            if (o?.Content is Panel p) p.Children.Clear();   // the card may be reparented by the next Open
            o?.Close();
        }
        catch { }
    }
}
