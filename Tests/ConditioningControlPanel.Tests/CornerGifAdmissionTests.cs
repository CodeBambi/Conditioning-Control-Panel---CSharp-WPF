using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Ticket 1539282547484139682: a session's corner GIF ignored the user's switches and could stack a
/// SECOND spiral on top of a standalone Corner GIF overlay. Two services can raise a corner overlay
/// (SessionEngine and CornerGifService) and the admission rule now lives once, beside the shared
/// source handling in <see cref="CornerGifMedia"/>, so neither can drift.
///
/// <para>The invariant the tests defend: whatever the inputs, the two helpers never both say yes.</para>
/// </summary>
public class CornerGifAdmissionTests
{
    private static readonly JsonSerializerSettings LoaderSettings = new()
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Error = (_, args) => { args.ErrorContext.Handled = true; }
    };

    // ---- the session side ----

    [Fact]
    public void SessionCornerGif_ShowsWhenTemplateAndUserBothAgree()
        => Assert.True(CornerGifMedia.AllowSessionCornerGif(
            templateEnabled: true, userAllowed: true, standaloneOverlayActive: false));

    [Fact]
    public void SessionCornerGif_UserMasterOffBeatsTheTemplate()
        => Assert.False(CornerGifMedia.AllowSessionCornerGif(
            templateEnabled: true, userAllowed: false, standaloneOverlayActive: false));

    [Fact]
    public void SessionCornerGif_TemplateOffStaysOffEvenWhenAllowed()
        => Assert.False(CornerGifMedia.AllowSessionCornerGif(
            templateEnabled: false, userAllowed: true, standaloneOverlayActive: false));

    [Fact]
    public void SessionCornerGif_YieldsToAStandaloneOverlayAlreadyOnScreen()
        => Assert.False(CornerGifMedia.AllowSessionCornerGif(
            templateEnabled: true, userAllowed: true, standaloneOverlayActive: true));

    // ---- the standalone side ----

    [Fact]
    public void StandaloneSlot_ShowsWhenNoSessionOverlayIsUp()
        => Assert.True(CornerGifMedia.AllowStandaloneCornerGif(
            slotEnabled: true, sessionCornerGifActive: false));

    [Fact]
    public void StandaloneSlot_YieldsWhileASessionOverlayIsUp()
        => Assert.False(CornerGifMedia.AllowStandaloneCornerGif(
            slotEnabled: true, sessionCornerGifActive: true));

    [Fact]
    public void DisabledStandaloneSlot_StaysDown()
        => Assert.False(CornerGifMedia.AllowStandaloneCornerGif(
            slotEnabled: false, sessionCornerGifActive: false));

    // ---- the invariant ----

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void TwoSpiralsAreNeverBothAdmitted(bool templateEnabled, bool userAllowed)
    {
        // Whichever one is already up, the other is refused - so the reported "two spirals" state
        // cannot be reached from either direction.
        var sessionUp = CornerGifMedia.AllowSessionCornerGif(templateEnabled, userAllowed, standaloneOverlayActive: false);
        Assert.False(sessionUp && CornerGifMedia.AllowStandaloneCornerGif(slotEnabled: true, sessionCornerGifActive: sessionUp));

        var standaloneUp = CornerGifMedia.AllowStandaloneCornerGif(slotEnabled: true, sessionCornerGifActive: false);
        Assert.False(standaloneUp && CornerGifMedia.AllowSessionCornerGif(templateEnabled, userAllowed, standaloneOverlayActive: standaloneUp));
    }

    // ---- the setting itself ----

    [Fact]
    public void SessionCornerGifAllowed_DefaultsOn_SoNobodysProgramChangesUnderThem()
    {
        Assert.True(new AppSettings().SessionCornerGifAllowed);
        Assert.True(JsonConvert.DeserializeObject<AppSettings>("{}", LoaderSettings)!.SessionCornerGifAllowed);
    }

    [Fact]
    public void SessionCornerGifAllowed_ReadsTheSavedFalse()
        => Assert.False(JsonConvert.DeserializeObject<AppSettings>(
            "{\"SessionCornerGifAllowed\": false}", LoaderSettings)!.SessionCornerGifAllowed);
}
