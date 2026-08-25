namespace CcpClient.Desktop.Pointer;

/// <summary>
/// The sample grid one ink read walks: the disc's own box, the stride derived from it, and how many
/// points that gives in each direction. Derived from <c>Win32PointerSurface.DiscBox</c> and
/// <c>Win32PointerSurface.SampleStep</c> so the reader and the painter can never disagree about
/// where the disc is.
/// </summary>
/// <param name="Left">Left edge of the disc's box, in client coordinates.</param>
/// <param name="Top">Top edge, same space.</param>
/// <param name="Step">The stride between sample points, both axes.</param>
/// <param name="Columns">Sample points across, counting the one at <paramref name="Left"/>.</param>
/// <param name="Rows">Sample points down, counting the one at <paramref name="Top"/>.</param>
public readonly record struct PointerInkGrid(int Left, int Top, int Step, int Columns, int Rows)
{
    /// <summary>Every point a whole-disc read visits.</summary>
    public int Points => Columns * Rows;

    /// <summary>The client-space pixel one grid position names.</summary>
    public (int X, int Y) At(int row, int column) => (Left + (column * Step), Top + (row * Step));
}

/// <summary>
/// <b>How much of a pointer target's disc one placement reads back, and why it is not all of it.</b>
///
/// <para><b>The cost this exists to remove, measured on the running product at maximum settings
/// rather than argued.</b> A per-stage probe of one bubble placement: paint 2.0-2.5 ms, the OS
/// read-back 7.7-9.1 ms total, of which the z-order walk is 0.07 ms, the hit test 0.09 ms and the
/// <b>ink read 7.6 ms</b> — <b>98 % of the read-back</b>, roughly 400 <c>GetPixel</c> calls. Bubble
/// Pop repositions up to three targets every 30 ms (<c>Effects/BubblePopField.cs</c>
/// <c>StepInterval</c> and <c>MaxConcurrent</c>, both upstream's), so that read ran three times per
/// step on the UI thread, and the field achieved 1.0-1.2 Hz against an intended 33.</para>
///
/// <para><b>Why the overlay's answer does not transfer, and what replaces it.</b>
/// <c>Overlay/Win32OverlayPresence.cs</c> reads its whole surface back after any event that could
/// have changed the WINDOW and one band otherwise; that rests on "nothing about the window
/// changed", and a bubble moves every 30 ms, so the predicate is false on every frame. What is true
/// instead is narrower and enough: <b>a move does not change a window's CONTENT</b>. The ink read
/// asks a question about content, so it can be spread over placements without ever being skipped.
/// </para>
///
/// <para><b>The sweep, and the guarantee that survives it.</b> Each placement reads
/// <see cref="Phases"/>-th of the grid — the diagonal <c>(row + column) % Phases</c> — and the
/// phase advances, so every point of the disc is re-read within <see cref="Phases"/> placements
/// (240 ms at the 30 ms step). Two properties make that safe rather than merely cheaper:
/// <list type="number">
///   <item><description><b>Total ink loss is caught on the very next placement, with no latency at
///   all.</b> The failure the ink read exists to catch is a target that is on screen, absorbs
///   clicks and shows nothing — and a target that holds no ink holds none in EVERY phase, so any
///   phase sees it. Only a PARTIAL loss, on a target still visible and still hittable, can take up
///   to <see cref="Phases"/> placements to find.</description></item>
///   <item><description><b>The four background control points are never swept.</b> They are read in
///   full on every placement, because they are the leg that makes the count a differential at all
///   (<c>Win32PointerSurface.ReadInk</c>'s own remarks, the M-t mutation) and the leg that catches a
///   <c>WS_EX_LAYERED</c> window whose attributes never took. Four pixels is under 1 % of the
///   read.</description></item>
/// </list>
/// </para>
///
/// <para><b>And the whole disc is still read outright wherever the sweep may assume nothing</b>: on
/// a target's first placement, on any placement after a read that did not find ink (a failure is
/// never latched, so a blank target re-proves itself in full every step until it recovers), after
/// the operating system made the window repaint itself (<c>WM_PAINT</c>), and on every
/// <see cref="IPointerSurface.Observe"/>, whose contract is that nothing is cached.</para>
///
/// <para><b>Nothing is remembered across a placement.</b> Every number in a
/// <see cref="PointerTargetObservation"/> still comes from a <c>GetPixel</c> taken during that call
/// — the sweep changes how MANY are read, never whether they were read — so the record's own rule
/// that no member is "a value this process remembered writing" stays literally true.
/// <see cref="PointerTargetObservation.SampledPixels"/> is how a reader tells the two apart.</para>
/// </summary>
public static class PointerInkSweep
{
    /// <summary>
    /// How many placements the disc's grid is re-proved over.
    ///
    /// <para><b>Eight is not a taste, it is the largest value that keeps the no-false-blank
    /// guarantee</b> (<c>PointerInkSweepTests</c>): every phase must sample at least one point in
    /// the central third of the disc's box, which lies strictly inside the inscribed ellipse
    /// ((1/3)² + (1/3)² = 0.22 &lt; 1) and is therefore ink on any target whose paint landed. Ten,
    /// twelve and twenty-four all leave phases with no central point at some legal target size, and
    /// a phase that samples only the box's corners would report a perfectly good bubble blank. The
    /// same test walks every legal size from <c>PointerTargetRequest.MinimumSide</c> to
    /// <c>BubblePopField.MaxSize</c>, both axes.</para>
    /// </summary>
    public const int Phases = 8;

    /// <summary>The phase value meaning "the last read was the whole disc". Not a phase.</summary>
    public const int WholeDisc = -1;

    /// <summary>The grid for a target of this size, from the painter's own disc box and stride.</summary>
    public static PointerInkGrid GridFor(int width, int height)
    {
        var (left, top, right, bottom) = Win32PointerSurface.DiscBox(width, height);
        var step = Win32PointerSurface.SampleStep(right - left, bottom - top);
        return new PointerInkGrid(
            left, top, step,
            Columns: Ceiling(right - left, step),
            Rows: Ceiling(bottom - top, step));
    }

    /// <summary>
    /// The first column of <paramref name="row"/> this phase reads; walk from it in
    /// <paramref name="stride"/> steps to get the rest.
    ///
    /// <para><b>The diagonal is load-bearing and a plain <c>index % Phases</c> is not.</b> Flattening
    /// the grid row-major and taking every eighth point makes a row's phase depend only on its
    /// column whenever the column count is a multiple of eight — over the legal rectangles
    /// <c>PointerInkSweepTests</c> walks, 4718 (width, height, phase) triples then have no point
    /// anywhere near the disc's centre, and every one of those would call a perfectly drawn bubble
    /// blank. Shifting by the row removes it: the same sweep over the same domain leaves none.</para>
    ///
    /// <para>A <paramref name="stride"/> of 1 is the whole-disc read and answers 0 for every row and
    /// any phase, so the swept and unswept walks are ONE loop and the whole read visits exactly the
    /// points, in exactly the order, that it did before this sweep existed.</para>
    /// </summary>
    public static int FirstColumn(int row, int phase, int stride) =>
        stride <= 1 ? 0 : (((phase - row) % stride) + stride) % stride;

    /// <summary>Which phase the next read takes: the one after the last, or none at all when the
    /// caller is reading the whole disc.</summary>
    public static int Next(int lastPhase, bool wholeDisc) =>
        wholeDisc ? WholeDisc : ((lastPhase + 1 + Phases) % Phases);

    private static int Ceiling(int span, int step) => step <= 0 ? 0 : ((span + step - 1) / step);
}
