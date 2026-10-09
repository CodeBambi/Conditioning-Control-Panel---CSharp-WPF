// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Chaster.cs (InitializeChasterFlash,
// OnChasterBooked, ShowBookedFlash): a booked price floats off the rail padlock, coalesced by Core
// BookedFlashPlan. ponytail: WPF first tries ChasterBookedPop, a topmost window at the cause/cursor
// (desktop-wide topmost is Bucket E, not permitted on Wayland); this head always takes WPF's rail
// fallback. The chip ring pulses in the figure's colour, as WPF. No CapRefused tag (it is a pop too).
using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private BookedFlashPlan.Figure? _chasterFigure;
        private ChasterBookedFlash? _chasterFlash;

        /// <summary>Wire the figure to the service App built (WPF calls this from the ctor).</summary>
        internal void InitializeChasterFlash(ChasterService chaster)
        {
            chaster.BookedAt += OnChasterBooked;
            Closed += (_, _) => chaster.BookedAt -= OnChasterBooked;
            InitializeCirceLines(chaster); // MainShellWindow.CirceLines.cs
        }

        /// <summary>Raised on whatever thread booked it, so marshalled first.</summary>
        private void OnChasterBooked(string eventId, TabBooking booking, ScreenPoint? originPx)
        {
            if (booking.AppliedSeconds == 0) return;
            Dispatcher.UIThread.Post(() => ShowBookedFlash(eventId, booking.AppliedSeconds));
        }

        private void ShowBookedFlash(string eventId, int seconds)
        {
            try
            {
                var (figure, isNew) = BookedFlashPlan.Merge(_chasterFigure, seconds, eventId, Environment.TickCount64);
                _chasterFigure = figure;
                var look = BookedFlashPlan.For(figure, AmbientFxCanvas.Env.Level);
                if (!isNew && _chasterFlash is not null)
                {
                    // Same figure, bigger number; a price and its credit in the same breath net to nothing.
                    if (look is { } merged) _chasterFlash.Retitle(merged.Text, merged.Colour);
                    else { _chasterFlash.Dismiss(); _chasterFlash = null; }
                }
                else if (look is { } fresh)
                {
                    _chasterFlash?.Dismiss();
                    _chasterFlash = IsVisible ? ChasterBookedFlash.Show(Named<ChasterRailChip>("ChasterRail"), fresh) : null;
                }
                // The ring takes the figure's colour for a beat; a net-zero merge has no pulse.
                if (IsVisible && look is { } pulse) Named<ChasterRailChip>("ChasterRail")?.Pulse(global::Avalonia.Media.Color.FromUInt32(pulse.Colour));
            }
            catch (Exception ex) { Serilog.Log.Debug("[Chaster] booked flash: {E}", ex.Message); }
        }
    }
}
