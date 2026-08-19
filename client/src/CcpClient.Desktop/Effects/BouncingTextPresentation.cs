namespace CcpClient.Desktop.Effects;

/// <summary>WPF's three colour modes (<c>CCP.Core/Models/AppSettings.cs:3653-3658</c>, and the
/// switch that reads them at <c>BouncingTextService.cs:635-645</c>).</summary>
public enum BouncingTextColourMode
{
    /// <summary>WPF 0: a uniform pick from ten bright colours on every bounce. The shipped default.</summary>
    Random = 0,

    /// <summary>WPF 1: the user's chosen colour, never re-rolled.</summary>
    Fixed = 1,

    /// <summary>WPF 2: an ordered twelve-step hue wheel, one step per bounce.</summary>
    Rainbow = 2,
}

/// <summary>
/// What the Bouncing Text module's dials currently describe: everything the motion and the raster
/// need, and nothing that belongs to a window.
///
/// <para>A record rather than a bag of parameters for the same reason
/// <see cref="SpiralPresentation"/> is one: the panel shows the user the values that would really be
/// used, and the module hands the same object to the field and to the rasteriser, so the two cannot
/// disagree about what is on screen.</para>
/// </summary>
/// <param name="Enabled">The module's own on/off dial.</param>
/// <param name="SpeedSetting">WPF's 1-10 speed (<c>AppSettings.cs:3606-3611</c>), which becomes a
/// ×0.1-×1.0 multiplier on a 180-300 px/s base.</param>
/// <param name="SizePercent">WPF's 50-300 % size (<c>:3613-3618</c>), applied to a 72 px base
/// (<c>BouncingTextService.cs:53</c>).</param>
/// <param name="OpacityPercent">WPF's 0-100 opacity (<c>:3620-3625</c>), which here becomes the
/// surface's uniform multiplier OVER the glyph's own per-pixel alpha — WPF sets the same value as
/// the text element's <c>Opacity</c> (<c>BouncingTextService.cs:975</c>).</param>
/// <param name="ColourMode">Which of the three colour rules is in force.</param>
/// <param name="FixedColour">The user's colour for <see cref="BouncingTextColourMode.Fixed"/>, or
/// null for WPF's hot-pink fallback.</param>
/// <param name="Outline">WPF's outline style (<c>:3709-3714</c>): a black stroke round the glyph
/// instead of a drop shadow.</param>
/// <param name="EnabledPhrases">The words this run may show, in a stable order.</param>
public sealed record BouncingTextPresentation(
    bool Enabled,
    int SpeedSetting,
    int SizePercent,
    int OpacityPercent,
    BouncingTextColourMode ColourMode,
    uint? FixedColour,
    bool Outline,
    IReadOnlyList<string> EnabledPhrases)
{
    /// <summary>WPF's base font size (<c>BouncingTextService.cs:53</c>): 72 px at 100 %.</summary>
    public const int BaseFontSize = 72;

    /// <summary>The font size this presentation renders at — WPF's
    /// <c>(int)(BASE_FONT_SIZE * BouncingTextSize / 100.0)</c> (<c>:139</c>).</summary>
    public int FontSize => Math.Max(1, (int)(BaseFontSize * SizePercent / 100.0));

    /// <summary>
    /// The surface's uniform multiplier in (0, 1], or null when the dial is at zero.
    ///
    /// <para><b>Zero is inside WPF's clamp</b> (<c>AppSettings.cs:3624</c>) and WPF at zero still
    /// creates a full-screen always-on-top window holding fully transparent text — the exact ghost
    /// this port refuses to construct. So the module answers for it in type instead, exactly as the
    /// spiral does (<see cref="SpiralPresentation.IsInvisible"/>).</para>
    /// </summary>
    public double? SurfaceOpacity => OpacityPercent <= 0 ? null : Math.Clamp(OpacityPercent / 100.0, 0.0, 1.0);

    /// <summary>True when the dials describe something nobody could see.</summary>
    public bool IsInvisible => SurfaceOpacity is null;
}
