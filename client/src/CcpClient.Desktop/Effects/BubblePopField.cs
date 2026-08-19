namespace CcpClient.Desktop.Effects;

/// <summary>
/// One bubble's state, as arithmetic. No window, no OS, no clock — a value the presenter turns into
/// a pointer target and back.
/// </summary>
/// <param name="Id">The field's own identity for this bubble, monotonic within a field.</param>
/// <param name="X">Left edge, in physical pixels on the virtual desktop.</param>
/// <param name="Y">Top edge, same space.</param>
/// <param name="Size">The side of the bubble's square, in physical pixels.</param>
/// <param name="Speed">Upward travel per logical step, in pixels
/// (<c>Services/BubbleService.cs:2823</c>: <c>1.0 + random × 1.0</c>, then the speed boost).</param>
/// <param name="AnimType">Which of upstream's four horizontal wobbles this bubble drifts on
/// (<c>:2836</c>, applied at <c>:3460-3463</c>).</param>
/// <param name="WobbleOffset">Unused by the wobble itself upstream (it feeds the breathe phase at
/// <c>:3644</c>) and kept here so a field can be reproduced exactly from its seed.</param>
/// <param name="StartX">The x the wobble oscillates around — upstream re-bases <c>_posX</c> from
/// <c>_startX</c> every step rather than integrating it (<c>:3496</c>).</param>
/// <param name="TimeAlive">Upstream's own <c>_timeAlive</c>, advanced by <c>0.02</c> per step
/// (<c>:3399</c>).</param>
/// <param name="Popping">True once the bubble has been hit and is playing its pop.</param>
/// <param name="FadeAlpha">The pop animation's remaining opacity; the bubble is destroyed when it
/// reaches zero (<c>:3225-3231</c>, <c>:3259</c>).</param>
public readonly record struct BubblePopBubble(
    int Id,
    double X,
    double Y,
    int Size,
    double Speed,
    int AnimType,
    double WobbleOffset,
    double StartX,
    double TimeAlive,
    bool Popping,
    double FadeAlpha)
{
    /// <summary>The bubble's square, rounded to whole pixels — what a pointer target is placed at.</summary>
    public (int X, int Y, int Size) Rect => ((int)Math.Round(X), (int)Math.Round(Y), Size);

    /// <summary>Half the side. The bound in <see cref="BubblePopField.MaxStepDisplacement"/> is
    /// compared against the smallest value this can take.</summary>
    public double Radius => Size / 2.0;
}

/// <summary>Why a bubble left the field.</summary>
public enum BubblePopExit
{
    /// <summary>The user hit it and the pop animation finished.</summary>
    Popped,

    /// <summary>It floated off the top of the play area unhit — upstream's <c>OnMiss</c>
    /// (<c>Services/BubbleService.cs:1194-1200</c>).</summary>
    Missed,
}

/// <summary>
/// <b>Bubble Pop's arithmetic</b>: where bubbles are, how they move, when they spawn, and what a hit
/// does to one. Deliberately free of every OS concern, so the numbers can be pinned without a
/// desktop — the same split <see cref="BubbleCountGame"/> and <see cref="LockCardTyping"/> already
/// use.
///
/// <para><b>Every constant here is upstream's, cited at its own line.</b> The port's divergence is
/// the MECHANISM by which a bubble becomes clickable (one non-activating native window per bubble,
/// the arbiter being the window manager rather than a user-space disc snapshot), never the numbers.</para>
///
/// <para><b>The one number this class exists to make checkable</b> is
/// <see cref="MaxStepDisplacement"/>. SP-110 predicted this packet's central race: a hit test's
/// answer is a function of a position that changes between asking and clicking. The port removes
/// that race from the product — nothing hit-tests and then acts on the answer — and what remains is
/// that a caller's belief about routing can be one step old. That residue is bounded by arithmetic
/// over these constants, and <see cref="OneStepNeverCarriesABubbleOffItsOwnCentre"/> is the
/// inequality itself, so a later change to the speed band, the boost ceiling or the size floor that
/// broke the bound would redden rather than quietly widen the race.</para>
/// </summary>
public sealed class BubblePopField
{
    /// <summary>The logical animation step. Upstream's <c>STEP_MS = 30.0</c>
    /// (<c>Services/BubbleService.cs:54</c>), gated off the composition clock so a busy frame is
    /// DROPPED rather than replayed as a backlog (<c>:44-52</c>).</summary>
    public static readonly TimeSpan StepInterval = TimeSpan.FromMilliseconds(30);

    /// <summary>Slowest upward travel, pixels per step (<c>Services/BubbleService.cs:2823</c>).</summary>
    public const double MinSpeed = 1.0;

    /// <summary>Fastest before the boost, pixels per step (<c>:2823</c>: <c>1.0 + rand × 1.0</c>).</summary>
    public const double MaxSpeed = 2.0;

    /// <summary>The dashboard's travel-speed boost, in percent. Upstream clamps 0..500 and
    /// multiplies by <c>1 + boost/100</c> (<c>:2831-2834</c>), so the ceiling is 6x.</summary>
    public const int MinSpeedBoostPercent = 0;

    /// <summary>See <see cref="MinSpeedBoostPercent"/>.</summary>
    public const int MaxSpeedBoostPercent = 500;

    /// <summary>Smallest bubble the field will draw, in pixels. Upstream's
    /// <c>BubbleSizing.ClickableFloorDip = 60</c> and its own reason: <i>"a bubble is a MOVING click
    /// target, and below roughly this it stops being fair to ask someone to hit it"</i>
    /// (<c>Services/BubbleSizing.cs:70</c>).</summary>
    public const int MinSize = 60;

    /// <summary>Bottom of upstream's untouched size band (<c>Services/BubbleSizing.cs:40</c>).</summary>
    public const int BaseMinSize = 150;

    /// <summary>EXCLUSIVE top of it, named to match the <c>Random.Next(min, max)</c> it is fed to
    /// (<c>Services/BubbleSizing.cs:47</c>).</summary>
    public const int BaseMaxSize = 250;

    /// <summary>Ceiling on the drawn size (<c>Services/BubbleSizing.cs:81</c>).</summary>
    public const int MaxSize = 500;

    /// <summary>The user's size dial, in percent (<c>Services/BubbleSizing.cs:50,57,60</c>).</summary>
    public const int MinSizePercent = 50;

    /// <summary>See <see cref="MinSizePercent"/>.</summary>
    public const int MaxSizePercent = 150;

    /// <summary>See <see cref="MinSizePercent"/>.</summary>
    public const int DefaultSizePercent = 100;

    /// <summary>Spawns a minute at the bottom of the dial (<c>CCP.Core/Models/AppSettings.cs:2743-2747</c>, clamped
    /// 1..60; the spawn timer is <c>60000 / frequency</c> ms at
    /// <c>Services/BubbleService.cs:188</c>).</summary>
    public const int MinPerMinute = 1;

    /// <summary>Spawns a minute at the top of the dial (<c>CCP.Core/Models/AppSettings.cs:2743-2747</c>).</summary>
    public const int MaxPerMinute = 60;

    /// <summary>Upstream's default frequency (<c>CCP.Core/Models/AppSettings.cs:2743-2747</c>).</summary>
    public const int DefaultPerMinute = 5;

    /// <summary>
    /// Concurrent bubbles. <b>Upstream's PER-WINDOW cap, taken with the mechanism</b>:
    /// <c>MAX_BUBBLES = 3</c>, commented <i>"per-window fallback cap (SetWindowPos-bound — keep
    /// small)"</i> (<c>Services/BubbleService.cs:26</c>). Upstream's shared-host cap of 40 (<c>:27</c>)
    /// belongs to the render path this port did not take, and taking the number without the path
    /// would be forty <c>SetWindowPos</c> calls per 30 ms step.
    /// </summary>
    public const int MaxConcurrent = 3;

    /// <summary>Pop animation growth per step (<c>Services/BubbleService.cs:3228</c>).</summary>
    public const double PopScaleGrowth = 0.04;

    /// <summary>Pop animation fade per step for an ordinary (non-lucky) pop
    /// (<c>Services/BubbleService.cs:3229</c>). Lucky pops fade at 0.044 and are not ported: the
    /// lucky roll is the skill tree's (<c>:944</c>), a subsystem this port does not have.</summary>
    public const double PopFadePerStep = 0.066;

    /// <summary>Upstream's own vertical margin above the play area before a bubble counts as gone
    /// (<c>Services/BubbleService.cs:2847</c>: <c>_screenTop = area.Y/dpi - _size - 50</c>).</summary>
    public const int ExitMargin = 50;

    /// <summary>
    /// <b>The race bound.</b> The furthest a bubble can travel in one logical step, in pixels:
    /// vertical at the boosted ceiling, horizontal at the steepest wobble.
    ///
    /// <para>Vertical is <see cref="MaxSpeed"/> × 6 = 12 (<c>Services/BubbleService.cs:2823</c>,
    /// <c>:2831-2834</c>). Horizontal is the largest per-step derivative of upstream's four wobbles,
    /// which is case 1's <c>30 × sin(7.5 t)</c> at <c>30 × 7.5 × 0.02 = 4.5</c>
    /// (<c>:3460-3463</c> over <c>:3399</c>); case 3 gives <c>(30×3 + 15×6) × 0.02 = 3.6</c>, case 0
    /// <c>3.0</c>, case 2 <c>2.7</c>.</para>
    /// </summary>
    public static double MaxStepDisplacement =>
        Math.Sqrt(Math.Pow(MaxSpeed * (1 + (MaxSpeedBoostPercent / 100.0)), 2) + Math.Pow(MaxWobbleStep, 2));

    /// <summary>The steepest per-step horizontal travel any of upstream's four wobbles produces
    /// (<c>Services/BubbleService.cs:3460-3463</c>, over <c>_timeAlive += 0.02</c> at <c>:3399</c>).</summary>
    public const double MaxWobbleStep = 30 * 7.5 * TimeAlivePerStep;

    /// <summary>Upstream's own logical time increment per step (<c>Services/BubbleService.cs:3399</c>).</summary>
    public const double TimeAlivePerStep = 0.02;

    /// <summary>
    /// <b>The inequality the whole race argument rests on</b>: one step's worst-case displacement is
    /// strictly less than the smallest radius a legal bubble can have, so a point the OS answered
    /// with a bubble at step N is still inside that same bubble's window at step N+1.
    /// </summary>
    public static bool OneStepNeverCarriesABubbleOffItsOwnCentre => MaxStepDisplacement < MinSize / 2.0;

    private readonly List<BubblePopBubble> _bubbles = [];
    private readonly Random _random;
    private readonly int _playWidth;
    private readonly int _playHeight;
    private readonly int _playX;
    private readonly int _playY;

    private int _nextId = 1;

    /// <param name="playX">Left edge of the play area, physical pixels.</param>
    /// <param name="playY">Top edge of the play area.</param>
    /// <param name="playWidth">Width of the play area.</param>
    /// <param name="playHeight">Height of the play area.</param>
    /// <param name="random">The spawn source, injected so a field is reproducible.</param>
    public BubblePopField(int playX, int playY, int playWidth, int playHeight, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _playX = playX;
        _playY = playY;
        _playWidth = Math.Max(MinSize * 2, playWidth);
        _playHeight = Math.Max(MinSize * 2, playHeight);
        _random = random;
    }

    /// <summary>Every live bubble, in spawn order.</summary>
    public IReadOnlyList<BubblePopBubble> Bubbles => _bubbles;

    /// <summary>How many bubbles the user has popped in this field's life.</summary>
    public int Popped { get; private set; }

    /// <summary>How many floated away unhit.</summary>
    public int Missed { get; private set; }

    /// <summary>
    /// The spawn interval for a frequency, in the port's own words: WPF's
    /// <c>60000.0 / Math.Max(1, frequency)</c> (<c>Services/BubbleService.cs:188</c>) with the
    /// dial's own 1..60 clamp applied first (<c>CCP.Core/Models/AppSettings.cs:2743-2747</c>).
    /// </summary>
    public static TimeSpan SpawnInterval(int perMinute) =>
        TimeSpan.FromMilliseconds(60000.0 / Math.Clamp(perMinute, MinPerMinute, MaxPerMinute));

    /// <summary>
    /// The drawn size for one bubble. Upstream's <c>BubbleSizing.Scale</c>: a draw from the
    /// 150..250 band scaled by the user's percentage, then railed into 60..500
    /// (<c>Services/BubbleSizing.cs:40,47,70,81</c>, and its own remarks on why the factors multiply
    /// freely rather than being clamped as a product).
    /// </summary>
    public static int SizeFor(int baseDip, int userPercent) =>
        Math.Clamp(
            (int)Math.Round(baseDip * Math.Clamp(userPercent, MinSizePercent, MaxSizePercent) / 100.0),
            MinSize,
            MaxSize);

    /// <summary>
    /// Add one bubble at the bottom of the play area, or return null when the field is already at
    /// <see cref="MaxConcurrent"/>. Upstream's own refusal: <c>if (_bubbles.Count &gt;=
    /// MaxAmbientBubbles) return;</c> with a log and no spawn (<c>Services/BubbleService.cs:862-866</c>).
    /// </summary>
    public BubblePopBubble? Spawn(int userSizePercent, int speedBoostPercent)
    {
        if (_bubbles.Count >= MaxConcurrent)
        {
            return null;
        }

        var size = SizeFor(_random.Next(BaseMinSize, BaseMaxSize), userSizePercent);

        // Upstream's own horizontal spawn band, in its own shape:
        // _startX = area.X + random.Next(50, max(100, area.Width - _size - 50))  (:2852)
        var span = Math.Max(100, _playWidth - size - 50);
        var startX = _playX + _random.Next(50, span);

        var speed = MinSpeed + (_random.NextDouble() * (MaxSpeed - MinSpeed));
        var boost = Math.Clamp(speedBoostPercent, MinSpeedBoostPercent, MaxSpeedBoostPercent);
        if (boost > 0)
        {
            speed *= 1.0 + (boost / 100.0);
        }

        var bubble = new BubblePopBubble(
            Id: _nextId++,
            X: startX,
            Y: _playY + _playHeight,          // FloatUp starts at the bottom (:2876)
            Size: size,
            Speed: speed,
            AnimType: _random.Next(4),        // (:2836)
            WobbleOffset: _random.NextDouble() * 100,   // (:2837)
            StartX: startX,
            TimeAlive: 0,
            Popping: false,
            FadeAlpha: 1.0);

        _bubbles.Add(bubble);
        return bubble;
    }

    /// <summary>
    /// Advance the field one logical step and report every bubble that left it.
    ///
    /// <para>Upstream's own order: the pop animation is judged before the travel, and destruction is
    /// deferred to the animation completing rather than happening inside the click
    /// (<c>Services/BubbleService.cs:3222-3231</c> and gotcha 6 in the primer).</para>
    /// </summary>
    /// <returns>The bubbles that left this step, with why.</returns>
    public IReadOnlyList<(BubblePopBubble Bubble, BubblePopExit Exit)> Step()
    {
        var gone = new List<(BubblePopBubble, BubblePopExit)>();

        for (var i = _bubbles.Count - 1; i >= 0; i--)
        {
            var bubble = _bubbles[i];

            if (bubble.Popping)
            {
                var fade = bubble.FadeAlpha - PopFadePerStep;
                if (fade <= 0)
                {
                    _bubbles.RemoveAt(i);
                    Popped++;
                    gone.Add((bubble, BubblePopExit.Popped));
                    continue;
                }

                _bubbles[i] = bubble with { FadeAlpha = fade, TimeAlive = bubble.TimeAlive + TimeAlivePerStep };
                continue;
            }

            var timeAlive = bubble.TimeAlive + TimeAlivePerStep;
            var y = bubble.Y - bubble.Speed;
            var x = bubble.StartX + Wobble(bubble.AnimType, timeAlive);

            // _screenTop = area.Y - _size - ExitMargin (:2847); exit at _posY < _screenTop (:3497).
            if (y < _playY - bubble.Size - ExitMargin)
            {
                _bubbles.RemoveAt(i);
                Missed++;
                gone.Add((bubble, BubblePopExit.Missed));
                continue;
            }

            _bubbles[i] = bubble with { X = x, Y = y, TimeAlive = timeAlive };
        }

        return gone;
    }

    /// <summary>
    /// Upstream's four horizontal wobbles, verbatim (<c>Services/BubbleService.cs:3460-3463</c>).
    /// The <c>_angle</c> spin each case also carries is a visual this port does not draw and is not
    /// ported as an inert field.
    /// </summary>
    public static double Wobble(int animType, double timeAlive) => animType switch
    {
        0 => Math.Sin(timeAlive * 6) * 25,
        1 => Math.Sin(timeAlive * 7.5) * 30,
        2 => Math.Cos(timeAlive * 5.4) * 25,
        _ => (Math.Sin(timeAlive * 3) * 30) + (Math.Cos(timeAlive * 6) * 15),
    };

    /// <summary>
    /// Start one bubble's pop. Idempotent: a second hit on a bubble already popping does nothing,
    /// which is upstream's own first line (<c>if (!_isAlive || _isPopping) return;</c>,
    /// <c>Services/BubbleService.cs:3990</c>) and the reason a double click cannot score twice.
    /// </summary>
    /// <returns>True when this call is what started the pop.</returns>
    public bool Hit(int id)
    {
        for (var i = 0; i < _bubbles.Count; i++)
        {
            if (_bubbles[i].Id != id)
            {
                continue;
            }

            if (_bubbles[i].Popping)
            {
                return false;
            }

            _bubbles[i] = _bubbles[i] with { Popping = true };
            return true;
        }

        return false;
    }

    /// <summary>Drop every bubble without counting a pop or a miss — upstream's
    /// <c>PopAllBubbles()</c> on <c>Stop</c> (<c>Services/BubbleService.cs:725-739</c>), which ends a
    /// run rather than resolving one.</summary>
    public IReadOnlyList<BubblePopBubble> Clear()
    {
        var dropped = _bubbles.ToArray();
        _bubbles.Clear();
        return dropped;
    }
}
