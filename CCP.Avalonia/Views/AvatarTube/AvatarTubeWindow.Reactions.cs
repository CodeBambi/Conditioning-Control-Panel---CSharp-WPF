using System;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    /// <summary>
    /// Port of WPF <c>AvatarTubeWindow.Reactions.cs</c> OnActivityChanged / OnStillOnActivity and the
    /// subscription in <c>AvatarTubeWindow.xaml.cs:317-322</c> / <c>Windowing.cs:1569</c>.
    ///
    /// <para><b>What leaves the machine.</b> WPF tries <c>App.Ai.GetAwarenessReactionAsync</c> /
    /// <c>GetStillOnReactionAsync</c> first when <c>AiChatEnabled</c> and the AI is available, and
    /// falls back to the preset line. That legacy call carries the app name and page title, which the
    /// consent only admits to with Awareness v2 off (<c>awareness_consent_leaves_body_legacy</c>);
    /// with v2 on WPF's arbiter speaks instead and titles stay local. This head has no v2 observer,
    /// so with v2 on it says the preset and nothing leaves (<see cref="MaySendToAi"/>,
    /// docs/avalonia-decisions.md). The deny list and incognito drop run in the poll, before any
    /// event.</para>
    /// </summary>
    public partial class AvatarTubeWindow
    {
        private const double StartupCooldownSeconds = 3.0;   // WPF Speech.cs:68
        internal DateTime _startupTime = DateTime.Now;   // settable by tests
        private bool _activityAiInFlight, _stillOnAiInFlight;   // WPF: don't stack requests

        /// <summary>The only door to the AI for an observed window: legacy pipeline (v2 off, so the
        /// consent said titles are sent), awareness on and consented, chat on, AI available.</summary>
        internal static bool MaySendToAi()
        {
            var s = CoreSettings.Current;
            return !s.UseAwarenessV2 && s.AwarenessModeEnabled && s.AwarenessConsentGiven
                && s.AiChatEnabled && App.Ai?.IsAvailable == true;
        }

        private void AttachAwareness()
        {
            App.WindowAwareness.ActivityChanged += OnActivityChanged;
            App.WindowAwareness.StillOnActivity += OnStillOnActivity;
            App.WindowAwareness.Start();
        }

        private void DetachAwareness()
        {
            App.WindowAwareness.ActivityChanged -= OnActivityChanged;
            App.WindowAwareness.StillOnActivity -= OnStillOnActivity;
        }

        /// <summary>WPF gates shared by both handlers: startup cooldown, bubble up, category.</summary>
        private bool MayReact(ActivityCategory category) =>
            (DateTime.Now - _startupTime).TotalSeconds >= StartupCooldownSeconds
            && !_speechBubble.IsVisible
            && App.WindowAwareness.IsCategoryEnabled(category);

        internal async void OnActivityChanged(object? sender, ActivityChangedEventArgs e) => await ReactToActivityAsync(e);

        internal async Task ReactToActivityAsync(ActivityChangedEventArgs e)
        {
            try
            {
                // Awareness v2 owns the awareness moment once it is live and attached (WPF Reactions.cs:62):
                // the arbiter drives delivery back through SpeakAwarenessLine below.
                if (ConditioningControlPanel.Services.Awareness.AwarenessV2Routing.IsActive) return;
                if (!MayReact(e.Category) || !App.WindowAwareness.CanReact() || _activityAiInFlight) return;

                string displayName = string.IsNullOrEmpty(e.ServiceName) ? e.DetectedName : e.ServiceName;
                string? reaction = null;
                if (MaySendToAi())
                {
                    _activityAiInFlight = true;
                    try
                    {
                        reaction = await App.Ai!.GetAwarenessReactionAsync(displayName, e.Category.ToString(),
                            e.ServiceName, e.PageTitle ?? "", App.WindowAwareness.CurrentActivityDuration);
                    }
                    catch (Exception ex) { Log.Warning(ex, "Failed to get AI awareness reaction"); }
                    finally { _activityAiInFlight = false; }
                }
                // Whitespace counts as "didn't work" (WPF): the preset, and the cooldown still burns.
                bool isAi = !string.IsNullOrWhiteSpace(reaction);
                if (!isAi)
                    reaction = AwarenessReactionPhrases.ForCategory(e.Category, displayName, _random, c => AwarenessReactionPhrases.Enabled(c));
                if (string.IsNullOrWhiteSpace(reaction)) return;

                App.WindowAwareness.MarkReaction();
                if (isAi) PlayDoubleBounce();
                GigglePriority(reaction, aiGenerated: isAi);
                Log.Debug("Awareness reaction fired ({Category}, app {AppChars} chars, line {Chars} chars)",
                    e.Category, displayName.Length, reaction.Length);
            }
            catch (Exception ex) { Log.Warning(ex, "OnActivityChanged handler failed"); }
        }

        /// <summary>
        /// WPF SpeakAwarenessLine (Reactions.cs:287): the arbiter's delivery door. The line was written
        /// from a privacy-projected frame and has already passed every gate; this only puts it in the
        /// bubble. Rare and above get the double bounce. Text only: there is no recorded clip for a
        /// model-written line, so it is never voiced.
        /// </summary>
        public void SpeakAwarenessLine(string text, bool doubleBounce)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            RunOnAvatar(() =>
            {
                try
                {
                    if (doubleBounce) PlayDoubleBounce();
                    GigglePriority(text, aiGenerated: true);
                    Log.Debug("Awareness v2 line delivered (rare={Rare}, {Chars} chars)", doubleBounce, text.Length);
                }
                catch (Exception ex) { Log.Warning(ex, "SpeakAwarenessLine failed"); }
            });
        }

        internal async void OnStillOnActivity(object? sender, ActivityChangedEventArgs e) => await ReactStillOnAsync(e);

        internal async Task ReactStillOnAsync(ActivityChangedEventArgs e)
        {
            try
            {
                // v2 owns still-on moments too: they arrive as Milestone frames (WPF Reactions.cs:179).
                if (ConditioningControlPanel.Services.Awareness.AwarenessV2Routing.IsActive) return;
                if (!MayReact(e.Category) || !App.WindowAwareness.CanStillOnReact() || _stillOnAiInFlight) return;

                var duration = App.WindowAwareness.CurrentActivityDuration;
                // 50/50 chance to use just service name vs page title
                bool useServiceNameOnly = _random.Next(2) == 0;
                string displayName = useServiceNameOnly || string.IsNullOrEmpty(e.PageTitle) ? e.ServiceName : e.PageTitle;

                string? reaction = null;
                if (MaySendToAi())
                {
                    _stillOnAiInFlight = true;
                    try { reaction = await App.Ai!.GetStillOnReactionAsync(displayName, e.Category.ToString(), duration); }
                    catch (Exception ex) { Log.Warning(ex, "Failed to get AI still-on reaction"); }
                    finally { _stillOnAiInFlight = false; }
                }
                bool isAi = !string.IsNullOrWhiteSpace(reaction);
                if (!isAi)
                {
                    var minutes = (int)duration.TotalMinutes;
                    var timeText = minutes < 1 ? "a bit" : $"{minutes} min";
                    reaction = $"Still on {displayName}? {timeText} already~ Do your nails instead!";
                }

                App.WindowAwareness.MarkStillOnReaction();
                GigglePriority(reaction!, aiGenerated: isAi);
            }
            catch (Exception ex) { Log.Warning(ex, "OnStillOnActivity handler failed"); }
        }
    }
}
