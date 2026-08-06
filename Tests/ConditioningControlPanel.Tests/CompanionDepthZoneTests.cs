using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Pins the "depth zones" of the redesigned Companion tab — Z4 Make her yours, Z5 What she can
/// see, Z6 Her attention, Z7 the Engine Room, Z8 the Workshop
/// (<c>ConditioningControlPanel/Views/Controls/Companion/</c>).
///
/// These five zones are the ones that carry real decision logic in the *view*: the Engine Room
/// shows one provider panel out of four, the Workshop lights a row up only when the row can
/// actually do something, and Z6 draws a spent meter differently from an empty one. A compile
/// proves none of that, so the checks live here:
///
/// <list type="bullet">
///   <item>Every <c>{local:CmpStr …}</c> key these five XAML files reference has an EN master.
///   A typo'd key renders as the raw key in the UI and nothing else catches it.</item>
///   <item>The one magic number in <c>AttentionGaugeView.xaml</c> (the 0.04 spent sliver its
///   DataTrigger matches) still comes back from <see cref="AttentionCopy"/>.</item>
///   <item>The provider grouping's converter maps all four modes, so no mode can silently render
///   an Engine Room with no panel at all.</item>
///   <item>The Workshop's deep-link anchors and its actionable/inert row split.</item>
/// </list>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class CompanionDepthZoneTests
{
    // =====================================================================================
    //  loc: no zone may reference a key that has no EN master
    // =====================================================================================

    /// <summary>The five XAML files this package owns.</summary>
    private static readonly string[] DepthZoneXaml =
    {
        "MakeHerYoursView.xaml",
        "AwarenessPrivacyView.xaml",
        "AttentionGaugeView.xaml",
        "EngineRoomDrawer.xaml",
        "WorkshopAccordion.xaml"
    };

    /// <summary>Walks up from the test assembly to the zone folder, the way the loc suite does.</summary>
    private static string ZoneFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ConditioningControlPanel",
                                         "Views", "Controls", "Companion");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate Views/Controls/Companion walking up from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void EveryLocKeyTheDepthZonesReferenceHasAnEnMaster()
    {
        var folder = ZoneFolder();
        // {local:CmpStr companion_xxx}  — the markup extension's positional-argument form.
        var pattern = new Regex(@"\{local:CmpStr\s+([a-z0-9_]+)\s*\}", RegexOptions.Compiled);

        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in DepthZoneXaml)
        {
            var path = Path.Combine(folder, file);
            Assert.True(File.Exists(path), $"{file} is missing from {folder}");

            foreach (Match m in pattern.Matches(File.ReadAllText(path)))
            {
                var key = m.Groups[1].Value;
                seen.Add(key);
                if (!CompanionLocStaging.English.ContainsKey(key)) missing.Add($"{file}: {key}");
            }
        }

        // Guard against the check going vacuous if the extension is ever renamed: these five
        // zones carry well over a dozen localized strings between them.
        Assert.True(seen.Count >= 15,
            $"only {seen.Count} CmpStr keys found across the depth zones — the scan regex has rotted");

        Assert.True(missing.Count == 0,
            "loc keys referenced by the depth zones with no EN master: " + string.Join(", ", missing));
    }

    [Fact]
    public void DepthZonesCarryNoBakedUserFacingText()
    {
        // Not an exhaustive lint — it catches the one mistake that is easy to make while filling a
        // zone: typing a label straight into Text="…" instead of routing it through CmpStr or a
        // viewmodel property. Bindings, markup extensions and pure glyphs are all fine.
        var folder = ZoneFolder();
        var literal = new Regex("Text=\"(?!\\{)([^\"]+)\"", RegexOptions.Compiled);
        var offenders = new List<string>();

        foreach (var file in DepthZoneXaml)
        {
            foreach (Match m in literal.Matches(File.ReadAllText(Path.Combine(folder, file))))
            {
                var text = m.Groups[1].Value.Trim();
                if (text.Length == 0) continue;
                // a lone glyph (✕, ▼) is iconography, not copy
                if (text.Length <= 2) continue;
                offenders.Add($"{file}: \"{text}\"");
            }
        }

        Assert.True(offenders.Count == 0,
            "baked user-facing text in the depth zones (use {local:CmpStr} or a VM property): " +
            string.Join(", ", offenders));
    }

    // =====================================================================================
    //  Z6 — the one number AttentionGaugeView.xaml knows
    // =====================================================================================

    [Fact]
    public void SpentSliver_IsADrawingMinimum_NotAStateFlag()
    {
        // The view no longer matches this number: it used to style the empty bar with
        //     <DataTrigger Binding="{Binding BarFraction}" Value="0.04">
        // which cannot tell the sliver apart from a real 4% (they are the same double), so a user
        // with 4 chats left got the "spent" treatment. The trigger asks IsSpent now, and this test
        // pins the reason: the collision is real and unavoidable at the BarFraction level.
        double sliver = AttentionCopy.BarFractionFor(0.0);
        Assert.Equal(AttentionCopy.SpentBarFraction, sliver, 10);
        Assert.Equal(sliver, AttentionCopy.BarFractionFor(AttentionCopy.SpentBarFraction), 10);

        Assert.True(AttentionCopy.IsSpent(0.0));
        Assert.False(AttentionCopy.IsSpent(AttentionCopy.SpentBarFraction));
        Assert.NotEqual(sliver, AttentionCopy.BarFractionFor(0.05));
    }

    [Fact]
    public void DrainedMeter_StillPromisesHerVoiceKeepsWorking()
    {
        // Doc 01 §5.4: the floor is not mute, and the card has to say so OUT LOUD — at rest, not
        // behind hover. The promise lives in FloorNote for that reason; DetailLine is numbers only
        // and is collapsed until the card is hovered or clicked.
        var drained = MockAttentionGaugeVm.Drained();
        Assert.True(drained.ShowFloorNote);
        Assert.Contains("never runs out", drained.FloorNote, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", drained.FloorNote, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", drained.DetailLine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", drained.StateCopy, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.30)]
    [InlineData(0.08)]
    [InlineData(0.0)]
    public void AttentionCopy_NeverSaysTokens_AtAnyRung(double fraction)
    {
        var copy = CompanionLocStaging.English[AttentionCopy.CopyKeyFor(fraction)];
        Assert.DoesNotContain("token", copy, StringComparison.OrdinalIgnoreCase);
    }

    // =====================================================================================
    //  Z7 — provider grouping
    // =====================================================================================

    [Theory]
    [InlineData(CompanionProviderMode.Off, "Off")]
    [InlineData(CompanionProviderMode.Cloud, "Cloud")]
    [InlineData(CompanionProviderMode.LocalOllama, "LocalOllama")]
    [InlineData(CompanionProviderMode.Custom, "Custom")]
    public void EveryProviderMode_ShowsExactlyItsOwnPanel(CompanionProviderMode mode, string ownParameter)
    {
        var conv = new CompanionEnumToVisibilityConverter();
        string[] all = { "Off", "Cloud", "LocalOllama", "Custom" };

        var visible = all.Where(p =>
            (Visibility)conv.Convert(mode, typeof(Visibility), p, CultureInfo.InvariantCulture)
                == Visibility.Visible).ToArray();

        // exactly one panel, and it is the right one — a mode that matched nothing would render
        // an Engine Room with a status line and no wiring at all
        Assert.Equal(new[] { ownParameter }, visible);
    }

    [Fact]
    public void EveryProviderMode_HasAnExhibit_SoNoPanelShipsUnrendered()
    {
        var modes = CompanionMockGallery.Exhibits.Keys
            .Where(k => k.StartsWith("engine.", StringComparison.OrdinalIgnoreCase))
            .Select(k => CompanionMockGallery.Get(k))
            .OfType<IEngineRoomDrawerVm>()
            .Select(vm => vm.Provider)
            .Distinct()
            .ToArray();

        foreach (CompanionProviderMode mode in Enum.GetValues(typeof(CompanionProviderMode)))
        {
            Assert.Contains(mode, modes);
        }
    }

    [Fact]
    public void LoggedOutEngineRoom_OffersTheInlineLoginRow_RatherThanVeilingThePage()
    {
        var vm = MockEngineRoomDrawerVm.LoggedOut();
        Assert.False(vm.IsLoggedIn);
        Assert.False(string.IsNullOrWhiteSpace(vm.LoginPrompt));
        Assert.False(string.IsNullOrWhiteSpace(vm.LoginButtonLabel));
        // the row only exists inside the cloud panel, so the exhibit has to actually be on cloud
        Assert.Equal(CompanionProviderMode.Cloud, vm.Provider);
    }

    // =====================================================================================
    //  Z8 — deep-link anchors and the actionable/inert row split
    // =====================================================================================

    [Fact]
    public void WorkshopCarriesTheTwoCellsOtherZonesDeepLinkTo()
    {
        var vm = MockWorkshopAccordionVm.Expanded();
        var titles = vm.Cells.Select(c => c.Title).ToArray();

        Assert.Contains(MockWorkshopAccordionVm.RosterCellTitle, titles);
        Assert.Contains(MockWorkshopAccordionVm.AwarenessCellTitle, titles);
    }

    [Fact]
    public void WorkshopRows_LightUpOnlyWhenTheyCanDoSomething()
    {
        var vm = MockWorkshopAccordionVm.Expanded();
        var rows = vm.Cells.SelectMany(c => c.Rows).ToArray();

        Assert.NotEmpty(rows.Where(r => r.ActivateCommand != null));

        foreach (var row in rows)
        {
            // a caption is prose, never a click target
            if (row.IsCaption) Assert.Null(row.ActivateCommand);
            // a slider's thumb is the control; the row itself must not also swallow the click
            if (row.IsSlider) Assert.Null(row.ActivateCommand);
        }
    }

    [Fact]
    public void EveryWorkshopRowHasALabel_SoNoCellRendersABlankLine()
    {
        var vm = MockWorkshopAccordionVm.Expanded();
        foreach (var row in vm.Cells.SelectMany(c => c.Rows))
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Label));
        }
    }

    // =====================================================================================
    //  Z5 — every deny chip can be lifted again
    // =====================================================================================

    [Fact]
    public void EveryDenyChip_ExposesARemoveCommand()
    {
        foreach (var key in new[] { "awareness.live", "awareness.dormant", "awareness.eyesClosed" })
        {
            var vm = (IAwarenessPrivacyVm)CompanionMockGallery.Get(key)!;
            Assert.NotEmpty(vm.DenyList);
            Assert.All(vm.DenyList, chip => Assert.NotNull(chip.RemoveCommand));
        }
    }

    [Fact]
    public void DormantAwareness_KeepsTheDialUsable_ButNotTheThirdStop()
    {
        var vm = MockAwarenessPrivacyVm.Dormant();
        Assert.True(vm.IsDormant);
        Assert.False(vm.IsEverythingAvailable);
        Assert.False(vm.IsWireLive);
        // designed content, not an empty box
        Assert.False(string.IsNullOrWhiteSpace(vm.DormantCopy));
        // the two bridging stops still drive today's single toggle
        vm.Intensity = AwarenessIntensity.Off;
        Assert.Equal(AwarenessIntensity.Off, vm.Intensity);
        vm.Intensity = AwarenessIntensity.BroadStrokes;
        Assert.Equal(AwarenessIntensity.BroadStrokes, vm.Intensity);
    }

    [Fact]
    public void PageTitles_DefaultToHidden_TheInvertedDefaultMadeVisible()
    {
        foreach (var key in new[] { "awareness.live", "awareness.dormant", "awareness.eyesClosed" })
        {
            var vm = (IAwarenessPrivacyVm)CompanionMockGallery.Get(key)!;
            Assert.False(vm.AllowPageTitles);
        }
    }

    // =====================================================================================
    //  Z4 — the interviewed chip row
    // =====================================================================================

    [Fact]
    public void InterviewedLine_CarriesTheDateOnly_TheVerbsAreButtons()
    {
        var vm = MockMakeHerYoursVm.Interviewed();
        Assert.True(vm.IsInterviewed);
        Assert.False(string.IsNullOrWhiteSpace(vm.InterviewedLine));
        // if the verbs crept back into the string the user would see each of them twice
        Assert.DoesNotContain("re-interview", vm.InterviewedLine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("adjust", vm.InterviewedLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DormantPersonality_SleepsWithoutDisablingAnything()
    {
        var vm = MockMakeHerYoursVm.Dormant();
        Assert.False(vm.IsInterviewAvailable);
        Assert.False(vm.IsInterviewed);
        Assert.False(string.IsNullOrWhiteSpace(vm.InterviewDormantCopy));
        // presets stay usable in bridge mode — Z4 is never gated
        Assert.NotEmpty(((IMakeHerYoursVm)vm).Presets);
    }

    [Fact]
    public void PresetChipsBehaveAsOneRadioGroup()
    {
        var vm = MockMakeHerYoursVm.Live();
        var presets = ((IMakeHerYoursVm)vm).Presets;
        Assert.Single(presets.Where(p => p.IsSelected));

        presets.Last().IsSelected = true;

        Assert.Single(presets.Where(p => p.IsSelected));
        Assert.True(presets.Last().IsSelected);
    }

    // =====================================================================================
    //  render smoke for the states the shared suite does not reach
    // =====================================================================================

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

    private static void Realize(UserControl control, object dataContext, double width)
    {
        control.DataContext = dataContext;
        control.Measure(new Size(width, double.PositiveInfinity));
        control.Arrange(new Rect(new Point(0, 0), new Size(width, Math.Max(1, control.DesiredSize.Height))));
        control.UpdateLayout();
        Assert.True(control.DesiredSize.Height > 0,
            $"{control.GetType().Name} measured to zero height — its content did not realize");
    }

    [Fact]
    public void EngineRoom_RealizesTheCustomByoPanel()
        => OnStaThread(() => Realize(new EngineRoomDrawer(), CompanionMockGallery.Get("engine.custom")!, 1160));

    [Fact]
    public void Workshop_DeepLinksToANamedCell()
    {
        OnStaThread(() =>
        {
            var workshop = new WorkshopAccordion();
            Realize(workshop, MockWorkshopAccordionVm.Collapsed(), 1160);

            workshop.ExpandAndReveal(MockWorkshopAccordionVm.RosterCellTitle);
            Assert.True(workshop.ViewModel!.IsExpanded);

            // an anchor nobody recognises must still open the drawer rather than throw
            workshop.ExpandAndReveal("NO SUCH SHELF");
            workshop.ExpandAndReveal(null);
            Assert.True(workshop.ViewModel!.IsExpanded);
        });
    }

    [Fact]
    public void MakeHerYours_PlayIntro_IsSafeBeforeAndAfterLoad()
    {
        OnStaThread(() =>
        {
            // Before load the shimmer must be a no-op, not a null-reference: PlayIntro is public
            // and the hero calls it on tab switches.
            var card = new MakeHerYoursView();
            card.PlayIntro();

            Realize(card, MockMakeHerYoursVm.Dormant(), 430);
            card.PlayIntro();
        });
    }

    [Fact]
    public void AwarenessPrivacy_CursorBlinkStartsAndStopsIdempotently()
    {
        OnStaThread(() =>
        {
            var card = new AwarenessPrivacyView();
            // stopping a blink that never started is what Unloaded does on a tab never opened
            card.StopCursorBlink();

            Realize(card, MockAwarenessPrivacyVm.Live(), 430);
            card.StartCursorBlink();
            card.StartCursorBlink();
            card.StopCursorBlink();
            card.StopCursorBlink();
        });
    }

    [Fact]
    public void AttentionGauge_RealizesEveryRung_AndOnlyTheLowOnesSell()
    {
        OnStaThread(() =>
        {
            foreach (var key in new[] { "attention.plenty", "attention.saving",
                                        "attention.whispering", "attention.drained" })
            {
                var vm = (IAttentionGaugeVm)CompanionMockGallery.Get(key)!;
                Realize(new AttentionGaugeView(), vm, 430);
                Assert.Equal(vm.Fraction < AttentionCopy.UpsellThreshold, vm.ShowUpsell);
            }
        });
    }

    [Fact]
    public void EnumEqualsConverter_DrivesTheProviderSegmentBothWays()
    {
        var conv = new CompanionEnumEqualsConverter();

        Assert.Equal(true, conv.Convert(CompanionProviderMode.LocalOllama, typeof(bool),
            "LocalOllama", CultureInfo.InvariantCulture));
        Assert.Equal(CompanionProviderMode.Custom, conv.ConvertBack(true, typeof(CompanionProviderMode),
            "Custom", CultureInfo.InvariantCulture));
        // unchecking the outgoing radio must never write through, or the segment flickers
        Assert.Same(Binding.DoNothing,
            conv.ConvertBack(false, typeof(CompanionProviderMode), "Custom", CultureInfo.InvariantCulture));
    }
}
