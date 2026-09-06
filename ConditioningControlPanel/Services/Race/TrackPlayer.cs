using System;
using System.Windows;
using NAudio.Wave;

namespace ConditioningControlPanel.Services.Race;

/// <summary>
/// Plays the loaded track for the race: AudioFileReader + WaveOutEvent, the app's master volume,
/// Play / Pause / Resume / Stop, PositionSec for the 250 ms clock, Ended when the file runs out.
/// Owned by CaucusHostService.
///
/// Every method here is called from the UI thread. The one callback that is not is NAudio's
/// PlaybackStopped, which arrives on the output device's own thread; it is marshalled back
/// through the dispatcher with the usual null and HasShutdownStarted guards before Ended is
/// raised, so subscribers never have to think about threads.
/// </summary>
public sealed class TrackPlayer : IDisposable
{
    /// <summary>Raised once when the file plays out to its end. A deliberate Stop does not raise it.</summary>
    public event Action? Ended;

    private AudioFileReader? _reader;
    private WaveOutEvent? _out;
    /// <summary>True while a Stop, a restart or a teardown is the reason playback is ending.</summary>
    private bool _stopping;
    /// <summary>Latches so a device that reports PlaybackStopped twice still ends the track once.</summary>
    private bool _endedFired;
    private float _gain = 1f;
    private bool _disposed;

    /// <summary>The track's own gain, 0..1. The master volume multiplies it; the host may leave it at 1.</summary>
    public float Volume
    {
        get => _gain;
        set { _gain = Math.Clamp(value, 0f, 1f); ApplyVolume(); }
    }

    /// <summary>Where the reader is, in seconds. 0 with no file loaded.</summary>
    public double PositionSec
    {
        get { try { return _reader?.CurrentTime.TotalSeconds ?? 0; } catch { return 0; } }
    }

    /// <summary>The loaded file's length in seconds. 0 with no file loaded.</summary>
    public double DurationSec
    {
        get { try { return _reader?.TotalTime.TotalSeconds ?? 0; } catch { return 0; } }
    }

    /// <summary>True only while the device is actually pushing samples (paused reads false).</summary>
    public bool IsPlaying
    {
        get { try { return _out?.PlaybackState == PlaybackState.Playing; } catch { return false; } }
    }

    /// <summary>True once a file is loaded, whatever the transport is doing.</summary>
    public bool HasTrack => _reader != null;

    /// <summary>
    /// Open a file for playback, disposing whatever was loaded before. Throws on a file NAudio
    /// cannot open, so the caller keeps this inside its own try.
    /// </summary>
    public void Load(string path)
    {
        if (_disposed) return;
        Unload();
        var reader = new AudioFileReader(path);
        // 200 ms of buffer: the clock reads CurrentTime, so a longer buffer would drift the
        // page's idea of the track ahead of what the room actually hears.
        var device = new WaveOutEvent { DesiredLatency = 200 };
        device.PlaybackStopped += OnPlaybackStopped;
        device.Init(reader);
        _reader = reader;
        _out = device;
        _stopping = false;
        _endedFired = false;
        ApplyVolume();
    }

    /// <summary>Start the track from the beginning, restarting it if it is already running.</summary>
    public void Play()
    {
        if (_disposed || _reader == null || _out == null) return;
        try
        {
            _stopping = true;                 // the restart's own stop must not read as an ending
            if (_out.PlaybackState != PlaybackState.Stopped) _out.Stop();
            _reader.CurrentTime = TimeSpan.Zero;
            _stopping = false;
            _endedFired = false;
            ApplyVolume();
            _out.Play();
        }
        catch (Exception ex) { App.Logger?.Warning("TrackPlayer.Play: {E}", ex.Message); }
    }

    /// <summary>Hold the track where it is. The reader keeps its position.</summary>
    public void Pause()
    {
        if (_disposed || _out == null) return;
        try { if (_out.PlaybackState == PlaybackState.Playing) _out.Pause(); }
        catch (Exception ex) { App.Logger?.Warning("TrackPlayer.Pause: {E}", ex.Message); }
    }

    /// <summary>Carry on from where Pause left the reader.</summary>
    public void Resume()
    {
        if (_disposed || _out == null) return;
        try
        {
            if (_out.PlaybackState == PlaybackState.Paused)
            {
                ApplyVolume();
                _out.Play();
            }
        }
        catch (Exception ex) { App.Logger?.Warning("TrackPlayer.Resume: {E}", ex.Message); }
    }

    /// <summary>End playback for good. Never raises Ended: the run asked for this.</summary>
    public void Stop()
    {
        if (_out == null) return;
        try
        {
            _stopping = true;
            if (_out.PlaybackState != PlaybackState.Stopped) _out.Stop();
            if (_reader != null) _reader.CurrentTime = TimeSpan.Zero;
        }
        catch (Exception ex) { App.Logger?.Warning("TrackPlayer.Stop: {E}", ex.Message); }
    }

    /// <summary>Re-read the master volume, for a settings change mid track.</summary>
    public void RefreshVolume() => ApplyVolume();

    private void ApplyVolume()
    {
        try
        {
            // The same shape every other cue in the app uses (see ChaosSfx.Volume): the app's
            // master percentage scales the caller's own gain.
            float master = Math.Clamp((App.Settings?.Current?.MasterVolume ?? 100) / 100f, 0f, 1f);
            if (_reader != null) _reader.Volume = Math.Clamp(master * _gain, 0f, 1f);
        }
        catch { }
    }

    /// <summary>
    /// NAudio's device thread. A stop we asked for, or a reader short of its end, is not an
    /// ending; anything else is the file running out and raises Ended once, on the UI thread.
    /// </summary>
    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
            App.Logger?.Warning("TrackPlayer: playback stopped with {E}", e.Exception.Message);
        if (_stopping || _endedFired || _disposed) return;
        bool atEnd;
        try
        {
            var r = _reader;
            // A quarter second of slack: the device reports stopped a buffer short of the tail.
            atEnd = r == null || r.Position >= r.Length
                    || r.CurrentTime >= r.TotalTime - TimeSpan.FromMilliseconds(250);
        }
        catch { atEnd = true; }
        if (!atEnd) return;
        _endedFired = true;

        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        disp.BeginInvoke(() =>
        {
            try { Ended?.Invoke(); }
            catch (Exception ex) { App.Logger?.Warning("TrackPlayer.Ended handler: {E}", ex.Message); }
        });
    }

    /// <summary>Close the device and the reader, leaving the player reusable through Load.</summary>
    public void Unload()
    {
        _stopping = true;
        var device = _out;
        var reader = _reader;
        _out = null;
        _reader = null;
        try
        {
            if (device != null)
            {
                device.PlaybackStopped -= OnPlaybackStopped;
                try { device.Stop(); } catch { }
                device.Dispose();
            }
        }
        catch (Exception ex) { App.Logger?.Debug("TrackPlayer.Unload device: {E}", ex.Message); }
        try { reader?.Dispose(); }
        catch (Exception ex) { App.Logger?.Debug("TrackPlayer.Unload reader: {E}", ex.Message); }
    }

    /// <summary>Close everything down. The player is finished after this.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unload();
        Ended = null;
    }
}
