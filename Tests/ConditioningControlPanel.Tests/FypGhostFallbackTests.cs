using ConditioningControlPanel.Services.Fyp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1157 #1158 #1166 #1211 #1219: "For You ghost mode is just a black screen".
///
/// <para>Ghost mode parks the real feed window off every monitor and puts a DWM live-thumbnail
/// mirror in its place. The mirror is a WinForms window painted RGB(1,1,1) and colour-keyed out of
/// composition, with the thumbnail riding on DWM_TNP_OPACITY. Two of those steps can fail on a
/// machine that is not the one they were measured on, and neither failure used to stop anything:
/// SetLayeredWindowAttributes was fire-and-forget, and DwmRegisterThumbnail only logged a warning.
/// Either way the user is left staring at an opaque, click-through, topmost, monitor-sized sheet
/// of near-black - with the real window parked off-screen behind it.</para>
///
/// <para>The decision "is this mirror worth showing" is now a pure function of what the native
/// calls reported, which is the only part of that stack a test can run. These pin the branches so
/// a later edit cannot quietly go back to showing the sheet regardless.</para>
/// </summary>
public class FypGhostFallbackTests
{
    private const int Ok = 0;
    private const int Failed = unchecked((int)0x80004005);

    [Fact]
    public void EverythingHealthy_IsTheOnlyWayToShowTheMirror()
    {
        Assert.Null(FypGhostOverlay.Diagnose(
            compositionEnabled: true, colorKeyApplied: true, registerHr: Ok,
            sourceWidth: 1920, sourceHeight: 1040));
    }

    [Fact]
    public void CompositionDisabled_RefusesFirst()
    {
        // No DWM, no thumbnail and no colour key - and the reason must name composition rather
        // than whatever it took down with it, or the next report reads as a colour-key bug.
        Assert.Equal("composition-disabled", FypGhostOverlay.Diagnose(
            compositionEnabled: false, colorKeyApplied: false, registerHr: Failed,
            sourceWidth: 0, sourceHeight: 0));
    }

    [Fact]
    public void ColorKeyRejected_RefusesEvenWithAPerfectThumbnail()
    {
        // THE black screen. A registered, correctly sized thumbnail composes on top of a sheet
        // that never dropped out, so at the default opacity of 1.0 the monitor goes solid.
        Assert.Equal("colorkey-rejected", FypGhostOverlay.Diagnose(
            compositionEnabled: true, colorKeyApplied: false, registerHr: Ok,
            sourceWidth: 1920, sourceHeight: 1040));
    }

    [Fact]
    public void RegistrationFailure_CarriesItsHresultIntoTheReason()
    {
        // The HRESULT is the whole value of this branch: DWM_E_COMPOSITIONDISABLED, E_INVALIDARG
        // against a cloaked source and E_HANDLE against a dead one are three different bugs.
        var reason = FypGhostOverlay.Diagnose(
            compositionEnabled: true, colorKeyApplied: true, registerHr: unchecked((int)0x80070057),
            sourceWidth: 1920, sourceHeight: 1040);
        Assert.Equal("thumbnail-register-failed-0x80070057", reason);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1920, 0)]
    [InlineData(0, 1040)]
    [InlineData(-1, -1)]
    public void ZeroSizedSource_RefusesEvenThoughRegistrationSucceeded(int w, int h)
    {
        // DwmRegisterThumbnail says yes to a window DWM is not composing. The registration is
        // real and the picture is nothing, which paints the destination black - the failure that
        // looks most like "it works on my machine" and least like an error.
        Assert.Equal("thumbnail-source-empty", FypGhostOverlay.Diagnose(
            compositionEnabled: true, colorKeyApplied: true, registerHr: Ok,
            sourceWidth: w, sourceHeight: h));
    }
}
