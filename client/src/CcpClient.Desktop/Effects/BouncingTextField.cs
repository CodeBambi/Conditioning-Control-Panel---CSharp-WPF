using CcpClient.Desktop.Haptics;

namespace CcpClient.Desktop.Effects;

/// <summary>One logo's motion state, in physical pixels and pixels per second.</summary>
/// <param name="Text">The word currently shown.</param>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">The rendered word's width.</param>
/// <param name="Height">Its height.</param>
/// <param name="Colour">Its colour, as a <c>COLORREF</c> (<c>0x00BBGGRR</c>).</param>
public readonly record struct BouncingLogo(
    string Text, double X, double Y, int Width, int Height, uint Colour);

/// <summary>
/// The DVD-screensaver motion, and nothing else: no window, no rasteriser, no clock, no operating
/// system. WPF's own arithmetic out of
/// <c>Services/Subliminal/BouncingTextService.cs</c>, so the bounce can be proven frame by frame in
/// the pure-logic project with no desktop anywhere near it.
///
/// <para><b>What is ported, with its line.</b> The delta-time step and its stall clamp
/// (<c>:352-368</c>: a first frame establishes the baseline and returns, a non-positive delta is
/// dropped, and anything over 0.1 s is clamped so the text never teleports across the screen); the
/// four wall reflections, each of which SNAPS the edge back to the wall before flipping the velocity
/// (<c>:414-455</c>); the corner rule, which is both axes bouncing in one step OR a single-axis
/// bounce within 15 px of a corner (<c>:57</c>, <c>:459-470</c>, <c>:609-622</c>); the speed
/// mapping, <c>(3 + rand·2)·60</c> pixels per second times <c>setting/10</c> with a random sign per
/// axis (<c>:162-168</c>); the colour change on every bounce and the 10 % chance of a new word
/// (<c>:489-556</c>); and the three colour modes — ten fixed bright colours, one user colour with a
/// hot-pink fallback, and an ordered twelve-step hue wheel advanced one step per bounce
/// (<c>:633-700</c>).</para>
///
/// <para><b>What is NOT ported</b>, and is a recorded divergence rather than a silent omission: the
/// six per-frame transform effects (breathing, wobble, spin, velocity tilt, squash and stretch,
/// corner burst — <c>:558-600</c>), the XP award and its rate limit (<c>:499-513</c>), the
/// achievement and haptic hooks, and the second logo. See <c>wpf-surface-reachability.md</c>.</para>
///
/// <para><b>Randomness is injected</b>, so a fact can pin an exact trajectory and a seeded run can
/// be replayed. The field never reads a clock: the caller supplies the elapsed seconds.</para>
/// </summary>
public sealed class BouncingTextField
{
    /// <summary>WPF's corner tolerance in pixels (<c>BouncingTextService.cs:57</c>).</summary>
    public const double CornerTolerance = 15.0;

    /// <summary>WPF's stall clamp (<c>:367</c>): a frame longer than this is treated as 0.1 s, so a
    /// stalled compositor does not teleport the text off the screen.</summary>
    public const double MaxStepSeconds = 0.1;

    /// <summary>WPF's chance of re-rolling the word on a bounce (<c>:549</c>).</summary>
    public const double TextChangeChanceOnBounce = 0.1;

    /// <summary>WPF's fallback word when the pool has nothing enabled (<c>:272</c>).</summary>
    public const string FallbackText = "GOOD GIRL";

    /// <summary>WPF's hot-pink fallback for the fixed-colour mode (<c>:665</c>), as a
    /// <c>COLORREF</c>: R=255 G=105 B=180.</summary>
    public const uint HotPink = 0x00B469FF;

    /// <summary>WPF's ten bright colours for the random mode (<c>:670-682</c>), in its own order,
    /// as <c>COLORREF</c> values.</summary>
    public static readonly uint[] RandomColours =
    [
        0x00B469FF, // hot pink        255,105,180
        0x009314FF, // deep pink       255, 20,147
        0x00E22B8A, // blue violet     138, 43,226
        0x00FF00FF, // magenta         255,  0,255
        0x00FFFF00, // cyan              0,255,255
        0x0000FFFF, // yellow          255,255,  0
        0x0000FF00, // lime              0,255,  0
        0x0000A5FF, // orange          255,165,  0
        0x000045FF, // red orange      255, 69,  0
        0x0032CD32, // lime green       50,205, 50
    ];

    /// <summary>WPF's ordered twelve-step hue wheel at full saturation and value
    /// (<c>:693-702</c>), advanced one step per bounce in the rainbow mode.</summary>
    public static readonly uint[] RainbowWheel = BuildRainbowWheel();

    private readonly Random _random;
    private readonly BouncingTextPresentation _presentation;
    private readonly Func<string, (int Width, int Height)> _measure;
    private readonly IHapticLimb? _haptics;

    private double _x;
    private double _y;
    private double _velocityX;
    private double _velocityY;
    private int _hueIndex;
    private string _text = FallbackText;
    private int _width;
    private int _height;
    private uint _colour;
    private bool _started;

    /// <param name="presentation">The dials this run uses.</param>
    /// <param name="bounds">The field the logo bounces inside, in physical pixels.</param>
    /// <param name="measure">How wide and tall a word renders. Injected so the motion can be proven
    /// with no rasteriser at all.</param>
    /// <param name="random">Injected, so a trajectory can be pinned exactly.</param>
    /// <param name="haptics">SP-126: the haptic limb, told once per wall bounce. Null is ABSENT
    /// rather than silent — this field has no limb at all in a build that wires none.</param>
    public BouncingTextField(
        BouncingTextPresentation presentation,
        (int X, int Y, int Width, int Height) bounds,
        Func<string, (int Width, int Height)> measure,
        Random random,
        IHapticLimb? haptics = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentNullException.ThrowIfNull(random);
        _presentation = presentation;
        _measure = measure;
        _random = random;
        _haptics = haptics;
        Bounds = bounds;

        _text = SelectText();
        (_width, _height) = _measure(_text);

        // WPF's start: a random position that keeps the whole word inside, and a random signed
        // velocity per axis (:158-168).
        _x = bounds.X + (_random.NextDouble() * Math.Max(1, bounds.Width - _width));
        _y = bounds.Y + (_random.NextDouble() * Math.Max(1, bounds.Height - _height));

        var speed = _presentation.SpeedSetting / 10.0;
        var baseSpeed = (3.0 + (_random.NextDouble() * 2.0)) * 60.0;
        _velocityX = baseSpeed * speed * (_random.Next(2) == 0 ? 1 : -1);
        _velocityY = baseSpeed * speed * (_random.Next(2) == 0 ? 1 : -1);

        _colour = NextColour(rerolling: false);
    }

    /// <summary>The field the logo bounces inside, in physical pixels.</summary>
    public (int X, int Y, int Width, int Height) Bounds { get; }

    /// <summary>How many wall bounces have happened. WPF awards XP off this event; the port does
    /// not, and counts it so a fact can pin the bounce without a screen.</summary>
    public int Bounces { get; private set; }

    /// <summary>How many of those were corner hits — WPF's own distinction, which drives its bark
    /// egg and its achievement (<c>:472-486</c>).</summary>
    public int CornerHits { get; private set; }

    /// <summary>True after the word or the colour last changed, so the presenter knows a re-raster
    /// is needed rather than only a move. This is the whole reason a move and a paint are separate
    /// operations on the surface.</summary>
    public bool NeedsRaster { get; private set; } = true;

    /// <summary>The logo as it stands, rounded to whole pixels — which is the space the surface's
    /// rectangle lives in.</summary>
    public BouncingLogo Logo => new(
        _text, Math.Round(_x), Math.Round(_y), _width, _height, _colour);

    /// <summary>The surface rectangle this logo needs right now.</summary>
    public (int X, int Y, int Width, int Height) Rectangle =>
        ((int)Math.Round(_x), (int)Math.Round(_y), _width, _height);

    /// <summary>Called by the presenter once it has re-rastered.</summary>
    public void RasterTaken() => NeedsRaster = false;

    /// <summary>
    /// Advance by <paramref name="elapsedSeconds"/>.
    ///
    /// <para>WPF's own frame guard, in its own order (<c>:352-368</c>): the FIRST call establishes
    /// the baseline and moves nothing, a non-positive delta is dropped, and a delta over
    /// <see cref="MaxStepSeconds"/> is clamped to it. Returns true when anything moved.</para>
    /// </summary>
    public bool Advance(double elapsedSeconds)
    {
        if (!_started)
        {
            _started = true;
            return false;
        }

        if (double.IsNaN(elapsedSeconds) || elapsedSeconds <= 0)
        {
            return false;
        }

        var step = Math.Min(elapsedSeconds, MaxStepSeconds);

        _x += _velocityX * step;
        _y += _velocityY * step;

        var minX = Bounds.X;
        var minY = Bounds.Y;
        var maxX = Bounds.X + Bounds.Width;
        var maxY = Bounds.Y + Bounds.Height;

        var bouncedX = false;
        var bouncedY = false;

        // WPF snaps the edge back to the wall BEFORE flipping the velocity (:414-431), which is what
        // stops a fast logo from accumulating overshoot and drifting out of the field.
        if (_x <= minX)
        {
            _x = minX;
            _velocityX = Math.Abs(_velocityX);
            bouncedX = true;
        }
        else if (_x + _width >= maxX)
        {
            _x = maxX - _width;
            _velocityX = -Math.Abs(_velocityX);
            bouncedX = true;
        }

        if (_y <= minY)
        {
            _y = minY;
            _velocityY = Math.Abs(_velocityY);
            bouncedY = true;
        }
        else if (_y + _height >= maxY)
        {
            _y = maxY - _height;
            _velocityY = -Math.Abs(_velocityY);
            bouncedY = true;
        }

        var bounced = bouncedX || bouncedY;
        if (!bounced)
        {
            return true;
        }

        Bounces++;

        // SP-126, census site 18. WPF fires BouncingTextBounceAsync() at
        // Services/Subliminal/BouncingTextService.cs:516, between the bounce bookkeeping and the
        // 10 % text re-roll at :519 — and this call sits in exactly that place in exactly that
        // sequence, above the re-roll below. One 60 ms request at priority 0, which the on-time
        // floor widens to a 130 ms on-time inside a 158 ms envelope (HapticPatterns.cs:36-65).
        _haptics?.BounceHit();

        // WPF's corner rule: both axes in one step is a corner, and a single-axis bounce counts too
        // when the word is within the tolerance of one (:459-470).
        if ((bouncedX && bouncedY) || IsNearCorner(minX, minY, maxX, maxY))
        {
            CornerHits++;
        }

        _colour = NextColour(rerolling: true);
        NeedsRaster = true;

        if (_random.NextDouble() < TextChangeChanceOnBounce)
        {
            _text = SelectText();
            (_width, _height) = _measure(_text);
        }

        return true;
    }

    /// <summary>WPF's <c>IsNearCorner</c> (<c>:609-622</c>), verbatim in effect.</summary>
    private bool IsNearCorner(int minX, int minY, int maxX, int maxY)
    {
        var left = _x;
        var top = _y;
        var right = _x + _width;
        var bottom = _y + _height;

        var nearTopLeft = left <= minX + CornerTolerance && top <= minY + CornerTolerance;
        var nearTopRight = right >= maxX - CornerTolerance && top <= minY + CornerTolerance;
        var nearBottomLeft = left <= minX + CornerTolerance && bottom >= maxY - CornerTolerance;
        var nearBottomRight = right >= maxX - CornerTolerance && bottom >= maxY - CornerTolerance;

        return nearTopLeft || nearTopRight || nearBottomLeft || nearBottomRight;
    }

    /// <summary>WPF's <c>SelectRandomText</c> (<c>:261-274</c>): a uniform pick from the ENABLED
    /// entries, and its own fallback word when nothing is enabled.</summary>
    private string SelectText()
    {
        var enabled = _presentation.EnabledPhrases;
        return enabled.Count == 0 ? FallbackText : enabled[_random.Next(enabled.Count)];
    }

    /// <summary>WPF's <c>NextColor</c> (<c>:633-646</c>). The rainbow wheel advances only on a
    /// re-roll, so a session's first colour is the wheel position it started at.</summary>
    private uint NextColour(bool rerolling)
    {
        switch (_presentation.ColourMode)
        {
            case BouncingTextColourMode.Fixed:
                return _presentation.FixedColour ?? HotPink;

            case BouncingTextColourMode.Rainbow:
                if (rerolling)
                {
                    _hueIndex = (_hueIndex + 1) % RainbowWheel.Length;
                }
                else
                {
                    _hueIndex = _random.Next(RainbowWheel.Length);
                }

                return RainbowWheel[_hueIndex];

            default:
                return RandomColours[_random.Next(RandomColours.Length)];
        }
    }

    /// <summary>WPF's <c>BuildRainbowWheel</c> (<c>:693-702</c>): twelve evenly spaced hues at full
    /// saturation and value.</summary>
    private static uint[] BuildRainbowWheel()
    {
        var wheel = new uint[12];
        for (var i = 0; i < wheel.Length; i++)
        {
            wheel[i] = HsvToColourRef(i * 360.0 / wheel.Length);
        }

        return wheel;
    }

    /// <summary>Full-saturation, full-value HSV to <c>COLORREF</c>. The reduced form of WPF's
    /// <c>HsvToRgb</c> for <c>s = v = 1</c>.</summary>
    private static uint HsvToColourRef(double hue)
    {
        var sector = (int)Math.Floor(hue / 60.0) % 6;
        var fraction = (hue / 60.0) - Math.Floor(hue / 60.0);
        var rising = (byte)Math.Round(fraction * 255.0);
        var falling = (byte)Math.Round((1.0 - fraction) * 255.0);

        var (r, g, b) = sector switch
        {
            0 => ((byte)255, rising, (byte)0),
            1 => (falling, (byte)255, (byte)0),
            2 => ((byte)0, (byte)255, rising),
            3 => ((byte)0, falling, (byte)255),
            4 => (rising, (byte)0, (byte)255),
            _ => ((byte)255, (byte)0, falling),
        };

        return (uint)((b << 16) | (g << 8) | r);
    }
}
