using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession;

// =====================================================================================================
//  PossessionPointer - "where the user is looking", as far as a mouse can tell us.
//
//  Wave 2 (density) needs the haunt to happen WHERE the user is, not wherever the deck's dice land:
//  a nudge on a card at the other end of the window is a tree falling in an empty forest. The deck's
//  proximity option and the event-driven ghosts both read this static, so it is deliberately a plain
//  static rather than a service - it is written from a PreviewMouseMove handler dozens of times a
//  second and read from the director's pick, and neither path should pay for a lookup or a lock.
//
//  Everything here is UI-thread only (WPF input events and hit-testing are), so no synchronisation.
//  Every value is a HINT: stale coordinates only cost the deck one slightly-off pick.
// =====================================================================================================
public static class PossessionPointer
{
    /// <summary>Last cursor position in the attached window's coordinates.</summary>
    public static Point Position;

    /// <summary>Cursor velocity in px per second, smoothed. Used by predictive dodges (Effects wave).</summary>
    public static Vector Velocity;

    /// <summary>Nearest interesting element under the cursor (a Possession-tagged element, or an
    /// interactive control type). Null when the cursor is over dead chrome.</summary>
    public static FrameworkElement? Hovered;

    /// <summary>Nearest interesting element the user last pressed. Survives the cursor moving away,
    /// which is the point: "the thing they were just doing" outlives "the thing under the mouse".</summary>
    public static FrameworkElement? LastClicked;

    /// <summary>When <see cref="LastClicked"/> was set. Readers age it out themselves.</summary>
    public static DateTime LastClickAt = DateTime.MinValue;

    /// <summary>Raised on every press over an interesting element, before <see cref="LastClicked"/>
    /// consumers get a chance to look. PossessionEvents subscribes for reactive ghosts.</summary>
    public static event Action<FrameworkElement>? Pressed;

    /// <summary>Raised when the hovered element CHANGES (not on every mouse move).</summary>
    public static event Action<FrameworkElement?>? HoverChanged;

    private static Window? _window;
    private static DateTime _lastMoveAt = DateTime.MinValue;

    /// <summary>Hook a window's preview input. Idempotent per window; attaching a second window
    /// replaces the first (only one room is haunted at a time in Phase 1).</summary>
    public static void Attach(Window w)
    {
        if (w == null) return;
        try
        {
            if (ReferenceEquals(_window, w)) return;
            Detach();
            _window = w;
            w.PreviewMouseMove += OnMove;
            w.PreviewMouseDown += OnDown;
            w.Closed += OnClosed;
        }
        catch (Exception ex) { App.Logger?.Warning("PossessionPointer attach failed: {Error}", ex.Message); }
    }

    public static void Detach()
    {
        var w = _window;
        _window = null;
        if (w == null) return;
        try
        {
            w.PreviewMouseMove -= OnMove;
            w.PreviewMouseDown -= OnDown;
            w.Closed -= OnClosed;
        }
        catch { }
        Hovered = null;
        LastClicked = null;
        Velocity = default;
    }

    private static void OnClosed(object? sender, EventArgs e) => Detach();

    private static void OnMove(object sender, MouseEventArgs e)
    {
        try
        {
            if (_window == null) return;
            var p = e.GetPosition(_window);
            var now = DateTime.Now;
            var dt = (now - _lastMoveAt).TotalSeconds;
            if (dt > 0.001 && dt < 0.5)
            {
                // Exponential smoothing: raw frame-to-frame velocity is noise, and a dodge that reads
                // it literally jitters. 0.35 keeps a flick readable while ignoring a hand tremor.
                var raw = new Vector((p.X - Position.X) / dt, (p.Y - Position.Y) / dt);
                Velocity = new Vector(Velocity.X * 0.65 + raw.X * 0.35, Velocity.Y * 0.65 + raw.Y * 0.35);
            }
            _lastMoveAt = now;
            Position = p;

            var hit = Interesting(e.OriginalSource as DependencyObject);
            if (!ReferenceEquals(hit, Hovered))
            {
                Hovered = hit;
                if (hit != null) { try { HoverChanged?.Invoke(hit); } catch { } }
                else { try { HoverChanged?.Invoke(null); } catch { } }
            }
        }
        catch { /* input handlers never throw into WPF */ }
    }

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (_window != null) Position = e.GetPosition(_window);
            var hit = Interesting(e.OriginalSource as DependencyObject);
            if (hit == null) return;
            LastClicked = hit;
            LastClickAt = DateTime.Now;
            try { Pressed?.Invoke(hit); } catch { }
        }
        catch { }
    }

    /// <summary>Walk up from whatever raw visual raised the event to the nearest element that means
    /// something to Possession: a hand-tagged one first (the author said so), then an interactive
    /// control type, then a card-ish Border. Capped at 24 levels - deeper than that and we are walking
    /// out of the control the user actually touched and into page chrome.</summary>
    public static FrameworkElement? Interesting(DependencyObject? source)
    {
        try
        {
            FrameworkElement? cardFallback = null;
            var node = source;
            for (int i = 0; node != null && i < 24; i++)
            {
                if (node is FrameworkElement fe)
                {
                    if (Possession.GetExclude(fe)) return null;
                    if (Possession.GetRole(fe) != PossessionRole.None) return fe;
                    if (IsInteractive(fe)) return fe;
                    if (cardFallback == null && fe is Border { CornerRadius.TopLeft: > 0 } b
                        && b.Background != null && b.ActualHeight >= 60)
                        cardFallback = b;
                }
                node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }
            return cardFallback;
        }
        catch { return null; }
    }

    private static bool IsInteractive(FrameworkElement fe) => fe is ButtonBase or Slider or ComboBox
        or TextBoxBase or ListBoxItem or TabItem or ScrollBar;
}
