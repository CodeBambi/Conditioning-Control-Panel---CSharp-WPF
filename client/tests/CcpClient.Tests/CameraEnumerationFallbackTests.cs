using CcpClient.Desktop.Camera;
using CcpClient.Desktop.Capabilities;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>THE SECOND ENUMERATION WALK: when it runs, when it must not, and what its answer is allowed to
/// turn into.</b>
///
/// <para>Upstream serves an MF-only or 32-bit-only camera with TWO separate things, and slice 1 of
/// this port had only one of them. Upstream's MSMF capture rungs OPEN such a camera
/// (<c>Services/Webcam/WebcamTrackingService.cs:167-172</c>) and a WinRT enumerator SEES it, because
/// <i>"a 64-bit process misses cameras that register only 32-bit DirectShow filters or are
/// Media-Foundation-only, so DirectShow returns an empty list even though Discord / Windows Camera /
/// OpenCV-MSMF open the device fine"</i>
/// (<c>Services/Webcam/WebcamWinRtEnumerator.cs:13-17</c>, issues #282/#279/#291). Making Media
/// Foundation the port's ONLY capture transport closed the first half by inversion and left the
/// second half exactly where it was: the roster still came from DirectShow alone, so a camera only
/// Media Foundation can see was still a camera this product reported as absent.</para>
///
/// <para><b>What is under test here is the RULE, not the interop.</b> Every fact below drives
/// synthetic inventories and a counting thunk, so no Media Foundation call is made, no camera is
/// enumerated for real and no camera is opened. That the real
/// <see cref="MediaFoundationCameraCapture.EnumerateDevices"/> walk names this machine's camera with
/// a durable symbolic-link identity was established separately, by a run against real hardware; no
/// assertion in this file stands in for that, and none of them should be read as doing so. The real
/// route for whatever machine the suite is on is exercised by
/// <c>CameraCapabilityTests.TheRouteForTHISMachineReallyRuns_AndAnswersTypedWithoutOpeningACamera</c>.</para>
///
/// <para><b>No Linux claim is made anywhere here.</b> The second walk is Media Foundation and
/// therefore Windows-only; Linux enumerates from the kernel device class and has no capture path at
/// all.</para>
/// </summary>
public sealed class CameraEnumerationFallbackTests
{
    /// <summary>
    /// <b>THE SECOND WALK IS ASKED ONLY WHEN THE FIRST REALLY RAN AND NAMED NOTHING</b> — never when
    /// it found a camera, and never when it refused.
    ///
    /// <para>Upstream's trigger is <c>if (devices.Count == 0)</c> and nothing else
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1120-1134</c>). Second-guessing a route that
    /// already named a camera costs a Media Foundation platform start-up on every launch probe and
    /// can only add duplicates of what is already there.</para>
    ///
    /// <para><b>The refusal leg is the one that matters, and it diverges from upstream.</b> Upstream
    /// returns an empty list for a failed walk (<c>Services/Webcam/WebcamDeviceEnumerator.cs:119-122</c>)
    /// and has no privacy read at all, so a denied user and a user with no webcam are the same event
    /// to it. Here a Windows Camera privacy denial is a typed
    /// <see cref="CapabilityState.PermissionRequired"/>, and running a second walk over the top of it
    /// would turn "you have denied camera access" into a device roster — evidence moving in the one
    /// direction <c>runtime-capability-contract.md</c> §2 rule 4 forbids.</para>
    /// </summary>
    [Fact]
    public void TheSecondWalkIsAskedONLYWhenTheFirstReallyRanAndNamedNothing()
    {
        var asks = 0;
        CameraInventory NeverExpected()
        {
            asks++;
            return CameraInventory.Named("the second walk", [Device("smuggled")]);
        }

        // A walk that found a camera is not second-guessed, and the answer comes back untouched.
        var found = CameraInventory.Named(Route, [Device("one")]);
        Assert.Same(found, CameraEnumerationFallback.Apply(Route, found, NeverExpected));
        Assert.Equal(0, asks);

        // A DENIED walk is not second-guessed either, and the denial survives to the classification.
        var denied = CameraInventory.Refusing(Route, new CapabilityState.PermissionRequired(
            new CapabilityReason(CameraReasonCodes.CameraPermissionDenied, "camera access is set to Deny")));
        var afterDenial = CameraEnumerationFallback.Apply(Route, denied, NeverExpected);
        Assert.Same(denied, afterDenial);
        Assert.Empty(afterDenial.Devices);
        Assert.Equal(0, asks);

        var classified = Assert.IsType<CapabilityState.PermissionRequired>(
            CameraCapability.Classify(engineAdmitted: true, consentRefusal: null, afterDenial));
        Assert.Equal(CameraReasonCodes.CameraPermissionDenied, classified.Reason.Code);

        // And a walk that THREW is a refusal too: "the route failed" is not "there is no camera",
        // so it is not an invitation to go looking with a different one either.
        var faulted = CameraInventory.Refusing(Route, new CapabilityState.Faulted(
            new CapabilityReason(CameraReasonCodes.CameraEnumerationFailed, "the walk threw")));
        Assert.Same(faulted, CameraEnumerationFallback.Apply(Route, faulted, NeverExpected));
        Assert.Equal(0, asks);
    }

    /// <summary>
    /// <b>AN EMPTY FIRST WALK IS SECOND-GUESSED, and the cameras the second walk names are the
    /// roster.</b> This is the whole point: the machine where DirectShow returns nothing and Media
    /// Foundation returns a webcam is the machine upstream filed three issues about.
    ///
    /// <para>They come back under the SOURCE's route rather than the second walk's, because
    /// <see cref="ICameraDeviceSource.Route"/> is what a user reads and it names both walks. A route
    /// string that changed shape depending on which walk answered would also break the equality
    /// <c>CameraCapabilityTests</c> pins between a source's route and its inventory's.</para>
    /// </summary>
    [Fact]
    public void AnEmptyFirstWalkIsSecondGuessed_AndTheCamerasItMissedBecomeTheRoster()
    {
        var asks = 0;
        var fallback = CameraInventory.Named("the second walk", [Device("mf-only"), Device("another")]);

        var inventory = CameraEnumerationFallback.Apply(
            Route,
            CameraInventory.Named(Route, []),
            () =>
            {
                asks++;
                return fallback;
            });

        Assert.Equal(1, asks);
        Assert.Equal(Route, inventory.Route);
        Assert.Null(inventory.Refusal);
        Assert.Equal(["another", "mf-only"], inventory.Devices.Select(device => device.DisplayName).Order());

        // A named device is a roster, and a roster still never reaches Available: this build has no
        // gaze engine to look through any of them.
        var state = Assert.IsType<CapabilityState.Degraded>(
            CameraCapability.Classify(engineAdmitted: true, consentRefusal: null, inventory));
        Assert.Equal(CameraReasonCodes.CameraNotOpened, state.Reason.Code);
        Assert.IsNotType<CapabilityState.Available>(state);
    }

    /// <summary>
    /// <b>BOTH WALKS NAMING NOTHING IS STILL "NO CAMERA"</b>, and the route a user is shown says two
    /// walks looked.
    ///
    /// <para><see cref="CameraReasonCodes.CameraNoDevice"/> may be said only when a real route ran and
    /// named nothing, which is exactly this case — twice over. The Windows route's own
    /// <see cref="ICameraDeviceSource.Route"/> is checked here against the same standard: it names
    /// both walks, so the "no video-capture device" sentence built from it tells somebody that
    /// DirectShow AND Media Foundation both looked, rather than implying one route's blind spot is
    /// the whole answer.</para>
    /// </summary>
    [Fact]
    public void BothWalksNamingNothingIsStillNoDevice_AndTheRouteSaysBothLooked()
    {
        var inventory = CameraEnumerationFallback.Apply(
            Route,
            CameraInventory.Named(Route, []),
            () => CameraInventory.Named("the second walk", []));

        Assert.Empty(inventory.Devices);
        Assert.Null(inventory.Refusal);

        var missing = Assert.IsType<CapabilityState.DependencyMissing>(
            CameraCapability.Classify(engineAdmitted: true, consentRefusal: null, inventory));
        Assert.Equal(CameraReasonCodes.CameraNoDevice, missing.Reason.Code);
        Assert.Contains("Plug a webcam in", missing.Reason.Detail, StringComparison.Ordinal);

        // The route the product really uses names both walks. Read from the factory rather than
        // constructed, because the Windows COM type must never be built off Windows — and the
        // off-Windows twin carries the same string, so this holds on either machine.
        var windows = CameraDeviceSourceFactory.For(CameraHostPlatform.Windows).Route;
        Assert.Contains("SystemDeviceEnum", windows, StringComparison.Ordinal);
        Assert.Contains("MFEnumDeviceSources", windows, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A SECOND WALK THAT COULD NOT RUN IS NOT ROUNDED DOWN TO "NO CAMERA".</b>
    ///
    /// <para>Upstream returns the empty DirectShow list when its fallback comes back with nothing
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1129-1142</c>), which collapses "Media Foundation
    /// is not installed on this Windows edition" into "plug a webcam in". This port keeps them apart,
    /// because <see cref="ICameraDeviceSource.Enumerate"/> says in as many words that <i>"the route
    /// threw" and "there is no camera" are the two facts this capability exists to keep apart</i>, and
    /// because sending an N-edition user to buy a webcam fixes nothing.</para>
    /// </summary>
    [Fact]
    public void ASecondWalkThatCouldNotRunIsNOTRoundedDownToNoCamera()
    {
        var inventory = CameraEnumerationFallback.Apply(
            Route,
            CameraInventory.Named(Route, []),
            () => CameraInventory.Refusing("the second walk", new CapabilityState.DependencyMissing(
                "the Media Foundation platform (mfplat.dll)",
                new CapabilityReason(
                    CameraReasonCodes.CameraEnumerationUnsupported,
                    "Media Foundation would not start on this Windows installation"))));

        // The refusal survives, restamped with the route the user reads.
        Assert.Equal(Route, inventory.Route);
        Assert.Empty(inventory.Devices);
        var refusal = Assert.IsType<CapabilityState.DependencyMissing>(inventory.Refusal);
        Assert.Equal(CameraReasonCodes.CameraEnumerationUnsupported, refusal.Reason.Code);

        // And the classification says the same thing rather than "no camera".
        var classified = Assert.IsType<CapabilityState.DependencyMissing>(
            CameraCapability.Classify(engineAdmitted: true, consentRefusal: null, inventory));
        Assert.Equal(CameraReasonCodes.CameraEnumerationUnsupported, classified.Reason.Code);
        Assert.DoesNotContain("Plug a webcam in", classified.Reason.Detail, StringComparison.Ordinal);
    }

    private const string Route = "the first walk, then the second walk when it names none";

    private static CameraDevice Device(string name) =>
        new($@"\\?\usb#vid_0000&pid_{name.GetHashCode(StringComparison.Ordinal):x4}#{{e5323777-f976-4f5b-9b55-b94699c46e44}}",
            name, IdentityIsStable: true);
}
