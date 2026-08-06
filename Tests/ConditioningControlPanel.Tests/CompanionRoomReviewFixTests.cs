using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Regression cover for the review findings on "Her Room" — the ones whose fix is a decision
/// rather than a brush. Each block below is a bug that shipped in the first cut of the page and
/// would come back the moment someone "simplified" the code that now prevents it.
///
/// <para>No WPF element is constructed here: every fix was deliberately pushed down into a pure
/// helper or a viewmodel precisely so it could be pinned without a window.</para>
/// </summary>
public class CompanionRoomReviewFixTests
{
    // =====================================================================================
    //  the page must stay scrollable over its three bounded lists
    // =====================================================================================

    /// <summary>
    /// A ScrollViewer with nothing to scroll still marks the wheel handled, so an inner list that
    /// cannot move must hand the notch back or the page dies under the cursor.
    /// </summary>
    [Fact]
    public void WheelRelay_ForwardsWhenTheInnerListCannotScrollAtAll()
    {
        Assert.True(CompanionWheelRelay.ShouldForward(scrollableHeight: 0, verticalOffset: 0, delta: -120));
        Assert.True(CompanionWheelRelay.ShouldForward(scrollableHeight: 0, verticalOffset: 0, delta: 120));
    }

    [Fact]
    public void WheelRelay_ForwardsAtTheEndsAndKeepsTheNotchInBetween()
    {
        // scrolling down: the list is at its bottom → the page takes it
        Assert.True(CompanionWheelRelay.ShouldForward(200, 200, -120));
        // scrolling up: the list is at its top → the page takes it
        Assert.True(CompanionWheelRelay.ShouldForward(200, 0, 120));

        // anywhere in the middle the list keeps its own wheel
        Assert.False(CompanionWheelRelay.ShouldForward(200, 100, -120));
        Assert.False(CompanionWheelRelay.ShouldForward(200, 100, 120));
        // …and at the bottom, scrolling UP is still the list's business
        Assert.False(CompanionWheelRelay.ShouldForward(200, 200, 120));
        Assert.False(CompanionWheelRelay.ShouldForward(200, 0, -120));
    }

    [Fact]
    public void WheelRelay_IgnoresAZeroDelta()
        => Assert.False(CompanionWheelRelay.ShouldForward(0, 0, 0));

    // =====================================================================================
    //  Z6 — "spent" is a state, not a magic number
    // =====================================================================================

    /// <summary>
    /// The bug: the empty-bar styling keyed off <c>BarFraction == 0.04</c>, and
    /// <c>BarFractionFor(0.04)</c> returns 0.04 unchanged. A user with 4% of the day left got the
    /// desaturated "she'll be all yours again tomorrow" bar while the copy said otherwise.
    /// </summary>
    [Fact]
    public void Attention_FourPercentIsNotSpent_EvenThoughItDrawsTheSameBar()
    {
        Assert.Equal(AttentionCopy.BarFractionFor(0.0), AttentionCopy.BarFractionFor(0.04), 10);

        Assert.True(AttentionCopy.IsSpent(0.0));
        Assert.False(AttentionCopy.IsSpent(0.04));
        Assert.False(AttentionCopy.IsSpent(0.01));

        Assert.True(new MockAttentionGaugeVm(0.0).IsSpent);
        Assert.False(new MockAttentionGaugeVm(0.04).IsSpent);
    }

    [Fact]
    public void Attention_SpentSliverIsTheOneDrawingMinimum()
    {
        Assert.Equal(AttentionCopy.SpentBarFraction, AttentionCopy.BarFractionFor(0.0), 10);
        Assert.Equal(AttentionCopy.SpentBarFraction, AttentionCopy.BarFractionFor(-1.0), 10);
    }

    /// <summary>
    /// The barks-only floor promise is not detail-on-demand: doc 01 §5.4 requires the card to say
    /// out loud that her voice keeps working, and it used to be folded into the hover-gated
    /// numeric line — so the drained card read as a pure ration.
    /// </summary>
    [Fact]
    public void Attention_FloorNoteIsShownWhereverTheRationReadingCouldStart()
    {
        Assert.True(AttentionCopy.ShowFloorNote(0.0));   // spent
        Assert.True(AttentionCopy.ShowFloorNote(0.08));  // whispering
        Assert.False(AttentionCopy.ShowFloorNote(0.30)); // saving — nobody is worried yet
        Assert.False(AttentionCopy.ShowFloorNote(0.90)); // plenty
    }

    [Fact]
    public void Attention_TheFloorNoteSaysHerVoiceSurvives_AndTheNumbersDoNotCarryIt()
    {
        var drained = MockAttentionGaugeVm.Drained();

        Assert.True(drained.ShowFloorNote);
        Assert.Equal(CompanionLocStaging.English["companion_attention_floor_note"], drained.FloorNote);
        // The promise must NOT be hiding in the hover-only line any more.
        Assert.DoesNotContain("never runs out", drained.DetailLine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mute", drained.DetailLine, StringComparison.OrdinalIgnoreCase);
        // …and it never says the forbidden word.
        Assert.DoesNotContain("token", drained.DetailLine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", drained.FloorNote, StringComparison.OrdinalIgnoreCase);
    }

    // =====================================================================================
    //  single-select chips: clicking the active one is a no-op, never "nothing selected"
    // =====================================================================================

    /// <summary>
    /// The bug: a ToggleButton unchecks on a second click, which cleared the chip while
    /// SelectedFilterKey — and therefore the wall — stayed filtered. Facts missing, nothing lit,
    /// no way to read why.
    /// </summary>
    [Fact]
    public void MemoryFilters_UncheckingTheActiveChipCannotEmptyTheGroup()
    {
        var vm = MockMemoryDiaryVm.Populated();
        var boundaries = vm.Filters.Single(f => f.Key == "boundary");

        boundaries.IsSelected = true;
        Assert.Equal("boundary", vm.SelectedFilterKey);

        boundaries.IsSelected = false;   // the second click on the active chip

        Assert.True(boundaries.IsSelected);
        Assert.Equal("boundary", vm.SelectedFilterKey);
        Assert.Single(vm.Filters.Where(f => f.IsSelected));
    }

    [Fact]
    public void MemoryFilters_SelectingAnotherChipStillMovesTheGroup()
    {
        var vm = MockMemoryDiaryVm.Populated();
        vm.Filters.Single(f => f.Key == "joke").IsSelected = true;

        Assert.Equal("joke", vm.SelectedFilterKey);
        Assert.Single(vm.Filters.Where(f => f.IsSelected));
        Assert.All(vm.Facts, f => Assert.True(f.IsDormant || f.KindKey == "joke"));
    }

    [Fact]
    public void PersonalityPresets_UncheckingTheActiveChipCannotEmptyTheGroup()
    {
        var vm = MockMakeHerYoursVm.Live();
        var active = vm.Presets.Single(p => p.IsSelected);

        active.IsSelected = false;

        Assert.True(active.IsSelected);
        Assert.Single(vm.Presets.Where(p => p.IsSelected));
    }

    [Fact]
    public void PersonalityPresets_SelectingAnotherChipClearsTheRest()
    {
        var vm = MockMakeHerYoursVm.Live();
        var target = vm.Presets.Single(p => p.Id == "strict_domme");

        target.IsSelected = true;

        Assert.Single(vm.Presets.Where(p => p.IsSelected));
        Assert.True(target.IsSelected);
    }

    // =====================================================================================
    //  Z1 — the free tier is not "asleep"
    // =====================================================================================

    /// <summary>
    /// Three different situations rendered the same gray "Off — she's asleep" pill: companion
    /// disabled, provider Off, and no AI entitlement. Only the last is something the user can buy,
    /// and it is the page's flagship state — the pill contradicted the teaser ribbon beside it.
    /// </summary>
    [Fact]
    public void Hero_FreeTierPillReadsAsALock_NotAsAFault()
    {
        var free = MockCompanionHeroCardVm.FreeTier();
        var asleep = MockCompanionHeroCardVm.Asleep();
        var providerOff = MockCompanionHeroCardVm.AiOff();

        Assert.True(free.IsAiLocked);
        Assert.False(asleep.IsAiLocked);
        Assert.False(providerOff.IsAiLocked);

        Assert.NotEqual(asleep.AiPillText, free.AiPillText);
        Assert.NotEqual(providerOff.AiPillText, free.AiPillText);
        Assert.Equal(asleep.AiPillText, providerOff.AiPillText);

        Assert.Equal(CompanionLocStaging.English["companion_hero_pill_ai_locked"], free.AiPillText);
        // and the header it sits in is the one that has something to sell
        Assert.False(free.Header!.HasAiAccess);
    }

    // =====================================================================================
    //  the loc hand-off covers the copy, not just the chrome
    // =====================================================================================

    /// <summary>
    /// The bug: the XAML literals were clean but roughly half the page's copy arrived through
    /// viewmodel string properties with no <c>companion_*</c> key at all — including every
    /// in-voice line, which is exactly the copy the design calls the product. The loc pass would
    /// have shipped the chrome and missed the veil sell line, the dormant promises and the ghost
    /// card.
    ///
    /// <para>This walks the real mocks and asserts each such string is a staged master, so a new
    /// hardcoded line in any zone fails here rather than in a translation round.</para>
    /// </summary>
    [Fact]
    public void EveryZoneMockSuppliesStagedCopy_NotLiterals()
    {
        var staged = new HashSet<string>(CompanionLocStaging.English.Values, StringComparer.Ordinal);

        var chat = MockChatThresholdVm.Locked();
        var memory = MockMemoryDiaryVm.Populated();
        var attention = MockAttentionGaugeVm.Plenty();
        var awareness = MockAwarenessPrivacyVm.Live();
        var personality = MockMakeHerYoursVm.Live();
        var hero = MockCompanionHeroCardVm.Default();
        var constellation = MockRelationshipConstellationVm.Live();
        var engine = MockEngineRoomDrawerVm.Cloud();
        var workshop = MockWorkshopAccordionVm.Collapsed();

        var supplied = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["chat.LockCopy"] = chat.LockCopy,
            ["chat.LockCtaLabel"] = chat.LockCtaLabel,
            ["chat.InputPlaceholder"] = chat.InputPlaceholder,
            ["chat.dormant.StateCopy"] = MockChatThresholdVm.Dormant().StateCopy,
            ["chat.aiOff.StateCopy"] = MockChatThresholdVm.AiOff().StateCopy,
            ["chat.FooterCopy"] = MockChatThresholdVm.Live().FooterCopy,

            ["memory.ProfileStripLabel"] = memory.ProfileStripLabel,
            ["memory.EmptyCopy"] = memory.EmptyCopy,
            ["memory.StorageNote"] = memory.StorageNote,
            ["memory.StorageLinkLabel"] = memory.StorageLinkLabel,
            ["memory.ForgetEverythingLabel"] = memory.ForgetEverythingLabel,

            ["attention.DetailLine"] = attention.DetailLine,
            ["attention.FloorNote"] = attention.FloorNote,
            ["attention.UpsellCopy"] = attention.UpsellCopy,
            ["attention.StateCopy"] = attention.StateCopy,

            ["awareness.WireCaption"] = awareness.WireCaption,
            ["awareness.DormantCopy"] = awareness.DormantCopy,
            ["awareness.AddDenyLabel"] = awareness.AddDenyLabel,
            ["awareness.PageTitlesLabel"] = awareness.PageTitlesLabel,

            ["personality.InterviewTitle"] = personality.InterviewTitle,
            ["personality.InterviewCtaLabel"] = personality.InterviewCtaLabel,
            ["personality.InterviewDormantCopy"] = personality.InterviewDormantCopy,
            ["personality.SpiceTitle"] = personality.SpiceTitle,
            ["personality.SpiceSubtitle"] = personality.SpiceSubtitle,
            ["personality.ActivePersonalityLine"] = personality.ActivePersonalityLine,
            ["personality.ResetLabel"] = personality.ResetLabel,

            ["hero.AiPillText"] = hero.AiPillText,
            ["hero.AwarenessPillText"] = hero.AwarenessPillText,
            ["hero.AsleepCopy"] = hero.AsleepCopy,
            ["hero.MoodWord"] = hero.MoodWord,
            ["hero.MoodCaption"] = hero.MoodCaption,

            ["constellation.FlavorLine"] = constellation.FlavorLine,
            ["constellation.FlavorAccent"] = constellation.FlavorAccent,
            ["constellation.DormantCopy"] = constellation.DormantCopy,

            ["engine.DrawerNote"] = engine.DrawerNote,
            ["engine.LoginPrompt"] = engine.LoginPrompt,
            ["engine.LoginButtonLabel"] = engine.LoginButtonLabel,
            ["engine.LiveActionsPlaceholder"] = engine.LiveActionsPlaceholder,
            ["engine.loggedOut.StatusLine"] = MockEngineRoomDrawerVm.LoggedOut().StatusLine,
            ["engine.off.StatusLine"] = MockEngineRoomDrawerVm.Off().StatusLine,

            ["workshop.DrawerNote"] = workshop.DrawerNote
        };

        var literals = supplied.Where(kv => !staged.Contains(kv.Value))
                               .Select(kv => $"{kv.Key} = \"{kv.Value}\"")
                               .ToArray();

        Assert.True(literals.Length == 0,
            "these viewmodel strings are not staged companion_* masters, so the loc pass would " +
            "miss them:\n  " + string.Join("\n  ", literals));
    }

    /// <summary>
    /// Per-item copy has to come through the staging table too — the filter chips, the fact kind
    /// captions and the constellation node blurbs are all user-visible product copy.
    /// </summary>
    [Fact]
    public void PerItemCopyIsStagedToo()
    {
        var staged = new HashSet<string>(CompanionLocStaging.English.Values, StringComparer.Ordinal);

        foreach (var chip in MockMemoryDiaryVm.Populated().Filters)
            Assert.True(staged.Contains(chip.Label), $"filter chip '{chip.Key}' has an unstaged label");

        foreach (var fact in MockMemoryDiaryVm.Populated().Facts)
            Assert.True(staged.Contains(fact.KindLabel), $"fact kind '{fact.KindKey}' has an unstaged caption");

        foreach (var node in MockRelationshipConstellationVm.Live().Nodes)
        {
            Assert.True(staged.Contains(node.Name), $"stage {node.Index} has an unstaged name");
            Assert.True(staged.Contains(node.Description), $"stage {node.Index} has an unstaged blurb");
        }

        foreach (var preset in MockMakeHerYoursVm.Live().Presets)
            Assert.True(staged.Contains(preset.Label), $"preset '{preset.Id}' has an unstaged label");

        foreach (var trait in MockMakeHerYoursVm.Live().Traits)
            Assert.True(staged.Contains(trait.Label), $"trait '{trait.Label}' is unstaged");

        foreach (var deny in MockAwarenessPrivacyVm.Live().DenyList)
            Assert.True(staged.Contains(deny.Label), $"deny chip '{deny.Label}' is unstaged");
    }

    /// <summary>
    /// The filter chips are built from <see cref="FactOrdering.FilterKeys"/>, so display order and
    /// filter keys cannot drift apart, and every key has a staged label.
    /// </summary>
    [Fact]
    public void FilterChipsFollowTheCanonicalKeyOrder()
    {
        var chips = MockMemoryDiaryVm.Populated().Filters;

        Assert.Equal(FactOrdering.FilterKeys, chips.Select(c => c.Key).ToArray());
        foreach (var key in FactOrdering.FilterKeys)
            Assert.True(CompanionLocStaging.English.ContainsKey($"companion_memory_filter_{key}"),
                $"no staged label for filter chip '{key}'");
    }
}
