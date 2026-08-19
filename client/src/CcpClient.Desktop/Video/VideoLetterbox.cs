namespace CcpClient.Desktop.Video;

/// <summary>Where the picture sits inside a surface, in the surface's own client coordinates.</summary>
public readonly record struct PictureBox(int X, int Y, int Width, int Height)
{
    /// <summary>True when there is any picture at all to draw.</summary>
    public bool HasArea => Width > 0 && Height > 0;

    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}

/// <summary>
/// The surface's own composition: bars, then the picture, nearest-neighbour, into one buffer.
///
/// <para><b>Why the port composes in managed code instead of letting GDI stretch.</b> The read-back
/// this capability rests on compares the operating system's copy of the surface against the pixels
/// that were handed to it. <c>StretchDIBits</c> with an interpolating mode BLENDS, so the colour at
/// a sampled point is no longer any source pixel and the comparison would need a tolerance —
/// and a tolerance is exactly how a read-back stops being able to tell a picture from a smear.
/// Composing here makes the expected value EXACT and known before the blit, which is what lets
/// <see cref="VideoSurfaceObservation.PictureMatched"/> be an equality rather than an
/// approximation. It also makes the whole letterbox+picture arrive in ONE blit, so no frame is
/// ever half-painted.</para>
/// </summary>
public static class VideoLetterbox
{
    /// <summary>
    /// The minimum bar, in pixels, on every side.
    ///
    /// <para><b>It is a structural guarantee, not an aesthetic one.</b> The read-back's control
    /// point is a pixel in a bar the painter FILLS and never draws picture into, and without a floor
    /// a clip whose aspect exactly matches the surface would have no bar at all — so the control
    /// point would land inside the picture and the check that proves the fill happened would be
    /// reading the film. SP-110 made the same call for the same reason (its cards' bands are floored
    /// at <c>ControlMargin = 3</c>) after a mutation showed an ink check that was structurally
    /// blind.</para>
    /// </summary>
    public const int Margin = 3;

    /// <summary>
    /// The largest rectangle of the picture's aspect that fits inside the surface with
    /// <see cref="Margin"/> on every side — letterbox or pillarbox, never stretch. Upstream's own
    /// rule and upstream's own arithmetic (<c>Services/Video/VideoService.cs:3193-3211</c>,
    /// <c>FitToAspect</c>), applied to the inner box rather than the whole window.
    /// </summary>
    public static PictureBox Fit(int surfaceWidth, int surfaceHeight, int pictureWidth, int pictureHeight)
    {
        if (surfaceWidth <= 0 || surfaceHeight <= 0 || pictureWidth <= 0 || pictureHeight <= 0)
        {
            return default;
        }

        var innerWidth = surfaceWidth - (2 * Margin);
        var innerHeight = surfaceHeight - (2 * Margin);
        if (innerWidth <= 0 || innerHeight <= 0)
        {
            return default;
        }

        var aspect = pictureWidth / (double)pictureHeight;
        var width = (double)innerWidth;
        var height = innerWidth / aspect;
        if (height > innerHeight)
        {
            height = innerHeight;
            width = innerHeight * aspect;
        }

        var w = Math.Max(1, Math.Min(innerWidth, (int)Math.Round(width)));
        var h = Math.Max(1, Math.Min(innerHeight, (int)Math.Round(height)));
        return new PictureBox(Margin + ((innerWidth - w) / 2), Margin + ((innerHeight - h) / 2), w, h);
    }

    /// <summary>
    /// Build the surface's whole client area: <paramref name="letterbox"/> everywhere, then
    /// <paramref name="frame"/> scaled nearest-neighbour into <paramref name="box"/>. Top-down BGRX,
    /// ready for a 1:1 blit.
    /// </summary>
    /// <param name="frame">The decoded picture.</param>
    /// <param name="surfaceWidth">Surface client width in physical pixels.</param>
    /// <param name="surfaceHeight">Surface client height in physical pixels.</param>
    /// <param name="box">Where the picture goes, from <see cref="Fit"/>.</param>
    /// <param name="letterbox">The bar colour as a <c>COLORREF</c> (<c>0x00BBGGRR</c>).</param>
    public static byte[] Compose(
        VideoFrame frame, int surfaceWidth, int surfaceHeight, PictureBox box, uint letterbox)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(surfaceWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(surfaceHeight, 0);

        var stride = surfaceWidth * VideoFrame.BytesPerPixel;
        var pixels = new byte[(long)stride * surfaceHeight];
        var blue = (byte)((letterbox >> 16) & 0xFF);
        var green = (byte)((letterbox >> 8) & 0xFF);
        var red = (byte)(letterbox & 0xFF);
        for (var i = 0; i < pixels.Length; i += VideoFrame.BytesPerPixel)
        {
            pixels[i] = blue;
            pixels[i + 1] = green;
            pixels[i + 2] = red;
            pixels[i + 3] = 0xFF;
        }

        if (!box.HasArea)
        {
            return pixels;
        }

        var source = frame.Pixels;
        for (var y = 0; y < box.Height; y++)
        {
            var destY = box.Y + y;
            if (destY < 0 || destY >= surfaceHeight)
            {
                continue;
            }

            var sourceY = Math.Min(frame.Height - 1, (int)((long)y * frame.Height / box.Height));
            var sourceRow = sourceY * frame.Stride;
            var destRow = destY * stride;
            for (var x = 0; x < box.Width; x++)
            {
                var destX = box.X + x;
                if (destX < 0 || destX >= surfaceWidth)
                {
                    continue;
                }

                var sourceX = Math.Min(frame.Width - 1, (int)((long)x * frame.Width / box.Width));
                var from = sourceRow + (sourceX * VideoFrame.BytesPerPixel);
                var to = destRow + (destX * VideoFrame.BytesPerPixel);
                pixels[to] = source[from];
                pixels[to + 1] = source[from + 1];
                pixels[to + 2] = source[from + 2];
                pixels[to + 3] = 0xFF;
            }
        }

        return pixels;
    }

    /// <summary>
    /// The points a read-back samples inside the picture, in surface client coordinates: nine
    /// fractions of the picture box, spread rather than on a lattice.
    ///
    /// <para><b>Their asymmetry is NOT load-bearing and the sweep proved it</b> (SP-111 record §4,
    /// M-au): the read-back's fold is order-dependent, so a mirrored picture differs even from a
    /// mirror-invariant point set, and a mutation making the set exactly mirror-invariant survived.
    /// The claim is corrected here rather than defended. What the points ARE for is coverage — nine
    /// places spread across the picture, so a blit that landed only part of a frame is seen.</para>
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> SamplePoints(PictureBox box)
    {
        if (!box.HasArea)
        {
            return [];
        }

        var points = new List<(int X, int Y)>(Fractions.Length);
        foreach (var (fx, fy) in Fractions)
        {
            var x = box.X + Math.Min(box.Width - 1, (int)(box.Width * fx));
            var y = box.Y + Math.Min(box.Height - 1, (int)(box.Height * fy));
            points.Add((x, y));
        }

        return points;
    }

    private static readonly (double X, double Y)[] Fractions =
    [
        (0.11, 0.13), (0.50, 0.09), (0.87, 0.17),
        (0.19, 0.47), (0.50, 0.50), (0.79, 0.53),
        (0.13, 0.83), (0.50, 0.91), (0.91, 0.71),
    ];
}
