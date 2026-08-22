namespace CcpClient.Desktop.Glyph;

/// <summary>
/// Stable machine-readable reason codes for the per-pixel-alpha glyph surface
/// (runtime-capability-contract §1: codes are additive and land beside their consumer, the same
/// placement <see cref="Overlay.OverlayReasonCodes"/> and <see cref="Video.VideoReasonCodes"/>
/// already use).
///
/// <para><b>Why this capability needs its own codes rather than the overlay's.</b> The overlay
/// composites ONE uniform <c>LWA_ALPHA</c> over an opaque frame and its ghost code
/// (<c>overlay-not-composited</c>) is defined as "the OS holds no non-zero alpha for this window".
/// A per-pixel-alpha surface is driven by <c>UpdateLayeredWindow</c>, for which
/// <c>GetLayeredWindowAttributes</c> returns FALSE <b>by design</b> — measured on this machine, and
/// measured to be exactly what a never-composited layered window also reports. Reusing the
/// overlay's code here would mean either a permanent refusal or a check that means nothing, so the
/// failure modes are named again in this capability's own terms, with a different instrument behind
/// each one.</para>
/// </summary>
public static class GlyphReasonCodes
{
    /// <summary>
    /// No per-pixel-alpha backend in this build can drive this platform, so nothing was attempted.
    /// The detail names the route and the undischarged manual gate.
    /// </summary>
    public const string GlyphMechanismAbsent = "glyph-mechanism-absent";

    /// <summary>The window class could not be registered, or <c>CreateWindowEx</c> refused.</summary>
    public const string GlyphWindowCreationFailed = "glyph-window-creation-failed";

    /// <summary>
    /// <b>THE GHOST CODE, and the reason this capability was allowed to exist.</b> The window is
    /// layered, visible and on top, and the operating system's own copy of its surface does not
    /// carry the frame that was handed over.
    ///
    /// <para>Measured before the code was written: a layered window that never received
    /// <c>UpdateLayeredWindow</c> reads back <b>0 non-zero pixels of 14400</b> through
    /// <c>PrintWindow(PW_RENDERFULLCONTENT)</c>, while the same window after one ULW reads back the
    /// frame's fully-opaque samples EXACTLY (0 mismatches over 10 colours). This code is what
    /// separates those two states, and it is the check that stands in for the overlay's
    /// <c>GetLayeredWindowAttributes</c> ghost check, which is structurally unavailable here.</para>
    /// </summary>
    public const string GlyphNotComposited = "glyph-not-composited";

    /// <summary>
    /// The OS holds uniform layered attributes for this window, which means it is in
    /// <c>SetLayeredWindowAttributes</c> mode and <c>UpdateLayeredWindow</c> will refuse it forever
    /// (measured: err 87, permanently, once the uniform call has been made). A per-pixel composite
    /// is impossible on that window and none is claimed.
    /// </summary>
    public const string GlyphUniformAlphaMode = "glyph-uniform-alpha-mode";

    /// <summary>
    /// <c>UpdateLayeredWindow</c> itself returned FALSE. The detail carries the Win32 last-error,
    /// because 87 (<c>ERROR_INVALID_PARAMETER</c>) is the specific answer this surface would get
    /// for a window that is not layered — the overlay's measured hazard — and it must never be reported
    /// as a generic failure.
    /// </summary>
    public const string GlyphCompositeRefused = "glyph-composite-refused";

    /// <summary>
    /// The frame carries no fully-opaque, non-black pixel, so <b>nothing about it can be
    /// distinguished from a window that composites nothing</b>. Measured: an all-transparent ULW
    /// frame reads back 0 non-zero pixels of 25600 — byte for byte what the ghost reads. Rather
    /// than claim a composite it cannot prove, the surface refuses the frame and says why.
    /// </summary>
    public const string GlyphFrameCarriesNoProvableInk = "glyph-frame-carries-no-provable-ink";

    /// <summary>The frame's pixel size is not the presented surface's. This capability scales
    /// nothing, for the same reason the overlay does not (D55).</summary>
    public const string GlyphFrameSizeMismatch = "glyph-frame-size-mismatch";

    /// <summary>The OS does not report the window visible after it was shown.</summary>
    public const string GlyphNotVisible = "glyph-not-visible";

    /// <summary>The OS holds a different rectangle than the one asked for, or refused to place
    /// or move the surface.</summary>
    public const string GlyphGeometryRefused = "glyph-geometry-refused";

    /// <summary>The extended-style read-back is missing a bit that was written.</summary>
    public const string GlyphStyleRefused = "glyph-style-refused";

    /// <summary>The OS's own top-level z-order does not place the surface above every ordinary
    /// window, so it is buried whatever <c>WS_EX_TOPMOST</c> says.</summary>
    public const string GlyphNotOnTop = "glyph-not-on-top";

    /// <summary>Click-through is set and the window manager still routes a point inside the surface
    /// to it: the desktop underneath is broken while the surface looks implemented.</summary>
    public const string GlyphInputNotPassingThrough = "glyph-input-not-passing-through";

    /// <summary>The surface could not be shown to own its own point even with click-through
    /// cleared, so its input behaviour cannot be told apart from being buried.</summary>
    public const string GlyphInputNotReceived = "glyph-input-not-received";

    /// <summary>The surface became the foreground window, interrupting whatever the user was
    /// doing. <c>WS_EX_NOACTIVATE</c> did not hold.</summary>
    public const string GlyphStoleFocus = "glyph-stole-focus";

    /// <summary>Nothing is presented by this surface, so the requested operation had nothing to act
    /// on and nothing succeeded.</summary>
    public const string GlyphNothingPresented = "glyph-nothing-presented";

    /// <summary>This surface was disposed; its window is gone and it will never present another.</summary>
    public const string GlyphSurfaceDisposed = "glyph-surface-disposed";

    /// <summary>The window is still on screen after it was asked to withdraw.</summary>
    public const string GlyphWithdrawRefused = "glyph-withdraw-refused";
}
