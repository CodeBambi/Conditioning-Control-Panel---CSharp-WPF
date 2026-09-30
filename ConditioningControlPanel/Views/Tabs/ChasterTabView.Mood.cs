using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>Circe's mood on the lock hero (<see cref="CircesMood"/>). The meter paints itself;
    /// this only hands it today's mood, which is null (hidden) with the heat row off.</summary>
    public partial class ChasterTabView
    {
        internal void RefreshMood()
        {
            try { MoodMeter.Apply(App.Chaster?.Mood); }
            catch (System.Exception ex) { Diag.Swallowed(ex, "chaster mood meter"); }
        }

        /// <summary>Circe says a line on the page (<see cref="CirceLines"/>, picked by the main window).</summary>
        internal void SayLine(string text)
        {
            try { PageSays.Say(text); }
            catch (System.Exception ex) { Diag.Swallowed(ex, "chaster page line"); }
        }
    }
}
