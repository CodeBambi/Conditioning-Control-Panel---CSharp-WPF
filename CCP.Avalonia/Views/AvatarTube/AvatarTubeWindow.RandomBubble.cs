using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    /// <summary>
    /// Port of WPF <c>AvatarTubeWindow.Speech.cs</c> RANDOM BUBBLE TIMER (:1641-1770): with
    /// <c>RandomBubbleEnabled</c>, every 3-5 min (re-rolled each tick) she says a RandomBubble line and,
    /// a second later, a clickable bubble rises beside her. Popping it plays a pop, pays 5 XP
    /// (AvatarInteraction) and gets "Good girl! *giggles*" (WPF copy verbatim).
    /// Skipped while another app has the foreground, as WPF.
    /// </summary>
    public partial class AvatarTubeWindow
    {
        private DispatcherTimer? _randomBubbleTimer;

        private void StartRandomBubbleTimer()
        {
            if (!CoreSettings.Current.RandomBubbleEnabled) return;
            var interval = Random.Shared.Next(180, 301);
            _randomBubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            _randomBubbleTimer.Tick += OnRandomBubbleTick;
            _randomBubbleTimer.Start();
            Log.Information("RandomBubble: Started with {Interval}s interval", interval);
        }

        private void StopRandomBubbleTimer()
        {
            _randomBubbleTimer?.Stop();
            _randomBubbleTimer = null;
        }

        public void RestartRandomBubbleTimer()
        {
            if (!_speechLoopsStarted) return;
            StopRandomBubbleTimer();
            StartRandomBubbleTimer();
        }

        private void OnRandomBubbleTick(object? sender, EventArgs e)
        {
            if (_randomBubbleTimer != null)
                _randomBubbleTimer.Interval = TimeSpan.FromSeconds(Random.Shared.Next(180, 301));

            // WPF compared the foreground HWND's process to ours; any active window of ours is that.
            var ours = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows;
            if (ours == null || !ours.Any(w => w.IsActive))
            {
                Log.Debug("RandomBubble: Skipped - app not in focus");
                return;
            }
            SpawnRandomBubble();
        }

        internal void SpawnRandomBubble()
        {
            GiggleFromCategory("RandomBubble");
            DispatcherTimer.RunOnce(() =>
            {
                try
                {
                    if (!IsVisible) return;
                    var center = _avatarBorder.PointToScreen(new Point(_avatarBorder.Bounds.Width / 2, _avatarBorder.Bounds.Height / 2));
                    var scaling = RenderScaling;
                    _ = new AvatarRandomBubble(new Point(center.X / scaling, center.Y / scaling), scaling, Random.Shared, OnRandomBubblePopped);
                }
                catch (Exception ex) { Log.Warning("RandomBubble: Failed to spawn - {Error}", ex.Message); }
            }, TimeSpan.FromSeconds(1));
        }

        private void OnRandomBubblePopped()
        {
            PlayBubblePopSound();
            CoreProgression.AddXP(5, "AvatarInteraction");
            Giggle("Good girl! *giggles*");
        }

        /// <summary>WPF PlayBubblePopSound: Pop/Pop2/Pop3 at max(0.05, (bubbles * master)^1.5).</summary>
        private static void PlayBubblePopSound()
        {
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "bubbles");
                var pops = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
                var path = Path.Combine(dir, pops[Random.Shared.Next(pops.Length)]);
                if (!File.Exists(path)) return;
                var s = CoreSettings.Current;
                var volume = Math.Max(0.05f, (float)Math.Pow(s.BubblesVolume / 100f * (s.MasterVolume / 100f), 1.5));
                CoreAudio.PlayOneShot(path, volume, "avatar-bubble-pop");
            }
            catch (Exception ex) { Log.Debug(ex, "RandomBubble: pop sound failed"); }
        }
    }
}
