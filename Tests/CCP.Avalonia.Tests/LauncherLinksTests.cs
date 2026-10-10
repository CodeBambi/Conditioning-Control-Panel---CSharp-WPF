using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Page wave x1 (P13, P14, P15): the launcher's Media pill and dialog, the shortcut link and
/// the tile shortcut button, and the bottom row's friends chip and "What's new" link.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class LauncherLinksTests
{
    private static void Run(Action body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            CoreSettings.Current.LauncherSoundEnabled = false;
            try { body(); }
            finally { CoreSettings.ServiceProvider = null; }
        });
    }

    [Fact]
    public void TheLauncherHasItsMediaPill_ShortcutLink_FriendsChip_AndWhatsNew() => Run(() =>
    {
        var w = new LauncherWindow();
        try
        {
            Assert.NotNull(w.FindControl<Button>("BtnGear"));
            Assert.NotNull(w.FindControl<Button>("BtnSound"));
            Assert.NotNull(w.FindControl<Button>("ShortcutLink"));
            Assert.NotNull(w.FindControl<Button>("WhatsNewLink"));
            Assert.NotNull(w.FindControl<FriendsRailChip>("FriendsChip"));
            Assert.Equal("launcher_add_shortcut", w.ShortcutLinkKey);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void ShortcutLink_WritesThePanelShortcut_AndSaysHowItWent() => Run(() =>
    {
        var w = new LauncherWindow();
        try
        {
            string? asked = "unset";
            w.ShortcutWriter = id => { asked = id; return true; };
            w.FindControl<Button>("ShortcutLink")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(asked);   // null = the panel
            Assert.Equal("launcher_shortcut_added", w.ShortcutLinkKey);

            w.ShortcutWriter = _ => false;
            w.WriteShortcut("backroom");
            Assert.Equal("launcher_shortcut_failed", w.ShortcutLinkKey);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void EveryRevealedTileCarriesAShortcutButton_ThatAsksForItsOwnGame() => Run(() =>
    {
        var w = new LauncherWindow();
        try
        {
            string? asked = null;
            w.ShortcutWriter = id => { asked = id; return true; };
            w.BuildTiles();
            var tiles = w.FindControl<UniformGrid>("GamesGrid")!.Children.OfType<Border>().ToArray();
            Assert.NotEmpty(tiles);
            foreach (var tile in tiles)
            {
                var id = (string)tile.Tag!;
                var btn = tile.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => Equals(b.Tag, "tile-shortcut"));
                if (!LauncherWindow.Revealed(LauncherCards.Find(id)!)) { Assert.Null(btn); continue; }   // the mystery card has none
                Assert.NotNull(btn);
                Assert.Equal(0, btn!.Opacity);   // unseen until the pointer is on the tile
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(id, asked);
            }
        }
        finally { w.Close(); }
    });

    [Fact]
    public void LinuxEntry_LandsInTheApplicationsFolder_AndOnTheDesktop()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-shortcut-" + Guid.NewGuid().ToString("N"));
        var (apps, desktop) = (Path.Combine(root, "apps"), Path.Combine(root, "desk"));
        Directory.CreateDirectory(desktop);
        try
        {
            (LauncherShortcutWriter.ApplicationsOverride, LauncherShortcutWriter.DesktopOverride, LauncherShortcutWriter.ForceDesktopEntry) = (apps, desktop, true);
            Assert.True(LauncherShortcutWriter.TryCreateDesktopShortcut("dtrh"));
            foreach (var folder in new[] { apps, desktop })
            {
                var text = File.ReadAllText(Path.Combine(folder, "cclabs-dtrh.desktop"));
                Assert.Contains(" --game dtrh\n", text);
                Assert.Contains("Type=Application\n", text);
            }
            Assert.True(LauncherShortcutWriter.TryCreateDesktopShortcut(null));
            Assert.Contains(" --panel\n", File.ReadAllText(Path.Combine(apps, "cclabs-panel.desktop")));
            Assert.False(LauncherShortcutWriter.TryCreateDesktopShortcut("no-such-game"));
            // A test host never writes to the real desktop.
            Assert.True(LauncherShortcutWriter.IsTestHost(Environment.ProcessPath));
            Assert.False(LauncherShortcutWriter.IsTestHost("C:/Program Files/CCP/CCP.Avalonia.exe"));
        }
        finally
        {
            (LauncherShortcutWriter.ApplicationsOverride, LauncherShortcutWriter.DesktopOverride, LauncherShortcutWriter.ForceDesktopEntry) = (null, null, null);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void MediaDialog_AsksBeforeGoingOnline_AndPointsTheBackRoomBackAtTheApp() => Run(() =>
    {
        var s = CoreSettings.Current;
        s.MediaSource = "local";
        s.RemoteMediaConsented = false;
        s.FypOnlineConsented = false;
        s.BackRoomMediaSource = "bundled";
        var dlg = new LauncherMediaDialog();
        var online = dlg.SourceChips.First(c => Equals(c.Tag, "online"));
        var local = dlg.SourceChips.First(c => Equals(c.Tag, "local"));
        Assert.True(local.IsChecked);
        Assert.False(dlg.RatioRowVisible);

        // "No" writes nothing and the chips stay where they were.
        dlg.ConsentOverride = (_, _) => Task.FromResult(false);
        online.IsChecked = true;
        dlg.LastChange.GetAwaiter().GetResult();
        Assert.Equal("local", s.MediaSource);
        Assert.False(s.HasRemoteMediaConsent);
        Assert.True(local.IsChecked);
        Assert.False(online.IsChecked);

        // "Yes" is remembered, the source moves and the room follows the app again.
        dlg.ConsentOverride = (_, _) => Task.FromResult(true);
        online.IsChecked = true;
        dlg.LastChange.GetAwaiter().GetResult();
        Assert.Equal("online", s.MediaSource);
        Assert.True(s.HasRemoteMediaConsent);
        Assert.Equal("auto", s.BackRoomMediaSource);
        Assert.NotEmpty(s.FypOnlineNiches);

        // Un-clicking the live chip never leaves the app with no source.
        online.IsChecked = false;
        dlg.LastChange.GetAwaiter().GetResult();
        Assert.Equal("online", s.MediaSource);
        Assert.True(online.IsChecked);

        // The last niche stays on.
        foreach (var chip in dlg.NicheChips) chip.IsChecked = false;
        Assert.Single(dlg.NicheChips, c => c.IsChecked == true);

        // Mixed shows the share row; the master slider writes the app-wide volume.
        dlg.SourceChips.First(c => Equals(c.Tag, "mixed")).IsChecked = true;
        dlg.LastChange.GetAwaiter().GetResult();
        Assert.True(dlg.RatioRowVisible);
        dlg.MasterSlider.Value = 40;
        Assert.Equal(40, s.MasterVolume);
    });
}
