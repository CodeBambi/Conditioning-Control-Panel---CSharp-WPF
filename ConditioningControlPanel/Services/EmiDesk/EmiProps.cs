using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// THE THINGS SHE HOLDS. Three small back-facing plates - a brick phone, a clipboard and a punch
/// card - that ride at her right hand during an idle beat and come straight back off.
///
/// <para><b>Why "back-facing" is the whole design.</b> The first pass drew these front-on, the way
/// a shop icon is drawn, and they read as props being SHOWN to the viewer. She is looking at them,
/// not us (owner, 2026-08-30), so every plate is drawn from behind: a blank phone shell, the back
/// of a clipboard, the reverse of a card. What the viewer sees is the back of a thing EMI is
/// reading, which is what a person holding something actually looks like from across a desk.</para>
///
/// <para><b>There is no hold pose and there is not going to be one.</b> Two dedicated body frames
/// (<c>body-hold</c>, <c>body-reach</c>) were generated for this and rejected as off-model: her
/// arms are four-pixel stubs and every attempt to bend one into a grip changed the silhouette. So
/// the prop anchors to the arm that is ALREADY there - it lands on top of her right hand, which
/// hides the hand behind it and lets the eye do the gripping. That is why the anchor below is a
/// fixed corner and not a per-pose table: the right arm sits in the same place across all ten
/// poses (it is the LEFT arm and the antenna that move), so one anchor is honest for the whole set,
/// sway included.</para>
///
/// <para><b>The anchor is the RIGHT hand and the desk toy owns the LEFT.</b> Counter Stock's
/// <c>emi_desk_toy</c> docks at <c>left:-6%</c> (widget.css); a held prop on that side would sit on
/// top of a prize the player paid for. The two never share a corner.</para>
///
/// <para>Geometry is in FRACTIONS OF THE BODY BOX (859 x 869), exactly like the glass rect in
/// emi.css, so the web twin (<c>.emi-prop</c> in <c>Resources/web/arcademy/emi/widget.css</c>) and
/// this file are the same numbers written twice. Change one, change the other.</para>
/// </summary>
public static class EmiProps
{
    /// <summary>How a plate is sized: by its height, or by its width.</summary>
    public enum Fit
    {
        /// <summary>Scale to <see cref="EmiProp.Frac"/> of the body box HEIGHT.</summary>
        Height,

        /// <summary>Scale to <see cref="EmiProp.Frac"/> of the body box WIDTH.</summary>
        Width
    }

    /// <summary>
    /// One held prop. <c>Frac</c> is read through <c>Sizing</c>; a wide flat thing (the punch card)
    /// has to be pinned by width or it grows off the side of the frame.
    /// </summary>
    /// <param name="Key">The name callers use. Also the file stem.</param>
    /// <param name="Frac">Size as a fraction of the body box, per <paramref name="Sizing"/>.</param>
    /// <param name="Sizing">Which body-box dimension <paramref name="Frac"/> is measured against.</param>
    /// <param name="TiltDeg">Clockwise tilt in degrees, about the plate's bottom-right corner. A
    /// prop hanging dead vertical reads as pasted on; a few degrees reads as held.</param>
    public sealed record EmiProp(string Key, double Frac, Fit Sizing, double TiltDeg);

    // ------------------------------------------------------------------ the anchor
    //
    // Her right hand, in body-box fractions. Both numbers were set by compositing all three plates
    // over body-idle and body-sway2 and looking at them; they are not derived from anything.

    /// <summary>
    /// The prop's RIGHT edge, as a fraction of the body box width. Pulled in from 0.965 on the
    /// owner's first look at it running (2026-08-30): flush against the edge of the box the plate
    /// read as taped to the frame rather than held, and three percent buys the hand some air on
    /// its outside without the plate starting to cover her sneaker.
    /// </summary>
    public const double RightFrac = 0.935;

    /// <summary>The prop's BOTTOM edge, as a fraction of the body box height.</summary>
    public const double BottomFrac = 0.905;

    /// <summary>
    /// THE ONE INVARIANT, and the twin of the desk toy's. The glass ends at
    /// <c>29.46% + 37.63% = 67.09%</c> down the body box. A prop's TOP must stay below that or the
    /// plate clips the bottom-right corner of her face. Every size below is chosen so that
    /// <c>BottomFrac - height</c> clears it, and <see cref="TopFrac"/> is that check.
    /// </summary>
    public const double GlassBottomFrac = 0.6709;

    /// <summary>The body PNG's aspect (869 / 859), repeated from EmiDeskWindow so a pure geometry
    /// check needs no window.</summary>
    public const double BodyAspect = 869.0 / 859.0;

    /// <summary>
    /// Where a prop's TOP edge lands, as a fraction of the body box height. Pure; the layout code
    /// and the invariant test read this one function rather than each doing the algebra.
    /// </summary>
    public static double TopFrac(EmiProp? prop)
    {
        if (prop == null) return BottomFrac;
        double h = prop.Sizing == Fit.Height
            ? prop.Frac
            // A width fraction is a fraction of the WIDTH; turning it into a height fraction needs
            // both the plate's own aspect and the body box's.
            : prop.Frac * PlateAspect(prop.Key) / BodyAspect;
        return BottomFrac - h;
    }

    /// <summary>The drawn plate's height / width. Falls back to a square, which is the pessimistic
    /// answer for the invariant above.</summary>
    private static double PlateAspect(string key)
        => PlateAspects.TryGetValue(key, out var a) ? a : 1.0;

    /// <summary>The shipped plates' aspects, so geometry needs no image decoder. Keep in step with
    /// the PNGs under <c>art/emi/props/</c>.</summary>
    private static readonly IReadOnlyDictionary<string, double> PlateAspects =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["phone"] = 170.0 / 98.0,
            ["clipboard"] = 226.0 / 170.0,
            ["punchcard"] = 118.0 / 214.0
        };

    // ------------------------------------------------------------------ the props

    /// <summary>
    /// The three that exist as art, in install order. Nothing here is a prize, a purchase or an
    /// unlock: they are wardrobe for an idle beat and every player has all three.
    /// </summary>
    public static readonly IReadOnlyList<EmiProp> All = new[]
    {
        // The brick phone. Tallest and narrowest, so it takes the biggest tilt without its top
        // corner wandering toward the glass.
        new EmiProp("phone", 0.26, Fit.Height, 7),
        // The clipboard. A shade taller than the phone because the clip at the top is drawn art
        // that wants room to read.
        new EmiProp("clipboard", 0.285, Fit.Height, 6),
        // The punch card, pinned by WIDTH: it is wider than it is tall and a height fraction would
        // run it off the side of the frame.
        new EmiProp("punchcard", 0.25, Fit.Width, 4)
    };

    /// <summary>One prop by name, or null for junk. Never throws.</summary>
    public static EmiProp? Get(string? key)
        => string.IsNullOrWhiteSpace(key)
            ? null
            : All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// Absolute path to a prop plate beside the exe, or null when the art is missing. Same tree and
    /// same Content glob as the pose PNGs (<see cref="EmiChains.BodyPath"/>), so the desk and the
    /// campus read one copy of the file. Never throws.
    /// </summary>
    public static string? Path(string? key)
    {
        try
        {
            var prop = Get(key);
            if (prop == null) return null;
            var path = System.IO.Path.Combine(AppContext.BaseDirectory,
                "Resources", "web", "arcademy", "art", "emi", "props", prop.Key + ".png");
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] prop path lookup failed for {Key}", key);
            return null;
        }
    }

    // ------------------------------------------------------------------ the beat

    /// <summary>
    /// THE IDLE BEAT'S SHAPE. She brings a thing up, looks at it, puts it away. It is deliberately
    /// longer than the other fidgets (a twitch is 500 ms) because a prop that flashes on and off
    /// reads as a glitch rather than as her checking something.
    /// </summary>
    public const int HoldMs = 2600;

    /// <summary>How long the plate takes to rise into her hand, and to drop back out of it.</summary>
    public const int RiseMs = 220;

    /// <summary>How far below its resting place the plate starts, as a fraction of its own height.
    /// It comes up from behind her, never in from off-screen.</summary>
    public const double RiseFrac = 0.55;

    /// <summary>The face she wears while reading: eyes down and half-lidded. She is not talking to
    /// you, she is looking at the thing.</summary>
    public const string Face = "-_-";

    /// <summary>The half-beat after it goes away: caught doing something ordinary.</summary>
    public const string DoneFace = "^_^";
}
