namespace CcpClient.Desktop.Camera;

/// <summary>
/// <b>THE ONE PIXEL BOUNDARY IN THIS PRODUCT, and it is a static class with no state at all.</b>
///
/// <para>Every other type under <c>Camera/</c> is forbidden to declare a member that can carry image
/// or per-frame biometric data, and <c>CameraCapabilityTests</c> enforces that mechanically. This
/// type is the single named exception, and the exception is bounded three ways that a reader can
/// check in one glance: it is <c>static</c> (nothing can hold an instance of it), it declares NO
/// fields (nothing can be retained between calls), and every pixel parameter is a
/// <see cref="ReadOnlySpan{T}"/> — which the C# compiler itself refuses to let anybody store in a
/// field, capture in a closure, or box. So a frame handed to this class cannot outlive the call,
/// cannot be cached, and cannot be reached later by anything that writes a file, a log line, a
/// crash report or an AI prompt. <c>client/docs/capability-inventory.md</c>'s memory-only rule for
/// "Webcam, face, and gaze tracking" is therefore still a property of the build rather than a
/// discipline, even now that the build really does decode frames.</para>
///
/// <para><b>What it answers, and why the question exists at all.</b> Upstream does not accept a
/// camera because a frame arrived — <i>"A solid-colour / black feed reads as a perfectly valid
/// non-empty Mat but contains no detectable face"</i>
/// (<c>Services/Webcam/WebcamTrackingService.cs:123-130</c>, BUG-F2XJE2E7X9: an Elgato Facecam Neo
/// read 1240 frames and found zero faces). Acceptance is a SPATIAL-or-TEMPORAL probe, and both
/// halves exist because of a specific camera that broke without them:</para>
///
/// <list type="number">
/// <item><b>Spatial</b> — the richest channel's standard deviation must clear
/// <see cref="MinStdDev"/>. That rejects the flat Elgato feed. The bar is deliberately tiny so a
/// dim, grainy room still scatters well above it (<c>:126-130</c>).</item>
/// <item><b>Temporal</b> — OR the mean absolute inter-frame difference clears
/// <see cref="MinTemporalDelta"/>. A genuinely dark room is spatially flat but LIVE: it changes as
/// the user moves. A dead, solid or frozen feed does not (<c>:132-143</c>). This second path exists
/// so a dark room is not misdiagnosed as a camera another application is holding, WITHOUT lowering
/// the spatial floor and re-admitting the feed the floor was added to reject.</item>
/// </list>
///
/// <para><b>The constants are upstream's, to the digit</b>, because they are behaviour: a camera
/// this product accepts and a camera it rejects must be the same set of cameras it is today
/// (<c>:130</c>, <c>:143</c>, <c>:152-153</c>). The port changes the API family underneath — Media
/// Foundation instead of OpenCV — and changes none of the numbers.</para>
/// </summary>
public static class CameraFrameProbe
{
    /// <summary>
    /// Bytes per pixel in the only frame layout this build ever sees: MF's RGB32, which is
    /// B, G, R, X. Requested explicitly so the probe is never handed a planar or subsampled buffer
    /// it would silently mis-measure (<c>Camera/MediaFoundationCameraCapture.cs</c> sets the reader's
    /// output subtype and lets MF's video processor convert).
    /// </summary>
    public const int BytesPerPixel = 4;

    /// <summary>
    /// How many of those bytes carry picture. Three: the fourth is RGB32's ignored X byte, and
    /// including a constant 0 or 255 alpha channel in a "richest channel" maximum would either
    /// suppress a real reading or manufacture a fake one. Upstream measures a 3-channel BGR Mat for
    /// the same reason (<c>Services/Webcam/WebcamTrackingService.cs:1393-1394</c>).
    /// </summary>
    public const int MeasuredChannels = 3;

    /// <summary>
    /// Upstream's <c>MinProbeStdDev</c>, verbatim (<c>Services/Webcam/WebcamTrackingService.cs:130</c>).
    /// A frame whose richest channel varies less than this is a solid or black feed.
    /// </summary>
    public const double MinStdDev = 3.0;

    /// <summary>
    /// Upstream's <c>MinProbeTemporalDelta</c>, verbatim
    /// (<c>Services/Webcam/WebcamTrackingService.cs:143</c>). Set above the uniform sensor-noise
    /// flicker of a static feed (under 1 on 0-255) and below real scene motion.
    /// </summary>
    public const double MinTemporalDelta = 2.0;

    /// <summary>
    /// Upstream's <c>ProbeWarmupMs</c> (<c>Services/Webcam/WebcamTrackingService.cs:152</c>): the
    /// per-attempt budget a camera gets to start producing something usable. It is three seconds
    /// because a real device needed it — the Lovense Webcam 2 streams black for over a second after
    /// its handle opens (BUG-6W469QGMHS, <c>:145-152</c>), and a five-read window rejected it as
    /// degenerate on every backend and surfaced a false "camera in use".
    /// </summary>
    public const int WarmupMilliseconds = 3000;

    /// <summary>
    /// Upstream's <c>MaxConsecutiveReadFails</c> (<c>Services/Webcam/WebcamTrackingService.cs:120</c>,
    /// <i>"~1s at 30fps"</i>): how many reads in a row may come back with nothing before an OPEN
    /// camera is treated as lost rather than merely slow.
    ///
    /// <para>Consecutive, never cumulative, and that distinction is the behaviour: a camera that
    /// drops one frame a second all session is working, and a camera that drops thirty in a row has
    /// been unplugged.</para>
    /// </summary>
    public const int MaxConsecutiveReadFailures = 30;

    /// <summary>
    /// The maximum, over the picture channels, of that channel's standard deviation across the
    /// frame — upstream's <c>Cv2.MeanStdDev</c> followed by a three-way max
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1393-1394</c>).
    ///
    /// <para>Zero for an empty span, which is the honest answer for "no pixels" and is below every
    /// acceptance bar, so a truncated buffer can never be adopted as a working camera.</para>
    /// </summary>
    /// <param name="frame">One RGB32 frame, tightly packed. Never retained: see the type remarks.</param>
    public static double MaxChannelStdDev(ReadOnlySpan<byte> frame)
    {
        var pixels = frame.Length / BytesPerPixel;
        if (pixels == 0)
        {
            return 0;
        }

        var best = 0.0;
        for (var channel = 0; channel < MeasuredChannels; channel++)
        {
            long sum = 0;
            long sumOfSquares = 0;
            for (var offset = channel; offset < pixels * BytesPerPixel; offset += BytesPerPixel)
            {
                int value = frame[offset];
                sum += value;
                sumOfSquares += (long)value * value;
            }

            var mean = sum / (double)pixels;
            // Population variance, which is what OpenCV's MeanStdDev computes. Clamped at zero
            // because the algebraic form can land a hair below it on a perfectly uniform channel.
            var variance = Math.Max(0, (sumOfSquares / (double)pixels) - (mean * mean));
            best = Math.Max(best, Math.Sqrt(variance));
        }

        return best;
    }

    /// <summary>
    /// The maximum, over the picture channels, of that channel's MEAN ABSOLUTE difference between
    /// two frames — upstream's <c>Cv2.Absdiff</c> then <c>Cv2.Mean</c> then a three-way max
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1405-1407</c>).
    ///
    /// <para>Zero when either frame is empty or the two differ in length, which is upstream's own
    /// guard in a different shape: it compares <c>prevProbe.Size()</c> and <c>prevProbe.Type()</c>
    /// before differencing (<c>:1403</c>), because a resolution change mid-warm-up would otherwise
    /// read as enormous motion and adopt a feed nobody has looked at.</para>
    /// </summary>
    public static double MaxChannelMeanAbsoluteDifference(ReadOnlySpan<byte> frame, ReadOnlySpan<byte> previous)
    {
        var pixels = frame.Length / BytesPerPixel;
        if (pixels == 0 || frame.Length != previous.Length)
        {
            return 0;
        }

        var best = 0.0;
        for (var channel = 0; channel < MeasuredChannels; channel++)
        {
            long sum = 0;
            for (var offset = channel; offset < pixels * BytesPerPixel; offset += BytesPerPixel)
            {
                sum += Math.Abs(frame[offset] - previous[offset]);
            }

            best = Math.Max(best, sum / (double)pixels);
        }

        return best;
    }

    /// <summary>
    /// Upstream's acceptance decision, verbatim: <c>maxStd &gt;= MinProbeStdDev || temporalDelta
    /// &gt;= MinProbeTemporalDelta</c> (<c>Services/Webcam/WebcamTrackingService.cs:1410</c>).
    ///
    /// <para><b>The OR is the whole design and must not become an AND.</b> Spatial-clean wins first
    /// so cameras that already work are untouched; the temporal path only ever rescues a dim-but-live
    /// feed. An AND would reject a static scene in a well-lit room, which is most users sitting
    /// still.</para>
    /// </summary>
    public static bool Accepts(double maxStdDev, double temporalDelta) =>
        maxStdDev >= MinStdDev || temporalDelta >= MinTemporalDelta;
}
