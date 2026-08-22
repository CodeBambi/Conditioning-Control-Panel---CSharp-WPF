using System.Security.Cryptography;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Video;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-140 — the first REAL COMPRESSED CLIP this port has ever decoded.
///
/// <para><b>What every video fact before this one ran against.</b> An uncompressed 32bpp
/// <c>BI_RGB</c> AVI that <see cref="TestAvi"/> writes in pure managed code. Media Foundation opens
/// it, but its native media type is already <c>MFVideoFormat_RGB32</c>: no codec runs, and the
/// source reader's video processor — the thing <c>MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING</c>
/// turns on at <c>MediaFoundationClipSource.cs:140-141</c> — has nothing to convert. That is
/// survivor <b>M-y</b>, and the facts below close it: this clip's stream is <b>H.264</b>, its
/// decoder does not produce RGB32, and a BGRX frame comes back anyway.</para>
///
/// <para><b>THE FIXTURE'S PROVENANCE, AND WHY IT IS NOT CIRCULAR.</b> A fixture produced by Media
/// Foundation's own sink writer would prove close to nothing here — the encoder and the decoder
/// would be the same stack, and a container it cannot handle is exactly what it would never
/// produce. This fixture is not that. It is
/// <c>client/spikes/CcpSpike.VideoHandoff/fixtures/clip.mp4</c>, committed by SP-018 at
/// <c>f21a7c011</c>, 122 commits before this packet existed: <b>ffmpeg</b> (gyan.dev full build
/// 2025-06-04) encoding <c>lavfi</c>'s synthetic <c>testsrc2</c> pattern through <b>x264</b>, 96x96
/// at 10 fps for 2 s, H.264 + AAC, <c>+faststart</c>. Its SHA-256 and its recipe are recorded in
/// <c>client/docs/video-handoff-spike.md:21</c>. <b>Media Foundation produced no byte of it</b>,
/// and it is synthetic, so nothing personal and nothing copyrighted is committed.
/// <see cref="TheFixtureIsTheFfmpegArtefactWhoseProvenanceIsRecorded_NotAnythingMediaFoundationCouldHaveMade"/>
/// pins that mechanically rather than trusting this paragraph.</para>
///
/// <para><b>WHAT THIS DOES NOT CLOSE, stated here so it cannot be read as closed.</b> Survivor
/// <b>M-w</b> — the openable format set against real files (D124) — is <b>NOT</b> closed and no
/// single committed fixture can close it: one file bounds one format. The board's acceptance ("ONE
/// compressed fixture closes both survivors") is wrong on that half, and SP-140's <c>record.md</c>
/// records it as a spec-versus-reality discrepancy. What bounds M-w is a MEASUREMENT over a real
/// library, which is evidence for the record and deliberately not a test: it depends on a directory
/// that exists on one machine, and a test that skipped when that directory was absent would be the
/// vacuous-green shape this repository bans. D323 carries the number.</para>
///
/// <para><b>Evidence class: decode only. Nothing here is <c>presentation-verified</c>.</b> A decoded
/// frame in memory is not a composited pixel; <c>client/docs/verification-harness.md</c> governs and
/// no in-memory frame ever discharges a headed gate. <b>Cadence, order and timing stay unmeasured:
/// a clip playing at half speed or backwards would pass every fact in this file.</b></para>
///
/// <para><b>Orientation is NOT this fixture's fact.</b> <see cref="TestAvi"/> can prove it because
/// the writer chooses which half is which; <c>testsrc2</c>'s layout has no independent oracle in
/// this repository, so pinning an observed top/bottom asymmetry would pin whatever the decoder did
/// on the day. Orientation stays <c>VideoCapabilityTests</c>'s fact, and
/// <see cref="TheStrideForThisRealContainerIsPOSITIVE_SoTheFLIPBranchIsTheAviFixturesAlone"/>
/// records why the two fixtures are complements rather than duplicates.</para>
/// </summary>
public class RealClipDecodeTests
{
    /// <summary>The recipe's own output, byte for byte.</summary>
    private const string FixtureSha256 = "eb14abd63a02a22029c513a4b512e2cecad34b2b0c9e31994030753c5d769fbc";

    private const int ClipWidth = 96;

    private const int ClipHeight = 96;

    /// <summary>2 s at 10 fps, exhausted once and measured — not the encoder's claim.</summary>
    private const int ClipFrames = 20;

    private static readonly string[] FixtureParts =
        ["client", "spikes", "CcpSpike.VideoHandoff", "fixtures", "clip.mp4"];

    private static readonly string[] ProvenanceDocParts = ["client", "docs", "video-handoff-spike.md"];

    // -------------------------------------------------------------------------------------------
    //  PROVENANCE — the anti-circularity pin
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void TheFixtureIsTheFfmpegArtefactWhoseProvenanceIsRecorded_NotAnythingMediaFoundationCouldHaveMade()
    {
        var actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(FixturePath())));

        Assert.Equal(FixtureSha256, actual);

        // The bytes are bound to the SENTENCE that says where they came from. A fixture swapped for
        // one Media Foundation encoded fails the hash above; a provenance claim quietly deleted
        // from the docs fails here. Either alone leaves the circularity question open.
        var doc = File.ReadAllText(Path.Combine([FindRepoRoot(), .. ProvenanceDocParts]));
        Assert.Contains("testsrc2", doc, StringComparison.Ordinal);
        Assert.Contains(FixtureSha256[..8], doc, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------------------
    //  M-y — the video processor, exercised for the first time
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void TheStreamIsH264_SoTheOsMustDECODEIt_AndTheVIDEOPROCESSORIsWhatMakesItBgrx()
    {
        var bytes = File.ReadAllBytes(FixturePath());

        // The AVC decoder configuration record. It appears in a sample entry and NOWHERE else — a
        // search for `avc1` would also match the ftyp compatible-brands list at offset 24, which
        // says what the file CLAIMS rather than what its video track IS.
        var avcC = IndexOfSingle(bytes, "avcC"u8);
        Assert.Equal(1, bytes[avcC + 4]);      // configurationVersion
        Assert.Equal(0x42, bytes[avcC + 5]);   // AVCProfileIndication — 66, Baseline
        Assert.Equal(0x0A, bytes[avcC + 7]);   // AVCLevelIndication — level 1.0

        var state = VideoPresenceFactory
            .CreateClipSourceFor(VideoHostPlatform.Windows)
            .Open(FixturePath(), out var clip);

        Assert.True(
            state is CapabilityState.Available,
            "the operating system's own media stack must open a REAL H.264 clip, and it answered "
            + $"{state}. This is the first fixture in the port whose decoder does not already produce "
            + "RGB32, so a refusal here is what removing MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING "
            + "(MediaFoundationClipSource.cs:140-141) looks like: the reader then offers only the "
            + "decoder's own output format and SetCurrentMediaType(RGB32) is refused");

        using var open = Assert.IsAssignableFrom<IVideoClip>(clip);
        Assert.True(open.Info.Asked);
        Assert.True(open.Info.HasPicture);
        Assert.Equal(ClipWidth, open.Info.Width);
        Assert.Equal(ClipHeight, open.Info.Height);

        var frame = ReadFirstFrame(open);
        Assert.Equal(ClipWidth, frame.Width);
        Assert.Equal(ClipHeight, frame.Height);

        // BGRX out of a stream no BGRX ever entered. That conversion is the video processor's work,
        // and it is the whole of M-y.
        Assert.Equal(ClipWidth * ClipHeight * VideoFrame.BytesPerPixel, frame.Pixels.Length);
    }

    // -------------------------------------------------------------------------------------------
    //  THE DECODE ITSELF
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void EveryFrameOfTheCompressedClipDecodes_AndThenTheClipENDS_RatherThanRunningOn()
    {
        using var clip = OpenFixture();

        var frames = 0;
        while (!clip.Ended)
        {
            var frame = clip.ReadFrame();
            if (frame is null)
            {
                continue; // a stream tick or a format change carries no sample; not an end
            }

            frames++;
            Assert.Equal(ClipWidth * ClipHeight * VideoFrame.BytesPerPixel, frame.Pixels.Length);
        }

        Assert.Equal(ClipFrames, frames);
        Assert.Equal(ClipFrames, clip.DecodedFrames);

        // The end is an END. A reader that kept answering past it would spin a surface forever.
        Assert.True(clip.Ended);
        Assert.Null(clip.ReadFrame());
    }

    [Fact]
    public void TheDecodedPictureIsAPICTURE_AndItMOVES_WhichNoSolidColourFixtureCanShow()
    {
        using var clip = OpenFixture();

        var first = ReadFirstFrame(clip).Pixels.ToArray();

        // A frame the decoder zeroed, or a buffer handed back unwritten, is one colour. This one
        // measured 134 distinct pixels among 256 sampled points; the bar sits an order of magnitude
        // below that, because the fact is "a picture arrived", not "this exact picture".
        var distinct = DistinctSampledPixels(first);
        Assert.True(
            distinct >= 16,
            $"the first decoded frame carries {distinct} distinct pixels among at most 256 sampled "
            + "points, which is what a blank or unwritten buffer looks like");

        byte[]? last = null;
        while (!clip.Ended)
        {
            var frame = clip.ReadFrame();
            if (frame is not null)
            {
                last = frame.Pixels.ToArray();
            }
        }

        Assert.NotNull(last);
        Assert.False(
            first.AsSpan().SequenceEqual(last),
            "the clip's last decoded frame is byte-identical to its first, so either the decoder is "
            + "handing back one picture repeatedly or the copy path is returning the same buffer. "
            + "NOTE what this still does not prove: nothing here measures CADENCE, ORDER or TIMING — "
            + "a clip playing at half speed or backwards passes this fact");
    }

    [Fact]
    public void TheStrideForThisRealContainerIsPOSITIVE_SoTheFLIPBranchIsTheAviFixturesAlone()
    {
        using var clip = OpenFixture();

        // Measured, and measured again across all 54 of the owner's real videos (D324): every one
        // of them reports a POSITIVE MF_MT_DEFAULT_STRIDE. The bottom-up flip in
        // MediaFoundationClipSource.CopyOut exists because the AVI fixture measured -1280, and no
        // real file in that library takes it. So the two fixtures are complements: this one covers
        // the straight copy against a real codec, TestAvi's covers the flip.
        Assert.False(
            clip.Info.BottomUp,
            "this container's picture is top-down, so CopyOut's straight-copy branch is the one under "
            + "test here; if that ever flips, VideoCapabilityTests' orientation fact is the one still "
            + "holding the line and this comment is stale");
    }

    [Fact]
    public void TheFrameRateAndTheDurationComeFromTheCONTAINER_NotFromThePortsOwnFallback()
    {
        using var clip = OpenFixture();

        // D125: a container that reports no rate falls back to 80 ms so a rate-less file cannot spin
        // the clock. This container reports 10 fps and the OS hands that back, so the fallback is
        // NOT what is being read here — 100 ms and 80 ms are different numbers on purpose.
        Assert.Equal(TimeSpan.FromMilliseconds(100), clip.Info.FrameInterval);

        // MF_PD_DURATION, out of a PROPVARIANT whose payload sits at offset 8
        // (MediaFoundationClipSource.cs:330-355). The first time that read has ever run against a
        // real container rather than an AVI the suite wrote itself.
        Assert.Equal(TimeSpan.FromSeconds(2), clip.Info.Duration);
    }

    // -------------------------------------------------------------------------------------------

    private static IVideoClip OpenFixture()
    {
        var state = VideoPresenceFactory
            .CreateClipSourceFor(VideoHostPlatform.Windows)
            .Open(FixturePath(), out var clip);

        Assert.True(state is CapabilityState.Available, $"the fixture clip must open; the OS answered {state}");
        return Assert.IsAssignableFrom<IVideoClip>(clip);
    }

    private static VideoFrame ReadFirstFrame(IVideoClip clip)
    {
        for (var attempt = 0; attempt < 30 && !clip.Ended; attempt++)
        {
            var frame = clip.ReadFrame();
            if (frame is not null)
            {
                return frame;
            }
        }

        throw new InvalidOperationException(
            "the clip opened but handed back no picture in 30 ReadSample calls — this fact refuses to skip");
    }

    /// <summary>How many distinct BGRX pixels appear among at most 256 evenly spaced points.</summary>
    private static int DistinctSampledPixels(byte[] pixels)
    {
        var count = pixels.Length / VideoFrame.BytesPerPixel;
        var step = Math.Max(1, count / 256);
        var seen = new HashSet<uint>();
        for (var i = 0; i < count; i += step)
        {
            var o = i * VideoFrame.BytesPerPixel;
            seen.Add((uint)(pixels[o] | (pixels[o + 1] << 8) | (pixels[o + 2] << 16)));
        }

        return seen.Count;
    }

    /// <summary>The one offset of <paramref name="needle"/>, asserting there is exactly one.</summary>
    private static int IndexOfSingle(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        var found = -1;
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                Assert.Equal(-1, found);
                found = i;
            }
        }

        Assert.NotEqual(-1, found);
        return found;
    }

    private static string FixturePath() => Path.Combine([FindRepoRoot(), .. FixtureParts]);

    /// <summary>The FindRepoRoot precedent: walk up to the anchor, and THROW rather than skip.</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "client", "CcpClient.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"SP-140: repo root not found walking up from {AppContext.BaseDirectory} " +
            "(anchor client/CcpClient.sln) — these facts refuse to skip.");
    }
}
