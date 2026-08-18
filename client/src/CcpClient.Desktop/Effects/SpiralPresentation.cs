namespace CcpClient.Desktop.Effects;

/// <summary>
/// The Spiral Overlay's whole presentation: how opaque the layer is, and how fast its frames go by.
///
/// <para>It is a value rather than loose parameters for the reason <see cref="PinkFilterTint"/> and
/// <see cref="SubliminalCard"/> are: the effect composes it from its own dials and the presenter
/// reads no settings at all, so a test can pin exactly what the module asked for with no screen
/// anywhere near it.</para>
/// </summary>
/// <param name="OpacityPercent">WPF's <c>SpiralOpacity</c> dial, in percent
/// (<c>CCP.Core/Models/AppSettings.cs:2670-2675</c>).</param>
public readonly record struct SpiralPresentation(int OpacityPercent)
{
    /// <summary>
    /// WPF's own reduction, in its own words: <c>var actualOpacity = (opacity / 100.0) * 0.1;</c>
    /// under the comment "Very subtle opacity - 90% reduction"
    /// (<c>Services/Notifications/OverlayService.cs:1692-1693</c>, and again for the video route at
    /// <c>:1758</c>).
    ///
    /// <para><b>The factor is behaviour, not decoration.</b> A user's saved 10 means a layer at 1 %
    /// of full, which is what makes a full-screen spinning image tolerable to sit under for an
    /// hour; dropping the ×0.1 here would multiply every existing user's spiral by ten.</para>
    /// </summary>
    public const double SubtletyFactor = 0.1;

    /// <summary>The opacity the surface is asked for: the dial, linear, times WPF's ×0.1.</summary>
    public double Opacity => OpacityPercent / 100.0 * SubtletyFactor;

    /// <summary>
    /// True when this presentation would put nothing at all on screen. WPF's clamp allows a zero
    /// dial (<c>AppSettings.cs:2674</c>) and WPF at zero still creates a full-screen always-on-top
    /// window holding a fully transparent image. The port refuses to place an invisible surface
    /// (<c>Overlay/OverlaySurfaceRequest.cs</c> will not construct one), so the module reports
    /// <c>Degraded</c> instead and its dot stays <c>Armed</c> — the same divergence the tint already
    /// carries (D78).
    /// </summary>
    public bool IsInvisible => Opacity <= 0.0;
}

/// <summary>
/// WPF's GIF frame-delay law, as a pure function — the clamp inside <c>LoadSpiralGifFramesFrom</c>
/// (<c>Services/Notifications/OverlayService.cs:1543-1556</c>).
///
/// <para>Separated from the decoder for the reason <see cref="FlashSchedule"/> is separated from the
/// flash: it is the whole behaviour-visible part of "how fast does the spiral turn", and a pure
/// function can be pinned exactly at its boundaries with no image, no screen and no GDI+.</para>
/// </summary>
public static class SpiralFrameDelay
{
    /// <summary>WPF's fallback when the GIF carries no usable delay (<c>OverlayService.cs:1547</c>,
    /// and again as the field's initial value at <c>:198</c>): 50 ms.</summary>
    public static readonly TimeSpan Default = TimeSpan.FromMilliseconds(50);

    /// <summary>WPF's lower bound (<c>OverlayService.cs:1552</c>). A GIF that asks for less is given
    /// <see cref="Default"/>, NOT clamped to the bound — see <see cref="FromHundredths"/>.</summary>
    public static readonly TimeSpan Minimum = TimeSpan.FromMilliseconds(20);

    /// <summary>WPF's upper bound (<c>OverlayService.cs:1552</c>). Same rule.</summary>
    public static readonly TimeSpan Maximum = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The delay a GIF's <c>PropertyTagFrameDelay</c> (0x5100) value means, in WPF's arithmetic:
    /// <c>value * 10</c> ms, and then <c>if (ms &lt; 20 || ms &gt; 500) ms = 50;</c>
    /// (<c>OverlayService.cs:1549-1552</c>).
    ///
    /// <para><b>Out of range falls back rather than clamping</b>, and that is upstream's shape, not
    /// a simplification: a 0-hundredths GIF (which is most of the web's, meaning "as fast as you
    /// can") becomes 50 ms rather than 20 ms, and a 30-second frame becomes 50 ms rather than
    /// 500 ms. Clamping instead would change both.</para>
    /// </summary>
    public static TimeSpan FromHundredths(int hundredths)
    {
        var milliseconds = (long)hundredths * 10;
        if (milliseconds < Minimum.TotalMilliseconds || milliseconds > Maximum.TotalMilliseconds)
        {
            return Default;
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
