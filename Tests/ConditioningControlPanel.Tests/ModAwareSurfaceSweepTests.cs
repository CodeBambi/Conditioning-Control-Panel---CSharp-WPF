using System;
using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The mod-awareness sweep's WIRING lane: the surfaces that captured a mod colour, name or piece of
/// art when they were built, and went on showing the previous mod after a switch until the app was
/// restarted.
///
/// <para><b>Why this suite is source-level.</b> Every finding here is a wiring fact - "this repaint
/// is reachable from ModChanged" - and the only way to observe it at runtime is to realise
/// MainWindow, switch mods and look at the pixels. That is a play-test, not a unit test. What CAN
/// rot silently is the wiring itself: a repaint call deleted during a refactor, a heavy rebuild
/// losing its visibility gate, or the sweep drifting off Background priority (which would repaint
/// the presets row from the OUTGOING mod's PinkBrush, since RefreshThemeAwareElements is subscribed
/// after ApplyModFeatureNames and would not have run yet). Each assertion below pins one of those.</para>
/// </summary>
public class ModAwareSurfaceSweepTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string AppFile(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine("ConditioningControlPanel", Path.Combine(parts))));

    /// <summary>The text between two anchors, so an assertion cannot pass on a match somewhere else in the file.</summary>
    private static string Body(string source, string startAnchor, string endAnchor)
    {
        var start = source.IndexOf(startAnchor, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{startAnchor}' is gone - re-read the file before fixing this scrape");
        var end = source.IndexOf(endAnchor, start, StringComparison.Ordinal);
        Assert.True(end > start, $"'{endAnchor}' no longer follows '{startAnchor}' - re-read the file, then fix the scrape");
        return source.Substring(start, end - start);
    }

    private static string SweepBody()
        => Body(AppFile("MainWindow", "MainWindow.UiUpdates.cs"),
                "private void RefreshModAwareSurfaces()",
                "private static void SweepStep(");

    // =====================================================================================
    //  the sweep itself
    // =====================================================================================

    [Fact]
    public void ApplyModFeatureNamesQueuesTheSweep()
    {
        // ApplyModFeatureNames is already subscribed to ModChanged (MainWindow.xaml.cs), which is
        // why the sweep hangs off it instead of adding a subscription of its own.
        var body = Body(AppFile("MainWindow", "MainWindow.UiUpdates.cs"),
                        "private void ApplyModFeatureNames()",
                        "mod-switch stale-surface sweep");

        Assert.Contains("QueueModAwareSurfaceSweep();", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSweepRunsBehindTheRestOfTheModChangedChain()
    {
        var uiUpdates = AppFile("MainWindow", "MainWindow.UiUpdates.cs");
        var queue = Body(uiUpdates, "private void QueueModAwareSurfaceSweep()", "private void RefreshModAwareSurfaces()");

        // Background, NOT Normal: off the UI thread every ModChanged handler is its own Invoke, and
        // a Normal post made from the first of them would be queued ahead of the ones still to come
        // - including RefreshThemeAwareElements, the handler that rewrites PinkBrush.
        Assert.Contains("DispatcherPriority.Background", queue, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherPriority.Normal", queue, StringComparison.Ordinal);

        // Coalesced, so a switch that also raises LanguageChanged sweeps once.
        Assert.Contains("if (_modSweepQueued) return;", queue, StringComparison.Ordinal);
        Assert.Contains("HasShutdownStarted", queue, StringComparison.Ordinal);

        // And the palette handler really is subscribed after the text pass, which is the whole
        // reason the priority matters. If this ever flips, the comment above is a lie.
        var main = AppFile("MainWindow", "MainWindow.xaml.cs");
        var textPass = main.IndexOf("ModChanged += (_, _) => Dispatcher.Invoke(ApplyModFeatureNames)", StringComparison.Ordinal);
        var palette = main.IndexOf("ModChanged += (_, _) => Dispatcher.Invoke(RefreshThemeAwareElements)", StringComparison.Ordinal);
        Assert.True(textPass >= 0 && palette > textPass,
            "RefreshThemeAwareElements is no longer subscribed after ApplyModFeatureNames - re-check the sweep's priority");
    }

    [Theory]
    // Cheap, and nothing else ever repaints them: they run on every sweep.
    [InlineData("TintStartCharge")]              // Start button charge gradient (MainWindow.HeroFx.cs)
    [InlineData("UpdateAutonomyButtonState")]    // Takeover start/stop accent (MainWindow.Autonomy.cs)
    [InlineData("UpdateLeaderboardModeButtons")] // board rows + you-bar accent (MainWindow.Leaderboard.cs)
    [InlineData("RefreshShowcasePinArt")]        // pinned achievement art (MainWindow.ProfileCosmetics.cs)
    [InlineData("RefreshPresetsDropdown")]       // header preset combo (MainWindow.Presets.cs)
    // Rebuild a whole tab, so they are gated on that tab being on screen.
    [InlineData("RefreshAllAchievementTiles")]   // achievement grid (MainWindow.AchievementsTab.cs)
    [InlineData("RefreshQuestUI")]               // quests tab (MainWindow.QuestsTab.cs)
    [InlineData("RefreshEnhancementsUI")]        // skill tree + secret rail (MainWindow.Enhancements.cs)
    [InlineData("RefreshPresetsModVisuals")]     // presets card row + detail titles (MainWindow.Presets.cs)
    public void TheSweepRepaintsEveryStuckSurface(string repaint)
    {
        Assert.Contains(repaint, SweepBody(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AchievementsTab")]
    [InlineData("QuestsTab")]
    [InlineData("EnhancementsTab")]
    public void WholeTabRebuildsOnlyRunForATabOnScreen(string tab)
    {
        // DrawSkillTree (via RefreshEnhancementsUI) is the most expensive redraw in the app, and
        // every one of these tabs repopulates itself on show - so rebuilding one nobody is looking
        // at is pure waste, and doing it eagerly would double the work on the very next show.
        Assert.Contains($"if ({tab}?.IsVisible == true)", SweepBody(), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePresetsTabIsTheOneSurfaceCarryingADirtyFlag()
    {
        // Its ShowTab case does NOT rebuild the card row (only the nav button calls
        // RefreshPresetsList), so unlike the other tabs it cannot self-heal on show.
        var uiUpdates = AppFile("MainWindow", "MainWindow.UiUpdates.cs");
        var sweep = SweepBody();

        Assert.Contains("_presetsModDirty = true;", sweep, StringComparison.Ordinal);
        Assert.Contains("_presetsModDirty = false;", sweep, StringComparison.Ordinal);

        // ...and the flag is actually drained, by the tab's own visibility change.
        var watchers = Body(uiUpdates, "private void EnsureModSweepWatchers()", "#endregion");
        Assert.Contains("PresetsTab.IsVisibleChanged", watchers, StringComparison.Ordinal);
        Assert.Contains("_presetsModDirty", watchers, StringComparison.Ordinal);
        Assert.Contains("_modSweepWatchersHooked", watchers, StringComparison.Ordinal);
    }

    // =====================================================================================
    //  the repaints the sweep leans on
    // =====================================================================================

    [Fact]
    public void RefreshingAnAchievementTileReResolvesItsArt()
    {
        // The grid is built ONCE at startup and its show case only calls RefreshAllAchievementTiles,
        // so this is the single place a badge can pick up the new mod's art. Both shipped .ccpmod
        // archives override all 58 PNGs.
        var body = Body(AppFile("MainWindow", "MainWindow.AchievementsTab.cs"),
                        "private void RefreshAchievementTile(string achievementId)",
                        "private void RefreshAllAchievementTiles()");

        Assert.Contains("LoadAchievementImage(parts.Achievement.ImageName)", body, StringComparison.Ordinal);
        // Missing art must leave the badge alone rather than blanking it.
        Assert.Contains("if (art != null) parts.Badge.Source = art;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowcasePinsRepaintFromTheTilesOnScreenNotFromYourSettings()
    {
        // The card on screen is not always yours - a searched profile renders someone else's pins,
        // and repainting those from settings would quietly show them your loadout.
        var body = Body(AppFile("MainWindow", "MainWindow.ProfileCosmetics.cs"),
                        "internal void RefreshShowcasePinArt()",
                        "// ============================== customize dialog");

        Assert.Contains("ProfilePinnedShowcase?.ItemsSource", body, StringComparison.Ordinal);
        Assert.Contains("ProfileAchievementTile", body, StringComparison.Ordinal);
        Assert.Contains("ApplyProfilePins(ids)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("App.Settings", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePresetsRepaintNeverStealsTheDetailPaneFromASession()
    {
        // The detail pane is shared with sessions: calling SelectPreset here would yank a user who
        // had a session open back onto a preset.
        var body = Body(AppFile("MainWindow", "MainWindow.Presets.cs"),
                        "private void RefreshPresetsModVisuals()",
                        "private Border CreatePresetCard(");

        Assert.Contains("RefreshPresetsList();", body, StringComparison.Ordinal);
        Assert.Contains("PresetDetailScroller?.Visibility != Visibility.Visible", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectPreset(", body, StringComparison.Ordinal);
    }

    // =====================================================================================
    //  the two surfaces with their own ModChanged hooks
    // =====================================================================================

    [Fact]
    public void TheCompanionPickerRidesTheTabsExistingModChangedHook()
    {
        // The picker is only otherwise written by SyncCompanionTabUI (tab show), so a switch made
        // while sitting on the tab left stale names, a stale accent border and possibly a card for
        // an avatar set the new mod does not support.
        var body = Body(AppFile("MainWindow", "MainWindow.CompanionFx.cs"),
                        "private void OnCompanionFxModChanged(",
                        "catch (Exception ex) { App.Logger?.Debug(\"OnCompanionFxModChanged");

        Assert.Contains("UpdateCompanionCardsUI();", body, StringComparison.Ordinal);
        Assert.Contains("UpdateCompanionPromptLabels();", body, StringComparison.Ordinal);
        // ...still marshalled, because ModChanged may be raised off the UI thread.
        Assert.Contains("Dispatcher.CheckAccess()", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTubesQuickMenuRepaintsOnModChanged()
    {
        // ApplyActiveModChange calls UpdateQuickMenuState too, but only on the top-bar path -
        // uninstalling the mod you are wearing activates the fallback inside ModService and never
        // reaches it, so the menu has to hang off the authoritative signal.
        var body = Body(AppFile("AvatarTube", "AvatarTubeWindow.Avatar.cs"),
                        "private void OnModChanged()",
                        "public void RefreshTubeLayout()");

        Assert.Contains("UpdateQuickMenuState();", body, StringComparison.Ordinal);
    }

    // =====================================================================================
    //  the Studio rack
    // =====================================================================================

    [Fact]
    public void TheStudioRackRepaintsFromTheSweepAndNotFromItsOwnModChangedHook()
    {
        // ccp-bugs#1100. RepaintModAwareChrome calls RefreshDots, which reads PinkBrush through
        // TryFindResource. Its own ModChanged subscription sat AHEAD of the palette handler in
        // MainWindow.xaml.cs, so the rack's lit state dots (and their glow) were painted from the
        // OUTGOING mod's accent and stayed there until an unrelated settings write repainted them.
        // The sweep's Background priority is by construction after every ModChanged handler.
        Assert.Contains("SweepStep(() => StudioTab?.RepaintModAwareChrome(), \"studio rack\");",
                        SweepBody(), StringComparison.Ordinal);

        Assert.DoesNotContain("StudioTab?.RepaintModAwareChrome())",
                              AppFile("MainWindow", "MainWindow.xaml.cs"), StringComparison.Ordinal);
    }

    // =====================================================================================
    //  the faucet handle
    // =====================================================================================

    [Fact]
    public void TheProfileFaucetHandleFollowsTheModAccent()
    {
        var xaml = AppFile("Views", "Tabs", "DiscordTabView.xaml");

        Assert.Contains("Margin=\"8.5,1,0,0\" Fill=\"{DynamicResource PinkBrush}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"8.5,1,0,0\" Fill=\"#FF69B4\"", xaml, StringComparison.Ordinal);

        // The DynamicResource is only worth anything because a mod switch rewrites that key.
        Assert.Contains("res[\"PinkBrush\"] = new SolidColorBrush(accent);",
                        AppFile("MainWindow", "MainWindow.xaml.cs"), StringComparison.Ordinal);
    }
}
