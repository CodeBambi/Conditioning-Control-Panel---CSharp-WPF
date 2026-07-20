namespace CcpClient.Desktop.Features.AvatarTube;

/// <summary>
/// The machine-checkable frame counter strip (SP-015 Step 1 design, consult verdict #5/#8):
/// every generated frame carries a pixel strip encoding pack ID, clip ID, and frame index,
/// decodable from RENDERED captures. A uniform-delay or unmarked asset cannot falsify
/// multiplied-speed; this strip plus non-uniform delays can.
/// Layout (bottom-left of each frame cell, 4x4 blocks): [magenta marker][pack:2 bits]
/// [clip:3 bits][frame:4 bits] = 40x4 px. Decode auto-locates the marker in the capture's
/// bottom band so float translation and content centering never break it.
/// </summary>
public static class AvatarStripCodec
{
    public const int BlockSize = 4;
    public const int PackBits = 2;
    public const int ClipBits = 3;
    public const int FrameBits = 4;
    public const int StripWidth = (1 + PackBits + ClipBits + FrameBits) * BlockSize; // 40
    public const int StripHeight = BlockSize;

    /// <summary>Marker color: pure magenta, kept out of all generated art.</summary>
    public const byte MarkerR = 255;
    public const byte MarkerG = 0;
    public const byte MarkerB = 255;

    /// <summary>Rows from the capture bottom scanned for the marker (float amplitude + slack).</summary>
    public const int MarkerSearchBandRows = 16;

    /// <summary>Writes the strip into a frame-sized BGRA buffer at bottom-left.</summary>
    public static void Encode(byte[] bgra, int frameWidth, int frameHeight, int packId, int clipId, int frameIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(packId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(packId, 1 << PackBits);
        ArgumentOutOfRangeException.ThrowIfNegative(clipId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(clipId, 1 << ClipBits);
        ArgumentOutOfRangeException.ThrowIfNegative(frameIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frameIndex, 1 << FrameBits);
        if (frameWidth < StripWidth || frameHeight < StripHeight)
        {
            throw new ArgumentException($"frame {frameWidth}x{frameHeight} too small for the {StripWidth}x{StripHeight} strip");
        }

        var top = frameHeight - StripHeight;
        FillBlock(bgra, frameWidth, 0, top, MarkerR, MarkerG, MarkerB);
        WriteBits(bgra, frameWidth, top, BlockSize, packId, PackBits);
        WriteBits(bgra, frameWidth, top, BlockSize * (1 + PackBits), clipId, ClipBits);
        WriteBits(bgra, frameWidth, top, BlockSize * (1 + PackBits + ClipBits), frameIndex, FrameBits);
    }

    /// <summary>
    /// Locates the marker in the capture's bottom band and decodes the strip. Bit blocks
    /// decode by center luminance (white=1/black=0) with a wide threshold band, so minor
    /// capture compression cannot flip bits; a missing marker or ambiguous block fails the
    /// decode loudly (a failed decode is evidence input, never silently zero).
    /// </summary>
    public static bool TryDecode(
        byte[] bgra, int width, int height,
        out int packId, out int clipId, out int frameIndex, out string? failure)
    {
        packId = clipId = frameIndex = 0;
        failure = null;
        if (width < StripWidth || height < StripHeight + 2)
        {
            failure = $"capture {width}x{height} too small for strip";
            return false;
        }

        var bandTop = Math.Max(0, height - MarkerSearchBandRows);
        for (var y = bandTop; y <= height - StripHeight; y++)
        {
            for (var x = 0; x <= width - StripWidth; x++)
            {
                if (!IsMarker(bgra, width, x, y))
                {
                    continue;
                }

                if (!TryReadBits(bgra, width, y, x + BlockSize, PackBits, out packId, out failure)
                    || !TryReadBits(bgra, width, y, x + BlockSize * (1 + PackBits), ClipBits, out clipId, out failure)
                    || !TryReadBits(bgra, width, y, x + BlockSize * (1 + PackBits + ClipBits), FrameBits, out frameIndex, out failure))
                {
                    return false;
                }

                return true;
            }
        }

        failure = "no-marker: strip marker not found in bottom band";
        return false;
    }

    /// <summary>
    /// Fraction of pixels differing from <paramref name="bgR/G/B"/> beyond tolerance — the
    /// union no-blank rule for crossfade windows (consult verdict #8): a capture whose
    /// strip does not decode during a declared crossfade is not blank if visible content
    /// (incoming, outgoing, or blend) covers the region.
    /// </summary>
    public static double ContentFraction(byte[] bgra, int width, int height, byte bgR, byte bgG, byte bgB, int tolerance)
    {
        var matched = 0;
        var total = width * height;
        for (var i = 0; i < total; i++)
        {
            var offset = i * 4;
            if (Math.Abs(bgra[offset + 2] - bgR) > tolerance
                || Math.Abs(bgra[offset + 1] - bgG) > tolerance
                || Math.Abs(bgra[offset] - bgB) > tolerance)
            {
                matched++;
            }
        }

        return total == 0 ? 0.0 : (double)matched / total;
    }

    private static void WriteBits(byte[] bgra, int frameWidth, int top, int left, int value, int bits)
    {
        for (var bit = 0; bit < bits; bit++)
        {
            var on = ((value >> bit) & 1) == 1;
            FillBlock(bgra, frameWidth, left + bit * BlockSize, top, on ? (byte)255 : (byte)0, on ? (byte)255 : (byte)0, on ? (byte)255 : (byte)0);
        }
    }

    private static void FillBlock(byte[] bgra, int frameWidth, int left, int top, byte r, byte g, byte b)
    {
        for (var y = top; y < top + BlockSize; y++)
        {
            for (var x = left; x < left + BlockSize; x++)
            {
                var offset = (y * frameWidth + x) * 4;
                bgra[offset] = b;
                bgra[offset + 1] = g;
                bgra[offset + 2] = r;
                bgra[offset + 3] = 255;
            }
        }
    }

    private static bool IsMarker(byte[] bgra, int width, int left, int top)
    {
        // Sample the block center: exact magenta with a small tolerance for capture paths.
        var cx = left + BlockSize / 2;
        var cy = top + BlockSize / 2;
        var offset = (cy * width + cx) * 4;
        return Math.Abs(bgra[offset + 2] - MarkerR) <= 24
            && Math.Abs(bgra[offset + 1] - MarkerG) <= 24
            && Math.Abs(bgra[offset] - MarkerB) <= 24;
    }

    private static bool TryReadBits(byte[] bgra, int width, int top, int left, int bits, out int value, out string? failure)
    {
        value = 0;
        failure = null;
        for (var bit = 0; bit < bits; bit++)
        {
            var cx = left + bit * BlockSize + BlockSize / 2;
            var cy = top + BlockSize / 2;
            var offset = (cy * width + cx) * 4;
            var luminance = (bgra[offset + 2] * 299 + bgra[offset + 1] * 587 + bgra[offset] * 114) / 1000;
            if (luminance is > 48 and < 208)
            {
                failure = $"ambiguous-bit: luminance {luminance} at bit {bit} (x={cx} y={cy})";
                return false;
            }

            if (luminance >= 208)
            {
                value |= 1 << bit;
            }
        }

        return true;
    }
}
