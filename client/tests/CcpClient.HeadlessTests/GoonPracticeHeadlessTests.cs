using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Features.Goon;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views;
using CcpClient.Tests;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The Goon door on the Play page, and the refusal rail the host window carries.
///
/// <para><b>Draw-level ONLY</b> (verification-harness.md evidence class): visual tree, real
/// input routing, real AXAML. <b>Nothing here claims the goon page loaded, that the init +
/// manifest handshake completed, or that a duel is playable.</b> The host window is never SHOWN
/// in these tests, deliberately: showing it would run the capability probe and build a real
/// embedded browser, which a headless frame cannot present and which no test should start. A
/// compile is not a load and a load is not a duel; both of those remain owed to a headed run.</para>
/// </summary>
public class GoonPracticeHeadlessTests
{
    private static async Task<(ApplicationHost Host, MainWindow Window)> BootAsync(string dir)
    {
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            LogSinkFactory = () => new DebugLogSink(),
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);

        var window = new MainWindow(host!);
        window.Show();
        window.UpdateLayout();
        return (host!, window);
    }

    private static T Descendant<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' in the mounted page");

    private static void Click(MainWindow window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        window.UpdateLayout();
    }

    /// <summary>How many times <paramref name="needle"/> occurs. Used to pin that a rail marker is
    /// UNIQUE before slicing on it: a decoy copy earlier in the row would otherwise pre-empt
    /// <c>IndexOf</c> and silently move every window measured from it.</summary>
    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var at = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ccp-sp130-headless-" + Guid.NewGuid().ToString("N"));

    // ==================================================================================
    // The door
    // ==================================================================================

    [AvaloniaFact]
    public async Task ColdStart_PlayDoorThenPractice_ReachesTheOneGoonLauncher()
    {
        var dir = TempDir();
        var (_, window) = await BootAsync(dir);
        window.Goon.DataDirectory = dir;

        GoonHostWindow? captured = null;
        // The window is CONSTRUCTED for real (its AXAML loads, its rail is built, its
        // participant binds a real loopback origin) and deliberately never shown — see the
        // class comment. Closing it is what disposes the participant.
        window.Goon.Open = w => captured = w;

        Click(window, window.FindControl<RadioButton>("DoorPlay")!);
        Click(window, Descendant<Button>(window, "GoonPracticeButton"));

        Assert.Equal(1, window.Goon.LaunchCount);
        Assert.Null(window.Goon.LastFault);
        Assert.NotNull(captured);

        captured!.Close();
        await TestWait.Until(
            () => !window.Goon.IsOpen,
            "the Goon launcher to drop its window and dispose the participant on close",
            () => $"isOpen={window.Goon.IsOpen}, launches={window.Goon.LaunchCount}");
    }

    [AvaloniaFact]
    public async Task TheGoonDoor_TakesTheClick_AndIsNeverDisabled()
    {
        var dir = TempDir();
        var (_, window) = await BootAsync(dir);
        window.Goon.DataDirectory = dir;
        window.Goon.Open = _ => { };

        Click(window, window.FindControl<RadioButton>("DoorPlay")!);
        var button = Descendant<Button>(window, "GoonPracticeButton");

        // Upstream's Goon card carries no lock band at all (Views/Tabs/PlayTabView.xaml:547-549
        // — "never a padlock over an open door"), and a greyed control that swallows the gesture
        // is the shape this port bans everywhere. The refusals live INSIDE, on four other doors.
        Assert.True(button.IsEnabled);
        Assert.True(button.IsEffectivelyEnabled);
        Assert.Equal("PRACTICE", button.Content);
    }

    [AvaloniaFact]
    public async Task TheGoonDoor_SaysOnItsOwnFace_ThatThisBuildPlaysPracticeOnly()
    {
        var dir = TempDir();
        var (_, window) = await BootAsync(dir);
        Click(window, window.FindControl<RadioButton>("DoorPlay")!);

        var line = Descendant<TextBlock>(window, "GoonScopeLine");
        Assert.True(line.IsVisible);
        var text = line.Text ?? "";
        Assert.Contains("Practice only", text, StringComparison.Ordinal);
        // The door names the four before a user opens it, so nothing behind it is a surprise.
        foreach (var named in new[] { "Hosting", "joining", "voice notes", "sending your own media" })
        {
            Assert.Contains(named, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [AvaloniaFact]
    public async Task AGoonLaunchFault_RendersOnItsOwnLine_AndIsNotARefusal()
    {
        var dir = TempDir();
        var (_, window) = await BootAsync(dir);
        window.Goon.DataDirectory = dir;
        window.Goon.Open = _ => throw new InvalidOperationException("headless: no browser surface");

        Click(window, window.FindControl<RadioButton>("DoorPlay")!);
        var fault = Descendant<TextBlock>(window, "GoonFaultText");
        Assert.False(fault.IsVisible);   // nothing has gone wrong yet

        Click(window, Descendant<Button>(window, "GoonPracticeButton"));

        Assert.NotNull(window.Goon.LastFault);
        Assert.True(fault.IsVisible);
        var text = fault.Text ?? "";
        // WPF's own words for THIS card (MainWindow/MainWindow.Lab.cs:207) — "open", not "start".
        Assert.StartsWith("Couldn't open the Goon Game:", text, StringComparison.Ordinal);
        // A fault is not a refusal, and the line says so out loud (LaunchFaultText.NotADecisionLine).
        Assert.Contains("nothing here was refused to you", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
    }

    // ==================================================================================
    // The refusal rail
    // ==================================================================================

    /// <summary>Builds the REAL host window (real AXAML, real participant, real rail) without
    /// showing it, and disposes the participant afterwards.</summary>
    private static async Task WithHostWindowAsync(Func<GoonHostWindow, Task> body)
    {
        var dir = TempDir();
        var (host, shell) = await BootAsync(dir);
        using var participant = new GoonParticipant(new DebugLogSink(), dir);
        participant.Start();
        var window = new GoonHostWindow(host, participant, shell, new DtrhWatchdog());
        try
        {
            await body(window);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [AvaloniaFact]
    public Task TheRefusalRail_CarriesOneRowPerOwnerGatedDoor() => WithHostWindowAsync(window =>
    {
        var rail = window.FindControl<Border>("RefusalRail")!;
        var panel = window.FindControl<StackPanel>("RefusalPanel")!;

        // Beside the page, not behind a click: the rail is a docked column of the host window,
        // visible from the moment it opens, because the page's own four sentences are false here.
        Assert.True(rail.IsVisible);
        Assert.Equal(Dock.Right, DockPanel.GetDock(rail));
        Assert.Equal(GoonDoors.Refused.Count, panel.Children.Count);
        Assert.Equal(4, panel.Children.Count);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task TheRefusalRail_RendersEachRefusalsHeadlineMissingAndOwnerGate() => WithHostWindowAsync(window =>
    {
        var panel = window.FindControl<StackPanel>("RefusalPanel")!;
        foreach (var refusal in GoonDoors.Refused)
        {
            var row = panel.Children.OfType<StackPanel>().FirstOrDefault(c => c.Name == $"Refusal{refusal.Door}");
            Assert.True(row is not null, $"no rendered row for the {refusal.Door} refusal");
            var texts = row!.Children.OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
            Assert.Equal(3, texts.Length);
            Assert.Equal(refusal.Headline, texts[0]);
            // The load-bearing one: what is MISSING, in the window, in words.
            Assert.Equal(refusal.Missing, texts[1]);
            Assert.Equal(refusal.OwnerGate, texts[2]);
            Assert.All(texts, t => Assert.True(t.Length > 0));
        }

        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task TheRefusalRail_NeverRepeatsThePagesSupporterPerkFraming() => WithHostWindowAsync(window =>
    {
        var panel = window.FindControl<StackPanel>("RefusalPanel")!;
        var rendered = string.Join(
            " ",
            panel.Children.OfType<StackPanel>()
                .SelectMany(row => row.Children.OfType<TextBlock>())
                .Select(t => t.Text ?? ""));

        // The page says "supporter perk" (ui/strings.js:41, :731) and "plain browser" (:751-752).
        // Both are false in this build, and the rail exists precisely because they cannot be
        // corrected without forking payload bytes.
        foreach (var banned in new[] { "perk", "coming soon", "upgrade", "plain browser" })
        {
            Assert.DoesNotContain(banned, rendered, StringComparison.OrdinalIgnoreCase);
        }

        // And it says what IS missing, per door.
        Assert.Contains("no outbound network connection", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("opens no capture device", rendered, StringComparison.OrdinalIgnoreCase);

        // ...AND the residual that keeps that sentence honest. A host that opens nothing is not a
        // build in which nothing can open: the voice screen is reachable and its recorder calls
        // getUserMedia. Asserting only the tightening let the rail claim "voice notes are not in
        // this build" while the page's recorder stayed live — the guard enforced the overstatement
        // rather than catching it (wave-65 land).
        //
        // A later wave re-pointed this needle. The deny hook made "the browser can still ask you" false on the
        // WINDOWS embedded path (the deny hook attaches at GoonHostWindow.axaml.cs:393), so the
        // rail now states a PLATFORM SPLIT — and a split introduces a failure the old single
        // unconditional needle could not have: POLARITY. Three unordered Contains over the joined
        // rail would pass on "ON LINUX the host denies; on Windows the browser can still ask",
        // which is the exact opposite of the truth (measured, not assumed). So the needles are
        // scoped to the VoiceNotes ROW, split at BOTH boundaries, and pinned per platform.
        //
        // WHAT THIS DETECTS, AND WHAT IT DOES NOT. It detects DRIFT: the code moves and the
        // sentence does not, which is the threat model this whole packet exists for and the one
        // its red-watch demonstrates. It does NOT detect a DELIBERATE REWRITE of the sentence into
        // a false one, and it cannot: these are token-presence and ordering needles, and token
        // presence cannot encode ATTRIBUTION — "answers it DENY" is a substring of "no code path
        // answers it DENY". An adversarial pass found further false-but-passing variants, and
        // chasing them with more needles is the matcher this port has already ruled out
        // (port-lessons.md wave 62: "the fix is never a cleverer matcher"). The bound here is
        // stated rather than papered over.
        var voiceRow = panel.Children.OfType<StackPanel>()
            .Single(row => row.Name == $"Refusal{GoonDoors.GoonDoor.VoiceNotes}");
        var voiceText = string.Join(" ", voiceRow.Children.OfType<TextBlock>().Select(t => t.Text ?? ""));

        // EXACTLY ONE of each platform marker and of the tail marker. Without this, a decoy earlier
        // in the row pre-empts IndexOf and every window below is measured over the wrong span.
        Assert.Equal(1, Occurrences(voiceText, "ON WINDOWS"));
        Assert.Equal(1, Occurrences(voiceText, "ON LINUX"));
        Assert.Equal(1, Occurrences(voiceText, "On neither"));

        var windowsAt = voiceText.IndexOf("ON WINDOWS", StringComparison.Ordinal);
        var linuxAt = voiceText.IndexOf("ON LINUX", StringComparison.Ordinal);
        var tailAt = voiceText.IndexOf("On neither", StringComparison.Ordinal);
        Assert.True(linuxAt > windowsAt, "the VoiceNotes refusal does not state Windows then Linux");
        Assert.True(tailAt > linuxAt, "the VoiceNotes refusal has no residual tail after its Linux arm");

        // BOTH arms are BOUNDED. The Linux clause used to run to end-of-string with a positive
        // needle only, which left every byte after "ON LINUX" — the whole OwnerGate block included —
        // unconstrained, so the same false claim a few words later was invisible.
        var windowsClause = voiceText[windowsAt..linuxAt];
        var linuxClause = voiceText[linuxAt..tailAt];
        var residualClause = voiceText[tailAt..];

        // Windows DENIES and is not the arm where the browser may ask; Linux is the reverse. The
        // two negatives are SYMMETRIC: each arm must not carry the other arm's verb.
        Assert.Contains("answers it DENY", windowsClause, StringComparison.Ordinal);
        Assert.DoesNotContain("can still ask you", windowsClause, StringComparison.Ordinal);
        Assert.Contains("the browser still owns the question and can still ask you", linuxClause,
            StringComparison.Ordinal);
        Assert.DoesNotContain("answers it DENY", linuxClause, StringComparison.Ordinal);

        // And no real prompt has ever been refused here, on either platform.
        Assert.Contains("nobody has made", residualClause, StringComparison.Ordinal);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task TheHostWindow_StartsOnNoSurfaceUntilItIsOpened() => WithHostWindowAsync(window =>
    {
        // Surface selection happens in Opened, which these tests deliberately never reach: an
        // embedded browser is not a thing a headless frame can present or should start.
        Assert.Equal("pending", window.Surface);
        Assert.False(window.BootMessagesSent);
        Assert.Equal(0, window.Heartbeats);
        return Task.CompletedTask;
    });
}
