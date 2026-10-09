using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Fix wave f4-you (2026-10-09): the Privacy and Sharing panel paints every switch from the stored settings, and
/// the three switches whose WPF handler is a settings write (share achievements, share level milestones, Goon rich
/// presence) write. The six consent switches that must also push to the server stay unwired on purpose: a click
/// on one changes nothing stored. Reads and writes CoreSettings, so it runs alone.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ProfilePrivacyPanelTests
{
    [Fact]
    public async Task SwitchesPaintFromSettings_SettingsOnlyOnesWrite_ConsentOnesDoNot()
    {
        var s = CoreSettings.Current;
        var old = (s.DiscordRichPresenceEnabled, s.DiscordShowLevelInPresence, s.ShowOnlineStatus, s.DiscordShareAchievements,
                   s.DiscordShareLevelUps, s.AllowDiscordDm, s.ShareProfilePicture, s.PublicShareRealAvatar,
                   s.GoonShareAvatar, s.GoonShareDiscordDm, s.GoonRichPresence);
        try
        {
            (s.DiscordShowLevelInPresence, s.ShowOnlineStatus, s.DiscordShareAchievements, s.DiscordShareLevelUps,
             s.AllowDiscordDm, s.ShareProfilePicture, s.PublicShareRealAvatar, s.GoonShareAvatar, s.GoonShareDiscordDm,
             s.GoonRichPresence) = (false, false, true, false, true, false, false, true, false, false);
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<AppA>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                LocalizationManager.Instance.SetLanguage("en");

                var panel = new ProfilePrivacyPanel();
                CheckBox Box(string n) => panel.FindControl<CheckBox>(n)!;
                panel.Refresh();

                // Painted from settings, not from the markup defaults (Show level / Show online were always on).
                Assert.False(Box("ChkDiscordTabShowLevel").IsChecked);
                Assert.False(Box("ChkDiscordTabShowOnline").IsChecked);
                Assert.True(Box("ChkDiscordTabShareAchievements").IsChecked);
                Assert.False(Box("ChkDiscordTabShareLevelUps").IsChecked);
                Assert.True(Box("ChkDiscordTabAllowDm").IsChecked);
                Assert.False(Box("ChkDiscordTabSharePfp").IsChecked);
                Assert.False(Box("ChkPublicShareRealAvatar").IsChecked);
                Assert.True(Box("ChkGoonShareAvatar").IsChecked);
                Assert.False(Box("ChkGoonShareDiscordDm").IsChecked);
                Assert.False(Box("ChkGoonRichPresence").IsChecked);

                // The settings-only switches write, both ways.
                Box("ChkDiscordTabShareAchievements").IsChecked = false;
                Box("ChkDiscordTabShareLevelUps").IsChecked = true;
                Box("ChkGoonRichPresence").IsChecked = true;
                Assert.Equal((false, true, true), (s.DiscordShareAchievements, s.DiscordShareLevelUps, s.GoonRichPresence));
                Box("ChkGoonRichPresence").IsChecked = false;
                Assert.False(s.GoonRichPresence);

                // A consent switch with no push behind it stores nothing: revoke and grant both wait for the sync seam.
                Box("ChkGoonShareAvatar").IsChecked = false;
                Box("ChkPublicShareRealAvatar").IsChecked = true;
                Box("ChkDiscordTabAllowDm").IsChecked = false;
                Assert.Equal((true, false, true), (s.GoonShareAvatar, s.PublicShareRealAvatar, s.AllowDiscordDm));

                // A repaint is not a click: painting a changed stored value writes nothing back.
                s.DiscordShareLevelUps = false;
                panel.Refresh();
                Assert.False(Box("ChkDiscordTabShareLevelUps").IsChecked);
                Assert.False(s.DiscordShareLevelUps);

                // Signed out of Discord: the line says so.
                if (ConditioningControlPanel.Avalonia.Platform.AccountSeed.Discord?.IsAuthenticated != true)
                    Assert.Equal(Loc.Get("label_not_connected"), panel.FindControl<TextBlock>("TxtDiscordTabStatus")!.Text);
                return Task.CompletedTask;
            });
        }
        finally
        {
            (s.DiscordRichPresenceEnabled, s.DiscordShowLevelInPresence, s.ShowOnlineStatus, s.DiscordShareAchievements,
             s.DiscordShareLevelUps, s.AllowDiscordDm, s.ShareProfilePicture, s.PublicShareRealAvatar,
             s.GoonShareAvatar, s.GoonShareDiscordDm, s.GoonRichPresence) = old;
            CoreSettings.SaveImmediate();
        }
    }
}
