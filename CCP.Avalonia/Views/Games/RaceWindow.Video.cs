// PORTED from WPF 7.1.5 Services/Chaos/CaucusHostService.cs (HookVideoEvents :551, OnVideoStarted :572,
// OnVideoEnded :574, PostPause :583): a mandatory video covers the race, so the page is told to pause
// (the kart is not driving blind under the video window) and to resume when it closes.
using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        private MandatoryVideoScheduler? _raceVideo;

        /// <summary>WPF HookVideoEvents(true). Idempotent; a second call moves the hook to the new scheduler.</summary>
        internal void HookRaceVideo(MandatoryVideoScheduler scheduler)
        {
            UnhookRaceVideo();
            _raceVideo = scheduler;
            scheduler.VideoStarted += OnRaceVideoStarted;
            scheduler.VideoEnded += OnRaceVideoEnded;
        }

        private void UnhookRaceVideo()
        {
            if (_raceVideo is not { } v) return;
            _raceVideo = null;
            v.VideoStarted -= OnRaceVideoStarted;
            v.VideoEnded -= OnRaceVideoEnded;
        }

        private void HookRaceVideoFromApp()
        {
            try { HookRaceVideo(MandatoryVideoOverlay.Instance.Scheduler); }
            catch (Exception ex) { Log.Debug("RaceHost: video hook: {E}", ex.Message); }
        }

        internal void OnRaceVideoStartedForTest() => OnRaceVideoStarted();
        internal void OnRaceVideoEndedForTest() => OnRaceVideoEnded();

        private void OnRaceVideoStarted() => PostRacePause(true);

        private void OnRaceVideoEnded()
        {
            PostRacePause(false);
            // the video window had the keyboard; hand it back to the race
            try { Dispatcher.UIThread.Post(() => { if (!IsClosedOrClosing) Web.Focus(); }); }
            catch (Exception ex) { Log.Debug("RaceHost: focus back: {E}", ex.Message); }
        }

        private void PostRacePause(bool on)
        {
            if (_raceDisposed || IsClosedOrClosing) return;
            Post(new { type = "pause", on });
        }
    }
}
