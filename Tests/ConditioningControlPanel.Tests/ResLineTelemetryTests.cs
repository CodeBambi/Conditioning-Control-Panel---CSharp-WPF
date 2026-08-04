using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// c8fa5db5 — the [RES] resource sampler gained privMB/wsMB/gcMB columns with per-session deltas
/// (bug #634 "RAM grew to 100%"): a native or managed memory leak is invisible in the USER/GDI/
/// handle counters, so private bytes, working set and the managed heap are now sampled too, each
/// with the delta from the first sample. Whichever column climbs monotonically over a long session
/// names the leak. FormatResLine is the extracted, headless-testable renderer of that line — these
/// tests are the regression net that keeps a #634-class leak VISIBLE in the [RES] output.
/// </summary>
public class ResLineTelemetryTests
{
    private const long MB = 1024L * 1024L;

    [Fact]
    public void FirstSample_AllDeltasZero()
    {
        // Sample values identical to the baseline (as the first sample is against itself).
        string line = UiHangWatchdog.FormatResLine(
            user: 120, gdi: 240, handles: 500, threads: 12,
            priv: 500 * MB, ws: 520 * MB, gcHeap: 25 * MB,
            firstUser: 120, firstGdi: 240, firstHandles: 500, firstThreads: 12,
            firstPriv: 500 * MB, firstWs: 520 * MB, firstGcHeap: 25 * MB);

        Assert.StartsWith("[RES] ", line);
        Assert.Contains("user=120 (+0)", line);
        Assert.Contains("gdi=240 (+0)", line);
        Assert.Contains("handles=500 (+0)", line);
        Assert.Contains("threads=12 (+0)", line);
        Assert.Contains("privMB=500 (+0)", line);
        Assert.Contains("wsMB=520 (+0)", line);
        Assert.Contains("gcMB=25.0 (+0.0)", line);
    }

    [Fact]
    public void MemoryColumns_AreMbScaled_WithF0F0F1_Formatting()
    {
        string line = UiHangWatchdog.FormatResLine(
            user: 130, gdi: 250, handles: 510, threads: 13,
            priv: 850 * MB, ws: 900 * MB, gcHeap: 42 * MB,
            firstUser: 130, firstGdi: 250, firstHandles: 510, firstThreads: 13,
            firstPriv: 850 * MB, firstWs: 900 * MB, firstGcHeap: 42 * MB);

        // privMB / wsMB render as whole MB (F0), gcMB to one decimal (F1).
        Assert.Contains("privMB=850 ", line);
        Assert.Contains("wsMB=900 ", line);
        Assert.Contains("gcMB=42.0 ", line);
    }

    [Fact]
    public void SimulatedLeak_ShowsPlus400_InPrivateBytesDelta()
    {
        // The #634 regression net: baseline 500MB private bytes climbing to 900MB must surface
        // as a visible +400 in the delta column — this is exactly what triage reads to spot a leak.
        string line = UiHangWatchdog.FormatResLine(
            user: 140, gdi: 260, handles: 640, threads: 14,
            priv: 900 * MB, ws: 950 * MB, gcHeap: 40 * MB,
            firstUser: 130, firstGdi: 255, firstHandles: 600, firstThreads: 12,
            firstPriv: 500 * MB, firstWs: 520 * MB, firstGcHeap: 25 * MB);

        Assert.Contains("privMB=900 (+400)", line);
        Assert.Contains("wsMB=950 (+430)", line);
        Assert.Contains("gcMB=40.0 (+15.0)", line);
        // Handle/USER counters climb too and keep their own signed deltas.
        Assert.Contains("user=140 (+10)", line);
        Assert.Contains("gdi=260 (+5)", line);
        Assert.Contains("handles=640 (+40)", line);
        Assert.Contains("threads=14 (+2)", line);
    }

    [Fact]
    public void NegativeDelta_WhenMemoryShrinks_RendersSignedMinus()
    {
        // GC collection between samples can drop the managed heap below baseline — the delta must
        // read as a real negative, not clamp to zero, so a transient spike isn't mistaken for a leak.
        string line = UiHangWatchdog.FormatResLine(
            user: 100, gdi: 200, handles: 400, threads: 10,
            priv: 480 * MB, ws: 500 * MB, gcHeap: 18 * MB,
            firstUser: 100, firstGdi: 200, firstHandles: 400, firstThreads: 10,
            firstPriv: 500 * MB, firstWs: 520 * MB, firstGcHeap: 25 * MB);

        Assert.Contains("privMB=480 (+-20)", line);
        Assert.Contains("gcMB=18.0 (+-7.0)", line);
    }
}
