using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views;
using CcpClient.Tests;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// SP-098 — the conditioning session and the Flash Images rack row, driven by REAL headless
/// input on the REAL controls, from a cold composition-root boot with NO command-line
/// arguments. The user story under test is the one the product did not have until this packet:
/// press START, an effect really runs; press STOP, it really stops.
///
/// <para>Only two seams are substituted, and both are declared by the spine itself: the session
/// CLOCK (so no test waits out an interval) and the image POOL (so no test depends on a
/// folder). The session, the engine, the effect, the preset store, the rack row, the dot, the
/// dials and the button are all the product's.</para>
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): visual tree, style-resolved
/// classes, real input routing. Nothing here claims composited pixels, and — said plainly
/// because it is the whole shape of this packet — <b>nothing here claims a flash was ever shown
/// on screen</b>. It cannot be: the on-screen half of Flash Images needs an always-on-top
/// click-through surface that this build does not have, and the module panel says so.</para>
/// </summary>
public class StudioRackHeadlessTests
{
    private sealed record Boot(ApplicationHost Host, MainWindow Window, ManualSessionClock Clock, StubPool Pool)
    {
        public SessionParticipant Session => Window.Session;

        public FlashImagesEffect Flash => Window.Session.Flash;

        /// <summary>An advance guaranteed to reach the next flash whatever the ±30 % draw was.</summary>
        public TimeSpan LongestInterval =>
            FlashSchedule.MaximumInterval(Session.Preset.Current.FlashesPerHour) + TimeSpan.FromSeconds(1);
    }

    private static async Task<Boot> BootAsync(int imageCount = 4)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp098-shell-" + Guid.NewGuid().ToString("N"));
        var clock = new ManualSessionClock();
        var pool = new StubPool(imageCount);
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            // The SAME instances on every ParticipantsFactory call (Validate probes once and
            // Build composes once), so the object this test advances is the object the built
            // host actually runs on.
            SessionClockFactory = () => clock,
            FlashImagePoolFactory = () => pool,
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);

        var window = new MainWindow(host!);
        // Phase 4: the real dispatch boundary, so the effect's UI projection takes the real
        // route rather than being read straight off the model.
        host!.BindUiDispatch(new AvaloniaUiDispatch());
        window.Show();
        window.UpdateLayout();
        return new Boot(host, window, clock, pool);
    }

    private static T Descendant<T>(MainWindow window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' in the mounted page");

    private static void Click(MainWindow window, Control control, MouseButton button = MouseButton.Left)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(center, button, RawInputModifiers.None);
        window.MouseUp(center, button, RawInputModifiers.None);
        window.UpdateLayout();
    }

    private static void OpenStudioAndFlashModule(MainWindow window)
    {
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowFlashImages"));
    }

    // =====================================================================================
    //  the user story
    // =====================================================================================

    [AvaloniaFact]
    public async Task ColdStart_PressingSTART_ReallyRunsTheEffect_AndPressingSTOP_ReallyStopsIt()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndFlashModule(window);

        var start = window.FindControl<Button>("SessionStartButton")!;
        Assert.Equal("START", start.Content);
        Assert.False(window.Session.Engine.Running);
        Assert.Equal(0, boot.Flash.FlashCount);

        Click(window, start);

        Assert.True(window.Session.Engine.Running);
        Assert.Equal("STOP", start.Content);
        Assert.True(boot.Flash.ScheduleArmed);

        // The clock, not a wait. Two flashes really come due, and the module panel — the ONE
        // surface that reports what this effect did — says so through the real dispatch
        // boundary.
        boot.Clock.Advance(boot.LongestInterval);
        boot.Clock.Advance(boot.LongestInterval);
        Assert.Equal(2, boot.Flash.FlashCount);
        Assert.Equal(2 * SessionPresetDocument.DefaultImagesPerFlash, boot.Pool.TotalDrawn);

        var live = Descendant<TextBlock>(window, "FlashLiveState");
        await TestWait.Until(
            () => live.Text is not null && live.Text.Contains("2 flashes", StringComparison.Ordinal),
            "the module panel to report the two flashes that came due",
            () => $"panel='{live.Text}', model count={boot.Flash.FlashCount}",
            cancellationToken: TestContext.Current.CancellationToken);

        Click(window, start);

        Assert.False(window.Session.Engine.Running);
        Assert.Equal("START", start.Content);
        Assert.False(boot.Flash.ScheduleArmed);

        // THE BITE. Ten more windows of clock after the user pressed STOP. If the stop leaked,
        // every one of them would produce a flash and a draw.
        var drawsAtStop = boot.Pool.TotalDrawn;
        for (var i = 0; i < 10; i++)
        {
            boot.Clock.Advance(boot.LongestInterval);
        }

        Assert.Equal(2, boot.Flash.FlashCount);
        Assert.Equal(drawsAtStop, boot.Pool.TotalDrawn);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheStartButtonIsOneControlInTwoStates_AndIsNeverDisabled()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        var start = window.FindControl<Button>("SessionStartButton")!;

        // WPF's BtnStart_Click is a single control that branches on _isRunning
        // (MainWindow/MainWindow.StartStop.cs:34,50,105) and UpdateStartButton repaints THAT
        // button rather than swapping in a second one (:751-796). A disabled stop is the exact
        // failure a panic button exists to prevent, so the enabled state is asserted in both.
        Assert.True(start.IsEnabled);
        Assert.DoesNotContain("running", start.Classes);

        Click(window, start);
        Assert.True(start.IsEnabled);
        Assert.Contains("running", start.Classes);
        Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "SessionStartButton");

        Click(window, start);
        Assert.True(start.IsEnabled);
        Assert.DoesNotContain("running", start.Classes);

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  D5 — the live dot, and it tells the truth
    // =====================================================================================

    [AvaloniaFact]
    public async Task TheRowsDot_ReportsWhatIsRunning_AndOffIsTheDialsWordNotTheSessions()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndFlashModule(window);
        var dot = Descendant<Avalonia.Controls.Shapes.Ellipse>(window, "FlashRowDot");
        var page = (CcpClient.Desktop.Views.Pages.StudioPage)window.PageFor(ShellRoutes.Studio);

        // Armed: the dial is on (WPF's FlashEnabled defaults TRUE, AppSettings.cs:751) but no
        // session owns it. WPF's dot would be LIT here, because it reads the flag — and WPF's
        // own onboarding card says the dot shows "everything that is currently running". The
        // port refuses to pick one of those and be wrong about the other (§14 D45).
        Assert.Equal(EffectDotState.Armed, page.RenderedFlashDot);
        Assert.Contains("armed", dot.Classes);
        Assert.DoesNotContain("live", dot.Classes);

        Click(window, window.FindControl<Button>("SessionStartButton")!);
        Assert.Equal(EffectDotState.Live, page.RenderedFlashDot);
        Assert.Contains("live", dot.Classes);

        // Off is the MODULE's word, not the session's: switched off, the row reads off even
        // while a session is running, because nothing will happen either way.
        Click(window, Descendant<RadioButton>(window, "RowFlashImages"), MouseButton.Right);
        Assert.True(window.Session.Engine.Running);
        Assert.Equal(EffectDotState.Off, page.RenderedFlashDot);
        Assert.DoesNotContain("armed", dot.Classes);
        Assert.DoesNotContain("live", dot.Classes);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheDotCannotClaimLiveAfterTheScheduleIsGone()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndFlashModule(window);
        var page = (CcpClient.Desktop.Views.Pages.StudioPage)window.PageFor(ShellRoutes.Studio);

        Click(window, window.FindControl<Button>("SessionStartButton")!);
        Assert.Equal(EffectDotState.Live, page.RenderedFlashDot);

        // Teardown, with the user never pressing STOP. The dot derives from the OPERATION
        // authority, so a cancelled generation reads not-live even though nobody repainted the
        // row — which is the difference between a dot and a cached bool.
        await boot.Host.ShutdownAsync();

        Assert.NotEqual(EffectDotState.Live, boot.Flash.Dot);
        Assert.False(boot.Flash.ScheduleArmed);
    }

    // =====================================================================================
    //  D6 — the right-click quick-toggle
    // =====================================================================================

    [AvaloniaFact]
    public async Task RightClickOnTheFlashRow_QuickTogglesTheEffect_AndOpensNoMenuAndSelectsNothing()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        var row = Descendant<RadioButton>(window, "RowFlashImages");

        Assert.True(window.Session.Preset.Current.FlashEnabled);
        Assert.False(row.IsChecked);

        // WPF's second rack gesture (StudioTabView.xaml.cs:660 -> :1109-1133 -> the `flash` case
        // at MainWindow/MainWindow.Presets.cs:1250). It flips the PERSISTED dial and it opens
        // nothing: a toggle that also selected the row would make the two gestures the same
        // gesture.
        Click(window, row, MouseButton.Right);

        Assert.False(window.Session.Preset.Current.FlashEnabled);
        Assert.False(row.IsChecked);
        Assert.False(Descendant<StackPanel>(window, "FlashModulePanel").IsVisible);
        Assert.Null(row.ContextMenu);
        Assert.Null(row.ContextFlyout);
        Assert.Empty(window.GetVisualDescendants().OfType<ContextMenu>());
        Assert.Equal(ShellRoutes.Studio, window.Router.Current.Id);

        Click(window, row, MouseButton.Right);
        Assert.True(window.Session.Preset.Current.FlashEnabled);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheQuickToggleAndThePanelCheckbox_AreTheSameOnePath()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndFlashModule(window);
        var check = Descendant<CheckBox>(window, "FlashEnableToggle");

        // WPF's panel Enable checkbox does the same body as the row's right-click — write the
        // flag, then start/stop the live service if the engine is running
        // (Features/FlashFeatureControl.xaml.cs:159-175 vs MainWindow.Presets.cs:1250). One
        // dispatch entry here, so the two can never drift into two behaviours.
        Assert.True(check.IsChecked);

        Click(window, Descendant<RadioButton>(window, "RowFlashImages"), MouseButton.Right);
        Assert.False(check.IsChecked);          // the row's gesture repainted the panel's dial

        Click(window, check);
        Assert.True(check.IsChecked);
        Assert.True(window.Session.Preset.Current.FlashEnabled);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task ToggledOffMidSession_TheWorkStops_AndToggledOnAgainItRestarts()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        var row = Descendant<RadioButton>(window, "RowFlashImages");

        Click(window, window.FindControl<Button>("SessionStartButton")!);
        boot.Clock.Advance(boot.LongestInterval);
        Assert.Equal(1, boot.Flash.FlashCount);

        Click(window, row, MouseButton.Right);          // off, mid-session
        for (var i = 0; i < 5; i++)
        {
            boot.Clock.Advance(boot.LongestInterval);
        }

        Assert.Equal(1, boot.Flash.FlashCount);

        Click(window, row, MouseButton.Right);          // back on, mid-session
        boot.Clock.Advance(boot.LongestInterval);

        // §14 D46: WPF's own path is dead here (FlashService.Start returns at
        // `if (_isRunning) return;`), so a module switched on mid-session never arms. The port
        // does what the rack's own onboarding text promises the gesture does.
        Assert.Equal(2, boot.Flash.FlashCount);

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  the module panel: real dials, and an honest account of what is missing
    // =====================================================================================

    [AvaloniaFact]
    public async Task LeftClickOpensTheModule_AndTheDialsAreTheRealPersistedOnes()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);

        Assert.True(Descendant<TextBlock>(window, "RackHint").IsVisible);
        Click(window, Descendant<RadioButton>(window, "RowFlashImages"));
        Assert.True(Descendant<StackPanel>(window, "FlashModulePanel").IsVisible);
        Assert.False(Descendant<StackPanel>(window, "SpiralModulePanel").IsVisible);
        Assert.False(Descendant<TextBlock>(window, "RackHint").IsVisible);

        var frequency = Descendant<Slider>(window, "FlashFrequencySlider");
        var images = Descendant<Slider>(window, "FlashImagesSlider");

        // The sliders carry WPF's own ranges (AppSettings.cs:769, :835) and open on the
        // persisted values, not on a hardcoded default.
        Assert.Equal(SessionPresetDocument.MinFlashesPerHour, frequency.Minimum);
        Assert.Equal(SessionPresetDocument.MaxFlashesPerHour, frequency.Maximum);
        Assert.Equal(SessionPresetDocument.MinImagesPerFlash, images.Minimum);
        Assert.Equal(SessionPresetDocument.MaxImagesPerFlash, images.Maximum);
        Assert.Equal(SessionPresetDocument.DefaultFlashesPerHour, frequency.Value);
        Assert.Equal(SessionPresetDocument.DefaultImagesPerFlash, images.Value);

        // Nothing on this panel is disabled: a greyed dial swallows the gesture and tells the
        // user nothing (§9 D7). The dials WPF has that the port does not are ABSENT.
        Assert.True(frequency.IsEnabled);
        Assert.True(images.IsEnabled);
        Assert.True(Descendant<CheckBox>(window, "FlashEnableToggle").IsEnabled);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task MovingADial_WritesThePreset_AndChangesWhatTheRunningEffectDoes()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndFlashModule(window);
        Click(window, window.FindControl<Button>("SessionStartButton")!);

        Descendant<Slider>(window, "FlashImagesSlider").Value = 12;

        Assert.Equal(12, window.Session.Preset.Current.ImagesPerFlash);
        Assert.Equal("12", Descendant<TextBlock>(window, "FlashImagesValue").Text);

        boot.Clock.Advance(boot.LongestInterval);

        // The dial is not decoration: the very next flash draws the new count. WPF reads
        // SimultaneousImages at the moment of the draw (FlashService.cs:586), not when the
        // flash was scheduled.
        Assert.Equal(12, boot.Flash.Last!.ImagesDrawn);
        Assert.Equal(12, boot.Pool.TotalDrawn);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheModulePanelNamesTheSurfaceAndTheEmptyPool_AndNeverClaimsAFlashWasShown()
    {
        var boot = await BootAsync(imageCount: 0);
        var window = boot.Window;
        OpenStudioAndFlashModule(window);

        // SP-101: this line used to say the drawing half was "not ported yet", which SP-100 made
        // false on Windows and left true on Linux. It now reads the presenter's own state, so it
        // still says something BEFORE anything is pressed — a user must not have to press START and
        // watch to find out how this effect reaches the screen — without asserting a platform.
        var surfaceLine = window.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.GetValue(Avalonia.Automation.AutomationProperties.AutomationIdProperty) == "FlashSurfaceState");
        Assert.Contains("always-on-top", surfaceLine.Text!, StringComparison.Ordinal);
        Assert.Contains("Nothing has been drawn yet", surfaceLine.Text!, StringComparison.Ordinal);
        Assert.DoesNotContain("not ported", surfaceLine.Text!, StringComparison.Ordinal);

        var poolLine = Descendant<TextBlock>(window, "FlashPoolState");
        // Nothing has looked in the folder yet, so nothing is claimed about it.
        Assert.True(string.IsNullOrEmpty(poolLine.Text));

        Click(window, window.FindControl<Button>("SessionStartButton")!);
        boot.Clock.Advance(boot.LongestInterval);

        Assert.Equal(1, boot.Flash.FlashCount);
        Assert.True(boot.Flash.Last!.PoolWasEmpty);

        await TestWait.Until(
            () => poolLine.Text is not null && poolLine.Text.Contains("no images to draw", StringComparison.Ordinal),
            "the module panel to report the empty pool after a flash came due",
            () => $"panel='{poolLine.Text}', model count={boot.Flash.FlashCount}",
            cancellationToken: TestContext.Current.CancellationToken);

        // ...and it points at the folder, which is what makes the most common first-run dead
        // end (FlashService.cs:589-597) fixable without a support thread.
        Assert.Contains(boot.Session.ImagesFolder, poolLine.Text!, StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheSpiralRow_StillHasNoDotAndNoToggle_AndThatIsTheHonestState()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);

        // §9 D5/D6 close for the Flash Images row and stay OPEN for this one. WPF's own rule
        // for a row it cannot wire honestly is to omit the dot (StudioTabView.xaml.cs:494-496)
        // and to leave the gesture unhandled (:659) — never to paint a dot that always reads
        // off, which is the fake-available shape the capability contract bans.
        var spiral = Descendant<RadioButton>(window, "RowSpiralOverlay");
        Assert.DoesNotContain(
            spiral.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>(),
            e => e.Classes.Contains("dot"));

        Click(window, spiral, MouseButton.Right);
        Assert.Null(spiral.ContextMenu);
        Assert.Empty(window.GetVisualDescendants().OfType<ContextMenu>());
        Assert.False(spiral.IsChecked);

        // The one row that DOES carry the grammar has both, so the rack is not uniformly
        // missing them — it is honest per row.
        Assert.Contains(
            Descendant<RadioButton>(window, "RowFlashImages").GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>(),
            e => e.Classes.Contains("dot"));

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  doubles: the two seams the spine declares, and nothing else
    // =====================================================================================

    private sealed class StubPool(int population) : IFlashImagePool
    {
        private readonly string[] _images =
            Enumerable.Range(0, population).Select(i => $"image-{i}.png").ToArray();

        public int TotalDrawn { get; private set; }

        public IReadOnlyList<string> Draw(int count)
        {
            if (_images.Length == 0)
            {
                return [];
            }

            var drawn = Enumerable.Range(0, count).Select(i => _images[i % _images.Length]).ToArray();
            TotalDrawn += drawn.Length;
            return drawn;
        }
    }

    /// <summary>Manual clock (the SP-043 <c>ManualClock</c> shape). Zero wall-clock.</summary>
    private sealed class ManualSessionClock : ISessionClock
    {
        private sealed class Entry
        {
            public DateTimeOffset Due;
            public required Action Fire;
            public bool Cancelled;
        }

        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            var entry = new Entry { Due = UtcNow + due, Fire = fire };
            lock (_timers)
            {
                _timers.Add(entry);
            }

            return new CancelHandle(this, entry);
        }

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers.Where(t => !t.Cancelled && t.Due <= UtcNow).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is null)
                    {
                        return;
                    }

                    _timers.Remove(next);
                }

                next.Fire();
            }
        }

        private sealed class CancelHandle(ManualSessionClock clock, Entry entry) : IDisposable
        {
            public void Dispose()
            {
                lock (clock._timers)
                {
                    entry.Cancelled = true;
                    clock._timers.Remove(entry);
                }
            }
        }
    }
}
