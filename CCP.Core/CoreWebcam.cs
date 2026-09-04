using System;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The webcam seam: whether this head has an eye-tracking engine at all, and the one verb that
    /// can be honoured without a live feed - revoking consent.
    ///
    /// <para><c>WebcamTrackingService</c> (ConditioningControlPanel/Services/Webcam/) owns a capture
    /// device, three ONNX sessions and an OpenCvSharp frame loop, so it does not move to Core. Only
    /// these two facts cross.</para>
    ///
    /// <para><b>What this seam deliberately does NOT carry, and why the next layer must not add it
    /// casually.</b> There is no <c>Start</c>, <c>Stop</c>, <c>IsRunning</c>, <c>Calibration</c> and
    /// no gaze / iris / head-pose event stream. Every blocked view that would consume those consumes
    /// them TOGETHER with the per-frame feed: the tracker-test window plots <c>OnGazeMove</c>, quick
    /// recal takes its median, the calibration window fits a polynomial to <c>OnRawIris</c> +
    /// <c>OnHeadPose</c>, and the gaze minigame's only input writer is <c>OnGazeSide</c>. A seam
    /// that answered "running" and "calibrated" but carried no frames would let all four pass their
    /// preconditions and then sit there - a full-screen gaze tracker whose dot cannot move, a recal
    /// that reports an offset it never sampled. Those are exactly the lies this port has been
    /// refusing elsewhere. The feed and the state belong in one layer; carrying the state alone is
    /// worse than carrying neither.
    ///
    /// <para>Add them the day a head can supply frames, and add them together.</para></para>
    ///
    /// <para>Unseeded means "this head has no webcam engine", answered honestly: not available, and
    /// revoke is a silent no-op. It is never optimistic - it cannot report a camera as closed while
    /// one is open, because it never claims to have closed one.</para>
    /// </summary>
    public static class CoreWebcam
    {
        public static volatile Func<bool>? IsAvailableProvider;
        public static volatile Action? RevokeConsentAction;

        /// <summary>
        /// True when this head constructed a webcam tracking engine. Says nothing about consent
        /// (that is <c>Services.Webcam.WebcamConsent.IsCurrent</c>), nothing about whether the
        /// camera is open, and nothing about calibration - it is the capability question a view asks
        /// before offering a webcam control at all. False means "offer nothing, and say why".
        /// </summary>
        public static bool IsAvailable
        {
            get { try { return IsAvailableProvider?.Invoke() ?? false; } catch { return false; } }
        }

        /// <summary>
        /// Undoes everything the consent dialog promised: stops tracking, deletes the calibration,
        /// clears the consent record and turns the webcam features off. All four or none - which is
        /// why a view must call this rather than clearing <c>WebcamConsentGiven</c> itself and
        /// leaving three of the four promises unkept.
        ///
        /// <para>A no-op when <see cref="IsAvailable"/> is false, safe in the direction that
        /// matters: no engine means nothing to stop and no calibration to delete. Callers still gate
        /// on <see cref="IsAvailable"/> before telling the user anything was revoked.</para>
        /// </summary>
        public static void RevokeConsent()
        {
            try { RevokeConsentAction?.Invoke(); } catch { /* revoking must never throw at a caller */ }
        }
    }
}
