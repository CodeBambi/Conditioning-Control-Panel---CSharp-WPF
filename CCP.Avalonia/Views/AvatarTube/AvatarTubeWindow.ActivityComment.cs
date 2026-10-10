// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.ChatInput.cs:185-345
// (TriggerActivityCommentAsync + TrySpeakAwarenessV2CommentAsync): the double-click "comment on what I am
// doing" with the AI off.
//
// WPF reaches this only from the double-click's last branch, after "AI chat on and available" opened the
// chat input instead, so its two AI branches (the v1 GetAwarenessReactionAsync comment and the AI random
// thought) can never run from here and are not carried over. What is left: a custom trigger in trigger
// mode; three times in four a random preset phrase; the fourth time, for a recognised activity, the
// awareness v2 road (the observer's last real frame through the arbiter: same projection, same deny list,
// same cooldown ledger) and a preset category phrase when the arbiter declines or v2 is off.
using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private DateTime _lastInteractionTime = DateTime.MinValue;
        private int _interactionCount;

        /// <summary>Test seams: the observer's last frame, the arbiter, and what the window is doing.</summary>
        internal Func<ContextFrame?> ActivityFrame = () => AwarenessLive.LastFrame;
        internal Func<IReactionArbiter?> ActivityArbiter = () => AwarenessV2Routing.IsActive ? AwarenessV2Routing.Arbiter : null;
        internal Func<(ActivityCategory Category, string Name)> ActivityNow = () =>
            (App.WindowAwareness.CurrentActivity, App.WindowAwareness.CurrentDetectedName ?? "");

        /// <summary>WPF double-click, last two branches (ChatInput.cs:104-112): nothing while a line is up
        /// or the AI is thinking, and at most one comment every 1.5 s.</summary>
        private void OnAvatarDoubleClickComment(DateTime now)
        {
            if (_isGiggling || _isWaitingForAi) { Log.Debug("Skipping double-click - message still showing"); return; }
            if ((now - _lastInteractionTime).TotalSeconds < 1.5) return;
            _lastInteractionTime = now;
            _ = TriggerActivityCommentAsync();
        }

        /// <summary>A comment on the current activity, or a random thought.</summary>
        internal async Task TriggerActivityCommentAsync()
        {
            try
            {
                var s = CoreSettings.Current;
                // 1. Trigger mode: a custom trigger, always.
                if (s.TriggerModeEnabled && s.CustomTriggers is { Count: > 0 } triggers)
                {
                    GigglePriority(triggers[_random.Next(triggers.Count)], aiGenerated: false);
                    return;
                }

                // 2. Three times in four: a standard random phrase.
                _interactionCount++;
                if (_interactionCount % 4 != 0)
                {
                    GigglePriority(RandomBambiPhrase(), aiGenerated: false);
                    return;
                }

                // 3. The fourth: what the player is doing, when it is something she recognises.
                var (category, name) = ActivityNow();
                bool recognized = category != ActivityCategory.Unknown && category != ActivityCategory.Idle;
                if (!recognized)
                {
                    GigglePriority(RandomBambiPhrase(), aiGenerated: false);
                    return;
                }
                if (ActivityArbiter() is { } arbiter && await TrySpeakAwarenessV2CommentAsync(arbiter)) return;
                var phrase = AwarenessReactionPhrases.ForCategory(category, name, _random, c => AwarenessReactionPhrases.Enabled(c));
                if (!string.IsNullOrWhiteSpace(phrase)) GigglePriority(phrase, aiGenerated: false);
            }
            catch (Exception ex) { Log.Debug("Double-click activity comment failed: {Error}", ex.Message); }
        }

        /// <summary>True when a line actually reached the player, so the caller says nothing more.</summary>
        private async Task<bool> TrySpeakAwarenessV2CommentAsync(IReactionArbiter arbiter)
        {
            try
            {
                var frame = ActivityFrame();
                if (frame == null) return false;
                var decision = await arbiter.SubmitAsync(frame);
                return decision.Verdict != AwarenessVerdict.Silence;
            }
            catch (Exception ex)
            {
                Log.Debug("Double-click awareness comment failed: {Error}", ex.Message);
                return false;
            }
        }
    }
}
