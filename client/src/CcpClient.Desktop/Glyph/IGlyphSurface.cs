using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Glyph;

/// <summary>
/// A per-pixel-alpha surface: a rectangle above every ordinary window whose <b>shape is its
/// alpha</b> — glyphs where the frame is opaque, the user's own desktop where it is not.
///
/// <para><b>Why this capability exists at all.</b> Eleven modules ran without it. Bouncing Text was
/// blocked from wave 46 and an earlier packet refused it with evidence rather than an excuse: its window is
/// transparency-backed glyphs (<c>Services/Subliminal/BouncingTextService.cs:827-828</c>,
/// <c>AllowsTransparency = true</c> and <c>Background = Brushes.Transparent</c>), and this port's
/// overlay composites one uniform <c>LWA_ALPHA</c> over an opaque BGRX frame by design
/// (<c>Overlay/OverlayFrame.cs:14-22</c>). At the shipped default opacity of 100
/// (<c>CCP.Core/Models/AppSettings.cs:3619-3624</c>) that would have put a BLACK SCREEN with text
/// on it in front of the user — worse than the absent module.</para>
///
/// <para><b>The contract, and it is not the overlay's.</b> Every operation returns a typed
/// <see cref="CapabilityState"/> and <see cref="CapabilityState.Available"/> is returned only after
/// the OPERATING SYSTEM was asked back. But the overlay's central question —
/// "does <c>GetLayeredWindowAttributes</c> hold a non-zero alpha?" — is <b>structurally
/// unavailable</b> here: <c>UpdateLayeredWindow</c> and <c>SetLayeredWindowAttributes</c> are
/// mutually exclusive per window, and a ULW window answers that call FALSE by design. Measured on
/// this machine, that is byte for byte what a never-composited layered window answers too. So the
/// ghost check is REPLACED rather than reused: <b>the operating system's own copy of the surface is
/// read back and required to carry the frame</b> (see <see cref="Paint"/>), with a negative control
/// that a layered window which never received a composite reads back nothing at all.</para>
///
/// <para><b>What Available still cannot mean.</b> That a human SEES it, and that a real pointer
/// passes through — both headed claims (<c>client/docs/verification-harness.md</c>). And, specific
/// to this capability: <b>a window read-back can never distinguish a fully transparent pixel from
/// an opaque black one</b>, because it flattens onto black and both are zero there. Only a
/// composited-desktop read over a known background separates them, that read is machine-conditional,
/// and it lives in the harness rather than in this interface.</para>
///
/// <para><b>Thread affinity.</b> A backend owns a native window and a native window belongs to the
/// thread that created it. Call every member on one thread.</para>
/// </summary>
public interface IGlyphSurface : IDisposable
{
    /// <summary>
    /// Put the surface on screen at the requested bounds, composited from
    /// <paramref name="frame"/>.
    ///
    /// <para><b>The frame is not optional, and that is the ghost defence at the API level.</b> A
    /// layered window that has been shown before it was ever composited is precisely the state
    /// the overlay capability measured and the first attempt shipped
    /// (<c>CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs:26-45</c>). There is no way to ask
    /// this surface for a window without content, so there is no moment at which one exists.</para>
    ///
    /// <para><see cref="CapabilityState.Available"/> means: the OS reports the window visible at
    /// exactly the requested rectangle, holds every extended style that was written, places it above
    /// every ordinary window in its own z-order, routes a point inside it the way the request asked
    /// (proven in both polarities), did not take the foreground, and — the clause that separates
    /// this from a ghost — <b>returns the frame's own opaque ink when asked for the surface's
    /// content back</b>.</para>
    /// </summary>
    CapabilityState Present(GlyphSurfaceRequest request, GlyphFrame frame);

    /// <summary>
    /// Replace the composited content of a surface that is already up.
    ///
    /// <para><see cref="CapabilityState.Available"/> means the operating system was asked for the
    /// surface's content BACK afterwards and returned this frame's fully-opaque samples exactly,
    /// and its fully-transparent samples as nothing. It never means <c>UpdateLayeredWindow</c>
    /// returned TRUE — measured: it returns TRUE for a non-premultiplied buffer that composites
    /// visibly wrong, and it returns TRUE for an entirely transparent frame that is indistinguishable
    /// from a window compositing nothing.</para>
    ///
    /// <para>A frame with no fully-opaque non-black pixel is REFUSED
    /// (<see cref="GlyphReasonCodes.GlyphFrameCarriesNoProvableInk"/>) rather than claimed: there is
    /// no reading of the OS that could tell it from the ghost.</para>
    /// </summary>
    CapabilityState Paint(GlyphFrame frame);

    /// <summary>
    /// Move a presented surface, keeping its content.
    ///
    /// <para><b>This is the operation the overlay does not have, and its absence is what blocked
    /// every moving-glyph module.</b> Moving an overlay means re-<c>Present</c>ing it, which walks
    /// the OS's whole top-level z-order and asks the window manager's hit test in BOTH polarities —
    /// momentarily clearing click-through — and at 60 Hz that is a window becoming input-catching
    /// sixty times a second over whatever the user is doing (<c>wpf-surface-reachability.md</c>
    /// D84 @ 7527243e7). Here a move is one <c>UpdateLayeredWindow</c> carrying a destination point and nothing
    /// else: no style write, no z-order walk, no hit test.</para>
    ///
    /// <para><see cref="CapabilityState.Available"/> is earned from <c>GetWindowRect</c>: the OS
    /// holds the new rectangle. It deliberately claims NOTHING about z-order, input routing or
    /// content, because none of those was re-asked — a caller that needs them calls
    /// <see cref="Present"/>, which earns them.</para>
    /// </summary>
    CapabilityState MoveTo(GlyphBounds bounds);

    /// <summary>
    /// Re-assert the topmost band for a surface already on screen — WPF's own
    /// <c>ReassertTopmost</c>, which the bouncing text service drives roughly every 500 ms because
    /// "bouncing text is long-lived and will lose topmost when competing with flash/video/overlay
    /// windows" (<c>BouncingTextService.cs:391-398</c>, its own comment).
    ///
    /// <para>It returns nothing, on purpose: it is one <c>SetWindowPos</c> and it confirms nothing.
    /// Returning a <see cref="CapabilityState"/> here would be a claim with no round trip behind
    /// it.</para>
    /// </summary>
    void Reassert();

    /// <summary>Take the surface off the screen, keeping the window and its composited content for
    /// the next <see cref="Present"/>. <see cref="CapabilityState.Available"/> means the OS
    /// confirmed the window is no longer visible and the hit test no longer routes to it.</summary>
    CapabilityState Withdraw();

    /// <summary>True only while a surface this instance put on screen is confirmed present.</summary>
    bool IsPresenting { get; }
}
