using System;
using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

// NAMESPACE TRAP: the FLAT ConditioningControlPanel namespace, same as every other file under
// Windows\. See the header of EmiDeskWindow.xaml.cs before "tidying" this.
namespace ConditioningControlPanel;

/// <summary>
/// Chunk B2's half of the widget: the ring.
///
/// <para>This file touches nothing in B1. It implements two of the partial-method seams declared in
/// <c>EmiDeskWindow.xaml.cs</c> (<c>OnBodyClickedCore</c> and <c>OnTearDownCore</c>) and otherwise
/// only subscribes to the events B1 already publishes. The ring itself lives in its own sibling
/// window, <see cref="EmiRingWindow"/>, because the widget carries only 120 DIPs of pad.</para>
///
/// <para>She reacts to none of this here on purpose: the ring fires MOMENTS, and chunk B3 decides
/// whether they are worth a face or a line. Playing a chain from this file would race B3 for the
/// same beat.</para>
/// </summary>
public partial class EmiDeskWindow
{
    private EmiRingWindow? _ring;
    private bool _ringWired;
    private Point _ringPressAt;
    private bool _ringWatchDrag;

    // ---------------------------------------------------------------- the seams

    /// <summary>
    /// A click on her body, already filtered by B1 down to "a real click, not a drag, not a resize,
    /// input not locked, the glass did not want it". That is the ring's toggle, and the only one:
    /// there is no double-click gesture anywhere near her, because it would delay this.
    /// </summary>
    partial void OnBodyClickedCore(ref bool handled)
    {
        try
        {
            if (InputLocked || Transiting) return;
            handled = true;
            ToggleRing();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ring toggle failed");
        }
    }

    /// <summary>
    /// She is leaving (dismiss outro, or shutdown). Fold the ring FIRST: a fan left hanging over the
    /// desktop after she has poofed out reads as a crash. Idempotent by contract.
    ///
    /// <para>MERGE NOTE: the glass chunk wants this seam too, and a partial method may only have one
    /// implementing declaration. The ring's file owns it (ring state wins the seam) and hands the
    /// rest of the tear-down straight on to <see cref="TearDownGlass"/>, so neither half is lost and
    /// the order is the one the ring needs.</para>
    /// </summary>
    partial void OnTearDownCore()
    {
        try { _ring?.CloseRing(); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring tear-down failed"); }

        try { TearDownGlass(); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] glass tear-down failed"); }
    }

    /// <summary>
    /// The glass asks before it wanders off to a channel: an open ring is the loudest "the user is
    /// mid-thought" signal there is, and a channel that flips up behind an open fan is a channel
    /// nobody sees. SEAMS 7.1; the ring is the only implementer.
    /// </summary>
    partial void OnRingOpenQuery(ref bool open)
    {
        if (RingOpen) open = true;
    }

    /// <summary>
    /// Re-compose the fan in place, without folding it. Used by the <c>pinTop:</c> offer effect,
    /// which can move a card under the user's pointer while the ring is up. A no-op when it is shut.
    /// </summary>
    public void RebuildRing()
    {
        try { _ring?.Rebuild(); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring rebuild failed"); }
    }

    // ---------------------------------------------------------------- open / close

    /// <summary>Open the ring if it is shut, fold it if it is not.</summary>
    public void ToggleRing()
    {
        var ring = EnsureRing();
        if (ring == null) return;

        if (ring.IsOpen)
        {
            ring.CloseRing();
            return;
        }

        ring.OpenRing();
        if (ring.IsOpen)
        {
            try { App.EmiDesk?.Fire("ringOpen"); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ringOpen moment failed"); }
        }
    }

    /// <summary>Fold the ring from anywhere (a drag, a dismiss, a full-screen feature starting).</summary>
    public void CloseRing()
    {
        try { _ring?.CloseRing(); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] CloseRing failed"); }
    }

    /// <summary>True while the fan is on screen. B3 checks it before a glass channel or an ask.</summary>
    public bool RingOpen
    {
        get
        {
            try { return _ring?.IsOpen == true; }
            catch { return false; }
        }
    }

    private EmiRingWindow? EnsureRing()
    {
        try
        {
            if (_ring != null) return _ring;

            _ring = new EmiRingWindow(this);
            _ring.CardPicked += OnRingCardPicked;
            _ring.PinToggled += OnRingPinToggled;
            _ring.RingClosed += OnRingClosed;

            if (!_ringWired)
            {
                _ringWired = true;

                // Follow her: a resize re-fans in place, a drop folds the ring (the pitch's rule:
                // "drag her while it's open and it folds").
                Resized += (_, _) => { try { _ring?.Relayout(); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring follow-resize failed"); } };
                Moved += (_, _) => { try { _ring?.CloseRing(); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring follow-move failed"); } };

                // Fold on the FIRST movement of a drag rather than on the drop, so the ring is gone
                // before she is. B1's own drag threshold, so the two agree on what a drag is.
                PreviewMouseLeftButtonDown += OnRingWatchPress;
                PreviewMouseMove += OnRingWatchMove;

                // ShutDown() closes the window without running the tear-down seam, so the sibling
                // has to be taken down here or it outlives her.
                Closed += (_, _) => { try { _ring?.Kill(); _ring = null; } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring shutdown failed"); } };
            }

            return _ring;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ring window could not be created");
            _ring = null;
            return null;
        }
    }

    private void OnRingWatchPress(object sender, MouseButtonEventArgs e)
    {
        try
        {
            _ringPressAt = e.GetPosition(this);
            _ringWatchDrag = true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ring drag watch failed to arm");
            _ringWatchDrag = false;
        }
    }

    private void OnRingWatchMove(object sender, MouseEventArgs e)
    {
        try
        {
            if (!_ringWatchDrag) return;
            if (e.LeftButton != MouseButtonState.Pressed) { _ringWatchDrag = false; return; }
            if (_ring?.IsOpen != true) return;

            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _ringPressAt.X) + Math.Abs(p.Y - _ringPressAt.Y) <= DragThresholdDip) return;

            _ringWatchDrag = false;
            _ring.CloseRing();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ring drag watch failed");
        }
    }

    // ---------------------------------------------------------------- the moments

    private void OnRingCardPicked(object? sender, EmiRingSlot slot)
    {
        try
        {
            // The ring has already folded. Open() owns the counter, the tier gate and the
            // ringPick / arcademyFromRing / lockedCardTapped moment, so navigation is never behind
            // a reaction.
            slot.Target.Open();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ring card {Target} failed to open", slot.Target.Id);
        }
    }

    private void OnRingPinToggled(object? sender, (EmiRingSlot Slot, bool Pinned) e)
    {
        try
        {
            if (!e.Pinned) return;
            App.EmiDesk?.Fire("pinAdded", new { target = e.Slot.Target.Id });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] pinAdded moment failed");
        }
    }

    /// <summary>
    /// The ring folded. A pick clears the ignore streak; a dismissal grows it, and the third
    /// dismissal in a row is its own moment (she offered six things three times and got nothing).
    /// The streak resets after firing so it can fire again, not once per install.
    /// </summary>
    private void OnRingClosed(object? sender, bool picked)
    {
        try
        {
            var st = EmiState.Current;

            if (picked)
            {
                if (st.RingIgnoreStreak != 0)
                {
                    st.RingIgnoreStreak = 0;
                    EmiState.SaveSoon();
                }
                return;
            }

            App.EmiDesk?.Fire("ringDismissed");

            st.RingIgnoreStreak++;
            if (st.RingIgnoreStreak >= 3)
            {
                st.RingIgnoreStreak = 0;
                App.EmiDesk?.Fire("suggestionIgnored3x");
            }
            EmiState.SaveSoon();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ring close bookkeeping failed");
        }
    }
}
