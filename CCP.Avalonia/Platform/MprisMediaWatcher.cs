// The Linux now-playing watcher: MPRIS over the session D-Bus (the freedesktop twin of Windows SMTC,
// WPF Services/Awareness/SmtcMediaWatcher.cs). Same shape and numbers: a 3 s poll while started, one
// read in flight, every path wrapped, failure = the signal is simply off. Raw Tmds.DBus.Protocol like
// OsNotifications and PortalPanicShortcut; its own connection, opened on Start and dropped on Dispose.
//
// Privacy as WPF: titles and artists stay in memory on this machine; nothing here writes a file or
// logs what is playing.
//
// Limits: a player that does not speak MPRIS (some Flatpak sandboxes hide the bus name, some browsers
// need a setting) is not seen. With several players, one that is Playing wins, else the first found.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Awareness;
using Serilog;
using Tmds.DBus.Protocol;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>The platform's now-playing watcher, or none.</summary>
internal static class MediaAwareness
{
    /// <summary>WPF SmtcMediaWatcher.PollInterval: media does not need 1.5 s resolution.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    /// <summary>Windows: SMTC. Linux: MPRIS. Anything else: null (the media signal is off). The
    /// watcher is idle until <see cref="IMediaWatcher.Start"/>: creating one opens nothing.</summary>
    internal static IMediaWatcher? Create()
    {
#if CCP_WINRT
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return new SmtcMediaWatcher();
#endif
        if (OperatingSystem.IsLinux()) return new MprisMediaWatcher();
        return null;
    }
}

internal sealed class MprisMediaWatcher : IMediaWatcher
{
    internal const string Prefix = "org.mpris.MediaPlayer2.";
    private const string PlayerPath = "/org/mpris/MediaPlayer2", PlayerIface = "org.mpris.MediaPlayer2.Player";

    private readonly object _lock = new();
    private DBusConnection? _bus;
    private Timer? _timer;
    private MediaSample? _current;
    private volatile bool _available;
    private int _reading;
    private bool _started;
    private bool _disposed;

    public MediaSample? Current { get { lock (_lock) return _current; } }
    public bool IsAvailable => _available;

    public void Start()
    {
        if (_disposed || _started || !OperatingSystem.IsLinux()) return;
        _started = true;
        if (_bus != null)
        {
            _available = true;
            try { _timer?.Change(TimeSpan.Zero, MediaAwareness.PollInterval); return; }
            catch (ObjectDisposedException) { _timer = null; }
        }
        _ = Task.Run(async () =>
        {
            try
            {
                if (_bus == null)
                {
                    if (DBusAddress.Session is not { } address) return;   // no session bus: the signal is off
                    var bus = new DBusConnection(address);
                    await bus.ConnectAsync();
                    if (_disposed) { bus.Dispose(); return; }
                    _bus = bus;
                }
                if (!_started) return;
                _available = true;
                _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, MediaAwareness.PollInterval);
                Log.Debug("Media awareness: MPRIS watcher armed");
            }
            catch (Exception ex)
            {
                _available = false;
                Log.Debug("Media awareness: MPRIS unavailable - {Error}", ex.Message);
            }
        });
    }

    public void Stop()
    {
        _started = false;
        try { _timer?.Change(Timeout.Infinite, Timeout.Infinite); } catch (ObjectDisposedException) { }
        lock (_lock) _current = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _timer?.Dispose(); } catch { }
        _timer = null;
        try { _bus?.Dispose(); } catch { }
        _bus = null;
        _available = false;
        lock (_lock) _current = null;
    }

    private void Poll()
    {
        if (_disposed || !_started) return;
        if (Interlocked.Exchange(ref _reading, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var sample = await ReadAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                if (_disposed || !_started) return;
                lock (_lock) _current = sample;
            }
            catch (Exception ex) { Log.Debug("Media awareness: MPRIS read failed - {Error}", ex.Message); }
            finally { Interlocked.Exchange(ref _reading, 0); }
        });
    }

    private async Task<MediaSample?> ReadAsync()
    {
        var bus = _bus;
        if (bus == null) return null;
        var names = await bus.CallMethodAsync(
            Call(bus, "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ListNames", null, null),
            (Message m, object? _) => m.GetBodyReader().ReadArrayOfString());

        var found = new List<MediaSample>();
        foreach (var name in names)
        {
            if (!name.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            try
            {
                var status = (await GetAsync(bus, name, "PlaybackStatus")).GetString();
                var metadata = await GetAsync(bus, name, "Metadata");
                var position = TimeSpan.Zero;
                try { position = FromMicroseconds((await GetAsync(bus, name, "Position")).GetInt64()); } catch { /* optional */ }
                var (title, artist) = ReadMetadata(metadata);
                if (Sample(title, artist, status, position) is { } sample) found.Add(sample);
            }
            catch { /* a player that vanished or answers oddly is skipped */ }
        }
        return Pick(found);
    }

    private static Task<VariantValue> GetAsync(DBusConnection bus, string destination, string property) =>
        bus.CallMethodAsync(
            Call(bus, destination, PlayerPath, "org.freedesktop.DBus.Properties", "Get", "ss",
                (ref MessageWriter w) => { w.WriteString(PlayerIface); w.WriteString(property); }),
            (Message m, object? _) => m.GetBodyReader().ReadVariantValue());

    private delegate void BodyWriter(ref MessageWriter w);   // by ref: MessageWriter is a struct

    private static MessageBuffer Call(DBusConnection bus, string destination, string path, string iface, string member,
        string? signature, BodyWriter? body)
    {
        var w = bus.GetMessageWriter();
        w.WriteMethodCallHeader(destination: destination, path: path, @interface: iface, member: member, signature: signature);
        body?.Invoke(ref w);
        return w.CreateMessage();
    }

    /// <summary>xesam:title (s) and the first xesam:artist (as) out of an MPRIS Metadata a{sv}.</summary>
    private static (string Title, string? Artist) ReadMetadata(VariantValue metadata)
    {
        string title = "";
        string? artist = null;
        for (var i = 0; i < metadata.Count; i++)
        {
            var e = metadata.GetDictionaryEntry(i);
            var v = e.Value.Type == VariantValueType.Variant ? e.Value.GetVariantValue() : e.Value;
            switch (e.Key.GetString())
            {
                case "xesam:title":
                    if (v.Type == VariantValueType.String) title = v.GetString();
                    break;
                case "xesam:artist":
                    if (v.Type == VariantValueType.Array && v.Count > 0) artist = v.GetItem(0).GetString();
                    else if (v.Type == VariantValueType.String) artist = v.GetString();
                    break;
            }
        }
        return (title, artist);
    }

    // ------------------------------------------------------------------ pure rules (tested)

    /// <summary>MPRIS Position is microseconds.</summary>
    internal static TimeSpan FromMicroseconds(long us) => us <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(us * 10);

    /// <summary>One player's reading as a sample; a blank title is nothing playing (WPF ReadAsync).
    /// MPRIS says Playing / Paused / Stopped, the same words SMTC's enum prints.</summary>
    internal static MediaSample? Sample(string? title, string? artist, string? status, TimeSpan position)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        return new MediaSample(title, string.IsNullOrWhiteSpace(artist) ? null : artist,
            string.IsNullOrWhiteSpace(status) ? "Unknown" : status, position);
    }

    /// <summary>Several players: the one that is playing, else the first (SMTC's "current session").</summary>
    internal static MediaSample? Pick(IReadOnlyList<MediaSample> samples)
    {
        foreach (var s in samples)
            if (s.IsPlaying) return s;
        return samples.Count > 0 ? samples[0] : null;
    }
}
