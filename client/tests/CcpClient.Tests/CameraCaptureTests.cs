using CcpClient.Desktop.Camera;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The CAPTURE half: the gate above it, the ladder inside it, the frame-acceptance rule that decides
/// whether a camera is working, and the release afterwards.
///
/// <para><b>NOTHING HERE OPENS A CAMERA, and that is a rule rather than a convenience.</b> A test
/// suite that opened this machine's webcam would light its owner's camera indicator on every floor
/// run, without consent, for a build that has no gaze engine — which is precisely the behaviour this
/// capability exists to prevent. Every fact below drives a recording double, so what is proved is the
/// GATE, the ORDER and the ARITHMETIC. That a real camera opens, streams and is released was proved
/// separately against this machine's integrated camera through
/// <c>client/spikes/CcpSpike.Camera</c>, which a human starts and watches; no assertion in this file
/// stands in for that, and none of them should be read as doing so.</para>
///
/// <para><b>What no fact here proves.</b> No camera is opened, no Media Foundation call is made, no
/// frame comes off a device, no gaze engine exists, and the Linux capture path does not exist at all
/// — its refusal is the thing under test, not a capture.</para>
/// </summary>
public sealed class CameraCaptureTests
{
    // =====================================================================================
    //  The ladder
    // =====================================================================================

    /// <summary>
    /// The format ladder is DEFAULT FIRST and MJPG SECOND, which is upstream's order and upstream's
    /// reason: the default attempt runs first <i>"so cameras that already work are untouched"</i>
    /// (<c>Services/Webcam/WebcamTrackingService.cs:162-163</c>), and MJPG is escalated to only when
    /// the default feed is unusable (<c>:163-166</c>, BUG-F2XJE2E7X9).
    ///
    /// <para>Reverse the two and every working camera is renegotiated into MJPG for no reason, and
    /// the YUY2-only cameras upstream's comment names stop opening at all. Order is behaviour.</para>
    /// </summary>
    [Fact]
    public void TheFormatLadderIsUpstreamsORDER_DefaultFirstSoWorkingCamerasAreUntouched()
    {
        Assert.Equal(
            [CameraCaptureLadder.DefaultFormat, CameraCaptureLadder.MotionJpeg],
            CameraCaptureLadder.Order);

        // Two rungs, both named, and the names are stable strings a log and a support answer share.
        Assert.Equal(2, CameraCaptureLadder.Order.Count);
        Assert.Equal("default-format", CameraCaptureLadder.DefaultFormat);
        Assert.Equal("motion-jpeg", CameraCaptureLadder.MotionJpeg);
    }

    // =====================================================================================
    //  Frame acceptance — the arithmetic that decides whether a camera is working
    // =====================================================================================

    /// <summary>
    /// <b>A frame arriving is not a working camera.</b> Upstream learned this from an Elgato Facecam
    /// Neo that read 1240 non-empty frames containing zero faces
    /// (<c>Services/Webcam/WebcamTrackingService.cs:123-130</c>), and the rule it landed on has two
    /// independent acceptance paths, ORed (<c>:1410</c>).
    ///
    /// <para>This is the truth table of that rule against synthetic frames, and each row is a real
    /// camera someone hit:</para>
    ///
    /// <list type="bullet">
    /// <item>A textured frame clears the SPATIAL floor and is adopted — the ordinary case.</item>
    /// <item>A solid frame that never changes is REJECTED on both paths — the Elgato feed, and the
    /// only reason the spatial floor exists.</item>
    /// <item>A nearly-flat frame that CHANGES is adopted on the TEMPORAL path — a dark room with a
    /// person moving in it, which the spatial floor alone would have called a camera another
    /// application is holding (<c>:132-143</c>).</item>
    /// <item>Sensor-noise flicker on a static feed does NOT clear the temporal bar, which is the
    /// whole reason that bar is 2.0 and not lower (<c>:143</c>).</item>
    /// </list>
    ///
    /// <para><b>The OR must never become an AND</b>, and the last block checks that directly: a
    /// well-lit but perfectly still scene — most users sitting still — has real spatial variation and
    /// zero temporal change, and it has to be accepted.</para>
    /// </summary>
    [Fact]
    public void AFrameIsAcceptedOnSPATIALVariationORonTEMPORALChange_AndAFlatStaticFeedIsRejected()
    {
        // Upstream's constants, to the digit. A change here is a change to which cameras this
        // product will and will not open (Services/Webcam/WebcamTrackingService.cs:130, :143).
        Assert.Equal(3.0, CameraFrameProbe.MinStdDev);
        Assert.Equal(2.0, CameraFrameProbe.MinTemporalDelta);
        Assert.Equal(3000, CameraFrameProbe.WarmupMilliseconds);
        Assert.Equal(30, CameraFrameProbe.MaxConsecutiveReadFailures);

        var textured = Frame(64, 64, (x, y) => (byte)(((x * 8) + (y * 8)) % 256));
        var solid = Frame(64, 64, (_, _) => 17);
        var solidAgain = Frame(64, 64, (_, _) => 17);
        var dimStill = Frame(64, 64, (_, _) => 40);
        var dimMoved = Frame(64, 64, (x, _) => (byte)(x < 32 ? 40 : 45));
        var noisy = Frame(64, 64, (x, y) => (byte)(40 + ((x + y) % 2)));

        // A textured frame clears the spatial floor on its own.
        var texturedSpatial = CameraFrameProbe.MaxChannelStdDev(textured);
        Assert.True(texturedSpatial >= CameraFrameProbe.MinStdDev, $"textured stddev was {texturedSpatial}");
        Assert.True(CameraFrameProbe.Accepts(texturedSpatial, temporalDelta: 0));

        // THE ELGATO CASE: solid, and unchanged frame to frame. Rejected on BOTH paths.
        var solidSpatial = CameraFrameProbe.MaxChannelStdDev(solid);
        var solidTemporal = CameraFrameProbe.MaxChannelMeanAbsoluteDifference(solid, solidAgain);
        Assert.Equal(0, solidSpatial);
        Assert.Equal(0, solidTemporal);
        Assert.False(CameraFrameProbe.Accepts(solidSpatial, solidTemporal));

        // THE DARK ROOM: below the spatial floor, but it MOVES. Rescued by the temporal path, which
        // is what stops a dim-but-live camera being reported as one another application is holding.
        var dimSpatial = CameraFrameProbe.MaxChannelStdDev(dimMoved);
        var dimTemporal = CameraFrameProbe.MaxChannelMeanAbsoluteDifference(dimMoved, dimStill);
        Assert.True(dimSpatial < CameraFrameProbe.MinStdDev, $"the dim frame was not dim: {dimSpatial}");
        Assert.True(dimTemporal >= CameraFrameProbe.MinTemporalDelta, $"dim temporal delta was {dimTemporal}");
        Assert.True(CameraFrameProbe.Accepts(dimSpatial, dimTemporal));

        // SENSOR NOISE on a static feed does not clear the bar. Lower MinTemporalDelta and the
        // Elgato feed walks straight back in through the path added to rescue dark rooms.
        var noiseTemporal = CameraFrameProbe.MaxChannelMeanAbsoluteDifference(noisy, dimStill);
        Assert.True(noiseTemporal < CameraFrameProbe.MinTemporalDelta, $"noise delta was {noiseTemporal}");

        // THE OR IS NOT AN AND: a well-lit, perfectly still scene has spatial variation and zero
        // motion, and it is most users sitting still.
        Assert.True(CameraFrameProbe.Accepts(maxStdDev: 12.0, temporalDelta: 0));
        Assert.True(CameraFrameProbe.Accepts(maxStdDev: 0, temporalDelta: 9.0));
        Assert.False(CameraFrameProbe.Accepts(maxStdDev: 2.99, temporalDelta: 1.99));

        // Truncated or absent pixels are never a working camera: zero is below every bar.
        Assert.Equal(0, CameraFrameProbe.MaxChannelStdDev([]));
        Assert.Equal(0, CameraFrameProbe.MaxChannelMeanAbsoluteDifference(textured, []));

        // Two frames of DIFFERENT sizes never produce a delta — upstream compares Size() and Type()
        // before differencing (:1403), because a mid-warm-up format change would otherwise read as
        // enormous motion and adopt a feed nobody has looked at.
        Assert.Equal(0, CameraFrameProbe.MaxChannelMeanAbsoluteDifference(textured, Frame(32, 32, (_, _) => 200)));
    }

    // =====================================================================================
    //  The gate above the capture
    // =====================================================================================

    /// <summary>
    /// <b>NO ENGINE AND NO CONSENT EACH STOP A CAMERA BEING OPENED, and neither of them even
    /// enumerates one.</b>
    ///
    /// <para>The two refusals are separate arms with separate codes, in
    /// <see cref="CameraCapability.Classify"/>'s order, and the order is the product behaviour: a
    /// user told to work through a consent flow for a feature that has no engine has been sent to
    /// spend a privacy decision on nothing.</para>
    ///
    /// <para><b>What makes this a fact and not a claim is the pair of counters.</b> The doubles count
    /// every ask, and both stay at zero through a start attempt with no engine, a start attempt with
    /// no consent, and the grant itself. Move the consent rung above the engine rung, or let the
    /// grant warm the camera up, and this fails.</para>
    /// </summary>
    [Fact]
    public async Task NoEngineAndNoConsentEACHRefuseBeforeADeviceIsEnumerated_LetAloneOpened()
    {
        // No engine: the product's real state on every build.
        var (participant, directory, route, capture) = NewParticipant(engineAdmitted: false);
        try
        {
            await participant.StartAsync(TestContext.Current.CancellationToken);
            var noEngine = Assert.IsType<CapabilityState.Unavailable>(
                await participant.StartCaptureAsync(null, TestContext.Current.CancellationToken));
            Assert.Equal(CameraReasonCodes.CameraNoEngine, noEngine.Reason.Code);
            Assert.Equal(0, route.Asks);
            Assert.Equal(0, capture.Opens);
            Assert.Equal(0, participant.CameraOpenAttempts);
            Assert.False(participant.CaptureRunning);
        }
        finally
        {
            await participant.StopAsync();
            Delete(directory);
        }

        // An engine, but no consent: the next rung down, and still nothing is asked of any camera.
        var (consenting, consentDirectory, consentRoute, consentCapture) = NewParticipant(engineAdmitted: true);
        try
        {
            await consenting.StartAsync(TestContext.Current.CancellationToken);
            var noConsent = Assert.IsType<CapabilityState.Unavailable>(
                await consenting.StartCaptureAsync(null, TestContext.Current.CancellationToken));
            Assert.Equal(CameraReasonCodes.CameraConsentAbsent, noConsent.Reason.Code);
            Assert.Equal(0, consentRoute.Asks);
            Assert.Equal(0, consentCapture.Opens);

            // THE GRANT ITSELF OPENS NOTHING. Upstream says the same in as many words — "Persist
            // consent. Camera stays closed" (Dialogs/WebcamConsentDialog.xaml.cs:142-143).
            Assert.True(await consenting.GrantConsentAsync(
                new CameraConsentRequest(true, true, true, CameraConsent.ConfirmationWord),
                DateTimeOffset.UnixEpoch));
            Assert.Equal(0, consentRoute.Asks);
            Assert.Equal(0, consentCapture.Opens);
            Assert.Equal(0, consenting.CameraOpenAttempts);
            Assert.False(consenting.CaptureRunning);
        }
        finally
        {
            await consenting.StopAsync();
            Delete(consentDirectory);
        }
    }

    // =====================================================================================
    //  Open, stream, release, re-open
    // =====================================================================================

    /// <summary>
    /// An EXPLICIT start opens a camera, frames flow, and stopping RELEASES the device — and the
    /// device can then be opened again, which is the only way "released" means anything.
    ///
    /// <para><b>The pump is bounded by upstream's own consecutive-failure budget</b>
    /// (<c>Services/Webcam/WebcamTrackingService.cs:120</c>, <c>MaxConsecutiveReadFails</c>, "~1s at
    /// 30fps"): a device that stops delivering ends the pump rather than spinning on it forever. The
    /// budget is CONSECUTIVE and never cumulative, and the second block proves that distinction —
    /// a camera that drops one frame in three all session is working, and this pump must deliver
    /// every frame it was asked for.</para>
    /// </summary>
    [Fact]
    public async Task AnExplicitStartOPENSTheCamera_FramesFlow_AndStoppingRELEASESItSoItCanOpenAgain()
    {
        var (participant, directory, route, capture) = NewParticipant(
            engineAdmitted: true,
            inventory: CameraInventory.Named("recording route", [Device("one")]));
        try
        {
            await participant.StartAsync(TestContext.Current.CancellationToken);
            Assert.True(await participant.GrantConsentAsync(
                new CameraConsentRequest(true, true, true, CameraConsent.ConfirmationWord),
                DateTimeOffset.UnixEpoch));

            var open = Assert.IsType<CapabilityState.Degraded>(
                await participant.StartCaptureAsync(null, TestContext.Current.CancellationToken));
            Assert.Equal(CameraReasonCodes.CameraOpen, open.Reason.Code);
            Assert.True(participant.CaptureRunning);
            Assert.Equal(1, participant.CameraOpenAttempts);
            Assert.Equal(1, capture.Opens);

            // NEVER Available. An open camera with no engine looking through it delivers none of the
            // feature a user asked for, and saying otherwise is the fake-available shape the
            // truthful-capability contract bans.
            Assert.IsNotType<CapabilityState.Available>(open);
            Assert.Contains("NO GAZE IS BEING TRACKED", open.Reason.Detail, StringComparison.Ordinal);

            Assert.Equal(24, await participant.PumpAsync(24, TestContext.Current.CancellationToken));
            Assert.Equal(24, participant.FramesRead);

            await participant.StopCaptureAsync();
            Assert.False(participant.CaptureRunning);
            Assert.Equal(1, capture.Closes);

            // RE-OPEN. If the stop had not really let the device go, this is where it would show.
            Assert.IsType<CapabilityState.Degraded>(
                await participant.StartCaptureAsync(null, TestContext.Current.CancellationToken));
            Assert.True(participant.CaptureRunning);
            Assert.Equal(2, participant.CameraOpenAttempts);

            // A camera that drops one read in three is WORKING: the failure budget is consecutive,
            // so every frame asked for still arrives.
            capture.FailEveryThirdRead = true;
            Assert.Equal(12, await participant.PumpAsync(12, TestContext.Current.CancellationToken));

            // A camera that has GONE ends the pump inside the budget rather than spinning forever,
            // and it stops at EXACTLY the budget: a thousand frames were asked for and thirty reads
            // answered the question.
            capture.FailEveryRead = true;
            var readsBeforeTheDeadPump = capture.Reads;
            Assert.Equal(0, await participant.PumpAsync(1000, TestContext.Current.CancellationToken));
            Assert.Equal(
                CameraFrameProbe.MaxConsecutiveReadFailures,
                capture.Reads - readsBeforeTheDeadPump);

            // Participant stop releases the device even when nothing called StopCaptureAsync: a
            // shutdown that flushed a file and left the indicator lit is the one failure a user
            // cannot diagnose.
            await participant.StopAsync();
            Assert.False(participant.CaptureRunning);
            Assert.Equal(2, capture.Closes);
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>
    /// <b>WITHDRAWING CONSENT STOPS A RUNNING CAMERA.</b> It does not merely make the next start
    /// refuse.
    ///
    /// <para>Upstream stops the service before it clears the consent fields
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1057-1068</c>) and the order is the behaviour:
    /// somebody who changes their mind while the camera is on means NOW. A revoke that wrote a file
    /// and left the device open would leave the indicator lit by an application that has just
    /// promised to stop.</para>
    /// </summary>
    [Fact]
    public async Task WithdrawingConsentRELEASESAnOpenCamera_RatherThanOnlyRefusingTheNextStart()
    {
        var (participant, directory, _, capture) = NewParticipant(
            engineAdmitted: true,
            inventory: CameraInventory.Named("recording route", [Device("one")]));
        try
        {
            await participant.StartAsync(TestContext.Current.CancellationToken);
            Assert.True(await participant.GrantConsentAsync(
                new CameraConsentRequest(true, true, true, CameraConsent.ConfirmationWord),
                DateTimeOffset.UnixEpoch));
            Assert.IsType<CapabilityState.Degraded>(
                await participant.StartCaptureAsync(null, TestContext.Current.CancellationToken));
            Assert.True(participant.CaptureRunning);

            await participant.RevokeConsentAsync();

            Assert.False(participant.CaptureRunning);
            Assert.Equal(1, capture.Closes);
            Assert.False(participant.Consent.Current.Granted);
            Assert.Null(participant.LastInventory);

            // And the next start refuses at the consent rung, having touched nothing.
            var refused = Assert.IsType<CapabilityState.Unavailable>(
                await participant.StartCaptureAsync(null, TestContext.Current.CancellationToken));
            Assert.Equal(CameraReasonCodes.CameraConsentAbsent, refused.Reason.Code);
            Assert.Equal(1, capture.Opens);
        }
        finally
        {
            await participant.StopAsync();
            Delete(directory);
        }
    }

    /// <summary>
    /// A start that NAMES a camera which is not in the roster is REFUSED. It never opens whichever
    /// other camera happens to be attached.
    ///
    /// <para>Substituting would be indistinguishable, from in front of the machine, from this product
    /// choosing a camera at random — and the camera somebody did not pick may be the one pointing at
    /// the rest of the room. The typed refusal names the state instead
    /// (<see cref="CameraReasonCodes.CameraDeviceNotMatched"/>), and no open is attempted at all.</para>
    /// </summary>
    [Fact]
    public async Task AStartThatNamesAnAbsentCameraREFUSES_AndNeverSubstitutesTheOneThatIsThere()
    {
        var (participant, directory, _, capture) = NewParticipant(
            engineAdmitted: true,
            inventory: CameraInventory.Named("recording route", [Device("present")]));
        try
        {
            await participant.StartAsync(TestContext.Current.CancellationToken);
            Assert.True(await participant.GrantConsentAsync(
                new CameraConsentRequest(true, true, true, CameraConsent.ConfirmationWord),
                DateTimeOffset.UnixEpoch));

            var missing = Assert.IsType<CapabilityState.DependencyMissing>(
                await participant.StartCaptureAsync(
                    @"@device:pnp:\\?\usb#vid_ffff&pid_0000#absent#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global",
                    TestContext.Current.CancellationToken));

            Assert.Equal(CameraReasonCodes.CameraDeviceNotMatched, missing.Reason.Code);
            Assert.Equal(0, capture.Opens);
            Assert.False(participant.CaptureRunning);

            // The camera that IS there opens when it is the one named — the same key, matched.
            Assert.IsType<CapabilityState.Degraded>(await participant.StartCaptureAsync(
                Device("present").StableId, TestContext.Current.CancellationToken));
            Assert.Equal(1, capture.Opens);
        }
        finally
        {
            await participant.StopAsync();
            Delete(directory);
        }
    }

    // =====================================================================================
    //  Identity: a device path, never an index
    // =====================================================================================

    /// <summary>
    /// A DirectShow moniker and a Media Foundation symbolic link for ONE camera resolve to the SAME
    /// hardware key, even though they carry different device-interface GUIDs.
    ///
    /// <para><b>This is what replaces upstream's camera index.</b> Upstream opens an integer its own
    /// comments warn is not guaranteed to mean the camera the dropdown showed —
    /// <i>"usually identical on a typical system, but is not guaranteed"</i>
    /// (<c>Services/Webcam/WebcamDeviceEnumerator.cs:15-21</c>) — and carries a runtime warning for
    /// when the remembered index and the remembered name disagree
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1243-1249</c>).
    /// <c>client/docs/capability-inventory.md</c> says <i>"Never use only a transient camera
    /// index"</i>, and the way this port obeys that is by not having one.</para>
    ///
    /// <para><b>Refusing to guess is half the fact.</b> A string with no device path yields no key
    /// at all, and no key never matches — because a wrong match here opens somebody's other camera.</para>
    /// </summary>
    [Fact]
    public void ADirectShowMonikerAndAMediaFoundationSymbolicLinkResolveToTheSameHARDWARE_NeverToAnIndex()
    {
        const string instance = @"usb#vid_04f2&pid_b82a&mi_00#7&23ad3411&1&0000";
        var directShow = $@"@device:pnp:\\?\{instance}#{{65e8773d-8f56-11d0-a3b9-00a0c9223196}}\global";
        var mediaFoundation = $@"\\?\{instance}#{{e5323777-f976-4f5b-9b55-b94699c46e44}}\global";

        Assert.Equal(instance.ToLowerInvariant(), CameraHardwareKey.Of(directShow));
        Assert.Equal(instance.ToLowerInvariant(), CameraHardwareKey.Of(mediaFoundation));
        Assert.True(CameraHardwareKey.Matches(directShow, mediaFoundation));
        Assert.True(CameraHardwareKey.Matches(mediaFoundation, directShow));

        // Case is not identity: the same device path in either case is the same camera.
        Assert.True(CameraHardwareKey.Matches(directShow.ToUpperInvariant(), mediaFoundation));

        // A DIFFERENT camera on the same bus does not match. This is the assertion that would fail if
        // the key were ever shortened to a vendor or a bus prefix.
        var sibling = $@"\\?\usb#vid_04f2&pid_b82a&mi_00#7&99999999&1&0000#{{e5323777-f976-4f5b-9b55-b94699c46e44}}\global";
        Assert.False(CameraHardwareKey.Matches(directShow, sibling));

        // NO KEY NEVER MATCHES, including against itself: a string this cannot parse is a refusal to
        // guess, never a wildcard.
        Assert.Null(CameraHardwareKey.Of(null));
        Assert.Null(CameraHardwareKey.Of("   "));
        Assert.Null(CameraHardwareKey.Of(@"\\?\#{65e8773d-8f56-11d0-a3b9-00a0c9223196}"));
        Assert.False(CameraHardwareKey.Matches(null, null));
        Assert.False(CameraHardwareKey.Matches("0", "0000"));
    }

    // =====================================================================================
    //  Platforms: what exists, and what honestly does not
    // =====================================================================================

    /// <summary>
    /// <b>LINUX HAS NO CAPTURE PATH IN THIS BUILD, AND SAYS SO.</b> It does not report a broken
    /// camera, and it is not a stub that would compile here and then fail on a real machine.
    ///
    /// <para>The refusal NAMES what is missing — <c>VIDIOC_STREAMON</c>, <c>mmap</c>, and the XDG
    /// Camera portal's PipeWire node inside a sandbox — because a silent failure on this path is
    /// indistinguishable from a camera that does not work, and would send a Linux user to buy
    /// hardware they already own. It also says out loud that the gap is in the port rather than in
    /// their machine.</para>
    ///
    /// <para><c>docs/constitution.md</c>: a successful build never establishes Linux support. A V4L2
    /// streaming path could be written today; no machine available to this port has a V4L2 device to
    /// run it against, and hand-laid-out ioctl structures that have never executed against a kernel
    /// are a compile-time claim rather than a capability.</para>
    /// </summary>
    [Fact]
    public void LinuxHasNOCaptureRouteAndNamesWhatIsMissing_AndTheWindowsRouteRefusesOffWindowsToo()
    {
        using var linux = CameraDeviceSourceFactory.CaptureFor(CameraHostPlatform.Linux);
        Assert.False(linux.IsOpen);
        Assert.Equal(0, linux.FramesRead);
        Assert.Null(linux.AdoptedRung);
        Assert.False(linux.ReadFrame(TestContext.Current.CancellationToken));

        // Empty until Open is called, and never empty afterwards — the invariant that lets a
        // launch-time fact ask the SEAM "were you asked?" instead of trusting a caller's own tally.
        // A caller's tally is exactly what a mutation walked straight past.
        Assert.Empty(linux.AttemptedRungs);

        var refusal = Assert.IsType<CapabilityState.DependencyMissing>(
            linux.Open(Device("anything"), TestContext.Current.CancellationToken));
        Assert.Equal(CameraReasonCodes.CameraCaptureUnsupported, refusal.Reason.Code);
        Assert.Contains("V4L2", refusal.Dependency, StringComparison.Ordinal);
        Assert.Contains("VIDIOC_STREAMON", refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("XDG Camera portal", refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("NO LINUX CAPTURE PATH", refusal.Reason.Detail, StringComparison.Ordinal);

        // It is never read as a fact about the user's hardware.
        Assert.NotEqual(CameraReasonCodes.CameraNoDevice, refusal.Reason.Code);
        Assert.NotEqual(CameraReasonCodes.CameraOpenFailed, refusal.Reason.Code);
        Assert.Contains("NOT a statement about your hardware", refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.NotEmpty(linux.AttemptedRungs);

        // macOS and an unknown platform name themselves rather than reporting an absent camera.
        using var mac = CameraDeviceSourceFactory.CaptureFor(CameraHostPlatform.MacOs);
        var macRefusal = Assert.IsType<CapabilityState.DependencyMissing>(
            mac.Open(Device("anything"), TestContext.Current.CancellationToken));
        Assert.Equal(CameraReasonCodes.CameraCaptureUnsupported, macRefusal.Reason.Code);

        using var unknown = CameraDeviceSourceFactory.CaptureFor(CameraHostPlatform.Unknown);
        Assert.IsType<CapabilityState.DependencyMissing>(
            unknown.Open(Device("anything"), TestContext.Current.CancellationToken));

        // The Windows arm is CONSTRUCTIBLE from any platform and opens nothing until asked. It is
        // never opened here: a suite that opened this machine's camera would light its owner's
        // indicator on every floor run. The hardware evidence is client/spikes/CcpSpike.Camera.
        using var windows = CameraDeviceSourceFactory.CaptureFor(CameraHostPlatform.Windows);
        Assert.False(string.IsNullOrWhiteSpace(windows.Backend));
        Assert.False(windows.IsOpen);
        Assert.Equal(0, windows.FramesRead);
        Assert.Empty(windows.AttemptedRungs);
    }

    // =====================================================================================
    //  Helpers
    // =====================================================================================

    /// <summary>A synthetic RGB32 frame. Every channel of a pixel is set to the same value, which is
    /// what makes the per-channel maximum equal to the value the row is reasoning about.</summary>
    private static byte[] Frame(int width, int height, Func<int, int, byte> value)
    {
        var frame = new byte[width * height * CameraFrameProbe.BytesPerPixel];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * CameraFrameProbe.BytesPerPixel;
                frame[offset] = value(x, y);
                frame[offset + 1] = value(x, y);
                frame[offset + 2] = value(x, y);
                frame[offset + 3] = 255;
            }
        }

        return frame;
    }

    private static CameraDevice Device(string tag) => new(
        $@"@device:pnp:\\?\usb#vid_04f2&pid_b82a&mi_00#7&{tag}&1&0000#{{65e8773d-8f56-11d0-a3b9-00a0c9223196}}\global",
        "Integrated Camera",
        IdentityIsStable: true);

    private static void Delete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was written; there is nothing to clean up.
        }
    }

    private static (CameraParticipant Participant, string Directory, RecordingRoute Route, RecordingCapture Capture)
        NewParticipant(bool engineAdmitted, CameraInventory? inventory = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ccp-camera-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var route = new RecordingRoute(inventory ?? CameraInventory.Named("recording route", []));
        var capture = new RecordingCapture();
        var infra = new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), new NullLog());
        return (
            new CameraParticipant(infra, directory, route, engineAdmitted, capture),
            directory,
            route,
            capture);
    }

    /// <summary>An enumeration route that counts every ask, so "nothing was asked of any camera" is an
    /// assertion about a number rather than a claim about unexecuted code.</summary>
    private sealed class RecordingRoute(CameraInventory answer) : ICameraDeviceSource
    {
        public int Asks { get; private set; }

        public string Route => answer.Route;

        public CameraInventory Enumerate()
        {
            Asks++;
            return answer;
        }
    }

    /// <summary>
    /// A capture route that counts opens, reads and closes and touches no hardware. It is what lets
    /// every fact above be about the GATE and the ORDER without a camera ever being started.
    /// </summary>
    private sealed class RecordingCapture : ICameraCaptureSource
    {
        private int _readsSinceOpen;

        public int Opens { get; private set; }

        public int Reads { get; private set; }

        public int Closes { get; private set; }

        public bool FailEveryRead { get; set; }

        public bool FailEveryThirdRead { get; set; }

        public string Backend => "recording capture (no device)";

        public bool IsOpen { get; private set; }

        public int Width => IsOpen ? 640 : 0;

        public int Height => IsOpen ? 480 : 0;

        public string? AdoptedRung => IsOpen ? CameraCaptureLadder.DefaultFormat : null;

        public IReadOnlyList<string> AttemptedRungs => IsOpen ? [CameraCaptureLadder.DefaultFormat] : [];

        public int FramesRead { get; private set; }

        public CapabilityState? Open(CameraDevice device, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Opens++;
            IsOpen = true;
            FramesRead = 0;
            _readsSinceOpen = 0;
            return null;
        }

        public bool ReadFrame(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            _readsSinceOpen++;
            if (FailEveryRead || (FailEveryThirdRead && _readsSinceOpen % 3 == 0))
            {
                return false;
            }

            FramesRead++;
            return true;
        }

        public void Close()
        {
            if (IsOpen)
            {
                Closes++;
            }

            IsOpen = false;
        }

        public void Dispose() => Close();
    }

    private sealed class NullLog : ILogSink
    {
        public void Log(string message)
        {
            // The log lines are asserted in CameraCapabilityTests; here they are noise.
        }
    }
}
