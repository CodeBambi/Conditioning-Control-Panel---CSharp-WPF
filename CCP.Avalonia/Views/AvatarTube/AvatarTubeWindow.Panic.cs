// PORTED from AvatarTube/AvatarTubeWindow.Speech.cs:1912 PanicSilence (WPF 7.1.5), called from the
// panic pass (MainWindow.xaml.cs:2005 "tube speech"). Ledger row tube#T2.

using System;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        /// <summary>Stop for the voiced line in the air (PlaySpeechAudio), or null. WPF _spokenHandle.</summary>
        private Action? _stopSpoken;

        /// <summary>WPF StopSpokenAudio: cut the spoken line now. Safe when nothing plays.</summary>
        internal void StopSpokenAudio()
        {
            var stop = _stopSpoken;
            _stopSpoken = null;
            try { stop?.Invoke(); } catch { }
        }

        /// <summary>The panic registry entry: the live tube, if any, goes silent.</summary>
        internal static void PanicSilenceLive()
        {
            var tube = Live;
            if (tube != null) tube.PanicSilence();
        }

        /// <summary>
        /// Panic ("panic stops everything"): silence the tube in one pass - cut the spoken line, drop the
        /// thinking dots and the listening dots, and take the bubble off the screen. Unlike the normal hide
        /// path nothing may speak after it. Idempotent and safe when idle.
        /// </summary>
        public void PanicSilence()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(PanicSilence); return; }

            _panicSilencedUtc = DateTime.UtcNow;   // reactions to the stop panic caused stay unsaid
            try { StopSpokenAudio(); } catch { }
            try { StopThinkingAnimation(); } catch { }
            try { _speechTimer?.Stop(); } catch { }
            try { ClearSpeechQueue(); } catch { }
            try
            {
                _isWaitingForAi = false;
                _isGiggling = false;
                _isShowingAiBubble = false;
                _isListeningBubble = false;
                _listeningDotsTimer?.Stop();
                _listeningDotsTimer = null;
            }
            catch { }
            try
            {
                _speechBubble.IsVisible = false;
                _lastSpeechEndTime = DateTime.Now;
            }
            catch { }
        }
    }
}
