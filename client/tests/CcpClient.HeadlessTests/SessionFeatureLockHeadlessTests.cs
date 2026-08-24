using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The SESSION FEATURE LOCK: while a scripted session runs, the dials it owns go read-only and the
/// rack says why.
///
/// <para><b>What this is fixing.</b> A scripted session borrows eleven preset documents
/// (<see cref="ScriptedSessionDials"/>), writes its own values into them, and gives the ORIGINALS
/// back at the end through a whole-document <c>Replace</c>. The Studio rack binds to those same
/// documents. So before this landed, a user could drag a slider during a session, watch it move,
/// have it silently rewritten by the next ramp tick, and have the edit discarded wholesale at the
/// restore — upstream's own description of the defect
/// (<c>MainWindow/MainWindow.SessionFeatureLock.cs:19-26</c>: "An editable-looking dial whose value
/// is overwritten in one second and thrown away at the end is worse than a greyed-out one. Greying
/// it out is just telling the truth.").</para>
///
/// <para><b>Both directions are facts here, on purpose.</b> Over-locking is a regression too
/// (<c>Features/SessionLock.cs:36-38</c>), so every fact that pins what goes grey is paired with
/// one that pins what stays live through the same running session: the volumes, the strict lock,
/// the whole Brain Drain and Scheduler panels, and the ramp's own sliders.</para>
///
/// <para>Nothing here waits. The scripted run takes an injected clock
/// (<see cref="CompositionRoot.ScriptedClockFactory"/>) and every "time passed" below is a hand
/// advance on it.</para>
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): visual/logical tree,
/// <c>IsEnabled</c>, and real input routing. No composited pixel is claimed — the headed
/// <c>studio-dial</c> captures are what claim those.</para>
/// </summary>
public class SessionFeatureLockHeadlessTests : HeadlessTest
{
    /// <summary>
    /// The thirty dials a running scripted session owns, in the order the logical tree yields them.
    ///
    /// <para><b>This list is the port of upstream's 42 <c>SessionLock.Owned</c> attribute sites</b>
    /// and every entry traces to one. Flash enable/frequency/images
    /// (<c>Features/FlashFeatureControl.xaml:43,82,98</c>); the Visuals draw trio
    /// (<c>VisualsFeatureControl.xaml:55,71,103</c>); mandatory video's enable, rate and max length
    /// (<c>VideoFeatureControl.xaml:42,80,112</c>); subliminal enable and rate
    /// (<c>SubliminalFeatureControl.xaml:42,80</c>); bouncing text's enable, speed and size
    /// (<c>BouncingTextFeatureControl.xaml:53,92,109</c>); spiral enable and opacity
    /// (<c>SpiralFeatureControl.xaml:45,85</c>); pink filter enable and opacity
    /// (<c>PinkFilterFeatureControl.xaml:44,82</c>); bubble pop's enable, rate and speed
    /// (<c>BubblePopFeatureControl.xaml:43,81,97</c>); bubble count's enable, rate and difficulty
    /// (<c>BubbleCountFeatureControl.xaml:44,82,98</c>); the lock card's enable, rate and repeats
    /// (<c>LockCardFeatureControl.xaml:50,90,107</c>); mind wipe's enable and rate
    /// (<c>MindWipeFeatureControl.xaml:49,88</c>); and the ramp CURVE and nothing else on that panel
    /// (<c>IntensityRampFeatureControl.xaml:94</c>, with
    /// <c>Views/Controls/Studio/RampRackPanel.xaml:24-26</c> saying in words that it is the only
    /// one).</para>
    /// </summary>
    private static readonly string[] OwnedDials =
    [
        "FlashEnableToggle", "FlashFrequencySlider", "FlashImagesSlider",
        "VisualsScaleSlider", "VisualsOpacitySlider", "VisualsDurationSlider",
        "MandatoryVideoEnableToggle", "MandatoryVideoFrequencySlider", "MandatoryVideoMaxLengthSlider",
        "SubliminalEnableToggle", "SubliminalFrequencySlider",
        "BouncingTextEnableToggle", "BouncingTextSpeedSlider", "BouncingTextSizeSlider",
        "SpiralEnableToggle", "SpiralOpacitySlider",
        "PinkFilterEnableToggle", "PinkFilterOpacitySlider",
        "BubblePopEnableToggle", "BubblePopFrequencySlider", "BubblePopSpeedSlider",
        "BubbleCountEnableToggle", "BubbleCountFrequencySlider", "BubbleCountDifficultySlider",
        "LockCardEnableToggle", "LockCardFrequencySlider", "LockCardRepeatsSlider",
        "MindWipeEnableToggle", "MindWipeFrequencySlider",
        "RampCurvePicker",
    ];

    /// <summary>
    /// The controls that stay LIVE through the whole session, each because a named upstream control
    /// is deliberately unmarked or because this port's session never touches its document.
    ///
    /// <list type="bullet">
    /// <item><c>BouncingTextOpacitySlider</c> — <c>BouncingTextFeatureControl.xaml:125</c> carries no
    /// marker, even though <c>SessionEngine.ApplySessionSettings</c> writes
    /// <c>BouncingTextOpacity</c> at <c>:1248</c>. Upstream under-marks it and says which way to err
    /// (<c>SessionLock.cs:36-38</c>): the 42 sites are the specification, so this dial stays live and
    /// the discrepancy is recorded rather than silently corrected.</item>
    /// <item><c>BubblePopSizeSlider</c> — <c>BubblePopFeatureControl.xaml:119</c> unmarked, and the
    /// session writes no bubble size either.</item>
    /// <item><c>MindWipeVolumeSlider</c> — <c>MindWipeFeatureControl.xaml:104</c> unmarked, and rule
    /// 3 names audio volume in the never-lock list (<c>MainWindow.SessionFeatureLock.cs:39-42</c>).</item>
    /// <item><c>LockCardStrictToggle</c> — <c>LockCardFeatureControl.xaml:124</c> unmarked, and rule
    /// 3 names Strict Lock in the same list.</item>
    /// <item>The four Brain Drain controls — <c>ChkHighRefresh</c> is unmarked upstream
    /// (<c>Views/Controls/Studio/BrainDrainFeatureControl.xaml:216</c>, whose header says why at
    /// <c>:48-50</c>), and the other three are live here for a stronger reason: the brain-drain
    /// document is not one of the eleven a run borrows, so nothing overwrites it and nothing
    /// discards it.</item>
    /// <item>The Intensity Ramp's own dials — <c>RampRackPanel.xaml:24-26</c>: "CmbRampCurve is the
    /// ONLY SessionLock.Owned control here".</item>
    /// <item>The Scheduler — <c>Views/Controls/Studio/SchedulerRackPanel.xaml:45-47</c>: "NOTHING on
    /// this panel is SessionLock.Owned … the scheduler decides when a session begins, it prescribes
    /// no dose inside one".</item>
    /// <item>Haptics and the Loom — neither persists inside the eleven. Upstream marks its haptics
    /// master (<c>Views/Tabs/HapticsTabView.xaml:587</c>) to back a premium-rail refusal that
    /// refuses the same flip mid-session; this port has no such refusal to make decorative, so
    /// locking it would take a control away for nothing.</item>
    /// <item><b>The whole AUDIO row</b> — both volumes, the endpoint picker and the Test button.
    /// It fails BOTH halves of the rule, which is why it is the cleanest entry on this list.
    /// <c>audio.json</c> is not one of the eleven documents a run borrows
    /// (<see cref="ScriptedSessionDials"/>'s constructor), so nothing snapshots it, nothing writes
    /// it back, and a change made mid-session is the user's afterwards; and upstream classes
    /// volumes as COMFORT rather than dosage, naming audio volume in the never-lock list outright
    /// (<c>MainWindow/MainWindow.SessionFeatureLock.cs:39-42</c>,
    /// <c>Features/SessionLock.cs:21-38</c>). A scripted session does not get to move the user's
    /// master volume or re-route their headphones.</item>
    /// </list>
    /// </summary>
    private static readonly string[] LiveThroughout =
    [
        "BouncingTextOpacitySlider",
        "BubblePopSizeSlider",
        "MindWipeVolumeSlider",
        "LockCardStrictToggle",
        "BrainDrainEnableToggle", "BrainDrainIntensitySlider", "BrainDrainVolumeSlider",
        "BrainDrainHighRefreshToggle",
        "RampEnableToggle", "RampDurationSlider", "RampMultiplierSlider", "RampEndSessionToggle",
        "RampLinkSpiralToggle", "RampLinkPinkFilterToggle", "RampLinkFlashToggle",
        "SchedulerEnableToggle", "SchedulerStartTimeBox", "SchedulerDayMon",
        "HapticsEnableToggle", "LoomButton",
        "AudioMasterSlider", "AudioVideoSlider", "AudioDevicePicker", "AudioTestButton",
    ];

    private sealed record Boot(ApplicationHost Host, MainWindow Window, ManualScriptedClock Clock)
    {
        public ScriptedSessionRun Run => Window.Session.Scripted;

        public StudioPage Studio => (StudioPage)Window.PageFor(ShellRoutes.Studio);
    }

    private async Task<Boot> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-session-lock-" + Guid.NewGuid().ToString("N"));
        var clock = new ManualScriptedClock();
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            ScriptedClockFactory = () => clock,
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        Track(host!);

        var window = new MainWindow(host!);
        host!.BindUiDispatch(new AvaloniaUiDispatch());
        window.Show();
        window.UpdateLayout();
        return new Boot(host, window, clock);
    }

    /// <summary>
    /// The LOGICAL tree, not the visual one, and that is the point of the helper: fifteen of the
    /// rack's sixteen module panels are hidden at any moment, so a fact that could only see what is
    /// on screen would pass with every hidden dial still live — the exact hole upstream's own sweep
    /// walks the logical tree to close (<c>Features/SessionLock.cs:166-172</c>).
    /// </summary>
    private static Control Dial(StudioPage page, string name) =>
        page.GetLogicalDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no control named '{name}' on the Studio page");

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

    private static void OpenTheSessionsRow(MainWindow window)
    {
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowScriptedSession"));
    }

    private static T Descendant<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' in the mounted page");

    /// <summary>The four gestures a real start takes; nothing here reaches past the surface.</summary>
    private static void StartMorningDrift(MainWindow window)
    {
        Click(window, Descendant<RadioButton>(window, "SessionRowMorningDrift"));
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));
        Assert.True(window.Session.Scripted.Running);
    }

    // =====================================================================================
    //  which dials the lock owns, and which it deliberately does not
    // =====================================================================================

    /// <summary>
    /// The marker set itself, pinned by name. It is the port of the 42 attribute sites, and a dial
    /// added to this page later without a marker — or marked when upstream leaves its counterpart
    /// alone — reds here before any behaviour is exercised.
    /// </summary>
    [AvaloniaFact]
    public async Task TheOwnedDialsAreExactlyTheThirtyUpstreamMarks_AndNothingElseOnThePageIs()
    {
        var boot = await BootAsync();
        Click(boot.Window, boot.Window.FindControl<RadioButton>("DoorStudio")!);

        var marked = boot.Studio.GetLogicalDescendants()
            .OfType<Control>()
            .Where(c => c.Classes.Contains("session-owned"))
            .Select(c => c.Name ?? "(unnamed)")
            .ToList();

        Assert.Equal(OwnedDials.Order(StringComparer.Ordinal), marked.Order(StringComparer.Ordinal));

        // The other half of the same statement: none of the deliberately-live controls carries the
        // marker. Over-locking is a regression too (Features/SessionLock.cs:36-38).
        foreach (var name in LiveThroughout)
        {
            Assert.DoesNotContain("session-owned", Dial(boot.Studio, name).Classes);
        }

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  the lock itself
    // =====================================================================================

    /// <summary>
    /// A running session greys every dial it owns — INCLUDING the ones inside panels that were
    /// never opened — and leaves every comfort and safety control live.
    /// </summary>
    [AvaloniaFact]
    public async Task WhileASessionRunsTheOwnedDialsGoReadOnly_AndTheComfortAndSafetyOnesDoNot()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        // Before: everything is the user's.
        foreach (var name in OwnedDials)
        {
            Assert.True(Dial(boot.Studio, name).IsEnabled, $"{name} was already disabled before the session");
        }

        StartMorningDrift(window);

        // The SESSIONS row is the only panel that has ever been on screen in this test, so every
        // assertion below about Flash, Visuals, Bubble Pop and the rest is about a dial the user
        // has not looked at yet. A visual-tree sweep would have missed them.
        foreach (var name in OwnedDials)
        {
            Assert.False(Dial(boot.Studio, name).IsEnabled, $"{name} is still live during a session");
        }

        foreach (var name in LiveThroughout)
        {
            Assert.True(Dial(boot.Studio, name).IsEnabled, $"{name} was locked and should not have been");
        }

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// The lock SAYS WHY, in upstream's own sentence with this port's subject, and says nothing at
    /// all when there is nothing to explain. Upstream's rule: "a greyed-out control with no
    /// explanation reads as a bug" (<c>MainWindow.SessionFeatureLock.cs:104-106</c>).
    /// </summary>
    [AvaloniaFact]
    public async Task TheLockNamesTheSessionOnScreen_AndTheBannerIsGoneWhenNothingIsRunning()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        var banner = Descendant<Border>(window, "SessionLockBanner");
        var reason = Descendant<TextBlock>(window, "SessionLockReason");
        Assert.False(banner.IsVisible);
        Assert.Equal(string.Empty, reason.Text);

        StartMorningDrift(window);

        Assert.True(banner.IsVisible);
        Assert.Equal(
            "Morning Drift is running this. Its features and intensity are locked until the session ends.",
            reason.Text);

        // It survives the tick, because the tick repaints it from the same derivation.
        boot.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(banner.IsVisible);

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  it lifts on every end, and it is derived rather than latched
    // =====================================================================================

    /// <summary>
    /// END ONE: the session reaches its own duration. Nobody presses anything — the run's tick ends
    /// it (<c>Services/Session/SessionEngine.cs:512-517</c>) and the dials come back.
    /// </summary>
    [AvaloniaFact]
    public async Task TheLockLiftsWhenTheSessionCOMPLETES()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);
        StartMorningDrift(window);
        Assert.False(Dial(boot.Studio, "FlashEnableToggle").IsEnabled);

        // Morning Drift is 30 minutes. One minute short, it is still locked — which is what stops
        // this fact from passing on a lock that lifted at the first tick.
        boot.Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.True(boot.Run.Running);
        Assert.False(Dial(boot.Studio, "FlashEnableToggle").IsEnabled);

        boot.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(boot.Run.Running);

        foreach (var name in OwnedDials)
        {
            Assert.True(Dial(boot.Studio, name).IsEnabled, $"{name} is still locked after the session completed");
        }

        Assert.False(Descendant<Border>(window, "SessionLockBanner").IsVisible);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// END TWO: the user stops it, through the confirmation the button really shows
    /// (<c>MainWindow/MainWindow.Presets.cs:1903-1906</c>).
    /// </summary>
    [AvaloniaFact]
    public async Task TheLockLiftsWhenTheUserSTOPSIt()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);
        StartMorningDrift(window);
        boot.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.False(Dial(boot.Studio, "SpiralOpacitySlider").IsEnabled);

        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));
        Assert.False(boot.Run.Running);

        foreach (var name in OwnedDials)
        {
            Assert.True(Dial(boot.Studio, name).IsEnabled, $"{name} is still locked after the session was stopped");
        }

        Assert.False(Descendant<Border>(window, "SessionLockBanner").IsVisible);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// END THREE, and the one a lock built on a latch would get wrong: the APP CLOSES with a
    /// session still running. <see cref="Session.SessionParticipant.FlushAsync"/> stops the run in
    /// the host's reserved pre-drain slot before a single document is flushed, so the user's own
    /// dials — not the session's — are what reach disk, and the page's paint follows the same
    /// <c>Ended</c> signal the other two ends use.
    /// </summary>
    [AvaloniaFact]
    public async Task TheLockLiftsONTHEAPPCLOSEPATH_BecauseTheDrainStopsTheRunFirst()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);
        StartMorningDrift(window);
        boot.Clock.Advance(TimeSpan.FromMinutes(4));
        Assert.True(boot.Run.Running);
        Assert.False(Dial(boot.Studio, "PinkFilterOpacitySlider").IsEnabled);

        await boot.Host.ShutdownAsync();

        Assert.False(boot.Run.Running);
        foreach (var name in OwnedDials)
        {
            Assert.True(Dial(boot.Studio, name).IsEnabled, $"{name} is still locked after the app-close drain");
        }
    }

    // =====================================================================================
    //  the second door: the rack row's right-click
    // =====================================================================================

    /// <summary>
    /// The rack's right-click quick-toggle is a shortcut for the panel's master box, so it is
    /// refused exactly when that box is — upstream's rule that every write path onto the prescribed
    /// dose runs through one refusal (<c>MainWindow.SessionFeatureLock.cs:232-241</c>).
    ///
    /// <para><b>The same gesture on the same page in the same session still works on Brain Drain</b>,
    /// whose document a scripted session never borrows. That is the inversion: a refusal that fired
    /// on every row would be over-locking, and this fact would still pass without it.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task ALockedRowsRightClickIsRefused_AndAnUnlockedRowsStillFlipsInTheSameSession()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);
        StartMorningDrift(window);

        var flashBefore = window.Session.Preset.Current.FlashEnabled;
        Click(window, Descendant<RadioButton>(window, "RowFlashImages"), MouseButton.Right);
        Assert.Equal(flashBefore, window.Session.Preset.Current.FlashEnabled);
        Assert.False(Dial(boot.Studio, "FlashEnableToggle").IsEnabled);

        var drainBefore = window.Session.BrainDrainPreset.Current.Enabled;
        Click(window, Descendant<RadioButton>(window, "RowBrainDrain"), MouseButton.Right);
        Assert.NotEqual(drainBefore, window.Session.BrainDrainPreset.Current.Enabled);

        // And once the session is over the refused row answers the same gesture again, because the
        // refusal was derived and not latched.
        Click(window, Descendant<RadioButton>(window, "RowScriptedSession"));
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));
        Assert.False(boot.Run.Running);

        Click(window, Descendant<RadioButton>(window, "RowFlashImages"), MouseButton.Right);
        Assert.NotEqual(flashBefore, window.Session.Preset.Current.FlashEnabled);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// The session keeps WRITING the dials it locked, and the locked control keeps SHOWING what it
    /// wrote. That is the whole reason greying is honest rather than obstructive — upstream states
    /// it in as many words (<c>MainWindow.SessionFeatureLock.cs:49-53</c>: "a disabled CheckBox or
    /// Slider still updates when set programmatically. The user watches the ramp happen, they just
    /// cannot overrule it").
    /// </summary>
    [AvaloniaFact]
    public async Task ALockedDialStillFOLLOWSTheSessionsOwnWrites()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        // The user's own flash rate, set before anything starts and nowhere near the session's.
        window.Session.Preset.Mutate(d => d.FlashesPerHour = 99);
        Assert.Equal(99, window.Session.Preset.Current.FlashesPerHour);

        StartMorningDrift(window);

        // morning_drift.session.json prescribes flashPerHour 12 and applies it at t=0
        // (ScriptedSessionDials.Apply, upstream Services/Session/SessionEngine.cs:1151-1156), so the
        // user's 99 is gone and the disabled slider is showing the session's number rather than a
        // frozen copy of the user's.
        var slider = (Slider)Dial(boot.Studio, "FlashFrequencySlider");
        Assert.False(slider.IsEnabled);
        Assert.Equal(12, window.Session.Preset.Current.FlashesPerHour);
        Assert.Equal(12, (int)Math.Round(slider.Value));

        // And the promise the confirmation made is kept at the end: the user's 99 comes back, on a
        // slider that is live again.
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));
        Assert.False(boot.Run.Running);
        Assert.True(slider.IsEnabled);
        Assert.Equal(99, (int)Math.Round(slider.Value));

        await boot.Host.ShutdownAsync();
    }
}
