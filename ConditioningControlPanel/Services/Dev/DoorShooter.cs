using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.Dev;

/// <summary>
/// Offscreen screenshot rig for the nav doors (launched via <c>--shoot-doors [outDir]</c>).
///
/// <para><b>Why this exists.</b> Screen-scraping the app needs a composited desktop. When the
/// machine's display is asleep or the session is locked, DWM stops compositing: a full-screen
/// grab returns a stale frame (measured 2026-08-11 — two captures 40s apart were byte-identical
/// with the taskbar clock frozen 37 minutes in the past) and <c>PrintWindow</c> hands back a blank
/// surface. UI Automation still works perfectly in that state, so the app is fine and only the
/// capture is broken. <see cref="RenderTargetBitmap"/> rasterizes the visual tree in-process and
/// never asks DWM for anything, so it produces true pixels with the monitor off — which is the
/// whole point when the owner is reviewing UI remotely.</para>
///
/// <para><b>Why it runs inside the real app</b> rather than in the test harness: this is the live
/// window with real services, the real mod, the real tier and the user's real data. A harness-built
/// MainWindow would be a different window, and the differences are exactly the kind of thing a
/// design review is looking at.</para>
///
/// <para>Doors are driven by raising <see cref="ButtonBase.ClickEvent"/> on the real header button,
/// so navigation goes through the same handler a user's click does — no private ShowTab call that
/// could drift from the shipped path. The app shuts down when the last shot is written.</para>
/// </summary>
internal static class DoorShooter
{
    /// <summary>Door headers, top to bottom, matching NavDoorMap in MainWindow.TabNavigation.cs.</summary>
    private static readonly (string Button, string File)[] Doors =
    {
        ("DoorHome",      "01-home"),
        ("DoorStudio",    "02-studio"),
        ("DoorCompanion", "03-companion"),
        ("DoorPlay",      "04-play"),
        ("DoorYou",       "05-you"),
        ("DoorLibrary",   "06-library"),
        ("DoorSettings",  "07-settings"),
    };

    /// <summary>
    /// Rail entries worth their own shot. The accordion keeps exactly one door open, so each entry
    /// names the door to open first.
    /// </summary>
    private static readonly (string Door, string Button, string File)[] Entries =
    {
        ("DoorStudio",    "BtnPresets",      "08-studio-presets"),
        ("DoorStudio",    "BtnNavHaptics",   "09-studio-haptics"),
        ("DoorCompanion", "BtnNavAwareness", "10-companion-awareness"),
        ("DoorYou",       "BtnQuests",       "11-you-quests"),
        ("DoorYou",       "BtnAchievements", "12-you-achievements"),
        ("DoorYou",       "BtnPrograms",     "13-you-programs"),
        ("DoorYou",       "BtnLeaderboard",  "14-you-leaderboard"),

        // Library's Mods and Catalogue entries are deliberately absent. Both open their own
        // window via ShowDialog(), and a modal loop parks this method's continuation until the
        // dialog closes - measured 2026-08-11 as a hang that ate the run's last two shots and
        // the completion sentinel with them. Anything added here must navigate WITHIN the main
        // window; a door entry that opens a window needs its own capture path.
    };

    /// <summary>Time for a door's expand animation (160ms) plus whatever the panel loads.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(1400);

    public static void Run(Window window, string outDir)
    {
        // Deferred to Loaded so the first layout pass has happened; rendering an unarranged
        // visual yields an empty bitmap and would look exactly like a broken door.
        if (window.IsLoaded) _ = Shoot(window, outDir);
        else window.Loaded += (_, _) => _ = Shoot(window, outDir);
    }

    private static async Task Shoot(Window window, string outDir)
    {
        var written = 0;
        try
        {
            Directory.CreateDirectory(outDir);
            App.Logger?.Information("DoorShooter: writing to {Dir}", outDir);

            // Let startup settle: services finish wiring, the mod's art resolves, first-run
            // popups (which are separate windows and never appear in these shots) come and go.
            await Task.Delay(TimeSpan.FromSeconds(6));

            foreach (var (button, file) in Doors)
            {
                if (!Click(window, button)) continue;
                await Task.Delay(Settle);
                if (Capture(window, Path.Combine(outDir, file + ".png"))) written++;
            }

            foreach (var (door, button, file) in Entries)
            {
                Click(window, door);                      // accordion: reopen the owning door
                await Task.Delay(TimeSpan.FromMilliseconds(700));
                if (!Click(window, button)) continue;
                await Task.Delay(Settle);
                if (Capture(window, Path.Combine(outDir, file + ".png"))) written++;
            }

            // The hover-expanded rail. UIA/offscreen rendering cannot hover, so the state is
            // driven through the same entry point the tutorial and Ctrl+K palette use.
            Click(window, "DoorHome");
            await Task.Delay(TimeSpan.FromMilliseconds(600));
            if (window is MainWindow mw)
            {
                mw.HoldNavRailOpen();
                // Shorter than the rail's 1000ms auto-collapse, longer than its 150ms tween.
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                if (Capture(window, Path.Combine(outDir, "17-rail-expanded.png"))) written++;
            }

            // Home's audio drawer, open. Same reason as the rail: no pointer to click it with.
            if (window is MainWindow home && home.SettingsTab?.HomeBtnAudioAdvanced != null)
            {
                Click(window, "DoorHome");
                await Task.Delay(TimeSpan.FromMilliseconds(600));
                home.SettingsTab.HomeBtnAudioAdvanced.IsChecked = true;
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                if (Capture(window, Path.Combine(outDir, "18-home-audio-open.png"))) written++;
                home.SettingsTab.HomeBtnAudioAdvanced.IsChecked = false;
            }

            App.Logger?.Information("DoorShooter: {Count} shots written", written);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "DoorShooter failed after {Count} shots", written);
        }
        finally
        {
            // Write a sentinel so the driving script can tell "finished" from "still going" and
            // from "died early" without guessing at file counts.
            try
            {
                File.WriteAllText(Path.Combine(outDir, "_done.txt"),
                    string.Format(CultureInfo.InvariantCulture, "{0} shots at {1:o}",
                                  written, DateTime.Now));
            }
            catch { }

            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => Application.Current.Shutdown()));
        }
    }

    /// <summary>
    /// Navigates by raising the real Click on the named header/entry button, so this goes through
    /// the shipped handler. Returns false (and logs) when the button is missing or disabled — a
    /// tier-locked entry is a legitimate skip, not a failure.
    /// </summary>
    private static bool Click(Window window, string name)
    {
        if (window.FindName(name) is not ButtonBase b)
        {
            App.Logger?.Warning("DoorShooter: '{Name}' not found", name);
            return false;
        }
        if (!b.IsEnabled)
        {
            App.Logger?.Information("DoorShooter: '{Name}' is disabled - skipped", name);
            return false;
        }

        b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        return true;
    }

    private static bool Capture(Window window, string path)
    {
        try
        {
            var source = (Visual)window.Content ?? window;
            var size = window.Content is FrameworkElement fe && fe.ActualWidth > 0
                ? new Size(fe.ActualWidth, fe.ActualHeight)
                : new Size(window.ActualWidth, window.ActualHeight);

            if (size.Width < 4 || size.Height < 4)
            {
                App.Logger?.Warning("DoorShooter: {Path} skipped - degenerate size {W}x{H}",
                                    path, size.Width, size.Height);
                return false;
            }

            // DPI is pinned to 96 so every shot is the same pixel size regardless of the
            // machine's scaling - a design review compares them side by side.
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(size.Width),
                                             (int)Math.Ceiling(size.Height),
                                             96, 96, PixelFormats.Pbgra32);

            // The window's Background paints BEHIND Content and is not part of Content's visual,
            // so rendering Content alone leaves every element that declares no background of its
            // own fully transparent. That is not a cosmetic difference: a PNG viewer shows alpha-0
            // as WHITE, so white-on-dark text renders as white-on-white and reads in the shot as
            // a missing control. Cost a false "four doors have no title" bug report on 2026-08-11.
            // Compose the window background first, then the content over it.
            // Two Render calls, not a VisualBrush: RenderTargetBitmap composites rather than
            // clearing, so the backdrop goes down first and the live visual paints over it at its
            // own coordinates. A VisualBrush would re-origin the content on its DESCENDANT bounds,
            // and the avatar tube hangs off the left at x=-154 - which silently shifted every shot
            // 154px right when this was first written that way.
            var backdrop = new DrawingVisual();
            using (var dc = backdrop.RenderOpen())
                dc.DrawRectangle(window.Background ?? Brushes.Black, null,
                                 new Rect(0, 0, size.Width, size.Height));
            rtb.Render(backdrop);
            rtb.Render(source);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var stream = File.Create(path);
            encoder.Save(stream);

            App.Logger?.Information("DoorShooter: {Path} ({W}x{H})", path, (int)size.Width, (int)size.Height);
            return true;
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "DoorShooter: capture failed for {Path}", path);
            return false;
        }
    }
}
