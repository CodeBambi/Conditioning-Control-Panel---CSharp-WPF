using CcpClient.Desktop.Camera;
using CcpClient.Desktop.Gaze;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The arithmetic between a camera frame and the three MediaPipe models' inputs, held to
/// upstream's numbers rather than to plausible ones.</b>
///
/// <para><b>Every expectation below is worked out from upstream's FORMULA by hand and written down
/// as a literal.</b> That is the whole point of the file. A fact that recomputed the expectation the
/// way <see cref="GazePreprocess"/> computes it would pass over any consistent implementation,
/// including one that letterboxes to the wrong offset or resizes on the wrong pixel centres — and a
/// resize that is merely plausible produces landmarks that are merely plausible, which looks like a
/// tracking accuracy problem forever. So: <c>640x480</c> at scale <c>0.2</c> gives a <c>128x96</c>
/// picture with a <c>16</c>-row pad, and <c>16</c> is typed here as <c>16</c>.</para>
///
/// <para><b>Where a resize is checked, the picture is chosen so bilinear interpolation has an
/// arithmetic answer.</b> A <c>256 -> 128</c> resize puts every destination centre exactly halfway
/// between two source pixels, so a source whose red channel is its own x coordinate must come back
/// as <c>2x + 1</c> — the mean of <c>2x</c> and <c>2x + 1</c>, rounded up at the half. That single
/// number simultaneously pins the half-pixel centre convention (a naive <c>x * scale</c> map gives
/// <c>2x</c>), the rounding direction (banker's rounding gives <c>2x</c> too) and the channel order.
/// None of it is read off the implementation.</para>
///
/// <para><b>What these facts do NOT establish.</b> No model is loaded, no inference runs, and no
/// camera is opened here: the frames are synthetic buffers. Nothing here says a real face is found,
/// and nothing here is evidence about any platform — this is pure arithmetic over a span.</para>
/// </summary>
public sealed class GazePreprocessTests
{
    /// <summary>A tolerance well under one 8-bit step (<c>1/255</c> is <c>0.0039</c>), so it can
    /// absorb the last bit of a float normalisation and nothing else.</summary>
    private const float Tolerance = 1e-5f;

    // ======================================================================================
    // Geometry: the letterbox, the decode back out of it, and the two crop expansions.
    // ======================================================================================

    /// <summary>
    /// <b>Upstream's letterbox, by hand.</b> <c>640x480</c> into <c>128</c>: the scale is
    /// <c>min(128/640, 128/480) = 0.2</c>, so the picture becomes <c>128x96</c> and the 32 spare rows
    /// split into 16 above and 16 below. Rotate the frame and the same numbers land on the other
    /// axis.
    ///
    /// <para>The third case is the one that matters most and is easiest to get wrong: <c>128x91</c>
    /// leaves <b>37</b> spare rows, and upstream's <c>(InputSize - newH) / 2</c> is INTEGER division,
    /// so the picture sits at row 18 with 19 blank rows below it rather than being centred. An
    /// implementation that rounded that pad up would decode every box one pixel out, forever, in a
    /// way no visual inspection would ever catch.</para>
    /// </summary>
    [Fact]
    public void TheLetterboxIsUpstreamsScaleAndItsTruncatedAsymmetricPad()
    {
        Assert.Equal(
            new GazeLetterbox(128, 0.2f, 128, 96, 0, 16),
            GazePreprocess.Letterbox(640, 480, GazePreprocess.DetectorInput));

        Assert.Equal(
            new GazeLetterbox(128, 0.2f, 96, 128, 16, 0),
            GazePreprocess.Letterbox(480, 640, GazePreprocess.DetectorInput));

        var odd = GazePreprocess.Letterbox(128, 91, GazePreprocess.DetectorInput);
        Assert.Equal(new GazeLetterbox(128, 1f, 128, 91, 0, 18), odd);
        Assert.Equal(19, 128 - odd.Height - odd.PadY);
    }

    /// <summary>
    /// <b>The decode, worked backwards from the same numbers.</b> With the <c>640x480</c> letterbox
    /// above, a source box at <c>(100, 60) 200x150</c> occupies padded pixels
    /// <c>(100*0.2, 60*0.2 + 16) = (20, 28)</c> sized <c>40x30</c>, which normalised against 128 is
    /// <c>(0.15625, 0.21875)</c> sized <c>0.3125 x 0.234375</c>. Feeding those four exact binary
    /// fractions in must hand the original box back.
    ///
    /// <para>The second case is the clip. A box covering the WHOLE padded square reaches above the
    /// picture — <c>(0 - 16) / 0.2 = -80</c> — and upstream shortens the box rather than sliding it
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3244-3247</c>), so the answer is the frame, not
    /// an 80-pixel-taller box hanging off the top of it. The third is a box entirely to the right of
    /// the frame, which upstream reports as no face at all.</para>
    /// </summary>
    [Fact]
    public void TheDecodeUnpadsAndUnscales_ThenClipsTheBoxRatherThanMovingIt()
    {
        var box = GazePreprocess.Letterbox(640, 480, GazePreprocess.DetectorInput);

        Assert.Equal(
            new GazeRect(100, 60, 200, 150),
            GazePreprocess.UnLetterbox(box, 640, 480, 0.15625f, 0.21875f, 0.3125f, 0.234375f));

        Assert.Equal(
            new GazeRect(0, 0, 640, 480),
            GazePreprocess.UnLetterbox(box, 640, 480, 0f, 0f, 1f, 1f));

        var offFrame = GazePreprocess.UnLetterbox(box, 640, 480, 1.5f, 0.21875f, 0.1f, 0.1f);
        Assert.False(offFrame.Describes);
    }

    /// <summary>
    /// <b>FaceMesh's 1.5x crop, and the rounding mode nobody notices until it is wrong.</b> A
    /// <c>200x150</c> box centred at <c>(200, 135)</c> expands its LONGER side to <c>300</c>, so the
    /// square starts at <c>(200 - 150, 135 - 150) = (50, -15)</c>. The negative origin is correct
    /// and deliberate: upstream pads outside the frame instead of sliding the square in.
    ///
    /// <para>The second box is chosen to land on two exact halves. <c>90 * 1.5 = 135</c>, so the
    /// origins are <c>30 - 67.5 = -37.5</c> and <c>65 - 67.5 = -2.5</c>. Upstream uses
    /// <see cref="Math.Round(double)"/> with its default, which is banker's rounding — so those go
    /// to <c>-38</c> and <c>-2</c>, one away from zero and one towards it. Round half away from zero
    /// instead and the second becomes <c>-3</c>; truncate and the first becomes <c>-37</c>.</para>
    /// </summary>
    [Fact]
    public void TheFaceCropExpandsTheLongerSideByOnePointFive_RoundingHalvesToEven()
    {
        Assert.Equal(
            new GazeCrop(50, -15, 300),
            GazePreprocess.FaceCrop(new GazeRect(100, 60, 200, 150)));

        Assert.Equal(
            new GazeCrop(-38, -2, 135),
            GazePreprocess.FaceCrop(new GazeRect(10, 20, 40, 90)));
    }

    /// <summary>
    /// <b>The iris model's 2.3x crop of the eye-corner pair.</b> Corners at <c>(300, 200)</c> and
    /// <c>(340, 210)</c> bound a <c>40x10</c> box centred at <c>(320, 205)</c>; the longer side times
    /// <c>2.3</c> is <c>92</c>, so the square starts at <c>(320 - 46, 205 - 46) = (274, 159)</c>.
    ///
    /// <para><b>The corners are min/maxed rather than assumed ordered</b>
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3494-3497</c>): the outer corner of a LEFT eye
    /// and of a RIGHT eye sit on opposite sides of the inner one, so the same landmark pair arrives
    /// reversed for one of them. Passing the same two points the other way round must give the same
    /// square, and a subtraction that assumed an order would give a negative side.</para>
    /// </summary>
    [Fact]
    public void TheEyeCropExpandsTheCornerPairByTwoPointThree_WhicheverOrderTheCornersArriveIn()
    {
        Assert.Equal(new GazeCrop(274, 159, 92), GazePreprocess.EyeCrop(300f, 200f, 340f, 210f));
        Assert.Equal(new GazeCrop(274, 159, 92), GazePreprocess.EyeCrop(340f, 210f, 300f, 200f));
    }

    /// <summary>
    /// <b>The refusal floor, which upstream writes twice and this writes once.</b> FaceMesh refuses a
    /// side under 16 (<c>Services/Webcam/WebcamTrackingService.cs:3344</c>); the iris model refuses
    /// one under <c>InputSize / 4</c>, and its <c>InputSize</c> is 64
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3505</c>). Same number.
    ///
    /// <para>The boundary is picked so the rounding shows: an <c>11x11</c> face expands to exactly
    /// <c>16.5</c>, which banker's rounding takes DOWN to 16 — and 16 is admitted, because upstream's
    /// test is <c>&lt; 16</c>. A <c>10x10</c> face gives 15 and is refused.</para>
    /// </summary>
    [Fact]
    public void ACropBelowUpstreamsFloorIsRefused_AndTheFloorItselfIsAdmitted()
    {
        Assert.Equal(16, GazePreprocess.MinCropSide);

        Assert.False(GazePreprocess.FaceCrop(new GazeRect(0, 0, 10, 10)).Describes);
        Assert.True(GazePreprocess.FaceCrop(new GazeRect(0, 0, 11, 11)).Describes);

        Assert.False(GazePreprocess.EyeCrop(0f, 0f, 6f, 0f).Describes);
        Assert.True(GazePreprocess.EyeCrop(0f, 0f, 7f, 0f).Describes);
    }

    /// <summary>
    /// <b>Tensor coordinates back to source pixels, in both spellings upstream uses.</b> The iris
    /// case: a <c>92</c>-pixel crop at <c>(274, 159)</c> fed to a 64-pixel model, landmark at
    /// <c>(20, 30)</c>. Unflipped that is <c>274 + (20/64)*92 = 302.75</c> and
    /// <c>159 + (30/64)*92 = 202.125</c>. For a RIGHT eye upstream mirrors x with <c>64 - x</c>
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3564</c>) — <b>not</b> <c>63 - x</c> — so
    /// <c>44/64</c> of 92 is <c>63.25</c> and the answer is <c>337.25</c>.
    ///
    /// <para>The FaceMesh case is a check that needs no arithmetic at all: the crop is BUILT around
    /// the face's centre, so the centre of the tensor must map to the centre of the face box. A
    /// <c>200x150</c> box at <c>(100, 60)</c> is centred at <c>(200, 135)</c>, and tensor
    /// <c>(96, 96)</c> of 192 must land there.</para>
    /// </summary>
    [Fact]
    public void ALandmarkMapsBackToSourcePixels_AndTheRightEyeIsUnflippedOnTheWayOut()
    {
        var eye = new GazeCrop(274, 159, 92);

        var left = GazePreprocess.FromCrop(eye, GazePreprocess.IrisInput, 20f, 30f, unflip: false);
        Assert.Equal(302.75f, left.X, Tolerance);
        Assert.Equal(202.125f, left.Y, Tolerance);

        var right = GazePreprocess.FromCrop(eye, GazePreprocess.IrisInput, 20f, 30f, unflip: true);
        Assert.Equal(337.25f, right.X, Tolerance);
        Assert.Equal(202.125f, right.Y, Tolerance);

        var face = GazePreprocess.FaceCrop(new GazeRect(100, 60, 200, 150));
        var centre = GazePreprocess.FromCrop(face, GazePreprocess.MeshInput, 96f, 96f, unflip: false);
        Assert.Equal(200f, centre.X, Tolerance);
        Assert.Equal(135f, centre.Y, Tolerance);
    }

    // ======================================================================================
    // Pixels: the resize itself, the pad, the buffer layout, the flip.
    // ======================================================================================

    /// <summary>
    /// <b>The detector tensor, one number at a time.</b> A <c>256x256</c> source letterboxes into
    /// <c>128</c> with no pad at all and a resize scale of exactly 2, so OpenCV's half-pixel map puts
    /// every destination centre at <c>2*dx + 0.5</c> — dead between source columns <c>2*dx</c> and
    /// <c>2*dx + 1</c>. With the red channel painted as its own x coordinate, the answer is the mean
    /// of those two, <c>2*dx + 0.5</c>, rounded up: <b><c>2*dx + 1</c></b>.
    ///
    /// <para>That one expectation is three assertions in one. A naive <c>dx * scale</c> map without
    /// the half-pixel shift samples column <c>2*dx</c> alone and gives <c>2*dx</c>. Rounding the half
    /// to even also gives <c>2*dx</c>. And because the three channels are painted differently —
    /// red is x, green is y, blue is a constant 7 — a tensor written in BGR order puts blue's
    /// <c>-0.9451</c> in the slot red belongs in.</para>
    ///
    /// <para>The normalisation is upstream's <c>[-1, 1]</c>
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3262</c>), so a byte <c>b</c> reads
    /// <c>b * 2/255 - 1</c> and the literals below are that arithmetic done by hand.</para>
    /// </summary>
    [Fact]
    public void TheDetectorTensorIsBilinearOnHalfPixelCentres_RgbOrdered_AndScaledToMinusOneToOne()
    {
        var info = new CameraFrameInfo(256, 256, 256 * 4, BottomUp: false);
        var frame = Picture(info, (x, y) => ((byte)x, (byte)y, (byte)7));
        var tensor = new float[128 * 128 * 3];

        var box = GazePreprocess.FillDetector(frame, info, tensor);
        Assert.Equal(new GazeLetterbox(128, 0.5f, 128, 128, 0, 0), box);

        // Red is 2*dx + 1: bytes 1, 3, 127, 255 at dx = 0, 1, 63, 127.
        Assert.Equal(-0.99215686f, Read(tensor, 128, 0, 0, 0), Tolerance);
        Assert.Equal(-0.97647059f, Read(tensor, 128, 1, 0, 0), Tolerance);
        Assert.Equal(-0.00392157f, Read(tensor, 128, 63, 0, 0), Tolerance);
        Assert.Equal(1f, Read(tensor, 128, 127, 0, 0), Tolerance);

        // Green is 2*dy + 1 and does not vary with x.
        Assert.Equal(-0.99215686f, Read(tensor, 128, 40, 0, 1), Tolerance);
        Assert.Equal(1f, Read(tensor, 128, 40, 127, 1), Tolerance);

        // Blue is the constant 7 everywhere, which is what makes the two above unmistakable.
        Assert.Equal(-0.94509804f, Read(tensor, 128, 0, 0, 2), Tolerance);
        Assert.Equal(-0.94509804f, Read(tensor, 128, 127, 127, 2), Tolerance);
    }

    /// <summary>
    /// <b>The pad is black, and it is where the truncating division puts it.</b> A <c>128x91</c>
    /// source scales by exactly 1, so 91 picture rows sit inside 128 with an 18-row pad above and a
    /// 19-row pad below. Every pad row must read exactly <c>-1</c>, which is what upstream's zeroed
    /// square normalises to through <c>b * 2/255 - 1</c>.
    ///
    /// <para>Rows 17 and 18 are asserted as a PAIR, and so are 108 and 109. Either boundary moving by
    /// one is the whole failure.</para>
    ///
    /// <para>The three channels are painted <c>200</c>, <c>100</c> and <c>50</c> rather than one flat
    /// grey, so the picture band's three readings are three DIFFERENT numbers — <c>0.5686</c>,
    /// <c>-0.2157</c>, <c>-0.6078</c> — and a channel swap cannot hide inside a uniform frame.</para>
    /// </summary>
    [Fact]
    public void TheLetterboxPadIsBlack_AndThePictureBandSitsWhereTheTruncationPutsIt()
    {
        var info = new CameraFrameInfo(128, 91, 128 * 4, BottomUp: false);
        var frame = Picture(info, (_, _) => (200, 100, 50));
        var tensor = new float[128 * 128 * 3];

        GazePreprocess.FillDetector(frame, info, tensor);

        // The pad above the picture, and the pad below it.
        Assert.Equal(-1f, Read(tensor, 128, 64, 17, 0), Tolerance);
        Assert.Equal(-1f, Read(tensor, 128, 64, 17, 1), Tolerance);
        Assert.Equal(-1f, Read(tensor, 128, 64, 17, 2), Tolerance);
        Assert.Equal(-1f, Read(tensor, 128, 64, 109, 0), Tolerance);
        Assert.Equal(-1f, Read(tensor, 128, 64, 109, 1), Tolerance);
        Assert.Equal(-1f, Read(tensor, 128, 64, 109, 2), Tolerance);

        // The first and last rows of picture: 200, 100 and 50 through the [-1, 1] normalisation.
        Assert.Equal(0.5686275f, Read(tensor, 128, 64, 18, 0), Tolerance);
        Assert.Equal(-0.21568627f, Read(tensor, 128, 64, 18, 1), Tolerance);
        Assert.Equal(-0.60784314f, Read(tensor, 128, 64, 18, 2), Tolerance);
        Assert.Equal(0.5686275f, Read(tensor, 128, 64, 108, 0), Tolerance);
        Assert.Equal(-0.21568627f, Read(tensor, 128, 64, 108, 1), Tolerance);
        Assert.Equal(-0.60784314f, Read(tensor, 128, 64, 108, 2), Tolerance);
    }

    /// <summary>
    /// <b>A bottom-up buffer is the same PICTURE, so it must produce the same TENSOR.</b> Media
    /// Foundation reports a negative default stride when the buffer's first row is the picture's
    /// last, and this port has already measured that on its own media
    /// (<c>Camera/ICameraCaptureSource.cs:19-25</c>). The consequence of ignoring it is not a crash
    /// and not a blank frame: it is an upside-down face, which a detector partly copes with and a
    /// gaze vector cannot, so it reads as poor accuracy rather than as a bug.
    ///
    /// <para>The two buffers here hold the same picture — the second is the first with its ROWS
    /// REVERSED, which is what bottom-up means — and the picture varies down the frame, so a
    /// preprocessing pass that ignored the flag could not produce equal tensors.</para>
    /// </summary>
    [Fact]
    public void ABottomUpBufferProducesTheSameTensorAsTheSamePictureStoredTopDown()
    {
        var down = new CameraFrameInfo(64, 64, 64 * 4, BottomUp: false);
        var up = down with { BottomUp = true };

        var topDown = Picture(down, (x, y) => ((byte)x, (byte)(y * 3), (byte)7));
        var bottomUp = ReverseRows(topDown, up);

        var a = new float[128 * 128 * 3];
        var b = new float[128 * 128 * 3];
        GazePreprocess.FillDetector(topDown, down, a);
        GazePreprocess.FillDetector(bottomUp, up, b);

        // Not vacuous: the picture really does vary down the frame, so the two buffers differ.
        Assert.NotEqual(topDown, bottomUp);
        Assert.Equal(a, b);
    }

    /// <summary>
    /// <b>Rows are found through the stride, never through the width.</b> A driver may pad every row,
    /// and <see cref="CameraFrameInfo.Stride"/> is read back from the operating system rather than
    /// computed for exactly that reason. Here the same picture is delivered twice, once tightly
    /// packed and once with 64 junk bytes on the end of every row; the tensors must be identical.
    /// The padding is <c>0xEE</c> rather than zero so a width-indexed read drifts into visible
    /// garbage instead of into black that might pass.
    /// </summary>
    [Fact]
    public void RowsAreIndexedByStride_NotByWidth()
    {
        var packed = new CameraFrameInfo(64, 64, 64 * 4, BottomUp: false);
        var padded = packed with { Stride = (64 * 4) + 64 };

        var a = new float[128 * 128 * 3];
        var b = new float[128 * 128 * 3];
        GazePreprocess.FillDetector(Picture(packed, Ramp), packed, a);
        GazePreprocess.FillDetector(Picture(padded, Ramp), padded, b);

        Assert.Equal(a, b);
    }

    /// <summary>
    /// <b>Outside the frame is black, and the resize BLENDS across that edge rather than stopping at
    /// it.</b> Upstream copies only the in-bounds part of a crop into a zeroed square and then
    /// resizes the square whole (<c>Services/Webcam/WebcamTrackingService.cs:3358-3372</c>), so a
    /// destination pixel straddling the frame's edge is half picture and half black.
    ///
    /// <para>The numbers: a <c>64</c>-wide crop starting at <c>x = -31</c> over a flat-100 frame is
    /// black in its columns 0-30 and picture in 31-63. Resized to 32 the scale is 2, so destination
    /// column <c>dx</c> averages source columns <c>2*dx</c> and <c>2*dx + 1</c> — column 14 averages
    /// 28 and 29 (both black, 0), column 15 averages 30 and 31 (<b>black and picture, so 50</b>),
    /// and column 16 averages 32 and 33 (both picture, 100). Normalised by <c>1/255</c> those are
    /// <c>0</c>, <c>0.19608</c> and <c>0.39216</c>.</para>
    /// </summary>
    [Fact]
    public void TheCropIsBlackOutsideTheFrame_AndTheResizeBlendsAcrossThatEdge()
    {
        var info = new CameraFrameInfo(64, 64, 64 * 4, BottomUp: false);
        var frame = Picture(info, (_, _) => (100, 100, 100));
        var tensor = new float[32 * 32 * 3];

        GazePreprocess.FillCrop(frame, info, new GazeCrop(-31, 0, 64), 32, flip: false, tensor);

        Assert.Equal(0f, Read(tensor, 32, 14, 8, 0), Tolerance);          // wholly outside the frame
        Assert.Equal(0.19607843f, Read(tensor, 32, 15, 8, 0), Tolerance); // straddling its edge
        Assert.Equal(0.39215686f, Read(tensor, 32, 16, 8, 0), Tolerance); // wholly inside
        Assert.Equal(0.39215686f, Read(tensor, 32, 31, 8, 0), Tolerance); // the far side of the crop
    }

    /// <summary>
    /// <b>The right eye is fed MIRRORED, because the model was trained on left eyes.</b> Upstream
    /// flips the resized crop about the vertical axis before inference
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3536</c>), which maps column <c>c</c> to column
    /// <c>N - 1 - c</c>.
    ///
    /// <para>This reuses the previous fact's picture, whose columns are asymmetric by construction:
    /// black on the left, a 50 at column 15, picture on the right. Mirrored, the 50 must be at column
    /// <b>16</b> and the black must be on the right — every named column is the previous fact's
    /// literal, read from <c>31 - dx</c> instead of <c>dx</c>.</para>
    /// </summary>
    [Fact]
    public void TheRightEyeCropIsFedMirroredAboutTheVerticalAxis()
    {
        var info = new CameraFrameInfo(64, 64, 64 * 4, BottomUp: false);
        var frame = Picture(info, (_, _) => (100, 100, 100));
        var tensor = new float[32 * 32 * 3];

        GazePreprocess.FillCrop(frame, info, new GazeCrop(-31, 0, 64), 32, flip: true, tensor);

        Assert.Equal(0f, Read(tensor, 32, 17, 8, 0), Tolerance);          // the black side, now right
        Assert.Equal(0.19607843f, Read(tensor, 32, 16, 8, 0), Tolerance); // the straddling column
        Assert.Equal(0.39215686f, Read(tensor, 32, 15, 8, 0), Tolerance); // the picture side, now left
        Assert.Equal(0.39215686f, Read(tensor, 32, 0, 8, 0), Tolerance);  // the far side of the crop
    }

    /// <summary>
    /// <b>A frame that does not match its own declared geometry is a caller defect, not a tensor.</b>
    /// The alternative is a model fed a silently truncated picture, which produces landmarks with
    /// nothing wrong with them except that they are wrong.
    /// </summary>
    [Fact]
    public void AFrameShorterThanItsGeometry_OrATensorTooSmall_IsRefusedRatherThanTruncated()
    {
        var info = new CameraFrameInfo(64, 64, 64 * 4, BottomUp: false);
        var frame = Picture(info, Ramp);

        Assert.Throws<ArgumentException>(() =>
        {
            var tensor = new float[128 * 128 * 3];
            GazePreprocess.FillDetector(frame.AsSpan(0, frame.Length - 4), info, tensor);
        });

        Assert.Throws<ArgumentException>(() =>
        {
            var tensor = new float[(128 * 128 * 3) - 1];
            GazePreprocess.FillDetector(frame, info, tensor);
        });
    }

    // ======================================================================================
    // Helpers. None of these knows anything about how GazePreprocess works.
    // ======================================================================================

    /// <summary>A red-is-x, green-is-y, blue-is-constant painter — asymmetric in both axes, so a
    /// swapped or dropped index cannot look right.</summary>
    private static (byte R, byte G, byte B) Ramp(int x, int y) => ((byte)x, (byte)y, 7);

    /// <summary>
    /// A TOP-DOWN frame buffer in Media Foundation's RGB32 layout — B, G, R, X per pixel
    /// (<c>Camera/CameraFrameProbe.cs:43-49</c>). Every byte the preprocessing must never read (the X
    /// byte and any stride padding) is <c>0xEE</c>, so reading one shows up as a wild value rather
    /// than as a plausible dark pixel. Bottom-up buffers are made by <see cref="ReverseRows"/>, never
    /// by this, so nothing here has to agree with the code under test about what the flag means.
    /// </summary>
    private static byte[] Picture(CameraFrameInfo info, Func<int, int, (byte R, byte G, byte B)> paint)
    {
        var buffer = new byte[info.Bytes];
        Array.Fill(buffer, (byte)0xEE);

        for (var y = 0; y < info.Height; y++)
        {
            for (var x = 0; x < info.Width; x++)
            {
                var (r, g, b) = paint(x, y);
                var at = (y * info.Stride) + (x * CameraFrameProbe.BytesPerPixel);
                buffer[at + 0] = b;
                buffer[at + 1] = g;
                buffer[at + 2] = r;
            }
        }

        return buffer;
    }

    /// <summary>The same bytes with the ROWS in the opposite order, which is the definition of a
    /// bottom-up buffer and involves no knowledge of the code under test.</summary>
    private static byte[] ReverseRows(byte[] source, CameraFrameInfo info)
    {
        var flipped = new byte[source.Length];
        for (var y = 0; y < info.Height; y++)
        {
            Array.Copy(source, y * info.Stride, flipped, (info.Height - 1 - y) * info.Stride, info.Stride);
        }

        return flipped;
    }

    /// <summary>One channel of one tensor pixel, channel-last. <b>The comparison stays at the call
    /// site rather than moving into a <c>Near(expected, actual, where)</c> helper</b>, which is what
    /// this file did first: a helper that owns the <c>Assert</c> makes every fact read as
    /// assertion-free to <see cref="VacuousShapeDetector"/>, and four of these facts were caught by
    /// it on the floor. The right answer was to make the assertion visible, not to write four
    /// dispositions into the ledger.</summary>
    private static float Read(float[] tensor, int side, int x, int y, int channel) =>
        tensor[(((y * side) + x) * GazePreprocess.TensorChannels) + channel];
}
