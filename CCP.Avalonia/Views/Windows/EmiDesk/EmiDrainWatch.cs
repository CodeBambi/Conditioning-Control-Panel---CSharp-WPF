using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// EMI's Brain Drain moments: WPF OverlayService.cs :2188 (<c>brainDrainOn</c>, below the
    /// withheld gate so she never reacts to an effect the user did not get) and :2504
    /// (<c>brainDrainOff</c>, gated on the run stamp so the many no-op stops stay silent and the
    /// duration is honest). The haze rebuilds its windows when a monitor or the melt variant
    /// changes, so the edge is read one dispatcher pass later: a rebuild is not an off and an on.
    /// </summary>
    internal static class EmiDrainWatch
    {
        private static DateTime _onSinceUtc = DateTime.MinValue;
        private static int _intensity;
        private static bool _melt;
        private static bool _queued;

        /// <summary>Test seam: is the haze on screen (default: the overlay's own answer).</summary>
        internal static Func<bool> Showing = () => Overlays.BrainDrainOverlay.IsShowing;

        /// <summary>Test seam: run the settle step (default: one dispatcher pass later).</summary>
        internal static Action<Action> Defer = a => Dispatcher.UIThread.Post(a, DispatcherPriority.Background);

        /// <summary>The haze went up at this strength.</summary>
        internal static void Up(int intensity, bool melt)
        {
            _intensity = intensity;
            _melt = melt;
            Queue();
        }

        /// <summary>The haze windows were closed (for good, or to be rebuilt).</summary>
        internal static void Down() => Queue();

        private static void Queue()
        {
            if (_queued) return;
            _queued = true;
            try { Defer(Settle); }
            catch (Exception ex) { _queued = false; Log.Debug(ex, "[EmiDesk] brain drain watch could not queue"); }
        }

        internal static void Settle()
        {
            _queued = false;
            try
            {
                bool showing = Showing();
                if (showing && _onSinceUtc == DateTime.MinValue)
                {
                    _onSinceUtc = DateTime.UtcNow;
                    EmiDeskBus.Fire("brainDrainOn", new { n = _intensity, melt = _melt });
                }
                else if (!showing && _onSinceUtc != DateTime.MinValue)
                {
                    int minutes = Math.Max(0, (int)(DateTime.UtcNow - _onSinceUtc).TotalMinutes);
                    _onSinceUtc = DateTime.MinValue;
                    EmiDeskBus.Fire("brainDrainOff", new { minutes });
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] brain drain watch failed"); }
        }

        internal static void ResetForTests()
        {
            _onSinceUtc = DateTime.MinValue;
            _queued = false;
        }
    }
}
