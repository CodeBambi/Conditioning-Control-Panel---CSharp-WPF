using System;

namespace ConditioningControlPanel.Services.Race;

/// <summary>
/// Plays the loaded track for the race: AudioFileReader + WaveOutEvent, the app's master volume,
/// Play / Pause / Resume / Stop, PositionSec for the 250 ms clock, Ended when the file runs out.
/// Owned by CaucusHostService. Filled in by PR c6.
/// </summary>
public sealed class TrackPlayer : IDisposable
{
    public event Action? Ended;

    public double PositionSec => throw new NotImplementedException("PR c6: TrackPlayer.PositionSec");
    public double DurationSec => throw new NotImplementedException("PR c6: TrackPlayer.DurationSec");
    public bool IsPlaying => throw new NotImplementedException("PR c6: TrackPlayer.IsPlaying");

    public void Load(string path) => throw new NotImplementedException("PR c6: TrackPlayer.Load");
    public void Play() => throw new NotImplementedException("PR c6: TrackPlayer.Play");
    public void Pause() => throw new NotImplementedException("PR c6: TrackPlayer.Pause");
    public void Resume() => throw new NotImplementedException("PR c6: TrackPlayer.Resume");
    public void Stop() => throw new NotImplementedException("PR c6: TrackPlayer.Stop");
    public void Dispose() { Ended = null; }
}
