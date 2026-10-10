// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.ChatInput.cs:957 (ShowEmoteFeedback).
using System;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        /// <summary>The bubble's words for a remote emote: "Sending..." while it is on its way, then
        /// Sent: "text" (40 characters at most), as WPF.</summary>
        internal static string EmoteFeedbackText(string? text, bool isPending)
        {
            var safe = (text ?? "").Trim();
            if (safe.Length > 40) safe = safe.Substring(0, 40) + "...";
            return isPending ? "Sending..." : "Sent: \"" + safe + "\"";
        }

        /// <summary>
        /// A speech bubble for emote feedback. Skips silently while the avatar waits on or shows an AI
        /// bubble (never fights the conversation); otherwise interrupts any preset speech and shows at
        /// once. Not added to the chat history, no audio, one second longer than a preset line. UI thread.
        /// </summary>
        public void ShowEmoteFeedback(string text, bool isPending)
        {
            try
            {
                if (_isWaitingForAi || _isShowingAiBubble) return;
                var content = EmoteFeedbackText(text, isPending);

                // Clear any in-flight preset speech so the bubble updates instantly.
                _speechTimer?.Stop();
                _speechDelayTimer?.Stop();
                _speechQueue.Clear();
                _isGiggling = false;

                ShowGiggle(content, playSound: false, source: SpeechSource.Preset);
                if (_speechTimer != null) _speechTimer.Interval += TimeSpan.FromSeconds(1);
            }
            catch (Exception ex) { Log.Warning(ex, "[Avatar] ShowEmoteFeedback failed"); }
        }
    }
}
