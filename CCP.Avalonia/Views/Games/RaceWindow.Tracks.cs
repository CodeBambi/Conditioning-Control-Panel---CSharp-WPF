using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        private void RacePickTrack() => Post(new { type = "track-error", message = "your own tracks are not on this build yet" });
        private void RaceTrackPlay() => Log.Debug("[Race] track-play: not handled on this head yet");
        private void RaceTrackPause(bool on) => Log.Debug("[Race] track-pause: not handled on this head yet");
        private void RaceStopTrack() { }
        private void RaceCancelAnalysis(bool postCancelled) => Log.Debug("[Race] track-cancel: not handled on this head yet");
        private void DisposeRaceTracks() { }
    }
}
