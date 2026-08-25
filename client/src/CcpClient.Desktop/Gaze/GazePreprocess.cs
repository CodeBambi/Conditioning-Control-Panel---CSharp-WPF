using CcpClient.Desktop.Camera;

namespace CcpClient.Desktop.Gaze;

/// <summary>
/// The letterbox geometry <see cref="GazePreprocess.Letterbox"/> computes for one frame: where the
/// whole picture lands inside the detector's square input, and the scale that maps back out of it.
/// </summary>
/// <param name="Input">The square side of the model input the picture was fitted into.</param>
/// <param name="Scale">Upstream's <c>scale</c> — the single factor both axes are multiplied by, so
/// the face keeps its aspect ratio (<c>Services/Webcam/WebcamTrackingService.cs:3170</c>).</param>
/// <param name="Width">The resized picture's width in input pixels
/// (<c>Services/Webcam/WebcamTrackingService.cs:3171</c>).</param>
/// <param name="Height">The resized picture's height in input pixels
/// (<c>Services/Webcam/WebcamTrackingService.cs:3172</c>).</param>
/// <param name="PadX">Black columns before the picture. <b>Integer division, so the pad can be one
/// pixel wider on the right than on the left</b> — that asymmetry is upstream's and it is
/// load-bearing, because the decode subtracts exactly this number
/// (<c>Services/Webcam/WebcamTrackingService.cs:3173</c>).</param>
/// <param name="PadY">Black rows above the picture, same truncation
/// (<c>Services/Webcam/WebcamTrackingService.cs:3174</c>).</param>
public readonly record struct GazeLetterbox(int Input, float Scale, int Width, int Height, int PadX, int PadY);

/// <summary>A rectangle in SOURCE-FRAME pixel coordinates — what the detector's box decodes to, and
/// what the face crop is built around.</summary>
public readonly record struct GazeRect(int X, int Y, int Width, int Height)
{
    /// <summary>False for a box the clip to the frame's bounds emptied, which upstream reports as
    /// "no face" rather than as a zero-area face
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3248</c>).</summary>
    public bool Describes => Width > 0 && Height > 0;
}

/// <summary>A square region of the source frame, in source-frame pixel coordinates. <b>Its origin
/// may be negative and its far edge may sit past the frame</b>: upstream builds the crop around a
/// centre and pads whatever falls outside with black rather than sliding the box back inside
/// (<c>Services/Webcam/WebcamTrackingService.cs:3358-3370</c>), because sliding it would move the
/// landmarks it is about.</summary>
public readonly record struct GazeCrop(int X, int Y, int Side)
{
    /// <summary>Whether the crop is big enough to be worth running a model over. See
    /// <see cref="GazePreprocess.MinCropSide"/> for the two upstream refusals this is.</summary>
    public bool Describes => Side >= GazePreprocess.MinCropSide;
}

/// <summary>
/// <b>The arithmetic between a camera frame and the three models' input tensors — resize,
/// letterbox-pad, crop, flip — and nothing else.</b>
///
/// <para><b>Why this is not in <c>CcpClient.Desktop.Camera</c>.</b> The camera seam's retention rule
/// bans a pixel-carrying field on any type whose namespace starts with that prefix, and an engine
/// that consumes a frame will eventually want a field typed from an inference package. Putting the
/// engine's arithmetic in its own namespace, reached only through the sink
/// <see cref="ICameraCaptureSource.ReadFrame"/> already hands out, is what keeps the camera seam
/// from ever seeing a tensor. This file therefore takes a <see cref="ReadOnlySpan{T}"/> — which the
/// C# compiler refuses to let anything store, capture or box — and a
/// <see cref="CameraFrameInfo"/>, so geometry never has to be paired with a buffer it was not
/// delivered with.</para>
///
/// <para><b>This is a port of upstream's OpenCV footprint, which is smaller than it looks.</b>
/// Between <c>Services/Webcam/WebcamTrackingService.cs:3179</c> and <c>:3540</c> the entire OpenCV
/// use in the inference path is three <c>Cv2.Resize</c> calls
/// (<c>Services/Webcam/WebcamTrackingService.cs:3190</c>, <c>:3372</c>, <c>:3531</c>) and one
/// <c>Cv2.Flip</c> (<c>Services/Webcam/WebcamTrackingService.cs:3536</c>). Everything else here is
/// arithmetic upstream already writes in plain C#, reproduced to the digit — including its rounding
/// mode, which is .NET's default banker's rounding and is not decoration: a crop centred at
/// <c>-2.5</c> lands at <c>-2</c>, not <c>-3</c>.</para>
///
/// <para><b>The two steps are fused, and the fusion is exact rather than close.</b> Upstream resizes
/// into a buffer and then copies that buffer into a zeroed square
/// (<c>Services/Webcam/WebcamTrackingService.cs:3190-3194</c> and <c>:3358-3372</c>). Reading the
/// source through the same coordinate map, and answering 0 for a coordinate outside the frame,
/// produces the identical image without the intermediate — the black upstream pads with IS the zero
/// this returns, so the interpolation blends against the same value on the same edge.</para>
///
/// <para><b>THE ONE NAMED DIVERGENCE.</b> OpenCV's 8-bit <c>INTER_LINEAR</c> quantises its
/// interpolation weights to 11 fractional bits and accumulates in fixed point; this computes the
/// same weights in <see cref="float"/> and rounds once, half away from zero, exactly where OpenCV's
/// fixed-point descale rounds. The coordinate map is OpenCV's own — half-pixel centres,
/// <c>(dst + 0.5) * scale - 0.5</c>, with edge replication outside the source — so no sample lands
/// on a different pair of pixels; only the last bit of a blended byte can differ. Nothing else here
/// diverges.</para>
///
/// <para><b>No model is loaded and no inference runs here.</b> This file produces the tensors the
/// three MediaPipe models expect and maps their coordinates back; the runtime that would consume
/// them is not admitted (<c>client/docs/onnxruntime-package-admission.md</c>).</para>
/// </summary>
public static class GazePreprocess
{
    /// <summary>BlazeFace's square input (<c>Services/Webcam/WebcamTrackingService.cs:3107</c>).</summary>
    public const int DetectorInput = 128;

    /// <summary>FaceMesh's square input (<c>Services/Webcam/WebcamTrackingService.cs:3301</c>).</summary>
    public const int MeshInput = 192;

    /// <summary>The iris model's square input (<c>Services/Webcam/WebcamTrackingService.cs:3459</c>).</summary>
    public const int IrisInput = 64;

    /// <summary>FaceMesh's ROI expansion around the detected face box — upstream's <c>RoiScale</c>
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3305</c>).</summary>
    public const float FaceRoiScale = 1.5f;

    /// <summary>The iris model's ROI expansion around the eye-corner pair — upstream's own
    /// <c>RoiScale</c>, a different number in a different class
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3460</c>).</summary>
    public const float EyeRoiScale = 2.3f;

    /// <summary>
    /// The smallest crop either model is run over. Upstream writes this refusal twice and the two
    /// spellings COINCIDE: FaceMesh refuses <c>rs &lt; 16</c> outright
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3344</c>) and the iris model refuses
    /// <c>rs &lt; InputSize / 4</c> with <c>InputSize</c> 64
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3505</c>), which is also 16.
    ///
    /// <para>The iris path carries a THIRD refusal, before the rounding
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3501</c>). It is subsumed: a side under 8 rounds
    /// to at most 8, and 8 is already under 16, so no crop is admitted here that upstream rejects
    /// there.</para>
    /// </summary>
    public const int MinCropSide = 16;

    /// <summary>Channels in a model input tensor: RGB, channel-last
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3200</c>).</summary>
    public const int TensorChannels = 3;

    // RGB32 as Media Foundation lays it out — B, G, R, X (Camera/CameraFrameProbe.cs:43-49). The
    // first three bytes are in the same order as the BGR Mat upstream reads, so the channel swap
    // below is upstream's swap and not a second one.
    private const int BlueByte = 0;
    private const int GreenByte = 1;
    private const int RedByte = 2;

    /// <summary>BlazeFace's normalisation, <c>[0, 255]</c> to <c>[-1, 1]</c>
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3262</c>, applied at <c>:3269-3271</c>).</summary>
    private const float DetectorScale = 2f / 255f;

    /// <summary>FaceMesh's and the iris model's normalisation, <c>[0, 255]</c> to <c>[0, 1]</c>
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3416</c>, and <c>:3589</c> for the iris).</summary>
    private const float CropScale = 1f / 255f;

    /// <summary>
    /// The letterbox geometry for a picture of <paramref name="sourceWidth"/> by
    /// <paramref name="sourceHeight"/> fitted into a square of <paramref name="input"/>: scale so
    /// the longer side fills the input, then centre the shorter one
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3170-3174</c>).
    /// </summary>
    public static GazeLetterbox Letterbox(int sourceWidth, int sourceHeight, int input)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input);

        var scale = Math.Min((float)input / sourceWidth, (float)input / sourceHeight);
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        return new GazeLetterbox(input, scale, width, height, (input - width) / 2, (input - height) / 2);
    }

    /// <summary>
    /// Fill BlazeFace's <c>1 x 128 x 128 x 3</c> input from one frame: letterbox the whole picture
    /// into the square, pad the rest black, swap BGR to RGB and scale to <c>[-1, 1]</c>
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3190-3198</c>). Returns the geometry it used,
    /// because <see cref="UnLetterbox"/> needs exactly that geometry to decode the box back.
    /// </summary>
    /// <param name="frame">The frame's pixels, as the sink was handed them. Never retained.</param>
    /// <param name="info">The geometry delivered WITH those pixels.</param>
    /// <param name="destination">At least <c>128 * 128 * 3</c> floats, channel-last.</param>
    public static GazeLetterbox FillDetector(
        ReadOnlySpan<byte> frame, CameraFrameInfo info, Span<float> destination)
    {
        Require(frame, info, DetectorInput, destination);

        var box = Letterbox(info.Width, info.Height, DetectorInput);
        var tensor = destination[..(DetectorInput * DetectorInput * TensorChannels)];

        // The pad, written first and overwritten where the picture lands — upstream's zeroed Mat
        // (Services/Webcam/WebcamTrackingService.cs:3183-3188) seen through its own normalisation,
        // because 0 * (2/255) - 1 is exactly -1.
        tensor.Fill(-1f);

        var scaleX = (double)info.Width / box.Width;
        var scaleY = (double)info.Height / box.Height;

        for (var dy = 0; dy < box.Height; dy++)
        {
            Taps(dy, scaleY, info.Height, out var top, out var bottom, out var weightY);
            var row = ((box.PadY + dy) * DetectorInput) + box.PadX;

            for (var dx = 0; dx < box.Width; dx++)
            {
                Taps(dx, scaleX, info.Width, out var left, out var right, out var weightX);
                var at = (row + dx) * TensorChannels;

                tensor[at + 0] = (Sample(frame, info, 0, 0, left, right, top, bottom, weightX, weightY, RedByte) * DetectorScale) - 1f;
                tensor[at + 1] = (Sample(frame, info, 0, 0, left, right, top, bottom, weightX, weightY, GreenByte) * DetectorScale) - 1f;
                tensor[at + 2] = (Sample(frame, info, 0, 0, left, right, top, bottom, weightX, weightY, BlueByte) * DetectorScale) - 1f;
            }
        }

        return box;
    }

    /// <summary>
    /// Decode a detector box — normalised <c>[0, 1]</c> against the padded square — back to
    /// source-frame pixels: multiply out, subtract the pad, divide by the scale, round, then clip to
    /// the frame (<c>Services/Webcam/WebcamTrackingService.cs:3233-3248</c>). A box the clip empties
    /// comes back with <see cref="GazeRect.Describes"/> false, which is upstream's null.
    /// </summary>
    public static GazeRect UnLetterbox(
        GazeLetterbox box, int sourceWidth, int sourceHeight,
        float xMin01, float yMin01, float width01, float height01)
    {
        var minX = ((xMin01 * box.Input) - box.PadX) / box.Scale;
        var minY = ((yMin01 * box.Input) - box.PadY) / box.Scale;
        var spanX = width01 * box.Input / box.Scale;
        var spanY = height01 * box.Input / box.Scale;

        var x = (int)Math.Round(minX);
        var y = (int)Math.Round(minY);
        var w = (int)Math.Round(spanX);
        var h = (int)Math.Round(spanY);

        if (x < 0)
        {
            w += x;
            x = 0;
        }

        if (y < 0)
        {
            h += y;
            y = 0;
        }

        if (x + w > sourceWidth)
        {
            w = sourceWidth - x;
        }

        if (y + h > sourceHeight)
        {
            h = sourceHeight - y;
        }

        return new GazeRect(x, y, w, h);
    }

    /// <summary>
    /// FaceMesh's ROI: a square centred on the face box, with the box's LONGER side expanded by
    /// <see cref="FaceRoiScale"/> (<c>Services/Webcam/WebcamTrackingService.cs:3338-3343</c>).
    /// </summary>
    public static GazeCrop FaceCrop(GazeRect face)
    {
        var centreX = face.X + (face.Width / 2f);
        var centreY = face.Y + (face.Height / 2f);
        var side = Math.Max(face.Width, face.Height) * FaceRoiScale;
        return Square(centreX, centreY, side);
    }

    /// <summary>
    /// The iris model's ROI: a square centred on the bounding box of ONE EYE'S TWO CORNERS, with
    /// that box's longer side expanded by <see cref="EyeRoiScale"/>
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3494-3504</c>). The corners arrive in whichever
    /// order the landmark indices give them, which is why both axes are min/maxed first.
    /// </summary>
    public static GazeCrop EyeCrop(float outerX, float outerY, float innerX, float innerY)
    {
        var minX = Math.Min(outerX, innerX);
        var maxX = Math.Max(outerX, innerX);
        var minY = Math.Min(outerY, innerY);
        var maxY = Math.Max(outerY, innerY);
        var side = Math.Max(maxX - minX, maxY - minY) * EyeRoiScale;
        return Square((minX + maxX) / 2f, (minY + maxY) / 2f, side);
    }

    /// <summary>
    /// Fill a crop model's <c>1 x N x N x 3</c> input: take the square crop out of the frame with
    /// everything outside the frame black, resize it to <paramref name="input"/>, optionally mirror
    /// it, swap BGR to RGB and scale to <c>[0, 1]</c>
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3358-3373</c> for FaceMesh, and <c>:3520-3540</c>
    /// for the iris).
    /// </summary>
    /// <param name="frame">The frame's pixels, as the sink was handed them. Never retained.</param>
    /// <param name="info">The geometry delivered WITH those pixels.</param>
    /// <param name="crop">The square region, from <see cref="FaceCrop"/> or <see cref="EyeCrop"/>.</param>
    /// <param name="input">The model's square input side.</param>
    /// <param name="flip">
    /// <b>True for the RIGHT eye and for nothing else.</b> The iris model was trained on left eyes,
    /// so upstream mirrors the right eye's crop about the vertical axis before inference
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3534-3538</c>) and un-mirrors the landmarks on
    /// the way out (<see cref="FromCrop"/>). <c>FlipMode.Y</c> maps column <c>c</c> of the fed image
    /// to column <c>N - 1 - c</c> of the resized one, which is what this reads instead of copying.
    /// </param>
    /// <param name="destination">At least <c>input * input * 3</c> floats, channel-last.</param>
    public static void FillCrop(
        ReadOnlySpan<byte> frame, CameraFrameInfo info, GazeCrop crop, int input,
        bool flip, Span<float> destination)
    {
        Require(frame, info, input, destination);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(crop.Side);

        var tensor = destination[..(input * input * TensorChannels)];
        var scale = (double)crop.Side / input;

        for (var dy = 0; dy < input; dy++)
        {
            Taps(dy, scale, crop.Side, out var top, out var bottom, out var weightY);

            for (var dx = 0; dx < input; dx++)
            {
                Taps(flip ? input - 1 - dx : dx, scale, crop.Side, out var left, out var right, out var weightX);
                var at = ((dy * input) + dx) * TensorChannels;

                tensor[at + 0] = Sample(frame, info, crop.X, crop.Y, left, right, top, bottom, weightX, weightY, RedByte) * CropScale;
                tensor[at + 1] = Sample(frame, info, crop.X, crop.Y, left, right, top, bottom, weightX, weightY, GreenByte) * CropScale;
                tensor[at + 2] = Sample(frame, info, crop.X, crop.Y, left, right, top, bottom, weightX, weightY, BlueByte) * CropScale;
            }
        }
    }

    /// <summary>
    /// Map one landmark from a crop model's tensor coordinates back to source-frame pixels
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3402-3404</c> for FaceMesh, and <c>:3564-3566</c>
    /// for the iris).
    ///
    /// <para><b>Upstream writes the same map twice in two spellings and they agree exactly.</b>
    /// FaceMesh divides by the input side; the iris model multiplies by a cached
    /// <c>1f / InputSize</c> (<c>Services/Webcam/WebcamTrackingService.cs:3558</c>). For 64 that
    /// reciprocal is a power of two and therefore exact, so one implementation is bit-identical to
    /// both.</para>
    /// </summary>
    /// <param name="unflip">True for the RIGHT eye, mirroring the tensor x back with
    /// <c>input - x</c> — upstream's own expression, which is <c>input - x</c> and not
    /// <c>input - 1 - x</c> (<c>Services/Webcam/WebcamTrackingService.cs:3564</c>).</param>
    public static (float X, float Y) FromCrop(
        GazeCrop crop, int input, float tensorX, float tensorY, bool unflip)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input);

        var x = unflip ? input - tensorX : tensorX;
        return (crop.X + (x / input * crop.Side), crop.Y + (tensorY / input * crop.Side));
    }

    /// <summary>Upstream's square-from-a-centre, written once because both ROIs use it verbatim
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3341-3343</c> and <c>:3502-3504</c>).
    /// <see cref="Math.Round(double)"/> is .NET's banker's rounding and upstream takes the default
    /// too; a centre offset of exactly <c>-2.5</c> gives <c>-2</c>.</summary>
    private static GazeCrop Square(float centreX, float centreY, float side) => new(
        (int)Math.Round(centreX - (side / 2f)),
        (int)Math.Round(centreY - (side / 2f)),
        (int)Math.Round(side));

    /// <summary>
    /// OpenCV's <c>INTER_LINEAR</c> coordinate map for one axis: half-pixel centres, and edge
    /// replication for a destination pixel whose source coordinate falls outside the source. Both
    /// taps collapse onto the same pixel when the weight is zero, so no read ever runs past the
    /// source's last row or column.
    /// </summary>
    private static void Taps(int destination, double scale, int length, out int low, out int high, out float weight)
    {
        var exact = (float)(((destination + 0.5) * scale) - 0.5);
        var floor = (int)MathF.Floor(exact);
        var fraction = exact - floor;

        if (floor < 0)
        {
            floor = 0;
            fraction = 0f;
        }

        if (floor >= length - 1)
        {
            floor = length - 1;
            fraction = 0f;
        }

        low = floor;
        high = fraction > 0f ? floor + 1 : floor;
        weight = fraction;
    }

    /// <summary>One bilinear sample of one channel, quantised to a byte exactly where OpenCV's
    /// fixed-point descale quantises — before the normalisation, never after, because upstream's
    /// intermediate really is an 8-bit Mat.</summary>
    private static float Sample(
        ReadOnlySpan<byte> frame, CameraFrameInfo info, int originX, int originY,
        int left, int right, int top, int bottom, float weightX, float weightY, int channel)
    {
        var upper = Blend(
            Pixel(frame, info, originX + left, originY + top, channel),
            Pixel(frame, info, originX + right, originY + top, channel),
            weightX);
        var lower = Blend(
            Pixel(frame, info, originX + left, originY + bottom, channel),
            Pixel(frame, info, originX + right, originY + bottom, channel),
            weightX);

        // Half away from zero, which for a convex blend of non-negative bytes is half up.
        return Math.Clamp((int)(Blend(upper, lower, weightY) + 0.5f), 0, 255);
    }

    private static float Blend(float low, float high, float weight) => (low * (1f - weight)) + (high * weight);

    /// <summary>
    /// One channel byte of one PICTURE pixel, or 0 for a coordinate outside the frame — the black
    /// upstream's zeroed crop buffer supplies (<c>Services/Webcam/WebcamTrackingService.cs:3349</c>).
    ///
    /// <para><b><see cref="CameraFrameInfo.BottomUp"/> is honoured here and nowhere else</b>, so
    /// every caller above works in picture coordinates. A buffer whose first row is the picture's
    /// last row would otherwise produce an upside-down face, which a detector partly copes with and
    /// a gaze vector does not.</para>
    /// </summary>
    private static byte Pixel(ReadOnlySpan<byte> frame, CameraFrameInfo info, int x, int y, int channel)
    {
        if ((uint)x >= (uint)info.Width || (uint)y >= (uint)info.Height)
        {
            return 0;
        }

        var row = info.BottomUp ? info.Height - 1 - y : y;
        return frame[(row * info.Stride) + (x * CameraFrameProbe.BytesPerPixel) + channel];
    }

    /// <summary>The trust boundary: a frame shorter than its own declared geometry, or a destination
    /// too small for the tensor, is a caller defect and never a silently truncated tensor a model
    /// would happily consume.</summary>
    private static void Require(
        ReadOnlySpan<byte> frame, CameraFrameInfo info, int input, Span<float> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input);

        if (!info.Describes)
        {
            throw new ArgumentException(
                $"the frame geometry does not describe a picture ({info.Width}x{info.Height}, stride {info.Stride})",
                nameof(info));
        }

        if (frame.Length < info.Bytes)
        {
            throw new ArgumentException(
                $"the frame is {frame.Length} bytes but its geometry declares {info.Bytes}",
                nameof(frame));
        }

        var needed = input * input * TensorChannels;
        if (destination.Length < needed)
        {
            throw new ArgumentException(
                $"the destination holds {destination.Length} floats but a {input}x{input} tensor needs {needed}",
                nameof(destination));
        }
    }
}
