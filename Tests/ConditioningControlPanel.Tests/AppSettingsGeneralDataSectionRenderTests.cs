using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Views.Controls.AppSettingsSections;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Realizes the two Settings sections rescued from the permanently-Collapsed
/// <c>LegacyDashboardHost</c> in Phase 2: General (language, startup group, tray explainer, Deeper
/// master switch) and Data (offline mode, phrase backup, Danger Zone).
///
/// <para>Same reason the Workshop cells have this suite: these controls carry app-level
/// <c>{StaticResource}</c> keys that were resolved from their old home's tree. A StaticResource is
/// resolved when the tree is BUILT, not when the XAML compiles, so a key that did not follow them
/// here builds clean and throws <c>ResourceReferenceKeyNotFoundException</c> the first time a user
/// opens Settings. The Deeper card is the one to watch - it is the only card on either page using
/// the <c>DeeperAccent*</c> brushes.</para>
///
/// <para>The name assertions matter more here than usual: General's checkboxes are dereferenced
/// WITHOUT a null guard by <c>MainWindow.LoadSettings()</c> (they used to be dashboard controls that
/// were always there), so a lost x:Name is a startup NullReferenceException, not a cosmetic bug.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class AppSettingsGeneralDataSectionRenderTests
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
    public void GeneralSection_Realizes() => OnStaThread(() => Realize(new GeneralSettingsSection()));

    [Fact]
    public void DataSection_Realizes() => OnStaThread(() => Realize(new DataSettingsSection()));

    [Fact]
    public void EveryGeneralNameTheMainWindowPartialsWriteToIsReachable()
    {
        // LoadSettings/SaveSettings, ChkWinStart_Click, ChkStartHidden_Click,
        // RequestToggleWindowsStartup, SyncWinStartState, ChkEnableDeeper_Changed and both
        // startup-video handlers all address these by name through AppSettingsTab passthroughs.
        // Four of them are dereferenced with no null guard on the startup path.
        OnStaThread(() =>
        {
            var s = new GeneralSettingsSection();

            Assert.NotNull(s.ChkWinStart);
            Assert.NotNull(s.ChkStartHidden);
            Assert.NotNull(s.ChkVidLaunch);
            Assert.NotNull(s.ChkAutoRun);
            Assert.NotNull(s.TxtStartupVideo);
            Assert.NotNull(s.ChkEnableDeeper);
            Assert.NotNull(s.CmbLanguageSetting);
        });
    }

    [Fact]
    public void DataSectionOwnsOfflineModeAndTheDangerZone()
    {
        OnStaThread(() =>
        {
            var s = new DataSettingsSection();

            // MainWindow drives this one by name in three places so its two-way sync runs once.
            Assert.NotNull(s.ChkOfflineMode);

            // The phrase-backup pair are the app's ONLY call sites for
            // MainWindow.PresetIO.cs's export/import handlers.
            Assert.NotNull(s.BtnExportPhrases);
            Assert.NotNull(s.BtnImportPhrases);

            Assert.NotNull(s.DangerZoneSection);
            Assert.NotNull(s.BtnFactoryReset);
        });
    }

    [Fact]
    public void TheLanguageListStartsEmpty_MainWindowFillsIt()
    {
        // Deliberate: MainWindow.InitializeLanguageSelector populates BOTH language surfaces from
        // LocalizationManager.AvailableLanguages so the chrome pill and this list can never drift.
        // Hard-coding items in the XAML would be the drift.
        OnStaThread(() =>
        {
            var s = new GeneralSettingsSection();
            Assert.Empty(s.CmbLanguageSetting.Items);
        });
    }
}
