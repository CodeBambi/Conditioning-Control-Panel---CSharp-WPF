using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Video;

/// <summary>
/// The honest refusal for a platform this build cannot PROVE a picture on.
///
/// <para>It shows nothing, decodes nothing and claims nothing. Every member answers the same typed
/// reason, and the read-backs answer <see cref="VideoSurfaceObservation.NotAsked"/> /
/// <see cref="VideoDisplayObservation.NotAsked"/> rather than a shaped zero — the distinction
/// matters, because "the OS said no frame is held" and "nobody asked" are different facts and a
/// caller that cannot tell them apart writes the wrong bug report.</para>
/// </summary>
public sealed class UnsupportedVideoPresence(string code, string detail) : IVideoPresence
{
    private readonly CapabilityState _refusal = new CapabilityState.Unavailable(new CapabilityReason(code, detail));

    /// <inheritdoc/>
    public CapabilityState Present(VideoSurfaceRequest request) => _refusal;

    /// <inheritdoc/>
    public CapabilityState Show(VideoFrame frame) => _refusal;

    /// <inheritdoc/>
    public CapabilityState Withdraw() => _refusal;

    /// <inheritdoc/>
    public VideoSurfaceObservation Observe() => VideoSurfaceObservation.NotAsked;

    /// <inheritdoc/>
    public VideoDisplayObservation ObserveDisplay() => VideoDisplayObservation.NotAsked;

    /// <inheritdoc/>
    public bool PictureIsMoving => false;

    /// <inheritdoc/>
    public bool CanReachADisplay => false;

    /// <inheritdoc/>
    public bool IsPresenting => false;

    /// <inheritdoc/>
    public CapabilityState? LastShow => _refusal;

    /// <inheritdoc/>
    public VideoSurfaceObservation LastObservation => VideoSurfaceObservation.NotAsked;

    /// <inheritdoc/>
    public int FramesHeld => 0;

    /// <inheritdoc/>
    public int FramesAdvanced => 0;

    public void Dispose()
    {
        // Nothing was ever acquired.
    }
}

/// <summary>The decoder's twin refusal: a clip source that opens nothing.</summary>
public sealed class UnsupportedVideoClipSource(string code, string detail) : IVideoClipSource
{
    private readonly CapabilityState _refusal = new CapabilityState.Unavailable(new CapabilityReason(code, detail));

    /// <inheritdoc/>
    public CapabilityState Open(string path, out IVideoClip? clip)
    {
        clip = null;
        return _refusal;
    }

    /// <inheritdoc/>
    public bool MediaStackUsable => false;
}
