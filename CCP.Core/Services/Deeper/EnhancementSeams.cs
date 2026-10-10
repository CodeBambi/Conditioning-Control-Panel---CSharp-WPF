using System;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>System.Windows.Rect's part in the engine: the rendered video frame in screen
    /// units. <see cref="Empty"/> is what an audio source answers (gaze rules then skip).</summary>
    public readonly record struct PlaybackRect(double X, double Y, double Width, double Height)
    {
        public static readonly PlaybackRect Empty = new(0, 0, -1, -1);
        public bool IsEmpty => Width < 0 || Height < 0;
    }

    /// <summary>System.Windows.Point's part in the engine: a gaze point in screen units.</summary>
    public readonly record struct PlaybackPoint(double X, double Y);

    /// <summary>The five WebcamTrackingService events the engine listens to. The head adapts its
    /// tracker; events may arrive on the capture thread (the engine marshals).</summary>
    public interface IEnhancementWebcam
    {
        event Action? OnBlink;
        event Action? OnMouthOpen;
        event Action<double, double>? OnGazeMove;
        event Action? OnFaceLost;
        event Action? OnFaceFound;
    }

    /// <summary>What WPF's engine asked App.Overlay / App.Flash / App.Subliminal for on Stop.
    /// The head's dispatcher implements it; a dispatcher without it (tests, dry run) has nothing
    /// on screen to retire.</summary>
    public interface IEnhancementRunCleanup
    {
        void ResetOverlayBands();
        void StopOneShotFlashes();
        void StopOneShotSubliminals();
    }
}
