using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The head half of WPF <c>SubliminalService.FlashPhrase</c> (:237), <c>TriggerBambiFreeze</c> (:329)
    /// and <c>PlayBambiReset</c> (:428): whisper (with ducking) first, the card
    /// <see cref="SubliminalWhisper.HapticLeadMs"/> + <see cref="SubliminalWhisper.VisualAfterHapticMs"/>
    /// later, and a Reset after a Freeze. Which clip, how loud and when come from Core
    /// <see cref="SubliminalWhisper"/>; this class only plays and draws.
    ///
    /// <para>Haptics through <see cref="CoreHaptics"/> as WPF: HapticLeadMs after the whisper, or
    /// straight away and the card after <c>SubliminalAnticipationMs</c> when nothing is whispered
    /// (TriggerSubliminalWithHapticPattern :563).</para>
    /// <para>A new whisper cuts the previous one, and <see cref="StopAll"/> (Stop, tray Stop everything)
    /// silences one mid-play and drops a card still waiting behind it (WPF StopAudio + the
    /// ShowSubliminalVisuals arrival guard). ponytail: no MarkWhisperAudio (no bark engine on this
    /// head); no deferred Reset (only mandatory video defers).</para>
    /// </summary>
    internal static class SubliminalWhisperShow
    {
        private static readonly Random Rng = new();
        private static Action? _stopWhisper;
        private static int _gen;   // bumped by StopAll: anything scheduled before it is stale

        /// <summary>WPF <c>_audioPath</c>: BaseDirectory/Resources/sub_audio.</summary>
        internal static string SubAudioDir => Path.Combine(AppContext.BaseDirectory, "Resources", "sub_audio");

        /// <summary>Test seams: the card, and the clock every delay runs on.</summary>
        internal static Action<string> Draw = text => Dispatcher.UIThread.Post(() =>
        {
            if ((global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is { } host)
                SubliminalOverlay.Show(host, text);
        });
        internal static Action<int, Action> After = (ms, then) => Task.Delay(ms).ContinueWith(_ => then());
        internal static Func<double> Roll = () => { lock (Rng) return Rng.NextDouble(); };

        /// <summary>WPF TearDownSurfaces: silence the whisper and retire every pending card/Reset.</summary>
        internal static void StopAll()
        {
            Interlocked.Increment(ref _gen);
            Interlocked.Exchange(ref _stopWhisper, null)?.Invoke();
        }

        /// <summary><see cref="After"/>, dropped when a Stop lands in between.</summary>
        private static void Later(int ms, Action then)
        {
            var gen = Volatile.Read(ref _gen);
            After(ms, () => { if (Volatile.Read(ref _gen) == gen) then(); });
        }

        /// <summary>WPF FlashPhrase: Core's ambient scheduler calls this for every show.</summary>
        public static void Phrase(string text)
        {
            if (Whisper(text))
            {
                if (SubliminalWhisper.IsFreezePhrase(text)) ScheduleReset();
                CoreProgression.AddXP(20, "Subliminal");
            }
            else
            {
                HapticThenDraw(text);
                CoreProgression.AddXP(10, "Subliminal");
            }
        }

        /// <summary>WPF TriggerBambiFreeze(deferReset: false): the mod's Freeze text, even with subliminals off.</summary>
        public static void Freeze()
        {
            var text = App.Mods?.GetFreezeTriggerText() ?? "Freeze";
            if (Whisper(text))
            {
                ScheduleReset();
                Log.Information("Bambi Freeze triggered with audio");
            }
            else
            {
                HapticThenDraw(text);
                Log.Information("Bambi Freeze triggered (no audio file, or whispers muted)");
            }
        }

        private static void ScheduleReset()
        {
            if (!SubliminalWhisper.ResetFollows(Roll()))
            {
                Log.Debug("Bambi Reset skipped (10% chance roll)");
                return;
            }
            int delay;
            lock (Rng) delay = SubliminalWhisper.ResetDelayMs(Rng);
            Later(delay, () =>
            {
                var text = App.Mods?.GetResetTriggerText() ?? "Reset";
                if (!Whisper(text)) HapticThenDraw(text);
                Log.Debug("Bambi Reset triggered after Bambi Freeze");
            });
        }

        /// <summary>Duck, play the phrase's linked clip, then draw the card. False (nothing played) when
        /// there is no clip or whispers are muted: the caller then draws the card straight away.</summary>
        internal static bool Whisper(string text)
        {
            var s = CoreSettings.Current;
            var path = SubliminalWhisper.FindLinkedAudio(text, SubliminalWhisper.ModAudioDir(App.Mods?.ActiveMod?.InstalledPath),
                SubAudioDir, App.Mods?.ActiveModId);
            if (path == null || !s.SubAudioAudible) return false;

            if (s.AudioDuckingEnabled) CoreAudio.Duck(s.DuckingLevel);
            var gen = CoreAudio.DuckGeneration;
            Interlocked.Exchange(ref _stopWhisper, null)?.Invoke();   // WPF PlayWhisperAudio: StopAudio first
            _stopWhisper = CoreAudio.PlayStoppable(path, SubliminalWhisper.Volume(s.MasterVolume, s.SubAudioVolume), "whisper",
                onFinished: () => After(SubliminalWhisper.UnduckDelayMs, () => CoreAudio.Unduck(gen)));
            Log.Debug("Playing subliminal audio: {Path}", Path.GetFileName(path));
            Later(SubliminalWhisper.HapticLeadMs, () =>
            {
                _ = CoreHaptics.Service?.TriggerSubliminalPatternAsync(text);
                Later(SubliminalWhisper.VisualAfterHapticMs, () => Draw(text));
            });
            return true;
        }

        /// <summary>WPF TriggerSubliminalWithHapticPattern: haptic first, the card after the provider's
        /// spin-up head start (0 with no toy connected).</summary>
        internal static void HapticThenDraw(string text)
        {
            var haptics = CoreHaptics.Service;
            _ = haptics?.TriggerSubliminalPatternAsync(text);
            var anticipationMs = haptics?.SubliminalAnticipationMs ?? 0;
            if (anticipationMs > 0) Later(anticipationMs, () => Draw(text));
            else Draw(text);
        }
    }
}
