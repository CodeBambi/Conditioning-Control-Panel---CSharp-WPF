namespace CcpClient.Desktop.Effects;

/// <summary>
/// The Pink Filter's whole visual payload: one colour, at one opacity, over the whole display.
///
/// <para>It is a value rather than three loose parameters for the same reason
/// <see cref="SubliminalCard"/> is: the effect composes it from its own dials and the presenter
/// reads no settings at all, so a test can pin exactly what the module asked for without a screen
/// anywhere near it.</para>
/// </summary>
/// <param name="Red">Red channel of WPF's <c>GetFilterRgb()</c> (<c>OverlayService.cs:679-686</c>).</param>
/// <param name="Green">Green channel.</param>
/// <param name="Blue">Blue channel.</param>
/// <param name="OpacityPercent">WPF's <c>PinkFilterOpacity</c>, in percent
/// (<c>CCP.Core/Models/AppSettings.cs:3733-3738</c>).</param>
public readonly record struct PinkFilterTint(byte Red, byte Green, byte Blue, int OpacityPercent)
{
    /// <summary>
    /// The opacity the surface is asked for. <b>Linear</b>, and WPF says so in its own words at the
    /// line that computes it — "Linear opacity (no exponential curve)"
    /// (<c>Services/Notifications/OverlayService.cs:1174-1175</c>). A gamma curve here would change
    /// what every existing user's saved number means.
    /// </summary>
    public double Opacity => OpacityPercent / 100.0;

    /// <summary>
    /// True when this tint would put nothing at all on screen. WPF's clamp allows it —
    /// <c>Math.Clamp(value, 0, 50)</c> (<c>AppSettings.cs:3737</c>) — and WPF at zero still creates
    /// a full-screen layered window holding alpha 0. The port refuses to place one: an invisible
    /// always-on-top window over the user's desktop is the exact ghost
    /// <c>OverlaySurfaceRequest</c> was written to make impossible, so the module reports
    /// <c>Degraded</c> instead and its dot stays <c>Armed</c> (divergence D78).
    /// </summary>
    public bool IsInvisible => OpacityPercent <= 0;
}

/// <summary>
/// WPF's filter colour law, as a pure function — <c>OverlayService.GetFilterRgb</c> and
/// <c>TryParseHexColor</c> (<c>Services/Notifications/OverlayService.cs:679-706</c>).
///
/// <para>Separated from the effect for the reason <see cref="FlashSchedule"/> is: it is the whole
/// behaviour-visible part of "what colour is the filter", and a pure function can be pinned exactly
/// at its boundaries with no session, no screen and no settings store.</para>
/// </summary>
public static class PinkFilterColour
{
    /// <summary>WPF's fallback tint, hot pink, written as three literals at the end of its own
    /// <c>??</c> chain (<c>OverlayService.cs:686</c>) and again as the parse function's initial
    /// value (<c>:691</c>).</summary>
    public static readonly PinkFilterTint HotPink = new(255, 105, 180, 0);

    /// <summary>
    /// The colour a filter is drawn in, given the user's persisted hex string.
    ///
    /// <para>WPF's chain is <c>user hex -> active mod's retint -> hot pink</c> (<c>:682-686</c>).
    /// The middle term is <b>not ported</b>: the port has no mod system at all, so there is nothing
    /// to ask, and inventing an answer for it would be a claim about content this build does not
    /// load (divergence D77). The two ends are exact.</para>
    /// </summary>
    public static (byte Red, byte Green, byte Blue) Resolve(string? userHex) =>
        TryParseHex(userHex, out var rgb) ? rgb : (HotPink.Red, HotPink.Green, HotPink.Blue);

    /// <summary>
    /// WPF's <c>TryParseHexColor</c> (<c>OverlayService.cs:689-706</c>), verbatim in its decisions:
    /// null/blank is false, a leading <c>#</c> is trimmed, anything whose remaining length is not
    /// exactly 6 is false, and a pair that will not parse as hex is false rather than an exception.
    /// The out value on every false path is hot pink, which is WPF's own — it seeds <c>rgb</c>
    /// before the first test (<c>:691</c>), so a caller that ignores the bool still gets a colour.
    /// </summary>
    public static bool TryParseHex(string? hex, out (byte Red, byte Green, byte Blue) rgb)
    {
        rgb = (HotPink.Red, HotPink.Green, HotPink.Blue);
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var trimmed = hex.Trim().TrimStart('#');
        if (trimmed.Length != 6)
        {
            return false;
        }

        try
        {
            rgb = (
                Convert.ToByte(trimmed[..2], 16),
                Convert.ToByte(trimmed[2..4], 16),
                Convert.ToByte(trimmed[4..6], 16));
            return true;
        }
        // WPF's arm is a bare `catch` (:704). The port names the three types Convert.ToByte(string,
        // 16) documents instead of swallowing everything, because a swallowed exception of some
        // OTHER kind here would be a colour bug that looks like a user typo. The out value is reset
        // on the way out: a partial parse may already have written one or two channels.
        catch (FormatException)
        {
            rgb = (HotPink.Red, HotPink.Green, HotPink.Blue);
            return false;
        }
        catch (ArgumentException)
        {
            rgb = (HotPink.Red, HotPink.Green, HotPink.Blue);
            return false;
        }
        catch (OverflowException)
        {
            rgb = (HotPink.Red, HotPink.Green, HotPink.Blue);
            return false;
        }
    }
}
