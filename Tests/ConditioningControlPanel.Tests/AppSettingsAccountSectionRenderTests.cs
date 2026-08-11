using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Views.Controls.AppSettingsSections;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Realizes the three Settings sections that came out of other trees in Phase 2: Account (seven
/// cards lifted from PatreonTabView), Updates (the check button lifted from the AppInfo popup) and
/// Notifications (ChkIntakeNudge lifted from the collapsed LegacyDashboardHost).
///
/// <para>Same reason the Workshop cells have this suite: these controls carry app-level
/// <c>{StaticResource}</c> keys (<c>SectionHeader</c>, <c>SectionPanel</c>, <c>SettingLabel</c>,
/// <c>ToggleStyle</c>) and app-level brushes from their old homes. A StaticResource is resolved when
/// the tree is built, not when the XAML compiles, so a key that did not follow them here builds
/// clean and throws <c>ResourceReferenceKeyNotFoundException</c> the first time someone opens
/// Settings.</para>
///
/// <para>The Account section additionally has a start-state contract: three of its cards are
/// Collapsed until MainWindow says otherwise. If one ever ships Visible, a signed-out user sees
/// cloud-backup and account-linking controls that do nothing.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class AppSettingsAccountSectionRenderTests
{
    // Delegates to the shared harness so the themed Application exists before any section
    // constructor runs — these sections resolve app-level {StaticResource} keys inside
    // InitializeComponent(), i.e. before they have a parent to look through.
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    /// <summary>
    /// A bare host. The theme is NOT merged here - WpfRenderHarness parses it onto
    /// <c>Application.Resources</c> the way App.xaml does, which is both where a parse-time
    /// {StaticResource} looks and the only arrangement in which the theme's own internal
    /// StaticResources (MainWindow.xaml styles pointing at Brushes.xaml) resolve at all.
    /// Merging a second, C#-constructed copy here would shadow it with a broken one.
    /// </summary>
    private static Grid ThemedHost() => new Grid();

    private static void Realize(UserControl section)
    {
        var host = ThemedHost();
        host.Children.Add(section);

        // The Settings page column is ~760px wide inside the 1489x901 canvas.
        host.Measure(new Size(760, double.PositiveInfinity));
        host.Arrange(new Rect(new Point(0, 0), new Size(760, Math.Max(1, host.DesiredSize.Height))));
        host.UpdateLayout();

        Assert.True(section.DesiredSize.Height > 0,
            $"{section.GetType().Name} measured to zero height — its content did not realize");
    }

    [Fact]
    public void AccountSection_Realizes() => OnStaThread(() => Realize(new AccountSettingsSection()));

    [Fact]
    public void UpdatesSection_Realizes() => OnStaThread(() => Realize(new UpdatesSettingsSection()));

    [Fact]
    public void NotificationsSection_Realizes() => OnStaThread(() => Realize(new NotificationsSettingsSection()));

    [Fact]
    public void EveryAccountNameTheMainWindowPartialsWriteToIsReachable()
    {
        // Each of these existed on PatreonTabView and is written by a MainWindow partial
        // (Patreon.cs, SubscribeStar.cs, CloudBackup.cs, UiUpdates.cs, xaml.cs). If the section
        // ever loses one, the app still compiles - the passthrough is a property - and then throws
        // a NullReferenceException on the next login. This fails first instead.
        OnStaThread(() =>
        {
            var s = new AccountSettingsSection();

            Assert.NotNull(s.PatreonLoginCard);
            Assert.NotNull(s.TxtPatreonStatus);
            Assert.NotNull(s.TxtPatreonTier);
            Assert.NotNull(s.BtnPatreonLogin);

            Assert.NotNull(s.SubscribeStarLoginCard);
            Assert.NotNull(s.TxtSubscribeStarStatus);
            Assert.NotNull(s.TxtSubscribeStarTier);
            Assert.NotNull(s.BtnSubscribeStarLogin);

            Assert.NotNull(s.DiscordLoginCard);
            Assert.NotNull(s.TxtDiscordStatus);
            Assert.NotNull(s.TxtDiscordInfo);
            Assert.NotNull(s.BtnDiscordLogin);

            Assert.NotNull(s.AccountLinkingSection);
            Assert.NotNull(s.BtnLinkPatreon);
            Assert.NotNull(s.BtnLinkDiscord);

            Assert.NotNull(s.CloudSettingsBackupSection);
            Assert.NotNull(s.TxtCloudBackupStatus);
            Assert.NotNull(s.BtnBackupSettingsNow);
            Assert.NotNull(s.BtnRestoreSettings);

            Assert.NotNull(s.DataPrivacySection);
            Assert.NotNull(s.BtnExportData);

            Assert.NotNull(s.SupportDevelopmentCard);
            Assert.NotNull(s.RunPatreonFeatures);
        });
    }

    [Fact]
    public void TheGatedAccountCardsStartHidden_LikeTheOldOnesDid()
    {
        OnStaThread(() =>
        {
            var s = new AccountSettingsSection();
            Assert.Equal(Visibility.Collapsed, s.AccountLinkingSection.Visibility);
            Assert.Equal(Visibility.Collapsed, s.CloudSettingsBackupSection.Visibility);
            Assert.Equal(Visibility.Collapsed, s.DataPrivacySection.Visibility);
            // The two link buttons are independently gated inside the (collapsed) linking card.
            Assert.Equal(Visibility.Collapsed, s.BtnLinkPatreon.Visibility);
            Assert.Equal(Visibility.Collapsed, s.BtnLinkDiscord.Visibility);
        });
    }

    [Fact]
    public void NotificationsSectionOwnsTheIntakeNudgeToggle()
    {
        // Its one row, and the only notification preference AppSettings has.
        OnStaThread(() =>
        {
            var s = new NotificationsSettingsSection();
            Assert.NotNull(s.ChkIntakeNudge);
        });
    }
}
