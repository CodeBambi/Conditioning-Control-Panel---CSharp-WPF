using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The Pop Quiz rack row, driven by REAL headless input on the REAL controls from a cold
/// composition-root boot with no command-line arguments.
///
/// <para><b>What these facts exist for is REACHABILITY, and nothing else.</b> The module itself
/// landed complete and proved — twenty-two facts in <c>CcpClient.Tests.PopQuizModuleTests</c> over
/// its schedule, its shuffle, its two card delays, its guards and its XP — and none of them could
/// tell that the shipped application constructed the thing at all. It did not: no rack entry, no
/// row, no panel, no way for a user to reach a question. So every fact here drives the SURFACE and
/// then reads the REAL composed module behind it, never a double.</para>
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): visual tree, real input routing,
/// the composed session's own state. Nothing here claims composited pixels, that a card was ever
/// really put on a display, or that anybody could read one.</para>
/// </summary>
public class PopQuizRowHeadlessTests
{
    private sealed record Boot(ApplicationHost Host, MainWindow Window)
    {
        public StudioPage Studio => (StudioPage)Window.PageFor(ShellRoutes.Studio);

        public PopQuizEffect Module => Window.Session.PopQuiz;
    }

    private static async Task<Boot> BootAsync(ManualScriptedClock? scriptedClock = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-popquiz-row-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            ScriptedClockFactory = scriptedClock is null ? null : () => scriptedClock,
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);

        var window = new MainWindow(host!);
        host!.BindUiDispatch(new AvaloniaUiDispatch());
        window.Show();
        window.UpdateLayout();
        return new Boot(host, window);
    }

    private static T Descendant<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' in the mounted page");

    private static void Click(Window window, Control control, MouseButton button = MouseButton.Left)
    {
        control.BringIntoView();
        window.UpdateLayout();
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(center, button, RawInputModifiers.None);
        window.MouseUp(center, button, RawInputModifiers.None);
        window.UpdateLayout();
    }

    private static void OpenThePopQuizRow(MainWindow window)
    {
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowPopQuiz"));
    }

    /// <summary>The four gestures a real scripted-session start takes; nothing here reaches past the
    /// surface.</summary>
    private static void StartMorningDrift(MainWindow window)
    {
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowScriptedSession"));
        Click(window, Descendant<RadioButton>(window, "SessionRowMorningDrift"));
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));
        Assert.True(window.Session.Scripted.Running);
    }

    // =====================================================================================
    //  the row and its panel
    // =====================================================================================

    /// <summary>
    /// The row opens a panel, and the panel carries UPSTREAM'S TWO DIALS AND NO MORE. Upstream's
    /// whole settings region for this module is <c>ChkPopQuizEnabled</c> and
    /// <c>SliderPopQuizFrequency</c> (<c>Views/Tabs/GradedIntakeTabView.xaml:268-292</c>): no strict
    /// mode, no colour, no repeat count, no phrase list. Asserted as a SET so a third dial cannot
    /// arrive without this fact noticing.
    /// </summary>
    [AvaloniaFact]
    public async Task ThePopQuizRowOpensAPanelCarryingUpstreamsTwoDialsAndNoMore()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenThePopQuizRow(window);

        var panel = Descendant<StackPanel>(window, "PopQuizModulePanel");
        Assert.True(panel.IsVisible);
        // The rack shows ONE panel: a row that opened its own and left a neighbour's up would put
        // two modules' dials in front of the user at once.
        Assert.False(Descendant<StackPanel>(window, "LockCardModulePanel").IsVisible);
        Assert.False(Descendant<StackPanel>(window, "BubbleCountModulePanel").IsVisible);

        Assert.Equal(
            ["PopQuizEnableToggle"],
            panel.GetLogicalDescendants().OfType<CheckBox>().Select(c => c.Name ?? "(unnamed)"));
        Assert.Equal(
            ["PopQuizFrequencySlider"],
            panel.GetLogicalDescendants().OfType<Slider>().Select(c => c.Name ?? "(unnamed)"));

        // The slider's travel is upstream's own, 1..100 (GradedIntakeTabView.xaml:286), which is also
        // AppSettings' clamp (Models/AppSettings.cs:3586). Pinned as LITERALS rather than through
        // PopQuizSchedule's constants, so a dial that stopped agreeing with the arithmetic behind it
        // reds here instead of agreeing with itself.
        var slider = Descendant<Slider>(window, "PopQuizFrequencySlider");
        Assert.Equal(1, slider.Minimum);
        Assert.Equal(100, slider.Maximum);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// The enable box really arms the REAL composed module and really writes the REAL document. This
    /// is the fact the packet exists for: before it, <c>PopQuizEffect</c> was constructed by nothing
    /// at all and the dial did not exist.
    /// </summary>
    [AvaloniaFact]
    public async Task TickingTheEnableBoxArmsTheRealModuleAndWritesItsOwnDocument()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenThePopQuizRow(window);

        // Ships OFF, which is upstream's default (Models/AppSettings.cs:3575,
        // _popQuizEnabled = false).
        Assert.False(boot.Module.Enabled);
        Assert.False(window.Session.PopQuizPreset.Current.Enabled);
        Assert.Equal(EffectDotState.Off, boot.Studio.RenderedPopQuizDot);

        Click(window, Descendant<CheckBox>(window, "PopQuizEnableToggle"));

        Assert.True(boot.Module.Enabled);
        Assert.True(window.Session.PopQuizPreset.Current.Enabled);
        // Armed, not Live: the dial is on and no session owns it yet (EffectDotState.Armed).
        Assert.Equal(EffectDotState.Armed, boot.Studio.RenderedPopQuizDot);

        Click(window, Descendant<CheckBox>(window, "PopQuizEnableToggle"));
        Assert.False(boot.Module.Enabled);
        Assert.Equal(EffectDotState.Off, boot.Studio.RenderedPopQuizDot);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// The frequency slider writes the dial through the module — so the live schedule is re-paced —
    /// rather than writing the document behind the module's back. Upstream recomputes its interval
    /// from the CURRENT setting on every tick (<c>Services/Quiz/PopQuizService.cs:163-171</c>), so a
    /// raised rate is meant to take effect at the next question.
    /// </summary>
    [AvaloniaFact]
    public async Task TheFrequencySliderWritesTheDialAndTheValueLabelIsUpstreamsOwnUnits()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenThePopQuizRow(window);

        // Upstream's default (Models/AppSettings.cs:3582, _popQuizFrequency = 2), pinned as a
        // literal so a changed default cannot pass by agreeing with itself.
        Assert.Equal(2, window.Session.PopQuizPreset.Current.PerHour);
        Assert.Equal("2/session hr", Descendant<TextBlock>(window, "PopQuizFrequencyValue").Text);

        Descendant<Slider>(window, "PopQuizFrequencySlider").Value = 37;
        window.UpdateLayout();

        Assert.Equal(37, window.Session.PopQuizPreset.Current.PerHour);
        Assert.Equal(37, boot.Module.Preset.PerHour);
        // Upstream's own label, verbatim: $"{val}/session hr" (MainWindow/MainWindow.Lab.cs:643).
        Assert.Equal("37/session hr", Descendant<TextBlock>(window, "PopQuizFrequencyValue").Text);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// The rack's second gesture reaches this row too — right-click flips the module without opening
    /// its panel (<c>Views/Tabs/StudioTabView.xaml.cs:660</c> → <c>:1109-1133</c>). A row that took
    /// the click but not the flip would look identical to one that worked.
    /// </summary>
    [AvaloniaFact]
    public async Task TheRowsRightClickFlipsTheModuleWithoutOpeningThePanel()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);

        var row = Descendant<RadioButton>(window, "RowPopQuiz");
        Assert.False(boot.Module.Enabled);

        Click(window, row, MouseButton.Right);

        Assert.True(boot.Module.Enabled);
        Assert.True(Descendant<CheckBox>(window, "PopQuizEnableToggle").IsChecked);
        // The gesture is a toggle, not a selection: the row did not open.
        Assert.False(row.IsChecked);
        Assert.False(Descendant<StackPanel>(window, "PopQuizModulePanel").IsVisible);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// <b>PRESSING START ARMS THIS MODULE, which is the whole point of the rack entry and the one
    /// thing twenty-two passing module facts could not tell.</b> The module is on
    /// <see cref="SessionEngine"/>'s array between Brain Drain and the ramp — upstream's
    /// <c>StartEngine</c> order (<c>MainWindow/MainWindow.StartStop.cs:255-258</c>, after Brain
    /// Drain at <c>:241-244</c> and before the ramp timer at <c>:265-269</c>) — and this drives the
    /// shell's real START button to prove it.
    ///
    /// <para><b>The assertion is <c>ScheduleArmed</c> and not the dot</b>, deliberately: the dot
    /// additionally requires the operating system to say this process can put a window in front of
    /// somebody (<see cref="PopQuizEffect.WorkIsRunning"/>), which is a real desktop's answer and
    /// differs between the two platforms this floor runs on. What is being proved here is that the
    /// ENGINE armed the module, and that is the same on both.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task PressingSTARTArmsThisModuleToo_AndSTOPDisarmsIt()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenThePopQuizRow(window);
        Click(window, Descendant<CheckBox>(window, "PopQuizEnableToggle"));

        Assert.False(window.Session.Engine.Running);
        Assert.False(boot.Module.ScheduleArmed);

        Click(window, Descendant<Button>(window, "SessionStartButton"));

        Assert.True(window.Session.Engine.Running);
        Assert.True(boot.Module.ScheduleArmed);
        Assert.Equal(0, boot.Module.QuizCount);

        Click(window, Descendant<Button>(window, "SessionStartButton"));

        Assert.False(window.Session.Engine.Running);
        Assert.False(boot.Module.ScheduleArmed);

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  what the panel says
    // =====================================================================================

    /// <summary>
    /// The panel tells the user what it will do BEFORE they tick anything: that it takes the
    /// keyboard, that every answer is correct, what is missing from the port, and that there is no
    /// Test button. Each is the notices file's own constant, so a "tidy" that softened the wording
    /// reds here rather than shipping.
    /// </summary>
    [AvaloniaFact]
    public async Task ThePanelSaysWhatItWillDoBeforeAnythingIsTicked()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenThePopQuizRow(window);

        Assert.Equal(
            PopQuizPanelNotices.InterruptionNotice,
            Descendant<TextBlock>(window, "PopQuizInterruptionNotice").Text);
        Assert.Equal(
            PopQuizPanelNotices.ScopeNotice,
            Descendant<TextBlock>(window, "PopQuizScopeNotice").Text);
        Assert.Equal(
            PopQuizPanelNotices.NoTestButtonNotice,
            Descendant<TextBlock>(window, "PopQuizTestNotice").Text);

        // The two sentences a user actually needs before agreeing to this module, read off the
        // rendered surface rather than off the constant: the interruption, and that nothing they can
        // press is wrong.
        var interruption = Descendant<TextBlock>(window, "PopQuizInterruptionNotice").Text ?? string.Empty;
        Assert.Contains("takes the keyboard", interruption, StringComparison.Ordinal);
        Assert.Contains("EVERY ANSWER IS CORRECT", interruption, StringComparison.Ordinal);

        // Off, and the live line says the whole of what that means.
        Assert.Equal(
            "Switched off. No question will come up, session or no session.",
            Descendant<TextBlock>(window, "PopQuizLiveState").Text);

        // The pool line reports upstream's built-in twenty-five with four answers each
        // (Services/Quiz/PopQuizService.cs:23-100). Literals, not the module's own constants.
        var pool = Descendant<TextBlock>(window, "PopQuizPoolState").Text ?? string.Empty;
        Assert.StartsWith("25 built-in questions, 4 answers each", pool, StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// <b>The twenty-five XP does not bank in this build, and the panel says so rather than hiding
    /// it.</b> Upstream pays it on every answer (<c>Windows/PopQuizWindow.xaml.cs:161</c>). The
    /// module will bank the moment it is handed a ledger; the composition hands it none, because the
    /// port's ledger is opened per modal window and a session-lifetime second writer over
    /// <c>progression.json</c> would write a stale record over a DTRH or intake grant
    /// (<c>Session/SessionParticipant.cs</c>, at the construction). This fact pins the honest state
    /// AND the sentence the user reads, so quietly handing over a ledger without dealing with that
    /// reds here.
    /// </summary>
    [AvaloniaFact]
    public async Task TheXpLineSaysTheTwentyFiveDoesNotBankHere()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenThePopQuizRow(window);

        Assert.False(boot.Module.BanksXp);
        Assert.Null(boot.Module.LastGrant);
        Assert.Equal(
            PopQuizPanelNotices.DescribeXp(banksXp: false, lastGrant: null),
            Descendant<TextBlock>(window, "PopQuizXpState").Text);
        Assert.Contains(
            "The shipping app pays 25 XP for answering",
            Descendant<TextBlock>(window, "PopQuizXpState").Text ?? string.Empty,
            StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  the session feature lock, and why these two dials are outside it
    // =====================================================================================

    /// <summary>
    /// <b>THE LOCK-RULE RESOLUTION, AS A FACT.</b> Upstream marks BOTH pop quiz dials
    /// <c>SessionLock.Owned</c> (<c>Views/Tabs/GradedIntakeTabView.xaml:269,286</c>) and this port
    /// deliberately marks neither, because <c>Owned</c> is about CUSTODY rather than prescription:
    /// upstream's run snapshots the two values (<c>Services/Session/SessionEngine.cs:919-920</c>) and
    /// writes them back at the end (<c>:1544-1545</c>), so a mid-session edit there is silently
    /// discarded. This port's run borrows eleven documents and <c>session_popquiz.json</c> is not one
    /// of them, so the value stays the user's.
    ///
    /// <para><b>The inversion is inside the same session</b>: a dial that IS borrowed
    /// (<c>FlashEnableToggle</c>) goes dead in the same breath, so this cannot pass by the lock being
    /// broken. And the assertion is not merely "still enabled" — the slider is MOVED mid-session and
    /// the real document is read back, which is the claim that matters: the value is still
    /// theirs.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task NeitherPopQuizDialIsLockedByARunningSession_AndBothStillWriteWhileOneRuns()
    {
        var boot = await BootAsync(new ManualScriptedClock());
        var window = boot.Window;

        var enable = Descendant<CheckBox>(window, "PopQuizEnableToggle");
        var slider = Descendant<Slider>(window, "PopQuizFrequencySlider");
        Assert.DoesNotContain("session-owned", enable.Classes);
        Assert.DoesNotContain("session-owned", slider.Classes);

        StartMorningDrift(window);

        // The borrowed dial is dead …
        Assert.False(Descendant<CheckBox>(window, "FlashEnableToggle").IsEnabled);
        // … and these two are not.
        Assert.True(enable.IsEnabled);
        Assert.True(slider.IsEnabled);

        // And they still WRITE. This is the half that says the value is the user's rather than the
        // session's: a locked dial that merely looked live would fail here.
        OpenThePopQuizRow(window);
        Click(window, Descendant<CheckBox>(window, "PopQuizEnableToggle"));
        Descendant<Slider>(window, "PopQuizFrequencySlider").Value = 61;
        window.UpdateLayout();

        Assert.True(window.Session.PopQuizPreset.Current.Enabled);
        Assert.Equal(61, window.Session.PopQuizPreset.Current.PerHour);

        // The session ends and gives back what it borrowed. It never held these, so they are
        // untouched by the restore — the whole reason the dials are not greyed.
        window.Session.Scripted.Stop();
        window.UpdateLayout();
        Assert.False(window.Session.Scripted.Running);
        Assert.True(window.Session.PopQuizPreset.Current.Enabled);
        Assert.Equal(61, window.Session.PopQuizPreset.Current.PerHour);

        await boot.Host.ShutdownAsync();
    }
}
