using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// LIBRARY: the page's entrance and its two effects, PORTED from WPF MainWindow.AssetsFx.cs
    /// (Mich, rows-assets-fx): the asset-tree row nudge and the Media Log pulse. Both are finite
    /// and both have an out; the tab has no ambient loop.
    /// </summary>
    public partial class AssetsTabView
    {
        private int _mediaLogSeenCount;
        private CancellationTokenSource? _mediaLogPulse;
        /// <summary>Set by the IsVisible refresh so the shell's OnTabShown does not scan twice.</summary>
        private bool _refreshedOnShow;

        internal bool MediaLogPulsing => _mediaLogPulse is { IsCancellationRequested: false };

        /// <summary>WPF ShowTab("assets"): RefreshAssetTree, InitializeAssetPresets, then the AssetsFx
        /// entrance (the Media Log pulse when entries arrived since it was last opened). Every door
        /// into the page rescans; the reveal itself already did when the tab was hidden before.</summary>
        internal void OnTabShown()
        {
            if (!_refreshedOnShow) RefreshAssetBrowser();
            _refreshedOnShow = false;
            PulseMediaLogIfUnseen();
        }

        private static bool MotionAllowed()
        {
            try { return global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowTransitions; }
            catch { return true; }
        }

        /// <summary>AssetsFx OnAssetTreeRowHover: a 3 px, 130 ms ease-out slide.</summary>
        private void AssetTreeRow_PointerEntered(object? sender, PointerEventArgs e) => NudgeRow(sender as Control, true);
        private void AssetTreeRow_PointerExited(object? sender, PointerEventArgs e) => NudgeRow(sender as Control, false);

        internal static void NudgeRow(Control? row, bool on)
        {
            if (row == null) return;
            if (row.RenderTransform is not TranslateTransform slide)
            {
                if (row.RenderTransform != null) return;   // someone else's transform: leave it
                slide = new TranslateTransform();
                row.RenderTransform = slide;
            }
            slide.Transitions = MotionAllowed()
                ? new Transitions { new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(130), Easing = new QuadraticEaseOut() } }
                : null;
            slide.X = on ? 3.0 : 0;
        }

        /// <summary>AssetsFx PulseMediaLogIfUnseen: three 0.45 s dips to 0.45 opacity and back,
        /// only when the log grew since it was last opened. Finite; stopped on hide or click.</summary>
        private void PulseMediaLogIfUnseen()
        {
            if ((App.MediaHistory?.Count ?? 0) <= _mediaLogSeenCount || !MotionAllowed() || MediaLogPulsing) return;
            var cts = new CancellationTokenSource();
            _mediaLogPulse = cts;
            var anim = new Animation
            {
                Duration = TimeSpan.FromSeconds(0.9),
                IterationCount = new IterationCount(3),
                Easing = new SineEaseInOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 1.0) } },
                    new KeyFrame { Cue = new Cue(0.5), Setters = { new Setter(OpacityProperty, 0.45) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1.0) } },
                },
            };
            _ = anim.RunAsync(BtnMediaLog, cts.Token).ContinueWith(_ =>
            {
                if (ReferenceEquals(_mediaLogPulse, cts)) _mediaLogPulse = null;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void StopMediaLogPulse(bool resetOpacity)
        {
            _mediaLogPulse?.Cancel();
            _mediaLogPulse = null;
            if (resetOpacity) BtnMediaLog.Opacity = 1.0;
        }

        /// <summary>AssetsFx MediaLogButton_Clicked: opening the log marks it seen and ends the pulse.</summary>
        private void MarkMediaLogSeen()
        {
            _mediaLogSeenCount = App.MediaHistory?.Count ?? 0;
            StopMediaLogPulse(resetOpacity: true);
        }
    }
}
