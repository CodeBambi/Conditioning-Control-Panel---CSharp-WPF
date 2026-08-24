using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using CcpClient.Desktop.Features.Mantra;
using CcpClient.Desktop.Features.Progression;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The typed mantra minigame's window, driven by REAL headless keyboard input through real AXAML.
///
/// <para><b>Draw-level ONLY</b> (verification-harness.md evidence class): visual tree, real input
/// routing, the brushes the window actually assigned. <b>Nothing here claims a composited pixel, a
/// legible layout, a colour a human saw, window activation, focus against a real window manager, or
/// anything at all on Linux.</b> Those need a headed run. The window HAS a door now — the Play
/// page's Mantras card, <see cref="MantraLaunch"/>'s one caller — so the capture harness has
/// something to drive, and <c>client/tools/verify/checks.json</c>'s <c>mantra-window</c> surface is
/// where that evidence lives. It is a WINDOWS capture; the Linux leg of this window is unrun.</para>
///
/// <para><b>What the input really is.</b> <c>KeyPress</c> with a text payload is the headless
/// platform's own key-to-text delivery: it raises the same <c>KeyDown</c> and <c>TextInput</c> the
/// Win32 and X11 backends raise, routed by the same input manager to the same focused element. So
/// these facts do prove the window's handlers are wired and reachable from a keystroke; they do not
/// prove a physical keyboard reaches them.</para>
/// </summary>
public class MantraWindowHeadlessTests : HeadlessTest
{
    /// <summary>A pool of one, so the mantra on screen is known and the facts below are about the
    /// window rather than about a draw.</summary>
    private const string Mantra = "obey";

    /// <summary>Stands in for the words a user wrote into their own pool. Deliberately unlike
    /// anything the product's own diagnostics say.</summary>
    private const string Secret = "zubrowka lantern quietly folds";

    private sealed class CapturingSink : ILogSink
    {
        public List<string> Lines { get; } = [];

        public void Log(string message)
        {
            lock (Lines)
            {
                Lines.Add(message);
            }
        }
    }

    private sealed class Clock
    {
        private DateTimeOffset _now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset Now() => _now;

        public void Advance(double seconds) => _now = _now.AddSeconds(seconds);
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-mantra-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private ApplicationHost NewHost(CapturingSink sink)
    {
        var host = new ApplicationHost(sink, [], new StartupTrace());
        Track(host);
        return host;
    }

    private static MantraWindow Show(MantraSession session)
    {
        var window = new MantraWindow(session);
        window.Show();
        window.UpdateLayout();
        return window;
    }

    /// <summary>
    /// Type one character the way a platform really delivers it: a key DOWN, then the TEXT the
    /// platform's own key-to-text translation produced, then the key UP.
    ///
    /// <para>The two are separate calls because they are separate raw events —
    /// <c>KeyPress</c>'s trailing string is the key SYMBOL on the key event and does not carry text
    /// into the input pipeline (measured: driving with <c>KeyPress</c> alone left the box empty
    /// while the same window's <c>KeyDown</c> handler fired for Escape). <c>KeyTextInput</c> is the
    /// one that raises <c>TextInput</c>, which is what a window with no edit control listens
    /// to.</para>
    /// </summary>
    private static void TypeChar(Window window, char c)
    {
        var (key, physical) = c == ' '
            ? (Key.Space, PhysicalKey.Space)
            : (Enum.Parse<Key>(char.ToUpperInvariant(c).ToString()),
               Enum.Parse<PhysicalKey>(char.ToUpperInvariant(c).ToString()));

        window.KeyPress(key, RawInputModifiers.None, physical, c.ToString());
        window.KeyTextInput(c.ToString());
        window.KeyRelease(key, RawInputModifiers.None, physical, c.ToString());
        window.UpdateLayout();
    }

    private static void Type(Window window, string text)
    {
        foreach (var c in text)
        {
            TypeChar(window, c);
        }
    }

    private static IReadOnlyList<Run> Runs(MantraWindow window) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Name == "MantraText")
            .Inlines!.OfType<Run>()];

    private static TextBlock Text(MantraWindow window, string name) =>
        window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == name);

    private static Color ColourOf(Run run) => ((ISolidColorBrush)run.Foreground!).Color;

    // ==================================================================================

    /// <summary>
    /// <b>The mantra is on the screen one Run per character, and typing lights the prefix.</b>
    /// Upstream's <c>BuildMantraRuns</c> plus its colouring loop
    /// (<c>Windows/MantraWindow.xaml.cs:95-111</c>, <c>:133-144</c>), reaching a real visual tree
    /// through real keystrokes.
    ///
    /// <para>Reds on the window painting the mantra as one flat <c>TextBlock.Text</c> (the game
    /// stops giving per-character feedback), on the prefix not lighting, and on the un-typed tail
    /// lighting with it.</para>
    /// </summary>
    [AvaloniaFact]
    public void TheWindowPaintsTheMantraOneRunPerCharacter_AndTypingLightsThePrefix()
    {
        var clock = new Clock();
        var window = Show(new MantraSession(3, [Mantra], clock: clock.Now));

        var runs = Runs(window);
        Assert.Equal(4, runs.Count);
        Assert.Equal(Mantra, string.Concat(runs.Select(r => r.Text)));
        Assert.All(runs, r => Assert.Equal(Color.FromRgb(0x35, 0x35, 0x50), ColourOf(r)));

        Type(window, "ob");

        runs = Runs(window);
        var cold = Color.FromRgb(0x99, 0x88, 0xDD);      // streak 0 (:28, :316)
        Assert.Equal(cold, ColourOf(runs[0]));
        Assert.Equal(cold, ColourOf(runs[1]));
        Assert.Equal(Color.FromRgb(0x35, 0x35, 0x50), ColourOf(runs[2]));
        Assert.Equal(Color.FromRgb(0x35, 0x35, 0x50), ColourOf(runs[3]));

        // The box echoes what has been typed.
        Assert.Equal("ob", Text(window, "AnswerText").Text);
    }

    /// <summary>
    /// <b>A wrong character paints exactly one character red and puts the rest back to dim</b>
    /// (<c>Windows/MantraWindow.xaml.cs:133-144</c>). Reds on the whole tail going red, and on a
    /// mistake leaving the already-matched prefix lit past the error.
    /// </summary>
    [AvaloniaFact]
    public void AWrongCharacterPaintsExactlyOneCharacterRed()
    {
        var clock = new Clock();
        var window = Show(new MantraSession(3, [Mantra], clock: clock.Now));

        Type(window, "ox");

        var runs = Runs(window);
        Assert.Equal(Color.FromRgb(0x99, 0x88, 0xDD), ColourOf(runs[0]));
        Assert.Equal(Color.FromRgb(0xFF, 0x44, 0x44), ColourOf(runs[1]));      // the one error
        Assert.Equal(Color.FromRgb(0x35, 0x35, 0x50), ColourOf(runs[2]));
        Assert.Equal(Color.FromRgb(0x35, 0x35, 0x50), ColourOf(runs[3]));
        Assert.Single(runs, r => ColourOf(r) == Color.FromRgb(0xFF, 0x44, 0x44));

        // Backspace takes it back off — the one editing key the box has.
        window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, string.Empty);
        window.UpdateLayout();
        Assert.Equal("o", Text(window, "AnswerText").Text);
        Assert.DoesNotContain(Runs(window), r => ColourOf(r) == Color.FromRgb(0xFF, 0x44, 0x44));
    }

    /// <summary>
    /// <b>The counters and the streak's warm-up reach the screen.</b> Three repetitions taken
    /// through the window's own keystroke path move REPS and STREAK and BEST, and the highlight
    /// colour the window paints is the ramp's, not the cold constant
    /// (<c>Windows/MantraWindow.xaml.cs:251</c>, <c>:265-266</c>, <c>:310-316</c>).
    ///
    /// <para>Reds on the window painting a fixed highlight colour (the whole "it gets hotter"
    /// payoff), on a counter not repainting, and on the streak ramp being read from the wrong
    /// number.</para>
    /// </summary>
    [AvaloniaFact]
    public void TheCountersAndTheStreakWarmUpReachTheScreen()
    {
        var clock = new Clock();
        var window = Show(new MantraSession(10, [Mantra], clock: clock.Now));

        Assert.Equal("0", Text(window, "CompletionsText").Text);
        Assert.Equal("/10", Text(window, "TargetText").Text);
        Assert.Equal("0", Text(window, "StreakText").Text);
        Assert.Equal("0", Text(window, "BestStreakText").Text);

        for (var i = 0; i < 3; i++)
        {
            clock.Advance(2);
            Type(window, Mantra);
        }

        Assert.Equal("3", Text(window, "CompletionsText").Text);
        Assert.Equal("3", Text(window, "StreakText").Text);
        Assert.Equal("3", Text(window, "BestStreakText").Text);

        // The box was cleared by the completion, and the mantra is dim again.
        Assert.Equal(string.Empty, Text(window, "AnswerText").Text);

        // t = 3/15 = 0.2. Highlight = lerp(#9988DD, #FF69B4, 0.2), truncated: R 153+102*0.2 =
        // 173.4 -> 173, G 136-31*0.2 = 129.8 -> 129, B 221-41*0.2 = 212.8 -> 212.
        Type(window, "o");
        Assert.Equal(Color.FromRgb(173, 129, 212), ColourOf(Runs(window)[0]));
    }

    /// <summary>
    /// <b>The end of the run raises the completion overlay with upstream's own line</b>
    /// (<c>Windows/MantraWindow.xaml.cs:286-295</c>), and from then on any key closes the window
    /// (<c>:449-453</c>).
    ///
    /// <para>Reds on the overlay staying hidden, on the counts being wrong in it, and on the window
    /// continuing to take repetitions after the run is over.</para>
    /// </summary>
    [AvaloniaFact]
    public void AFinishedRunRaisesTheCompletionOverlay_AndThenAnyKeyCloses()
    {
        var clock = new Clock();
        var window = Show(new MantraSession(2, [Mantra], clock: clock.Now));

        var overlay = window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "CompletionOverlay");
        Assert.False(overlay.IsVisible);

        clock.Advance(2);
        Type(window, Mantra);
        Assert.False(overlay.IsVisible);

        clock.Advance(2);
        Type(window, Mantra);

        Assert.True(overlay.IsVisible);
        Assert.Equal("2 repetitions  |  Best streak: 2", Text(window, "CompletionStatsText").Text);

        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Assert.True(closed);
    }

    /// <summary>
    /// <b>Escape leaves, and leaving ends the run</b> (<c>Windows/MantraWindow.xaml.cs:442-447</c>,
    /// <c>:456-471</c>). Reds on Escape being swallowed — which would be a chromeless, topmost,
    /// maximized window with no way out — and on the run being left live behind a closed window.
    /// </summary>
    [AvaloniaFact]
    public void EscapeLeaves_AndLeavingEndsTheRun()
    {
        var clock = new Clock();
        var session = new MantraSession(5, [Mantra], clock: clock.Now);
        var window = Show(session);

        Assert.True(session.IsActive);

        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");

        Assert.True(closed);
        Assert.False(session.IsActive);
        Assert.Null(session.CurrentMantra);         // Services/MantraService.cs:118
    }

    /// <summary>
    /// <b>A second launch focuses the live window instead of restarting the run</b> — upstream's
    /// own guard and its own reason: "a second <c>StartSession</c> would reset Completions and
    /// Streak mid-run, i.e. silently delete the user's progress"
    /// (<c>MainWindow/MainWindow.PlayTab.cs:294-303</c>).
    ///
    /// <para>Reds on the second press building a second window, and on it building a second session
    /// over the same one — either way the banked repetitions below go back to zero.</para>
    /// </summary>
    [AvaloniaFact]
    public void ASecondLaunchFocusesTheLiveWindow_RatherThanRestartingTheRun()
    {
        var clock = new Clock();
        var sink = new CapturingSink();
        var host = NewHost(sink);
        var owner = new Window();

        var opened = new List<MantraWindow>();
        var launch = new MantraLaunch(host, owner)
        {
            DataDirectory = NewDir(),
            Pool = [Mantra],
            Clock = clock.Now,
            Show = (window, _) => { opened.Add(window); window.Show(); window.UpdateLayout(); },
        };

        var first = launch.Open(5);
        Assert.NotNull(first);
        Assert.True(launch.IsOpen);

        clock.Advance(2);
        Type(first!, Mantra);
        Assert.Equal(1, first!.Session.Completions);

        var second = launch.Open(5);
        Assert.Same(first, second);
        Assert.Single(opened);
        Assert.Equal(2, launch.LaunchCount);
        Assert.Equal(1, first.Session.Completions);      // the run survived the second press

        first.Close();
        Assert.False(launch.IsOpen);
    }

    /// <summary>
    /// <b>The launch banks into the SHARED progression file, and never writes a mantra anywhere.</b>
    /// Two things at once, because they are the same run:
    ///
    /// <para>The XP decision — <c>ProgressionLedger.Open</c> per WINDOW, disposed with it, over the
    /// install's own <c>progression.json</c> (<c>:180-185</c>). This drives a real window with real
    /// keystrokes and then reads the FILE, so what is proved is that a user who plays this game has
    /// a level afterwards, not that a method was called.</para>
    ///
    /// <para>The privacy rule — the pool is text the user wrote
    /// (<c>Models/AppSettings.cs:6325</c>), and the rule the media modules already hold
    /// (<c>Effects/MandatoryVideoEffect.cs:9-10</c>) applies to it. Every diagnostic the host
    /// received during a full run is checked against the phrase. The mutation this catches is one
    /// argument wide: the launch's own two lines carry counts, and either could as easily have
    /// carried the mantra.</para>
    ///
    /// <para>Reds on the ledger not being opened, on it not being disposed with the window (the
    /// file would still hold the pre-run value at the read below), and on any diagnostic carrying
    /// the phrase.</para>
    /// </summary>
    [AvaloniaFact]
    public void TheLaunchBanksIntoTheSharedLedger_AndNeverLogsAMantra()
    {
        var clock = new Clock();
        var sink = new CapturingSink();
        var host = NewHost(sink);
        var owner = new Window();
        var dir = NewDir();

        var launch = new MantraLaunch(host, owner)
        {
            DataDirectory = dir,
            Pool = [Secret],
            Clock = clock.Now,
            Show = (window, _) => { window.Show(); window.UpdateLayout(); },
        };

        var window = launch.Open(3);
        Assert.NotNull(window);

        for (var i = 0; i < 3; i++)
        {
            clock.Advance(2);
            Type(window!, Secret);
        }

        Assert.Equal(3, window!.Session.Completions);
        Assert.True(window.Session.LastGrant!.Banked);

        // Closing disposes the ledger, which flushes it. The file is then the evidence.
        window.Close();
        Assert.False(launch.IsOpen);

        var path = Path.Combine(dir, ProgressionDocument.FileName);
        Assert.True(File.Exists(path), $"the ledger never reached {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(120d, doc.RootElement.GetProperty("xp").GetDouble());     // 35 + 40 + 45

        // Not one line of what the host was told carries the user's words.
        Assert.NotEmpty(sink.Lines);
        var captured = string.Join("\n", sink.Lines);
        Assert.DoesNotContain(Secret, captured, StringComparison.OrdinalIgnoreCase);
        foreach (var word in Secret.Split(' '))
        {
            Assert.DoesNotContain(word, captured, StringComparison.OrdinalIgnoreCase);
        }

        // And it really did say something about the run — counts only.
        Assert.Contains(sink.Lines, l => l.Contains("mantra: run started", StringComparison.Ordinal));
        Assert.Contains(sink.Lines, l => l.Contains("3/3 repetitions", StringComparison.Ordinal));
    }
}
