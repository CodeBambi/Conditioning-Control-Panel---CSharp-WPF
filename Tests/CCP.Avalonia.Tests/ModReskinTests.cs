using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Home;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// H14: a mod switch re-skins every surface the shell owns, live (WPF MainWindow ApplyActiveModChange
/// plus its ModChanged subscriptions). Walks a real switch through the header combo, then a mod that
/// overrides art, then back.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ModReskinTests
{
    private static readonly string[] Steps =
    {
        "palette", "logo", "tile art", "tile names", "door art", "quest stamps", "quests tab",
        "achievement grid", "profile badges", "companion roster", "wall states", "intake pass",
    };

    [Fact]
    public Task AModSwitchRepaintsEverySurface() => ModChoiceTests.Run(async (shell, svc, server) =>
    {
        var oldProvider = CoreModArt.OverridePathProvider;
        var scratch = Directory.CreateTempSubdirectory("ccp-reskin-").FullName;
        try
        {
            var dash = shell.SettingsPage!;
            var dial = dash.FindControl<AnimatedLogoDial>("ImgLogo")!;
            var combo = shell.FindControl<ComboBox>("ModSelectorCombo")!;

            // The saved mod was painted at startup: one pass, every step finished.
            Assert.True(shell.ModReskinPasses >= 1);
            Assert.Equal(Steps, shell.LastModReskin.Select(s => s.Step).ToArray());
            Assert.All(shell.LastModReskin, s => Assert.True(s.Ok, s.Step));
            Assert.False(dial.ShowsModArtwork);                       // CCP Default: the animated dial
            Assert.Equal("logo2.png", MainShellWindow.LogoFileForActiveMod());

            var passes = shell.ModReskinPasses;
            var accent = (Color)Application.Current!.Resources["PinkColor"]!;
            var mystery = dash.CardMystery.Icon;
            var vault = dash.CardVault.Icon;
            var flash = dash.CardFlash.Icon;

            // 1. A real switch through the header combo.
            combo.SelectedItem = shell.AvailableMods.Single(i => i.Id == BuiltInMods.DronificationId);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(BuiltInMods.DronificationId, AvApp.Mods!.ActiveModId);
            Assert.True(shell.ModReskinPasses > passes);                                   // ModChanged ran the pass
            Assert.All(shell.LastModReskin, s => Assert.True(s.Ok, s.Step));
            Assert.NotEqual(accent, (Color)Application.Current.Resources["PinkColor"]!);   // palette
            Assert.NotSame(mystery, dash.CardMystery.Icon);                                // themed ? box
            Assert.NotSame(vault, dash.CardVault.Icon);                                    // themed vault
            Assert.NotSame(flash, dash.CardFlash.Icon);                                    // re-read, not kept
            Assert.Equal("logo.png", MainShellWindow.LogoFileForActiveMod());
            Assert.Equal(MainShellWindow.StripLeadingGlyph(MainShellWindow.ModAwareLabel("Flash Images", "section_flash_images")),
                dash.CardFlash.Title);
            Assert.Equal(MainShellWindow.StripLeadingGlyph(MainShellWindow.ModAwareLabel("Brain Drain", "label_brain_drain")),
                dash.ComboMindDrain.TitleB);

            // 2. A mod that ships its own art: every art surface takes the override.
            var png = Path.Combine(scratch, "own.png");
            using (var src = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/nav/door_home.png")))
            using (var dst = File.Create(png)) src.CopyTo(dst);
            var asked = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CoreModArt.OverridePathProvider = name => { lock (asked) asked.Add(name); return png; };

            var doorsBefore = MainShellWindow.DoorArt.Select(d => shell.FindControl<Image>(d.Image)!.Source).ToArray();
            var splitA = dash.ComboSpiralPink.IconA;
            var splitB = dash.ComboSpiralPink.IconB;
            mystery = dash.CardMystery.Icon;
            shell.RunModReskin();

            Assert.All(shell.LastModReskin, s => Assert.True(s.Ok, s.Step));
            Assert.True(dial.ShowsModArtwork);                                             // the mod's wordmark, still
            Assert.False(dial.IsAnimating);
            var doorsAfter = MainShellWindow.DoorArt.Select(d => shell.FindControl<Image>(d.Image)!.Source).ToArray();
            for (var i = 0; i < doorsBefore.Length; i++) Assert.NotSame(doorsBefore[i], doorsAfter[i]);
            Assert.NotSame(splitA, dash.ComboSpiralPink.IconA);
            Assert.NotSame(splitB, dash.ComboSpiralPink.IconB);
            Assert.NotSame(mystery, dash.CardMystery.Icon);
            foreach (var name in new[]
                     {
                         "logo.png", "nav/door_home.png", "nav/door_settings.png", "features/flash.png",
                         "features/mysterybox.png", "features/vault.png", "features/brain_drain.png",
                         "features/Phrase_Lock.png",
                     })
                Assert.Contains(name, asked);   // the base path is asked: a .ccpmod override wins over a themed face

            // 3. Back to a mod with no art of its own: the dial returns.
            CoreModArt.OverridePathProvider = oldProvider;
            combo.SelectedItem = shell.AvailableMods.Single(i => i.Id == BuiltInMods.CCPDefaultId);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(BuiltInMods.CCPDefaultId, AvApp.Mods.ActiveModId);
            Assert.False(dial.ShowsModArtwork);
            Assert.Equal(accent, (Color)Application.Current.Resources["PinkColor"]!);

            // The content rules a switch re-reads: Bambi audio only under the Bambi / Sissy mods,
            // niche personas off the CCP Default list.
            Assert.False(ModAudioPolicy.UsesSharedSubAudio(BuiltInMods.CCPDefaultId));
            Assert.False(ModAudioPolicy.UsesSharedSubAudio(BuiltInMods.LockedId));
            Assert.False(ModAudioPolicy.UsesBaselineVoicePack(BuiltInMods.CCPDefaultId));
            Assert.True(ModAudioPolicy.UsesSharedSubAudio(BuiltInMods.BambiSleepId));
            Assert.True(ModAudioPolicy.UsesSharedSubAudio(BuiltInMods.SissyHypnoId));
            Assert.DoesNotContain(PersonalityService.Shared.GetAllPresets(), p => PersonalityPresets.IsHiddenInNeutral(p.Id)
                && p.Id != CoreSettings.Current.ActivePersonalityPresetId);
            await Task.CompletedTask;
        }
        finally
        {
            CoreModArt.OverridePathProvider = oldProvider;
            try { Directory.Delete(scratch, true); } catch { /* a decoded file may still be open */ }
        }
    });

    [Fact]
    public void TheGlyphStripKeepsPlainAndAllSymbolText()
    {
        Assert.Equal("Flash Images", MainShellWindow.StripLeadingGlyph("⚡ Flash Images"));
        Assert.Equal("Flash Images", MainShellWindow.StripLeadingGlyph("Flash Images"));
        Assert.Equal("⚡", MainShellWindow.StripLeadingGlyph("⚡"));
        Assert.Equal("", MainShellWindow.StripLeadingGlyph(""));
    }
}
