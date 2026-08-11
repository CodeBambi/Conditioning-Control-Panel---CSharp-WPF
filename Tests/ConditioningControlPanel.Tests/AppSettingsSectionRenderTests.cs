using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Views.Controls.AppSettingsSections;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Covers the Phase 2 Settings sections that did not already have a render suite (Audio, Devices,
/// Performance) plus the assembled page itself.
///
/// <para><b>Why this suite exists at all.</b> Every one of these controls was cut out of a tab or a
/// popup and pasted into a <see cref="UserControl"/>. Two failure modes survive a clean compile:
/// a <c>{StaticResource}</c> key that lived in the old parent's <c>Resources</c> and did not follow
/// (StaticResource resolves when the tree is built, so it throws
/// <c>ResourceReferenceKeyNotFoundException</c> the first time a user opens the door), and an
/// <c>x:Name</c> that got dropped while a MainWindow partial still dereferences it through a
/// passthrough property (which compiles, then NullReferences at runtime).</para>
///
/// <para><b>The whole-page test is the important one.</b> Realizing
/// <see cref="AppSettingsTabView"/> proves all eight sections instantiate together, and the
/// reflection sweep below proves every passthrough any partial resolved against actually returns a
/// control. That sweep is deliberately reflective rather than a hand-written list: eight agents
/// contribute passthroughs in eight partial files, and a hand-written list is exactly the thing that
/// goes stale when a ninth is added.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class AppSettingsSectionRenderTests
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

    private static void Realize(FrameworkElement section, double width = 760)
    {
        var host = ThemedHost();
        host.Children.Add(section);

        host.Measure(new Size(width, double.PositiveInfinity));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, Math.Max(1, host.DesiredSize.Height))));
        host.UpdateLayout();

        Assert.True(section.DesiredSize.Height > 0,
            $"{section.GetType().Name} measured to zero height — its content did not realize");
    }

    // =====================================================================================
    //  the three sections without their own suite
    // =====================================================================================

    [Fact]
    public void AudioSection_Realizes() => OnStaThread(() => Realize(new AudioSettingsSection()));

    [Fact]
    public void DevicesSection_Realizes() => OnStaThread(() => Realize(new DevicesSettingsSection()));

    [Fact]
    public void PerformanceSection_Realizes() => OnStaThread(() => Realize(new PerformanceSettingsSection()));

    [Fact]
    public void EveryAudioNameTheMainWindowPartialsWriteToIsReachable()
    {
        // MainWindow.Settings.cs (load/save), .StartStop.cs (ramping), .UiUpdates.cs (device
        // enumeration), .Patreon.cs (voice ducking) and .Presets.cs (help wiring) all dereference
        // these without a null guard, exactly as the dashboard code they replaced did.
        OnStaThread(() =>
        {
            var s = new AudioSettingsSection();
            Assert.NotNull(s.AudioSection);
            Assert.NotNull(s.HelpBtnAudio);
            Assert.NotNull(s.SliderMaster);
            Assert.NotNull(s.TxtMaster);
            Assert.NotNull(s.SliderVideoVolume);
            Assert.NotNull(s.TxtVideoVolume);
            Assert.NotNull(s.ChkAudioDuck);
            Assert.NotNull(s.SliderDuck);
            Assert.NotNull(s.TxtDuck);
            Assert.NotNull(s.ChkExcludeBambiCloudDucking);
            Assert.NotNull(s.CmbAudioOutputDevice);
            Assert.NotNull(s.BtnAudioOutputRefresh);
            Assert.NotNull(s.BtnTestAudio);
            Assert.NotNull(s.BtnAudioLayers);
        });
    }

    [Fact]
    public void TheAudioSectionBorderKeepsTheNameTheTutorialSpotlights()
    {
        // TutorialService step "set_audio" spotlights TargetElementName "AudioSection" and
        // TutorialOverlay.FindElementByName matches on FrameworkElement.Name while walking the
        // visual tree, so the name has to survive on the element itself - not only as a passthrough.
        OnStaThread(() =>
        {
            var s = new AudioSettingsSection();
            Assert.Equal("AudioSection", s.AudioSection.Name);
        });
    }

    [Fact]
    public void PerformanceSectionOwnsTheMotionKillSwitch()
    {
        // The ComboBoxItem order IS the Models.MotionLevel ordinal contract: MainWindow.Settings.cs
        // casts SelectedIndex straight to the enum. Full / Reduced / Off, in that order.
        OnStaThread(() =>
        {
            var s = new PerformanceSettingsSection();
            Assert.NotNull(s.ChkPerformanceMode);
            Assert.NotNull(s.ChkAutoPerformance);
            Assert.NotNull(s.ChkUnifiedOverlay);
            Assert.NotNull(s.CmbMotionLevel);
            Assert.Equal(Enum.GetValues<ConditioningControlPanel.Models.MotionLevel>().Length,
                s.CmbMotionLevel.Items.Count);
        });
    }

    [Fact]
    public void DevicesSectionOwnsTheSingleWebcamAndMicAndPanicEditors()
    {
        // R-1 retired: the webcam engine bar is no longer detached out of the Lab tab, and the
        // blink-recal / restrict-gaze / camera / monitor controls collapsed from 3-5 copies to one.
        // The panic key collapsed from two live editors to this one.
        OnStaThread(() =>
        {
            var s = new DevicesSettingsSection();
            Assert.NotNull(s.LabWebcamEngineBar);
            Assert.NotNull(s.CmbWebcamDevice);
            Assert.NotNull(s.CmbWebcamMonitor);
            Assert.NotNull(s.ChkBlinkRecalWebcamBar);
            Assert.NotNull(s.ChkRestrictGazeToCalScreen);
            Assert.NotNull(s.CmbMicDevice);
            Assert.NotNull(s.ChkSpeechWakeWord);
            Assert.NotNull(s.ChkSpeechPushToTalk);
            Assert.NotNull(s.ChkHeadphones);
            Assert.NotNull(s.ChkNoPanic);
            Assert.NotNull(s.BtnPanicKey);
        });
    }

    // =====================================================================================
    //  the assembled page
    // =====================================================================================

    [Fact]
    public void TheWholeSettingsPageRealizes()
    {
        // All eight sections in one tree. This is the test that would have caught a section whose
        // StaticResource lived in MainWindow.xaml's own Window.Resources: a UserControl's XAML is
        // parsed before it is attached to MainWindow, so those keys never resolve.
        OnStaThread(() => Realize(new AppSettingsTabView(), 940));
    }

    [Fact]
    public void EveryPassthroughOnThePageResolvesToARealControl()
    {
        // Reflective on purpose. The passthroughs are contributed by eight partial files
        // (AppSettingsTabView.<Section>.cs); a hand-maintained list would miss the ninth.
        OnStaThread(() =>
        {
            var page = new AppSettingsTabView();

            var passthroughs = typeof(AppSettingsTabView)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
                .Where(p => typeof(System.Windows.DependencyObject).IsAssignableFrom(p.PropertyType))
                .Where(p => p.DeclaringType == typeof(AppSettingsTabView))
                .ToList();

            Assert.True(passthroughs.Count >= 60,
                $"expected the eight section partials to contribute passthroughs, found {passthroughs.Count}");

            var broken = new List<string>();
            foreach (var p in passthroughs)
            {
                object? value;
                try { value = p.GetValue(page); }
                catch (Exception ex) { broken.Add($"{p.Name} threw {ex.GetType().Name}"); continue; }
                if (value == null) broken.Add($"{p.Name} is null");
            }

            Assert.True(broken.Count == 0,
                "passthroughs that do not resolve (a MainWindow partial will NullReference on these): "
                + string.Join(", ", broken));
        });
    }

    [Fact]
    public void EverySectionKeyHasAPillAndASectionHost()
    {
        // FocusSection(key) is the contract the account chip, TierGate, the tutorial and the Ctrl+K
        // palette all navigate through. A key with no pill silently no-ops.
        OnStaThread(() =>
        {
            var page = new AppSettingsTabView();
            Assert.NotEmpty(AppSettingsTabView.SectionKeys);

            foreach (var key in AppSettingsTabView.SectionKeys)
            {
                var suffix = char.ToUpperInvariant(key[0]) + key.Substring(1);
                Assert.True(page.FindName("SectionPill" + suffix) is RadioButton,
                    $"section key '{key}' has no SectionPill{suffix} in the mini-rail");
                Assert.True(page.FindName("Section" + suffix) is UserControl,
                    $"section key '{key}' has no Section{suffix} host in the scroll stack");
            }

            // And an unknown key must be a quiet no-op, never a throw - callers pass it blind.
            page.FocusSection("no-such-section");
            page.FocusSection(null);
        });
    }

    // =====================================================================================
    //  the Ctrl+K index (pure - no WPF needed)
    // =====================================================================================

    [Fact]
    public void ThePaletteIndexOnlyPointsAtSectionsThatExist()
    {
        // The cheap guard against the index rotting as Phases 3-8 move controls around: no STA
        // thread, no window, just the authored rows.
        var keys = new HashSet<string>(AppSettingsTabView.SectionKeys, StringComparer.OrdinalIgnoreCase);

        var bad = SettingsPaletteIndex.All
            .Where(e => !string.IsNullOrEmpty(e.SectionKey) && !keys.Contains(e.SectionKey!))
            .Select(e => $"{e.Id} -> '{e.SectionKey}'")
            .ToList();

        Assert.True(bad.Count == 0,
            "palette rows aimed at a Settings section that does not exist: " + string.Join(", ", bad));
    }

    [Fact]
    public void ThePaletteIndexHasNoDuplicateIdsAndAlwaysAnswersAnEmptyQuery()
    {
        var dupes = SettingsPaletteIndex.All
            .GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(dupes.Count == 0, "duplicate palette entry ids: " + string.Join(", ", dupes));

        // Opening the palette shows the authored order rather than a blank box.
        Assert.NotEmpty(SettingsPaletteIndex.Search("", 12));
        Assert.NotEmpty(SettingsPaletteIndex.Search("audio", 12));
    }
}
