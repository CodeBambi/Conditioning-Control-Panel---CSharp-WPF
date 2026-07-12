using System;
using System.Collections.Generic;
using System.Text;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the HARD privacy contract for screen-frame capture
/// (linux-framesource-contract.md §1.4, same class as the webcam rule): raw captured
/// frames are MEMORY-ONLY — never written to disk, never sent over the network, never
/// logged. The Linux backends log dimensions and backend/probe reasons only, never pixel
/// content (verified by inspection of X11BasicFrameSourceBackend / FallbackFrameSource /
/// LinuxFrameSource, which live in the Linux head and are not referenced by this test
/// project).
///
/// What CAN be pinned at the Core seam is the realistic accidental-leak vector: someone
/// logging a <see cref="RawFrame"/> as a structured-logging parameter. RawFrame is a
/// record, and record ToString() renders every property — these tests assert that the
/// pixel buffer renders OPAQUELY (the CLR's array rendering, "System.Byte[]"), so no
/// pixel bytes can reach a log sink through casual structured logging. If RawFrame's
/// buffer is ever changed to a type whose ToString dumps contents (e.g. ImmutableArray
/// formatting, a custom ToString), these tests fail and the privacy hard-line must be
/// re-reviewed.
/// </summary>
public class FrameSourcePrivacyTests
{
    /// <summary>
    /// A distinctive ASCII sentinel embedded INTO the pixel buffer bytes. If any rendering
    /// path dumps buffer contents, this substring surfaces in the rendered string.
    /// </summary>
    private const string PixelSentinel = "FRAME_PIXEL_SENTINEL_314159_DISCLOSE_ME";

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Capture the FULLY FORMATTED message (what a real sink writes).
            if (formatter is not null)
            {
                Messages.Add(formatter(state, exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private static RawFrame SentinelFrame()
    {
        // 4x4 BGRA frame with the ASCII sentinel embedded in the pixel bytes.
        var bytes = new byte[4 * 4 * 4];
        var sentinel = Encoding.ASCII.GetBytes(PixelSentinel);
        Array.Copy(sentinel, bytes, Math.Min(sentinel.Length, bytes.Length));
        return new RawFrame(4, 4, bytes);
    }

    [Fact]
    public void RawFrame_ToString_RendersPixelBufferOpaquely_NeverContents()
    {
        var frame = SentinelFrame();

        var rendered = frame.ToString();

        // The record's generated ToString must render the buffer as the CLR's opaque array
        // string, never its contents.
        Assert.NotNull(rendered);
        Assert.DoesNotContain(PixelSentinel, rendered!, StringComparison.Ordinal);
        Assert.Contains("System.Byte[]", rendered!, StringComparison.Ordinal);
        // Dimensions MAY render (contract §1.4 allows logging dimensions only).
        Assert.Contains("Width = 4", rendered!, StringComparison.Ordinal);
    }

    [Fact]
    public void LoggingARawFrame_AsStructuredParameter_NeverEmitsPixelData()
    {
        var frame = SentinelFrame();
        var logger = new CapturingLogger();

        // The worst realistic accidental-leak shape: the frame object logged directly.
        // NOTE deliberately NOT tested: logging the raw byte[] itself
        // (logger.Log("{Buffer}", frame.BgraData)) DOES dump contents — MEL's structured
        // formatter joins IEnumerable elements as decimals. That call shape is a privacy
        // bug by definition (contract §1.4) and must never appear in any head; RawFrame
        // (a non-enumerable record) is the safe unit to pass around.
        logger.LogInformation("captured frame {Frame}", frame);

        Assert.NotEmpty(logger.Messages);
        // Neither the ASCII sentinel nor its decimal byte rendering ("70, 82, 65" = "FRA")
        // may surface in a formatted log message.
        var decimalPrefix = string.Join(", ", System.Text.Encoding.ASCII.GetBytes("FRAME_PIXEL"));
        Assert.All(logger.Messages, msg =>
        {
            Assert.DoesNotContain(PixelSentinel, msg, StringComparison.Ordinal);
            Assert.DoesNotContain(decimalPrefix, msg, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void RawFrame_TightPackContract_LengthIsWidthTimesHeightTimesFour()
    {
        // Contract §1.2 (normative packing): consumers index by Width/Height and corrupt on
        // padded rows — pin the reference shape the Linux backends must repack to.
        var frame = SentinelFrame();
        Assert.Equal(frame.Width * frame.Height * 4, frame.BgraData.Length);
    }
}
