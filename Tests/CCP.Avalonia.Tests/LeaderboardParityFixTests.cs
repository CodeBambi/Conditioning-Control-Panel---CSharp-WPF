using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Fix wave 2026-10-09 (S11-S16): the Discord chip opens the profile, the trophy_case skill gates the Streak column,
/// a refresh pushes the profile before it fetches, rank arrows come from the local snapshot, and "Jump to me" says
/// where you stand when your row is not on screen. Same class as the other cloud tests (CoreSettings / CoreAccount).
/// </summary>
public sealed partial class AccountSeedTests
{
    [Fact]
    public async Task LeaderboardTab_Chip_TrophyCase_PushFirst_Deltas_AndJumpToMe()
    {
        var s = CoreSettings.Current;
        var (oldOffline, oldClient, oldId) = (s.OfflineMode, LeaderboardTabView.NewClient, CoreAccount.UnifiedUserId);
        var (oldLink, oldPush, oldClock) = (LeaderboardTabView.OpenLink, LeaderboardTabView.PushBeforeFetch, LeaderboardRankSnapshots.UtcNow);
        var oldSkills = s.UnlockedSkills.ToList();
        var file = Path.Combine(Path.GetTempPath(), "ccp-lb-ranks-" + Guid.NewGuid().ToString("N") + ".json");
        var wire = new BoardWire();
        var opened = new List<string>();
        var seenAtPush = new List<int>();
        Window? host = null;
        try
        {
            // A baseline 13 h old: monthly ranks were u7 #1, u2 #2, u1 #3; u3 was not on the board.
            var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
            File.WriteAllText(file, "{ \"modes\": { \"monthly\": { \"captured_at_utc\": \"2026-10-08T23:00:00Z\", "
                + "\"ranks\": { \"u7\": 1, \"u2\": 2, \"u1\": 3 } } } }");
            LeaderboardRankSnapshots.ResetForTests(file);
            LeaderboardRankSnapshots.UtcNow = () => now;

            LeaderboardTabView.NewClient = () => new LeaderboardClient(wire);
            LeaderboardTabView.OpenLink = (_, url) => { opened.Add(url); return Task.FromResult(true); };
            LeaderboardTabView.PushBeforeFetch = () => { seenAtPush.Add(wire.Seen.Count); return Task.CompletedTask; };
            CoreAccount.UnifiedUserId = "u7";
            s.OfflineMode = false;
            s.UnlockedSkills.Remove("trophy_case");

            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (global::Avalonia.Application.Current is null)
                    global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                LocalizationManager.Instance.SetLanguage("en");

                var tab = new LeaderboardTabView();
                T F<T>(string n) where T : Control => tab.FindControl<T>(n)!;
                tab.SetLeaderboardMode(false);                 // the monthly board (the default is all-time)
                host = new Window { Width = 1400, Height = 900, Content = tab };
                host.Show();
                await tab.RefreshLeaderboardAsync();
                Dispatcher.UIThread.RunJobs();

                // S16: the profile push runs before the board is asked for.
                Assert.NotEmpty(seenAtPush);
                Assert.Equal(0, seenAtPush[0]);

                // S13: no trophy_case, no Streak column; buying it shows the column on the next look.
                Assert.False(tab.ShowTrophyStats);
                s.UnlockedSkills.Add("trophy_case");
                tab.UpdateTrophyCaseColumns();
                Assert.True(tab.ShowTrophyStats);

                // S12: monthly ranks now are u1 #1, u2 #2, u3 #3, u7 #4.
                var rows = tab.RankedPage!.Entries!.ToDictionary(r => r.UnifiedId!);
                Assert.Equal(("up", "▲2"), (rows["u1"].DeltaState, rows["u1"].DeltaText));
                Assert.Equal("same", rows["u2"].DeltaState);
                Assert.Equal("new", rows["u3"].DeltaState);
                Assert.Equal(("down", "▼3"), (rows["u7"].DeltaState, rows["u7"].DeltaText));
                Assert.Equal("▼3", F<TextBlock>("TxtYouDelta").Text);
                Assert.Contains("down", F<TextBlock>("TxtYouDelta").Classes);
                // The 13 h old baseline was due, so this board is the new one: a second look moves nobody.
                Assert.Equal(1, LeaderboardRankSnapshots.GetPreviousRank("monthly", "u1"));

                // S11: the Discord chip (Nyx, on the podium) opens that profile.
                var chip = tab.GetVisualDescendants().OfType<Button>()
                    .First(b => b.Classes.Contains("lbdiscord") && b.Tag as string == "33" && b.IsEffectivelyVisible);
                chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(new[] { "https://discord.com/users/33" }, opened);

                // S14: filtered out of sight, "Jump to me" says where you stand instead of doing nothing.
                F<RadioButton>("ChipFilterOg").IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                F<Button>("BtnJumpToMe").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Loc.GetF("label_your_rank_0_of_1", 4, 1234), F<TextBlock>("TxtLeaderboardStatus").Text);

                // S15: the percentile falls back to your place in the slice when the server sent no rank.
                Assert.Equal(1, tab.GetPlayerPercentile());
                host.Close();
                host = null;
            });
        }
        finally
        {
            if (host != null) await AvaloniaTestDispatcher.RunAsync(() => { host.Close(); return Task.CompletedTask; });
            (s.OfflineMode, LeaderboardTabView.NewClient, CoreAccount.UnifiedUserId) = (oldOffline, oldClient, oldId);
            (LeaderboardTabView.OpenLink, LeaderboardTabView.PushBeforeFetch, LeaderboardRankSnapshots.UtcNow) = (oldLink, oldPush, oldClock);
            s.UnlockedSkills.Clear();
            s.UnlockedSkills.AddRange(oldSkills);
            CoreSettings.SaveImmediate();
            LeaderboardRankSnapshots.ResetForTests(null);
            try { File.Delete(file); } catch { }
        }
    }
}
