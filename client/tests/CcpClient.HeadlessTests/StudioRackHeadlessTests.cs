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
using CcpClient.Desktop.Views.Pages;
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
        // SP-112: bring it into view FIRST. The rack scrolls now (ten rows are taller than the
        // window), and a row scrolled out of the viewport is clipped and not hit-testable — a real
        // click at its own centre selects nothing, which is what a user with a mouse wheel does
        // before clicking. Measured rather than assumed: the ramp row's own centre stopped
        // selecting it the moment a tenth row landed.
        control.BringIntoView();
        window.UpdateLayout();
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
    public async Task TheSpiralRow_NowCarriesTheGrammarToo_AndD5D6CloseForTheWholeRack()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);

        // THIS FACT USED TO ASSERT THE OPPOSITE, and the assertion was right at the time: WPF's
        // rule for a row it cannot wire honestly is to omit the dot (StudioTabView.xaml.cs:494-496)
        // and leave the gesture unhandled (:659), and until SP-106 there was no spiral effect to
        // report or to flip. There is now, so §9 D5/D6 CLOSE for the last row that had them open.
        // Nothing was weakened to get here: the rule is unchanged and the row simply stopped
        // qualifying for it.
        var spiral = Descendant<RadioButton>(window, "RowSpiralOverlay");
        Assert.Contains(
            spiral.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>(),
            e => e.Classes.Contains("dot"));

        // Every rack row, without exception — which is the claim that was not available before, and
        // which SP-108's fifth row (from a different rack GROUP) had to keep true rather than dent.
        foreach (var name in new[]
        {
            "RowFlashImages", "RowMandatoryVideo", "RowSubliminals", "RowSpiralOverlay", "RowPinkFilter",
            "RowBubblePop", "RowBubbleCount", "RowLockCard", "RowMindWipe", "RowBrainDrain",
            "RowIntensityRamp",
        })
        {
            Assert.Contains(
                Descendant<RadioButton>(window, name).GetVisualDescendants()
                    .OfType<Avalonia.Controls.Shapes.Ellipse>(),
                e => e.Classes.Contains("dot"));
        }

        // And the gesture still opens nothing and selects nothing — the half of the old fact that
        // was never about the row being unported.
        Click(window, spiral, MouseButton.Right);
        Assert.Null(spiral.ContextMenu);
        Assert.Empty(window.GetVisualDescendants().OfType<ContextMenu>());
        Assert.False(spiral.IsChecked);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task RightClickOnTheSpiralRow_ReallyFlipsTheMovingModule_AndTheDotFollowsBothWays()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        var row = Descendant<RadioButton>(window, "RowSpiralOverlay");
        var page = (CcpClient.Desktop.Views.Pages.StudioPage)window.PageFor(ShellRoutes.Studio);

        // Spiral Overlay is the ONE ported module that ships ON (AppSettings.cs:2645 —
        // _spiralEnabled = true), so this row starts lit where the other three start dark, and the
        // first gesture turns it OFF. That asymmetry is upstream's and is asserted rather than
        // normalised away.
        Assert.True(window.Session.Spiral.Enabled);
        Assert.Equal(EffectDotState.Armed, page.RenderedSpiralDot);

        Click(window, row, MouseButton.Right);

        Assert.False(window.Session.Spiral.Enabled);
        Assert.False(window.Session.SpiralPreset.Current.Enabled);
        Assert.Equal(EffectDotState.Off, page.RenderedSpiralDot);

        // Both ways: a row whose toggle only goes one way is a row whose toggle does not toggle.
        Click(window, row, MouseButton.Right);
        Assert.True(window.Session.Spiral.Enabled);
        Assert.Equal(EffectDotState.Armed, page.RenderedSpiralDot);

        // Never Live from a gesture alone: no session owns the rack here and nothing is on screen,
        // and for a MOVING module Live additionally requires the frames to be advancing.
        Assert.NotEqual(EffectDotState.Live, page.RenderedSpiralDot);
        Assert.False(window.Session.SpiralSurface.Showing);
        Assert.False(row.IsChecked);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheSpiralPanelsCheckbox_AndItsRowsRightClick_AreTheSameOnePath()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowSpiralOverlay"));
        var check = Descendant<CheckBox>(window, "SpiralEnableToggle");

        // The panel opened on the module's real persisted dials.
        Assert.True(check.IsChecked);
        Assert.Equal(
            window.Session.SpiralPreset.Current.OpacityPercent,
            (int)Math.Round(Descendant<Slider>(window, "SpiralOpacitySlider").Value));

        Click(window, Descendant<RadioButton>(window, "RowSpiralOverlay"), MouseButton.Right);
        Assert.False(check.IsChecked);          // the row's gesture repainted the panel's dial
        Assert.False(window.Session.Spiral.Enabled);

        check.IsChecked = true;                 // and the panel's dial drives the same one path
        window.UpdateLayout();
        Assert.True(window.Session.Spiral.Enabled);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheSpiralPanelNamesTheLibraryFolder_WhenThereIsNoSpiralToDraw()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowSpiralOverlay"));

        // A fresh data directory has no spirals folder at all, which is the ordinary first-run
        // state — WPF's third fallback is a spiral compiled into the app and this port bundles no
        // art (D86). The panel must therefore answer "where do I put one", which is the same dead
        // end WPF names for the flash pool (FlashService.cs:589-597).
        var library = Descendant<TextBlock>(window, "SpiralLibraryState");
        Assert.Contains(window.Session.SpiralsFolder, library.Text!, StringComparison.Ordinal);

        // And the live line says the module is armed with nothing to draw, without telling a user
        // who has not started a session to stop one, or vice versa.
        var live = Descendant<TextBlock>(window, "SpiralLiveState");
        Assert.Contains("no spiral", live.Text!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Running", live.Text!, StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  SP-105 — the rack rows that switch the other two modules on
    // =====================================================================================

    [AvaloniaFact]
    public async Task TheRackIsInWpfsOrder_AndEveryRowWithAPortedEffectBehindItCarriesADot()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);

        // WPF's EFFECTS group is flash, video, subliminal, spiral, pinkfilter, visuals
        // (StudioTabView.xaml.cs:483-497). SP-111 lands Mandatory Video SECOND, where upstream puts
        // it; the two still-unported rows (Magenta Filter, Visuals) are the ones the ported rows
        // close up around. The ORDER is upstream.s and is asserted, because a rack that reorders
        // itself as modules land stops being the rack the user learned.
        var rows = window.GetVisualDescendants().OfType<RadioButton>()
            .Where(r => r.Classes.Contains("rack-row"))
            .Select(r => r.Name)
            .ToList();
        Assert.Equal(
            [
                "RowFlashImages", "RowMandatoryVideo", "RowSubliminals", "RowSpiralOverlay", "RowPinkFilter",
                "RowBubblePop", "RowBubbleCount", "RowLockCard", "RowMindWipe", "RowBrainDrain",
                "RowIntensityRamp",
            ],
            rows);

        // SP-105: three of the four had an effect whose state could be reported, and D5/D6 stayed
        // open for exactly one row. SP-106 gave that row an effect, so the loop below is now every
        // row on the page — see TheSpiralRow_NowCarriesTheGrammarToo_AndD5D6CloseForTheWholeRack.
        // SP-108 adds a fifth from a different rack GROUP (TIMING), placed after the EFFECTS block
        // exactly as upstream orders its groups (StudioTabView.xaml.cs:482-541).
        foreach (var name in new[]
        {
            "RowFlashImages", "RowMandatoryVideo", "RowSubliminals", "RowSpiralOverlay", "RowPinkFilter",
            "RowBubblePop", "RowBubbleCount", "RowLockCard", "RowMindWipe", "RowBrainDrain",
            "RowIntensityRamp",
        })
        {
            Assert.Contains(
                Descendant<RadioButton>(window, name).GetVisualDescendants()
                    .OfType<Avalonia.Controls.Shapes.Ellipse>(),
                e => e.Classes.Contains("dot"));
        }

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task RightClickOnTheSubliminalsRow_ReallyTurnsTheModuleOn_WhichNoGestureCouldDoBefore()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        var row = Descendant<RadioButton>(window, "RowSubliminals");
        var page = (CcpClient.Desktop.Views.Pages.StudioPage)window.PageFor(ShellRoutes.Studio);

        // D72, closed. The module landed in SP-101 with no row, so until this gesture existed only
        // a test or a hand-edited file could switch it on. It ships OFF
        // (CCP.Core/Models/AppSettings.cs:1234), which is why the dot starts unlit.
        Assert.False(window.Session.Subliminals.Enabled);
        Assert.Equal(EffectDotState.Off, page.RenderedSubliminalDot);

        Click(window, row, MouseButton.Right);

        Assert.True(window.Session.Subliminals.Enabled);
        Assert.True(window.Session.SubliminalPreset.Current.Enabled);
        Assert.Equal(EffectDotState.Armed, page.RenderedSubliminalDot);

        // And it really toggles, both ways — a row whose toggle only goes one way is a row whose
        // toggle does not toggle.
        Click(window, row, MouseButton.Right);
        Assert.False(window.Session.Subliminals.Enabled);
        Assert.Equal(EffectDotState.Off, page.RenderedSubliminalDot);

        // The gesture opens nothing and selects nothing, exactly as the flash row's does.
        Assert.False(row.IsChecked);
        Assert.Empty(window.GetVisualDescendants().OfType<ContextMenu>());

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task RightClickOnThePinkFilterRow_ReallyTurnsTheContinuousModuleOn_AndTheDotFollows()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        var row = Descendant<RadioButton>(window, "RowPinkFilter");
        var page = (CcpClient.Desktop.Views.Pages.StudioPage)window.PageFor(ShellRoutes.Studio);

        // Pink Filter ships OFF too (AppSettings.cs:3726). No session is running here, so the
        // gesture writes the dial and nothing is drawn — which is WPF's own outcome: its
        // quick-toggle calls RefreshOverlays() unconditionally (MainWindow.Presets.cs:1255) and
        // that method returns at once while the overlay service is stopped (OverlayService.cs:421).
        Assert.False(window.Session.PinkFilter.Enabled);
        Assert.Equal(EffectDotState.Off, page.RenderedPinkFilterDot);

        Click(window, row, MouseButton.Right);

        Assert.True(window.Session.PinkFilter.Enabled);
        Assert.True(window.Session.PinkFilterPreset.Current.Enabled);

        // Armed, not Live: the dial is on and nothing is on screen. For a CONTINUOUS module those
        // two are the same instant once a session owns it, which is why this row's dot is derived
        // from the surface rather than from the flag it was just handed.
        Assert.Equal(EffectDotState.Armed, page.RenderedPinkFilterDot);
        Assert.False(window.Session.PinkFilterSurface.Showing);

        Click(window, row, MouseButton.Right);
        Assert.False(window.Session.PinkFilter.Enabled);
        Assert.Equal(EffectDotState.Off, page.RenderedPinkFilterDot);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task EachNewRowOpensItsOwnPanel_AndOnlyThat()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);

        Click(window, Descendant<RadioButton>(window, "RowSubliminals"));
        Assert.True(Descendant<StackPanel>(window, "SubliminalModulePanel").IsVisible);
        Assert.False(Descendant<StackPanel>(window, "FlashModulePanel").IsVisible);
        Assert.False(Descendant<StackPanel>(window, "PinkFilterModulePanel").IsVisible);

        Click(window, Descendant<RadioButton>(window, "RowPinkFilter"));
        Assert.True(Descendant<StackPanel>(window, "PinkFilterModulePanel").IsVisible);
        Assert.False(Descendant<StackPanel>(window, "SubliminalModulePanel").IsVisible);
        Assert.False(Descendant<StackPanel>(window, "SpiralModulePanel").IsVisible);

        // The panel shows the module's real persisted dials, not placeholder numbers.
        Assert.Equal(
            window.Session.PinkFilterPreset.Current.OpacityPercent,
            (int)Math.Round(Descendant<Slider>(window, "PinkFilterOpacitySlider").Value));
        Assert.Equal(
            window.Session.SubliminalPreset.Current.PerMinute,
            (int)Math.Round(Descendant<Slider>(window, "SubliminalFrequencySlider").Value));

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task ThePinkFilterPanelsCheckbox_AndItsRowsRightClick_AreTheSameOnePath()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowPinkFilter"));
        var check = Descendant<CheckBox>(window, "PinkFilterEnableToggle");

        Assert.False(check.IsChecked);

        Click(window, Descendant<RadioButton>(window, "RowPinkFilter"), MouseButton.Right);
        Assert.True(check.IsChecked);          // the row's gesture repainted the panel's dial

        Click(window, check);
        Assert.False(check.IsChecked);
        Assert.False(window.Session.PinkFilterPreset.Current.Enabled);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task ThePinkFilterPanel_ReportsTheTintInForce_AndNeverClaimsItIsOnScreen()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowPinkFilter"));

        // The colour has no picker yet, so the panel REPORTS the tint rather than offering a dead
        // control (§9 D7). The default is WPF's hot pink and the panel says it is the default.
        Assert.Equal("#FF69B4 (the default)", ReadTintHead(window));

        // And the surface line claims nothing: nothing has been drawn in this session, and that is
        // a fact about the session rather than about the screen.
        var surface = Descendant<TextBlock>(window, "PinkFilterSurfaceState").Text ?? string.Empty;
        Assert.Contains("Nothing has been drawn yet", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("is on an always-on-top", surface, StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    private static string ReadTintHead(MainWindow window)
    {
        var text = Descendant<TextBlock>(window, "PinkFilterTintState").Text ?? string.Empty;
        var start = text.IndexOf('#', StringComparison.Ordinal);
        var end = text.IndexOf(" at ", StringComparison.Ordinal);
        return start >= 0 && end > start ? text[start..end] : text;
    }

    // =====================================================================================
    //  SP-108 — the rack's SECOND GROUP, and the row for a module that draws nothing
    // =====================================================================================

    [AvaloniaFact]
    public async Task TheRackNowHasTWOGROUPS_AndTheSecondOneHoldsAModuleThatDrawsNothing()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);

        // WPF has FOUR groups — EFFECTS, GAMES & CARDS, IMMERSION, TIMING (§8.3, built at
        // StudioTabView.xaml.cs:483/498/508/530) — and until SP-108 the port had rows in exactly one
        // of them, because every module it had ported was an EFFECT that painted an overlay. The
        // group headers are upstream's own strings (st4_studio_group_effects / _timing,
        // en.json:4816,4819).
        var groups = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("rack-group"))
            .Select(t => t.Text)
            .ToList();
        // SP-109 adds IMMERSION and SP-110 adds GAMES & CARDS, and each lands exactly where upstream
        // puts it: the group order is EFFECTS, GAMES & CARDS, IMMERSION, TIMING
        // (StudioTabView.xaml.cs:483/498/508/530), so the port now has all four and NOTHING was
        // reordered as they arrived. A rack that reshuffles itself as modules land stops being the
        // rack the user learned.
        Assert.Equal(["EFFECTS", "GAMES & CARDS", "IMMERSION", "TIMING"], groups);

        // And the row order is still upstream's, with the new group after the old one.
        var rows = window.GetVisualDescendants().OfType<RadioButton>()
            .Where(r => r.Classes.Contains("rack-row"))
            .Select(r => r.Name)
            .ToList();
        Assert.Equal(
            [
                "RowFlashImages", "RowMandatoryVideo", "RowSubliminals", "RowSpiralOverlay", "RowPinkFilter",
                "RowBubblePop", "RowBubbleCount", "RowLockCard", "RowMindWipe", "RowBrainDrain",
                "RowIntensityRamp",
            ],
            rows);

        // The new row carries the same grammar the other four do: a dot, and nothing about it says
        // "this module is different" to the user.
        Assert.Contains(
            Descendant<RadioButton>(window, "RowIntensityRamp").GetVisualDescendants()
                .OfType<Avalonia.Controls.Shapes.Ellipse>(),
            e => e.Classes.Contains("dot"));

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task RightClickOnTheRampRow_ReallyFlipsTheNonDrawingModule_AndTheDotFollowsBothWays()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        var row = Descendant<RadioButton>(window, "RowIntensityRamp");
        var page = (CcpClient.Desktop.Views.Pages.StudioPage)window.PageFor(ShellRoutes.Studio);

        // It ships OFF (CCP.Core/Models/AppSettings.cs:2574-2579), so the dot starts unlit.
        Assert.False(window.Session.Ramp.Enabled);
        Assert.Equal(EffectDotState.Off, page.RenderedRampDot);

        Click(window, row, MouseButton.Right);
        Assert.True(window.Session.Ramp.Enabled);
        Assert.True(window.Session.RampPreset.Current.Enabled);

        // Armed, not Live: no session owns it, so it is holding nothing. The row's dot is the
        // module's own derived state and this is the first module for which the third clause is a
        // claim about CUSTODY rather than about a clock or a screen.
        Assert.Equal(EffectDotState.Armed, page.RenderedRampDot);
        Assert.Empty(window.Session.Ramp.HeldDials);

        Click(window, row, MouseButton.Right);
        Assert.False(window.Session.Ramp.Enabled);
        Assert.Equal(EffectDotState.Off, page.RenderedRampDot);

        // The gesture still opens no menu and selects nothing, exactly as on every other row.
        Assert.Null(row.ContextMenu);
        Assert.Empty(window.GetVisualDescendants().OfType<ContextMenu>());
        Assert.False(row.IsChecked);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheRampPanelOpensOnItsOwn_AndHasNOSurfaceLineBecauseItHasNoSurface()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowIntensityRamp"));

        Assert.True(Descendant<StackPanel>(window, "RampModulePanel").IsVisible);
        Assert.False(Descendant<StackPanel>(window, "FlashModulePanel").IsVisible);
        Assert.False(Descendant<StackPanel>(window, "PinkFilterModulePanel").IsVisible);
        Assert.False(Descendant<StackPanel>(window, "SpiralModulePanel").IsVisible);

        // THE STRUCTURAL HALF OF THE PACKET'S SECOND TRAP. Every module before this one ends its
        // panel with a line about where its pixels went. This one has no such line, and the absence
        // is asserted rather than left to a reader: a surface notice here would be a sentence about
        // a capability the module deliberately never acquires.
        var named = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Name).ToList();
        Assert.Contains("FlashSurfaceState", named);
        Assert.Contains("SubliminalSurfaceState", named);
        Assert.Contains("SpiralSurfaceState", named);
        Assert.Contains("PinkFilterSurfaceState", named);
        Assert.DoesNotContain("RampSurfaceState", named);

        // What it has instead: a live-state line and a custody line, both rendered from the module.
        // The module ships OFF (CCP.Core/Models/AppSettings.cs:2574-2579), so the live line is the
        // dial's word and not the session's, and the custody line says it has borrowed nothing yet.
        var live = Descendant<TextBlock>(window, "RampLiveState").Text ?? string.Empty;
        Assert.Equal("Switched off. Nothing is ramped, session or no session.", live);

        var custody = Descendant<TextBlock>(window, "RampCustodyState").Text ?? string.Empty;
        Assert.Contains("Holding nothing", custody, StringComparison.Ordinal);

        // Its dials are the real persisted ones, not placeholder numbers.
        Assert.Equal(
            window.Session.RampPreset.Current.DurationMinutes,
            (int)Math.Round(Descendant<Slider>(window, "RampDurationSlider").Value));
        Assert.Equal(
            window.Session.RampPreset.Current.Multiplier,
            Descendant<Slider>(window, "RampMultiplierSlider").Value,
            3);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheRampPanelsCheckbox_AndItsRowsRightClick_AreTheSameOnePath()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowIntensityRamp"));
        var check = Descendant<CheckBox>(window, "RampEnableToggle");

        Assert.False(check.IsChecked);

        Click(window, Descendant<RadioButton>(window, "RowIntensityRamp"), MouseButton.Right);
        Assert.True(check.IsChecked);          // the row's gesture repainted the panel's dial

        Click(window, check);
        Assert.False(check.IsChecked);
        Assert.False(window.Session.RampPreset.Current.Enabled);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheRampsOwnLinkSwitchesWriteItsDocument_AndOnlyTheTwoDialsThePortReallyHas()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowIntensityRamp"));

        Click(window, Descendant<CheckBox>(window, "RampLinkSpiralToggle"));
        Assert.True(window.Session.RampPreset.Current.LinkSpiralOpacity);

        Click(window, Descendant<CheckBox>(window, "RampLinkPinkFilterToggle"));
        Assert.True(window.Session.RampPreset.Current.LinkPinkFilterOpacity);

        // D93. WPF has FIVE link switches (AppSettings.cs:2589-2621); flash opacity, master volume
        // and subliminal volume have no dial on any ported panel, so their switches are ABSENT rather
        // than present-and-inert — a switch that quietly does nothing is the greyed control §9 D7
        // refuses.
        var names = window.GetVisualDescendants().OfType<CheckBox>().Select(c => c.Name).ToList();
        Assert.DoesNotContain("RampLinkFlashToggle", names);
        Assert.DoesNotContain("RampLinkMasterAudioToggle", names);
        Assert.DoesNotContain("RampLinkSubliminalAudioToggle", names);

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

    // =====================================================================================
    //  SP-109 — the two rows nobody can SEE, and the one that is half a row
    // =====================================================================================

    [AvaloniaFact]
    public async Task TheRackGainsAThirdGroup_AndTheTwoAudioRowsCarryTheSameGrammarAsEveryOtherRow()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);

        // Left-click opens its own panel and only its own — the grammar needed nothing new for a
        // module whose output is sound rather than pixels.
        Click(window, Descendant<RadioButton>(window, "RowMindWipe"));
        Assert.True(Descendant<StackPanel>(window, "MindWipeModulePanel").IsVisible);
        Assert.False(Descendant<StackPanel>(window, "BrainDrainModulePanel").IsVisible);
        Assert.False(Descendant<TextBlock>(window, "RackHint").IsVisible);

        Click(window, Descendant<RadioButton>(window, "RowBrainDrain"));
        Assert.True(Descendant<StackPanel>(window, "BrainDrainModulePanel").IsVisible);
        Assert.False(Descendant<StackPanel>(window, "MindWipeModulePanel").IsVisible);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task RightClickOnAnAudioRow_ReallyFlipsTheModule_AndTheDotFollowsBothWays()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        var row = Descendant<RadioButton>(window, "RowMindWipe");
        var page = window.GetVisualDescendants().OfType<CcpClient.Desktop.Views.Pages.StudioPage>().Single();

        // Ships OFF (MainWindow.StartStop.cs:229 gates on the flag), so the dot starts dark.
        Assert.False(boot.Session.MindWipePreset.Current.Enabled);
        Assert.Equal(EffectDotState.Off, page.RenderedMindWipeDot);

        Click(window, row, MouseButton.Right);

        // The persisted dial really moved, and the dot really followed — with NO session running,
        // so nothing here opens an audio device or claims anything about sound.
        Assert.True(boot.Session.MindWipePreset.Current.Enabled);
        Assert.Equal(EffectDotState.Armed, page.RenderedMindWipeDot);

        Click(window, row, MouseButton.Right);
        Assert.False(boot.Session.MindWipePreset.Current.Enabled);
        Assert.Equal(EffectDotState.Off, page.RenderedMindWipeDot);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheBrainDrainRowSAYSItIsHalfARow_OnTheRowItselfAndOnItsPanel()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowBrainDrain"));

        // THE ROW'S LABEL IS LOAD-BEARING, not decoration: the module's dot is Live while its audio
        // half runs, and that is only honest because the row says which half it is. This fact is
        // what stops the label being "tidied" back to plain "Brain Drain".
        var label = Descendant<RadioButton>(window, "RowBrainDrain")
            .GetVisualDescendants().OfType<TextBlock>().First();
        Assert.Equal("Brain Drain (audio half)", label.Text);

        // And the panel LEADS with the missing half, in the MODULE's own words — the same constant
        // the arm result's typed reason carries, so the panel and the outcome are one account.
        //
        // "LEADS" is a POSITIONAL claim, so it is pinned positionally rather than asserted in prose
        // (code review): the notice sits above every control on the panel, so a user meets the
        // missing half before they meet the switch that enables the half that exists. An earlier
        // version of this fact pinned only the text and the absence of blur controls, which would
        // have stayed green if the notice were moved to the bottom.
        var panel = Descendant<StackPanel>(window, "BrainDrainModulePanel");
        var children = panel.Children.ToList();
        var noticeIndex = children.FindIndex(c => c.GetVisualDescendants().OfType<TextBlock>()
            .Any(t => t.Name == "BrainDrainVisualHalfState"));
        var enableIndex = children.FindIndex(c => c.Name == "BrainDrainEnableToggle");
        Assert.True(noticeIndex >= 0 && enableIndex >= 0, "both the notice and the enable are on the panel");
        Assert.True(
            noticeIndex < enableIndex,
            $"the missing-half notice is at index {noticeIndex} and the enable at {enableIndex} — the "
            + "notice must come first");

        var notice = Descendant<TextBlock>(window, "BrainDrainVisualHalfState");
        Assert.Equal(BrainDrainEffect.VisualHalfNotice, notice.Text);
        Assert.Contains("VISUAL half", notice.Text!, StringComparison.Ordinal);
        Assert.Contains("30 to 60 times a second", notice.Text!, StringComparison.Ordinal);

        // No control on this panel offers the missing half. A greyed blur slider would be the dead
        // dial §9 D7 refuses, and worse here: it would imply the blur is one setting away.
        var names = window.GetVisualDescendants().OfType<Control>().Select(c => c.Name).ToList();
        Assert.DoesNotContain("BrainDrainBlurSlider", names);
        Assert.DoesNotContain("BrainDrainMeltToggle", names);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheAudioPanelsHaveNoSurfaceLine_AndSayInsteadWhatTheOsWasAsked()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowMindWipe"));

        // Every DRAWING module's panel ends in a "where did the pixels go" line. These two cannot
        // have one, so the absence is asserted the way SP-108 asserted the ramp's.
        var names = window.GetVisualDescendants().OfType<Control>().Select(c => c.Name).ToList();
        Assert.DoesNotContain("MindWipeSurfaceState", names);
        Assert.DoesNotContain("BrainDrainSurfaceState", names);
        Assert.Contains("FlashSurfaceState", names);

        // What they have instead: a line that quotes the operating system. Before anything has been
        // asked it names the MECHANISM and says so — a fact about this session, not a claim about
        // the machine, and it is there before the user presses anything.
        var audioLine = Descendant<TextBlock>(window, "MindWipeAudioState");
        Assert.Equal(
            "Clips play on a shared audio output device. Nothing has been asked of the operating "
            + "system yet.",
            audioLine.Text);

        // And the clip folder is named, so an empty pool has an answer.
        Assert.Contains(
            "Put .mp3, .wav or .ogg files in",
            Descendant<TextBlock>(window, "MindWipeClipState").Text!,
            StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheAudioPanelsDialsAreTheRealPersistedOnes_AndMovingOneWritesThroughTheModule()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowMindWipe"));

        var frequency = Descendant<Slider>(window, "MindWipeFrequencySlider");
        Assert.Equal(MindWipeSchedule.DefaultPerHour, frequency.Value);
        Assert.Equal(MindWipeSchedule.MinPerHour, frequency.Minimum);
        Assert.Equal(MindWipeSchedule.MaxPerHour, frequency.Maximum);

        frequency.Value = 42;
        window.UpdateLayout();
        Assert.Equal(42, boot.Session.MindWipePreset.Current.PerHour);
        Assert.Equal("42", Descendant<TextBlock>(window, "MindWipeFrequencyValue").Text);

        Click(window, Descendant<RadioButton>(window, "RowBrainDrain"));
        var intensity = Descendant<Slider>(window, "BrainDrainIntensitySlider");
        Assert.Equal(BrainDrainSchedule.MinIntensity, intensity.Minimum);
        Assert.Equal(BrainDrainSchedule.MaxIntensity, intensity.Maximum);

        intensity.Value = 77;
        window.UpdateLayout();
        Assert.Equal(77, boot.Session.BrainDrainPreset.Current.IntensityPercent);

        // The high-refresh switch is the module's own dial, not the rack's enable, so it writes
        // through the module rather than through QuickToggle.
        var highRefresh = Descendant<CheckBox>(window, "BrainDrainHighRefreshToggle");
        Assert.False(boot.Session.BrainDrainPreset.Current.HighRefresh);
        highRefresh.IsChecked = true;
        window.UpdateLayout();
        Assert.True(boot.Session.BrainDrainPreset.Current.HighRefresh);
        Assert.False(boot.Session.BrainDrainPreset.Current.Enabled); // and it did NOT enable the module

        await boot.Host.ShutdownAsync();
    }

    // =================================================================================
    //  SP-110 — the one row that ASKS
    // =================================================================================

    [AvaloniaFact]
    public async Task TheLockCardPanelLEADSWithTheFactThatThisModuleInterruptsYou()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowLockCard"));

        // THE INTERRUPTION WARNING IS LOAD-BEARING. Every other row on this page draws, tints,
        // paces, ramps or plays OVER whatever the user is doing; this one takes the keyboard away
        // from it. A user is entitled to know that before they tick the box, not the first time a
        // card lands in the middle of somebody else's chat window.
        //
        // "LEADS" is a POSITIONAL claim, so it is pinned positionally as SP-109's missing-half
        // notice is: the warning sits above the enable, so nobody can reach the switch without
        // passing it. Pinning only the text would stay green if it were moved to the bottom.
        var panel = Descendant<StackPanel>(window, "LockCardModulePanel");
        var children = panel.Children.ToList();
        var noticeIndex = children.FindIndex(c => c.GetVisualDescendants().OfType<TextBlock>()
            .Any(t => t.Name == "LockCardInterruptionNotice"));
        var enableIndex = children.FindIndex(c => c.Name == "LockCardEnableToggle");
        Assert.True(noticeIndex >= 0 && enableIndex >= 0, "both the warning and the enable are on the panel");
        Assert.True(
            noticeIndex < enableIndex,
            $"the interruption warning is at index {noticeIndex} and the enable at {enableIndex} — the warning "
            + "must come first");

        var notice = Descendant<TextBlock>(window, "LockCardInterruptionNotice");
        Assert.Equal(InputPanelNotices.InterruptionNotice, notice.Text);
        Assert.Contains("INTERRUPTS you", notice.Text!, StringComparison.Ordinal);

        // And the ESCAPE PROMISE is in it, because it is the safety property this build rests on:
        // upstream's own fall-open rule (LockCardWindow.xaml.cs:632, :610-622) evaluates to "Esc
        // always closes" here, since there is no panic hook for strict mode to lean on.
        Assert.Contains("Esc always closes a card in this build", notice.Text!, StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheLockCardPanelHasNoSurfaceLine_AndSaysInsteadWhatTheOsWasAskedAboutTheKeyboard()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowLockCard"));

        var names = window.GetVisualDescendants().OfType<Control>().Select(c => c.Name).ToList();
        Assert.DoesNotContain("LockCardSurfaceState", names);

        // Before anything has been asked the line names the MECHANISM and says nothing has been
        // attempted — a fact about this session rather than a claim about the machine, and it is
        // there before the user presses anything.
        Assert.Equal(
            "A card is a window that takes the keyboard. Nothing has been asked of the operating system yet.",
            Descendant<TextBlock>(window, "LockCardCaptureState").Text);

        // The scope notice names both halves that are NOT ported, where the user reads them.
        var scope = Descendant<TextBlock>(window, "LockCardScopeNotice");
        Assert.Equal(InputPanelNotices.ScopeNotice, scope.Text);
        Assert.Contains("EVERY monitor", scope.Text!, StringComparison.Ordinal);
        Assert.Contains("SPEAKING the phrase", scope.Text!, StringComparison.Ordinal);

        // And the phrase pool is named with the file that edits it, so it is never a mystery.
        var phrases = Descendant<TextBlock>(window, "LockCardPhraseState");
        Assert.Contains("GOOD GIRLS OBEY", phrases.Text!, StringComparison.Ordinal);
        Assert.Contains(LockCardPresetDocument.FileName, phrases.Text!, StringComparison.Ordinal);

        // No control offers the two halves that do not exist. A greyed voice-mode switch would be
        // the dead dial §9 D7 refuses, and worse: it would imply a microphone is one setting away.
        Assert.DoesNotContain("LockCardVoiceToggle", names);
        Assert.DoesNotContain("LockCardPhraseEditorButton", names);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheLockCardDialsAreTheRealPersistedOnes_AndMovingOneWritesThroughTheModule()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowLockCard"));

        var frequency = Descendant<Slider>(window, "LockCardFrequencySlider");
        Assert.Equal(LockCardSchedule.DefaultPerHour, frequency.Value);
        Assert.Equal(LockCardSchedule.MinPerHour, frequency.Minimum);
        Assert.Equal(LockCardSchedule.MaxPerHour, frequency.Maximum);

        frequency.Value = 7;
        window.UpdateLayout();
        Assert.Equal(7, boot.Session.LockCardPreset.Current.PerHour);
        Assert.Equal("7", Descendant<TextBlock>(window, "LockCardFrequencyValue").Text);

        var repeats = Descendant<Slider>(window, "LockCardRepeatsSlider");
        Assert.Equal(LockCardSchedule.DefaultRepeats, repeats.Value);
        repeats.Value = 6;
        window.UpdateLayout();
        Assert.Equal(6, boot.Session.LockCardPreset.Current.Repeats);
        Assert.Equal("6x", Descendant<TextBlock>(window, "LockCardRepeatsValue").Text);

        // Strict is the module's own dial, not the rack's enable, so it writes through the module
        // and does NOT switch the module on.
        var strict = Descendant<CheckBox>(window, "LockCardStrictToggle");
        Assert.False(boot.Session.LockCardPreset.Current.Strict);
        strict.IsChecked = true;
        window.UpdateLayout();
        Assert.True(boot.Session.LockCardPreset.Current.Strict);
        Assert.False(boot.Session.LockCardPreset.Current.Enabled);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheLockCardRowCarriesTheSameGrammarAsEveryOtherRow_IncludingItsQuickToggle()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);

        var label = Descendant<RadioButton>(window, "RowLockCard")
            .GetVisualDescendants().OfType<TextBlock>().First();
        Assert.Equal("Lock Card", label.Text);
        Assert.Contains(
            Descendant<RadioButton>(window, "RowLockCard").GetVisualDescendants()
                .OfType<Avalonia.Controls.Shapes.Ellipse>(),
            e => e.Classes.Contains("dot"));

        // WPF's right-click quick-toggle reaches this row like any other (StudioTabView.xaml.cs:660
        // -> :1109-1133): nothing about the row tells the user it is a different KIND of module.
        Assert.False(boot.Session.LockCardPreset.Current.Enabled);
        Click(window, Descendant<RadioButton>(window, "RowLockCard"), MouseButton.Right);
        Assert.True(boot.Session.LockCardPreset.Current.Enabled);

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  SP-111 — the row that plays a FILE, and it is half a row
    // =====================================================================================

    [AvaloniaFact]
    public async Task TheMandatoryVideoRowSAYSItIsHalfARow_OnTheRowItselfAndOnItsPanel()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowMandatoryVideo"));

        // THE ROW'S LABEL IS LOAD-BEARING, not decoration: the module's dot goes Live while the
        // picture moves, and that is only honest because the row says which half it is. This fact is
        // what stops the label being "tidied" back to plain "Mandatory Video".
        var label = Descendant<RadioButton>(window, "RowMandatoryVideo")
            .GetVisualDescendants().OfType<TextBlock>().First();
        Assert.Equal(MandatoryVideoEffect.DisplayTitle, label.Text);
        Assert.Equal("Mandatory Video (silent half)", label.Text);

        // And the panel LEADS with the missing half, in the MODULE's own words — the same constant
        // the arm result's typed reason carries, so the panel and the outcome are one account of the
        // absence rather than two. "LEADS" is a POSITIONAL claim and is pinned positionally.
        var panel = Descendant<StackPanel>(window, "MandatoryVideoModulePanel");
        var children = panel.Children.ToList();
        var noticeIndex = children.FindIndex(c => c.GetVisualDescendants().OfType<TextBlock>()
            .Any(t => t.Name == "MandatoryVideoSilentHalfState"));
        var enableIndex = children.FindIndex(c => c.Name == "MandatoryVideoEnableToggle");
        Assert.True(noticeIndex >= 0 && enableIndex >= 0, "both the notice and the enable are on the panel");
        Assert.True(
            noticeIndex < enableIndex,
            $"the missing-half notice is at index {noticeIndex} and the enable at {enableIndex} — the "
            + "notice must come first");

        var notice = Descendant<TextBlock>(window, "MandatoryVideoSilentHalfState");
        Assert.Equal(MandatoryVideoEffect.VideoPanelNoticeText, notice.Text);
        Assert.Contains("SILENTLY", notice.Text!, StringComparison.Ordinal);
        Assert.Contains("ABSENT rather than broken", notice.Text!, StringComparison.Ordinal);

        // No control on this panel offers the missing half. A greyed volume slider would be the dead
        // dial §9 D7 refuses, and worse here: it would imply the sound is one setting away.
        var names = window.GetVisualDescendants().OfType<Control>().Select(c => c.Name).ToList();
        Assert.DoesNotContain("MandatoryVideoVolumeSlider", names);
        Assert.DoesNotContain("MandatoryVideoAudioDeviceBox", names);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheMandatoryVideoRowCarriesTheWholeRackGrammar_AndItsPanelQuotesTheOperatingSystem()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);

        Assert.Contains(
            Descendant<RadioButton>(window, "RowMandatoryVideo").GetVisualDescendants()
                .OfType<Avalonia.Controls.Shapes.Ellipse>(),
            e => e.Classes.Contains("dot"));

        // WPF's right-click quick-toggle reaches this row like any other (StudioTabView.xaml.cs:660
        // -> :1109-1133). The rack grammar needed nothing new for a module that plays a file.
        Assert.False(boot.Session.MandatoryVideoPreset.Current.Enabled);
        Click(window, Descendant<RadioButton>(window, "RowMandatoryVideo"), MouseButton.Right);
        Assert.True(boot.Session.MandatoryVideoPreset.Current.Enabled);

        // The panel's closing line is the CAPABILITY's own answer, not a sentence this page composed.
        // Before anything has been asked it says "nobody asked", which is a different fact from "the
        // answer was no" and is the distinction VideoSurfaceObservation.NotAsked exists to hold.
        Click(window, Descendant<RadioButton>(window, "RowMandatoryVideo"));
        var surfaceLine = Descendant<TextBlock>(window, "MandatoryVideoSurfaceState");
        Assert.Contains(
            "The video surface has not been asked for anything yet.",
            surfaceLine.Text!,
            StringComparison.Ordinal);
        Assert.Contains("nobody asked", surfaceLine.Text!, StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  SP-112 - the row that consumes TWO capabilities, and the group order the rack decides
    // =====================================================================================

    [AvaloniaFact]
    public async Task TheBubbleCountRowOpensGAMESANDCARDS_BecauseTheRACKSOrderIsTheOneThePortTakes()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);

        // The two upstream orders DISAGREE here: the rack is Bubble Pop, Bubble Count, Lock Card,
        // Bouncing Text (StudioTabView.xaml.cs:499-505) while StartEngine starts the lock card
        // first and the bubble count second (MainWindow.StartStop.cs:206-215). D90 settled which
        // one this port takes for Spiral/Pink Filter, and this row follows it: the RACK's, because
        // the rack is the order the user has learned. Asserted on the RENDERED rack rather than on
        // a list, so a reordered panel reds here.
        //
        // SP-113 lands Bubble Pop at the head of the group — Add("bubbles", ...) at :499 — so the
        // whole of upstream's GAMES & CARDS order that this port has rows for is now pinned here.
        var rows = window.GetVisualDescendants().OfType<RadioButton>()
            .Where(r => r.Classes.Contains("rack-row"))
            .Select(r => r.Name)
            .ToList();
        var popIndex = rows.IndexOf("RowBubblePop");
        var bubbleIndex = rows.IndexOf("RowBubbleCount");
        var lockIndex = rows.IndexOf("RowLockCard");
        var pinkIndex = rows.IndexOf("RowPinkFilter");
        Assert.True(popIndex >= 0 && bubbleIndex >= 0 && lockIndex >= 0);
        Assert.True(
            pinkIndex < popIndex && popIndex < bubbleIndex && bubbleIndex < lockIndex,
            $"the rack renders pinkfilter at {pinkIndex}, bubblepop at {popIndex}, bubblecount at "
            + $"{bubbleIndex} and lockcard at {lockIndex}; GAMES and CARDS must open with Bubble Pop, "
            + "which is where StudioTabView.xaml.cs:499 puts it");

        // The label is the row's own, minus the emoji this rack does not render.
        var label = Descendant<RadioButton>(window, "RowBubbleCount")
            .GetVisualDescendants().OfType<TextBlock>().First();
        Assert.Equal(BubbleCountEffect.DisplayTitle, label.Text);
        Assert.Equal("Bubble Count", label.Text);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task THERACKSCROLLS_AndTheLastRowIsReachableOnlyBecauseItDoes()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);

        // D140, pinned EXPLICITLY rather than transitively. The Click helper now brings its target
        // into view, which is what a user's mouse wheel does — but it also means no other rack fact
        // can ever again notice a clipped row, so the scrolling itself needs its own assertion.
        var scroll = Descendant<ScrollViewer>(window, "RackScroll");
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
        Assert.Equal(
            Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);

        // AND THE RACK REALLY OVERFLOWS at ten rows, which is why this matters: the extent is taller
        // than the viewport, so the last row is off-screen until something scrolls. A rack that fit
        // would make this fact vacuous, so the overflow is asserted rather than assumed.
        window.UpdateLayout();
        Assert.True(
            scroll.Extent.Height > scroll.Viewport.Height,
            $"the rack's extent is {scroll.Extent.Height} and its viewport {scroll.Viewport.Height}: the "
            + "rack now fits, so this fact no longer proves the scrolling is load-bearing");

        // The last row is reachable through the scroller, and selecting it really opens its panel -
        // the outcome the clipping took away.
        var ramp = Descendant<RadioButton>(window, "RowIntensityRamp");
        Click(window, ramp);
        Assert.True(ramp.IsChecked);
        Assert.True(Descendant<StackPanel>(window, "RampModulePanel").IsVisible);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheBubbleCountRowCarriesTheWholeRackGrammar_AndItsPanelLEADSWithTheInterruption()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);

        Assert.Contains(
            Descendant<RadioButton>(window, "RowBubbleCount").GetVisualDescendants()
                .OfType<Avalonia.Controls.Shapes.Ellipse>(),
            e => e.Classes.Contains("dot"));

        // The right-click quick-toggle reaches this row like any other. The rack grammar needed
        // nothing new for a module that uses two capabilities.
        Assert.False(boot.Session.BubbleCountPreset.Current.Enabled);
        Click(window, Descendant<RadioButton>(window, "RowBubbleCount"), MouseButton.Right);
        Assert.True(boot.Session.BubbleCountPreset.Current.Enabled);

        Click(window, Descendant<RadioButton>(window, "RowBubbleCount"));

        // The interruption warning LEADS, above the dials - a POSITIONAL claim, pinned
        // positionally, exactly as the Lock Card's and Mandatory Video's are. This row interrupts
        // TWICE and a user is entitled to read that before ticking the box.
        var panel = Descendant<StackPanel>(window, "BubbleCountModulePanel");
        var children = panel.Children.ToList();
        var noticeIndex = children.FindIndex(c => c.GetVisualDescendants().OfType<TextBlock>()
            .Any(t => t.Name == "BubbleCountInterruptionNotice"));
        var enableIndex = children.FindIndex(c => c.Name == "BubbleCountEnableToggle");
        Assert.True(noticeIndex >= 0 && enableIndex >= 0);
        Assert.True(
            noticeIndex < enableIndex,
            $"the interruption notice is at index {noticeIndex} and the enable at {enableIndex}");

        var notice = Descendant<TextBlock>(window, "BubbleCountInterruptionNotice");
        Assert.Equal(BubbleCountPanelNotices.InterruptionNotice, notice.Text);
        Assert.Contains("INTERRUPTS you twice", notice.Text!, StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheBubbleCountPanelQuotesBOTHCapabilities_BecauseEitherCanRefuseOnItsOwn()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowBubbleCount"));

        // THE ONLY PANEL ON THIS PAGE THAT QUOTES TWO CAPABILITIES, because it is the only row that
        // needs two. Before anything has been asked each half says "nobody asked" - a different
        // fact from "the answer was no", and one sentence covering both channels would be false
        // about one of them.
        var line = Descendant<TextBlock>(window, "BubbleCountCapabilityState");
        Assert.Contains("Video: nothing has been asked", line.Text!, StringComparison.Ordinal);
        Assert.Contains("Question: nothing has been asked", line.Text!, StringComparison.Ordinal);

        // And no strict switch exists on this panel. Upstream has one; this build has neither of the
        // two things it does, so it is absent rather than greyed (D93).
        var names = window.GetVisualDescendants().OfType<Control>().Select(c => c.Name).ToList();
        Assert.DoesNotContain("BubbleCountStrictToggle", names);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheBubbleCountPanelsDialsAreUpstreamsOwnBounds_AndDifficultyReadsAsAWord()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowBubbleCount"));

        var frequency = Descendant<Slider>(window, "BubbleCountFrequencySlider");
        Assert.Equal(BubbleCountSchedule.MinPerHour, frequency.Minimum);

        // TEN, not the video row's twenty: upstream clamps this dial at the point of use with its
        // own comment, "Frequency is games per hour (1-10)" (Services/BubbleCountService.cs:88).
        Assert.Equal(BubbleCountSchedule.MaxPerHour, frequency.Maximum);
        Assert.Equal(10, frequency.Maximum);
        Assert.Equal(BubbleCountSchedule.DefaultPerHour, frequency.Value);

        // The difficulty is a WORD on the panel, never the raw enum ordinal a user would have to
        // decode.
        Assert.Equal("Medium", Descendant<TextBlock>(window, "BubbleCountDifficultyValue").Text);
        var difficulty = Descendant<Slider>(window, "BubbleCountDifficultySlider");
        difficulty.Value = 2;
        Assert.Equal(BubbleCountDifficulty.Hard, boot.Session.BubbleCountPreset.Current.Difficulty);
        Assert.Equal("Hard", Descendant<TextBlock>(window, "BubbleCountDifficultyValue").Text);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheVideoPanelsDialsAreUpstreamsSliderBounds_AndTheCapIsShownAsNoCapAtZero()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowMandatoryVideo"));

        var frequency = Descendant<Slider>(window, "MandatoryVideoFrequencySlider");
        Assert.Equal(MandatoryVideoSchedule.MinPerHour, frequency.Minimum);
        Assert.Equal(MandatoryVideoSchedule.MaxPerHour, frequency.Maximum);
        Assert.Equal(MandatoryVideoSchedule.DefaultPerHour, frequency.Value);

        // Zero is upstream's own "no cap" (VideoService.cs:5509-5510) and the panel says so in
        // words rather than showing a bare 0 the user would read as "no video at all".
        Assert.Equal("no cap", Descendant<TextBlock>(window, "MandatoryVideoMaxLengthValue").Text);

        var cap = Descendant<Slider>(window, "MandatoryVideoMaxLengthSlider");
        cap.Value = 120;
        Assert.Equal(120, boot.Session.MandatoryVideoPreset.Current.MaxSeconds);
        Assert.Equal("120s", Descendant<TextBlock>(window, "MandatoryVideoMaxLengthValue").Text);

        await boot.Host.ShutdownAsync();
    }

    // ---------------------------------------------------------------------------------
    //  SP-113 — the row the user ACTS on
    // ---------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task TheBubblePopRowCarriesTheWholeRackGrammar_AndItsPanelLEADSWithTheInterruption()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);

        Assert.Contains(
            Descendant<RadioButton>(window, "RowBubblePop").GetVisualDescendants()
                .OfType<Avalonia.Controls.Shapes.Ellipse>(),
            e => e.Classes.Contains("dot"));

        // The label is upstream's own, and it is NOT the dispatch key: the key is "bubbles"
        // (StudioTabView.xaml.cs:499) and the label is "Bubble Pop".
        var label = Descendant<RadioButton>(window, "RowBubblePop")
            .GetVisualDescendants().OfType<TextBlock>().First();
        Assert.Equal(BubblePopEffect.DisplayTitle, label.Text);
        Assert.Equal("Bubble Pop", label.Text);
        Assert.Equal("bubbles", BubblePopEffect.EffectId);

        // The right-click quick-toggle reaches this row like any other. The rack grammar needed
        // nothing new for a module the user clicks back at.
        Assert.False(boot.Session.BubblePopPreset.Current.Enabled);
        Click(window, Descendant<RadioButton>(window, "RowBubblePop"), MouseButton.Right);
        Assert.True(boot.Session.BubblePopPreset.Current.Enabled);

        Click(window, Descendant<RadioButton>(window, "RowBubblePop"));

        // The interruption warning LEADS, above the dials — a POSITIONAL claim, pinned
        // positionally, exactly as the Lock Card's, Mandatory Video's and Bubble Count's are. This
        // row's warning is the inverse of theirs and a user is entitled to read it before ticking
        // the box: these windows cover points and take clicks, and take nothing else.
        var panel = Descendant<StackPanel>(window, "BubblePopModulePanel");
        var children = panel.Children.ToList();
        var noticeIndex = children.FindIndex(c => c.GetVisualDescendants().OfType<TextBlock>()
            .Any(t => t.Name == "BubblePopInterruptionNotice"));
        var enableIndex = children.FindIndex(c => c.Name == "BubblePopEnableToggle");
        Assert.True(noticeIndex >= 0 && enableIndex >= 0);
        Assert.True(
            noticeIndex < enableIndex,
            $"the interruption notice is at index {noticeIndex} and the enable at {enableIndex}");

        var notice = Descendant<TextBlock>(window, "BubblePopInterruptionNotice");
        Assert.Equal(PointerPanelNotices.InterruptionNotice, notice.Text);
        Assert.Contains("never take the keyboard or the foreground", notice.Text!, StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheBubblePopPanelCarriesAnEVIDENCENotice_WhichNoOtherPanelOnThisPageNeeds()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowBubblePop"));

        // Everything else on this page is something the user can SEE happening. "A click would
        // reach this window" is a question only the operating system can answer, so the panel says
        // what was asked AND, in the same breath, what no automated check can ever show.
        var evidence = Descendant<TextBlock>(window, "BubblePopEvidenceNotice");
        Assert.Equal(PointerPanelNotices.EvidenceNotice, evidence.Text);
        Assert.Contains("manual step", evidence.Text!, StringComparison.Ordinal);

        // The delivery line reads as evidence and not as a fault before anything has been clicked.
        var delivery = Descendant<TextBlock>(window, "BubblePopDeliveryState");
        Assert.Contains("not a fault", delivery.Text!, StringComparison.Ordinal);

        // The capability's own answer, verbatim, and "nobody asked" before anything was asked —
        // which is a different fact from "the answer was no".
        var capability = Descendant<TextBlock>(window, "BubblePopCapabilityState");
        Assert.Contains("has not been asked for anything yet", capability.Text!, StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheBubblePopPanelsDialsAreUpstreamsOwnBounds_AndEachOneReadsInItsOwnUnits()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowBubblePop"));

        // Spawns per MINUTE, 1..60 (CCP.Core/Models/AppSettings.cs:2743-2747) — not per hour, which is what every
        // other paced row on this page uses and what a reader would otherwise assume.
        var frequency = Descendant<Slider>(window, "BubblePopFrequencySlider");
        Assert.Equal(BubblePopField.MinPerMinute, frequency.Minimum);
        Assert.Equal(BubblePopField.MaxPerMinute, frequency.Maximum);
        Assert.Equal(BubblePopField.DefaultPerMinute, frequency.Value);
        Assert.Contains("one every 12 s", Descendant<TextBlock>(window, "BubblePopFrequencyValue").Text!,
            StringComparison.Ordinal);

        // Size, 50..150 % (Services/BubbleSizing.cs:50,57), shown in the PIXELS the user will see.
        var size = Descendant<Slider>(window, "BubblePopSizeSlider");
        Assert.Equal(BubblePopField.MinSizePercent, size.Minimum);
        Assert.Equal(BubblePopField.MaxSizePercent, size.Maximum);
        size.Value = 50;
        Assert.Equal(50, boot.Session.BubblePopPreset.Current.SizePercent);
        Assert.Contains("75 to", Descendant<TextBlock>(window, "BubblePopSizeValue").Text!,
            StringComparison.Ordinal);

        // Extra speed, 0..500 % (CCP.Core/Models/AppSettings.cs:2814-2818), shown WITH the race bound, because
        // they are the same number.
        var speed = Descendant<Slider>(window, "BubblePopSpeedSlider");
        Assert.Equal(BubblePopField.MinSpeedBoostPercent, speed.Minimum);
        Assert.Equal(BubblePopField.MaxSpeedBoostPercent, speed.Maximum);
        speed.Value = 500;
        Assert.Equal(500, boot.Session.BubblePopPreset.Current.SpeedBoostPercent);
        Assert.Contains("12 px", Descendant<TextBlock>(window, "BubblePopSpeedValue").Text!,
            StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheBubblePopPanelHasNONEOfUpstreamsFiveUnportedDials_AbsentRatherThanGreyed()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowBubblePop"));

        // D93's rule: a dial that moves nothing is ABSENT, never present-and-disabled. Upstream's
        // card carries Solid mode (AppSettings.cs:2758), a pop-volume slider (:2230), a ramp link
        // (:2236), a session-clickable switch (:2242) and the Trigger Bubbles block (:2250-2280);
        // BubblePopPresetDocument says why each one is gone.
        var names = window.GetVisualDescendants().OfType<Control>().Select(c => c.Name).ToList();
        Assert.DoesNotContain("BubblePopSolidModeToggle", names);
        Assert.DoesNotContain("BubblePopVolumeSlider", names);
        Assert.DoesNotContain("BubblePopLinkRampToggle", names);
        Assert.DoesNotContain("BubblePopClickableToggle", names);
        Assert.DoesNotContain("BubblePopTriggersToggle", names);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task SelectingTheBubblePopRowClosesEveryOtherPanel_AndTheRackHintStaysHidden()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);

        Click(window, Descendant<RadioButton>(window, "RowBubbleCount"));
        Assert.True(Descendant<StackPanel>(window, "BubbleCountModulePanel").IsVisible);

        Click(window, Descendant<RadioButton>(window, "RowBubblePop"));

        Assert.True(Descendant<StackPanel>(window, "BubblePopModulePanel").IsVisible);
        Assert.False(Descendant<StackPanel>(window, "BubbleCountModulePanel").IsVisible);
        Assert.False(Descendant<StackPanel>(window, "LockCardModulePanel").IsVisible);
        Assert.False(Descendant<TextBlock>(window, "RackHint").IsVisible);

        await boot.Host.ShutdownAsync();
    }
}
