using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models.Dashboard;
using ConditioningControlPanel.Services.Dashboard;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The edit-mode rules, away from the window that draws them. Every question the picker asks the
/// user is decided here first, so "does this slot offer Split?" is answerable without a wall, a
/// mouse or a session - and so Phase F's rolodex answers it the same way rather than growing a
/// second opinion behind a bridge.
///
/// <para>One fact does a lot of work in these cases: the shipped wall already holds all eleven FX
/// keys, so <c>focusgaze</c> is the only splittable feature that is NOT on it. That makes it the
/// one key that can test "a new FX landing on an occupied slot" without the move rule catching it
/// first.</para>
/// </summary>
public class DashboardPickerRuleTests
{
    // ── the pencil ───────────────────────────────────────────────

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void The_pencil_is_shown_unless_a_session_is_running(bool sessionLocked, bool shown)
        => Assert.Equal(shown, DashboardPickerRule.ShowPencil(sessionLocked));

    // ── the prompt matrix ────────────────────────────────────────

    [Fact]
    public void An_empty_slot_places_with_no_question()
    {
        var layout = DashboardLayout.Default();
        DashboardLayoutRule.Clear(layout, 0);
        Assert.Equal(PickPrompt.Place, DashboardPickerRule.Decide(layout, 0, "focusgaze"));
    }

    [Fact]
    public void An_occupied_single_that_can_share_offers_replace_or_split()
    {
        // Slot 0 is flash, one FX; focusgaze is another. Two FX in one cell is a split tile.
        Assert.Equal(PickPrompt.AskReplaceOrSplit,
            DashboardPickerRule.Decide(DashboardLayout.Default(), 0, "focusgaze"));
    }

    [Fact]
    public void An_occupied_single_that_cannot_share_offers_replace_only()
    {
        var layout = DashboardLayout.Default();

        // A door cannot be half of a tile, in either direction: not as the occupant...
        Assert.Equal(PickPrompt.AskReplaceOnly, DashboardPickerRule.Decide(layout, 4, "focusgaze"));
        // ... and not as the pick.
        Assert.Equal(PickPrompt.AskReplaceOnly, DashboardPickerRule.Decide(layout, 0, "fyp"));
    }

    [Fact]
    public void An_already_split_slot_is_full_and_offers_replace_only()
    {
        // Slot 1 is video|bubblecount. A third feature in one cell is not a tile.
        Assert.Equal(PickPrompt.AskReplaceOnly,
            DashboardPickerRule.Decide(DashboardLayout.Default(), 1, "focusgaze"));
    }

    [Fact]
    public void A_feature_already_on_the_wall_moves_without_a_question()
    {
        var layout = DashboardLayout.Default();

        // lockcard sits in slot 8; dropping it on an occupied slot 0 is a move, not a replacement
        // question, because the user is vacating a tile in the same gesture.
        Assert.Equal(PickPrompt.Move, DashboardPickerRule.Decide(layout, 0, "lockcard"));
        // Even onto an empty slot, and even from the far half of a split tile.
        DashboardLayoutRule.Clear(layout, 0);
        Assert.Equal(PickPrompt.Move, DashboardPickerRule.Decide(layout, 0, "bubblecount"));
    }

    [Fact]
    public void An_unknown_key_asks_rather_than_acting()
    {
        // Place refuses it downstream; what matters here is that nothing happens silently.
        Assert.Equal(PickPrompt.AskReplaceOnly,
            DashboardPickerRule.Decide(DashboardLayout.Default(), 0, "nonsense"));
        Assert.Equal(PickPrompt.AskReplaceOnly,
            DashboardPickerRule.Decide(DashboardLayout.Default(), 0, null));
    }

    [Fact]
    public void Every_prompt_the_matrix_can_produce_is_reachable()
    {
        // A guard against a fifth outcome being added and never wired to a button.
        var seen = new System.Collections.Generic.HashSet<PickPrompt>();
        var empty = DashboardLayout.Default();
        DashboardLayoutRule.Clear(empty, 0);

        seen.Add(DashboardPickerRule.Decide(empty, 0, "focusgaze"));
        seen.Add(DashboardPickerRule.Decide(DashboardLayout.Default(), 0, "focusgaze"));
        seen.Add(DashboardPickerRule.Decide(DashboardLayout.Default(), 1, "focusgaze"));
        seen.Add(DashboardPickerRule.Decide(DashboardLayout.Default(), 0, "lockcard"));

        Assert.Equal(Enum.GetValues<PickPrompt>().Length, seen.Count);
    }

    [Fact]
    public void A_slot_outside_the_wall_throws_rather_than_wrapping()
    {
        var layout = DashboardLayout.Default();
        Assert.Throws<ArgumentOutOfRangeException>(() => DashboardPickerRule.Decide(layout, -1, "flash"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DashboardPickerRule.Decide(layout, DashboardLayout.SlotCount, "flash"));
    }

    // ── split availability ───────────────────────────────────────

    [Fact]
    public void Split_is_offered_only_where_the_pair_can_actually_form()
    {
        var layout = DashboardLayout.Default();

        Assert.True(DashboardPickerRule.CanOfferSplit(layout, 0, "focusgaze"));   // FX + FX
        Assert.False(DashboardPickerRule.CanOfferSplit(layout, 1, "focusgaze"));  // already split
        Assert.False(DashboardPickerRule.CanOfferSplit(layout, 4, "focusgaze"));  // occupant is a door
        Assert.False(DashboardPickerRule.CanOfferSplit(layout, 0, "fyp"));        // pick is a door
        Assert.False(DashboardPickerRule.CanOfferSplit(layout, 0, "flash"));      // itself, both halves
        Assert.False(DashboardPickerRule.CanOfferSplit(layout, 0, "nonsense"));

        DashboardLayoutRule.Clear(layout, 0);
        Assert.False(DashboardPickerRule.CanOfferSplit(layout, 0, "focusgaze"));  // nothing to share with
    }

    [Fact]
    public void What_the_rule_offers_is_what_Place_accepts()
    {
        // The two must not drift: an offered Split that Place refuses is a dead button.
        foreach (var key in new[] { "focusgaze", "fyp", "flash", "lockcard" })
            for (int slot = 0; slot < DashboardLayout.SlotCount; slot++)
            {
                var probe = DashboardLayout.Default();
                var offered = DashboardPickerRule.CanOfferSplit(probe, slot, key);
                var outcome = DashboardLayoutRule.Place(probe, slot, key, split: true);
                Assert.Equal(offered, outcome == PlaceOutcome.Split);
            }
    }

    // ── what an edit writes ──────────────────────────────────────

    [Fact]
    public void Reset_writes_the_default_wire_and_stays_touched()
    {
        var (wire, touched) = DashboardPickerRule.Reset();

        Assert.Equal(DashboardLayoutRule.ToWire(DashboardLayout.Default()), wire);
        Assert.True(DashboardLayoutRule.FromWire(wire).IsDefault);
        // Touched records that the user has had an opinion, not that their wall differs from the
        // shipped one - the cloud adopt reads it, so a reset must not re-open the door to another
        // machine's layout.
        Assert.True(touched);
    }

    [Fact]
    public void Commit_writes_the_live_wall_and_marks_it_touched()
    {
        var layout = DashboardLayout.Default();
        DashboardLayoutRule.Place(layout, 0, "fyp", split: false);

        var (wire, touched) = DashboardPickerRule.Commit(layout);

        Assert.Equal(DashboardLayoutRule.ToWire(layout), wire);
        Assert.StartsWith("fyp,", wire, StringComparison.Ordinal);
        Assert.True(touched);
        Assert.False(DashboardLayoutRule.FromWire(wire).IsDefault);
    }
}

/// <summary>
/// The parts of edit mode that are not a rule: the copy the picker reaches for, and the pencils,
/// which are built in code because the nine slot hosts are empty cells by contract
/// (<see cref="DashboardSlotMapTests"/> fails on an authored child). Nothing here can be checked
/// by the compiler - a loc key is a string, and a pencil is a Button added at runtime - so both
/// are scraped.
/// </summary>
public class DashboardPickerSurfaceTests
{
    /// <summary>Every key this phase added. Listed rather than scraped, because a key that is
    /// dropped from the code AND from the files together is exactly the regression a scrape of
    /// the code cannot see.</summary>
    public static readonly string[] NewKeys =
    {
        "dash_pencil_tip",
        "dash_ring_1", "dash_ring_2", "dash_ring_3", "dash_ring_4",
        "dash_picker_title", "dash_picker_done",
        "dash_picker_reset", "dash_picker_reset_title", "dash_picker_reset_confirm",
        "dash_picker_ask", "dash_picker_replace", "dash_picker_split", "dash_picker_cancel",
    };

    private static readonly string[] Languages =
        { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    [Fact]
    public void Every_new_key_exists_in_english()
    {
        var en = Strings("en");
        foreach (var key in NewKeys)
            Assert.True(en.ContainsKey(key), $"'{key}' is missing from en.json");
    }

    [Fact]
    public void Every_new_key_exists_in_all_nine_language_files()
    {
        // A missing key falls back to the raw key string on screen, which is how "dash_picker_done"
        // ends up on a button in a RU screenshot.
        foreach (var lang in Languages)
        {
            var map = Strings(lang);
            foreach (var key in NewKeys)
                Assert.True(map.ContainsKey(key), $"'{key}' is missing from {lang}.json");
        }
    }

    [Fact]
    public void Every_dash_key_the_picker_asks_for_is_one_of_them()
    {
        // The other direction: a Loc.Get in the picker naming a key nobody added. A trailing
        // fragment counts as declared when a real key starts with it - "dash_ring_" is built by
        // concatenating the ring number, which is the one key here that is not a literal.
        foreach (var file in new[] { PickerCodePath, PickerXamlPath, EditPath })
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"""(dash_[a-z0-9_]+)"""))
            {
                var asked = m.Groups[1].Value;
                Assert.True(Array.Exists(NewKeys, k => k == asked
                                || (asked.EndsWith("_", StringComparison.Ordinal)
                                    && k.StartsWith(asked, StringComparison.Ordinal))),
                    $"{Path.GetFileName(file)} asks for '{asked}', which this phase never added");
            }
    }

    [Fact]
    public void There_is_a_pencil_for_every_slot_host()
    {
        var renderer = File.ReadAllText(RendererPath);
        var edit = File.ReadAllText(EditPath);

        // The pencils are built from the SAME hosts array the cards are rendered into, so the
        // nine cells and the nine pencils cannot go out of step.
        Assert.Contains("BuildDashboardPencils(hosts)", renderer, StringComparison.Ordinal);
        for (int slot = 0; slot < DashboardLayout.SlotCount; slot++)
            Assert.Contains($"tab.Slot{slot}", renderer, StringComparison.Ordinal);
        Assert.Contains("for (int i = 0; i < hosts.Length; i++)", edit, StringComparison.Ordinal);
        Assert.Contains("host.Children.Add(pencil)", edit, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"dash_pencil_tip\")", edit, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pencil_is_hidden_by_the_rule_and_not_merely_disabled()
    {
        var edit = File.ReadAllText(EditPath);
        Assert.Contains("DashboardPickerRule.ShowPencil(locked)", edit, StringComparison.Ordinal);
        Assert.Contains("Visibility.Collapsed", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("pencil.IsEnabled", edit, StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_the_picker_goes_through_the_session_refusal()
    {
        Assert.Contains("RefuseActionIfSessionLocked(\"dashboard:edit\")",
            File.ReadAllText(EditPath), StringComparison.Ordinal);
    }

    [Fact]
    public void The_picker_is_an_overlay_and_not_a_Popup()
    {
        // A Popup gets its own HWND, which does not inherit the root Viewbox's scale, so it would
        // line up with the wall at exactly one window size. It also has to sit in the rect the
        // Phase F rolodex takes over.
        var xaml = File.ReadAllText(PickerXamlPath);
        Assert.DoesNotContain("<Popup", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRowSpan(picker, 4)", File.ReadAllText(EditPath), StringComparison.Ordinal);
    }

    // ── the scrape ───────────────────────────────────────────────

    private static string ClientDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static string RendererPath => Path.Combine(ClientDir(), "MainWindow", "MainWindow.DashboardSlots.cs");
    private static string EditPath => Path.Combine(ClientDir(), "MainWindow", "MainWindow.DashboardEdit.cs");
    private static string PickerXamlPath =>
        Path.Combine(ClientDir(), "Views", "Controls", "Dashboard", "DashboardPickerPopup.xaml");
    private static string PickerCodePath => PickerXamlPath + ".cs";

    private static Dictionary<string, string> Strings(string lang)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(ClientDir(), "Localization", "Languages", lang + ".json")));
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject()) map[p.Name] = p.Value.GetString() ?? "";
        return map;
    }
}
