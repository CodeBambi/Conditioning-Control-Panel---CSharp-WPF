using System;
using System.IO;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// Her small sounds: the WPF head's Services/EmiDesk/EmiSfx.cs pat and ring cues, same chains,
    /// scales and 130 ms per-cue throttle, played through Core <see cref="CoreAudio"/>. The first
    /// candidate that exists under Resources/sounds wins; none is silence.
    ///
    /// <para>ponytail: WPF also stays silent while AudioService.IsOutputSuppressed or an
    /// EmiLineEngine hold is active; neither exists on this head, so only master volume 0 mutes.
    /// Files resolve through ContentLocator only: WPF's ModResourceResolver.ResolveAudioPath (a
    /// mod's replacement cue, the .wav/.mp3 swap) has no Core sounds seam yet, so a mod cannot
    /// re-voice these here; a stock install sounds right.
    /// The toss cues (Lift/Bump/Thud/Chime) come with the toss port (row win-emi-desk-extras).</para>
    /// </summary>
    internal static class EmiSfx
    {
        private const float PatScale = 0.12f;
        private const float OpenScale = 0.17f;
        private const float CloseScale = 0.13f;
        private const int MinGapMs = 130;

        /// <summary>The throttle's clock; tests step it.</summary>
        internal static TimeProvider Clock = TimeProvider.System;

        private static readonly object Gate = new();
        private static DateTimeOffset _lastPat = DateTimeOffset.MinValue;
        private static DateTimeOffset _lastRing = DateTimeOffset.MinValue;
        private static DateTimeOffset _lastChime = DateTimeOffset.MinValue;

        /// <summary>WPF EmiSfx.Chime: the pet streak's glee chime.</summary>
        public static void Chime()
        {
            if (Throttle(ref _lastChime))
                Play(new[] { "emi/chime.mp3", "chaos/reveal_chime.mp3", "chime1.mp3" }, 0.15f, "emi-sfx-chime");
        }

        public static void Pat()
        {
            if (Throttle(ref _lastPat))
                Play(new[] { "emi/pat.mp3", "chaos/chip_pop.mp3", "bubbles/Pop3.mp3" }, PatScale, "emi-sfx-pat");
        }

        public static void RingOpen()
        {
            if (Throttle(ref _lastRing))
                Play(new[] { "emi/ring_open.mp3", "chaos/cards_in.mp3", "chaos/reveal_chime.mp3" }, OpenScale, "emi-sfx-ring");
        }

        public static void RingClose()
        {
            if (Throttle(ref _lastRing))
                Play(new[] { "emi/ring_close.mp3", "chaos/ui_unequip.mp3", "chaos/sink.mp3" }, CloseScale, "emi-sfx-ring");
        }

        private static bool Throttle(ref DateTimeOffset slot)
        {
            var now = Clock.GetUtcNow();
            lock (Gate)
            {
                // Abs: a clock stepped backwards (NTP, a test clock) must not mute her until it catches up.
                if (Math.Abs((now - slot).TotalMilliseconds) < MinGapMs) return false;
                slot = now;
                return true;
            }
        }

        private static void Play(string[] candidates, float scale, string tag)
        {
            try
            {
                int level = CoreSettings.Current?.MasterVolume ?? 0;
                if (level <= 0) return;
                float master = Math.Clamp(level / 100f, 0f, 1f);
                foreach (var rel in candidates)
                {
                    var path = ContentLocator.Resolve(Path.Combine("Resources", "sounds", rel.Replace('/', Path.DirectorySeparatorChar)));
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    CoreAudio.PlayOneShot(path, Math.Clamp(master * scale, 0f, 1f), tag);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] sfx {Tag} failed", tag);
            }
        }
    }
}
