using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    /// <summary>
    /// Port of the rest of WPF <c>ImgAvatar_MouseLeftButtonDown</c> (AvatarTubeWindow.ChatInput.cs:50-142)
    /// that has a twin here: the click squash (<c>PlayClickBounce</c>: 1.0 -> 1.015 in 80 ms cubic
    /// ease-out, back to 1.0 by 300 ms on an elastic ease-out, 1 oscillation, springiness 7, origin at
    /// her feet), the 1-in-25 pop and the double-click (300 ms) that opens the chat input when the AI
    /// is on and available.
    /// <para>ponytail: the 50-clicks-in-60s collapse (TriggerBambiCumAndCollapse), the Neon Obsession
    /// click count (App.Achievements) and the drop-shadow glow pulse (no Effect over the avatar here)
    /// remain. The activity-comment double click with the AI off is AvatarTubeWindow.ActivityComment.cs.</para>
    /// </summary>
    public partial class AvatarTubeWindow
    {
        private DispatcherTimer? _clickBounceTimer;
        private DateTime _lastAvatarClickTime = DateTime.MinValue;

        /// <summary>Called from OnAvatarPointerPressed on a left press.</summary>
        private void OnAvatarLeftClick()
        {
            if (Random.Shared.Next(25) == 0) PlayAvatarPopSound();

            var now = DateTime.Now;
            if ((now - _lastAvatarClickTime).TotalMilliseconds < 300)
            {
                if (IsMuted) ShowMutedIndicator();   // WPF ChatInput.cs:97
                else if (CoreSettings.Current.AiChatEnabled && App.Ai?.IsAvailable == true) OpenChatInput();
                else OnAvatarDoubleClickComment(now);   // WPF ChatInput.cs:104: the activity comment (AvatarTubeWindow.ActivityComment.cs)
            }
            _lastAvatarClickTime = now;

            PlayClickBounce();
        }

        private DispatcherTimer? _mutedIndicatorTimer;

        /// <summary>WPF ShowMutedIndicator (Speech.cs:813): a double-click while muted says so in the
        /// bubble for two seconds. Never over a bubble that is already up.</summary>
        internal void ShowMutedIndicator()
        {
            if (_speechBubble.IsVisible || _isPlayingUninterruptibleClip) return;
            StopTypewriter();
            _aiBadge.IsVisible = false;
            _policyBadge.IsVisible = false;
            _txtSpeech.Text = "MUTED \U0001F509";
            _txtSpeech.FontSize = 15;
            _speechBubble.MaxWidth = 380;
            ApplySpeechBubblePlacement();
            _speechBubble.IsVisible = true;
            _mutedIndicatorTimer?.Stop();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (!_isGiggling && !_isListeningBubble && !_isWaitingForAi) CollapseSpeechBubble();
            };
            _mutedIndicatorTimer = timer;
            timer.Start();
        }

        /// <summary>WPF PlayClickBounce, stepped at 60 Hz on AvatarBounceHost (origin 50%,100%). A running
        /// double bounce (the AI reply hop) owns the host's transform, so the squash yields to it.</summary>
        internal void PlayClickBounce()
        {
            var host = this.FindControl<Grid>("AvatarBounceHost");
            if (host == null || _bounceTimer?.IsEnabled == true) return;
            var scale = new ScaleTransform(1, 1);
            host.RenderTransform = scale;
            _clickBounceTimer?.Stop();
            var clock = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                var ms = clock.Elapsed.TotalMilliseconds;
                var s = ClickBounceScale(ms);
                scale.ScaleX = s;
                scale.ScaleY = s;
                if (ms >= 300)
                {
                    timer.Stop();
                    if (ReferenceEquals(host.RenderTransform, scale)) host.RenderTransform = null;
                }
            };
            _clickBounceTimer = timer;
            timer.Start();
        }

        /// <summary>The WPF keyframes as a pure curve (tests pin it).</summary>
        internal static double ClickBounceScale(double ms)
        {
            if (ms <= 0) return 1.0;
            if (ms < 80)
            {
                var t = ms / 80.0;
                var eased = 1 - Math.Pow(1 - t, 3);   // CubicEase EaseOut
                return 1.0 + 0.015 * eased;
            }
            if (ms < 300)
            {
                var t = (ms - 80) / 220.0;
                return 1.015 + (1.0 - 1.015) * ElasticEaseOut(t, oscillations: 1, springiness: 7);
            }
            return 1.0;
        }

        /// <summary>WPF ElasticEase (EaseOut = 1 - EaseIn(1 - t)).</summary>
        private static double ElasticEaseOut(double t, int oscillations, double springiness)
        {
            static double EaseIn(double x, int osc, double spring)
            {
                var expo = spring == 0 ? x : (Math.Exp(spring * x) - 1.0) / (Math.Exp(spring) - 1.0);
                return expo * Math.Sin((2.0 * Math.PI * osc + Math.PI * 0.5) * x);
            }
            return 1.0 - EaseIn(1.0 - t, oscillations, springiness);
        }

        /// <summary>WPF PlayAvatarPopSound: Pop/Pop2/Pop3 at master^1.5.</summary>
        private static void PlayAvatarPopSound()
        {
            try
            {
                var pops = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
                var path = Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "bubbles", pops[Random.Shared.Next(pops.Length)]);
                if (!File.Exists(path)) return;
                CoreAudio.PlayOneShot(path, (float)Math.Pow(CoreSettings.Current.MasterVolume / 100f, 1.5), "avatar-pop");
            }
            catch (Exception ex) { Log.Debug("Failed to play avatar pop sound: {Error}", ex.Message); }
        }
    }
}
