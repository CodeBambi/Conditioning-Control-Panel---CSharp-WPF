using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A switched-off companion must always be one click from coming back, inside the app.
///
/// <para>7.0.1 shipped with no way to turn it on but the tray (ask-support, 2026-10-01): Dismiss
/// turns <c>AvatarEnabled</c> off for good, the v2 Companion page hides the old room whose eye
/// toggle and Wake button were the switch, Pop out did nothing while the companion was off, Look
/// and personality said "turn it on" with nothing to press, and the She's Listening voice test
/// said "show the avatar" without saying where. These are source tripwires for the WPF-bound
/// halves, so the next redesign cannot drop the switch again without failing here.</para>
/// </summary>
public class CompanionOnSwitchTests
{
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { AppDir() }.Concat(parts).ToArray()));

    /// <summary>Whitespace-insensitive contains, so a reflow of the source never fails a tripwire.</summary>
    private static bool Mentions(string haystack, string needle)
    {
        static string Flat(string s) => Regex.Replace(s, @"\s+", " ");
        return Flat(haystack).Contains(Flat(needle), StringComparison.Ordinal);
    }

    [Fact]
    public void Companion_settings_sheet_carries_the_on_switch()
    {
        var page = ReadSource("Views", "Controls", "Companion", "V2", "ConversationPage.xaml");
        Assert.True(Mentions(page, "Content=\"{loc:Str companion_v2_show}\" Command=\"{Binding Room.Hero.ToggleShownCommand}\""),
            "Companion > Settings needs a Show companion button bound to the hero's on switch");
        Assert.True(Mentions(page, "Visibility=\"{Binding CompanionHidden, Converter={StaticResource Visible}}\""),
            "the Show companion button appears while the companion is off");
        Assert.True(Mentions(page, "Content=\"{loc:Str companion_v2_hide}\" Command=\"{Binding Room.Hero.ToggleShownCommand}\""),
            "and its Hide twin while it is on");
    }

    /// <summary>7.1.0 shipped with the switch only in the Settings sheet, which the nav rework made
    /// unreachable (its opener now goes to the Personality page). The switch must sit in the page
    /// itself, ahead of the sheet, so it shows without opening anything.</summary>
    [Fact]
    public void Companion_page_header_carries_the_on_switch_outside_the_sheet()
    {
        var page = Regex.Replace(ReadSource("Views", "Controls", "Companion", "V2", "ConversationPage.xaml"), @"\s+", " ");
        int sheet = page.IndexOf("x:Name=\"SheetOverlay\"", StringComparison.Ordinal);
        Assert.True(sheet > 0, "the conversation page still has its settings sheet");
        string show = "Content=\"{loc:Str companion_v2_show}\" Command=\"{Binding Room.Hero.ToggleShownCommand}\"";
        string hide = "Content=\"{loc:Str companion_v2_hide}\" Command=\"{Binding Room.Hero.ToggleShownCommand}\"";
        int s = page.IndexOf(show, StringComparison.Ordinal), h = page.IndexOf(hide, StringComparison.Ordinal);
        Assert.True(s >= 0 && s < sheet, "Show companion sits on the page, not only inside the sheet");
        Assert.True(h >= 0 && h < sheet, "Hide companion sits on the page, not only inside the sheet");
    }

    [Fact]
    public void Look_and_personality_offers_the_switch_where_it_says_the_companion_is_off()
    {
        var card = ReadSource("Views", "Controls", "Companion", "CompanionPickerCard.xaml.cs");
        Assert.True(Mentions(card, "BtnTurnOn.Visibility = tube == null ? Visibility.Visible : Visibility.Collapsed;"),
            "the Turn it on button shows exactly when the card says the companion is switched off");
        Assert.True(Mentions(card, "App.MainWindowRef?.SetAvatarEnabled(true);"),
            "and it flips the same switch as the Companion settings");
    }

    [Fact]
    public void Pop_out_wakes_a_switched_off_companion()
    {
        var source = ReadSource("MainWindow", "MainWindow.Patreon.cs");
        Assert.True(Mentions(source, "if (App.Settings?.Current?.AvatarEnabled != true) WakeBambiUp();"),
            "Pop out on a switched-off companion wakes it, popped out, as the tray's Wake does");
    }

    [Fact]
    public void Voice_test_offers_to_turn_the_companion_on()
    {
        var source = ReadSource("Services", "AutonomyService.cs");
        var start = source.IndexOf("public void TestVoiceCommand()", StringComparison.Ordinal);
        Assert.True(start >= 0, "TestVoiceCommand moved");
        var body = source.Substring(start, Math.Min(3000, source.Length - start));
        Assert.Contains("voice_test_companion_off_body", body);
        Assert.True(Mentions(body, "App.MainWindowRef?.SetAvatarEnabled(true);"),
            "a yes on the prompt turns the companion on and the test carries on");
    }

    [Theory]
    [InlineData("companion_v2_show")]
    [InlineData("companion_v2_hide")]
    [InlineData("modmgr_avatar_turn_on")]
    [InlineData("voice_test_companion_off_title")]
    [InlineData("voice_test_companion_off_body")]
    public void Every_language_carries_the_switch_copy(string key)
    {
        foreach (var lang in new[] { "de", "en", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" })
        {
            using var doc = JsonDocument.Parse(ReadSource("Localization", "Languages", lang + ".json"));
            Assert.True(doc.RootElement.TryGetProperty(key, out var value) && value.GetString()?.Trim().Length > 0,
                $"{lang}.json is missing {key}");
            Assert.DoesNotContain("—", value.GetString());
        }
    }
}
