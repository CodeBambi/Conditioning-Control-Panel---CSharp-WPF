using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #1052 - subliminals arrived 1.3s late whenever haptics were enabled, instant when they were off.
///
/// SubliminalService awaits <c>HapticService.SubliminalAnticipationMs</c> BEFORE drawing the card so
/// a real toy's motor has time to spin up and the pulse lands with the text. The old rule keyed that
/// delay off the PROVIDER alone (Buttplug = 1300ms, anything else = 250ms), so a user who had
/// haptics enabled but no toy connected paid the full latency for a motor that did not exist.
/// The delay is now spent only when there is actually something to anticipate.
/// </summary>
public class SubliminalAnticipationGateTests
{
    private const int Buttplug = 1300;
    private const int Direct = 250;

    [Fact]
    public void ConnectedButtplugDevice_KeepsTheFullSpinUpHeadStart()
    {
        // The reason the delay exists at all - do not regress it away.
        Assert.Equal(Buttplug, HapticService.ResolveSubliminalAnticipationMs(
            hapticsEnabled: true, deviceConnected: true, subliminalRuleEnabled: true, isButtplugProvider: true));
    }

    [Fact]
    public void ConnectedDirectDevice_KeepsItsShorterHeadStart()
    {
        Assert.Equal(Direct, HapticService.ResolveSubliminalAnticipationMs(
            hapticsEnabled: true, deviceConnected: true, subliminalRuleEnabled: true, isButtplugProvider: false));
    }

    [Fact]
    public void HapticsEnabledButNothingConnected_IsInstant_TheBugRepro()
    {
        // The exact repro: haptics on, Buttplug selected, no toy paired. Used to cost 1.3s.
        Assert.Equal(0, HapticService.ResolveSubliminalAnticipationMs(
            hapticsEnabled: true, deviceConnected: false, subliminalRuleEnabled: true, isButtplugProvider: true));
        Assert.Equal(0, HapticService.ResolveSubliminalAnticipationMs(
            hapticsEnabled: true, deviceConnected: false, subliminalRuleEnabled: true, isButtplugProvider: false));
    }

    [Fact]
    public void MasterToggleOff_IsInstantEvenWithADeviceStillPaired()
    {
        // Master off means the mixer never posts the pulse, so there is nothing to wait for.
        Assert.Equal(0, HapticService.ResolveSubliminalAnticipationMs(
            hapticsEnabled: false, deviceConnected: true, subliminalRuleEnabled: true, isButtplugProvider: true));
    }

    [Fact]
    public void SubliminalRoutingRowOff_IsInstant()
    {
        // A disabled routing row makes PostEvent return Completed without touching a motor, so the
        // head start would be spent on a pulse that is never sent.
        Assert.Equal(0, HapticService.ResolveSubliminalAnticipationMs(
            hapticsEnabled: true, deviceConnected: true, subliminalRuleEnabled: false, isButtplugProvider: true));
    }

    [Fact]
    public void EveryDisconnectedCombination_IsZero()
    {
        foreach (var enabled in new[] { false, true })
            foreach (var rule in new[] { false, true })
                foreach (var buttplug in new[] { false, true })
                    Assert.Equal(0, HapticService.ResolveSubliminalAnticipationMs(
                        hapticsEnabled: enabled, deviceConnected: false,
                        subliminalRuleEnabled: rule, isButtplugProvider: buttplug));
    }

    [Fact]
    public void OnlyTheFullyRoutedConnectedCase_IsEverNonZero()
    {
        foreach (var enabled in new[] { false, true })
            foreach (var connected in new[] { false, true })
                foreach (var rule in new[] { false, true })
                    foreach (var buttplug in new[] { false, true })
                    {
                        var ms = HapticService.ResolveSubliminalAnticipationMs(enabled, connected, rule, buttplug);
                        if (enabled && connected && rule)
                            Assert.Equal(buttplug ? Buttplug : Direct, ms);
                        else
                            Assert.Equal(0, ms);
                    }
    }

    // ---------------------------------------------------------------------------------------
    // The subliminal gate must NOT leak into the transport-latency figure. AudioSyncService uses
    // the provider round-trip as the look-ahead for the audio/funscript-synced haptic track; that
    // has nothing to do with whether the user routes SUBLIMINALS to their toy. Conflating the two
    // would put audio-synced haptics ~1.3s behind the video for anyone who simply turned the
    // Subliminals haptic row off.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ProviderLatency_IgnoresTheSubliminalRoutingRow()
    {
        // Same connected Buttplug device, subliminal row off: the transport figure does not move.
        Assert.Equal(Buttplug, HapticService.ResolveProviderLatencyMs(
            hapticsEnabled: true, deviceConnected: true, isButtplugProvider: true));
        Assert.Equal(0, HapticService.ResolveSubliminalAnticipationMs(
            hapticsEnabled: true, deviceConnected: true, subliminalRuleEnabled: false, isButtplugProvider: true));
    }

    [Fact]
    public void ProviderLatency_MatchesTheDeviceRoundTrip()
    {
        Assert.Equal(Buttplug, HapticService.ResolveProviderLatencyMs(
            hapticsEnabled: true, deviceConnected: true, isButtplugProvider: true));
        Assert.Equal(Direct, HapticService.ResolveProviderLatencyMs(
            hapticsEnabled: true, deviceConnected: true, isButtplugProvider: false));
    }

    [Fact]
    public void ProviderLatency_IsZeroWithNothingToTalkTo()
    {
        foreach (var buttplug in new[] { false, true })
        {
            Assert.Equal(0, HapticService.ResolveProviderLatencyMs(
                hapticsEnabled: true, deviceConnected: false, isButtplugProvider: buttplug));
            Assert.Equal(0, HapticService.ResolveProviderLatencyMs(
                hapticsEnabled: false, deviceConnected: true, isButtplugProvider: buttplug));
        }
    }

    [Fact]
    public void SubliminalAnticipation_IsTheProviderLatencyPlusTheRoutingGate()
    {
        // The two stay in lockstep except for the row gate - the only difference there may ever be.
        foreach (var enabled in new[] { false, true })
            foreach (var connected in new[] { false, true })
                foreach (var buttplug in new[] { false, true })
                {
                    var transport = HapticService.ResolveProviderLatencyMs(enabled, connected, buttplug);
                    Assert.Equal(transport, HapticService.ResolveSubliminalAnticipationMs(
                        enabled, connected, subliminalRuleEnabled: true, isButtplugProvider: buttplug));
                    Assert.Equal(0, HapticService.ResolveSubliminalAnticipationMs(
                        enabled, connected, subliminalRuleEnabled: false, isButtplugProvider: buttplug));
                }
    }
}
