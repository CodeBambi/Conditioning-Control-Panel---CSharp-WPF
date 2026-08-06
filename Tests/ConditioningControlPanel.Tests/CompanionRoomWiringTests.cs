using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Views.Controls.Companion;
using ConditioningControlPanel.Views.Controls.Companion.Runtime;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The wiring pass's own decisions — the pure ones, which is deliberately most of them.
///
/// <para>The zone viewmodels themselves need <c>App.Settings</c>, <c>App.Brain</c> and a live
/// dispatcher, so they are not constructible here. Everything that could be a decision rather than
/// a lookup was therefore written as a static function on the viewmodel it belongs to, and this
/// suite is what those functions exist for: the chat state ladder, the AI-badge mapping, the
/// provider round-trip, the attention arithmetic, the wire-view format, and the anchor/heading
/// split that keeps every deep link on the page working in every language.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class CompanionRoomWiringTests
{
    // =====================================================================================
    //  Z2 — the state ladder
    // =====================================================================================

    [Fact]
    public void ChatState_AiOff_IsDisabled_WhateverElseIsTrue()
    {
        Assert.Equal(CompanionZoneState.Disabled, ChatThresholdRuntimeVm.ResolveState(
            brainRouting: true, aiEnabled: false, cloudProvider: true, entitled: true));
        Assert.Equal(CompanionZoneState.Disabled, ChatThresholdRuntimeVm.ResolveState(
            brainRouting: false, aiEnabled: false, cloudProvider: false, entitled: false));
    }

    [Fact]
    public void ChatState_NoEntitlementOnCloud_IsTheTeaser_NotTheDormantPromise()
    {
        // The free-tier veil is the page's conversion surface. Telling a paying-curious user
        // "that's about to change" instead of showing them what they're missing buries the sell.
        Assert.Equal(CompanionZoneState.Locked, ChatThresholdRuntimeVm.ResolveState(
            brainRouting: false, aiEnabled: true, cloudProvider: true, entitled: false));
    }

    [Fact]
    public void ChatState_NoEntitlementButLocalProvider_IsNotLocked()
    {
        // A model running on the user's own machine is not something we can sell them.
        Assert.NotEqual(CompanionZoneState.Locked, ChatThresholdRuntimeVm.ResolveState(
            brainRouting: true, aiEnabled: true, cloudProvider: false, entitled: false));
    }

    [Fact]
    public void ChatState_KillSwitchOff_IsDormant_NotBroken()
    {
        // UseCompanionBrain=false is the legacy stateless path, which genuinely does forget every
        // conversation — exactly what the dormant copy says.
        Assert.Equal(CompanionZoneState.Dormant, ChatThresholdRuntimeVm.ResolveState(
            brainRouting: false, aiEnabled: true, cloudProvider: true, entitled: true));
    }

    [Fact]
    public void ChatState_EverythingOn_IsLive()
    {
        Assert.Equal(CompanionZoneState.Live, ChatThresholdRuntimeVm.ResolveState(
            brainRouting: true, aiEnabled: true, cloudProvider: true, entitled: true));
    }

    [Fact]
    public void TheInputRowStandsDown_InBothLockedAndDormant()
    {
        // The view hides it with ConverterParameter="Locked|Dormant"; this is that decision.
        Assert.True(CompanionEnumToVisibilityConverter.Matches("Locked", "Locked|Dormant"));
        Assert.True(CompanionEnumToVisibilityConverter.Matches("Dormant", "Locked|Dormant"));
        Assert.False(CompanionEnumToVisibilityConverter.Matches("Live", "Locked|Dormant"));
        Assert.False(CompanionEnumToVisibilityConverter.Matches("Disabled", "Locked|Dormant"));
    }

    [Fact]
    public void TheEnumConverter_StillBehavesForASingleName()
    {
        // Every pre-existing binding passes one name; the pipe list must not change them.
        Assert.True(CompanionEnumToVisibilityConverter.Matches("Cloud", "Cloud"));
        Assert.False(CompanionEnumToVisibilityConverter.Matches("Cloud", "Off"));
        Assert.False(CompanionEnumToVisibilityConverter.Matches(null, "Off"));
        Assert.False(CompanionEnumToVisibilityConverter.Matches("Off", null));
    }

    // =====================================================================================
    //  Z2 — the thread projection, which is where the AI badge lives
    // =====================================================================================

    private static CompanionTurn Turn(TurnKind kind, string text) =>
        CompanionTurn.Create(kind, text);

    [Fact]
    public void OnlyAssistantChatTurnsWearTheAiBadge()
    {
        var bubbles = ChatThresholdRuntimeVm.ProjectThread(new[]
        {
            Turn(TurnKind.UserChat, "hi"),
            Turn(TurnKind.AssistantChat, "hi back~"),
            Turn(TurnKind.BarkEcho, "«bambi said aloud: \"good girl~\"»")
        });

        var byKind = bubbles.ToDictionary(b => b.Kind);
        Assert.False(byKind[CompanionBubbleKind.You].IsAiGenerated);
        Assert.True(byKind[CompanionBubbleKind.Her].IsAiGenerated);
        // A bark is a recording, not a completion. This is the invariant the whole badge rests on.
        Assert.False(byKind[CompanionBubbleKind.Echo].IsAiGenerated);
    }

    [Fact]
    public void AmbientTurnsNeverReachTheThread()
    {
        var bubbles = ChatThresholdRuntimeVm.ProjectThread(new[]
        {
            Turn(TurnKind.AmbientEvent, "user is on Chrome (fun) for 22m"),
            Turn(TurnKind.AmbientReply, "still scrolling, hmm?"),
            Turn(TurnKind.SystemNote, "app closed"),
            Turn(TurnKind.UserChat, "hey")
        });

        var only = Assert.Single(bubbles);
        Assert.Equal(CompanionBubbleKind.You, only.Kind);
        Assert.Equal("hey", only.Text);
    }

    [Fact]
    public void TheThresholdShowsTheNewestTurns_OldestFirst()
    {
        var turns = new List<CompanionTurn>();
        for (int i = 1; i <= 8; i++) turns.Add(Turn(TurnKind.UserChat, "line " + i));

        var bubbles = ChatThresholdRuntimeVm.ProjectThread(turns);

        Assert.Equal(ChatThresholdRuntimeVm.VisibleTurnCount, bubbles.Count);
        Assert.Equal("line 6", bubbles[0].Text);
        Assert.Equal("line 8", bubbles[^1].Text);
    }

    [Fact]
    public void ProjectThread_TakesAnEmptyOrNullLogWithoutComplaining()
    {
        Assert.Empty(ChatThresholdRuntimeVm.ProjectThread(null));
        Assert.Empty(ChatThresholdRuntimeVm.ProjectThread(Array.Empty<CompanionTurn>()));
    }

    [Fact]
    public void TheBarkEchoBubbleShowsTheLine_NotTheWireSigil()
    {
        var speaker = CompanionTurn.FormatBarkEcho("Bambi", "the rabbit hole~");
        Assert.Equal("the rabbit hole~", ChatThresholdRuntimeVm.UnwrapEcho(speaker));
    }

    [Fact]
    public void UnwrapEcho_LeavesAnUnexpectedShapeAlone()
    {
        Assert.Equal("plain line", ChatThresholdRuntimeVm.UnwrapEcho("plain line"));
        Assert.Equal(string.Empty, ChatThresholdRuntimeVm.UnwrapEcho(null));
    }

    // =====================================================================================
    //  Z3 — the fact card's kind mapping and provenance line
    // =====================================================================================

    [Theory]
    [InlineData(MemoryFactKind.Boundary, "boundary")]
    [InlineData(MemoryFactKind.Joke, "joke")]
    [InlineData(MemoryFactKind.Preference, "preference")]
    [InlineData(MemoryFactKind.Goal, "goal")]
    [InlineData(MemoryFactKind.Event, "moment")]
    [InlineData(MemoryFactKind.Identity, "identity")]
    public void MemoryKindsMapOntoTheWallsFilterKeys(MemoryFactKind kind, string expected)
        => Assert.Equal(expected, MemoryFactRuntimeVm.KindKeyFor(kind));

    [Fact]
    public void EveryFilterChipHasAKindThatCanReachIt()
    {
        // "all" is the default chip and matches everything; the rest must each be produced by some
        // MemoryFactKind, or the wall would ship a chip that can never show a card.
        var produced = Enum.GetValues<MemoryFactKind>()
            .Select(MemoryFactRuntimeVm.KindKeyFor).ToHashSet(StringComparer.Ordinal);

        foreach (var key in FactOrdering.FilterKeys.Where(k => k != "all"))
            Assert.Contains(key, produced);
    }

    [Fact]
    public void AFreshFactSaysSoRatherThanShowingAnEmptyMetaLine()
    {
        var meta = MemoryFactRuntimeVm.BuildMeta(uses: 0, lastUsed: null, userEdited: false);
        Assert.False(string.IsNullOrWhiteSpace(meta));
    }

    [Fact]
    public void AHandEditedFactAdvertisesItsProvenance()
    {
        var meta = MemoryFactRuntimeVm.BuildMeta(uses: 4, lastUsed: DateTime.UtcNow, userEdited: true);
        Assert.Contains(CompanionLocMasters.Get("companion_memory_meta_edited"), meta, StringComparison.Ordinal);
        Assert.Contains("4", meta, StringComparison.Ordinal);
    }

    // =====================================================================================
    //  Z5 — the wire view is the real frame, not a mock
    // =====================================================================================

    [Fact]
    public void TheWireLineCarriesCategoryAppAndDuration()
    {
        var line = AwarenessPrivacyRuntimeVm.FormatWire("fun", "Chrome", "Chrome", TimeSpan.FromMinutes(22));
        Assert.Equal("[ fun · Chrome · 22m ]", line);
    }

    [Fact]
    public void TheWireLineShowsTheTabTitleOnlyWhenItDiffersFromTheApp()
    {
        var line = AwarenessPrivacyRuntimeVm.FormatWire("fun", "YouTube", "Bambi Bae", TimeSpan.FromMinutes(3));
        Assert.Contains("Bambi Bae", line, StringComparison.Ordinal);

        var same = AwarenessPrivacyRuntimeVm.FormatWire("fun", "Discord", "Discord", TimeSpan.FromSeconds(30));
        // "Discord · Discord · 30s" would be noise, and the promise is that this line is exact.
        Assert.Equal("[ fun · Discord · 30s ]", same);
    }

    [Fact]
    public void TheWireLineSurvivesMissingFields()
    {
        var line = AwarenessPrivacyRuntimeVm.FormatWire(null, null, null, null);
        Assert.Equal("[ 0s ]", line);
    }

    // =====================================================================================
    //  Z6 — the attention gauge's arithmetic
    // =====================================================================================

    [Fact]
    public void AttentionFraction_IsRemainingOverTheCeiling()
    {
        Assert.Equal(1.0, AttentionGaugeRuntimeVm.FractionFor(100, 100));
        Assert.Equal(0.4, AttentionGaugeRuntimeVm.FractionFor(40, 100), 6);
        Assert.Equal(0.0, AttentionGaugeRuntimeVm.FractionFor(0, 100));
    }

    [Fact]
    public void AttentionFraction_TreatsNoCeilingAsFull_AndClamps()
    {
        // Local Ollama and an uncapped BYO endpoint have no meter to draw.
        Assert.Equal(1.0, AttentionGaugeRuntimeVm.FractionFor(0, 0));
        Assert.Equal(1.0, AttentionGaugeRuntimeVm.FractionFor(500, 100));
        Assert.Equal(0.0, AttentionGaugeRuntimeVm.FractionFor(-5, 100));
    }

    [Fact]
    public void TheGaugesCopyLadderStillReadsOffThatFraction()
    {
        // Guards the seam between this pass's arithmetic and the zone's existing copy rules.
        Assert.Equal(AttentionMood.Plenty, AttentionCopy.MoodFor(AttentionGaugeRuntimeVm.FractionFor(80, 100)));
        Assert.Equal(AttentionMood.Saving, AttentionCopy.MoodFor(AttentionGaugeRuntimeVm.FractionFor(30, 100)));
        Assert.Equal(AttentionMood.Whispering, AttentionCopy.MoodFor(AttentionGaugeRuntimeVm.FractionFor(10, 100)));
        Assert.Equal(AttentionMood.Spent, AttentionCopy.MoodFor(AttentionGaugeRuntimeVm.FractionFor(0, 100)));
    }

    // =====================================================================================
    //  Z7 — the provider segment round-trips
    // =====================================================================================

    [Theory]
    [InlineData(CompanionProviderMode.Off)]
    [InlineData(CompanionProviderMode.Cloud)]
    [InlineData(CompanionProviderMode.LocalOllama)]
    [InlineData(CompanionProviderMode.Custom)]
    public void ProviderModeSurvivesTheRoundTripThroughSettings(CompanionProviderMode mode)
    {
        var (enabled, provider) = EngineRoomRuntimeVm.SettingsFor(mode);
        Assert.Equal(mode, EngineRoomRuntimeVm.ModeFor(enabled, provider));
    }

    [Fact]
    public void AiDisabledAlwaysReadsAsTheOffSegment()
    {
        // Whatever provider is remembered, "AI off" is the Off segment — the two cannot drift.
        foreach (var provider in Enum.GetValues<AiProviderType>())
            Assert.Equal(CompanionProviderMode.Off, EngineRoomRuntimeVm.ModeFor(aiEnabled: false, provider));
    }

    [Fact]
    public void ChoosingOffDoesNotForgetWhichProviderWasConfigured()
    {
        // SettingsFor(Off) must not write a provider; the write path only assigns when enabled.
        var (enabled, _) = EngineRoomRuntimeVm.SettingsFor(CompanionProviderMode.Off);
        Assert.False(enabled);
    }

    // =====================================================================================
    //  Z8 — anchor key vs display heading
    // =====================================================================================

    [Fact]
    public void ACellWithNoExplicitKeyIsItsOwnAnchor()
    {
        // The pre-split behaviour, which every design-time cell and the zone tests rely on.
        var cell = new CompanionWorkshopCell("ROSTER");
        Assert.Equal("ROSTER", cell.Key);
    }

    [Fact]
    public void ALocalizedHeadingDoesNotMoveTheAnchor()
    {
        var cell = new CompanionWorkshopCell("Personalliste")
        {
            Key = CompanionRoomAnchors.WorkshopRosterCell
        };

        Assert.Equal(CompanionRoomAnchors.WorkshopRosterCell, cell.Key);
        Assert.Equal("Personalliste", cell.Title);
    }

    [Fact]
    public void EveryWorkshopAnchorHasAStagedHeading()
    {
        // A cell whose heading key is missing would render its raw key as a title.
        var anchors = new[]
        {
            ("companion_workshop_cell_roster", CompanionRoomAnchors.WorkshopRosterCell),
            ("companion_workshop_cell_behavior", CompanionRoomAnchors.WorkshopBehaviorCell),
            ("companion_workshop_cell_triggers", CompanionRoomAnchors.WorkshopTriggersCell),
            ("companion_workshop_cell_library", CompanionRoomAnchors.WorkshopLibraryCell),
            ("companion_workshop_cell_community", CompanionRoomAnchors.WorkshopCommunityCell),
            ("companion_workshop_cell_awareness", CompanionRoomAnchors.WorkshopAwarenessCell)
        };

        foreach (var (key, anchor) in anchors)
        {
            Assert.True(CompanionLocMasters.Companion.ContainsKey(key), "missing staged heading: " + key);
            Assert.False(string.IsNullOrWhiteSpace(anchor));
        }
    }

    [Fact]
    public void TheDesignTimeCellsStillCarryNoContent_SoThePreviewHarnessRendersTheScaffold()
    {
        var mock = MockWorkshopAccordionVm.Collapsed();
        Assert.All(mock.Cells, c => Assert.Null(c.Content));
        Assert.All(mock.Cells, c => Assert.NotEmpty(c.Rows));
    }

    // =====================================================================================
    //  the staged copy the wiring pass added
    // =====================================================================================

    [Theory]
    [InlineData("companion_hero_next_level_fmt")]
    [InlineData("companion_chat_time_minutes")]
    [InlineData("companion_chat_time_hours")]
    [InlineData("companion_chat_time_days")]
    [InlineData("companion_memory_meta_uses")]
    [InlineData("companion_memory_meta_last")]
    [InlineData("companion_attention_detail_fmt")]
    [InlineData("companion_engine_status_ready_fmt")]
    [InlineData("companion_engine_daily_limit_fmt")]
    public void EveryFormattedKeyActuallyCarriesItsPlaceholder(string key)
    {
        // A translator dropping "{0}" turns a live number into silence; Loc.GetF cannot notice.
        Assert.Contains("{0}", CompanionLocMasters.Companion[key], StringComparison.Ordinal);
    }

    [Fact]
    public void LocGetF_SubstitutesTheArgument_NotTheRawKey()
    {
        Assert.Equal("22m ago", Loc.GetF("companion_chat_time_minutes", 22));
    }

    [Fact]
    public void LocGetF_SurvivesAMalformedTemplate()
    {
        // A bad translation is cosmetic; it may never take a card down.
        Assert.Equal("companion_not_a_real_key", Loc.GetF("companion_not_a_real_key", 1, 2, 3));
    }

    [Fact]
    public void ThePerCardCaptionsAreTheirOwnFamily_NotTrain1sGroupHeaders()
    {
        // The failure this catches is nasty and quiet, and it nearly happened once. Train 1 ships
        // companion_memory_kind_* as the memory panel's GROUP HEADERS ("Boundaries she must
        // respect"); this page wanted the same names for its per-card captions ("boundary · always
        // honored"). Merged into one en.json the header would simply have won, in a chip-sized
        // slot, and the caption copy would have been unreachable. The page's family is
        // companion_memory_card_*, and both families have to exist side by side in all nine files.
        string[] kinds = { "identity", "preference", "boundary", "joke", "goal" };

        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var kind in kinds)
            {
                Assert.True(file.ContainsKey($"companion_memory_card_{kind}"),
                    $"{lang}.json has no 'companion_memory_card_{kind}'");
                Assert.True(file.ContainsKey($"companion_memory_kind_{kind}"),
                    $"{lang}.json has no 'companion_memory_kind_{kind}'");
            }
        }

        // The EN masters are what the collision would have destroyed, so they are what gets pinned.
        // Translations may legitimately converge — Japanese, Korean and Chinese have no plural to
        // separate the header "Running jokes" from the caption "running joke".
        var english = CompanionLocMasters.For("en");
        foreach (var kind in kinds)
            Assert.NotEqual(english[$"companion_memory_kind_{kind}"], english[$"companion_memory_card_{kind}"]);
    }

    [Fact]
    public void TheTwoWipesAreDistinguishableInCopy()
    {
        // Doc 01 §2.4: the diary's wipe is THE wipe; the Engine Room's is conversation-only. If the
        // two ever read the same, a user reaching for one gets the other.
        var diary = CompanionLocMasters.Get("companion_memory_forget_everything");
        var engine = CompanionLocMasters.Get("companion_engine_clear_conversation");
        Assert.NotEqual(diary, engine);

        // …and the narrower one says out loud what it does NOT touch.
        var note = CompanionLocMasters.Get("companion_engine_clear_conversation_note");
        Assert.False(string.IsNullOrWhiteSpace(note));
        Assert.NotEqual("companion_engine_clear_conversation_note", note);
    }
}
