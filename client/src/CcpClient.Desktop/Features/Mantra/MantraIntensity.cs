namespace CcpClient.Desktop.Features.Mantra;

/// <summary>One colour as upstream writes it, kept as four channels rather than a framework type
/// for the reason <c>Effects/PinkFilterTint.cs:16</c> keeps its own as three: the law is pure and is
/// proved with no screen anywhere near it.</summary>
public readonly record struct MantraColour(byte A, byte R, byte G, byte B)
{
    /// <summary>An opaque colour, upstream's <c>Color.FromRgb</c>.</summary>
    public static MantraColour Rgb(byte r, byte g, byte b) => new(0xFF, r, g, b);

    /// <summary>
    /// Upstream's <c>LerpColor</c> verbatim (<c>Windows/MantraWindow.xaml.cs:353-360</c>), including
    /// the detail that makes it a behaviour rather than an intention: each channel is
    /// <c>(byte)(a + (b - a) * t)</c>, a TRUNCATING cast and not a rounded one. Half of the ramp's
    /// steps land one unit lower than a rounding lerp would put them.
    /// </summary>
    public static MantraColour Lerp(MantraColour a, MantraColour b, double t) => new(
        (byte)(a.A + ((b.A - a.A) * t)),
        (byte)(a.R + ((b.R - a.R) * t)),
        (byte)(a.G + ((b.G - a.G) * t)),
        (byte)(a.B + ((b.B - a.B) * t)));
}

/// <summary>
/// <b>The streak's whole visual payload.</b> Upstream's <c>UpdateVisualIntensity</c>
/// (<c>Windows/MantraWindow.xaml.cs:310-351</c>) as a pure function of the streak: the game warms
/// from cold purple to hot pink as the user keeps the chain going, and cools back the instant it
/// breaks (<c>:283</c> calls this with zero).
///
/// <para><b>THE DRONE AND THE THREE TONES ARE REFUSED, and this is where the reason lives.</b>
/// Upstream's window is also an instrument: a 90 Hz sine with a 180 Hz harmonic at 0.4 of its gain,
/// mixed live and ramped from 0.05 to 0.4 by the very <see cref="T"/> below
/// (<c>:350</c>, <c>:362-393</c>, <c>:188-197</c>), plus three event tones — <c>400 + streak*20</c>
/// Hz for 150 ms on a banked repetition (<c>:258</c>), 200 Hz for 300 ms when the streak breaks
/// (<c>:280</c>) and 523 Hz for 400 ms at the end of the run (<c>:298</c>). Every one of them is
/// SYNTHESISED, by NAudio's <c>SignalGenerator</c> straight into a <c>WaveOutEvent</c>. This build's
/// audio seam takes a file: <c>Audio/IAudioPresence.cs:52</c> is
/// <c>AudioCue(string Slot, string Path, float Volume)</c>, there is no oscillator anywhere in
/// <c>Audio/</c>, and there is no bundled tone asset to point a cue at. So the port has no way to
/// make these sounds and does not pretend to — <see cref="DroneGain"/> is computed and published
/// so the number is not lost, and nothing consumes it. The user-visible consequence, stated plainly:
/// <b>the typed mantra game is silent here and hums upstream.</b></para>
/// </summary>
public readonly record struct MantraIntensity
{
    /// <summary>The streak at which the ramp is fully hot — upstream's
    /// <c>Math.Min(streak / 15.0, 1.0)</c> (<c>Windows/MantraWindow.xaml.cs:313</c>).</summary>
    public const int StreakCeiling = 15;

    /// <summary>An un-typed character (<c>Windows/MantraWindow.xaml.cs:29</c>). Fixed: upstream's
    /// <c>DimColor</c> is <c>static readonly</c> and the ramp never touches it.</summary>
    public static readonly MantraColour Dim = MantraColour.Rgb(0x35, 0x35, 0x50);

    /// <summary>The character the user got wrong (<c>:30</c>). Also fixed.</summary>
    public static readonly MantraColour Wrong = MantraColour.Rgb(0xFF, 0x44, 0x44);

    /// <summary>The cold end of every ramp but one — the highlight colour a run starts at
    /// (<c>:28</c>, <c>:316</c>).</summary>
    public static readonly MantraColour ColdHighlight = MantraColour.Rgb(0x99, 0x88, 0xDD);

    /// <summary>The hot end of every ramp: <c>#FF69B4</c>, upstream's pink
    /// (<c>:316</c>, <c>:336</c>, <c>:341</c>, <c>:344</c>).</summary>
    public static readonly MantraColour Hot = MantraColour.Rgb(0xFF, 0x69, 0xB4);

    /// <summary>The backdrop's outer stop. Constant in the markup
    /// (<c>Windows/MantraWindow.xaml:68</c>) — only the centre warms.</summary>
    public static readonly MantraColour BaseEdge = MantraColour.Rgb(0x0A, 0x05, 0x14);

    private MantraIntensity(double t) => T = t;

    /// <summary>Upstream's normalised streak, <c>Math.Min(streak / 15.0, 1.0)</c>
    /// (<c>Windows/MantraWindow.xaml.cs:313</c>). A negative streak cannot reach this — the session
    /// never produces one — but the floor is here so a caller cannot drive the ramp backwards past
    /// its cold end.</summary>
    public double T { get; }

    /// <summary>The ramp for a given streak.</summary>
    public static MantraIntensity For(int streak) =>
        new(Math.Clamp(streak / (double)StreakCeiling, 0.0, 1.0));            // :313

    /// <summary>The colour a matched character is painted (<c>:316</c>).</summary>
    public MantraColour Highlight => MantraColour.Lerp(ColdHighlight, Hot, T);

    /// <summary>How strongly the colour wash sits over the backdrop, <c>t * 0.8</c>
    /// (<c>:335</c>).</summary>
    public double WashOpacity => T * 0.8;

    /// <summary>The wash's centre stop, <c>#6633AA</c> to <c>#FF69B4</c> (<c>:336</c>).</summary>
    public MantraColour WashCentre => MantraColour.Lerp(MantraColour.Rgb(0x66, 0x33, 0xAA), Hot, T);

    /// <summary>The mantra's glow radius, <c>20 + t * 30</c> (<c>:339</c>).</summary>
    public double GlowBlurRadius => 20 + (T * 30);

    /// <summary>The glow's opacity, <c>0.6 + t * 0.4</c> (<c>:340</c>).</summary>
    public double GlowOpacity => 0.6 + (T * 0.4);

    /// <summary>The glow's colour, <c>#9966CC</c> to <c>#FF69B4</c> (<c>:341</c>).</summary>
    public MantraColour GlowColour => MantraColour.Lerp(MantraColour.Rgb(0x99, 0x66, 0xCC), Hot, T);

    /// <summary>The box's border, <c>#40FF69B4</c> to <c>#FFFF69B4</c> (<c>:344</c>) — the one ramp
    /// that moves ALPHA and leaves the three colour channels alone, so the border fades in rather
    /// than changing hue.</summary>
    public MantraColour InputBorder => MantraColour.Lerp(new MantraColour(0x40, 0xFF, 0x69, 0xB4), Hot, T);

    /// <summary>The backdrop's centre stop, <c>#1A0A2E</c> to <c>#2E0A2E</c> (<c>:347</c>).</summary>
    public MantraColour BaseCentre => MantraColour.Lerp(
        MantraColour.Rgb(0x1A, 0x0A, 0x2E), MantraColour.Rgb(0x2E, 0x0A, 0x2E), T);

    /// <summary>Upstream's drone target gain, <c>0.05f + t * 0.35f</c> (<c>:350</c>). <b>Computed
    /// and consumed by nothing</b> — see the type remarks for why this build cannot make the
    /// sound.</summary>
    public double DroneGain => 0.05 + (T * 0.35);
}
