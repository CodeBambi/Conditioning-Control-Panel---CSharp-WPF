using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    /// <summary>
    /// Port of WPF <c>AvatarTubeWindow.Reactions.cs</c> OnActivityChanged / OnStillOnActivity and the
    /// subscription in <c>AvatarTubeWindow.xaml.cs:317-322</c> / <c>Windowing.cs:1569</c>.
    ///
    /// <para><b>Nothing leaves the machine.</b> WPF tries <c>App.Ai.GetAwarenessReactionAsync</c> /
    /// <c>GetStillOnReactionAsync</c> first when <c>AiChatEnabled</c> and falls back to the preset
    /// line. This head has no AI service, so it always takes the preset branch - the same line WPF
    /// says with AI off. ponytail: AI reactions (and their double bounce) return when an AI service
    /// lands here; WPF's queued <c>Giggle</c> is <see cref="GigglePriority"/> (no speech queue).</para>
    /// </summary>
    public partial class AvatarTubeWindow
    {
        private const double StartupCooldownSeconds = 3.0;   // WPF Speech.cs:68
        internal DateTime _startupTime = DateTime.Now;   // settable by tests

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

        internal void OnActivityChanged(object? sender, ActivityChangedEventArgs e)
        {
            try
            {
                if (!MayReact(e.Category) || !App.WindowAwareness.CanReact()) return;

                string displayName = string.IsNullOrEmpty(e.ServiceName) ? e.DetectedName : e.ServiceName;
                var reaction = AwarenessReactionPhrases.ForCategory(e.Category, displayName, _random, c => AwarenessReactionPhrases.Enabled(c));
                if (string.IsNullOrWhiteSpace(reaction)) return;

                App.WindowAwareness.MarkReaction();
                GigglePriority(reaction, aiGenerated: false);
                Log.Debug("Awareness reaction fired ({Category}, app {AppChars} chars, line {Chars} chars)",
                    e.Category, displayName.Length, reaction.Length);
            }
            catch (Exception ex) { Log.Warning(ex, "OnActivityChanged handler failed"); }
        }

        internal void OnStillOnActivity(object? sender, ActivityChangedEventArgs e)
        {
            try
            {
                if (!MayReact(e.Category) || !App.WindowAwareness.CanStillOnReact()) return;

                var duration = App.WindowAwareness.CurrentActivityDuration;
                // 50/50 chance to use just service name vs page title
                bool useServiceNameOnly = _random.Next(2) == 0;
                string displayName = useServiceNameOnly || string.IsNullOrEmpty(e.PageTitle) ? e.ServiceName : e.PageTitle;

                var minutes = (int)duration.TotalMinutes;
                var timeText = minutes < 1 ? "a bit" : $"{minutes} min";
                var reaction = $"Still on {displayName}? {timeText} already~ Do your nails instead!";

                App.WindowAwareness.MarkStillOnReaction();
                GigglePriority(reaction, aiGenerated: false);
            }
            catch (Exception ex) { Log.Warning(ex, "OnStillOnActivity handler failed"); }
        }
    }
}
