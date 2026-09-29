using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Views.Tabs;
using Xunit;
using static ConditioningControlPanel.Services.Chaster.NatashasFavourite;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Natasha's red flash as a setting (2026-09-29). Off, the default: no flash is ever red. On: a red
/// flash shows a 4 s ring, a click, stare or fling in time books nothing, the ring running out
/// books +5:00, and nobody away from the keyboard is ever dealt one.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class NatashaDodgeTests
{
    [Fact]
    public void The_setting_is_off_by_default()
    {
        Assert.False(new AppSettings().ChasterFlashDodge);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(59)]
    [InlineData(3600)]
    public void Off_means_no_red_flash_is_ever_rolled(int idleSeconds)
    {
        Assert.False(FlashMayRoll(dodgeOn: false, idleSeconds));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(59, true)]
    [InlineData(60, false)]
    [InlineData(600, false)]
    [InlineData(-1, false)]
    public void On_it_only_rolls_for_someone_at_the_keyboard(int idleSeconds, bool mayRoll)
    {
        Assert.Equal(mayRoll, FlashMayRoll(dodgeOn: true, idleSeconds));
        Assert.Equal(60, DodgeIdleSec);
    }

    [Fact]
    public void The_ring_runs_out_and_books_five_minutes()
    {
        Assert.True(DodgeBooks(stillUp: true, dodged: false));
        Assert.Equal(300, TabPrices.Find(EventId)!.Seconds);
    }

    [Fact]
    public void Clicked_stared_flung_or_cleared_in_time_books_nothing()
    {
        // A click or a stare takes it off the screen: not up when the ring ends.
        Assert.False(DodgeBooks(stillUp: false, dodged: false));
        // A fling throws it: still fading off the screen, but dodged.
        Assert.False(DodgeBooks(stillUp: true, dodged: true));
        Assert.False(DodgeBooks(stillUp: false, dodged: true));
    }

    [Fact]
    public void The_ring_drains_over_four_seconds_and_the_flash_outlives_it()
    {
        Assert.Equal(4000, DodgeMs);
        Assert.Equal(1, DodgeLeft(0));
        Assert.Equal(0.5, DodgeLeft(2000), 6);
        Assert.Equal(0, DodgeLeft(DodgeMs));
        Assert.Equal(0, DodgeLeft(DodgeMs * 2));
        Assert.Equal(1, DodgeLeft(-100));
        Assert.Equal(1, DodgeLeft(double.NaN));
        Assert.True(DodgeMinLifetimeMs > DodgeMs, "a short flash would fade out from under its ring");
    }

    /// <summary>The flash side is WPF and a timer, so hold the source to the two rules that
    /// matter: the roll asks the setting and the idle guard, and the only booking is at the
    /// ring's end, behind DodgeBooks.</summary>
    [Fact]
    public void A_flash_books_only_when_its_ring_runs_out()
    {
        var flash = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Flash", "FlashService.cs"));
        Assert.Contains("NatashasFavourite.FlashMayRoll(settings.ChasterFlashDodge, ActivityTracker.GetIdleSeconds())", flash);
        Assert.Equal(1, Regex.Matches(flash, Regex.Escape("NoteAt(\"natasha\"")).Count);
        var ring = flash.IndexOf("private void StartNatashaDodge(", StringComparison.Ordinal);
        var guard = flash.IndexOf("NatashasFavourite.DodgeBooks(up, window.NatashaDodged)", StringComparison.Ordinal);
        var book = flash.IndexOf("NoteAt(\"natasha\"", StringComparison.Ordinal);
        Assert.True(ring > 0 && guard > ring && book > guard, "the flash booking must sit behind the ring's end");
    }

    [Fact]
    public void The_switch_sits_on_the_costs_board_and_is_off_with_no_settings()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.ApplyPriceToggles();
            Assert.False(tab.ChkFlashDodge.IsChecked == true);
            var board = (StackPanel)tab.CostRows.Parent;
            Assert.Equal(board.Children.IndexOf(tab.CostRows) + 1, board.Children.IndexOf(tab.FlashDodgeRow));
        });
    }

    [Fact]
    public void Both_switch_strings_are_in_every_language()
    {
        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(9, files.Length);
        foreach (var file in files)
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
            foreach (var key in new[] { "chaster_flash_dodge", "chaster_flash_dodge_hint" })
                Assert.False(string.IsNullOrWhiteSpace((string?)json[key]), $"{Path.GetFileName(file)} lacks {key}");
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
