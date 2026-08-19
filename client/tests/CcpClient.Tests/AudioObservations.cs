using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Tests;

/// <summary>
/// Runs the real audio presence through a full lifecycle and records, side by side, <b>what the
/// presence claimed</b> and <b>what the Windows audio engine independently reports</b> (via
/// <see cref="WasapiRenderProbe"/>).
///
/// <para>Keeping both in one record is what lets the facts assert
/// <c>Assert.Equal(machineFact, osFact)</c> at statement depth 0 — no conditional, no early return,
/// nothing that can silence an assertion. The claim and the effect are separate fields precisely so
/// a presence that returned success without doing the work shows up as an inequality rather than as
/// a green test. This is <see cref="TrayObservations"/>' shape, for the same reason.</para>
/// </summary>
internal static class AudioObservations
{
    /// <summary>Open the device, observe, cue a real clip, observe while it renders, stop, dispose,
    /// observe again.</summary>
    internal static LifecycleRun FullLifecycle(string clipPath, Func<Func<bool>, string, Task> waitUntil)
    {
        ArgumentNullException.ThrowIfNull(waitUntil);

        var beforeAnything = WasapiRenderProbe.SessionForThisProcess();
        var presence = new WasapiAudioPresence(
            new SoundFlowAudioBackend(_ => { }), _ => { });
        try
        {
            var open = presence.Open();
            var afterOpen = WasapiRenderProbe.SessionForThisProcess();
            var claimedRenderingAfterOpen = presence.IsRendering;

            // THE NEGATIVE CONTROL FOR THE METER, and it is what stops the peak fact below from
            // passing vacuously: the device is open and started, and nothing has been cued, so the
            // OS's own meter on this process's stream must read ZERO. Measured before it was relied
            // on (plan.md §0).
            var peakWithDeviceOpenAndNothingCued = afterOpen.Peak;

            var cue = presence.Cue(new AudioCue("observation", clipPath, 0.9f));

            float peakWhileRendering = 0;
            var sawActiveWhileRendering = false;
            var peakWentNonZero = false;
            try
            {
                waitUntil(
                    () =>
                    {
                        var fact = WasapiRenderProbe.SessionForThisProcess();
                        sawActiveWhileRendering |= fact.Active;
                        if (fact.Peak > peakWhileRendering)
                        {
                            peakWhileRendering = fact.Peak;
                        }

                        peakWentNonZero = peakWhileRendering > 0f;
                        return peakWentNonZero;
                    },
                    "the OS's own peak meter to read a non-zero level on this process's audio stream")
                    .GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // The window expired. NOT swallowed into a pass: peakWentNonZero stays false and the
                // fact that reads it fails against the machine's own endpoint count.
            }

            var silence = presence.Silence("observation");
            presence.Dispose();
            var afterDispose = WasapiRenderProbe.SessionForThisProcess();

            return new LifecycleRun(
                MachineRenderEndpoints: WasapiRenderProbe.ActiveRenderEndpointCount(),
                OsHeldSessionBeforeAnything: beforeAnything.SessionForThisProcess,
                OsHeldACTIVESessionBeforeAnything: beforeAnything.Active,
                OpenState: open,
                ClaimedAvailableOnOpen: open is CapabilityState.Available,
                OsHeldActiveSessionAfterOpen: afterOpen.Active,
                ClaimedRenderingAfterOpen: claimedRenderingAfterOpen,
                PeakWithDeviceOpenAndNothingCued: peakWithDeviceOpenAndNothingCued,
                CueState: cue,
                ClaimedAvailableOnCue: cue is CapabilityState.Available,
                OsSawActiveSessionWhileRendering: sawActiveWhileRendering,
                OsMeteredNonZeroPeakWhileRendering: peakWentNonZero,
                PeakWhileRendering: peakWhileRendering,
                ClaimedAvailableOnSilence: silence is CapabilityState.Available,
                OsSessionStillActiveAfterDispose: afterDispose.Active,
                ClaimedRenderingAfterDispose: presence.IsRendering);
        }
        finally
        {
            presence.Dispose();
        }
    }

    /// <summary>One run's two columns: what the product said, and what the OS said.</summary>
    internal sealed record LifecycleRun(
        int MachineRenderEndpoints,
        bool OsHeldSessionBeforeAnything,
        bool OsHeldACTIVESessionBeforeAnything,
        CapabilityState OpenState,
        bool ClaimedAvailableOnOpen,
        bool OsHeldActiveSessionAfterOpen,
        bool ClaimedRenderingAfterOpen,
        float PeakWithDeviceOpenAndNothingCued,
        CapabilityState CueState,
        bool ClaimedAvailableOnCue,
        bool OsSawActiveSessionWhileRendering,
        bool OsMeteredNonZeroPeakWhileRendering,
        float PeakWhileRendering,
        bool ClaimedAvailableOnSilence,
        bool OsSessionStillActiveAfterDispose,
        bool ClaimedRenderingAfterDispose)
    {
        /// <summary>The machine can render audio at all — the property that flips every honest
        /// expectation below, established by the probe rather than taken from the product.</summary>
        internal bool MachineCanRender => MachineRenderEndpoints > 0;

        /// <summary>Everything, for a failure message: which side disagreed is the whole diagnosis.</summary>
        internal string Evidence =>
            $"endpoints={MachineRenderEndpoints}; osSessionBefore={OsHeldSessionBeforeAnything}; "
            + $"osActiveBefore={OsHeldACTIVESessionBeforeAnything}; "
            + $"open={OpenState.GetType().Name}; osActiveAfterOpen={OsHeldActiveSessionAfterOpen}; "
            + $"claimedRendering={ClaimedRenderingAfterOpen}; peakIdle={PeakWithDeviceOpenAndNothingCued}; "
            + $"cue={CueState.GetType().Name}; peakRendering={PeakWhileRendering}; "
            + $"osActiveAfterDispose={OsSessionStillActiveAfterDispose}";
    }
}
