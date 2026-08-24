using CcpClient.Desktop.Video;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// One bubble the user is meant to count: where it is in the picture, how big, when it appeared and
/// how long it lives. Fractions rather than pixels, so one run is independent of the clip's size.
/// </summary>
/// <param name="CentreX">Horizontal centre as a fraction of the picture's width.</param>
/// <param name="CentreY">Vertical centre as a fraction of the picture's height.</param>
/// <param name="DiameterFraction">Diameter as a fraction of the picture's WIDTH (never of its
/// height: a bubble must stay round on a letterboxed picture of any aspect).</param>
/// <param name="BornAt">When it appeared, on the clip's own elapsed time.</param>
/// <param name="Lifetime">How long it stays up before it pops.</param>
public readonly record struct CountedBubble(
    double CentreX, double CentreY, double DiameterFraction, TimeSpan BornAt, TimeSpan Lifetime)
{
    /// <summary>When the pop animation starts.</summary>
    public TimeSpan PopsAt => BornAt + Lifetime;

    /// <summary>When there is nothing left to draw.</summary>
    public TimeSpan GoneAt => PopsAt + BubbleCountRun.PopDuration;
}

/// <summary>
/// The counting game's arithmetic: how many bubbles a clip carries and how often one appears.
/// Upstream's, verbatim, kept pure so every number is pinnable with no clip, no window and no
/// decoder.
/// </summary>
public static class BubbleCountArithmetic
{
    /// <summary>Bubbles per THIRTY seconds of clip, per difficulty
    /// (<c>Windows/BubbleCountWindow.xaml.cs:1139-1145</c>).</summary>
    public static double BaseRate(BubbleCountDifficulty difficulty) => difficulty switch
    {
        BubbleCountDifficulty.Easy => 3,
        BubbleCountDifficulty.Medium => 5,
        BubbleCountDifficulty.Hard => 8,

        // Upstream's own fall-through arm is 5, not a throw (<c>:1144</c>).
        _ => 5,
    };

    /// <summary>The window the base rate is denominated in (<c>:1147</c>, <c>/ 30.0</c>).</summary>
    public const double RateWindowSeconds = 30.0;

    /// <summary>±20 % on the scaled count (<c>:1148</c>).</summary>
    public const double TargetJitterFraction = 0.2;

    /// <summary>Upstream's floor (<c>:1150</c>, <c>Math.Max(3, …)</c>). Three bubbles is the least
    /// this game will ever ask about.</summary>
    public const int MinimumTarget = 3;

    /// <summary>The spawn timer runs at 70 % of the even spacing (<c>:1201</c>), which is what makes
    /// the 0.7-probability tick below produce roughly the target over the clip.</summary>
    public const double SpawnIntervalFactor = 0.7;

    /// <summary>How long spawning waits for the picture to be up (<c>:1220</c>,
    /// <c>Task.Delay(1500)</c>).</summary>
    public static readonly TimeSpan SpawnLeadIn = TimeSpan.FromMilliseconds(1500);

    /// <summary>The chance a tick spawns once at least half the target is out (<c>:1211</c>).</summary>
    public const double SpawnChance = 0.7;

    /// <summary>Upstream's fallback clip length when the container reports none
    /// (<c>Windows/BubbleCountWindow.xaml.cs:98</c>, <c>FallbackDurationSeconds = 30</c>). Upstream
    /// replaces it from LibVLC's <c>LengthChanged</c> within a second; this port asks Media
    /// Foundation once, at open, and a container that reports no duration keeps this number.</summary>
    public static readonly TimeSpan FallbackDuration = TimeSpan.FromSeconds(30);

    /// <summary>Upstream's safety end: the clip's own length plus five seconds
    /// (<c>:1180</c>, <c>timeoutSeconds = videoDurationSeconds + 5</c>). In this port it is passed to
    /// the surface as its max-length cap, so the same number ends the same overrun.</summary>
    public static readonly TimeSpan SafetyMargin = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How many bubbles this clip carries (<c>:1137-1151</c>):
    /// <c>max(3, round(baseRate/30 * seconds ± 20 %))</c>.
    /// </summary>
    /// <param name="roll">A uniform draw in [0,1) — upstream's <c>_random.NextDouble()</c>.</param>
    public static int Target(BubbleCountDifficulty difficulty, TimeSpan duration, double roll)
    {
        var seconds = duration > TimeSpan.Zero ? duration.TotalSeconds : FallbackDuration.TotalSeconds;
        var scaled = BaseRate(difficulty) / RateWindowSeconds * seconds;
        var variance = scaled * TargetJitterFraction;
        var target = (int)Math.Round(scaled + ((roll * variance * 2) - variance), MidpointRounding.AwayFromZero);
        return Math.Max(MinimumTarget, target);
    }

    /// <summary>
    /// How often the spawn tick comes round (<c>:1201</c>): the even spacing over the clip, times
    /// 0.7. Floored at one millisecond so a degenerate duration cannot produce a zero interval and a
    /// spin.
    /// </summary>
    public static TimeSpan SpawnInterval(TimeSpan duration, int target)
    {
        var seconds = duration > TimeSpan.Zero ? duration.TotalSeconds : FallbackDuration.TotalSeconds;
        var even = seconds * 1000.0 / Math.Max(1, target);
        return TimeSpan.FromMilliseconds(Math.Max(1.0, even * SpawnIntervalFactor));
    }

    /// <summary>
    /// Does this tick spawn? Upstream (<c>:1211</c>):
    /// <c>roll &lt; 0.7 || shown &lt; target / 2</c> — INTEGER division, upstream's own, so at a
    /// target of 3 the guaranteed-spawn window is the first bubble only.
    /// </summary>
    public static bool TickSpawns(int shown, int target, double roll) =>
        roll < SpawnChance || shown < target / 2;
}

/// <summary>
/// One playthrough of the counting game: it decides where the bubbles are, and it DRAWS them onto
/// the clip's own pictures as the video capability hands them over.
///
/// <para><b>The mechanism differs from upstream's and the outcome does not, which is the licence
/// this port runs on.</b> Upstream spawns one transparent, topmost, click-through WINDOW per bubble
/// over a fullscreen video window (<c>Windows/BubbleCountWindow.xaml.cs:1269-1290</c>,
/// <c>CountBubble</c> at <c>:1641</c>). This port composes the bubble INTO the decoded picture
/// before the picture is handed to the video capability. The user sees the same thing — bubbles
/// appear over the clip, hold for a moment and pop — and the port gains something upstream cannot
/// have: <b>a bubble in the picture is a bubble the video capability's own read-back proves the
/// operating system is holding</b>, at no new evidence cost and with no second capability.</para>
///
/// <para><b>The shape is upstream's own no-asset fallback bubble</b> — a radial pink disc with a
/// white rim (<c>:1688-1698</c>) — rather than its <c>bubble.png</c>, because a module that needed
/// an asset to be countable would be a module that silently stops counting when the asset is
/// missing.</para>
///
/// <para><b>The pop SOUND is ported</b> and is raised through the <c>onPop</c> callback — see
/// <see cref="Paint"/> for the once-per-bubble rule and the thread it arrives on. <b>Not ported:</b>
/// the per-bubble rotation, and the multi-monitor spread that puts each bubble on one random screen
/// (<c>:1259-1267</c>) — the port has one surface on the primary display (D66/D123).</para>
///
/// <para><b>Threading.</b> Every <see cref="Paint"/> call arrives on the surface thread, inline with
/// the frame advance, in order. The counters are read from the module's thread when the clip ends,
/// which is the same thread.</para>
/// </summary>
public sealed class BubbleCountRun : IVideoFramePainter
{
    /// <summary>How long a bubble takes to reach full size. Upstream grows it from 0.1 to 1.0 at
    /// +0.1 per 30 ms animation tick (<c>:1648</c>, <c>:1767-1770</c>, <c>:49</c>) — nine ticks.</summary>
    public static readonly TimeSpan GrowDuration = TimeSpan.FromMilliseconds(270);

    /// <summary>How long the pop takes. Upstream fades opacity by 0.12 per 30 ms tick
    /// (<c>:1755-1762</c>) — nine ticks again — while scaling by +0.08 a tick.</summary>
    public static readonly TimeSpan PopDuration = TimeSpan.FromMilliseconds(270);

    /// <summary>How much bigger a bubble gets while it pops: +0.08 per tick over nine ticks
    /// (<c>:1756</c>).</summary>
    public const double PopScaleGrowth = 0.72;

    /// <summary>The scale a bubble starts at (<c>:1648</c>).</summary>
    public const double BirthScale = 0.1;

    /// <summary>The shortest a bubble stays up before it pops (<c>:1734</c>,
    /// <c>1000 + random.Next(500)</c>).</summary>
    public static readonly TimeSpan MinLifetime = TimeSpan.FromMilliseconds(1000);

    /// <summary>The span above <see cref="MinLifetime"/> (<c>:1734</c>).</summary>
    public static readonly TimeSpan LifetimeSpan = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The smallest bubble, as a fraction of the picture's width. Upstream draws 120..225 PIXELS
    /// (<c>:1254</c>, <c>_random.Next(120, 225)</c>) on a window covering a whole screen, so the
    /// fractions here are those pixel sizes over a 1920-wide screen — the size the user sees is
    /// upstream's, and it survives a picture of any resolution, which a pixel constant would not.
    /// </summary>
    public const double MinDiameterFraction = 120.0 / 1920.0;

    /// <summary>See <see cref="MinDiameterFraction"/>: 225 px over 1920.</summary>
    public const double MaxDiameterFraction = 225.0 / 1920.0;

    /// <summary>Horizontal placement: <c>roll * 0.7 + 0.15</c> (<c>:1252</c>).</summary>
    public const double PlacementXSpan = 0.7;

    /// <summary>See <see cref="PlacementXSpan"/> (<c>:1252</c>).</summary>
    public const double PlacementXOffset = 0.15;

    /// <summary>Vertical placement: <c>roll * 0.5 + 0.25</c> (<c>:1253</c>).</summary>
    public const double PlacementYSpan = 0.5;

    /// <summary>See <see cref="PlacementYSpan"/> (<c>:1253</c>).</summary>
    public const double PlacementYOffset = 0.25;

    /// <summary>The disc's centre colour, upstream's radial-gradient inner stop
    /// (<c>:1692</c>, <c>Color.FromArgb(200, 255, 182, 193)</c>).</summary>
    public const double CentreAlpha = 200.0 / 255.0;

    /// <summary>Upstream's outer stop (<c>:1693</c>, <c>Color.FromArgb(100, 255, 105, 180)</c>).</summary>
    public const double EdgeAlpha = 100.0 / 255.0;

    private static readonly (byte B, byte G, byte R) CentreColour = (193, 182, 255);
    private static readonly (byte B, byte G, byte R) EdgeColour = (180, 105, 255);

    private readonly List<CountedBubble> _bubbles = [];

    /// <summary>Whether each bubble in <see cref="_bubbles"/> has already sounded its pop. Parallel
    /// to that list, which is append-only, so the two never fall out of step.</summary>
    private readonly List<bool> _sounded = [];

    private readonly Random _random;
    private readonly double _targetRoll;
    private readonly Action? _onPop;
    private TimeSpan _nextTickAt = BubbleCountArithmetic.SpawnLeadIn;

    /// <param name="difficulty">The dial, which decides the base rate.</param>
    /// <param name="random">Injected, so a fact pins the arithmetic rather than re-deriving it.</param>
    /// <param name="onPop">Raised once per bubble, at the moment it starts popping — see
    /// <see cref="Paint"/>. Null in a composition with no app audio.</param>
    public BubbleCountRun(BubbleCountDifficulty difficulty, Random random, Action? onPop = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
        _onPop = onPop;
        Difficulty = difficulty;

        // ONE roll for the target's ±20 %, drawn here and reused if the clip's real length arrives
        // later. Re-rolling on Opening would make the same run's target depend on how many times it
        // was recomputed, which is the kind of hidden non-determinism a seeded fact cannot see.
        _targetRoll = random.NextDouble();

        // Until the capability opens the clip and says otherwise, the arithmetic runs on upstream's
        // own fallback length — the same number upstream starts every game with when its metadata
        // cache has never seen the file (BubbleCountWindow.xaml.cs:98, :703-712).
        Recompute(BubbleCountArithmetic.FallbackDuration);
    }

    /// <summary>The dial this run was built with.</summary>
    public BubbleCountDifficulty Difficulty { get; }

    /// <summary>The clip length this run's arithmetic used, as the OS reported it — or upstream's
    /// 30 s fallback where it reported none.</summary>
    public TimeSpan Duration { get; private set; }

    /// <summary>How many bubbles this clip WILL carry if it runs to its end. Never the answer the
    /// user is asked about — see <see cref="BubblesShown"/>.</summary>
    public int Target { get; private set; }

    /// <summary>How often the spawn tick comes round.</summary>
    public TimeSpan SpawnInterval { get; private set; }

    /// <inheritdoc/>
    public void Opening(VideoClipInfo clip)
    {
        // Upstream re-derives the target the moment LibVLC reports the real length, for the same
        // reason (Windows/BubbleCountWindow.xaml.cs:719-736, AdoptRealDuration: "Re-derives the
        // bubble target, re-spaces the spawn timer"). Nothing has been painted yet, so re-deriving
        // here cannot change a bubble that is already on screen.
        Recompute(clip.Duration);
    }

    /// <summary>
    /// <b>The number the user is asked for.</b> How many bubbles have really been drawn into a
    /// picture handed to the operating system — not <see cref="Target"/>, which is what the clip
    /// would have carried had it run to its end. A clip that ends early, is capped, or whose surface
    /// stops holding the picture leaves these two different, and asking about the one the user could
    /// not have seen is the failure this distinction exists to prevent.
    /// </summary>
    public int BubblesShown => _bubbles.Count;

    /// <summary>How many pictures this run has painted on.</summary>
    public int PicturesPainted { get; private set; }

    /// <summary>The bubbles so far, for a fact that wants to see where they were.</summary>
    public IReadOnlyList<CountedBubble> Bubbles => _bubbles;

    /// <summary>
    /// Draw this run's bubbles onto one picture, and sound the ones that started popping on it.
    ///
    /// <para><b>The pop sound, once per bubble.</b> Upstream's bubble sounds from
    /// <c>StartPopping</c>, whose first line is <c>if (_isPopping || _isDisposed) return;</c>
    /// (<c>Windows/BubbleCountWindow.xaml.cs:1795-1797</c>) — so the clip is tied to the TRANSITION
    /// into popping and a bubble makes exactly one, however many animation ticks it spends popping.
    /// Here the transition is <c>elapsed &gt;= PopsAt</c> observed for the first time, and
    /// <see cref="_sounded"/> is what makes it once. It is deliberately NOT filtered by
    /// <c>GoneAt</c>: if the pictures arrive far enough apart that a bubble's whole pop falls
    /// between two of them, upstream's own 30 ms animation timer would still have popped it and
    /// still have made the noise — the timer does not wait for a frame.</para>
    ///
    /// <para><b>Threading.</b> Every call arrives on the surface thread, inline with the frame
    /// advance, so the callback does too — and <see cref="EffectSounds.Pop"/> hands the play off
    /// that thread for exactly the reason upstream's own line gives ("Run everything off UI thread
    /// to avoid blocking LibVLC rendering", <c>:1331</c>): the caller here is a frame painter.</para>
    /// </summary>
    public void Paint(VideoFrame frame, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(frame);

        // Spawn first, so a bubble that comes due on this beat is drawn on this picture rather than
        // on the next one. Upstream's tick does the same thing for the same reason: it spawns into
        // the window that is already on screen.
        while (elapsed >= _nextTickAt && _bubbles.Count < Target)
        {
            var isLeadInTick = _nextTickAt == BubbleCountArithmetic.SpawnLeadIn;

            // Upstream spawns ONCE unconditionally when the timer starts (:1231-1233 calls
            // SpawnBubbleOnAllWindows outside the probability branch) and applies the 0.7 rule to
            // every tick after it (:1211).
            if (isLeadInTick
                || BubbleCountArithmetic.TickSpawns(_bubbles.Count, Target, _random.NextDouble()))
            {
                _bubbles.Add(new CountedBubble(
                    (_random.NextDouble() * PlacementXSpan) + PlacementXOffset,
                    (_random.NextDouble() * PlacementYSpan) + PlacementYOffset,
                    MinDiameterFraction
                        + (_random.NextDouble() * (MaxDiameterFraction - MinDiameterFraction)),
                    _nextTickAt,
                    MinLifetime + (LifetimeSpan * _random.NextDouble())));
                _sounded.Add(false);
            }

            _nextTickAt += SpawnInterval;
        }

        PicturesPainted++;
        for (var i = 0; i < _bubbles.Count; i++)
        {
            var bubble = _bubbles[i];
            if (elapsed >= bubble.PopsAt && !_sounded[i])
            {
                _sounded[i] = true;
                _onPop?.Invoke();
            }

            if (elapsed < bubble.BornAt || elapsed >= bubble.GoneAt)
            {
                continue;
            }

            Draw(frame, bubble, elapsed);
        }
    }

    /// <summary>
    /// The scale and opacity of one bubble at a moment: grow in, hold, then pop outward while it
    /// fades. Upstream's three phases (<c>:1745-1775</c>), expressed as time rather than as animation
    /// ticks so the picture looks the same whatever the clip's frame rate is.
    /// </summary>
    public static (double Scale, double Opacity) Animation(CountedBubble bubble, TimeSpan elapsed)
    {
        if (elapsed >= bubble.PopsAt)
        {
            var popped = Math.Clamp(
                (elapsed - bubble.PopsAt) / PopDuration, 0.0, 1.0);
            return (1.0 + (PopScaleGrowth * popped), 1.0 - popped);
        }

        var grown = GrowDuration > TimeSpan.Zero
            ? Math.Clamp((elapsed - bubble.BornAt) / GrowDuration, 0.0, 1.0)
            : 1.0;
        return (BirthScale + ((1.0 - BirthScale) * grown), 1.0);
    }

    /// <summary>
    /// One bubble onto one picture: a radial pink disc with a white rim, alpha-blended into the
    /// frame's own BGRX bytes. Upstream's brush (<c>:1691-1696</c>) in the port's pixel format.
    /// </summary>
    private static void Draw(VideoFrame frame, CountedBubble bubble, TimeSpan elapsed)
    {
        var (scale, opacity) = Animation(bubble, elapsed);
        if (opacity <= 0)
        {
            return;
        }

        var radius = bubble.DiameterFraction * frame.Width * scale / 2.0;
        if (radius < 1.0)
        {
            return;
        }

        var centreX = bubble.CentreX * frame.Width;
        var centreY = bubble.CentreY * frame.Height;
        var rim = Math.Max(1.0, radius * 0.06);
        var left = Math.Max(0, (int)Math.Floor(centreX - radius));
        var right = Math.Min(frame.Width - 1, (int)Math.Ceiling(centreX + radius));
        var top = Math.Max(0, (int)Math.Floor(centreY - radius));
        var bottom = Math.Min(frame.Height - 1, (int)Math.Ceiling(centreY + radius));
        var pixels = frame.Pixels;

        for (var y = top; y <= bottom; y++)
        {
            var dy = y + 0.5 - centreY;
            var row = y * frame.Stride;
            for (var x = left; x <= right; x++)
            {
                var dx = x + 0.5 - centreX;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));
                if (distance > radius)
                {
                    continue;
                }

                var t = radius > 0 ? distance / radius : 0.0;
                double alpha;
                double blue;
                double green;
                double red;
                if (distance >= radius - rim)
                {
                    // Upstream's white pen around the disc (:1694).
                    alpha = opacity;
                    blue = 255;
                    green = 255;
                    red = 255;
                }
                else
                {
                    alpha = (CentreAlpha + ((EdgeAlpha - CentreAlpha) * t)) * opacity;
                    blue = CentreColour.B + ((EdgeColour.B - CentreColour.B) * t);
                    green = CentreColour.G + ((EdgeColour.G - CentreColour.G) * t);
                    red = CentreColour.R + ((EdgeColour.R - CentreColour.R) * t);
                }

                var offset = row + (x * VideoFrame.BytesPerPixel);
                pixels[offset] = Blend(pixels[offset], blue, alpha);
                pixels[offset + 1] = Blend(pixels[offset + 1], green, alpha);
                pixels[offset + 2] = Blend(pixels[offset + 2], red, alpha);
            }
        }
    }

    private static byte Blend(byte under, double over, double alpha) =>
        (byte)Math.Clamp(Math.Round((over * alpha) + (under * (1.0 - alpha))), 0, 255);

    /// <summary>
    /// Derive the target and the spacing from a clip length. Called with upstream's fallback at
    /// construction and again with the operating system's own answer when the clip opens — and NEVER
    /// after a bubble has been spawned, so the lead-in tick cannot move under a run in progress.
    /// </summary>
    private void Recompute(TimeSpan duration)
    {
        Duration = duration > TimeSpan.Zero ? duration : BubbleCountArithmetic.FallbackDuration;
        Target = BubbleCountArithmetic.Target(Difficulty, Duration, _targetRoll);
        SpawnInterval = BubbleCountArithmetic.SpawnInterval(Duration, Target);
    }
}
