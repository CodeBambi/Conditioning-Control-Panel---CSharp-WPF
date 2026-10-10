// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.UiUpdates.cs (2904 lines).
// Sorted member by member against the fifteen Core seams. One member is real; the blanket claim
// was wrong for it and is right for most of the rest, with three exceptions worth naming.
//
// WHAT IS REAL: BtnAccountChip_Click. WPF's body is ShowTab("appsettings") +
// AppSettingsTab.FocusSection("account"), and BOTH halves already ship here as
// OpenAppSettingsSection (MainShellWindow.Settings.cs:65) - "account" is one of the nine keys in
// AppSettingsTabView.SectionKeys. It routes through that helper rather than repeating the pair,
// which also means it gets the helper's Named<T> lookup: the generated AppSettingsTab field on
// this window is permanently null (AvaloniaXamlLoader.Load), so AppSettingsTab?.FocusSection(...)
// transcribed from WPF would have compiled, rendered, reviewed clean and done nothing.
//
// NOT BLOCKED, MOVED - the settings toggles. SliderMaster_Changed, SliderVideoVolume_Changed,
// SliderDuck_Changed, ChkAudioDuck_Changed, ChkExcludeBambiCloudDucking_Changed,
// CmbAudioOutputDevice_SelectionChanged, BtnAudioOutputRefresh_Click, BtnTestAudio_Click,
// PopulateAudioOutputDevices, ChkPerformanceMode_Changed, ChkAutoPerformance_Changed,
// CmbMotionLevel_SelectionChanged, ChkVideoHwDecode_Changed, ChkUnifiedOverlay_Changed,
// ChkPanicOverridesAll_Changed, ChkNoPanic_Changed, ChkWinStart_Click, ChkStartHidden_Click,
// BtnPauseKey_Click, BtnPanicKey_Click, BtnSelectStartupVideo_Click, BtnClearStartupVideo_Click and
// ChkOfflineMode_Changed were all "MainWindow owns the handler, the section relays to it" on WPF.
// On this head each section owns its own handler and its own _isLoading guard:
// Views/Controls/AppSettings/{Audio,Performance,General,Devices,Data}SettingsSection.axaml.cs.
// Adding any of them back here would be a SECOND writer for one setting, not a restoration.
//
// PURE AND PORTABLE, LEFT OUT ANYWAY because they are orphans on this head:
//   ModAwareLabel(english, locKey) - CoreMods.MakeModAware answers it, so it compiles today. Its
//     consumer, StudioTabView, already carries its own inlined twin
//     (Views/Tabs/StudioTabView.axaml.cs:1105-1131) precisely because a UserControl cannot reach a
//     private on the shell. A second public copy with no caller is duplication, not progress.
//   StripLeadingGlyph - same story, same file, already inlined there.
//   FormatFileSize - its only callers are the assets-folder rows, which are not ported.
//
// STILL OUT, by blocker:
//   (UpdateLevelDisplay's header half is live in MainShellWindow.HeroFx.cs.)
//   App.Progression / the XP bar - UpdateXPBarLoginState, UpdateStatPills,
//     RefreshXPBarBonuses, GetBonusChipTooltip, StartStatPillUpdateTimer, XPBarTrack_ToolTipOpening,
//     RefreshAccountChip and the three AccountChip* brushes. CoreProgression is a seam, but the
//     chip and the pills read App.Account / App.Patreon tier state on top of it, and a chip that
//     paints the logged-out tier unconditionally is the same failure recorded for
//     UpdateSubscribeStarUI in MainShellWindow.SubscribeStar.cs.
//   The conditioning-time tracker - StartConditioningTimeTracker, StopConditioningTimeTracker,
//     SyncConditioningTimeToServerAsync. A server round trip with no seam.
//   UpdateUI / QueueModAwareSurfaceSweep / RefreshModAwareSurfaces / SweepStep /
//     EnsureModSweepWatchers / ApplyModFeatureNames / ApplyBimboJournalModVisibility - the sweep
//     walks named controls across every tab, most of which are unported; it is a pass over the
//     finished UI, so it lands last, not first.
//   UpdateUnlockablesVisibility / SetFeatureImageBlur - App.Unlockables plus a WPF BlurEffect.
//   The Intake Pass tile is live in MainShellWindow.IntakePassTile.cs; only its CTA breath
//     (StartIntakePassCtaPulse / StopIntakePassCtaPulse) is out.
//   ImgLogo_MouseLeftButtonDown / ShowEasterEgg / TriggerStartupVideo - a media window each.
//   BtnManageAttention_Click, BtnAttentionStyle_Click, BtnSubliminalSettings_Click,
//     BtnManageMessages_Click, BtnViewLog_Click, BtnPrevImage_Click, BtnNextImage_Click,
//     BtnRefreshAssets_Click - each opens an unported window or drives the unported asset tree.
//   SetOfflineDisabled / UpdateOfflineModeUI / DisconnectNetworkServices - the toggle itself moved
//     (DataSettingsSection.axaml.cs:67, which persists OfflineMode and asks for the offline name),
//     but the TEARDOWN did not: App.Account, App.RemoteControl and the update checker are the
//     things a disconnect disconnects, and none of them exists here to disconnect. The greying
//     pass, SetOfflineDisabled, walks named controls on unported tabs.
//   BtnPickAssetsFolder_Click / CopyDirectoryRecursive - the picker itself already ships as
//     MainShellWindow.Settings.cs RequestPickAssetsFolder, called from SystemFeatureControl. What
//     is missing here is only the WPF handler shell around it and the copy-the-old-library
//     migration, which needs the assets tree to say what it copied.

using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// The header's account chip. Opens the Settings door and scrolls it to Account - no popup
        /// and no reparenting, same as WPF (MainWindow.UiUpdates.cs). The scroll deliberately
        /// happens after the tab is shown, because a section can only be measured once visible;
        /// OpenAppSettingsSection keeps that order and swallows a failed scroll.
        /// </summary>
        private void BtnAccountChip_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => OpenAppSettingsSection("account");

        // ---- header streak pill + XP bar stat pills (WPF MainWindow.UiUpdates.cs:542 UpdateStatPills) ----
        private DispatcherTimer? _statPillTimer;

        /// <summary>The last leaderboard page's figures the pills read (WPF App.Leaderboard).
        /// SEAM(leaderboard): LeaderboardTabView should call this when a page lands; until it does the
        /// online pill reads 0 and the percentile reads Loading, as WPF does before its first fetch.</summary>
        internal static void NoteLeaderboardPage(int onlineUsers, int? yourRank, int? total)
        {
            _lbOnline = onlineUsers; _lbRank = yourRank ?? 0; _lbTotal = total ?? 0; _lbLoaded = true;
        }
        private static int _lbOnline, _lbRank, _lbTotal;
        private static bool _lbLoaded;

        /// <summary>WPF StartStatPillUpdateTimer: paint now, then keep the figures fresh while shown.</summary>
        internal void StartStatPillUpdateTimer()
        {
            UpdateStatPills();
            if (_statPillTimer != null) return;
            _statPillTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => UpdateStatPills());
            _statPillTimer.Start();
            Closed += (_, _) => { _statPillTimer?.Stop(); _statPillTimer = null; };
        }

        /// <summary>Each pill shows only with the skill that buys it. ponytail: the conditioning figure
        /// is the stored total (no live session seconds: the tracker is not on this head).</summary>
        internal void UpdateStatPills()
        {
            try
            {
                var s = CoreSettings.Current;
                bool Has(string skill) => ConditioningControlPanel.Models.SkillTreeRules.HasSkill(s, skill);

                if (Named<Border>("PillConditioningTime") is { } time)
                {
                    time.IsVisible = Has("pink_hours");
                    if (time.IsVisible && Named<TextBlock>("TxtPillConditioningTime") is { } t)
                    {
                        double secs = s.TotalConditioningMinutes * 60;
                        t.Text = $"{(int)(secs / 3600)}h {(int)(secs % 3600 / 60)}m {(int)(secs % 60)}s";
                    }
                }
                if (Named<Border>("PillOnlineUsers") is { } online)
                {
                    online.IsVisible = Has("hive_mind");
                    if (online.IsVisible && Named<TextBlock>("TxtPillOnlineUsers") is { } t)
                        t.Text = _lbOnline.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                if (Named<Border>("PillRankPercentile") is { } rank)
                {
                    rank.IsVisible = Has("popular_girl");
                    if (rank.IsVisible && Named<TextBlock>("TxtPillRankPercentile") is { } t)
                    {
                        int pct = _lbRank > 0 && _lbTotal > 0 ? Math.Clamp((int)Math.Ceiling(_lbRank * 100.0 / _lbTotal), 1, 100) : 0;
                        t.Text = pct > 0 ? $"Top {pct}%" : Loc.Get(_lbLoaded ? "label_unranked" : "label_loading_2");
                    }
                }
                if (Named<Border>("StreakFirePill") is { } fire)
                {
                    fire.IsVisible = Has("good_girl_streak");
                    if (fire.IsVisible)
                    {
                        int streak = 0;
                        try { streak = global::ConditioningControlPanel.Avalonia.App.Achievements?.Progress?.ConsecutiveDays ?? 0; } catch { }
                        if (Named<TextBlock>("TxtStreakFireCount") is { } count) count.Text = streak.ToString();
                        if (Named<Control>("TxtStreakShieldIcon") is { } shield) shield.IsVisible = s.StreakShieldsRemaining > 0;
                    }
                }
                RefreshXPBarBonuses();   // WPF UpdateStatPills' tail (MainShellWindow.XpBar.cs)
            }
            catch (Exception ex) { Log.Debug("UpdateStatPills: {E}", ex.Message); }
        }
    }
}
