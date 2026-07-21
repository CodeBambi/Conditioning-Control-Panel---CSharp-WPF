using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace CcpSpike.VideoHandoff;

public enum ProbeOutcome
{
    Success,            // track metadata + frame-level decode + time progression + EndReached
    AuthRequired,       // preflight 401/403 gate shape; decoder produced no events (direct auth unsupported)
    SourceExpired,      // preflight 403 with "expired" marker
    ManifestInvalid,    // preflight manifest sanity-parse failed
    SourceUnreachable,  // preflight connect failure / 404
    DecodeFailed,       // preflight OK but decoder events missing/incomplete
}

public sealed record ProbeReport(
    ProbeOutcome Outcome,
    int? PreflightStatus,
    string? VideoTrack,     // codec WxH (demuxer-observed)
    string? AudioTrack,     // codec rate (demuxer-observed)
    long DurationMs,        // demuxer-observed
    int FramesDecoded,      // vmem display callbacks (frame-level decode proof)
    int TimeChanges,        // decoder TimeChanged event count
    long MaxTimeMs,         // furthest decoder-reported position
    bool EndReached,        // decoder EndReached event
    string Detail);

/// <summary>
/// Native decode probe. Success claims come ONLY from native-decoder events (parse track
/// metadata + vmem frame delivery + TimeChanged progression + EndReached) — never from HTTP 200
/// or call returns (packet honesty framing f). Presentation-free: vout = vmem memory callbacks
/// (the unified-video row owns presentation); audio decoded through aout=dummy.
///
/// Findings baked in (see record.md):
///  V1 — D3D11VA hw decode (libvlc default) segfaults this box even parsing a local file → sw decode.
///  V2 — dummy-vout sw-decode pipeline crashes ~at/after EndReached on a background thread
///       (dummy vout AND --no-audio both excluded by isolation; --no-video is stable) → vmem vout.
///  V3 — libvlc native teardown (release in ANY order, both admitted and WPF-pinned version
///       combos) segfaults → the spike hard-exits after flushing evidence; clean teardown is
///       owned by the unified-video row.
/// </summary>
public sealed class Probe
{
    // Pre-declared success thresholds (consult §1.5c trap 5 — declared BEFORE runs):
    public const long MinDurationMs = 1500;          // fixture is 2000ms; accept [1500, 2500]
    public const long MaxDurationMs = 2500;
    public const int MinFrames = 5;                  // 2s @ 10fps = 20 frames expected
    public const int MinTimeChanges = 3;             // real progression, not a single jump
    public const long MinProgressSpanMs = 1000;      // positions must span >= 1s ...
    public const float MinProgressPosition = 0.5f;   // ... or position >= 0.5 ...
    // ... or frame-paced wall-clock playback (finding V8: adaptive demuxers FLAKILY report
    // neither Time nor Position across runs; delivered frames + wall-time-to-end is the
    // timeline progression evidence there).
    public const long MinProgressWallMs = 1500;
    public const int EndWindowMs = 12000;            // EndReached must arrive within 12s of Play
    public const int ParseTimeoutMs = 10000;

    private const int FrameW = 96, FrameH = 96, FrameBytes = FrameW * FrameH * 4; // RV32

    private readonly LibVLC _vlc;
    private readonly byte[] _frameBuffer = new byte[FrameBytes];
    private readonly GCHandle _framePin;
    private int _frames;

    public Probe()
    {
        Core.Initialize();
        _vlc = new LibVLC("--no-video-title-show", "--network-caching=400", "--avcodec-hw=none", "--aout=dummy");
        _framePin = GCHandle.Alloc(_frameBuffer, GCHandleType.Pinned);
    }

    /// <summary>
    /// Run one URL probe: preflight classification (redaction-safe) then decoder attempt.
    /// <paramref name="mediaOptions"/> are libvlc media options for the direct-decoder-auth
    /// attempts (their values are pre-registered with Redact by the caller).
    /// <paramref name="forceDecoderAttempt"/>: direct-auth rows must exercise the DECODER against
    /// the gate (preflight alone would never test libvlc's credential behavior).
    /// </summary>
    public async Task<ProbeReport> RunAsync(string url, string? manifestKind = null, string[]? mediaOptions = null, bool forceDecoderAttempt = false)
    {
        var preflight = await Preflight(url, manifestKind);
        SpikeLog.Line("probe", $"preflight url={Redact.Scrub(url)} status={preflight.status} detail={preflight.detail}");

        ProbeOutcome? typed = preflight.status switch
        {
            401 => ProbeOutcome.AuthRequired,
            403 when preflight.detail.Contains("expired", StringComparison.Ordinal) => ProbeOutcome.SourceExpired,
            403 => ProbeOutcome.AuthRequired,
            404 => ProbeOutcome.SourceUnreachable,
            -1 => ProbeOutcome.SourceUnreachable,
            -2 => ProbeOutcome.ManifestInvalid,
            >= 200 and < 300 => null,
            _ => ProbeOutcome.DecodeFailed,
        };
        if (typed is { } early && !forceDecoderAttempt)
            return new ProbeReport(early, preflight.status, null, null, 0, 0, 0, 0, false, "preflight-classified; decoder not attempted on a known-failing gate");

        var media = new Media(_vlc, url, FromType.FromLocation, mediaOptions ?? Array.Empty<string>());
        MediaParsedStatus parseStatus;
        try
        {
            using var cts = new CancellationTokenSource(ParseTimeoutMs);
            parseStatus = await media.Parse(MediaParseOptions.ParseNetwork, ParseTimeoutMs, cts.Token);
        }
        catch (Exception ex)
        {
            SpikeLog.Line("probe", $"parse threw url={Redact.Scrub(url)} {ex.GetType().Name}");
            return new ProbeReport(ProbeOutcome.DecodeFailed, preflight.status, null, null, 0, 0, 0, 0, false, $"parse threw {ex.GetType().Name}");
        }
        if (parseStatus != MediaParsedStatus.Done)
        {
            SpikeLog.Line("probe", $"parse status={parseStatus} url={Redact.Scrub(url)}");
            return new ProbeReport(ProbeOutcome.DecodeFailed, preflight.status, null, null, 0, 0, 0, 0, false, $"ParsedStatus={parseStatus}");
        }

        var (vTrack, aTrack) = await DescribeDeep(media);
        var durationMs = media.Duration;
        var play = await PlayToEnd(media, url);
        // Track evidence: demuxer parse when available; decoder-format-observed otherwise
        // (HLS playlists expose no tracks at playlist level — finding V4).
        var trackEvidence = vTrack ?? (play.FormatObserved is { } f ? $"decoded:{f}" : null);
        var progressed = play.MaxTime >= MinProgressSpanMs || play.MaxPosition >= MinProgressPosition
            || (play.Frames >= MinFrames && play.WallMs >= MinProgressWallMs); // V8 fallback, pre-declared
        var success = play.EndReached
            && durationMs is >= MinDurationMs and <= MaxDurationMs
            && play.Frames >= MinFrames
            && play.Times.Length >= MinTimeChanges
            && progressed
            && trackEvidence is not null;

        SpikeLog.Line("probe", $"decode url={Redact.Scrub(url)} vtrack={trackEvidence ?? "none"} atrack={aTrack ?? "none"} dur={durationMs} frames={play.Frames} timeChanges={play.Times.Length} maxT={play.MaxTime} maxPos={play.MaxPosition:F2} wall={play.WallMs} end={play.EndReached} => {(success ? "SUCCESS" : "FAIL")}");
        return new ProbeReport(
            success ? ProbeOutcome.Success : ProbeOutcome.DecodeFailed,
            preflight.status, trackEvidence, aTrack, durationMs, play.Frames, play.Times.Length, play.MaxTime, play.EndReached,
            success ? "decoder-event-verified (parse + vmem frames + progression + end)" : "decoder events incomplete vs pre-declared thresholds");
    }

    /// <summary>File-path variant for fault isolation (no HTTP/lab involvement).</summary>
    public async Task<ProbeReport> RunFileAsync(string path)
    {
        var media = new Media(_vlc, path, FromType.FromPath);
        using var cts = new CancellationTokenSource(ParseTimeoutMs);
        var parseStatus = await media.Parse(MediaParseOptions.ParseLocal, ParseTimeoutMs, cts.Token);
        SpikeLog.Line("probe", $"file parse status={parseStatus} path={path}");
        if (parseStatus != MediaParsedStatus.Done)
            return new ProbeReport(ProbeOutcome.DecodeFailed, null, null, null, 0, 0, 0, 0, false, $"ParsedStatus={parseStatus}");
        var (vTrack, aTrack) = await DescribeDeep(media);
        var play = await PlayToEnd(media, path);
        var trackEvidence = vTrack ?? (play.FormatObserved is { } f ? $"decoded:{f}" : null);
        var ok = play.EndReached && trackEvidence is not null && play.Frames >= MinFrames && play.Times.Length >= MinTimeChanges;
        return new ProbeReport(ok ? ProbeOutcome.Success : ProbeOutcome.DecodeFailed, null, trackEvidence, aTrack,
            media.Duration, play.Frames, play.Times.Length, play.MaxTime, play.EndReached, "file selftest");
    }

    private static async Task<(string? V, string? A)> DescribeDeep(Media media, int depth = 0)
    {
        var direct = Describe(media);
        // HLS playlist parse reports duration but no tracks at playlist level; the segment
        // sub-items carry the real ES metadata (finding V4 — observed on both fMP4 and TS variants).
        var items = media.SubItems;
        if (direct.V is not null || depth >= 2 || items is null || items.Count == 0) return direct;
        var sub = items[0] ?? throw new InvalidOperationException("null sub-item");
        try
        {
            using var cts = new CancellationTokenSource(ParseTimeoutMs);
            var st = await sub.Parse(MediaParseOptions.ParseNetwork, ParseTimeoutMs, cts.Token);
            SpikeLog.Line("probe", $"describe-deep subitems={items.Count} sub-parse={st} sub-tracks={sub.Tracks?.Length ?? 0}");
            if (st != MediaParsedStatus.Done) return direct;
        }
        catch (Exception ex) { SpikeLog.Line("probe", $"describe-deep threw {ex.GetType().Name}"); return direct; }
        return await DescribeDeep(sub, depth + 1);
    }

    private static (string? V, string? A) Describe(Media media)
    {
        string? vTrack = null, aTrack = null;
        foreach (var t in media.Tracks)
        {
            if (t.TrackType == TrackType.Video)
                vTrack = $"{FourCC(t.Codec)} {t.Data.Video.Width}x{t.Data.Video.Height}";
            else if (t.TrackType == TrackType.Audio)
                aTrack = $"{FourCC(t.Codec)} {t.Data.Audio.Rate}Hz";
        }
        return (vTrack, aTrack);
    }

    private sealed record PlayResult(int Frames, long[] Times, long MaxTime, float MaxPosition, long WallMs, string? FormatObserved, bool EndReached);

    private string? _fmtObserved;

    private async Task<PlayResult> PlayToEnd(Media media, string what)
    {
        var player = new MediaPlayer(media);
        var endTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var times = new List<long>();
        var maxPos = 0f;
        Interlocked.Exchange(ref _frames, 0);
        _fmtObserved = null;
        player.SetVideoFormatCallbacks(FormatCallback, null);
        player.SetVideoCallbacks(LockFrame, null!, DisplayFrame);
        player.TimeChanged += (_, e) => { lock (times) times.Add(e.Time); };
        player.PositionChanged += (_, e) => { if (e.Position > maxPos) maxPos = e.Position; };
        player.EndReached += (_, _) => endTcs.TrySetResult();
        player.EncounteredError += (_, _) => SpikeLog.Line("probe", $"EncounteredError src={Redact.Scrub(what)}");
        player.Play();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var endDone = await Task.WhenAny(endTcs.Task, Task.Delay(EndWindowMs));
        sw.Stop();
        var endReached = endDone == endTcs.Task;
        long[] snapshot;
        lock (times) snapshot = times.ToArray();
        return new PlayResult(_frames, snapshot, snapshot.Length > 0 ? snapshot.Max() : 0, maxPos, sw.ElapsedMilliseconds, _fmtObserved, endReached);
    }

    // Decoder-observed format (chroma/dims proposed by the DECODER, then we request RV32).
    private uint FormatCallback(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        var proposed = $"{Marshal.PtrToStringAnsi(chroma, 4)} {width}x{height}";
        _fmtObserved = proposed;
        var rv32 = System.Text.Encoding.ASCII.GetBytes("RV32");
        Marshal.Copy(rv32, 0, chroma, 4);
        width = FrameW; height = FrameH;
        pitches = FrameW * 4; lines = FrameH;
        return 1;
    }

    // vmem callbacks: frame-level decode proof (the decoder must produce real frames for these to fire).
    private IntPtr LockFrame(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, _framePin.AddrOfPinnedObject());
        return _framePin.AddrOfPinnedObject();
    }

    private void DisplayFrame(IntPtr opaque, IntPtr picture) => Interlocked.Increment(ref _frames);

    /// <summary>
    /// Typed-limitation classification layer (consult §1.5c trap 4: libvlc EncounteredError is
    /// generic; 401-vs-403-vs-expired classification lives HERE, redaction-safe).
    /// manifestKind: "hls" | "dash" | null — sanity-parse the manifest body.
    /// </summary>
    private static async Task<(int status, string detail)> Preflight(string url, string? manifestKind)
    {
        try
        {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            using var res = await http.GetAsync(url);
            var status = (int)res.StatusCode;
            var body = status >= 400 ? await res.Content.ReadAsStringAsync() : "";
            if (status >= 400) return (status, body.Trim());

            if (manifestKind == "hls")
            {
                var text = await res.Content.ReadAsStringAsync();
                if (!text.StartsWith("#EXTM3U", StringComparison.Ordinal)) return (-2, "hls-missing-EXTM3U");
            }
            else if (manifestKind == "dash")
            {
                var text = await res.Content.ReadAsStringAsync();
                if (!text.Contains("<MPD", StringComparison.Ordinal)) return (-2, "dash-missing-MPD");
            }
            return (status, "ok");
        }
        catch (Exception ex)
        {
            return (-1, ex.GetType().Name);
        }
    }

    private static string FourCC(uint code)
    {
        var bytes = BitConverter.GetBytes(code);
        return new string(bytes.Select(b => b is >= 32 and < 127 ? (char)b : '?').ToArray());
    }

    /// <summary>
    /// Finding V3: libvlc native teardown (media/player release, instance release, atexit)
    /// segfaults in this probe shape on this box — reproduced across LibVLCSharp 3.10.0+3.0.23.1
    /// AND the WPF-pinned 3.8.5+3.0.21, every disposal order. Decode evidence is unaffected and
    /// always logged BEFORE teardown. The spike hard-exits after flushing evidence; clean
    /// teardown is owned by the unified-video row.
    /// </summary>
    public static void HardExit(int code)
    {
        if (OperatingSystem.IsWindows()) WindowsExit(code);
        else LinuxExit(code);
        Environment.Exit(code); // unreachable, keeps the compiler happy
    }

    [DllImport("msvcrt", EntryPoint = "_exit")]
    private static extern void WindowsExit(int code);

    [DllImport("libc", EntryPoint = "_exit")]
    private static extern void LinuxExit(int code);
}
