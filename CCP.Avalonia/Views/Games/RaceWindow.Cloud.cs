using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        internal const bool RaceCloudAvailable = false;
        private bool _raceCloudSwapping, _raceCloudTrackRefused;
        private void RaceOpenCloud(string? url, bool background) => Log.Debug("[Race] cloud-open: not handled on this head yet");
        private void RaceStartCloudTrack() => Log.Debug("[Race] cloud-start: not handled on this head yet");
        private void DisposeRaceCloud() { }
    }
}
