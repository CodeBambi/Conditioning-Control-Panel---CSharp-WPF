using System;

namespace ConditioningControlPanel.Services.Race;

/// <summary>
/// What the race's 250 ms clock needs from whatever is actually making the sound (CHART.md,
/// "the clock is the file"). Two sources implement it: <see cref="LocalTrackClock"/> wraps the
/// NAudio player a picked file plays through, and <see cref="CloudTrackClock"/> mirrors an
/// audio element the player is driving on their own page in the BambiCloud window.
///
/// The host does not care which one it holds: it reads the three numbers for track-clock and
/// routes the Brake through <see cref="SetPaused"/>.
/// </summary>
public interface ITrackClock
{
    /// <summary>Where the audio is, in seconds.</summary>
    double PositionSec { get; }

    /// <summary>The audio's whole length in seconds, 0 while it is still unknown.</summary>
    double DurationSec { get; }

    /// <summary>True only while sound is actually coming out.</summary>
    bool IsPlaying { get; }

    /// <summary>The run started (track-play). A local file rewinds and plays; a cloud element is
    /// already running and is left exactly where the player put it.</summary>
    void Start();

    /// <summary>The Brake, a host pause or a video pop. true holds, false carries on.</summary>
    void SetPaused(bool on);

    /// <summary>End of run, exit or teardown.</summary>
    void Stop();
}

/// <summary>The picked-file clock: a thin face over <see cref="TrackPlayer"/>, which stays the
/// owner of the device and the reader. Nothing about the local path changed when the cloud
/// source arrived.</summary>
public sealed class LocalTrackClock : ITrackClock
{
    private readonly TrackPlayer _player;

    public LocalTrackClock(TrackPlayer player) => _player = player ?? throw new ArgumentNullException(nameof(player));

    public double PositionSec => _player.PositionSec;
    public double DurationSec => _player.DurationSec;
    public bool IsPlaying => _player.IsPlaying;

    public void Start()
    {
        _player.RefreshVolume();
        _player.Play();
    }

    public void SetPaused(bool on)
    {
        if (on) _player.Pause(); else _player.Resume();
    }

    public void Stop() => _player.Stop();
}

/// <summary>
/// The BambiCloud clock: the player's own audio element is the authority, so this holds nothing
/// but the last cloud-clock the watcher sent and pushes a pause back the other way.
///
/// Stop() deliberately does NOT silence the site. A lap ends when the track ends, and by then
/// their playlist has usually already started the next one; pausing them there would fight the
/// player for their own transport. The window closing is what stops their audio, and the window
/// is torn down with the race host.
/// </summary>
public sealed class CloudTrackClock : ITrackClock
{
    private readonly Action<bool> _setPaused;
    private double _t;
    private double _dur;
    private bool _playing;

    /// <param name="setPaused">Sends cloud-set-paused {on} into the page.</param>
    public CloudTrackClock(Action<bool> setPaused) => _setPaused = setPaused ?? throw new ArgumentNullException(nameof(setPaused));

    /// <summary>A cloud-clock landed. Duration is kept when a tick reports 0 for it: their element
    /// answers NaN until the metadata is in, and a run must not see the length flicker to nothing.</summary>
    public void Update(double t, bool playing, double durationSec)
    {
        if (t >= 0 && !double.IsNaN(t) && !double.IsInfinity(t)) _t = t;
        if (durationSec > 0 && !double.IsNaN(durationSec) && !double.IsInfinity(durationSec)) _dur = durationSec;
        _playing = playing;
    }

    public double PositionSec => _t;
    public double DurationSec => _dur;
    public bool IsPlaying => _playing;

    /// <summary>Nothing: the audio is already running over there, which is why the run started.</summary>
    public void Start() { }

    public void SetPaused(bool on)
    {
        // Optimistic locally so the very next track-clock already reads the new state; the
        // watcher's own play / pause echo confirms it a moment later.
        _playing = !on;
        try { _setPaused(on); }
        catch (Exception ex) { App.Logger?.Debug("CloudTrackClock.SetPaused: {E}", ex.Message); }
    }

    public void Stop() => _playing = false;
}
