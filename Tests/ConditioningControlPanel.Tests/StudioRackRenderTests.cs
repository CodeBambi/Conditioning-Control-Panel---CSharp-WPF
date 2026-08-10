using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Views.Controls.Studio;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// UX restructure Phase 4 — the Studio door's effects rack.
///
/// <para><b>Why this suite exists.</b> Same two silent failure modes the Phase 2 Settings suite
/// was written for, plus one that is new to this phase. (1) A <c>{StaticResource}</c> key that
/// lived in <c>MainWindow.xaml</c>'s own <c>Window.Resources</c> does not follow a control into a
/// <see cref="UserControl"/>, and StaticResource resolves while the tree is being built — so it
/// throws the first time a user opens the door, not at compile time. (2) An <c>x:Name</c> that a
/// MainWindow partial dereferences through a passthrough compiles fine and then NullReferences at
/// runtime. (3) New here: <see cref="HapticsTabView"/> was <b>moved</b> out of MainWindow.xaml
/// into the rack, so 140+ <c>HapticsTab.&lt;x:Name&gt;</c> dereferences now resolve through one
/// property. If <see cref="StudioTabView.HapticsPanel"/> ever returns null, every one of them
/// NullReferences and nothing in the compiler notices.</para>
///
/// <para>The rack-table tests are the other half. The rack key, the panel, the state dot and the
/// <c>NotifyFeatureOpened</c> bark key all live in one private table in
/// <c>StudioTabView.BuildRack()</c>; these assert the observable consequences of that table
/// (every key selects, unknown keys are quiet, the bark keys are the popup's) so the table cannot
/// drift away from the contract without a red test.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class StudioRackRenderTests
{
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    /// <summary>
    /// A bare host — the theme lives on <c>Application.Resources</c> where App.xaml puts it and
    /// where a parse-time StaticResource looks. Merging a second copy here would shadow it with a
    /// broken one (see <see cref="WpfRenderHarness"/>).
    /// </summary>
    private static void Realize(FrameworkElement element, double width = 1209, double height = 700)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);

        host.Measure(new Size(width, height));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
        host.UpdateLayout();

        Assert.True(element.DesiredSize.Height > 0,
            $"{element.GetType().Name} measured to zero height — its content did not realize");
    }

    // =====================================================================================
    //  the three panels that are new on disk in Phase 4
    // =====================================================================================

    [Fact]
    public void BrainDrainPanel_Realizes()
        => OnStaThread(() => Realize(new BrainDrainFeatureControl(), 488, 640));

    [Fact]
    public void SchedulerRackPanel_Realizes()
        => OnStaThread(() => Realize(new SchedulerRackPanel(), 488, 640));

    [Fact]
    public void RampRackPanel_Realizes()
        => OnStaThread(() => Realize(new RampRackPanel(), 488, 640));

    [Fact]
    public void TheThreeStudioPanelRootsAreStillPanels()
    {
        // MainWindow.SessionFeatureLock.cs inserts the session-lock banner with
        // ((Panel)content.Content).Children.Insert(0, banner). A root that stops being a Panel
        // (a Border or a ScrollViewer would both look fine on screen) silently loses the banner
        // — and on the Brain Drain panel it would also lose the belt-and-braces ChkEnable sweep.
        OnStaThread(() =>
        {
            Assert.IsAssignableFrom<Panel>(new BrainDrainFeatureControl().Content);
            Assert.IsAssignableFrom<Panel>(new SchedulerRackPanel().Content);
            Assert.IsAssignableFrom<Panel>(new RampRackPanel().Content);
        });
    }

    [Fact]
    public void BrainDrainKeepsTheConventionalMasterToggleName()
    {
        // ApplySessionLockToFeaturePopup does a belt-and-braces FindName("ChkEnable") because
        // three of the older panels never marked their master toggle. Brain Drain follows the
        // convention, so it is covered by both halves.
        OnStaThread(() =>
        {
            var p = new BrainDrainFeatureControl();
            Assert.True(p.FindName("ChkEnable") is CheckBox, "Brain Drain master toggle is not named ChkEnable");
            Assert.True(p.FindName("SliderIntensity") is Slider);
            Assert.True(p.FindName("ChkHighRefresh") is CheckBox);
        });
    }

    [Fact]
    public void BrainDrainIntensitySliderCoversTheWholePercentRange()
    {
        // A WPF Slider defaults to Maximum=10, which silently clamps a percent on the way in and
        // then writes the clamped value straight back out. The dead ProgressionTab copy carries an
        // explicit 1..100 for exactly this reason; the rescue must not reintroduce the bug.
        OnStaThread(() =>
        {
            var slider = (Slider)new BrainDrainFeatureControl().FindName("SliderIntensity")!;
            Assert.Equal(1, slider.Minimum);
            Assert.Equal(100, slider.Maximum);
        });
    }

    [Fact]
    public void TheSchedulerAndRampPanelsHostTheLiveEditorsNotTheGhostTwins()
    {
        // The dead ProgressionTab copy has no ramp-curve combo, and CmbRampCurve is the one
        // control that reshapes a RUNNING session (SessionEngine resolves RampCurve every tick).
        // Hosting the live control is what keeps it reachable.
        OnStaThread(() =>
        {
            var ramp = new RampRackPanel();
            Assert.NotNull(ramp.Inner);
            Assert.True(ramp.Inner.FindName("CmbRampCurve") is ComboBox,
                "the rack's ramp panel lost CmbRampCurve — it is hosting the poorer ProgressionTab twin");

            var sched = new SchedulerRackPanel();
            Assert.NotNull(sched.Inner);
        });
    }

    [Fact]
    public void BothTimingPanelsFireThePopupsSingleSchedulerRampBarkKey()
    {
        // Deriving the key from the type name would give "SchedulerRackPanel"/"RampRackPanel",
        // for which no rule exists in any mod. One key, two rows, is the intent.
        Assert.Equal("SchedulerRamp", SchedulerRackPanel.BarkFeatureKey);
        Assert.Equal("SchedulerRamp", RampRackPanel.BarkFeatureKey);
    }

    [Fact]
    public void TheTutorialSpotlightNamesResolveOnTheRackPanels()
    {
        // TutorialService steps prog_scheduler/prog_ramp were re-targeted at these names, and
        // TutorialOverlay.FindElementByName matches FrameworkElement.Name while walking the visual
        // tree — so the name has to be on the element itself, not only a passthrough. The names
        // deliberately differ from the ProgressionTab twins' ("SchedulerPanel"/"RampPanel"), which
        // are still in the tree until Phase 8 and would win the walk.
        OnStaThread(() =>
        {
            Assert.Equal("StudioSchedulerPanel", ((FrameworkElement)new SchedulerRackPanel().Content).Name);
            Assert.Equal("StudioRampPanel", ((FrameworkElement)new RampRackPanel().Content).Name);
        });
    }

    // =====================================================================================
    //  the assembled rack
    // =====================================================================================

    [Fact]
    public void TheWholeStudioPageRealizes()
    {
        // All fifteen modules in one tree, including the whole HapticsTabView page. This is the
        // test that would catch a module whose StaticResource lived in MainWindow.xaml's own
        // Window.Resources: a UserControl's XAML is parsed before it is attached to MainWindow,
        // so those keys never resolve.
        OnStaThread(() => Realize(new StudioTabView()));
    }

    [Fact]
    public void TheDetailPaneIsWiderThanThePopupEveryPanelWasAuthoredFor()
    {
        // The silent-clip guard, in the only direction a unit test can prove. Twelve of the
        // fifteen modules are Features/*FeatureControl panels authored against
        // FeaturePopupWindow's fixed 520x640 window (488px of content); the rack has to give them
        // AT LEAST that, or a panel that fitted in the popup starts overflowing with no scrollbar
        // (HorizontalScrollBarVisibility is Disabled on every host). None of them declares a hard
        // Width or MinWidth, so wider is always safe and narrower is the whole hazard.
        //
        // The real-world width check still belongs to the UIA play-test sweep — this only proves
        // the geometry cannot regress below the floor.
        OnStaThread(() =>
        {
            // The MainWindow canvas is a fixed 1489x901 Viewbox and StudioTab sits in the content
            // column with Margin="10,5,10,10".
            const double contentWidth = 1489 - 20;

            var page = new StudioTabView();
            Realize(page, contentWidth, 891);

            var rack = (FrameworkElement)page.FindName("RackList")!;
            var detail = (FrameworkElement)page.FindName("DetailHost")!;

            Assert.True(detail.ActualWidth >= 488,
                $"the rack's detail pane is {detail.ActualWidth:F0}px — narrower than the 488px "
                + "popup every Features/* panel was authored against; they will clip silently");

            // And the rack list keeps its contracted 230px, which is what the pane's width is
            // derived from — a widened rack is how the pane quietly shrinks.
            Assert.True(rack.ActualWidth > 0 && rack.ActualWidth <= 230,
                $"rack list measured {rack.ActualWidth:F0}px, expected <= the contracted 230px");
        });
    }

    [Fact]
    public void EveryPassthroughOnTheRackResolvesToARealControl()
    {
        // Reflective on purpose: the passthroughs are contributed by per-module partial files
        // (StudioTabView.<Area>.cs) so a hand-maintained list would miss the next one.
        OnStaThread(() =>
        {
            var page = new StudioTabView();

            var passthroughs = typeof(StudioTabView)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
                .Where(p => typeof(DependencyObject).IsAssignableFrom(p.PropertyType))
                .Where(p => p.DeclaringType == typeof(StudioTabView))
                .ToList();

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
    public void TheHapticsPageIsReachableThroughTheOnePassthroughMainWindowUses()
    {
        // MainWindow.HapticsTab => StudioTab?.HapticsPanel. ~140 HapticsTab.<x:Name> dereferences
        // across eight MainWindow partials go through it, none of them null-guarded on the inner
        // names. If this returns null the whole haptics page NullReferences on first paint.
        OnStaThread(() =>
        {
            var page = new StudioTabView();
            Assert.NotNull(page.HapticsPanel);

            // Two names from opposite ends of the page, plus the premium gate MainWindow.Patreon.cs
            // paints and the enable toggle the rack's dot listens to.
            Assert.NotNull(page.HapticsPanel.FindName("ChkHapticsEnabled"));
            Assert.NotNull(page.HapticsPanel.FindName("HapticsGate"));
            Assert.NotNull(page.HapticsPanel.FindName("HapticsContentGrid"));
        });
    }

    [Fact]
    public void EveryRackKeySelectsItsOwnPanelAndNothingElse()
    {
        // FocusRackEntry(key) is the contract ShowTab("haptics"), the tutorial's FocusStudioRack
        // and (from Phase 3) the Home mosaic tiles all navigate through. A key that selects
        // nothing is a silently dead door.
        OnStaThread(() =>
        {
            var page = new StudioTabView();

            foreach (var key in RackKeys)
            {
                page.FocusRackEntry(key);
                Assert.Equal(key, page.SelectedRackKey);
            }

            // An unknown key must be a quiet no-op that leaves the selection alone, never a throw
            // — every caller passes it blind.
            page.FocusRackEntry("flash");
            page.FocusRackEntry("no-such-module");
            page.FocusRackEntry(null);
            Assert.Equal("flash", page.SelectedRackKey);
        });
    }

    [Fact]
    public void ExactlyOneModulePanelIsVisibleAtATime()
    {
        OnStaThread(() =>
        {
            var page = new StudioTabView();
            var host = (Grid)page.FindName("DetailHost")!;

            foreach (var key in RackKeys)
            {
                page.FocusRackEntry(key);
                var visible = host.Children.Cast<UIElement>()
                                  .Count(c => c.Visibility == Visibility.Visible);
                Assert.True(visible == 1, $"rack key '{key}' left {visible} panels visible, expected exactly 1");
            }
        });
    }

    [Fact]
    public void TheSessionLockSweepListCoversEveryHostedFeaturePanel()
    {
        // MainWindow.ApplySessionLockToStudioRack walks HostedFeaturePanels and runs the POPUP
        // painter over each. Flash, Spiral and PinkFilter leave their master ChkEnable unmarked
        // and rely entirely on that painter's FindName("ChkEnable"), so anything dropped from this
        // list is a master toggle left live mid-session.
        OnStaThread(() =>
        {
            var page = new StudioTabView();
            var panels = page.HostedFeaturePanels;

            // 15 rack entries; Haptics is deliberately excluded (already swept by name as
            // HapticsTab in ApplySessionLockToTabs, and its Content is a ScrollViewer not a Panel).
            Assert.Equal(RackKeys.Length - 1, panels.Count);
            Assert.DoesNotContain(panels, p => p is HapticsTabView);

            foreach (var name in new[] { "FlashFeatureControl", "SpiralFeatureControl", "PinkFilterFeatureControl" })
                Assert.True(panels.Any(p => p.GetType().Name == name),
                    $"{name} is not in HostedFeaturePanels — its unmarked ChkEnable would stay live mid-session");

            // And the belt-and-braces path has something to find on all three.
            foreach (var p in panels.Where(p => p.GetType().Name is "FlashFeatureControl"
                                                or "SpiralFeatureControl" or "PinkFilterFeatureControl"))
                Assert.True(p.FindName("ChkEnable") is CheckBox,
                    $"{p.GetType().Name}.ChkEnable vanished — the popup painter's fallback now finds nothing");
        });
    }

    [Fact]
    public void TheBubblePopEggHintSurvivedTheReHost()
    {
        // Preservation item: the persona line on the bubble trigger ("careful — <persona> loves
        // these…"). It lives on the panel, so re-hosting must not have lost it.
        OnStaThread(() =>
        {
            var panel = new StudioTabView().HostedFeaturePanels
                                           .First(p => p.GetType().Name == "BubblePopFeatureControl");
            Assert.True(panel.FindName("TxtTriggerEggHint") is System.Windows.Controls.TextBlock);
        });
    }

    [Fact]
    public void TheFlashExclusionBoxIsOnTheRackAndHasExactlyOneEditor()
    {
        // G12. FlashFeatureControl is the single surface; the legacy save-side mirror was deleted
        // in #859, so nothing can overwrite the user's choice. Slider bounds must match
        // AppSettings.FlashCenterExclusionPercent's own Math.Clamp(value, 5, 60) or the round trip
        // silently clamps.
        OnStaThread(() =>
        {
            var flash = new StudioTabView().HostedFeaturePanels
                                           .First(p => p.GetType().Name == "FlashFeatureControl");
            Assert.True(flash.FindName("ChkFlashAvoidCenter") is CheckBox);
            var slider = (Slider)flash.FindName("SliderCenterExclusion")!;
            Assert.Equal(5, slider.Minimum);
            Assert.Equal(60, slider.Maximum);
        });
    }

    // =====================================================================================
    //  bark parity (pure — reads the shipped rule files, no WPF)
    // =====================================================================================

    /// <summary>The rack's contract order. Duplicated here on purpose: this list IS the assertion.</summary>
    private static readonly string[] RackKeys =
    {
        "flash", "video", "subliminal", "spiral", "pinkfilter", "visuals",
        "bubbles", "bubblecount", "lockcard", "bouncingtext",
        "mindwipe", "braindrain", "haptics",
        "scheduler", "ramp",
    };

    /// <summary>
    /// The <c>NotifyFeatureOpened</c> keys the rack fires, which must stay the keys the
    /// FeaturePopupWindow path fired (type name minus "FeatureControl").
    /// </summary>
    private static readonly string[] RackBarkFeatures =
    {
        "Flash", "Video", "Subliminal", "Spiral", "PinkFilter", "Visuals",
        "BubblePop", "BubbleCount", "LockCard", "BouncingText",
        "MindWipe", "BrainDrain", "SchedulerRamp",
    };

    [Fact]
    public void EveryRackBarkKeyHasRulesInEveryBuiltInMod()
    {
        // The popup path's feature_eq values are voiced content: one rule per built-in mod matches
        // on each of them. Re-hosting must not have renamed one. "BrainDrain" was the single known
        // gap — a NEW feature_eq value introduced by the G2 rescue — and this test was written as
        // "all but that one" so that the day the rules landed it would demand promotion rather than
        // silently passing. The feat_braindrain rules are now in all three built-in mods, so the
        // gap list is empty and stays empty.
        var known = new HashSet<string>(RackBarkFeatures, StringComparer.Ordinal);
        var missingEverywhere = new List<string>();

        foreach (var mod in BuiltInModRuleFiles())
        {
            var featureValues = FeatureEqValues(mod);
            foreach (var key in known)
                if (!featureValues.Contains(key))
                    missingEverywhere.Add($"{Path.GetFileName(Path.GetDirectoryName(mod))}:{key}");
        }

        Assert.Empty(missingEverywhere);
    }

    [Fact]
    public void TheStudioTabKeyHasNavigationRulesInEveryBuiltInMod()
    {
        // "studio" is a genuinely new ShowTab key, and PLAN §2.2 requires a new key to ship with
        // new bark rules or the door is voiceless.
        foreach (var mod in BuiltInModRuleFiles())
        {
            var ids = StudioTabRuleIds(mod).ToList();
            Assert.True(ids.Count >= 3,
                $"{Path.GetFileName(Path.GetDirectoryName(mod))} has only {ids.Count} tab_eq:'studio' rules");
        }
    }

    [Fact]
    public void TheBuiltInModsStillAgreeOnTheirStudioRuleIds()
    {
        // A rule id present in one mod and not another is how a mod-specific silence gets shipped.
        var perMod = BuiltInModRuleFiles()
            .ToDictionary(f => Path.GetFileName(Path.GetDirectoryName(f))!,
                          f => new HashSet<string>(StudioTabRuleIds(f), StringComparer.Ordinal));

        var first = perMod.Values.First();
        foreach (var (mod, ids) in perMod)
            Assert.True(first.SetEquals(ids),
                $"{mod} studio rule ids differ: [{string.Join(",", ids.OrderBy(x => x))}] "
                + $"vs [{string.Join(",", first.OrderBy(x => x))}]");
    }

    [Fact]
    public void EveryBuiltInModsRuleFileStillParsesStrictly()
    {
        // Newtonsoft is lenient; System.Text.Json is not. The loc files were bitten by exactly
        // this (CLAUDE.md, Localization known issue 11) and rule files are edited the same way.
        foreach (var f in BuiltInModRuleFiles())
        {
            var ex = Record.Exception(() => JsonDocument.Parse(File.ReadAllText(f)));
            Assert.True(ex == null, $"{f} does not parse strictly: {ex?.Message}");
        }
    }

    // ---- helpers -------------------------------------------------------------------------

    private static string RepoRoot()
    {
        // bin/Debug/net8.0 -> Tests/<proj> -> Tests -> repo root
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static IEnumerable<string> BuiltInModRuleFiles()
    {
        var root = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "sounds",
                                "companion_audio", "mods");
        var files = Directory.GetDirectories(root, "builtin-*")
                             .Select(d => Path.Combine(d, "bark_rules.json"))
                             .Where(File.Exists)
                             .OrderBy(f => f)
                             .ToList();
        Assert.True(files.Count >= 3, $"expected the three built-in mods' rule files under {root}, found {files.Count}");
        return files;
    }

    private static IEnumerable<JsonElement> Rules(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var root = doc.RootElement;
        var array = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("rules");
        // Cloned because the JsonDocument is disposed on the way out.
        return array.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static HashSet<string> FeatureEqValues(string file) =>
        new(Rules(file)
            .Where(r => r.TryGetProperty("conditions", out var c) && c.TryGetProperty("feature_eq", out _))
            .Select(r => r.GetProperty("conditions").GetProperty("feature_eq").GetString()!)
            .Where(v => v != null), StringComparer.Ordinal);

    private static IEnumerable<string> StudioTabRuleIds(string file) =>
        Rules(file)
            .Where(r => r.TryGetProperty("conditions", out var c)
                        && c.TryGetProperty("tab_eq", out var t)
                        && t.GetString() == "studio")
            .Select(r => r.GetProperty("id").GetString()!)
            .Where(id => id != null);
}
