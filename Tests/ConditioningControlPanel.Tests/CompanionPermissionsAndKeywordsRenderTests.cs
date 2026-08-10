using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Views.Controls.Companion;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// PHASE 5 (Companion door) — the two surfaces that were physically moved, and the passthrough
/// seams that keep MainWindow's handlers pointing at them.
///
/// <para><b>Why this suite exists.</b> Both controls were cut out of one XAML file and pasted into
/// a <see cref="UserControl"/>. Two failure modes survive a clean compile and neither is theoretical
/// here:</para>
/// <list type="bullet">
///   <item>a <c>{StaticResource}</c> key that lived in the old parent's local <c>Resources</c> and
///   did not follow. <c>AiPermissionsGrid</c> came off <c>LabTabView</c>, whose card styles were
///   <c>Grid.Resources</c> (<c>LabCard</c>/<c>LabCardTitle</c>/<c>LabZoneTag</c>) — StaticResource
///   resolves while the tree is built, so a missed key builds green and then throws
///   <c>ResourceReferenceKeyNotFoundException</c> the first time a user opens the door.</item>
///   <item>an <c>x:Name</c> dropped in the move while a MainWindow partial still dereferences it
///   through a passthrough property — which compiles, then NullReferences at runtime.</item>
/// </list>
///
/// <para>The passthrough sweeps are reflective on purpose: the permissions card is reached through
/// <i>two</i> hops (<c>CompanionTabView</c> → <c>CompanionRoomView</c> → <c>AiPermissionsGrid</c>)
/// and a hand-written list is exactly the thing that goes stale when a nineteenth name is added.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class CompanionPermissionsAndKeywordsRenderTests
{
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    private static T Realize<T>(T control, double width) where T : FrameworkElement
    {
        control.Measure(new Size(width, double.PositiveInfinity));
        control.Arrange(new Rect(new Point(0, 0), new Size(width, Math.Max(1, control.DesiredSize.Height))));
        control.UpdateLayout();

        Assert.True(control.DesiredSize.Height > 0,
            $"{control.GetType().Name} measured to zero height — its content did not realize");
        return control;
    }

    // =====================================================================================
    //  the two moved controls realize at all
    // =====================================================================================

    [Fact]
    public void AiPermissionsGrid_Realizes()
    {
        // The card is full-width between the Engine Room (Z7) and the Workshop (Z8), so it is
        // measured at the room's own width.
        OnStaThread(() => Realize(new AiPermissionsGrid(), 1240));
    }

    [Fact]
    public void KeywordTriggersPanel_Realizes()
    {
        // Collapsed by default (it is an Expander), so realize it BOTH ways: a StaticResource inside
        // an Expander's content is not resolved until the content is expanded, which means a
        // collapsed-only render would prove nothing about the rows inside.
        OnStaThread(() =>
        {
            Realize(new KeywordTriggersPanel(), 900);

            var open = new KeywordTriggersPanel();
            open.KeywordTriggersExpander.IsExpanded = true;
            Realize(open, 900);
        });
    }

    [Fact]
    public void KeywordTriggersPanel_RevealOpensTheDrawerWithoutADispatcherPump()
    {
        // The Awareness "advanced editor" link calls this on an account with no preset installed —
        // before Phase 5 that branch dead-ended in the App Info popup. It must not need a message
        // loop to have run, and the deferred BringIntoView must not throw on a parentless control.
        OnStaThread(() =>
        {
            var panel = new KeywordTriggersPanel();
            panel.Measure(new Size(900, double.PositiveInfinity));
            panel.RevealTriggerEditor();
            Assert.True(panel.KeywordTriggersExpander.IsExpanded);
        });
    }

    // =====================================================================================
    //  the permissions card: every moved name, through both hops
    // =====================================================================================

    /// <summary>
    /// The sixteen names <c>MainWindow.Patreon.cs</c> / <c>.UiUpdates.cs</c> / <c>.Presets.cs</c> /
    /// <c>.xaml.cs</c> dereference without a null guard, exactly as the Lab tab code they replaced
    /// did. Spelled out rather than reflected because the point of this list is that the *spelling*
    /// survived the move — a rename would still pass a reflective sweep.
    /// </summary>
    private static readonly string[] MovedPermissionNames =
    {
        "LabAiMemoryHeroBrush", "LabEffectsNeedsLocalNotice",
        "ChkCapEffects", "EffectPermsPanel",
        "SliderMaxHapticIntensity", "TxtMaxHapticIntensity",
        "ChkAllowFlash", "ChkAllowVideo", "ChkAllowAudio", "ChkAllowBubbles", "ChkAllowSubliminal",
        "ChkAllowOverlay", "ChkAllowLockCard", "ChkAllowBounce", "ChkAllowHaptic", "ChkAllowGetBackToMe",
        "HelpBtnGetBackToMe", "ChkChatMemoryEnabled"
    };

    [Fact]
    public void EveryMovedPermissionNameIsReachableThroughBothPassthroughHops()
    {
        OnStaThread(() =>
        {
            var tab = new CompanionTabView();

            var missing = new List<string>();
            foreach (var name in MovedPermissionNames)
            {
                var property = typeof(CompanionTabView).GetProperty(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null) { missing.Add($"{name} (no passthrough on CompanionTabView)"); continue; }

                object? value;
                try { value = property.GetValue(tab); }
                catch (Exception ex) { missing.Add($"{name} threw {ex.GetType().Name}"); continue; }
                if (value == null) missing.Add($"{name} resolved to null");
            }

            Assert.True(missing.Count == 0,
                "moved permission controls a MainWindow partial will NullReference on: " + string.Join(", ", missing));
        });
    }

    [Fact]
    public void EveryPassthroughOnTheCompanionTabResolves()
    {
        // Reflective sweep over BOTH hops. Catches a passthrough added later that points at a name
        // the card no longer has.
        OnStaThread(() =>
        {
            var tab = new CompanionTabView();

            var broken = new List<string>();
            foreach (var p in DependencyPassthroughs(typeof(CompanionTabView)))
            {
                object? value;
                try { value = p.GetValue(tab); }
                catch (Exception ex) { broken.Add($"CompanionTabView.{p.Name} threw {ex.GetType().Name}"); continue; }
                if (value == null) broken.Add($"CompanionTabView.{p.Name} is null");
            }

            var room = new CompanionRoomView();
            foreach (var p in DependencyPassthroughs(typeof(CompanionRoomView)))
            {
                object? value;
                try { value = p.GetValue(room); }
                catch (Exception ex) { broken.Add($"CompanionRoomView.{p.Name} threw {ex.GetType().Name}"); continue; }
                if (value == null) broken.Add($"CompanionRoomView.{p.Name} is null");
            }

            Assert.True(broken.Count == 0, "passthroughs that do not resolve: " + string.Join(", ", broken));
        });
    }

    private static List<PropertyInfo> DependencyPassthroughs(Type owner) => owner
        .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
        .Where(p => typeof(DependencyObject).IsAssignableFrom(p.PropertyType))
        .Where(p => p.DeclaringType == owner)
        .ToList();

    [Fact]
    public void TheTenEffectSwitchesKeepTheTagsThatNameTheirSetting()
    {
        // One handler serves all ten; each switch names its own CompanionPromptSettings property
        // through Tag, so re-ordering the UniformGrid can never remap a saved permission. A dropped
        // or retyped Tag would silently write the wrong setting — nothing else would notice.
        var expected = new[]
        {
            ("ChkAllowFlash", "Flash"), ("ChkAllowVideo", "Video"), ("ChkAllowAudio", "Audio"),
            ("ChkAllowBubbles", "Bubbles"), ("ChkAllowSubliminal", "Subliminal"),
            ("ChkAllowOverlay", "Overlay"), ("ChkAllowLockCard", "LockCard"),
            ("ChkAllowBounce", "Bounce"), ("ChkAllowHaptic", "Haptic"),
            ("ChkAllowGetBackToMe", "GetBackToMe")
        };

        OnStaThread(() =>
        {
            var grid = Realize(new AiPermissionsGrid(), 1240);
            foreach (var (name, tag) in expected)
            {
                var box = grid.FindName(name) as CheckBox;
                Assert.True(box != null, $"{name} is missing from the permissions grid");
                Assert.Equal(tag, box!.Tag as string);
            }
        });
    }

    // =====================================================================================
    //  the Tier 2 lockband
    // =====================================================================================

    [Fact]
    public void WithNoEntitlementTheEffectsHalfIsDisabledAndBanded_AndTheMemoryHalfIsNot()
    {
        // App.Patreon is null on the test thread, and TierGate fails CLOSED by contract, so this is
        // the Free-account rendering. Two separate assertions matter here:
        //   * the effects half is DISABLED, not hidden — the plan's "locked cards stay visible as
        //     the permanent ad" rule, and the reason the veil is never the only barrier;
        //   * chat memory is NOT gated, because it runs on cloud AI, which is Tier 1. Gating it
        //     would take a feature away from accounts that pay for it.
        OnStaThread(() =>
        {
            var grid = Realize(new AiPermissionsGrid(), 1240);
            grid.ApplyTierGate();

            var gateHost = (FrameworkElement)grid.FindName("EffectsGateHost");
            var band = (FrameworkElement)grid.FindName("EffectsLockBand");
            var memoryToggle = (CheckBox)grid.FindName("ChkChatMemoryEnabled");

            Assert.False(grid.IsLabEntitled);
            Assert.False(gateHost.IsEnabled);
            Assert.True(gateHost.Visibility == Visibility.Visible, "the locked half must stay visible, not hidden");
            Assert.Equal(Visibility.Visible, band.Visibility);
            Assert.True(memoryToggle.IsEnabled, "chat memory is Tier 1 and must never sit behind the Tier 2 band");
        });
    }

    [Fact]
    public void TheLockbandCopyComesFromTierGateAndIsNotEmpty()
    {
        // The band, the toast a blocked click raises and any future CLI refusal all have to say the
        // same sentence — that is the whole reason TierVerdict.Reason exists.
        OnStaThread(() =>
        {
            var grid = Realize(new AiPermissionsGrid(), 1240);
            grid.ApplyTierGate();

            var copy = (TextBlock)grid.FindName("TxtEffectsLockCopy");
            var expected = ConditioningControlPanel.Services.TierGate
                .RequiresLab(ConditioningControlPanel.Localization.Loc.Get("lab_ai_effects_memory_title")).Reason;

            Assert.False(string.IsNullOrWhiteSpace(copy.Text), "the lockband rendered with no refusal copy");
            Assert.Equal(expected, copy.Text);
        });
    }

    [Fact]
    public void ApplyTierGateIsIdempotentAndNeverThrows()
    {
        // It is called from Loaded, IsVisibleChanged, SyncLabEffectPermsUI and both verdicts of
        // UpdateUnlockablesVisibility. Any of those can fire twice in a row.
        OnStaThread(() =>
        {
            var grid = Realize(new AiPermissionsGrid(), 1240);
            var band = (FrameworkElement)grid.FindName("EffectsLockBand");

            grid.ApplyTierGate();
            var first = band.Visibility;
            grid.ApplyTierGate();
            grid.ApplyTierGate();

            Assert.Equal(first, band.Visibility);
        });
    }

    // =====================================================================================
    //  the keywords rescue: names, and the seam onto the Awareness tab
    // =====================================================================================

    /// <summary>
    /// The names <c>MainWindow.KeywordTriggers.cs</c> re-pointed from <c>PatreonTab.</c> to
    /// <c>AwarenessTab.</c>. Same spelling-survives-the-move argument as the permissions list.
    /// </summary>
    private static readonly string[] RescuedKeywordNames =
    {
        "SliderKeywordBufferTimeout", "TxtKeywordBufferTimeout",
        "SliderKeywordSessionMultiplier", "TxtKeywordSessionMultiplier",
        "TxtScreenOcrOffHint", "ScreenOcrIntervalPanel", "SliderScreenOcrInterval",
        "TxtScreenOcrInterval", "CmbOcrConfirmation",
        "TxtHighlightOffHint", "HighlightDurationPanel", "CmbOcrHighlightMode",
        "SliderKeywordHighlightDuration", "TxtKeywordHighlightDuration",
        "KeywordTriggerListPanel", "HelpBtnKeywordTriggers", "HelpBtnScreenOcr"
    };

    [Fact]
    public void EveryRescuedKeywordNameResolvesOffTheAwarenessTab()
    {
        OnStaThread(() =>
        {
            var tab = new AwarenessTabView();

            var missing = new List<string>();
            foreach (var name in RescuedKeywordNames)
            {
                var property = typeof(AwarenessTabView).GetProperty(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null) { missing.Add($"{name} (no passthrough on AwarenessTabView)"); continue; }

                object? value;
                try { value = property.GetValue(tab); }
                catch (Exception ex) { missing.Add($"{name} threw {ex.GetType().Name}"); continue; }
                if (value == null) missing.Add($"{name} resolved to null");
            }

            Assert.True(missing.Count == 0,
                "rescued keyword controls MainWindow.KeywordTriggers.cs will NullReference on: "
                + string.Join(", ", missing));
        });
    }

    [Fact]
    public void TheAwarenessTabRealizesWithTheRescuedDrawerMounted()
    {
        OnStaThread(() =>
        {
            var tab = Realize(new AwarenessTabView(), 980);
            Assert.NotNull(tab.KeywordPanel);
            Assert.False(tab.KeywordPanel.KeywordTriggersExpander.IsExpanded,
                "the rescue drawer must open closed — it is an advanced surface, not the default view");
        });
    }

    [Fact]
    public void TheSliderRangesTheRescueSeedsIntoStillCoverTheirSettings()
    {
        // SyncKeywordRescuePanelUi clamps each seeded value to its control's range. If a Minimum or
        // Maximum drifted from the dead tab's, the clamp would quietly rewrite a saved setting —
        // which is exactly the class of bug the removed global-cooldown mirror was (a 120-max twin
        // writing 120 back over a 150 the user chose).
        OnStaThread(() =>
        {
            var p = new KeywordTriggersPanel();

            Assert.Equal(1000, p.SliderKeywordBufferTimeout.Minimum);
            Assert.Equal(10000, p.SliderKeywordBufferTimeout.Maximum);

            Assert.Equal(1.0, p.SliderKeywordSessionMultiplier.Minimum);
            Assert.Equal(3.0, p.SliderKeywordSessionMultiplier.Maximum);

            Assert.Equal(2, p.SliderScreenOcrInterval.Minimum);
            Assert.Equal(10, p.SliderScreenOcrInterval.Maximum);

            Assert.Equal(0.3, p.SliderKeywordHighlightDuration.Minimum);
            Assert.Equal(5.0, p.SliderKeywordHighlightDuration.Maximum);
        });
    }

    [Fact]
    public void TheOcrCombosKeepTheItemOrderTheHandlersCastFrom()
    {
        // Both handlers map SelectedIndex straight onto a setting: confirmation scans are
        // index + 1, and the highlight mode is All / RandomSubset in that order. The item count and
        // order ARE the contract.
        OnStaThread(() =>
        {
            var p = new KeywordTriggersPanel();
            Assert.Equal(3, p.CmbOcrConfirmation.Items.Count);
            Assert.Equal(2, p.CmbOcrHighlightMode.Items.Count);
        });
    }

    // =====================================================================================
    //  the contracts a render pass cannot see
    // =====================================================================================

    [Fact]
    public void NeitherMovedSurfaceAddsAStoryboardToTheCompanionDoor()
    {
        // The room's motion budget is ONE Forever storyboard (the hero's portrait breathe,
        // CompanionTheme.xaml). A loop added here would be unreachable from ParkClocks/ResumeClocks
        // and from the motion kill-switch, and nothing at runtime would say so.
        foreach (var relative in new[]
                 {
                     Path.Combine("Views", "Controls", "Companion", "AiPermissionsGrid.xaml"),
                     Path.Combine("Views", "Controls", "Companion", "KeywordTriggersPanel.xaml")
                 })
        {
            var xaml = File.ReadAllText(Path.Combine(SourceRoot(), relative));
            Assert.DoesNotContain("<Storyboard", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("RepeatBehavior", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("BeginStoryboard", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NeitherMovedSurfaceIntroducesASecondMicOrWebcamEditor()
    {
        // Phase 2 made Settings/Devices the single truth for both. A rescue that quietly re-hosted a
        // device picker would put the app back to two editors for one property.
        foreach (var relative in new[]
                 {
                     Path.Combine("Views", "Controls", "Companion", "AiPermissionsGrid.xaml"),
                     Path.Combine("Views", "Controls", "Companion", "KeywordTriggersPanel.xaml")
                 })
        {
            var xaml = File.ReadAllText(Path.Combine(SourceRoot(), relative));
            Assert.DoesNotContain("CmbMicDevice", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("CmbWebcamDevice", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheRescuedPanelDoesNotShipASecondEditorForAnAwarenessMasterProperty()
    {
        // PLAN §Phase-2's exit rule is one editor per property, and the six masters below live a few
        // hundred pixels above on the SAME page. Re-hosting them would put two editors for
        // ScreenOcrEnabled / KeywordGlobalCooldownSeconds / KeywordHighlightEnabled on one screen.
        var xaml = File.ReadAllText(Path.Combine(SourceRoot(),
            "Views", "Controls", "Companion", "KeywordTriggersPanel.xaml"));

        foreach (var master in new[]
                 {
                     "ChkScreenOcrEnabled", "ChkKeywordHighlightEnabled", "ChkHighlightVisibleInCapture",
                     "SliderKeywordGlobalCooldown", "BtnKeywordTriggersStartStop", "TxtKeywordTriggersLocked",
                     "CmbAwarenessAppScope"
                 })
        {
            Assert.False(xaml.Contains("x:Name=\"" + master + "\"", StringComparison.Ordinal),
                $"{master} is a master that lives on the Awareness page above — the drawer must follow it, not duplicate it");
        }
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "ConditioningControlPanel project not found above " + AppContext.BaseDirectory);
    }
}
