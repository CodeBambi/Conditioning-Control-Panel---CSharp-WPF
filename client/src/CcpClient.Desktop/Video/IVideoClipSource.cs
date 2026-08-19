using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Video;

/// <summary>
/// What the operating system said about a clip it opened. Every field is the OS's answer, not the
/// caller's request.
/// </summary>
/// <param name="Asked">A decode was attempted at all. False on a build with no media stack — the
/// Linux case — and never dressed up as a zero.</param>
/// <param name="Width">The OS's own picture width for the stream it will hand back.</param>
/// <param name="Height">The OS's own picture height.</param>
/// <param name="FrameInterval">The stream's own frame period, from <c>MF_MT_FRAME_RATE</c>.
/// <see cref="TimeSpan.Zero"/> when the OS reports none, which is a legitimate answer for a
/// container that carries no rate.</param>
/// <param name="Duration">The presentation's duration, from <c>MF_PD_DURATION</c>, or
/// <see cref="TimeSpan.Zero"/> when the OS reports none.</param>
/// <param name="BottomUp">The OS handed the picture back BOTTOM-UP
/// (<c>MF_MT_DEFAULT_STRIDE &lt; 0</c>). Recorded rather than silently absorbed because it is the
/// thing a solid-colour fixture can never catch — see <see cref="VideoFrame"/>.</param>
public readonly record struct VideoClipInfo(
    bool Asked, int Width, int Height, TimeSpan FrameInterval, TimeSpan Duration, bool BottomUp)
{
    /// <summary>Nothing was asked, because this build has no way to ask.</summary>
    public static VideoClipInfo NotAsked { get; } = new(false, 0, 0, TimeSpan.Zero, TimeSpan.Zero, false);

    /// <summary>The OS reports a picture with real dimensions. It says NOTHING about whether any
    /// frame reached a surface — that is <see cref="IVideoPresence"/>'s question, and keeping the
    /// two apart is the whole of SP-111.</summary>
    public bool HasPicture => Asked && Width > 0 && Height > 0;
}

/// <summary>
/// One open clip, handing back decoded pictures in order.
///
/// <para><b>This is the DECODE half, and on its own it proves only that a file is readable.</b> The
/// port has rejected that grade of evidence four times under other names — the tray method
/// returning, the overlay flag being set, <c>Play()</c> returning, a fake dial re-imposing its own
/// clamp. A caller must never report a module as working because this type produced bytes.</para>
///
/// <para><b>Thread affinity.</b> An open clip belongs to the thread that opened it (Media
/// Foundation's source reader is not free-threaded by default). Open, read and dispose on one
/// thread — the surface thread in the product.</para>
/// </summary>
public interface IVideoClip : IDisposable
{
    /// <summary>What the OS said about this clip when it opened.</summary>
    VideoClipInfo Info { get; }

    /// <summary>
    /// The next decoded picture, or <c>null</c> at end of stream or on a decode fault. Top-down
    /// BGRX, already normalised out of the OS's own orientation.
    /// </summary>
    VideoFrame? ReadFrame();

    /// <summary>How many pictures this clip has handed back. A count of DECODED frames, and it is
    /// named that way so nothing can mistake it for a count of DISPLAYED ones.</summary>
    int DecodedFrames { get; }

    /// <summary>True once the OS has reported end of stream.</summary>
    bool Ended { get; }
}

/// <summary>
/// The port's decoder seam: the operating system's own media stack, behind an interface, so a
/// module can be exercised with no codec anywhere near it.
///
/// <para><b>Why the OS's stack and not a bundled decoder.</b> The shipping product carries LibVLC
/// (<c>Services/Video/VideoService.cs:308-316</c>) and roughly a hundred megabytes of native
/// libraries with it, plus the whole quarantine/poisoning apparatus its own comments describe
/// (<c>:790</c>, <c>:1031</c>, <c>:819</c>). This port asks Windows to decode instead: it is already
/// on every target machine, its refusals are typed HRESULTs, and — the part that matters here —
/// <b>"the operating system decoded this file" is itself an OS-level fact</b> rather than a claim
/// about a library this process shipped.</para>
/// </summary>
public interface IVideoClipSource
{
    /// <summary>
    /// Open a file for decoding. <see cref="CapabilityState.Available"/> means the OS opened it,
    /// reported a video stream, and agreed to hand its pictures back in the surface's own pixel
    /// format. Anything else is a typed refusal naming which link failed — and a clip refusal is
    /// never a surface refusal.
    /// </summary>
    CapabilityState Open(string path, out IVideoClip? clip);

    /// <summary>
    /// Ask the OS whether its media stack is usable AT ALL, right now. A live round trip; never
    /// throws, and a build with no mechanism answers <c>false</c> rather than a shaped success.
    /// </summary>
    bool MediaStackUsable { get; }
}
