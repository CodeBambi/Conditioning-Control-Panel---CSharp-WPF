namespace CcpSpike.VideoHandoff;

public sealed record MatrixRow(string Id, ProbeOutcome Expected, ProbeOutcome Actual, bool Pass, string Detail);

/// <summary>
/// Decode-level matrix (M1-M8): every row's expected outcome is PRE-DECLARED before the run.
/// Browser-layer rows (M5 discovery, M9 blob:, M10 DRM) are Step 3 (Windows) / Step 4 (Linux named limits).
/// </summary>
public static class Matrix
{
    public static async Task<List<MatrixRow>> RunDecodeLevelAsync(Lab lab)
    {
        var probe = new Probe();
        var rows = new List<MatrixRow>();

        async Task Add(string id, ProbeOutcome expected, Func<Task<ProbeReport>> run, string detail)
        {
            var report = await run();
            // Direct-auth negative rows pass when the decoder does NOT succeed (typed limitation observed).
            var pass = expected switch
            {
                ProbeOutcome.Success => report.Outcome == ProbeOutcome.Success,
                _ => report.Outcome == expected || (expected == ProbeOutcome.DecodeFailed && report.Outcome != ProbeOutcome.Success),
            };
            rows.Add(new MatrixRow(id, expected, report.Outcome, pass, detail));
            SpikeLog.Line("matrix", $"row {id} expected={expected} actual={report.Outcome} pass={pass} vtrack={report.VideoTrack ?? "-"} dur={report.DurationMs} tc={report.TimeChanges} end={report.EndReached} detail={detail}");
        }

        // M1 direct MP4
        await Add("M1-direct-mp4", ProbeOutcome.Success,
            () => probe.RunAsync(lab.Url("/media/clip.mp4")), "plain MP4 over loopback HTTP");
        // M2 direct WebM
        await Add("M2-direct-webm", ProbeOutcome.Success,
            () => probe.RunAsync(lab.Url("/media/clip.webm")), "plain WebM over loopback HTTP");
        // M3 HLS (fMP4 primary variant, EXT-X-MAP; consult trap: report actual playlist shape served)
        await Add("M3-hls-fmp4", ProbeOutcome.Success,
            () => probe.RunAsync(lab.Url("/hls-fmp4/vod.m3u8"), manifestKind: "hls"), "HLS v7 fMP4 VOD playlist (EXT-X-MAP + one .m4s)");
        // M3b HLS (MPEG-TS variant)
        await Add("M3b-hls-ts", ProbeOutcome.Success,
            () => probe.RunAsync(lab.Url("/hls-ts/vod.m3u8"), manifestKind: "hls"), "HLS MPEG-TS VOD playlist");
        // M4 DASH
        await Add("M4-dash", ProbeOutcome.Success,
            () => probe.RunAsync(lab.Url("/dash/vod.mpd"), manifestKind: "dash"), "DASH static MPD (SegmentTemplate, fMP4)");
        // M6 cookies — three shapes (consult §1.5b: split evidence classes)
        await Add("M6-direct-decoder-auth", ProbeOutcome.DecodeFailed,
            () => probe.RunAsync(lab.Url("/gated-cookie/clip.mp4"),
                mediaOptions: new[] { $":http-cookie={lab.CookieValue}" }, forceDecoderAttempt: true),
            "libvlc media-option direct cookie attempt — expected UNSUPPORTED (no :http-cookie in libvlc 3.x); lab gate log is the arbiter");
        await Add("M6-relay-mediated", ProbeOutcome.Success,
            () => probe.RunAsync(lab.Url("/relay/cookie/clip.mp4")),
            "relay injects cookie upstream (proxy-mediated auth; strategy evidence, pending-owner)");
        await Add("M6-relay-negative-control", ProbeOutcome.AuthRequired,
            () => probe.RunAsync(lab.Url("/relay/nocookie/clip.mp4")),
            "relay WITHOUT injection must propagate upstream 401 — no decode events");
        // M7 custom headers — same three shapes
        await Add("M7-direct-decoder-auth", ProbeOutcome.DecodeFailed,
            () => probe.RunAsync(lab.Url("/gated-header/clip.mp4"), forceDecoderAttempt: true),
            "no arbitrary-header mechanism in libvlc 3.x media options; raw open must fail at the gate");
        await Add("M7-relay-mediated", ProbeOutcome.Success,
            () => probe.RunAsync(lab.Url("/relay/header/clip.mp4")),
            "relay injects X-Spike-Gate upstream (proxy-mediated auth; strategy evidence, pending-owner)");
        await Add("M7-relay-negative-control", ProbeOutcome.AuthRequired,
            () => probe.RunAsync(lab.Url("/relay/noheader/clip.mp4")),
            "relay WITHOUT injection must propagate upstream 403 — no decode events");
        // M8 expiring signed URLs (mid-stream expiry = named limit, untested by design)
        await Add("M8-signed-valid", ProbeOutcome.Success,
            () => probe.RunAsync(lab.SignedUrl("/signed/clip.mp4", DateTimeOffset.UtcNow.AddSeconds(300))),
            "HMAC-signed URL, TTL 300s, opened before expiry");
        await Add("M8-signed-expired", ProbeOutcome.SourceExpired,
            () => probe.RunAsync(lab.SignedUrl("/signed/clip.mp4", DateTimeOffset.UtcNow.AddSeconds(-60))),
            "pre-expired signed URL → typed source-expired at preflight, ZERO decoder opens, no retry-storm");
        await Add("M8-signed-badsig", ProbeOutcome.AuthRequired,
            () => { Redact.Register("sig", "deadbeef"); return probe.RunAsync(lab.Url("/signed/clip.mp4?exp=9999999999&sig=deadbeef")); },
            "tampered signature → 403 bad-signature");

        // Gate-side arbiter log (what credentials the decoder actually presented per gate).
        foreach (var obs in lab.GateObservations)
            SpikeLog.Line("matrix", $"gate-observation {obs}");

        return rows;
    }
}
