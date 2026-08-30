using System;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// Where the book goes and how wide it is: the whole horizontal decision, as arithmetic.
///
/// <para><b>Why this is not in the window.</b> It used to be, and it was wrong in a way nobody could
/// have caught by looking at it. The old code put the book at her right, flipped to her left only if
/// the right overflowed the work area, and then finished with a clamp into the work area - so on a
/// desk where NEITHER side had room the clamp quietly dragged the book back on top of her, covering
/// her body, her gear, her <c>?</c> and anything in her hand. It needs a work area under roughly
/// <c>bodyWidth + 750</c> to happen, which is why it never showed on the machine that wrote it and
/// would have shown on somebody's 1280 laptop. Geometry that only misbehaves at sizes you do not
/// own has to be reachable from a test, so it lives here and the window just applies the answer.</para>
///
/// <para><b>The book has two widths, not a continuous one.</b> A book that took whatever width was
/// going would give every desk a different text column, and the cards are written to a measured one -
/// four nudges that fit here would be six lines and a scroll somewhere else. So it is
/// <see cref="FullWidth"/> or <see cref="NarrowWidth"/>, the same two-step the demo stage already
/// takes vertically, and the narrow book is exactly the full one with the stage at 2x.</para>
///
/// <para>The rail does not shrink in either. Forty-four DIP is two pixels per cell of a 16 cell
/// glyph plus its air, and an icon column you have to squint at is not navigation.</para>
/// </summary>
internal static class EmiBookLayout
{
    /// <summary>Air between her silhouette and the book's near edge, in DIPs.</summary>
    public const double BodyGap = 12.0;

    /// <summary>
    /// The book at its drawn width, in DIPs. Matches the XAML: 2 panel border + 44 rail + 2 rail
    /// border + 12 margin + 290 card column + 12 margin + 2 panel border.
    /// </summary>
    public const double FullWidth = 364.0;

    /// <summary>
    /// The narrow book. Same chrome, same rail, the card column cut to what a 2x stage needs
    /// (192 + its 2 DIP frame each side), which is the width the demo stops being the constraint at.
    /// </summary>
    public const double NarrowWidth = 270.0;

    /// <summary>What <see cref="Place"/> decided.</summary>
    /// <param name="Left">The book's left edge, in DIPs, in the same space the inputs were given in.</param>
    /// <param name="Width">Either <see cref="FullWidth"/> or <see cref="NarrowWidth"/>.</param>
    /// <param name="OnHerLeft">True when the book ended up on her left.</param>
    /// <param name="Narrow">True when the book took its narrow width, which also drops the stage to 2x.</param>
    /// <param name="CoversHer">
    /// True when even the narrow book did not fit beside her and the clamp put it over her body. The
    /// window still shows it - a book you asked for and cannot see is worse than one that overlaps -
    /// but this says so out loud instead of it being a silent consequence of a clamp.
    /// </param>
    internal readonly record struct Placement(
        double Left, double Width, bool OnHerLeft, bool Narrow, bool CoversHer);

    /// <summary>
    /// Choose a side and a width for a book beside a body box.
    ///
    /// <para>Reading order of the decisions, and each one is a rule rather than a fallback:</para>
    /// <list type="number">
    /// <item>If the full book fits on either side, it is full width, and it prefers her RIGHT.</item>
    /// <item>If it does not, the book goes narrow - narrow on the roomier side beats full width
    /// hanging over her, because the thing the book is for is being read.</item>
    /// <item>If the narrow book does not fit either, it goes on the roomier side anyway and is
    /// clamped into the work area, which is the only case that covers her.</item>
    /// </list>
    ///
    /// <para>Every length is in DIPs and in one space; the caller converts.</para>
    /// </summary>
    /// <param name="workLeft">Left edge of the monitor work area.</param>
    /// <param name="workWidth">Width of the monitor work area.</param>
    /// <param name="bodyLeft">Left edge of her body box.</param>
    /// <param name="bodyRight">Right edge of her body box.</param>
    public static Placement Place(double workLeft, double workWidth, double bodyLeft, double bodyRight)
    {
        double workRight = workLeft + workWidth;

        // The room OUTSIDE her, on each side, once the gap is paid for. Negative means she is
        // already past that edge, which a body dragged half off the desk really does produce.
        double roomRight = workRight - (bodyRight + BodyGap);
        double roomLeft = (bodyLeft - BodyGap) - workLeft;
        double best = Math.Max(roomRight, roomLeft);

        bool narrow = best < FullWidth;
        double w = narrow ? NarrowWidth : FullWidth;

        // Her right unless her left is genuinely roomier AND her right cannot take the book. Ties go
        // right, and so does the case where both sides fit: the book is read rather than operated,
        // and she usually lives at the right of the desk with her near side on the left.
        bool onHerLeft = roomRight < w && roomLeft > roomRight;

        double left = onHerLeft ? bodyLeft - BodyGap - w : bodyRight + BodyGap;

        // The clamp that started all this. It is still here and still last, because a book off the
        // side of the screen is not a better outcome than a book over her - but by now it only bites
        // when even the narrow book had nowhere to go, and it says so.
        double clamped = Math.Max(workLeft, Math.Min(workRight - w, left));
        bool coversHer = best < w;

        return new Placement(clamped, w, onHerLeft, narrow, coversHer);
    }
}
