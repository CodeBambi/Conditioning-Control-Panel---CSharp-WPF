using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// The plumbing WPF's <c>Adorner</c> base class gave <see cref="CardSheenAdorner"/>,
    /// <see cref="RowSweepAdorner"/> and <see cref="PerimeterCometAdorner"/> for free, written once.
    ///
    /// <para>Avalonia's <see cref="AdornerLayer"/> hosts any control (TierFxBorder already rides it),
    /// but the layer is the WINDOW's: hiding the card's tab does not hide the adorner. So the
    /// adorner follows the card's effective visibility (WPF IsVisibleChanged semantics) and its
    /// clock only ticks while it can be seen (P01). WPF's frame-rate-capped DoubleAnimation on an
    /// AffectsRender property becomes a capped <see cref="DispatcherTimer"/> that reads
    /// <see cref="Time"/>, so tests step the clock instead of sleeping.</para>
    /// </summary>
    public abstract class FxAdorner : Control
    {
        /// <summary>Test seam: every adorner phase is elapsed time on this clock.</summary>
        internal static TimeProvider Time = TimeProvider.System;

        protected readonly Control Adorned;
        private readonly int _fps;
        private global::ConditioningControlPanel.Avalonia.Controls.Fx.FrameClock? _timer;
        private IDisposable? _watch;

        protected FxAdorner(Control adorned, int fps) => (Adorned, _fps, IsHitTestVisible) = (adorned, fps, false);

        /// <summary>True while the frame clock runs. Test seam.</summary>
        internal bool IsTicking => _timer?.IsEnabled == true;

        /// <summary>One frame. The timer calls it; tests call it after stepping <see cref="Time"/>.</summary>
        internal abstract void Tick();

        /// <summary>Joins the adorned element's layer. False (quietly) when it has none yet - the
        /// element is not in a visual tree, a normal state during construction.</summary>
        internal bool AddToLayer()
        {
            var layer = AdornerLayer.GetAdornerLayer(Adorned);
            if (layer == null) return false;
            // Without this the layer arranges the adorner over the whole window.
            AdornerLayer.SetAdornedElement(this, Adorned);
            layer.Children.Add(this);
            _watch ??= EffectiveVisibility.Watch(Adorned, OnShownChangedCore);
            IsVisible = Adorned.IsEffectivelyVisible;
            return true;
        }

        /// <summary>Parks the clock and leaves the layer. Idempotent.</summary>
        internal void RemoveFromLayer()
        {
            StopTicking();
            _watch?.Dispose();
            _watch = null;
            if (Parent is Panel panel) panel.Children.Remove(this);
        }

        protected void StartTicking()
        {
            if (!IsVisible) return;
            // The window's frame clock at the capped rate (Controls/Fx/FrameClock): frame-locked,
            // where a free-running DispatcherTimer drifts against the refresh.
            if (_timer == null)
            {
                _timer = new global::ConditioningControlPanel.Avalonia.Controls.Fx.FrameClock(this) { Interval = TimeSpan.FromMilliseconds(1000.0 / _fps) };
                _timer.Tick += (_, _) => Tick();
            }
            _timer.Start();
        }

        protected void StopTicking() => _timer?.Stop();

        private void OnShownChangedCore()
        {
            try { IsVisible = Adorned.IsEffectivelyVisible; OnShownChanged(IsVisible); }
            catch { /* decoration never takes its card down */ }
        }

        /// <summary>The card (or an ancestor) was shown or hidden.</summary>
        protected abstract void OnShownChanged(bool shown);
    }
}
