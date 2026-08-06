using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Views.Controls.Companion.Runtime;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Realizes the six re-parented Workshop cells.
///
/// <para>These are the riskiest new XAML in the wiring pass and the only files in it that reach
/// OUTSIDE the companion theme: the controls came from the old accordions, so they still use the
/// app-level styles (<c>ToggleStyle</c>, <c>PinkSlider</c>, <c>DarkComboBoxStyle</c>,
/// <c>HelpButtonStyle</c>) and the app-level brushes. A <c>{StaticResource}</c> is resolved when the
/// tree is built, not when the XAML compiles, so a key that moved or was renamed builds clean and
/// then throws <c>ResourceReferenceKeyNotFoundException</c> the first time someone opens the
/// Workshop — which, being a collapsed drawer, is not the first thing anyone opens.</para>
///
/// <para>No <c>Application</c> is created (one per process, and xUnit is not one process per test),
/// so the app dictionaries are merged into a host panel instead. That is the same lookup chain a
/// real run walks, one level lower.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class CompanionWorkshopCellRenderTests
{
    private static readonly string[] AppThemeDictionaries =
    {
        "Resources/Theme/Colors.xaml",
        "Resources/Theme/Brushes.xaml",
        "Resources/Theme/Controls.xaml",
        "Resources/Theme/Converters.xaml",
        "Resources/Theme/Motion.xaml",
        "Resources/Theme/MainWindow.xaml"
    };

    private static void OnStaThread(Action body)
    {
        Exception? escaped = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { escaped = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA render thread did not finish in time");
        if (escaped != null) throw new Xunit.Sdk.XunitException(escaped.ToString());
    }

    /// <summary>A host whose Resources carry what Application.Resources carries at runtime.</summary>
    private static Grid ThemedHost()
    {
        var host = new Grid();
        foreach (var relative in AppThemeDictionaries)
        {
            host.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/ConditioningControlPanel;component/" + relative,
                    UriKind.Absolute)
            });
        }
        return host;
    }

    private static void Realize(UserControl cell)
    {
        var host = ThemedHost();
        host.Children.Add(cell);

        // A Workshop pigeonhole is 340px wide with 13px of padding either side.
        host.Measure(new Size(312, double.PositiveInfinity));
        host.Arrange(new Rect(new Point(0, 0), new Size(312, Math.Max(1, host.DesiredSize.Height))));
        host.UpdateLayout();

        Assert.True(cell.DesiredSize.Height > 0,
            $"{cell.GetType().Name} measured to zero height — its content did not realize");
    }

    [Fact]
    public void RosterCell_Realizes() => OnStaThread(() => Realize(new WorkshopRosterCell()));

    [Fact]
    public void BehaviorCell_Realizes() => OnStaThread(() => Realize(new WorkshopBehaviorCell()));

    [Fact]
    public void TriggersCell_Realizes() => OnStaThread(() => Realize(new WorkshopTriggersCell()));

    [Fact]
    public void LibraryCell_Realizes() => OnStaThread(() => Realize(new WorkshopLibraryCell()));

    [Fact]
    public void CommunityCell_Realizes() => OnStaThread(() => Realize(new WorkshopCommunityCell()));

    [Fact]
    public void AwarenessCell_Realizes() => OnStaThread(() => Realize(new WorkshopAwarenessCell()));

    [Fact]
    public void EveryCompatNameTheMainWindowPartialsWriteToIsReachable()
    {
        // The disposition table's promise, made checkable: each of these x:Names existed on the old
        // tab and is written by a MainWindow partial. If a cell ever loses one, the app compiles
        // (the passthrough is a property, resolved at build) but this fails.
        OnStaThread(() =>
        {
            var parts = new WorkshopShelfParts();

            Assert.NotNull(parts.Roster.CompanionCard0);
            Assert.NotNull(parts.Roster.CompanionCard4);
            Assert.NotNull(parts.Roster.TxtCompanion0Name);
            Assert.NotNull(parts.Roster.TxtCompanion4Prompt);
            Assert.NotNull(parts.Roster.HelpBtnCompanions);

            Assert.NotNull(parts.Behavior.SliderIdleIntervalCompanion);
            Assert.NotNull(parts.Behavior.TxtIdleIntervalCompanion);
            Assert.NotNull(parts.Behavior.SliderBubbleDurationCompanion);
            Assert.NotNull(parts.Behavior.TxtChatShortcutLabel);
            Assert.NotNull(parts.Behavior.TxtCameraShortcutLabel);
            Assert.NotNull(parts.Behavior.ChkMuteWhispersCompanion);
            Assert.NotNull(parts.Behavior.ChkPauseBrowserCompanion);

            Assert.NotNull(parts.Triggers.ChkTriggerModeCompanion);
            Assert.NotNull(parts.Triggers.TriggerSettingsPanelCompanion);
            Assert.NotNull(parts.Triggers.SliderTriggerIntervalCompanion);
            Assert.NotNull(parts.Triggers.TxtPhraseCount);
            Assert.NotNull(parts.Triggers.CmbPhrasePresets);

            Assert.NotNull(parts.Library.TxtHypnotubeModeLabel);
            Assert.NotNull(parts.Library.VideoLinkPoolPanel);
            Assert.NotNull(parts.Library.TxtNoVideoLinks);
            Assert.NotNull(parts.Library.HelpBtnVideoLinks);

            Assert.NotNull(parts.Community.BtnRefreshPrompts);
            Assert.NotNull(parts.Community.InstalledPromptsPanel);
            Assert.NotNull(parts.Community.HelpBtnPrompts);

            Assert.NotNull(parts.Awareness.AwarenessSettingsPanel);
            Assert.NotNull(parts.Awareness.SliderAwarenessCooldown);
            Assert.NotNull(parts.Awareness.SliderAwarenessCooldownMax);
            Assert.NotNull(parts.Awareness.BtnPrivacySpoiler);
            Assert.NotNull(parts.Awareness.TxtPrivacyDetails);
            Assert.NotNull(parts.Awareness.HelpBtnAwareness);
        });
    }

    [Fact]
    public void TheAwarenessCooldownsStartHidden_LikeTheOldPanelDid()
    {
        // MainWindow collapses this while her eyes are closed; it must not start visible, or a user
        // with awareness off sees cooldowns for a capability that is not running.
        OnStaThread(() =>
        {
            var cell = new WorkshopAwarenessCell();
            Assert.Equal(Visibility.Collapsed, cell.AwarenessSettingsPanel.Visibility);
        });
    }

    [Fact]
    public void TheTriggerIntervalPanelStartsHidden_LikeTheOldOneDid()
    {
        OnStaThread(() =>
        {
            var cell = new WorkshopTriggersCell();
            Assert.Equal(Visibility.Collapsed, cell.TriggerSettingsPanelCompanion.Visibility);
        });
    }
}
