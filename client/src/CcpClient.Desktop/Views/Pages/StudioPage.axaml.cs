using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Scheduling;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The Studio rack.
///
/// <para>Hop 2 of the Loom route (wpf-surface-reachability.md §4, §8.4 @ 7527243e7 verified live):
/// rack row <c>Spiral Overlay</c> -> module panel -> <c>THE LOOM — weave your own spiral</c>
/// -> <see cref="LoomLaunch"/>. A row selects a module and opens NO window
/// (<c>MainWindow.Presets.cs:976-978,1009</c> — "Navigation tiles still navigate to the ONE
/// existing entry, never launch"); only the button on the destination page launches, which is
/// WPF's one-entry rule (<c>MainWindow.Presets.cs:1007</c>).</para>
///
/// <para><b>The second gesture arrives.</b> The <c>Flash Images</c> row carries the
/// rack grammar in full for the first time in the port: left-click opens its panel,
/// right-click quick-toggles the effect, and the dot reports what is really happening
/// (§8.3, and §9 D5/D6, which recorded both as gaps "because the port has nothing to wire
/// yet"). Every gesture on this page routes through ONE dispatch entry —
/// <see cref="SessionEngine.QuickToggle"/> — so the row's right-click and the panel's Enable
/// checkbox cannot drift into two behaviours (the A-004 one-command-path rule the retired
/// demonstrator card established).</para>
///
/// <para><b>The grammar generalises, and one of the rows is continuous.</b> Two more
/// rows land: <c>Subliminals</c>, whose module shipped with no way to switch it on at
/// all (D72), and <c>Pink Filter</c>, the port's first module with no schedule. Both go through
/// the same one dispatch entry, and neither needed a new gesture: the RACK grammar turns out to
/// be indifferent to whether a module is paced. Its <b>dot</b> is not — a continuous module's
/// <see cref="EffectDotState.Live"/> is a claim about the SCREEN rather than about a clock — but
/// that is decided inside the effect (see <see cref="OwnedSessionEffect.WorkIsRunning"/>) and
/// this page reads the same three states off the same property for all three modules.</para>
///
/// <para><b>The rack gets a SECOND GROUP, and the module in it draws nothing.</b> WPF's
/// rack has four groups (§8.3); this port had rows in one because every module it had ported was an
/// EFFECT that painted an overlay. <c>Intensity Ramp</c> is from TIMING, has no surface, and its
/// whole visible output is the opacity numbers on the Spiral Overlay and Pink Filter panels next to
/// it. The rack grammar needed nothing new for it — left-click opens, right-click quick-toggles
/// through the same one <see cref="SessionEngine.QuickToggle"/> entry — and its <b>panel is the
/// first on this page with no surface line at all</b>, because a sentence about where its pixels
/// went would be a sentence about a capability it deliberately never acquires.</para>
///
/// <para><b>A THIRD group, and the first two rows nobody can see.</b> <c>Mind Wipe</c> and
/// <c>Brain Drain (audio half)</c> are from IMMERSION and their output is sound. The rack grammar
/// again needed nothing new. Two things about them are new to this page. First, their closing notice
/// is not a claim this page makes: it renders the AUDIO CAPABILITY's own typed outcome, so what a
/// user reads about sound is what the operating system answered — including the peak level Windows
/// measured on this process's stream — rather than a sentence about a platform. Second, the Brain
/// Drain row is <b>half a row on purpose</b>: its title says so, its panel leads with the missing
/// desktop blur, and its arm result carries that absence on every healthy run. Its dot is
/// nonetheless <see cref="EffectDotState.Live"/> while its audio is running, because the dot is
/// scoped to what the row says it is — which is exactly why the title is not editable prose.</para>
/// </summary>
public partial class StudioPage : UserControl
{
    private readonly SessionParticipant _session;
    private readonly FlashImagesEffect _flash;
    private readonly SubliminalsEffect _subliminals;
    private readonly PinkFilterEffect _pinkFilter;
    private readonly SpiralOverlayEffect _spiral;
    private readonly BouncingTextEffect _bouncingText;
    private readonly IntensityRampEffect _ramp;
    private readonly MindWipeEffect _mindWipe;
    private readonly BrainDrainEffect _brainDrain;
    private readonly LockCardEffect _lockCard;
    private readonly MandatoryVideoEffect _mandatoryVideo;
    private readonly BubbleCountEffect _bubbleCount;
    private readonly BubblePopEffect _bubblePop;
    private readonly PopQuizEffect _popQuiz;
    private readonly VisualsDials _visuals;
    private readonly SessionScheduler _scheduler;
    private readonly Haptics.HapticParticipant _haptics;

    /// <summary>
    /// The APP-WIDE audio owner (<c>Audio/AudioParticipant.cs</c>), and it is what makes the Audio
    /// row possible at all: the volumes, the endpoint choice and the device seams used to exist only
    /// inside the DTRH host window, so no rack row could reach any of them. It arrives as its own
    /// constructor argument for the reason <see cref="_scheduler"/> and <see cref="_haptics"/> do —
    /// it is owned at APP lifetime and a session never borrows it.
    /// </summary>
    private readonly Audio.AudioParticipant _audio;

    /// <summary>
    /// The endpoints the picker is currently offering, index-aligned with its items after the
    /// leading "System default" entry — the <see cref="_spiralLibrary"/> shape, for the same reason:
    /// the list is the MACHINE's and changes under the app, so it is a session fact re-read on
    /// demand rather than a cached one.
    /// </summary>
    private IReadOnlyList<string> _audioDevices = [];

    /// <summary>Whether the endpoints have been enumerated at all this run. Null connectivity is
    /// not "absent" — see <see cref="AudioDialsNotices.DescribeChoice"/>.</summary>
    private bool _audioDevicesListed;

    /// <summary>Upstream's <c>_testingAudio</c> re-entrancy guard
    /// (<c>MainWindow/MainWindow.UiUpdates.cs:1063</c>), widened to cover the device switch as well
    /// because both gestures reach the same one native device.</summary>
    private bool _audioBusy;

    private string _audioTest = AudioDialsNotices.DescribeTestNotRun();

    /// <summary>The one scripted session, reached off the composed participant — never a second
    /// one built here, for the reason <c>MainWindow</c> gives about the engine: two runs would
    /// borrow the user's dials twice and give them back once.</summary>
    private readonly ScriptedSessionRun _scripted;
    private readonly SessionRecapLaunch _recap;

    /// <summary>
    /// Every dial on this page a running scripted session OWNS — the port of upstream's
    /// <c>Features/SessionLock.FindOwnedControls</c> sweep (<c>Features/SessionLock.cs:178-186</c>)
    /// over the port of its <c>SessionLock.Owned="True"</c> marker, which here is the
    /// <c>session-owned</c> style class in <c>StudioPage.axaml</c>.
    ///
    /// <para><b>A style class rather than a list of names in this file</b>, for upstream's own
    /// reason (<c>SessionLock.cs:13-19</c>): the marker has to live ON the control, "three
    /// characters from the thing a future author is already editing", or the list rots into a set
    /// of holes. Avalonia's <c>Classes</c> is the native declarative marker and needs no attached
    /// property to carry it.</para>
    ///
    /// <para><b>The LOGICAL tree, not the visual one</b>, and for upstream's stated reason
    /// (<c>SessionLock.cs:166-172</c>): this rack keeps fifteen of its sixteen module panels
    /// hidden at any moment, and a dial that is only unlocked because it happened to be hidden
    /// when the session started is exactly the hole the sweep exists to close.</para>
    ///
    /// <para>Swept ONCE, at construction, because unlike upstream's popups — rebuilt on every open
    /// — these panels are built with the page and live as long as it does. That is the same
    /// property upstream relies on for its own rack (<c>MainWindow.SessionFeatureLock.cs:345-348</c>),
    /// and it is why the lock is painted on session start and end rather than on reveal. The
    /// count is pinned by a headless fact, so a dial added later without the marker reds.</para>
    /// </summary>
    private readonly IReadOnlyList<Control> _sessionOwned;

    /// <summary>
    /// The library files the spiral picker is currently offering, index-aligned with its items
    /// after the leading "library default" entry. Rebuilt by the Refresh button and on every load,
    /// never cached across one — the folder is the user's and can change under the app, which is
    /// why upstream has a Refresh button at all
    /// (<c>Features/SpiralFeatureControl.xaml:180</c>).
    /// </summary>
    private IReadOnlyList<string> _spiralLibrary = [];

    private readonly List<(RadioButton Row, ScriptedSession Session)> _scriptedRows = [];
    private ScriptedSession? _scriptedSelection;
    private ScriptedConfirmIntent _scriptedConfirm;
    private string? _scriptedRefusal;

    /// <summary>
    /// Every session on disk, read ONCE — upstream's registry, which its rack re-reads from memory
    /// on every toolbar touch rather than from the filesystem
    /// (<c>MainWindow/MainWindow.SessionIO.cs:264-271</c>). Re-reading here would stat a folder per
    /// keystroke in the search box.
    /// </summary>
    private IReadOnlyList<ScriptedSession> _scriptedCatalogue = [];

    /// <summary>The difficulty bands still switched on — upstream's <c>_rackDifficulties</c>, all
    /// four by default (<c>MainWindow/MainWindow.SessionIO.cs:189-195</c>).</summary>
    private readonly HashSet<ScriptedSessionDifficulty> _scriptedBands =
        [.. Enum.GetValues<ScriptedSessionDifficulty>()];

    private ScriptedSessionSort _scriptedSort;
    private string _scriptedSearch = string.Empty;

    /// <summary>Set while the repaint is rebuilding rows, so the <c>IsChecked</c> it restores onto
    /// the selected row does not come back through <see cref="OnScriptedRowChecked"/> as a fresh
    /// pick — upstream's <c>_rackToolbarSyncing</c>, for its own stated reason: the toolbar's own
    /// change events must not "read a half-applied state back out and repaint against it"
    /// (<c>MainWindow/MainWindow.SessionIO.cs:201-203</c>).</summary>
    private bool _scriptedRepainting;

    private bool _syncing;

    /// <param name="loom">The one Loom launch path.</param>
    /// <param name="session">The one conditioning session.</param>
    /// <param name="scheduler">
    /// The one SCHEDULER, and it arrives as its own argument rather than off
    /// <paramref name="session"/> because it is not part of one: it is owned at APP lifetime by
    /// <c>Scheduling/SchedulerParticipant</c> and it drives the session from outside
    /// (<c>MainWindow/MainWindow.StartStop.cs:601-639</c>). Threading it through the session would
    /// have put an app-lifetime object inside a session-lifetime one, which is exactly the
    /// ownership confusion the rack comment on this page warned about for nine waves.
    /// </param>
    /// <param name="haptics">
    /// The one HAPTIC sink's owner, and it arrives as its own argument for the same reason
    /// <paramref name="scheduler"/> does: it is not part of a session. Upstream's is a static built
    /// at startup and torn down at exit (<c>App.xaml.cs:533</c>, <c>:2060</c>, <c>:4406</c>) that
    /// the engine never touches. It is reached here rather than rebuilt, so the switch this panel
    /// offers and the gate the composition root resolved are the same object.
    /// </param>
    /// <param name="recap">
    /// The ONE session-recap and session-history launch path
    /// (<see cref="SessionRecapLaunch"/>), built by the shell because the windows it opens are
    /// owned by the shell window and a page is not one — the same reason
    /// <paramref name="loom"/> arrives already built.
    /// </param>
    /// <param name="audio">
    /// The one app-wide audio owner, and it arrives as its own argument for the same reason
    /// <paramref name="scheduler"/> and <paramref name="haptics"/> do: it is not part of a session.
    /// Upstream's audio service is a field on the application, built once at startup
    /// (<c>App.xaml.cs:1798</c>) and outliving every window and every run. It is reached here rather
    /// than rebuilt, so the endpoint this panel chooses and the endpoint the app plays through are
    /// the same one device.
    /// </param>
    public StudioPage(LoomLaunch loom, SessionParticipant session, SessionScheduler scheduler,
        Haptics.HapticParticipant haptics, SessionRecapLaunch recap, Audio.AudioParticipant audio)
    {
        ArgumentNullException.ThrowIfNull(loom);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(haptics);
        ArgumentNullException.ThrowIfNull(recap);
        ArgumentNullException.ThrowIfNull(audio);
        InitializeComponent();
        _sessionOwned =
            [.. this.GetLogicalDescendants().OfType<Control>().Where(IsSessionOwnedMarker)];
        _recap = recap;
        _scheduler = scheduler;
        _haptics = haptics;
        _audio = audio;

        _session = session;
        _flash = session.Flash;
        _subliminals = session.Subliminals;
        _pinkFilter = session.PinkFilter;
        _spiral = session.Spiral;
        _bouncingText = session.BouncingText;
        _ramp = session.Ramp;
        _mindWipe = session.MindWipe;
        _brainDrain = session.BrainDrain;
        _lockCard = session.LockCard;
        _mandatoryVideo = session.MandatoryVideo;
        _bubbleCount = session.BubbleCount;
        _bubblePop = session.BubblePop;
        _popQuiz = session.PopQuiz;

        // NOT an effect, and never taken from session.Engine.Effects: the Visuals row is
        // the Flash Images module's DRAW dials, so this is the only field on this page that is not
        // an ISessionEffect. Everything the row does not have below — an enable, a dot, a quick
        // toggle, an arm result — is absent for that one reason.
        _visuals = session.Visuals;

        // Row selection swaps the panel in, exactly as WPF's rack drives its row state from
        // the RadioButton's own checked transitions rather than from the click handler
        // (StudioTabView.xaml.cs:664-665), so the panel can never drift out of step with the
        // selection.
        RowFlashImages.IsCheckedChanged += (_, _) => ApplySelection();
        RowSubliminals.IsCheckedChanged += (_, _) => ApplySelection();
        RowSpiralOverlay.IsCheckedChanged += (_, _) => ApplySelection();
        RowBouncingText.IsCheckedChanged += (_, _) => ApplySelection();
        RowPinkFilter.IsCheckedChanged += (_, _) => ApplySelection();
        RowIntensityRamp.IsCheckedChanged += (_, _) => ApplySelection();
        RowMindWipe.IsCheckedChanged += (_, _) => ApplySelection();
        RowBrainDrain.IsCheckedChanged += (_, _) => ApplySelection();
        RowLockCard.IsCheckedChanged += (_, _) => ApplySelection();
        RowMandatoryVideo.IsCheckedChanged += (_, _) => ApplySelection();
        RowBubbleCount.IsCheckedChanged += (_, _) => ApplySelection();
        RowBubblePop.IsCheckedChanged += (_, _) => ApplySelection();
        RowPopQuiz.IsCheckedChanged += (_, _) => ApplySelection();
        RowVisuals.IsCheckedChanged += (_, _) => ApplySelection();
        RowScheduler.IsCheckedChanged += (_, _) => ApplySelection();
        RowHaptics.IsCheckedChanged += (_, _) => ApplySelection();
        RowAudio.IsCheckedChanged += (_, _) => ApplySelection();
        RowScriptedSession.IsCheckedChanged += (_, _) => ApplySelection();

        // The rack row's second gesture (StudioTabView.xaml.cs:660 -> :1109-1133). On the ROW,
        // not on the dot: the dot is 8px and the gesture belongs to the whole entry (:658-659).
        // Right-click is NOT handled on the Spiral Overlay row — that row has no effect to
        // flip, which is WPF's own unhandled case (:659, "Rows with no Toggle fall through
        // unhandled"), and a fake toggle there would be worse than no gesture.
        AddQuickToggle(RowFlashImages, FlashImagesEffect.EffectId, FlashEnableToggle);
        AddQuickToggle(RowSubliminals, SubliminalsEffect.EffectId, SubliminalEnableToggle);
        AddQuickToggle(RowSpiralOverlay, SpiralOverlayEffect.EffectId, SpiralEnableToggle);
        AddQuickToggle(RowBouncingText, BouncingTextEffect.EffectId, BouncingTextEnableToggle);
        AddQuickToggle(RowPinkFilter, PinkFilterEffect.EffectId, PinkFilterEnableToggle);
        AddQuickToggle(RowIntensityRamp, IntensityRampEffect.EffectId, RampEnableToggle);
        AddQuickToggle(RowMindWipe, MindWipeEffect.EffectId, MindWipeEnableToggle);
        AddQuickToggle(RowBrainDrain, BrainDrainEffect.EffectId, BrainDrainEnableToggle);
        AddQuickToggle(RowLockCard, LockCardEffect.EffectId, LockCardEnableToggle);
        AddQuickToggle(RowMandatoryVideo, MandatoryVideoEffect.EffectId, MandatoryVideoEnableToggle);
        AddQuickToggle(RowBubbleCount, BubbleCountEffect.EffectId, BubbleCountEnableToggle);
        AddQuickToggle(RowBubblePop, BubblePopEffect.EffectId, BubblePopEnableToggle);
        AddQuickToggle(RowPopQuiz, PopQuizEffect.EffectId, PopQuizEnableToggle);

        // NO quick toggle on RowVisuals, and it is upstream's own unhandled case rather than an
        // omission: the gesture flips a module's enable, and this row has no enable to flip
        // (StudioTabView.xaml.cs:496 passes null where every other row passes a dot predicate, and
        // its panel carries no master box). WPF's rack lets exactly such rows fall through
        // unhandled (:659).

        // The Scheduler row DOES take the gesture, and it is the one row whose toggle
        // does not go through SessionEngine.QuickToggle: it has no ISessionEffect behind it, so
        // there is no module id to dispatch on. Upstream's entry does the same thing for the same
        // reason — its toggle is `() => FlipMasterCheckBox(PanelScheduler?.Inner.ChkEnabled)`
        // (StudioTabView.xaml.cs:537), and its comment says why in as many words: "neither drives
        // a service directly … so the honest quick-toggle is the panel's own enable box" (:532-534).
        RowScheduler.AddHandler(
            PointerReleasedEvent,
            (_, e) => OnSchedulerRowPointerReleased(e),
            RoutingStrategies.Tunnel);

        // The Haptics row takes the gesture too, and it is the ONE row on this page whose
        // quick-toggle can be REFUSED. Upstream's entry flips the panel's own master box precisely
        // so the refusal runs: "Flip the page's own master box so MainWindow.ChkHapticsEnabled_Changed
        // runs - including the premium gate that reverts the box for a free account"
        // (StudioTabView.xaml.cs:521-525). Its rack then re-reads the dot one beat later because
        // "a refusal can undo the write (the haptics premium gate flips IsChecked back)" (:1121-1124).
        // Here the refusal happens INSIDE the request rather than after it, so nothing is written
        // and there is nothing to undo — see HapticParticipant.RequestEnable.
        RowHaptics.AddHandler(
            PointerReleasedEvent,
            (_, e) => OnHapticsRowPointerReleased(e),
            RoutingStrategies.Tunnel);

        FlashEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(FlashEnableToggle, _flash, FlashImagesEffect.EffectId);
        SubliminalEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(SubliminalEnableToggle, _subliminals, SubliminalsEffect.EffectId);
        PinkFilterEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(PinkFilterEnableToggle, _pinkFilter, PinkFilterEffect.EffectId);
        SpiralEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(SpiralEnableToggle, _spiral, SpiralOverlayEffect.EffectId);

        BouncingTextEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(BouncingTextEnableToggle, _bouncingText, BouncingTextEffect.EffectId);
        RampEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(RampEnableToggle, _ramp, IntensityRampEffect.EffectId);
        MindWipeEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(MindWipeEnableToggle, _mindWipe, MindWipeEffect.EffectId);
        BrainDrainEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(BrainDrainEnableToggle, _brainDrain, BrainDrainEffect.EffectId);
        LockCardEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(LockCardEnableToggle, _lockCard, LockCardEffect.EffectId);
        MandatoryVideoEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(MandatoryVideoEnableToggle, _mandatoryVideo, MandatoryVideoEffect.EffectId);
        BubbleCountEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(BubbleCountEnableToggle, _bubbleCount, BubbleCountEffect.EffectId);
        BubblePopEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(BubblePopEnableToggle, _bubblePop, BubblePopEffect.EffectId);
        PopQuizEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(PopQuizEnableToggle, _popQuiz, PopQuizEffect.EffectId);

        // The scheduler's own enable. It does NOT route through SessionEngine.QuickToggle for the
        // same reason its row's right-click does not: there is no module to arm.
        SchedulerEnableToggle.IsCheckedChanged += (_, _) => OnSchedulerEnableToggled();

        // The haptics master box. It does NOT write and then check: it asks the gate and writes only
        // if allowed, which is upstream's order (MainWindow/MainWindow.Haptics.cs:489-500 returns
        // BEFORE HapticCfg.Enabled = isEnabled).
        HapticsEnableToggle.IsCheckedChanged += (_, _) => OnHapticsEnableToggled();

        // The two provider boxes. They are a SET rather than a choice — upstream connects every
        // enabled provider concurrently (Services/Haptics/Core/HapticDeviceManager.cs:101-125) — so
        // both write through the same one-argument entry and neither un-ticks the other.
        HapticsLovenseToggle.IsCheckedChanged +=
            (_, _) => OnHapticsRouteToggled(Haptics.HapticProviderRoute.Lovense, HapticsLovenseToggle);
        HapticsButtplugToggle.IsCheckedChanged +=
            (_, _) => OnHapticsRouteToggled(Haptics.HapticProviderRoute.Buttplug, HapticsButtplugToggle);

        // Upstream writes BOTH times from ONE LostFocus handler and saves once
        // (Features/SchedulerFeatureControl.xaml.cs:71-79, wired at .xaml:49 and :58), so both
        // boxes land on the same entry here. LostFocus rather than TextChanged is upstream's own
        // choice and it is the right one: writing on every keystroke would put "1", then "16",
        // then "16:" through the parser, and "1" parses successfully as ONE DAY.
        SchedulerStartTimeBox.LostFocus += (_, _) => OnSchedulerTimesCommitted();
        SchedulerEndTimeBox.LostFocus += (_, _) => OnSchedulerTimesCommitted();

        AddSchedulerDay(SchedulerDayMon, DayOfWeek.Monday);
        AddSchedulerDay(SchedulerDayTue, DayOfWeek.Tuesday);
        AddSchedulerDay(SchedulerDayWed, DayOfWeek.Wednesday);
        AddSchedulerDay(SchedulerDayThu, DayOfWeek.Thursday);
        AddSchedulerDay(SchedulerDayFri, DayOfWeek.Friday);
        AddSchedulerDay(SchedulerDaySat, DayOfWeek.Saturday);
        AddSchedulerDay(SchedulerDaySun, DayOfWeek.Sunday);

        // Strict is one of the module's own dials, not its enable, so it writes through the module
        // the way the ramp's switches do rather than through the rack's quick-toggle.
        LockCardStrictToggle.IsCheckedChanged += (_, _) => OnLockCardStrictToggled();

        // Brain Drain's high-refresh switch is one of the module's own dials, not its enable, so it
        // writes through the module like the ramp's switches do rather than through the rack's
        // quick-toggle.
        BrainDrainHighRefreshToggle.IsCheckedChanged += (_, _) =>
            OnBrainDrainHighRefreshToggled();

        // The ramp's remaining switches are its own dials, not the module's enable, so they write
        // through the module rather than through the rack's quick-toggle. Each one goes to a setter
        // that mutates and re-evaluates, which is the same shape the opacity sliders above use and
        // WPF's own per-control write-then-save (Features/IntensityRampFeatureControl.xaml.cs:115-150).
        RampEndSessionToggle.IsCheckedChanged += (_, _) =>
            OnRampSwitch(RampEndSessionToggle, _ramp.Preset.EndSessionOnComplete, _ramp.SetEndSessionOnComplete);
        RampLinkSpiralToggle.IsCheckedChanged += (_, _) =>
            OnRampSwitch(RampLinkSpiralToggle, _ramp.Preset.LinkSpiralOpacity, _ramp.SetLinkSpiralOpacity);
        RampLinkPinkFilterToggle.IsCheckedChanged += (_, _) =>
            OnRampSwitch(
                RampLinkPinkFilterToggle, _ramp.Preset.LinkPinkFilterOpacity, _ramp.SetLinkPinkFilterOpacity);
        RampLinkFlashToggle.IsCheckedChanged += (_, _) =>
            OnRampSwitch(RampLinkFlashToggle, _ramp.Preset.LinkFlashOpacity, _ramp.SetLinkFlashOpacity);
        RampCurvePicker.SelectionChanged += (_, _) => OnRampCurvePicked();

        // THE TWO PICKERS. Both were backends with nothing in front of them: SpiralLibrary resolved
        // a file nobody could choose, and PinkFilterColour parsed a hex string nobody could write.
        // The tint's items never change, so they are built once here; the spiral's are the contents
        // of a folder and are rebuilt whenever the user asks or the dials reload.
        PinkFilterTintPicker.ItemsSource =
            StudioPickerNotices.TintPalette.Select(entry => entry.Label).ToList();
        PinkFilterTintPicker.SelectionChanged += (_, _) => OnTintPicked();
        PinkFilterTintResetButton.Click += (_, _) => OnTintReset();
        SpiralPicker.SelectionChanged += (_, _) => OnSpiralPicked();
        SpiralRefreshButton.Click += (_, _) => OnSpiralLibraryRefreshed();

        // THE AUDIO ROW'S FOUR CONTROLS. The picker is NOT populated here: see
        // EnsureAudioDevicesListed for when the endpoints are enumerated and why it is not at
        // construction. Both buttons hand off to an awaitable so the native call they make is off
        // this thread — upstream moved the same work off ITS UI thread because a wedged endpoint
        // blocks for many seconds or forever (MainWindow/MainWindow.UiUpdates.cs:1072-1076, #686).
        AudioDevicePicker.SelectionChanged += (_, _) => OnAudioDevicePicked();
        AudioDeviceRefreshButton.Click += (_, _) => OnAudioDevicesRefreshed();
        AudioTestButton.Click += (_, _) => _ = TestAudioAsync();

        OnSliderMoved(FlashFrequencySlider, OnFrequencyMoved);
        OnSliderMoved(FlashImagesSlider, OnImagesPerFlashMoved);
        OnSliderMoved(SubliminalFrequencySlider, OnSubliminalFrequencyMoved);
        OnSliderMoved(PinkFilterOpacitySlider, OnPinkFilterOpacityMoved);
        OnSliderMoved(SpiralOpacitySlider, OnSpiralOpacityMoved);
        OnSliderMoved(BouncingTextSpeedSlider, OnBouncingTextSpeedMoved);
        OnSliderMoved(BouncingTextSizeSlider, OnBouncingTextSizeMoved);
        OnSliderMoved(BouncingTextOpacitySlider, OnBouncingTextOpacityMoved);
        OnSliderMoved(RampDurationSlider, OnRampDurationMoved);
        OnSliderMoved(RampMultiplierSlider, OnRampMultiplierMoved);
        OnSliderMoved(MindWipeFrequencySlider, OnMindWipeFrequencyMoved);
        OnSliderMoved(MindWipeVolumeSlider, OnMindWipeVolumeMoved);
        OnSliderMoved(BrainDrainIntensitySlider, OnBrainDrainIntensityMoved);
        OnSliderMoved(BrainDrainVolumeSlider, OnBrainDrainVolumeMoved);
        OnSliderMoved(LockCardFrequencySlider, OnLockCardFrequencyMoved);
        OnSliderMoved(LockCardRepeatsSlider, OnLockCardRepeatsMoved);
        OnSliderMoved(MandatoryVideoFrequencySlider, OnMandatoryVideoFrequencyMoved);
        OnSliderMoved(MandatoryVideoMaxLengthSlider, OnMandatoryVideoMaxLengthMoved);
        OnSliderMoved(BubbleCountFrequencySlider, OnBubbleCountFrequencyMoved);
        OnSliderMoved(BubbleCountDifficultySlider, OnBubbleCountDifficultyMoved);
        OnSliderMoved(BubblePopFrequencySlider, OnBubblePopFrequencyMoved);
        OnSliderMoved(BubblePopSizeSlider, OnBubblePopSizeMoved);
        OnSliderMoved(BubblePopSpeedSlider, OnBubblePopSpeedMoved);
        OnSliderMoved(PopQuizFrequencySlider, OnPopQuizFrequencyMoved);
        OnSliderMoved(AudioMasterSlider, OnAudioMasterMoved);
        OnSliderMoved(AudioVideoSlider, OnAudioVideoMoved);
        OnSliderMoved(VisualsScaleSlider, OnVisualsScaleMoved);
        OnSliderMoved(VisualsOpacitySlider, OnVisualsOpacityMoved);
        OnSliderMoved(VisualsDurationSlider, OnVisualsDurationMoved);

        _session.Engine.Changed += OnSessionChanged;
        // The scheduler's state moves on a pool thread, 30 seconds at a time, with nobody
        // touching the app. It raises through the same EffectSignal every module uses, so this
        // handler may touch controls directly.
        _scheduler.Changed += OnSessionChanged;
        _flash.Fired += _ => Refresh();
        _subliminals.Fired += _ => Refresh();
        _mindWipe.Fired += _ => Refresh();
        _brainDrain.Fired += _ => Refresh();

        // BOTH of this module's signals, and both matter: Shown is a card appearing, Resolved is the
        // user answering it OR walking away from it. A page that only repainted on the first would
        // leave "asking you now" on screen after the card was gone.
        _lockCard.Shown += _ => Refresh();
        _lockCard.Resolved += _ => Refresh();
        _mandatoryVideo.Fired += _ => Refresh();

        // ALL THREE of this module's signals, and each is a different moment the panel must repaint
        // at: a clip going up, the question going up, and the game ending however it ended. A page
        // that only repainted on the first would leave "counting now" on screen through the whole
        // question and after the game was over.
        _bubbleCount.Started += _ => Refresh();
        _bubbleCount.Asked += _ => Refresh();
        _bubbleCount.Resolved += _ => Refresh();

        // BOTH of this module's signals, for the reason the Lock Card's pair is taken: Shown is a
        // question appearing, Resolved is the user answering it OR walking away from it. A page that
        // only repainted on the first would leave "asking you now" on screen after the card was gone.
        _popQuiz.Shown += _ => Refresh();
        _popQuiz.Resolved += _ => Refresh();

        LoomButton.Click += (_, _) => loom.Launch();

        // THE SCRIPTED SESSION. The rack is built once, from the four files beside the binary:
        // this build has no editor and no import, so the SET cannot change while the app runs, and
        // upstream's repaint-on-every-change (MainWindow/MainWindow.SessionIO.cs:212) has nothing
        // here to react to. The moment one of those arrives, this becomes a repaint.
        _scripted = session.Scripted;
        BuildScriptedSessionRack();
        ScriptedSessionStartButton.Click += (_, _) => OnScriptedStartClicked();
        ScriptedSessionPauseButton.Click += (_, _) => OnScriptedPauseClicked();
        ScriptedSessionConfirmButton.Click += (_, _) => OnScriptedConfirmClicked();
        ScriptedSessionCancelButton.Click += (_, _) => OnScriptedCancelClicked();

        // The run's three notifications arrive already marshalled (it was composed with the
        // session's EffectSignal), so these handlers touch controls directly like every other
        // handler on this page. PhaseChanged fires for phase 0 at START, which is what paints the
        // first readout without this page owning a clock of its own.
        _scripted.ProgressUpdated += _ => RenderScriptedSession();
        _scripted.PhaseChanged += (_, _) => RenderScriptedSession();
        // An ending session hands the dials back, so the panels above have to re-read them: that
        // is the whole visible half of the restore, and it is the same repaint a quick-toggle does.
        _scripted.Ended += _ => OnSessionChanged();

        // THE RECAP, AND IT IS DRIVEN OFF THE LOG RATHER THAN OFF THE END OF THE SESSION.
        // Upstream states the reason where it makes the same choice: "SessionEngine raises LogReady
        // AFTER it fires SessionCompleted, so OnSessionCompleted handles XP awarding only - the
        // dialog is shown from this hook" (MainWindow/MainWindow.xaml.cs:373-378). A recap wired to
        // `Ended` would be racing the write of the very log it renders, and would render the log of
        // a session that had not been finalized. It fires for COMPLETION AND ABORT alike, because
        // the log does (Services/Session/SessionLogService.cs:95-101 — the persist is inside an if,
        // the raise is outside it).
        session.MediaLog.LogReady += log => _recap.ShowRecap(log);
        ScriptedSessionHistoryButton.Click += (_, _) => _recap.ShowHistory();
        ScriptedSessionEditButton.Click += (_, _) => OnScriptedEditClicked();

        LoadDialsFromPreset();
        Refresh();
    }

    /// <summary>The Flash Images module's dot state, as the row is currently painting it
    /// (public so a test reads the RENDERED claim rather than the model it came from).</summary>
    public EffectDotState RenderedFlashDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Subliminals row's dot, same reason.</summary>
    public EffectDotState RenderedSubliminalDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Pink Filter row's dot, same reason. This is the one that had to be earned:
    /// a continuous module is <c>Live</c> only while its surface is really up.</summary>
    public EffectDotState RenderedPinkFilterDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Spiral Overlay row's dot, same reason — and the one with the strictest rule
    /// behind it: a MOVING module is <c>Live</c> only while its surface is up AND still changing
    /// (see <see cref="SpiralSurfacePresenter.Running"/>).</summary>
    public EffectDotState RenderedSpiralDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Bouncing Text row's dot, and the one with the NEWEST rule: Live means
    /// the operating system's own copy of the surface still carries the frame's opaque ink
    /// (<see cref="BouncingTextSurfacePresenter.Running"/>).</summary>
    public EffectDotState RenderedBouncingTextDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Intensity Ramp row's dot, same reason — and the one whose <c>Live</c> is a claim
    /// about neither a clock nor a screen but about CUSTODY: the module is running exactly while it
    /// holds dials belonging to other modules and owes them back (see
    /// <see cref="IntensityRampEffect"/>).</summary>
    public EffectDotState RenderedRampDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Mind Wipe row's dot, same reason — and the first on this page whose <c>Live</c>
    /// depends on a fact from OUTSIDE this process: a firing on the clock AND an audio render session
    /// the operating system confirms belongs to us (see <see cref="AudioCueEffect"/>).</summary>
    public EffectDotState RenderedMindWipeDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Brain Drain row's dot. Same rule as Mind Wipe's, and it is deliberately
    /// <see cref="EffectDotState.Live"/> while the audio half runs even though the row is half of its
    /// upstream: the dot is scoped to what the ROW is, and this row's title says what it is.</summary>
    public EffectDotState RenderedBrainDrainDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Lock Card row's dot — the sixth meaning, and the first that is a claim on the
    /// USER rather than on anything this process owns: a firing on the clock, a desktop this process
    /// can put a window on, AND, while a card is up, the operating system's own confirmation that the
    /// card holds the foreground and the keyboard focus (see
    /// <see cref="LockCardEffect"/>).</summary>
    public EffectDotState RenderedLockCardDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Mandatory Video row's dot — the SEVENTH meaning, MOTION: a firing on the clock, a
    /// display the operating system confirms, AND, while a clip is up, the operating system's own
    /// copy of the surface having CHANGED when a different picture was handed over. The first of the
    /// seven that can be false while every call this process made succeeded (see
    /// <see cref="MandatoryVideoEffect.WorkIsRunning"/>).</summary>
    public EffectDotState RenderedMandatoryVideoDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Bubble Count row's dot state, as the row is currently painting it. It carries
    /// TWO of the dot's landed meanings, one per phase of a game (see
    /// <see cref="BubbleCountEffect.WorkIsRunning"/>).</summary>
    public EffectDotState RenderedBubbleCountDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Bubble Pop row's dot. <c>Live</c> means the field is running AND
    /// either nothing of its own is on screen yet or the operating system says it routes a click
    /// at one of its bubbles TO this app — the Lock Card row's DEMAND with the hit test in place of the
    /// foreground (<see cref="BubblePopSurfacePresenter.Running"/>).</summary>
    public EffectDotState RenderedBubblePopDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Pop Quiz row's dot. The LOCK CARD's rule exactly — a firing on the clock, a
    /// desktop this process can put a window on, and, while a card of ITS OWN is up, the operating
    /// system's own confirmation that the card holds the input
    /// (<see cref="PopQuizEffect.WorkIsRunning"/>). The <c>of MINE</c> qualifier is what stops this
    /// row darkening for a Lock Card or a Bubble Count question on the shared capability.</summary>
    public EffectDotState RenderedPopQuizDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Scheduler row's dot — the first on this page that is not a claim about
    /// a module at all. <c>Live</c> means the enable is on, a tick is REALLY on the clock, and the
    /// LOCAL clock is inside the user's window right now; <c>Off</c> covers both "switched off" and
    /// "cannot act yet" (see <see cref="SessionScheduler.Dot"/>).</summary>
    public EffectDotState RenderedSchedulerDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Haptics row's dot. Two reachable values, never three:
    /// <see cref="EffectDotState.Live"/> would claim something is being sent and nothing is
    /// (<see cref="Haptics.HapticParticipant.Dot"/>).</summary>
    public EffectDotState RenderedHapticsDot { get; private set; } = EffectDotState.Off;

    /// <summary>
    /// The tint the Pink Filter panel is currently reporting, as text.
    ///
    /// <para>The module's colour has no picker yet, so the panel REPORTS the tint in force instead
    /// of offering a control that does nothing — WPF's own swatch is beside a picker
    /// (<c>Features/PinkFilterFeatureControl.xaml.cs:217-220</c>) and the port has the swatch
    /// without the picker. The words name the fallback explicitly when the fallback is what is in
    /// use, because "hot pink" being a default rather than a choice is exactly the kind of thing a
    /// user should not have to guess.</para>
    /// </summary>
    internal static string DescribeTint(PinkFilterTint tint, bool userPicked) =>
        userPicked
            ? $"Tint #{tint.Red:X2}{tint.Green:X2}{tint.Blue:X2} at {tint.OpacityPercent}% opacity."
            : $"Tint #{tint.Red:X2}{tint.Green:X2}{tint.Blue:X2} (the default) at {tint.OpacityPercent}% opacity.";

    private static void OnSliderMoved(Slider slider, Action handler) =>
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                handler();
            }
        };

    private void AddSchedulerDay(CheckBox box, DayOfWeek day) =>
        box.IsCheckedChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            var target = box.IsChecked == true;
            if (target == ScheduleWindow.IsDayActive(_scheduler.Preset, day))
            {
                return;
            }

            _scheduler.SetDay(day, target);
            Refresh();
        };

    /// <param name="row">The rack row the gesture lives on.</param>
    /// <param name="effectId">The module the gesture dispatches to.</param>
    /// <param name="enable">
    /// The panel's own master box for that module, and it is here for the SESSION FEATURE LOCK.
    /// The right-click is a shortcut for this checkbox, so a locked checkbox with a live shortcut
    /// beside it is a bypass — precisely what upstream's <c>RefuseIfSessionFeatureLocked</c> guards
    /// against ("Call from any handler that is about to change the prescribed dose",
    /// <c>MainWindow/MainWindow.SessionFeatureLock.cs:232-241</c>), and why upstream's own rack
    /// quick-toggle flips the panel's master box rather than calling the service
    /// (<c>StudioTabView.xaml.cs:521-525</c>). Passing the box rather than a second list of module
    /// ids means the refusal and the greying are decided by the SAME marker, so they cannot drift.
    /// </param>
    private void AddQuickToggle(RadioButton row, string effectId, CheckBox enable) =>
        row.AddHandler(
            PointerReleasedEvent,
            (_, e) => OnRowPointerReleased(e, effectId, enable),
            RoutingStrategies.Tunnel);

    /// <summary>The Bouncing Text speed dial. WPF's slider writes the setting and the next start
    /// reads it; here a live run picks it up through the module's own Refresh.</summary>
    private void OnBouncingTextSpeedMoved()
    {
        if (_syncing)
        {
            return;
        }

        _bouncingText.SetSpeed((int)Math.Round(BouncingTextSpeedSlider.Value));
        _ = _session.BouncingTextPreset.Save();
        Refresh();
    }

    /// <summary>The size dial, same route.</summary>
    private void OnBouncingTextSizeMoved()
    {
        if (_syncing)
        {
            return;
        }

        _bouncingText.SetSizePercent((int)Math.Round(BouncingTextSizeSlider.Value));
        _ = _session.BouncingTextPreset.Save();
        Refresh();
    }

    /// <summary>The opacity dial. This one reaches a LIVE surface: it is the uniform multiplier over
    /// the glyph's own per-pixel alpha, which is WPF's own structure (the text element's
    /// <c>Opacity</c>, <c>BouncingTextService.cs:975</c>).</summary>
    private void OnBouncingTextOpacityMoved()
    {
        if (_syncing)
        {
            return;
        }

        _bouncingText.SetOpacityPercent((int)Math.Round(BouncingTextOpacitySlider.Value));
        _ = _session.BouncingTextPreset.Save();
        Refresh();
    }

    /// <summary>
    /// The Spiral Overlay opacity slider writes the setting, re-applies it to whatever is already on
    /// screen and saves — the same order the pink slider above it uses, and WPF's own for this
    /// module (write, then <c>RefreshOverlays()</c>, reconciled at <c>OverlayService.cs:446</c>
    /// -&gt; <c>UpdateSpiralOpacity</c>). The re-apply goes through the module rather than the
    /// surface so the arm state and the dot move with it.
    /// </summary>
    /// <summary>
    /// Fill the spiral picker from the folder and select what the document says is in force.
    ///
    /// <para>Item 0 is always "let the library choose" — the empty path — and every later item is
    /// one file, so the selected index maps to <see cref="_spiralLibrary"/> minus one. A configured
    /// path whose file is no longer there falls back to item 0, which is exactly what
    /// <see cref="SpiralLibrary.Resolve"/> does with the same string
    /// (<c>Services/Notifications/OverlayService.cs:302-304</c>): the picker must not claim a file
    /// the module would not draw.</para>
    /// </summary>
    private void LoadSpiralPicker(string configuredPath)
    {
        _spiralLibrary = SpiralLibrary.ListFolder(_session.SpiralsFolder);

        var items = new List<string> { StudioPickerNotices.SpiralLibraryDefault };
        items.AddRange(_spiralLibrary.Select(StudioPickerNotices.SpiralLabel));
        SpiralPicker.ItemsSource = items;

        var index = string.IsNullOrWhiteSpace(configuredPath)
            ? -1
            : _spiralLibrary.ToList().FindIndex(
                path => string.Equals(path, configuredPath, StringComparison.Ordinal));
        SpiralPicker.SelectedIndex = index < 0 ? 0 : index + 1;
    }

    /// <summary>
    /// The user picked a spiral. Upstream's <c>SelectSpiral</c> writes the path, saves and
    /// reconciles the live overlay (<c>Features/SpiralFeatureControl.xaml.cs:378-400</c>); the
    /// module owns the first and third of those and this owns the save, exactly as the opacity
    /// slider beside it does.
    /// </summary>
    private void OnSpiralPicked()
    {
        if (_syncing)
        {
            return;
        }

        var index = SpiralPicker.SelectedIndex;
        var path = index >= 1 && index - 1 < _spiralLibrary.Count ? _spiralLibrary[index - 1] : string.Empty;
        _spiral.SetSpiralPath(path);
        _ = _session.SpiralPreset.Save();
        Refresh();
    }

    /// <summary>
    /// Upstream's <c>⟳ Refresh</c> (<c>Features/SpiralFeatureControl.xaml:176-184</c> -&gt;
    /// <c>RefreshLibrary()</c>): re-read the folder, because the user just dropped a file into it.
    /// It changes no dial — the selection is re-derived from the document, so a file that vanished
    /// while the app was running is reflected here rather than silently kept.
    /// </summary>
    private void OnSpiralLibraryRefreshed()
    {
        _syncing = true;
        try
        {
            LoadSpiralPicker(_session.SpiralPreset.Current.Path);
        }
        finally
        {
            _syncing = false;
        }

        Refresh();
    }

    /// <summary>
    /// The user picked a tint. Upstream's <c>BtnChooseColor_Click</c> without the Win32 dialog
    /// (<c>Features/PinkFilterFeatureControl.xaml.cs:176-193</c>): write the hex, save, re-tint what
    /// is on screen. Palette entry 0 carries the empty string, so picking "Hot pink (the default)"
    /// and pressing Reset land on the same stored value — which is upstream's, where the two
    /// handlers also differ only in the string they write.
    /// </summary>
    private void OnTintPicked()
    {
        if (_syncing)
        {
            return;
        }

        var index = SafePickerIndex(PinkFilterTintPicker.SelectedIndex, StudioPickerNotices.TintPalette.Count);
        _pinkFilter.SetTintColour(StudioPickerNotices.TintPalette[index].Hex);
        _ = _session.PinkFilterPreset.Save();
        Refresh();
    }

    /// <summary>
    /// Upstream's <c>BtnResetColor_Click</c> (<c>:195-202</c>): the empty string, which is stored
    /// as "use the default tint". It drives the PICKER rather than the document directly, so the
    /// control and the setting cannot disagree about what just happened — the same reason the
    /// scheduler's boxes reload from the document rather than from the click.
    /// </summary>
    private void OnTintReset() => PinkFilterTintPicker.SelectedIndex = 0;

    /// <summary>A <see cref="ComboBox"/> with nothing selected reports -1, which no palette has an
    /// entry for; the default is entry 0, for the reason
    /// <see cref="StudioPickerNotices.TintIndexOf"/> gives.</summary>
    private static int SafePickerIndex(int index, int count) =>
        index >= 0 && index < count ? index : 0;

    private void OnSpiralOpacityMoved()
    {
        if (_syncing)
        {
            return;
        }

        var value = (int)Math.Round(SpiralOpacitySlider.Value);
        _spiral.SetOpacityPercent(value);
        _ = _session.SpiralPreset.Save();
        Refresh();
    }

    private void ApplySelection()
    {
        var flashOpen = RowFlashImages.IsChecked == true;
        var subliminalOpen = RowSubliminals.IsChecked == true;
        var spiralOpen = RowSpiralOverlay.IsChecked == true;
        var bouncingTextOpen = RowBouncingText.IsChecked == true;
        var pinkOpen = RowPinkFilter.IsChecked == true;
        var rampOpen = RowIntensityRamp.IsChecked == true;
        var mindWipeOpen = RowMindWipe.IsChecked == true;
        var brainDrainOpen = RowBrainDrain.IsChecked == true;
        var lockCardOpen = RowLockCard.IsChecked == true;
        var videoOpen = RowMandatoryVideo.IsChecked == true;
        var bubbleCountOpen = RowBubbleCount.IsChecked == true;
        var bubblePopOpen = RowBubblePop.IsChecked == true;
        var popQuizOpen = RowPopQuiz.IsChecked == true;
        var visualsOpen = RowVisuals.IsChecked == true;
        var schedulerOpen = RowScheduler.IsChecked == true;
        var hapticsOpen = RowHaptics.IsChecked == true;
        var audioOpen = RowAudio.IsChecked == true;
        var scriptedOpen = RowScriptedSession.IsChecked == true;
        ScriptedSessionModulePanel.IsVisible = scriptedOpen;
        SchedulerModulePanel.IsVisible = schedulerOpen;
        HapticsModulePanel.IsVisible = hapticsOpen;
        AudioModulePanel.IsVisible = audioOpen;
        if (audioOpen)
        {
            EnsureAudioDevicesListed();
        }

        VisualsModulePanel.IsVisible = visualsOpen;
        MandatoryVideoModulePanel.IsVisible = videoOpen;
        BubbleCountModulePanel.IsVisible = bubbleCountOpen;
        BubblePopModulePanel.IsVisible = bubblePopOpen;
        PopQuizModulePanel.IsVisible = popQuizOpen;
        FlashModulePanel.IsVisible = flashOpen;
        SubliminalModulePanel.IsVisible = subliminalOpen;
        SpiralModulePanel.IsVisible = spiralOpen;
        BouncingTextModulePanel.IsVisible = bouncingTextOpen;
        PinkFilterModulePanel.IsVisible = pinkOpen;
        RampModulePanel.IsVisible = rampOpen;
        MindWipeModulePanel.IsVisible = mindWipeOpen;
        BrainDrainModulePanel.IsVisible = brainDrainOpen;
        LockCardModulePanel.IsVisible = lockCardOpen;
        RackHint.IsVisible = !flashOpen && !subliminalOpen && !spiralOpen && !pinkOpen && !rampOpen
            && !mindWipeOpen && !brainDrainOpen && !lockCardOpen && !videoOpen && !bubbleCountOpen
            && !bubblePopOpen && !bouncingTextOpen && !visualsOpen && !schedulerOpen && !hapticsOpen
            && !scriptedOpen && !popQuizOpen && !audioOpen;
    }

    // =====================================================================================
    //  THE AUDIO ROW — the app-wide volumes, the endpoint and the test
    // =====================================================================================

    /// <summary>Upstream's extension set, in upstream's own order
    /// (<c>Services/MindWipeService.cs:162-165</c>, mirrored by
    /// <c>Effects/AudioCuePool.cs:67</c>).</summary>
    private static readonly string[] TestClipExtensions = [".mp3", ".wav", ".ogg"];

    /// <summary>
    /// Read the render endpoints ONCE per reveal-or-refresh and fill the picker.
    ///
    /// <para><b>Why not at construction.</b> This page and all of its panels are built during
    /// startup, so "on load" would mean "at every launch" — and a launch that builds a native audio
    /// context for a user who never opens this row and never plays a sound is the same unrequested
    /// claim on a shared resource that <c>AudioParticipant</c>'s phase 3 refuses. Upstream reads its
    /// list at the equivalent moment: inside <c>LoadSettings</c>, when the audio door's own state
    /// loads (<c>MainWindow/MainWindow.Settings.cs:140</c>).</para>
    ///
    /// <para><b>Listing is not opening.</b> <see cref="Audio.AudioParticipant.Devices"/> reads the
    /// backend's playback-device list; only <c>TryInit</c> brings a device up
    /// (<c>Audio/SoundFlowAudioBackend.cs:58-70</c> against <c>:73-110</c>). So the picker can offer
    /// endpoints without seizing one, and <c>DeviceInitAttempts</c> stays where it was.</para>
    /// </summary>
    private void EnsureAudioDevicesListed()
    {
        if (_audioDevicesListed)
        {
            return;
        }

        ListAudioDevices();
        Refresh();
    }

    private void ListAudioDevices()
    {
        _audioDevices = _audio.Devices();
        _audioDevicesListed = true;
        _syncing = true;
        try
        {
            AudioDevicePicker.ItemsSource =
                (string[])[AudioDialsNotices.SystemDefaultLabel, .. _audioDevices];
            SyncAudioPickerSelection();
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// Put the picker on the stored choice. A stored name that is NOT in the fresh enumeration
    /// selects entry 0, which is upstream's own behaviour
    /// (<c>MainWindow/MainWindow.UiUpdates.cs:1117-1124</c>, <c>SelectedItem = pick ?? devices[0]</c>)
    /// and is also what the arbitration really does with it
    /// (<c>Audio/SoundArbitration.cs:325-328</c>: absent name → typed fallback to the default). The
    /// difference from upstream is that the notice beside it SAYS so instead of leaving the user to
    /// discover their choice was dropped — and the setting itself is never rewritten here, so the
    /// choice comes back when the device does.
    /// </summary>
    private void SyncAudioPickerSelection()
    {
        var chosen = _audio.OutputDeviceName;
        var index = chosen is null
            ? -1
            : _audioDevices.ToList().FindIndex(
                name => string.Equals(name, chosen, StringComparison.OrdinalIgnoreCase));
        AudioDevicePicker.SelectedIndex = index < 0 ? 0 : index + 1;
    }

    /// <summary>True when the stored choice is in the fresh enumeration; null when nothing has been
    /// enumerated yet, which is a different thing and is rendered differently.</summary>
    private bool? AudioChoiceConnected =>
        !_audioDevicesListed || _audio.OutputDeviceName is null
            ? null
            : _audioDevices.Any(
                name => string.Equals(name, _audio.OutputDeviceName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Upstream's <c>SliderMaster_Changed</c> (<c>MainWindow/MainWindow.UiUpdates.cs:1008-1021</c>):
    /// truncate to an int, write it, persist it. Upstream additionally pushes the new value into
    /// whatever is playing right now (<c>:1016-1017</c>); this build's one reader takes it when the
    /// companion's window is built (<c>Features/Dtrh/DtrhHostWindow.axaml.cs:268</c>), so there is
    /// nothing here to push it into and the panel says which of the two this is.
    /// </summary>
    private void OnAudioMasterMoved()
    {
        if (_syncing)
        {
            return;
        }

        var value = (int)Math.Round(AudioMasterSlider.Value);
        if (value == _audio.MasterVolume)
        {
            return;
        }

        _audio.Settings.Mutate(document => document.MasterVolume = value);
        _ = _audio.Settings.Save();
        Refresh();
    }

    /// <summary>Upstream's <c>SliderVideoVolume_Changed</c> (<c>:1023-1030</c>), minus its one live
    /// consumer: <c>App.Video.UpdateVideoVolume</c> (<c>:1028</c>) drives a video service whose
    /// soundtrack this port does not play at all
    /// (<c>Session/MandatoryVideoPresetDocument.cs:17-19</c>). The setting is real and app-wide, so
    /// the dial is here and the notice states plainly that today it stores a preference rather than
    /// changing playback.</summary>
    private void OnAudioVideoMoved()
    {
        if (_syncing)
        {
            return;
        }

        var value = (int)Math.Round(AudioVideoSlider.Value);
        if (value == _audio.VideoVolume)
        {
            return;
        }

        _audio.Settings.Mutate(document => document.VideoVolume = value);
        _ = _audio.Settings.Save();
        Refresh();
    }

    /// <summary>Upstream's <c>BtnAudioOutputRefresh_Click</c> (<c>:1159-1162</c>): re-ask the
    /// machine, because endpoints come and go under a running app. It changes no setting — the
    /// selection is re-derived from the document, so a device that vanished while the app was
    /// running is reflected rather than silently kept.</summary>
    private void OnAudioDevicesRefreshed()
    {
        ListAudioDevices();
        Refresh();
    }

    /// <summary>
    /// The user picked an endpoint. Entry 0 is the system default and stores the empty string,
    /// which is upstream's own encoding (<c>Models/AppSettings.cs:1238-1240</c>).
    ///
    /// <para>It goes through <see cref="Audio.AudioParticipant.SelectOutputDevice"/> — the one seam
    /// that persists the choice and re-probes in that order — rather than writing the document here,
    /// so a crash between the two halves cannot leave the app playing on one endpoint while the
    /// setting names another.</para>
    /// </summary>
    private void OnAudioDevicePicked()
    {
        if (_syncing)
        {
            return;
        }

        var index = AudioDevicePicker.SelectedIndex;
        var name = index >= 1 && index - 1 < _audioDevices.Count ? _audioDevices[index - 1] : string.Empty;
        _ = SelectAudioDeviceAsync(name);
    }

    /// <summary>
    /// Route this app's sound to the chosen endpoint. <b>Off this thread</b>, because a device
    /// switch is a native init and upstream moved the same class of call off ITS UI thread for the
    /// stated reason that a wedged endpoint blocks for many seconds or forever
    /// (<c>MainWindow/MainWindow.UiUpdates.cs:1072-1074</c>, #686).
    ///
    /// <para>Public so a fact can await the GESTURE instead of waiting on a clock — the same reason
    /// the rendered dots on this page are public.</para>
    /// </summary>
    public async Task SelectAudioDeviceAsync(string deviceName)
    {
        if (_audioBusy)
        {
            // A second gesture while the first is still in the driver. Upstream returns outright
            // (_testingAudio, :1067); this also puts the picker back on the stored value, because a
            // combo box left showing an endpoint that was never applied is a lie the user can read.
            _syncing = true;
            try
            {
                SyncAudioPickerSelection();
            }
            finally
            {
                _syncing = false;
            }

            return;
        }

        _audioBusy = true;
        try
        {
            await Task.Run(() => _audio.SelectOutputDevice(deviceName)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // The seam is documented never to throw into a caller; this is the belt on top of those
            // braces, and it is upstream's own shape (:1081-1086).
            _audioTest = AudioDialsNotices.DescribeTestFailure(ex);
        }
        finally
        {
            _audioBusy = false;
            Refresh();
        }
    }

    /// <summary>
    /// The port of upstream's <c>BtnTestAudio_Click</c>
    /// (<c>MainWindow/MainWindow.UiUpdates.cs:1065-1091</c> over
    /// <c>Services/AudioService.TestAudioPlayback</c>, <c>:553-643</c>): bring a device up and cue
    /// one real clip at a fixed half gain, then report what came back.
    ///
    /// <para><b>The clip is looked for FIRST, and the device is only asked for if there is one.</b>
    /// That inverts upstream's order (<c>:578</c> probes the device before <c>:590-607</c> looks for
    /// a file) and it is deliberate: upstream's diagnostic exists to REPORT that probe, while this
    /// panel already carries the device's own last typed outcome permanently
    /// (<see cref="AudioDialsNotices.DescribeDeviceOutcome"/>). So there is nothing to gain by
    /// seizing a render endpoint for a test that cannot play anything, and
    /// <see cref="Audio.AudioParticipant.DeviceInitAttempts"/> stays put on the refusal — which is
    /// the same discipline the participant applies to phase 3.</para>
    ///
    /// <para><b>The gain is fixed at half and does NOT scale with master</b>, which is upstream's
    /// own decision in upstream's own words (<c>AudioService.cs:625</c>, <i>"Fixed 50% for test —
    /// bypasses curve"</i>): a user with the master at 0 still gets to find out whether their
    /// endpoint works.</para>
    ///
    /// <para>Public so a fact can await it rather than wait on a clock.</para>
    /// </summary>
    public async Task TestAudioAsync()
    {
        if (_audioBusy)
        {
            return;
        }

        _audioBusy = true;
        try
        {
            var folders = TestClipFolders();
            _audioTest = await Task.Run(() =>
            {
                var clip = FirstTestClip();
                if (clip is null)
                {
                    return AudioDialsNotices.DescribeTestRefusal(folders);
                }

                var device = _audio.EnsureDevice();
                var play = _audio.Arbitration.PlaySfx(clip, AudioDialsNotices.TestGain);
                return AudioDialsNotices.DescribeTest(
                    device, play, System.IO.Path.GetFileName(clip), _audio.MasterVolume);
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _audioTest = AudioDialsNotices.DescribeTestFailure(ex);
        }
        finally
        {
            _audioBusy = false;
            Refresh();
        }
    }

    /// <summary>
    /// The clip the test cues, or null when there is none.
    ///
    /// <para><b>This build ships no sound of its own</b> — upstream's three candidates are
    /// application resources under <c>Resources/sounds</c> (<c>AudioService.cs:590-594</c>) and those
    /// bytes belong to the legacy tree, which is the same absence
    /// <c>Effects/PopQuizEffect.cs:96-99</c> records for its chime. What this port has instead is
    /// the two user clip folders its audio modules already draw from, so the test plays something a
    /// user recognises as theirs and the refusal names a folder they can act on.</para>
    ///
    /// <para><b>First by name, never a random draw</b>, and that is a divergence from the pools
    /// beside it (<c>Effects/AudioCuePool.Draw</c> picks uniformly) chosen for upstream's property
    /// rather than upstream's mechanism: its candidate list is fixed and ordered
    /// (<c>:590-602</c>, first existing wins), because a diagnostic that plays a different clip on
    /// every press is a diagnostic you cannot compare two runs of.</para>
    /// </summary>
    private string? FirstTestClip()
    {
        foreach (var folder in new[] { _mindWipe.ClipFolder, _brainDrain.ClipFolder })
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            var clip = Directory.EnumerateFiles(folder)
                .Where(file => TestClipExtensions.Contains(
                    System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (clip is not null)
            {
                return clip;
            }
        }

        return null;
    }

    private string TestClipFolders() => $"{_mindWipe.ClipFolder} or {_brainDrain.ClipFolder}";

    // =====================================================================================
    //  THE SCRIPTED SESSION RACK
    // =====================================================================================

    /// <summary>The difficulty stripe's width in DIP — upstream's 4
    /// (<c>MainWindow/MainWindow.SessionIO.cs:423</c>). The headed capture derives the cell it
    /// photographs from this and from the meta cell's own margin, so it is a constant rather than a
    /// literal in two places.</summary>
    public const double StripeWidth = 4;

    /// <summary>The difficulty stripe's height in DIP. See the note at its construction: the row's
    /// content is centred, so this is what makes the cell derivable.</summary>
    public const double StripeHeight = 20;

    /// <summary>The gap either side of the meta cell in DIP, which is also the row template's own
    /// padding (<c>MainWindow.axaml:79</c>) and therefore the whole arithmetic between the meta
    /// cell's right edge and the stripe.</summary>
    public const double RowGutter = 10;

    /// <summary>Which question the confirmation strip is asking. Explicit rather than derived from
    /// <see cref="ScriptedSessionRun.Running"/> at click time, because a session can END between
    /// the strip going up and the button being pressed, and a strip that silently changed its mind
    /// from "stop this?" to "start that?" would act on a gesture nobody made.</summary>
    private enum ScriptedConfirmIntent
    {
        /// <summary>Nothing is being asked; the strip is not on screen.</summary>
        None,

        /// <summary>Upstream's start confirmation (<c>MainWindow.Presets.cs:1465-1476</c>).</summary>
        Start,

        /// <summary>Upstream's stop confirmation (<c>MainWindow.Presets.cs:1893-1906</c>).</summary>
        Stop,

        /// <summary>Upstream's pause confirmation (<c>MainWindow.Presets.cs:1928-1932</c>). There
        /// is no Resume member on purpose: upstream asks nothing before a resume
        /// (<c>:1919-1924</c>), so that path never puts the strip up.</summary>
        Pause,
    }

    /// <summary>
    /// One row per shipped session, from the files themselves — upstream's
    /// <c>RepaintSessionRack</c> / <c>BuildSessionRackRow</c>
    /// (<c>MainWindow/MainWindow.SessionIO.cs:212-232</c>, <c>:377-556</c>).
    ///
    /// <para><b>What a row carries is upstream's row minus two cells.</b> Upstream's is: difficulty
    /// stripe, icon, name, the description's first line, a difficulty pill, the duration, the XP
    /// reward, a provenance badge and four hover actions. The stripe, icon, name, blurb, difficulty
    /// and duration are all here. The XP cell is refused (see
    /// <see cref="SessionRackNotices.RowMeta"/> — nothing in this build awards it), and the
    /// provenance badge and the four actions belong to custom, imported and editable sessions,
    /// which this build does not have: every row here is built-in, so a badge saying so on all four
    /// would carry no information, and an edit or delete button would be a control that cannot act.
    /// </para>
    ///
    /// <para>The empty case is upstream's too (<c>:238-260</c>): a line where the rows would be,
    /// never a blank panel. It is reachable — the four files are content beside the binary and a
    /// published tree missing them is a degraded install rather than a crash
    /// (<see cref="ScriptedSession.ReadFolder"/>). There are TWO of those lines here where upstream
    /// has one, and the difference is real rather than cosmetic: "nothing is installed" and "your
    /// filter matched nothing" send a user to two different places.</para>
    ///
    /// <para>This runs ONCE, at construction. Everything after it goes through
    /// <see cref="RepaintScriptedSessionRack"/>, which is the only thing that ever writes to the
    /// rack panel.</para>
    /// </summary>
    private void BuildScriptedSessionRack()
    {
        ReloadScriptedCatalogue();
        BuildScriptedSessionToolbar();
        RepaintScriptedSessionRack();
    }

    /// <summary>
    /// Re-read every session on disk — upstream's <c>LoadAllSessions</c>
    /// (<c>Services/Session/SessionManager.cs:55-96</c>: built-ins first, then the user's), which
    /// its own rack re-runs whenever a session file has been written behind its back
    /// (<c>MainWindow/MainWindow.SessionIO.cs:1907-1944</c>).
    ///
    /// <para><b>The selection is re-pointed at the fresh instance, and upstream says why in its own
    /// words:</b> "Reload drops and rebuilds every Session instance, so re-point the selection at
    /// the fresh object or Start Session would run a detached copy" (<c>:1926-1934</c>). The port
    /// has the same hazard for the same reason — <see cref="RepaintScriptedSessionRack"/> finds the
    /// armed row by <c>ReferenceEquals</c>, and <see cref="OnScriptedConfirmClicked"/> starts the
    /// object the field holds. Matched on the file path first because that is what a save makes
    /// unique, and on the id second for a built-in, which has a path but no way to change one.</para>
    /// </summary>
    private void ReloadScriptedCatalogue()
    {
        _scriptedCatalogue = _session.CustomSessions.Catalogue();

        if (_scriptedSelection is not { } armed)
        {
            return;
        }

        _scriptedSelection = _scriptedCatalogue.FirstOrDefault(candidate =>
                candidate.SourceFilePath.Length > 0
                && string.Equals(
                    candidate.SourceFilePath, armed.SourceFilePath, StringComparison.Ordinal))
            ?? _scriptedCatalogue.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, armed.Id, StringComparison.Ordinal));
    }

    /// <summary>
    /// The toolbar's own controls, filled once — upstream's <c>EnsureSessionRackToolbar</c>
    /// (<c>MainWindow/MainWindow.SessionIO.cs:627-702</c>), minus the source chips and the two
    /// persisted preferences it restores (the markup beside this panel says why).
    ///
    /// <para>The four band filters are checkboxes rather than upstream's coloured dots
    /// (<c>:663-681</c>), and they carry BOTH channels upstream splits between a glyph and a
    /// tooltip: the band's word from <see cref="SessionRackNotices.Difficulty"/> and the band's
    /// colour from <see cref="SessionRackNotices.DifficultyStripe"/> — the same colour the row's own
    /// stripe is painted in, which is upstream's "one part of the row you can read at a glance while
    /// scrolling" (<c>MainWindow/MainWindow.SessionIO.cs:421-422</c>).</para>
    ///
    /// <para>The sort entries carry the enum member itself on <c>Tag</c>, never a position. That is
    /// upstream's rule for its own combo, in its own words: "Tag is the persisted token … never key
    /// the sort off SelectedIndex - reordering this list would silently repoint every saved
    /// preference" (<c>Views/Tabs/PresetsTabView.xaml:826-828</c>). Nothing is persisted here, so
    /// what it buys this port is narrower and still worth having: a member inserted into
    /// <see cref="ScriptedSessionSort"/> cannot silently re-point this list.</para>
    /// </summary>
    private void BuildScriptedSessionToolbar()
    {
        foreach (var band in Enum.GetValues<ScriptedSessionDifficulty>())
        {
            var chip = new CheckBox
            {
                Name = "SessionFilter" + band,
                Content = SessionRackNotices.Difficulty(band),
                IsChecked = true,
                FontSize = 11,
                Foreground = new SolidColorBrush(
                    Color.Parse(SessionRackNotices.DifficultyStripe(band))),
                [Avalonia.Automation.AutomationProperties.AutomationIdProperty] =
                    "SessionFilter" + band,
                [Avalonia.Automation.AutomationProperties.NameProperty] =
                    SessionRackNotices.Difficulty(band) + " sessions",
            };
            chip.IsCheckedChanged += (_, _) => OnScriptedBandToggled(band, chip.IsChecked == true);
            ScriptedSessionFilterPanel.Children.Add(chip);
        }

        foreach (var sort in Enum.GetValues<ScriptedSessionSort>())
        {
            ScriptedSessionSortBox.Items.Add(new ComboBoxItem
            {
                Tag = sort,
                Content = ScriptedSessionRack.SortLabel(sort),
            });
        }

        ScriptedSessionSortBox.SelectedIndex = 0;
        ScriptedSessionSortBox.SelectionChanged += (_, _) => OnScriptedSortChanged();

        ScriptedSessionSearchBox.PlaceholderText = ScriptedSessionRack.SearchWatermark;
        ScriptedSessionSearchBox.TextChanged += (_, _) => OnScriptedSearchChanged();
    }

    /// <summary>
    /// The whole rack, rebuilt from the catalogue through the toolbar — upstream's
    /// <c>RepaintSessionRack</c> (<c>MainWindow/MainWindow.SessionIO.cs:212-261</c>), which is one
    /// path for every cause: a filter touched, an order chosen, a letter typed. Upstream's reason
    /// for having exactly one is its own: "a single rebuild path is what stops the list and the
    /// registry drifting apart" (<c>:174-176</c>).
    ///
    /// <para><b>The SELECTION survives a repaint, including one that hides it.</b> A row filtered
    /// out of the rack does not disarm the pick behind it — upstream keeps its
    /// <c>_selectedSessionId</c> across every repaint too — and the readout under the button goes
    /// on naming the armed session (<see cref="SessionRackNotices.IdleLine"/>), so a session cannot
    /// be started from an invisible row without the panel saying which one it is. Clearing the pick
    /// instead would mean a search box silently disarming a user mid-gesture.</para>
    /// </summary>
    private void RepaintScriptedSessionRack()
    {
        _scriptedRepainting = true;
        try
        {
            _scriptedRows.Clear();
            ScriptedSessionRackPanel.Children.Clear();

            if (_scriptedCatalogue.Count == 0)
            {
                // Upstream's rack cannot reach this: its registry falls back to a hard-coded set
                // (:264-271). This port has no second copy of the four sessions, so a published
                // tree missing its content folder is a real state and it says so, rather than
                // showing the filter's line and sending the user to look for a filter.
                ScriptedSessionRackPanel.Children.Add(RackLine(
                    "ScriptedSessionRackEmpty",
                    "No sessions are installed. The four built-in sessions ship beside the "
                        + "app, in its sessions folder."));
                ScriptedSessionRackCount.Text = ScriptedSessionRack.CountLine(0, 0);
                return;
            }

            var shown = ScriptedSessionRack.Arrange(
                _scriptedCatalogue, _scriptedBands, _scriptedSort, _scriptedSearch);

            foreach (var session in shown)
            {
                var row = BuildScriptedSessionRow(session);
                _scriptedRows.Add((row, session));
                ScriptedSessionRackPanel.Children.Add(row);
            }

            // AFTER the rows are in the tree, never during: a RadioButton takes its group from the
            // visual root it is attached to, so a check applied to a detached row is a check
            // applied to a different group than the one the rack ends up in.
            var selected = _scriptedRows.Find(entry => ReferenceEquals(entry.Session, _scriptedSelection));
            if (selected.Row is { } selectedRow)
            {
                selectedRow.IsChecked = true;
            }

            if (shown.Count == 0)
            {
                ScriptedSessionRackPanel.Children.Add(
                    RackLine("ScriptedSessionRackNoMatch", ScriptedSessionRack.NoMatches));
            }

            ScriptedSessionRackCount.Text =
                ScriptedSessionRack.CountLine(shown.Count, _scriptedCatalogue.Count);
        }
        finally
        {
            _scriptedRepainting = false;
        }
    }

    /// <summary>A line where the rows would be. A <see cref="TextBlock"/> and never a row-shaped
    /// control, which is upstream's own care at the same place: its empty line is "a TextBlock, not
    /// a Border … so the empty line cannot be staggered in as a row or mistaken for one"
    /// (<c>MainWindow/MainWindow.SessionIO.cs:245-248</c>).</summary>
    private static TextBlock RackLine(string name, string text) => new()
    {
        Name = name,
        Text = text,
        Foreground = new SolidColorBrush(Color.Parse("#FFE8E0EE")),
        Opacity = 0.7,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        [Avalonia.Automation.AutomationProperties.AutomationIdProperty] = name,
    };

    /// <summary>A band filter was switched — upstream's <c>RackDifficultyChip_Changed</c>
    /// (<c>MainWindow/MainWindow.SessionIO.cs:778-787</c>): add or drop the band, then repaint.
    /// Nothing is persisted and nothing else moves.</summary>
    private void OnScriptedBandToggled(ScriptedSessionDifficulty band, bool wanted)
    {
        if (wanted)
        {
            _scriptedBands.Add(band);
        }
        else
        {
            _scriptedBands.Remove(band);
        }

        RepaintScriptedSessionRack();
    }

    /// <summary>An order was chosen — upstream's <c>CmbRackSort_SelectionChanged</c>
    /// (<c>MainWindow/MainWindow.SessionIO.cs:789-802</c>), read off the item's <c>Tag</c> and
    /// ignored when it has not changed.</summary>
    private void OnScriptedSortChanged()
    {
        if (ScriptedSessionSortBox.SelectedItem is not ComboBoxItem { Tag: ScriptedSessionSort sort }
            || sort == _scriptedSort)
        {
            return;
        }

        _scriptedSort = sort;
        RepaintScriptedSessionRack();
    }

    /// <summary>The search box was typed in — upstream's <c>TxtRackSearch_TextChanged</c>
    /// (<c>MainWindow/MainWindow.SessionIO.cs:804-819</c>), including the guard upstream comments
    /// on the line itself: a trimmed text equal to the one already in force is not a new filter, so
    /// typing spaces around a needle does not rebuild the rack (<c>:815-816</c>).</summary>
    private void OnScriptedSearchChanged()
    {
        var trimmed = (ScriptedSessionSearchBox.Text ?? string.Empty).Trim();
        if (trimmed == _scriptedSearch)
        {
            return;
        }

        _scriptedSearch = trimmed;
        RepaintScriptedSessionRack();
    }

    /// <summary>
    /// One rack row. A <see cref="RadioButton"/> in the page's own rack livery, which is what makes
    /// selection the control's own <c>:checked</c> state and its keyboard activation the control's
    /// own — the same reasoning the module rows above carry, and upstream's own row is a selectable
    /// card too (<c>MainWindow.SessionIO.cs:381-397</c>).
    /// </summary>
    private RadioButton BuildScriptedSessionRow(ScriptedSession session)
    {
        var icon = new TextBlock
        {
            Text = SessionRackNotices.RowIcon(session),
            FontSize = 13,
            Width = 22,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        var name = new TextBlock
        {
            Text = session.Name,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            MaxWidth = 150,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Avalonia.Thickness(4, 0, 0, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        var blurb = new TextBlock
        {
            Text = SessionRackNotices.RowBlurb(session),
            FontSize = 11,
            Opacity = 0.6,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Avalonia.Thickness(10, 0, 0, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        var meta = new TextBlock
        {
            Text = SessionRackNotices.RowMeta(session),
            FontSize = 11,
            Opacity = 0.8,
            TextAlignment = TextAlignment.Right,
            Margin = new Avalonia.Thickness(RowGutter, 0, RowGutter, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            [Avalonia.Automation.AutomationProperties.AutomationIdProperty] =
                "SessionMeta" + PascalId(session.Id),
        };

        // THE PROVENANCE BADGE — upstream's pill (MainWindow/MainWindow.SessionIO.cs:508-517,
        // :588-597). Refused at slice 3 on the ground that every row was built-in and a badge on
        // all four would carry no information; that premise died with the editor, because saving an
        // edited built-in puts a session of the SAME NAME on the row below it and this badge is the
        // only cell that tells them apart. See SessionRackNotices.RowProvenance.
        var badge = new TextBlock
        {
            Name = "SessionBadge" + PascalId(session.Id),
            Text = SessionRackNotices.RowProvenance(session.Origin),
            FontSize = 9,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(
                Color.Parse(SessionRackNotices.RowProvenanceColour(session.Origin))),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            [Avalonia.Automation.AutomationProperties.AutomationIdProperty] =
                "SessionBadge" + PascalId(session.Id),
        };

        // THE DIFFICULTY STRIPE, in upstream's own colours (Resources/Theme/Colors.xaml:191-197)
        // and for upstream's own stated reason: it is "the one part of the row you can read at a
        // glance while scrolling" (MainWindow.SessionIO.cs:421-422). It sits at the row's TRAILING
        // edge rather than its leading one, because the leading edge of a rack row in this port is
        // already the selection marker (RadioButton.rack-row:checked, MainWindow.axaml:101-104) and
        // two 3-4 DIP bars on the same edge would be one bar the user cannot decode.
        var stripe = new Border
        {
            Name = "SessionStripe" + PascalId(session.Id),
            Width = StripeWidth,
            // An explicit height rather than upstream's full bleed, and it is a HARNESS
            // requirement as much as a visual one: this rack row's content is vertically CENTRED
            // by the row template (RadioButton.rack-row, MainWindow.axaml:80-95), so a stretched
            // child would be as tall as whatever the font metrics made the tallest cell — a
            // number nothing can derive, on the one cell a headed capture has to aim at exactly.
            Height = StripeHeight,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Background = new SolidColorBrush(
                Color.Parse(SessionRackNotices.DifficultyStripe(session.Difficulty))),
        };

        // THE BADGE GOES BEFORE THE META CELL, NOT AFTER IT, and the reason is a live gate rather
        // than taste: the headed `session-row` capture derives the stripe's cell from the meta
        // cell's right edge plus one RowGutter and REFUSES the capture when the two do not close
        // (client/tools/verify/capture.ps1:2712-2727). A column inserted between meta and stripe
        // would put a badge's width into that gap and fail a check that is doing its job. Between
        // the blurb and the meta the derivation is untouched — the meta cell's own left margin is
        // the gap, and nothing between meta and the trailing edge has moved.
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto,Auto") };
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(blurb, 2);
        Grid.SetColumn(badge, 3);
        Grid.SetColumn(meta, 4);
        Grid.SetColumn(stripe, 5);
        grid.Children.Add(icon);
        grid.Children.Add(name);
        grid.Children.Add(blurb);
        grid.Children.Add(badge);
        grid.Children.Add(meta);
        grid.Children.Add(stripe);

        var row = new RadioButton
        {
            Name = "SessionRow" + PascalId(session.Id),
            GroupName = "scripted-sessions",
            Content = grid,
            // The rack livery aligns its content LEFT (MainWindow.axaml:84), which arranges the row's
            // content presenter at its DESIRED width — so a trailing cell would float in the middle
            // of the row instead of sitting at its edge. The module rows above do not notice because
            // they set MinWidth=200 and are exactly that wide; this row's blurb column is a star and
            // has to fill. Measured by the headed capture's own grid-closes cross-check, which
            // refused a stripe 43 DIP short of the row's trailing edge.
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            [Avalonia.Automation.AutomationProperties.AutomationIdProperty] =
                "SessionRow" + PascalId(session.Id),
            [Avalonia.Automation.AutomationProperties.NameProperty] = session.Name,
        };
        row.Classes.Add("rack-row");

        // Upstream puts the WHOLE authored description on the row's tooltip, because the blurb cell
        // is one ellipsised line of it (MainWindow.SessionIO.cs:474-477).
        ToolTip.SetTip(row, session.Description);
        row.IsCheckedChanged += (_, _) => OnScriptedRowChecked();
        return row;
    }

    /// <summary>A session id as a control name: <c>good_girls_dont_cum</c> ->
    /// <c>GoodGirlsDontCum</c>. Upstream keys its rows on the raw id (<c>Tag</c>,
    /// <c>MainWindow.SessionIO.cs:371-373</c>); a control name cannot carry an underscore-cased id
    /// and stay conventional, and the headed harness addresses these rows by name.</summary>
    private static string PascalId(string id) =>
        string.Concat(
            id.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    /// <summary>
    /// A row was picked. Upstream's <c>SelectSession</c> repaints the rack and enables the actions
    /// that act on a selection (<c>MainWindow.SessionIO.cs</c>, cited from
    /// <c>Views/Tabs/PresetsTabView.xaml:920-923</c>); here selection is the control's own state,
    /// so all this has to do is remember which one and repaint the panel.
    ///
    /// <para>A new pick ABANDONS an unanswered confirmation: the strip names a session by name, and
    /// leaving it up over a different selection is how a user ends up starting something they did
    /// not read.</para>
    /// </summary>
    private void OnScriptedRowChecked()
    {
        // A repaint re-checks the armed row after rebuilding the rack. That is the SAME pick coming
        // back, not a new one, and treating it as new would tear down a confirmation the user is
        // reading every time they typed a letter into the search box.
        if (_scriptedRepainting)
        {
            return;
        }

        var picked = _scriptedRows.Find(entry => entry.Row.IsChecked == true);
        _scriptedSelection = picked.Session;
        _scriptedRefusal = null;
        if (_scriptedConfirm == ScriptedConfirmIntent.Start)
        {
            _scriptedConfirm = ScriptedConfirmIntent.None;
        }

        RenderScriptedSession();
    }

    /// <summary>
    /// The one button, and the branch upstream comments in its own words: "The button doubles as
    /// Start/Stop — state dictates which path to run. This also makes us resilient to any
    /// stale/duplicate Click subscriptions" (<c>MainWindow/MainWindow.Presets.cs:1455-1459</c>).
    ///
    /// <para>Then upstream's two refusals, in upstream's order: nothing selected, or a session that
    /// is not available (<c>:1463</c>). Upstream returns in silence; this says which guard
    /// refused, because a button that does nothing and explains nothing is indistinguishable from
    /// one that is broken.</para>
    /// </summary>
    private void OnScriptedStartClicked()
    {
        if (_scripted.Running)
        {
            _scriptedConfirm = ScriptedConfirmIntent.Stop;
            RenderScriptedSession();
            return;
        }

        if (_scriptedSelection is null)
        {
            _scriptedRefusal = SessionRackNotices.NothingSelected;
            RenderScriptedSession();
            return;
        }

        if (!_scriptedSelection.IsAvailable)
        {
            _scriptedRefusal = SessionRackNotices.NotAvailable;
            RenderScriptedSession();
            return;
        }

        _scriptedRefusal = null;
        _scriptedConfirm = ScriptedConfirmIntent.Start;
        RenderScriptedSession();
    }

    /// <summary>
    /// The PAUSE button — upstream's <c>BtnPauseSession_Click</c>
    /// (<c>MainWindow/MainWindow.Presets.cs:1908-1940</c>), which is one handler serving both
    /// directions and treats them asymmetrically on purpose.
    ///
    /// <para><b>Resume is immediate; pause asks.</b> Upstream's paused branch calls
    /// <c>ResumeSession()</c> straight away (<c>:1919-1924</c>) and its running branch puts up a
    /// dialog naming the cost first (<c>:1928-1939</c>). The asymmetry is the cost: a pause spends
    /// something and a resume spends nothing, so only one of them is worth a question.</para>
    ///
    /// <para>The <see cref="ScriptedSessionRun.Running"/> guard is upstream's first line
    /// (<c>:1910</c>). The button is not on screen when nothing runs, so this is the second door on
    /// the same refusal rather than the only one — the same belt-and-braces the START button's own
    /// state check gets.</para>
    /// </summary>
    private void OnScriptedPauseClicked()
    {
        if (!_scripted.Running)
        {
            return;
        }

        if (_scripted.Paused)
        {
            _scripted.Resume();

            // The resume re-armed every module off the SESSION's dials, so the panels on this page
            // are showing what they showed while it was held. Same reason the confirm path repaints.
            OnSessionChanged();
            return;
        }

        _scriptedRefusal = null;
        _scriptedConfirm = ScriptedConfirmIntent.Pause;
        RenderScriptedSession();
    }

    /// <summary>
    /// The confirmation was answered YES — upstream's <c>if (confirmed) StartSession(...)</c>
    /// (<c>:1472-1476</c>) and <c>if (confirmed) StopSession(completed: false)</c>
    /// (<c>:1903-1906</c>).
    ///
    /// <para>The intent is read and cleared FIRST, so a strip that is answered twice (a double
    /// click, a duplicated subscription — the thing upstream's doubling comment is about) can only
    /// act once.</para>
    /// </summary>
    private void OnScriptedConfirmClicked()
    {
        var intent = _scriptedConfirm;
        _scriptedConfirm = ScriptedConfirmIntent.None;
        switch (intent)
        {
            case ScriptedConfirmIntent.Start when _scriptedSelection is { IsAvailable: true } pick:
                // Upstream starts the ordinary engine on the way in (:1511-1514); the run does
                // that itself, in the order its own doc gives, so there is nothing to do here but
                // ask. A false return means one was already running, which the strip cannot mean.
                _scripted.Start(pick);
                break;
            case ScriptedConfirmIntent.Stop:
                _scripted.Stop();
                break;
            case ScriptedConfirmIntent.Pause:
                // Upstream's `if (confirmed) { _sessionEngine.PauseSession(); ... }` (:1934-1939).
                // A false return means the session ended while the question was up, which the run
                // refuses on its own guard — the same shape the Start case relies on.
                _scripted.Pause();
                break;
            default:
                break;
        }

        // The start applied the session's dials to every document above, and the stop gave the
        // user's back: either way the panels on this page are now showing stale numbers.
        OnSessionChanged();
    }

    /// <summary>The confirmation was answered NO. Upstream's dialog simply returns
    /// (<c>:1472</c>, <c>:1903</c>) — nothing starts, nothing stops.</summary>
    private void OnScriptedCancelClicked()
    {
        _scriptedConfirm = ScriptedConfirmIntent.None;
        RenderScriptedSession();
    }

    // =====================================================================================
    //  THE SESSION EDITOR
    // =====================================================================================

    /// <summary>The editor on screen, or null. Public so a fact reads the window the page really
    /// built rather than a second one it made for itself.</summary>
    public SessionEditorWindow? CurrentEditor { get; private set; }

    /// <summary>
    /// EDIT — upstream's <c>SessionBtn_Edit</c> (<c>MainWindow/MainWindow.SessionIO.cs:1819-1868</c>):
    /// find the session the gesture names, open the editor on it, and do nothing at all when there
    /// is no such session (<c>:1824</c>).
    ///
    /// <para>Upstream cannot be pressed with nothing selected — its action button carries its own
    /// row's id — so the refusal here is the port's, for the port's one-button-per-selection shape,
    /// and it is worded as the START button's twin rather than left silent.</para>
    ///
    /// <para>ONE editor at a time, refocusing rather than stacking — the rule
    /// <see cref="SessionRecapLaunch.ShowHistory"/> already keeps, and upstream gets for free from
    /// <c>ShowDialog()</c> (<c>:1828</c>).</para>
    /// </summary>
    private void OnScriptedEditClicked()
    {
        if (CurrentEditor is { } open)
        {
            open.Activate();
            return;
        }

        if (_scriptedSelection is not { } pick)
        {
            _scriptedRefusal = SessionRackNotices.NothingToEdit;
            RenderScriptedSession();
            return;
        }

        _scriptedRefusal = null;
        var editor = new SessionEditorWindow(pick, CommitEditedSession, DeleteCustomSession);
        CurrentEditor = editor;
        editor.Closed += (_, _) =>
        {
            if (ReferenceEquals(CurrentEditor, editor))
            {
                CurrentEditor = null;
            }
        };

        // Owned by the shell window this page is mounted in, as every other window in this shell is
        // (Navigation/SessionRecapLaunch.cs). Read off the tree rather than injected, because the
        // page is already inside it by the time a button on it can be pressed.
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            editor.Show(owner);
        }
        else
        {
            editor.Show();
        }
        RenderScriptedSession();
    }

    /// <summary>
    /// Persist what the editor built and put the rack back in step with the disk — upstream's
    /// <c>AddNewSession</c> / <c>UpdateCustomSession</c> pair
    /// (<c>Services/Session/SessionManager.cs:174-196</c>, <c>:152-169</c>), both of which write the
    /// file and then rebuild the list.
    ///
    /// <para>False when nothing was written, and the editor stays open holding the user's typing.
    /// Nothing in the rack moves on a false: the catalogue is re-read only after a write that
    /// really landed.</para>
    /// </summary>
    private bool CommitEditedSession(ScriptedSession edited)
    {
        ArgumentNullException.ThrowIfNull(edited);
        if (_session.CustomSessions.Save(edited) is null)
        {
            return false;
        }

        // Re-read rather than splice the instance in: the file on disk is now the authority, and a
        // rack built from anything else could disagree with it after a save that normalised
        // something (an out-of-range duration, a name with trailing space). Upstream reloads for the
        // same reason (MainWindow/MainWindow.SessionIO.cs:1929).
        _scriptedSelection = edited;
        ReloadScriptedCatalogue();
        RepaintScriptedSessionRack();
        _scriptedRefusal = SessionRackNotices.EditorSaved(edited);
        RenderScriptedSession();
        return true;
    }

    /// <summary>
    /// Remove one of the user's own sessions — upstream's <c>SessionBtn_Delete</c>
    /// (<c>MainWindow/MainWindow.SessionIO.cs:1946-1968</c>) into <c>DeleteSession</c>
    /// (<c>Services/Session/SessionManager.cs:201-219</c>), which refuses a built-in outright.
    ///
    /// <para>The armed selection goes with it: a pick that names a file that is no longer there is
    /// a Start button aimed at nothing.</para>
    /// </summary>
    private bool DeleteCustomSession(ScriptedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_session.CustomSessions.Delete(session))
        {
            return false;
        }

        _scriptedSelection = null;
        ReloadScriptedCatalogue();
        RepaintScriptedSessionRack();
        _scriptedRefusal = SessionRackNotices.EditorDeleted(session);
        RenderScriptedSession();
        return true;
    }

    /// <summary>
    /// The whole scripted panel, repainted from ONE clock reading.
    ///
    /// <para><b>One reading, not three.</b> <see cref="ScriptedSessionRun.ReadProgress"/> exists
    /// for exactly this: elapsed, remaining and percent taken together, so the button's countdown
    /// and the line under it cannot disagree by the second it took to ask twice — which is what
    /// upstream's own event args do (<c>Services/Session/SessionEngine.cs:520-524</c>).</para>
    /// </summary>
    private void RenderScriptedSession()
    {
        var running = _scripted.Running;
        var paused = _scripted.Paused;
        var live = _scripted.Current;
        var progress = _scripted.ReadProgress();

        // A stop or pause confirmation outlives nothing: if the session ended while the strip was
        // up (it reached its own duration), there is no longer anything to stop or hold and the
        // question goes.
        if (!running
            && _scriptedConfirm is ScriptedConfirmIntent.Stop or ScriptedConfirmIntent.Pause)
        {
            _scriptedConfirm = ScriptedConfirmIntent.None;
        }

        var caption = running
            ? SessionRackNotices.StopButtonRunning(progress)
            : SessionRackNotices.StartButtonIdle;
        ScriptedSessionStartButton.Content = caption;
        ScriptedSessionStartButton.SetValue(
            Avalonia.Automation.AutomationProperties.NameProperty, caption);
        ScriptedSessionStartButton.Classes.Set("running", running);

        // Upstream's visibility rule, verbatim: the pause button exists only while a session is
        // running (:1809 shows it, :1855 collapses it). Absent rather than disabled, which is §9
        // D7's rule and upstream's own choice here.
        var pauseCaption = paused
            ? SessionRackNotices.PauseButtonPaused
            : SessionRackNotices.PauseButtonIdle;
        ScriptedSessionPauseButton.IsVisible = running;
        ScriptedSessionPauseButton.Content = pauseCaption;
        ScriptedSessionPauseButton.SetValue(
            Avalonia.Automation.AutomationProperties.NameProperty, pauseCaption);

        ScriptedSessionConfirmPanel.IsVisible = _scriptedConfirm != ScriptedConfirmIntent.None;
        if (_scriptedConfirm == ScriptedConfirmIntent.Start && _scriptedSelection is { } pick)
        {
            ScriptedSessionConfirmTitle.Text = SessionRackNotices.StartConfirmTitle(pick);
            ScriptedSessionConfirmDetail.Text = SessionRackNotices.StartConfirmDuration(pick);
            ScriptedSessionConfirmPromise.Text = SessionRackNotices.SettingsPromise;
            ScriptedSessionConfirmQuestion.Text = SessionRackNotices.ReadyToBegin;
            ScriptedSessionConfirmButton.Content = SessionRackNotices.ConfirmStart;
            ScriptedSessionCancelButton.Content = SessionRackNotices.CancelStart;
        }
        else if (_scriptedConfirm == ScriptedConfirmIntent.Stop && live is { } stopping)
        {
            ScriptedSessionConfirmTitle.Text = SessionRackNotices.StopConfirmTitle;
            ScriptedSessionConfirmDetail.Text = SessionRackNotices.StopConfirmSubject(stopping);
            ScriptedSessionConfirmPromise.Text = SessionRackNotices.StopConfirmTiming(progress);
            ScriptedSessionConfirmQuestion.Text = SessionRackNotices.StopConfirmQuestion;
            ScriptedSessionConfirmButton.Content = SessionRackNotices.ConfirmStop;
            ScriptedSessionCancelButton.Content = SessionRackNotices.CancelStop;
        }
        else if (_scriptedConfirm == ScriptedConfirmIntent.Pause)
        {
            // Upstream's four lines onto the strip's four slots (en.json:3387-3389). The running
            // total is read from the RUN rather than counted here, so the number the question
            // quotes and the number the outcome carries are the same one.
            ScriptedSessionConfirmTitle.Text = SessionRackNotices.PauseConfirmTitle;
            ScriptedSessionConfirmDetail.Text = SessionRackNotices.PauseConfirmCost;
            ScriptedSessionConfirmPromise.Text =
                SessionRackNotices.PauseConfirmPenalty(_scripted.PauseCount);
            ScriptedSessionConfirmQuestion.Text = SessionRackNotices.PauseConfirmQuestion;
            ScriptedSessionConfirmButton.Content = SessionRackNotices.ConfirmPause;
            ScriptedSessionCancelButton.Content = SessionRackNotices.CancelPause;
        }

        ScriptedSessionConfirmButton.SetValue(
            Avalonia.Automation.AutomationProperties.NameProperty,
            ScriptedSessionConfirmButton.Content as string ?? string.Empty);
        ScriptedSessionCancelButton.SetValue(
            Avalonia.Automation.AutomationProperties.NameProperty,
            ScriptedSessionCancelButton.Content as string ?? string.Empty);

        ScriptedSessionPhaseState.Text = running && live is { } current
            ? SessionRackNotices.PhaseLine(current, _scripted.CurrentPhase, _scripted.CurrentPhaseIndex)
            : _scriptedRefusal ?? SessionRackNotices.IdleLine(_scriptedSelection);
        ScriptedSessionProgressState.Text = running
            ? SessionRackNotices.ProgressLine(progress, paused)
            : string.Empty;
        ScriptedSessionAbsenceState.Text = SessionRackNotices.Absences;

        // THE SESSION FEATURE LOCK, painted from the one place every path already reaches. It is
        // last because it is derived from the same run this method just read, and it is HERE rather
        // than in Refresh() because Refresh()'s own last line is a call to this method: putting it
        // here gives it BOTH of upstream's drivers at once — the session's start and stop (which
        // arrive through Refresh) and the per-second progress tick (which arrives straight here).
        // Upstream drives it from exactly those two plus a tab switch, so that "no single missed
        // event can leave the UI out of step" (MainWindow/MainWindow.SessionFeatureLock.cs:139-142).
        // The tab-switch leg is not owed here: upstream needs it because its feature popups are
        // rebuilt on every open, and it says so where its rack panels are long-lived like these
        // ones (:345-348).
        ApplySessionFeatureLock();
    }

    /// <summary>
    /// WPF's right-click quick-toggle. <c>Handled</c> is set so the gesture stops here rather
    /// than also selecting the row (<c>StudioTabView.xaml.cs:1115</c>): a toggle that also
    /// opened the panel would make the two gestures indistinguishable.
    /// </summary>
    private void OnRowPointerReleased(PointerReleasedEventArgs e, string effectId, CheckBox enable)
    {
        if (e.InitialPressMouseButton != MouseButton.Right)
        {
            return;
        }

        e.Handled = true;

        // THE SESSION FEATURE LOCK'S SECOND DOOR. The gesture is a shortcut for `enable`, so it is
        // refused exactly when that box is refused — upstream's rule that every write path onto the
        // prescribed dose goes through one refusal (MainWindow/MainWindow.SessionFeatureLock.cs:232-241).
        // The refusal is not silent: the banner naming the session is on screen above the detail
        // host for the whole time this can happen, which is the outcome upstream's ribbon pulse
        // (:275-300) is for.
        if (IsSessionFeatureLockActive && _sessionOwned.Contains(enable))
        {
            return;
        }

        _session.Engine.QuickToggle(effectId);
        LoadDialsFromPreset();
        Refresh();
    }

    /// <summary>
    /// The Scheduler row's right-click. Same gesture, same <c>Handled</c>, different destination:
    /// it flips the scheduler's own enable, because this row has no <see cref="ISessionEffect"/>
    /// for <see cref="SessionEngine.QuickToggle"/> to find. Upstream's entry does exactly this —
    /// <c>toggle: () =&gt; FlipMasterCheckBox(PanelScheduler?.Inner.ChkEnabled)</c>
    /// (<c>Views/Tabs/StudioTabView.xaml.cs:537</c>).
    /// </summary>
    private void OnSchedulerRowPointerReleased(PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right)
        {
            return;
        }

        e.Handled = true;
        _scheduler.SetEnabled(!_scheduler.Enabled);
        LoadDialsFromPreset();
        Refresh();
    }

    /// <summary>
    /// The Haptics row's right-click. Same gesture, same <c>Handled</c>, and the one destination on
    /// this page that can say no: it asks <see cref="Haptics.HapticParticipant.RequestEnable"/>,
    /// which consults the premium gate and writes nothing when the gate refuses.
    ///
    /// <para>Upstream reaches the same gate by a longer road — its toggle flips the panel's own
    /// checkbox so <c>ChkHapticsEnabled_Changed</c> runs and REVERTS the box
    /// (<c>StudioTabView.xaml.cs:521-525</c>, <c>MainWindow/MainWindow.Haptics.cs:489-497</c>). The
    /// user-visible outcome is identical and the intermediate state is not: nothing here is ever
    /// written and then undone, so there is no instant at which the setting says yes.</para>
    /// </summary>
    private void OnHapticsRowPointerReleased(PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right)
        {
            return;
        }

        e.Handled = true;
        _haptics.RequestEnable(!_haptics.Enabled);
        LoadDialsFromPreset();
        Refresh();
    }

    /// <summary>
    /// The haptics panel's Enable box — the same request the row's right-click makes (the A-004
    /// one-command-path rule), and the surface where the refusal is read.
    ///
    /// <para>The box is re-synced from the DOCUMENT afterwards rather than left where the user put
    /// it, which is upstream's own repair for exactly this handler
    /// (<c>MainWindow/MainWindow.Haptics.cs:491</c> sets <c>IsChecked = false</c> before it returns).
    /// Without it the box would sit ticked over a setting that says otherwise, and a restart would
    /// silently disagree with the screen.</para>
    /// </summary>
    private void OnHapticsEnableToggled()
    {
        if (_syncing)
        {
            return;
        }

        var target = HapticsEnableToggle.IsChecked == true;
        if (target == _haptics.Enabled)
        {
            return;
        }

        _haptics.RequestEnable(target);
        LoadDialsFromPreset();
        Refresh();
    }

    /// <summary>
    /// One provider box. <b>Never gated and never exclusive</b>: upstream's per-provider handler
    /// writes and saves with no premium check (<c>MainWindow/MainWindow.Haptics.cs:580-595</c>) and
    /// its own comment records that assigning the legacy single-choice enum here was the bug —
    /// <i>"ticking a second provider un-ticked the first one on the way out"</i> (<c>:576-579</c>).
    /// A ticked route reaches nothing on its own; the gate is on the master box above.
    /// </summary>
    private void OnHapticsRouteToggled(Haptics.HapticProviderRoute route, CheckBox box)
    {
        if (_syncing)
        {
            return;
        }

        _haptics.SetRouteEnabled(route, box.IsChecked == true);
        Refresh();
    }

    /// <summary>The scheduler panel's Enable box — the same write the row's right-click makes, so
    /// the two gestures cannot drift into two behaviours (the A-004 one-command-path rule).</summary>
    private void OnSchedulerEnableToggled()
    {
        if (_syncing)
        {
            return;
        }

        var target = SchedulerEnableToggle.IsChecked == true;
        if (target == _scheduler.Enabled)
        {
            return;
        }

        _scheduler.SetEnabled(target);
        Refresh();
    }

    /// <summary>
    /// Both time boxes, committed together on focus loss — upstream's own handler shape
    /// (<c>Features/SchedulerFeatureControl.xaml.cs:71-79</c>: it assigns start AND end and saves
    /// once, whichever box lost focus).
    ///
    /// <para>The text is written through UNVALIDATED, deliberately. What a user typed is what
    /// upstream stores, and the predicate's fallback is what happens to text it cannot read
    /// (<c>MainWindow/MainWindow.StartStop.cs:667-677</c>). Rejecting it here would delete a live
    /// behaviour and, worse, would leave the user's box showing something the scheduler is not
    /// using. The panel reports what was really parsed instead.</para>
    /// </summary>
    private void OnSchedulerTimesCommitted()
    {
        if (_syncing)
        {
            return;
        }

        var start = SchedulerStartTimeBox.Text ?? string.Empty;
        var end = SchedulerEndTimeBox.Text ?? string.Empty;
        if (string.Equals(start, _scheduler.Preset.StartTime, StringComparison.Ordinal)
            && string.Equals(end, _scheduler.Preset.EndTime, StringComparison.Ordinal))
        {
            return;
        }

        _scheduler.SetTimes(start, end);
        Refresh();
    }

    /// <summary>
    /// A panel's Enable checkbox. WPF's does the same work as the row's right-click — write
    /// the flag, then start/stop the live service if the engine is running
    /// (<c>Features/FlashFeatureControl.xaml.cs:159-175</c>, and the continuous pair's
    /// <c>Features/PinkFilterFeatureControl.xaml.cs:87-95</c>, which writes the flag and
    /// reconciles) — so it routes through the SAME entry rather than growing a second copy of
    /// that body, once per module.
    /// </summary>
    private void OnEnableToggled(CheckBox toggle, ISessionEffect effect, string effectId)
    {
        if (_syncing)
        {
            return;
        }

        var target = toggle.IsChecked == true;
        if (target == effect.Enabled)
        {
            return;
        }

        _session.Engine.QuickToggle(effectId);
        Refresh();
    }

    /// <summary>
    /// WPF's frequency slider writes the setting, re-paces the live schedule and saves
    /// (<c>FlashFeatureControl.xaml.cs:177-188</c>) — all three, in that order.
    /// </summary>
    private void OnFrequencyMoved()
    {
        if (_syncing)
        {
            return;
        }

        var value = (int)Math.Round(FlashFrequencySlider.Value);
        _session.Preset.Mutate(p => p.FlashesPerHour = value);
        _flash.RefreshSchedule();
        _ = _session.Preset.Save();
        Refresh();
    }

    /// <summary>
    /// WPF's images slider writes the setting and saves; it does NOT re-pace, because the count
    /// is read at the moment a flash fires rather than when it is scheduled
    /// (<c>FlashFeatureControl.xaml.cs:190-199</c>, and <c>FlashService.cs:586</c> reads it in
    /// the draw). Same here.
    /// </summary>
    private void OnImagesPerFlashMoved()
    {
        if (_syncing)
        {
            return;
        }

        var value = (int)Math.Round(FlashImagesSlider.Value);
        _session.Preset.Mutate(p => p.ImagesPerFlash = value);
        _ = _session.Preset.Save();
        Refresh();
    }

    /// <summary>
    /// The Subliminals frequency slider writes the setting and saves — and, unlike the flash
    /// frequency slider directly above it, does <b>not</b> re-pace the live schedule. That is not
    /// an inconsistency in the port: WPF's two panels really differ here
    /// (<c>Features/SubliminalFeatureControl.xaml.cs:89-98</c> writes and saves;
    /// <c>Features/FlashFeatureControl.xaml.cs:177-188</c> also calls
    /// <c>App.Flash.RefreshSchedule()</c>), so a subliminal frequency change takes effect at the
    /// next firing rather than immediately. Kept, because a user who moves both sliders is
    /// entitled to the timing they already have.
    /// </summary>
    private void OnSubliminalFrequencyMoved()
    {
        if (_syncing)
        {
            return;
        }

        var value = (int)Math.Round(SubliminalFrequencySlider.Value);
        _session.SubliminalPreset.Mutate(p => p.PerMinute = value);
        _ = _session.SubliminalPreset.Save();
        Refresh();
    }

    /// <summary>
    /// The Pink Filter opacity slider writes the setting, re-tints whatever is already on screen
    /// and saves — WPF's own order (<c>Features/PinkFilterFeatureControl.xaml.cs:99-109</c>:
    /// write, save, <c>RefreshOverlays()</c>, reconciled at <c>OverlayService.cs:434-437</c>).
    /// The re-tint goes through the module rather than the surface so the arm state and the dot
    /// move with it.
    /// </summary>
    private void OnPinkFilterOpacityMoved()
    {
        if (_syncing)
        {
            return;
        }

        var value = (int)Math.Round(PinkFilterOpacitySlider.Value);
        _pinkFilter.SetOpacityPercent(value);
        _ = _session.PinkFilterPreset.Save();
        Refresh();
    }

    /// <summary>
    /// The ramp's duration slider — WPF's writes the setting and saves
    /// (<c>Features/IntensityRampFeatureControl.xaml.cs:91-100</c>), and does NOT poke the running
    /// ramp: upstream's tick re-reads the setting at its next 2 s sample. Here the write goes through
    /// the module, which re-evaluates at once — the same relationship the opacity sliders above have
    /// to their modules, and indistinguishable from upstream's to a human. D98.
    /// </summary>
    private void OnRampDurationMoved()
    {
        if (_syncing)
        {
            return;
        }

        _ramp.SetDurationMinutes((int)Math.Round(RampDurationSlider.Value));
        _ = _session.RampPreset.Save();
        Refresh();
    }

    /// <summary>The ramp's multiplier slider (<c>IntensityRampFeatureControl.xaml.cs:102-111</c>).
    /// It is the one dial on this panel a user will move mid-session and expect to feel at once, and
    /// it is why the re-evaluate is worth having.</summary>
    private void OnRampMultiplierMoved()
    {
        if (_syncing)
        {
            return;
        }

        _ramp.SetMultiplier(RampMultiplierSlider.Value);
        _ = _session.RampPreset.Save();
        Refresh();
    }

    /// <summary>The curve picker (<c>IntensityRampFeatureControl.xaml.cs:122-136</c>). An index this
    /// build does not know maps to <see cref="RampCurve.Linear"/>, which is upstream's own
    /// <c>_ =&gt; Models.RampCurve.Linear</c> at <c>:135</c>.</summary>
    private void OnRampCurvePicked()
    {
        if (_syncing)
        {
            return;
        }

        var curve = RampCurvePicker.SelectedIndex switch
        {
            1 => RampCurve.EaseIn,
            2 => RampCurve.EaseOut,
            3 => RampCurve.SCurve,
            4 => RampCurve.Exponential,
            _ => RampCurve.Linear,
        };

        if (curve == _ramp.Preset.Curve)
        {
            return;
        }

        _ramp.SetCurve(curve);
        _ = _session.RampPreset.Save();
        Refresh();
    }

    /// <summary>
    /// Mind Wipe's frequency dial. Writes through the module, which re-evaluates at once — the port's
    /// standing convention for every module's dial, and it matters more here than usual: this dial
    /// changes the ODDS of the next ten-second window rather than the spacing of a schedule, so a user
    /// who turns it up expects the very next window to be likelier.
    /// </summary>
    private void OnMindWipeFrequencyMoved()
    {
        if (_syncing)
        {
            return;
        }

        _mindWipe.SetPerHour((int)Math.Round(MindWipeFrequencySlider.Value));
        _ = _session.MindWipePreset.Save();
        Refresh();
    }

    /// <summary>Mind Wipe's volume dial. WPF pushes a moved slider onto the CLIP THAT IS PLAYING as
    /// well (<c>MindWipeService.cs:102-122</c>); the port's audio seam has no live-gain path, so it
    /// takes effect on the next cue — a divergence whose whole window is one short clip.</summary>
    private void OnMindWipeVolumeMoved()
    {
        if (_syncing)
        {
            return;
        }

        _mindWipe.SetVolumePercent((int)Math.Round(MindWipeVolumeSlider.Value));
        _ = _session.MindWipePreset.Save();
        Refresh();
    }

    /// <summary>Mandatory Video's frequency dial. It RE-PACES rather than waiting: upstream's own
    /// frequency slider changes the next scheduled firing, not the one already on the clock.</summary>
    private void OnMandatoryVideoFrequencyMoved()
    {
        if (_syncing)
        {
            return;
        }

        _mandatoryVideo.SetPerHour((int)Math.Round(MandatoryVideoFrequencySlider.Value));
        _ = _session.MandatoryVideoPreset.Save();
        Refresh();
    }

    /// <summary>Mandatory Video's max-length cap. Zero is upstream's own "no cap"
    /// (<c>Services/Video/VideoService.cs:5509-5510</c>) and it applies to the NEXT clip, because
    /// upstream arms its cap timer when playback starts (<c>:2440</c>) and never re-arms it.</summary>
    private void OnMandatoryVideoMaxLengthMoved()
    {
        if (_syncing)
        {
            return;
        }

        _mandatoryVideo.SetMaxSeconds((int)Math.Round(MandatoryVideoMaxLengthSlider.Value));
        _ = _session.MandatoryVideoPreset.Save();
        Refresh();
    }

    /// <summary>Brain Drain's intensity dial — the AUDIO half's, which upstream's own comment insists
    /// is not the blur's (<c>MainWindow/MainWindow.StartStop.cs:239-241</c>).</summary>
    private void OnBrainDrainIntensityMoved()
    {
        if (_syncing)
        {
            return;
        }

        _brainDrain.SetIntensityPercent((int)Math.Round(BrainDrainIntensitySlider.Value));
        _ = _session.BrainDrainPreset.Save();
        Refresh();
    }

    /// <summary>Brain Drain's volume dial (port-local — upstream plays this module at the app-wide
    /// master volume, which the port does not have).</summary>
    private void OnBrainDrainVolumeMoved()
    {
        if (_syncing)
        {
            return;
        }

        _brainDrain.SetVolumePercent((int)Math.Round(BrainDrainVolumeSlider.Value));
        _ = _session.BrainDrainPreset.Save();
        Refresh();
    }

    /// <summary>Brain Drain's high-refresh switch. It re-evaluates rather than waiting, and here that
    /// is behaviour rather than convention: the window IS the schedule, so a switch that only took
    /// effect at the next firing would leave a 5-second tick running after the user asked for
    /// 500 ms.</summary>
    private void OnBrainDrainHighRefreshToggled()
    {
        if (_syncing)
        {
            return;
        }

        var target = BrainDrainHighRefreshToggle.IsChecked == true;
        if (target == _brainDrain.Preset.HighRefresh)
        {
            return;
        }

        _brainDrain.SetHighRefresh(target);
        _ = _session.BrainDrainPreset.Save();
        Refresh();
    }

    /// <summary>One of the ramp's own switches — its end-at-complete flag and its two links. Not the
    /// module's enable: that one goes through <see cref="SessionEngine.QuickToggle"/> like every
    /// other module's, because it is the same gesture the rack row's right-click makes.</summary>
    private void OnRampSwitch(CheckBox toggle, bool current, Action<bool> write)
    {
        if (_syncing)
        {
            return;
        }

        var target = toggle.IsChecked == true;
        if (target == current)
        {
            return;
        }

        write(target);
        _ = _session.RampPreset.Save();
        Refresh();
    }

    /// <summary>
    /// The session's state can move on a thread that is not this one — teardown stops the engine
    /// from the shutdown path. This handler nonetheless touches controls directly, because since
    /// the marshalling landed on the PRODUCER: every module raises <c>Changed</c> through
    /// <see cref="EffectSignal"/>, which delivers on the UI thread whenever one exists. This page
    /// used to carry its own <c>CheckAccess</c>-or-<c>Post</c> copy, and so did the shell; two
    /// copies agreed and the fifteenth module's panel would not have.
    /// </summary>
    private void OnSessionChanged()
    {
        LoadDialsFromPreset();
        Refresh();
    }

    /// <summary>
    /// The style class that marks a control as "a running scripted session owns this value" —
    /// the port of <c>features:SessionLock.Owned="True"</c> (<c>Features/SessionLock.cs:50-55</c>).
    ///
    /// <para><b>THE CLASSIFICATION RULE</b>, carried here verbatim from the place upstream keeps it
    /// (<c>SessionLock.cs:21-38</c>) so that whoever adds the next dial reads it: marked is DOSAGE
    /// — the master enable, rates, counts, opacity, scale, speed, intensity, ramp endpoints, the
    /// values a session prescribes and ramps. Unmarked is COMFORT — volumes, click-to-dismiss,
    /// renderer choice, monitor placement, which asset folder — and everything remotely
    /// safety-shaped: stop, panic, strict lock, withdraw, exit. When in doubt leave it unmarked;
    /// over-locking takes control away for no benefit.</para>
    ///
    /// <para><b>The port adds one clause upstream does not need.</b> A dial here is only worth
    /// marking if its document is one of the ELEVEN a run actually borrows
    /// (<see cref="ScriptedSessionDials"/>, its constructor). Brain Drain, the Intensity Ramp's
    /// sliders, the Scheduler and Haptics all persist OUTSIDE those eleven, so a session neither
    /// overwrites them nor discards them at the restore, and greying them would be a lie in the
    /// other direction.</para>
    ///
    /// <para><b>THE CLAUSE IS ABOUT CUSTODY, NOT PRESCRIPTION, AND POP QUIZ IS WHY THAT SENTENCE IS
    /// HERE.</b> Two upstream facts look like a contradiction and are not.
    /// <list type="number">
    /// <item>Upstream marks BOTH pop quiz dials <c>Owned</c>
    /// (<c>Views/Tabs/GradedIntakeTabView.xaml:269</c> and <c>:286</c>).</item>
    /// <item>Upstream also says pop quiz is <i>"a user-level toggle (AppSettings), not per-session"</i>
    /// (<c>Services/Session/SessionEngine.cs:1406</c>), and it means it: the engine starts the
    /// service from <c>App.Settings.Current.PopQuizEnabled</c> (<c>:490</c>, <c>:1407</c>) and the
    /// per-session fields the model declares for it (<c>Models/Session.cs:913-916</c>) are read by
    /// NOTHING — its own built-in programs say the same, <i>"PopQuiz*, MiniGameEnabled and BrainDrain*
    /// are dead in the engine and are not touched"</i>
    /// (<c>Services/Program/BuiltInPrograms.cs:430</c>).</item>
    /// </list>
    /// They reconcile because <c>Owned</c> is not about a session PRESCRIBING a value. Upstream's run
    /// takes CUSTODY of these two: it snapshots them into its spare settings at
    /// <c>Services/Session/SessionEngine.cs:919-920</c> and writes them back at <c>:1544-1545</c>, so a dial the user
    /// moved mid-session is silently discarded at the end. That is the harm the lock prevents, and it
    /// is exactly what the clause above is testing for.</para>
    ///
    /// <para><b>So the two Pop Quiz dials on this page are deliberately NOT marked.</b> This port's
    /// run borrows eleven documents and <c>session_popquiz.json</c> is not one of them: nothing
    /// snapshots it, nothing writes it back, and a change the user makes during a session is theirs
    /// afterwards. Greying it would claim a custody this build does not take. That places Pop Quiz
    /// beside Brain Drain and Haptics, the two rows that ALREADY carry upstream <c>Owned</c> markers
    /// this port does not mirror (<c>Views/Controls/Studio/BrainDrainFeatureControl.xaml:98,155,179,200</c>
    /// and <c>Views/Tabs/HapticsTabView.xaml:587</c>) — the same divergence for the same one reason,
    /// not a new one. The other two exclusions above are NOT divergences at all and are worth
    /// separating: upstream marks nothing on its scheduler panel either
    /// (<c>Views/Controls/Studio/SchedulerRackPanel.xaml:45</c>, "NOTHING on this panel is
    /// SessionLock.Owned, and that is deliberate"), and the ramp's CURVE is marked in both trees
    /// while its sliders are marked in neither (<c>Views/Controls/Studio/RampRackPanel.xaml:24</c>,
    /// "CmbRampCurve is the ONLY SessionLock.Owned control here"). It also lands where
    /// upstream's own rule points when the two readings disagree: <i>"When in doubt, leave it
    /// unmarked. Over-locking takes control away from the user for no benefit"</i>
    /// (<c>Features/SessionLock.cs:36-38</c>).</para>
    ///
    /// <para><b>The close condition, so the next reader does not have to re-derive this:</b> the day
    /// <see cref="ScriptedSessionDials"/> takes a twelfth document and it is the pop quiz one, both
    /// dials must gain this marker in the same change — because that is the moment a mid-session edit
    /// starts being thrown away.</para>
    /// </summary>
    private const string SessionOwnedMarker = "session-owned";

    private static bool IsSessionOwnedMarker(Control control) =>
        control.Classes.Contains(SessionOwnedMarker);

    /// <summary>
    /// True while a scripted session is running — upstream's <c>IsSessionFeatureLockActive</c>
    /// (<c>MainWindow/MainWindow.SessionFeatureLock.cs:68-83</c>).
    ///
    /// <para><b>DERIVED, NEVER LATCHED</b>, which is upstream's rule 2 (<c>:30-37</c>) and the
    /// reason nothing here remembers "we locked": a crash, an abort, a window close or a stop that
    /// raised its events out of order cannot strand the user with a permanently dead rack. Like
    /// upstream it keys off the RUN's own liveness rather than any global flag, and it reads BOTH
    /// halves for upstream's reason — the run nulls its session inside <c>Stop</c>, so the two go
    /// false together on every exit path.</para>
    ///
    /// <para>Upstream additionally catches and fails open (<c>:78-82</c>) because its accessor
    /// reaches through a service locator that can be mid-teardown. This one reads two fields under
    /// the run's own lock and has nothing to throw, so an unreachable catch is left out rather
    /// than written for symmetry.</para>
    /// </summary>
    private bool IsSessionFeatureLockActive => _scripted.Running && _scripted.Current is not null;

    /// <summary>
    /// Re-derive the lock and repaint every control it owns — upstream's
    /// <c>RefreshSessionFeatureLock</c> (<c>MainWindow/MainWindow.SessionFeatureLock.cs:158-181</c>).
    /// Idempotent in both directions, so a session that ends while a panel is open re-enables
    /// everything in place.
    ///
    /// <para><b>Unlock is <c>ClearValue</c>, not <c>= true</c></b>, and that is upstream's decision
    /// with upstream's reason (<c>:440-448</c>): a control may be disabled for some OTHER reason,
    /// and hard-writing true would silently promote the user past that gate.</para>
    ///
    /// <para><b>The session's own writes are not suppressed.</b> A disabled <c>CheckBox</c> or
    /// <c>Slider</c> still takes a programmatic value, so <see cref="LoadDialsFromPreset"/> keeps
    /// showing the ramp climbing while the user cannot overrule it — upstream states exactly this
    /// (<c>:49-53</c>) and it is the whole point: the dial tells the truth instead of pretending.</para>
    /// </summary>
    private void ApplySessionFeatureLock()
    {
        var locked = IsSessionFeatureLockActive;
        foreach (var control in _sessionOwned)
        {
            if (locked)
            {
                control.IsEnabled = false;
            }
            else
            {
                control.ClearValue(IsEnabledProperty);
            }
        }

        SessionLockBanner.IsVisible = locked;
        SessionLockReason.Text = locked ? SessionLockNotices.Reason(_scripted.Current) : string.Empty;
    }

    private void LoadDialsFromPreset()
    {
        _syncing = true;
        try
        {
            var preset = _session.Preset.Current;
            FlashEnableToggle.IsChecked = preset.FlashEnabled;
            FlashFrequencySlider.Value = preset.FlashesPerHour;
            FlashImagesSlider.Value = preset.ImagesPerFlash;

            var subliminal = _session.SubliminalPreset.Current;
            SubliminalEnableToggle.IsChecked = subliminal.Enabled;
            SubliminalFrequencySlider.Value = subliminal.PerMinute;

            var pink = _session.PinkFilterPreset.Current;
            PinkFilterEnableToggle.IsChecked = pink.Enabled;
            PinkFilterOpacitySlider.Value = pink.OpacityPercent;

            var spiral = _session.SpiralPreset.Current;
            SpiralEnableToggle.IsChecked = spiral.Enabled;
            SpiralOpacitySlider.Value = spiral.OpacityPercent;
            LoadSpiralPicker(spiral.Path);
            PinkFilterTintPicker.SelectedIndex = StudioPickerNotices.TintIndexOf(pink.Colour);

            var bouncing = _session.BouncingTextPreset.Current;
            BouncingTextEnableToggle.IsChecked = bouncing.Enabled;
            BouncingTextSpeedSlider.Value = bouncing.Speed;
            BouncingTextSizeSlider.Value = bouncing.SizePercent;
            BouncingTextOpacitySlider.Value = bouncing.OpacityPercent;

            var ramp = _session.RampPreset.Current;
            RampEnableToggle.IsChecked = ramp.Enabled;
            RampDurationSlider.Value = ramp.DurationMinutes;
            RampMultiplierSlider.Value = ramp.Multiplier;
            RampEndSessionToggle.IsChecked = ramp.EndSessionOnComplete;
            RampLinkSpiralToggle.IsChecked = ramp.LinkSpiralOpacity;
            RampLinkPinkFilterToggle.IsChecked = ramp.LinkPinkFilterOpacity;
            RampLinkFlashToggle.IsChecked = ramp.LinkFlashOpacity;
            RampCurvePicker.SelectedIndex = ramp.Curve switch
            {
                RampCurve.EaseIn => 1,
                RampCurve.EaseOut => 2,
                RampCurve.SCurve => 3,
                RampCurve.Exponential => 4,
                _ => 0,
            };

            var mindWipe = _session.MindWipePreset.Current;
            MindWipeEnableToggle.IsChecked = mindWipe.Enabled;
            MindWipeFrequencySlider.Value = mindWipe.PerHour;
            MindWipeVolumeSlider.Value = mindWipe.VolumePercent;

            var brainDrain = _session.BrainDrainPreset.Current;
            BrainDrainEnableToggle.IsChecked = brainDrain.Enabled;
            BrainDrainIntensitySlider.Value = brainDrain.IntensityPercent;
            BrainDrainVolumeSlider.Value = brainDrain.VolumePercent;
            BrainDrainHighRefreshToggle.IsChecked = brainDrain.HighRefresh;

            var lockCard = _session.LockCardPreset.Current;
            LockCardEnableToggle.IsChecked = lockCard.Enabled;
            LockCardFrequencySlider.Value = lockCard.PerHour;
            LockCardRepeatsSlider.Value = lockCard.Repeats;
            LockCardStrictToggle.IsChecked = lockCard.Strict;

            var video = _session.MandatoryVideoPreset.Current;
            MandatoryVideoEnableToggle.IsChecked = video.Enabled;
            MandatoryVideoFrequencySlider.Value = video.PerHour;
            MandatoryVideoMaxLengthSlider.Value = video.MaxSeconds;

            var bubbleCount = _session.BubbleCountPreset.Current;
            BubbleCountEnableToggle.IsChecked = bubbleCount.Enabled;
            BubbleCountFrequencySlider.Value = bubbleCount.PerHour;
            BubbleCountDifficultySlider.Value = (int)bubbleCount.Difficulty;

            var popQuiz = _session.PopQuizPreset.Current;
            PopQuizEnableToggle.IsChecked = popQuiz.Enabled;
            PopQuizFrequencySlider.Value = popQuiz.PerHour;

            var bubblePop = _session.BubblePopPreset.Current;
            BubblePopEnableToggle.IsChecked = bubblePop.Enabled;
            BubblePopFrequencySlider.Value = bubblePop.PerMinute;
            BubblePopSizeSlider.Value = bubblePop.SizePercent;
            BubblePopSpeedSlider.Value = bubblePop.SpeedBoostPercent;

            // No enable box to load: the Visuals row has none, upstream has none, and this
            // is the only block here with three slider lines and no checkbox line.
            var visuals = _session.VisualsPreset.Current;
            VisualsScaleSlider.Value = visuals.ImageScalePercent;
            VisualsOpacitySlider.Value = visuals.FlashOpacityPercent;
            VisualsDurationSlider.Value = visuals.FlashDurationSeconds;

            // Nine controls, and the two TEXT ones are loaded back VERBATIM — the raw
            // string the document holds, not the parsed value — because the parse is lossy in both
            // directions here: "8" would come back as "8.00:00:00" and "25:00" would come back as
            // "16:00", and a box that silently rewrites what the user typed is a box that hides
            // the very mistake the reading line exists to show them.
            var scheduler = _scheduler.Preset;
            SchedulerEnableToggle.IsChecked = scheduler.Enabled;
            SchedulerStartTimeBox.Text = scheduler.StartTime;
            SchedulerEndTimeBox.Text = scheduler.EndTime;
            SchedulerDayMon.IsChecked = scheduler.Monday;
            SchedulerDayTue.IsChecked = scheduler.Tuesday;
            SchedulerDayWed.IsChecked = scheduler.Wednesday;
            SchedulerDayThu.IsChecked = scheduler.Thursday;
            SchedulerDayFri.IsChecked = scheduler.Friday;
            SchedulerDaySat.IsChecked = scheduler.Saturday;
            SchedulerDaySun.IsChecked = scheduler.Sunday;

            // Loaded from the DOCUMENT rather than from whatever the user just clicked — which is
            // what makes a refused tick visible: the gate wrote nothing, so this puts the box back
            // where the setting really is.
            HapticsEnableToggle.IsChecked = _haptics.Enabled;
            HapticsLovenseToggle.IsChecked = _haptics.Preset.Current.LovenseEnabled;
            HapticsButtplugToggle.IsChecked = _haptics.Preset.Current.ButtplugEnabled;

            // The app-wide audio document, whose defaults are upstream's own on a fresh install
            // (32 and 50, Models/AppSettings.cs:1127 and :1134). NOT the picker: its items are the
            // machine's endpoints rather than a stored value, and enumerating them here would put a
            // native audio context on every reload of these dials — see EnsureAudioDevicesListed.
            AudioMasterSlider.Value = _audio.MasterVolume;
            AudioVideoSlider.Value = _audio.VideoVolume;
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// Repaint what is true right now. Every dot's classes come from
    /// <see cref="ISessionEffect.Dot"/> — the effect's own derived state, never a bool this
    /// page keeps — so a row cannot claim a module is running after its schedule is gone, or
    /// after its surface was refused.
    /// </summary>
    private void Refresh()
    {
        var preset = _session.Preset.Current;
        FlashFrequencyValue.Text = preset.FlashesPerHour.ToString(System.Globalization.CultureInfo.CurrentCulture);
        FlashImagesValue.Text = preset.ImagesPerFlash.ToString(System.Globalization.CultureInfo.CurrentCulture);

        RenderedFlashDot = PaintDot(FlashRowDot, _flash);
        FlashLiveState.Text = DescribeState(RenderedFlashDot, _flash.FlashCount, _flash.Last);
        FlashPoolState.Text = DescribePool(_flash.Last);
        FlashSurfaceState.Text = DescribeSurface(_session.Surface.LastPlacement);

        // The Visuals row. NO PaintDot CALL, and that is the row's whole shape: it has no
        // Ellipse in the visual tree to paint, because upstream gives it no dot
        // (StudioTabView.xaml.cs:494-496). Its three numbers come from the same reading the
        // presenter will use for the next flash, so the panel and the screen cannot disagree.
        var draw = _visuals.Draw();
        VisualsScaleValue.Text = draw.ScalePercent.ToString(System.Globalization.CultureInfo.CurrentCulture);
        VisualsOpacityValue.Text = draw.OpacityPercent.ToString(System.Globalization.CultureInfo.CurrentCulture);
        VisualsDurationValue.Text = draw.DurationSeconds.ToString(System.Globalization.CultureInfo.CurrentCulture);
        VisualsOwnershipState.Text = VisualsPanelNotices.DescribeOwnership(_flash.Enabled);
        VisualsDialState.Text = VisualsPanelNotices.DescribeDials(draw);
        VisualsAbsenceState.Text = VisualsPanelNotices.DescribeAbsences();
        VisualsSurfaceState.Text = VisualsPanelNotices.DescribeSurface(_session.Surface.LastPlacement);

        // The Scheduler row. Its dot is NOT PaintDot's: that overload takes an
        // ISessionEffect and this row has none, which is the structural half of "it drives the
        // engine rather than living in it". The three states are the module's own derived state
        // (SessionScheduler.Dot) and are painted with the same two classes every other dot uses.
        var schedulerReading = _scheduler.Reading;
        RenderedSchedulerDot = PaintSchedulerDot(SchedulerRowDot, _scheduler.Dot);
        SchedulerWhatItIs.Text = SchedulerPanelNotices.DescribeWhatItIs(_scheduler.Enabled);
        SchedulerLiveState.Text = SchedulerPanelNotices.DescribeLiveState(
            RenderedSchedulerDot, _scheduler.Enabled, _scheduler.Polling, schedulerReading,
            _session.Engine.Running);
        SchedulerReadingState.Text = SchedulerPanelNotices.DescribeReading(schedulerReading);
        SchedulerLastTickState.Text = SchedulerPanelNotices.DescribeLastTick(_scheduler.Last);
        SchedulerAbsenceState.Text = SchedulerPanelNotices.DescribeAbsences();

        // The Haptics row. Its dot goes through the same non-effect overload the scheduler
        // uses, for the same structural reason: this row is not on SessionEngine.Effects and never
        // will be, because it is APP-scoped (App.xaml.cs:533, :2060) and the engine never touches it.
        // The sink line is the sink's OWN last answer, so the panel and the capability the System
        // page reports cannot tell two different stories.
        var hapticsReachable = _haptics.LastObservation is { Confirmed: true };
        RenderedHapticsDot = PaintSchedulerDot(HapticsRowDot, _haptics.Dot);
        HapticsWhatItIs.Text = HapticsPanelNotices.DescribeWhatItIs();
        HapticsLiveState.Text = HapticsPanelNotices.DescribeLiveState(
            RenderedHapticsDot, _haptics.Enabled, hapticsReachable);
        HapticsGateState.Text = HapticsPanelNotices.DescribeGate(_haptics.Gate);
        HapticsSinkState.Text = HapticsPanelNotices.DescribeSink(_haptics.SinkState);
        HapticsAbsenceState.Text = HapticsPanelNotices.DescribeAbsences();

        // The Audio row. NO PaintDot AND NO PaintSchedulerDot CALL — it has no Ellipse to paint,
        // for the Visuals row's reason: there is no enable to switch, nothing to arm and no
        // schedule to be live on. What the operating system last said about a device goes in words
        // instead, where "nothing has been asked yet" can be said at all.
        AudioWhatItIs.Text = AudioDialsNotices.DescribeWhatItIs();
        AudioMasterValue.Text = $"{_audio.MasterVolume}%";
        AudioVideoValue.Text = $"{_audio.VideoVolume}%";
        AudioMasterState.Text = AudioDialsNotices.DescribeMaster(_audio.MasterVolume);
        AudioVideoState.Text = AudioDialsNotices.DescribeVideo(_audio.VideoVolume);
        AudioChoiceState.Text =
            AudioDialsNotices.DescribeChoice(_audio.OutputDeviceName, AudioChoiceConnected);
        AudioDeviceState.Text =
            AudioDialsNotices.DescribeDeviceOutcome(_audio.DeviceOutcome, _audio.DeviceInitAttempts);
        AudioTestState.Text = _audioTest;

        var subliminal = _session.SubliminalPreset.Current;
        SubliminalFrequencyValue.Text = subliminal.PerMinute.ToString(System.Globalization.CultureInfo.CurrentCulture);
        RenderedSubliminalDot = PaintDot(SubliminalRowDot, _subliminals);
        SubliminalLiveState.Text = DescribeSubliminalState(
            RenderedSubliminalDot, _subliminals.SubliminalCount, _subliminals.Last);
        SubliminalPoolState.Text = DescribePhrasePool(_subliminals.ActivePhraseCount);
        SubliminalSurfaceState.Text = DescribeSubliminalSurface(_session.SubliminalSurface.LastPlacement);

        var pink = _session.PinkFilterPreset.Current;
        PinkFilterOpacityValue.Text = $"{pink.OpacityPercent}%";
        RenderedPinkFilterDot = PaintDot(PinkFilterRowDot, _pinkFilter);
        var tint = _pinkFilter.Tint;
        PinkFilterSwatch.Fill = new SolidColorBrush(Color.FromRgb(tint.Red, tint.Green, tint.Blue));
        PinkFilterTintState.Text = DescribeTint(tint, PinkFilterColour.TryParseHex(pink.Colour, out _));
        PinkFilterLiveState.Text = DescribePinkFilterState(
            RenderedPinkFilterDot, tint, _session.Engine.Running, _pinkFilter.LastPlacement);
        PinkFilterSurfaceState.Text = DescribePinkFilterSurface(_pinkFilter.LastPlacement);

        var spiral = _session.SpiralPreset.Current;
        SpiralOpacityValue.Text = $"{spiral.OpacityPercent}%";
        RenderedSpiralDot = PaintDot(SpiralRowDot, _spiral);
        SpiralLiveState.Text = DescribeSpiralState(
            RenderedSpiralDot, _spiral.Presentation, _spiral.SpiralPath is not null, _spiral.Showing,
            _spiral.FrameCount, _session.Engine.Running, _spiral.LastPlacement);
        SpiralLibraryState.Text = DescribeSpiralLibrary(_spiral.SpiralPath, _session.SpiralsFolder);
        SpiralSurfaceState.Text = DescribeSpiralSurface(_spiral.LastPlacement);

        var bouncing = _session.BouncingTextPreset.Current;
        BouncingTextSpeedValue.Text = bouncing.Speed.ToString(System.Globalization.CultureInfo.CurrentCulture);
        BouncingTextSizeValue.Text = $"{bouncing.SizePercent}%";
        BouncingTextOpacityValue.Text = $"{bouncing.OpacityPercent}%";
        BouncingTextAbsentNotice.Text = BouncingTextEffect.TransformsAbsentNotice;
        RenderedBouncingTextDot = PaintDot(BouncingTextRowDot, _bouncingText);
        BouncingTextLiveState.Text = DescribeBouncingTextState(
            RenderedBouncingTextDot, _bouncingText.Presentation, _bouncingText.Showing,
            _bouncingText.Bounces, _session.Engine.Running);
        BouncingTextPoolState.Text = DescribeBouncingTextPool(bouncing.Phrases);
        BouncingTextSurfaceState.Text = DescribeBouncingTextSurface(_bouncingText.LastPlacement);

        var ramp = _ramp.Preset;
        RampDurationValue.Text = $"{ramp.DurationMinutes} min";
        RampMultiplierValue.Text =
            $"{ramp.Multiplier.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture)}x";
        RenderedRampDot = PaintDot(RampRowDot, _ramp);
        var held = HeldDials();
        RampLiveState.Text = RampPanelNotices.DescribeRampState(
            RenderedRampDot, ramp, _ramp.Progress, _ramp.CurrentMultiplier, held.Count,
            _session.Engine.Running);
        RampCustodyState.Text = RampPanelNotices.DescribeRampCustody(_ramp.Dials.Count, held);

        // The two audio rows. Their closing line is the CAPABILITY's own typed outcome plus the OS's
        // own last measurement — never a sentence this page composed about a platform, which is the
        // same rule the four drawing panels' surface lines follow.
        var mindWipePreset = _session.MindWipePreset.Current;
        MindWipeFrequencyValue.Text =
            mindWipePreset.PerHour.ToString(System.Globalization.CultureInfo.CurrentCulture);
        MindWipeVolumeValue.Text = $"{mindWipePreset.VolumePercent}%";
        RenderedMindWipeDot = PaintDot(MindWipeRowDot, _mindWipe);
        MindWipeLiveState.Text = AudioPanelNotices.DescribeCueState(
            RenderedMindWipeDot, "clip", _mindWipe.CueCount, _mindWipe.Last, _session.Engine.Running,
            _mindWipe.Presence.IsRendering);
        MindWipeClipState.Text = AudioPanelNotices.DescribeClipPool(_mindWipe.ClipCount, _mindWipe.ClipFolder);
        MindWipeAudioState.Text = AudioPanelNotices.DescribeAudioCapability(
            _session.Audio.LastOpen, _session.Audio.LastObservation);

        var brainDrainPreset = _session.BrainDrainPreset.Current;
        BrainDrainIntensityValue.Text = $"{brainDrainPreset.IntensityPercent}%";
        BrainDrainVolumeValue.Text = $"{brainDrainPreset.VolumePercent}%";
        RenderedBrainDrainDot = PaintDot(BrainDrainRowDot, _brainDrain);
        BrainDrainLiveState.Text = AudioPanelNotices.DescribeCueState(
            RenderedBrainDrainDot, "clip", _brainDrain.CueCount, _brainDrain.Last, _session.Engine.Running,
            _brainDrain.Presence.IsRendering);
        BrainDrainClipState.Text =
            AudioPanelNotices.DescribeClipPool(_brainDrain.ClipCount, _brainDrain.ClipFolder);
        BrainDrainAudioState.Text = AudioPanelNotices.DescribeAudioCapability(
            _session.Audio.LastOpen, _session.Audio.LastObservation);
        // The module's own constant, rendered verbatim. The same string the arm result's reason
        // carries, so the panel and the typed outcome are one account of the absence rather than two.
        BrainDrainVisualHalfState.Text = BrainDrainEffect.VisualHalfNotice;

        // The one row that ASKS. Its closing line is the input capability's own typed outcome plus
        // the OS's own last read-back, on the same rule the audio rows follow: what the user reads
        // about a resource this process does not own is QUOTED, never composed here.
        var lockCard = _lockCard.Preset;
        LockCardFrequencyValue.Text =
            lockCard.PerHour.ToString(System.Globalization.CultureInfo.CurrentCulture);
        LockCardRepeatsValue.Text = $"{lockCard.Repeats}x";
        LockCardInterruptionNotice.Text = InputPanelNotices.InterruptionNotice;
        LockCardScopeNotice.Text = InputPanelNotices.ScopeNotice;
        RenderedLockCardDot = PaintDot(LockCardRowDot, _lockCard);
        LockCardLiveState.Text = InputPanelNotices.DescribeCardState(
            RenderedLockCardDot, _lockCard.CardCount, _lockCard.Last, _session.Engine.Running,
            _lockCard.Presence.CanReachAUser, _lockCard.Presence.IsPrompting,
            _lockCard.Presence.HoldsTheInput, _lockCard.LastResolution, _lockCard.LastPrompt);
        LockCardDemandState.Text = InputPanelNotices.DescribeDemand(lockCard.Repeats, lockCard.Strict);
        LockCardPhraseState.Text = InputPanelNotices.DescribePhrasePool(
            _lockCard.PhraseCount, lockCard.EnabledPhrases());
        LockCardCaptureState.Text = InputPanelNotices.DescribeInputCapability(
            _lockCard.Presence.LastPrompt, _lockCard.Presence.LastObservation);

        // The one row that plays a FILE. Its closing line is the video capability's own typed
        // outcome plus the OS's own last read-back, on the same rule the audio and input rows
        // follow: what the user reads about a resource this process does not own is QUOTED.
        var video = _session.MandatoryVideoPreset.Current;
        MandatoryVideoFrequencyValue.Text =
            video.PerHour.ToString(System.Globalization.CultureInfo.CurrentCulture);
        MandatoryVideoMaxLengthValue.Text = video.MaxSeconds == 0
            ? "no cap"
            : $"{video.MaxSeconds}s";
        // The module's own constant, rendered verbatim. The same string the arm result's reason
        // carries, so the panel and the typed outcome are one account of the absence rather than two.
        MandatoryVideoSilentHalfState.Text = MandatoryVideoEffect.VideoPanelNoticeText;
        RenderedMandatoryVideoDot = PaintDot(MandatoryVideoRowDot, _mandatoryVideo);
        MandatoryVideoLiveState.Text = VideoPanelNotices.DescribeVideoState(
            RenderedMandatoryVideoDot, _mandatoryVideo.PlayedCount, _mandatoryVideo.Last,
            _session.Engine.Running, _session.VideoSurface.CanReachADisplay, _mandatoryVideo.Playing,
            _mandatoryVideo.FramesDecoded, _mandatoryVideo.FramesHeld, _mandatoryVideo.FramesAdvanced);
        MandatoryVideoClipState.Text =
            VideoPanelNotices.DescribeClipPool(_mandatoryVideo.ClipCount, _mandatoryVideo.ClipFolder);
        MandatoryVideoSurfaceState.Text = VideoPanelNotices.DescribeVideoCapability(
            _session.VideoSurface.LastPlacement, _session.VideoSurface.LastObservation);

        // The one row that needs TWO capabilities. Its closing line quotes both of them, because
        // either can refuse on its own and a single sentence would be false for one of the two.
        var bubbleCount = _bubbleCount.Preset;
        BubbleCountFrequencyValue.Text =
            bubbleCount.PerHour.ToString(System.Globalization.CultureInfo.CurrentCulture);
        BubbleCountDifficultyValue.Text = bubbleCount.Difficulty.ToString();
        BubbleCountInterruptionNotice.Text = BubbleCountPanelNotices.InterruptionNotice;
        BubbleCountScopeNotice.Text = BubbleCountPanelNotices.ScopeNotice;
        RenderedBubbleCountDot = PaintDot(BubbleCountRowDot, _bubbleCount);
        BubbleCountLiveState.Text = BubbleCountPanelNotices.DescribeGameState(
            RenderedBubbleCountDot, _bubbleCount.PlayedCount, _bubbleCount.Last, _session.Engine.Running,
            _session.VideoSurface.CanReachADisplay, _bubbleCount.Presence.CanReachAUser,
            _bubbleCount.Playing, _bubbleCount.Asking, _bubbleCount.LastResolution);
        BubbleCountDifficultyState.Text =
            BubbleCountPanelNotices.DescribeDifficulty(bubbleCount.Difficulty);
        BubbleCountClipState.Text =
            BubbleCountPanelNotices.DescribeClipPool(_bubbleCount.ClipCount, _bubbleCount.ClipFolder);
        BubbleCountCapabilityState.Text = BubbleCountPanelNotices.DescribeBothCapabilities(
            _bubbleCount.LastPlayback, _bubbleCount.LastPrompt);

        // The SECOND row on this page that asks a question through the shared input capability, and
        // the only one whose answer can never be wrong (Services/Quiz/PopQuizService.cs:12).
        var popQuizDials = _popQuiz.Preset;
        PopQuizFrequencyValue.Text = PopQuizPanelNotices.DescribeFrequency(popQuizDials.PerHour);
        PopQuizInterruptionNotice.Text = PopQuizPanelNotices.InterruptionNotice;
        PopQuizScopeNotice.Text = PopQuizPanelNotices.ScopeNotice;
        PopQuizTestNotice.Text = PopQuizPanelNotices.NoTestButtonNotice;
        RenderedPopQuizDot = PaintDot(PopQuizRowDot, _popQuiz);
        PopQuizLiveState.Text = PopQuizPanelNotices.DescribeQuizState(
            RenderedPopQuizDot, _popQuiz.QuizCount, _popQuiz.Last, _session.Engine.Running,
            _popQuiz.Presence.CanReachAUser, _popQuiz.Ask is not null, _popQuiz.AnsweredCount,
            _popQuiz.SkippedCount, _popQuiz.LastResolution);
        PopQuizPoolState.Text = PopQuizPanelNotices.DescribePool(
            _popQuiz.QuestionCount, PopQuizQuestion.AnswerCount);
        PopQuizXpState.Text = PopQuizPanelNotices.DescribeXp(_popQuiz.BanksXp, _popQuiz.LastGrant);
        PopQuizCapabilityState.Text = PopQuizPanelNotices.DescribeInputCapability(
            _popQuiz.LastPrompt, _popQuiz.Presence.LastObservation);

        // The one row the user ACTS on. Its live line has a clause no other row's has — targets are
        // up and the window manager routes clicks at none of them — because that is a state only
        // this row can be in and it is invisible from anywhere else.
        var bubblePop = _bubblePop.Settings;
        BubblePopFrequencyValue.Text = PointerPanelNotices.DescribeSpawnRate(bubblePop.PerMinute);
        BubblePopSizeValue.Text = PointerPanelNotices.DescribeSize(bubblePop.SizePercent);
        BubblePopSpeedValue.Text = PointerPanelNotices.DescribeSpeed(bubblePop.SpeedBoostPercent);
        BubblePopInterruptionNotice.Text = PointerPanelNotices.InterruptionNotice;
        BubblePopScopeNotice.Text = PointerPanelNotices.ScopeNotice;
        BubblePopEvidenceNotice.Text = PointerPanelNotices.EvidenceNotice;
        RenderedBubblePopDot = PaintDot(BubblePopRowDot, _bubblePop);
        var (targetsUp, routable) = _bubblePop.Targets;
        BubblePopLiveState.Text = PointerPanelNotices.DescribeFieldState(
            RenderedBubblePopDot, _session.Engine.Running, _session.BubblePopSurface.CanReachAPointer,
            targetsUp, routable, _bubblePop.Popped, _bubblePop.Missed);
        var (presses, refused) = _bubblePop.Delivery;
        BubblePopDeliveryState.Text = PointerPanelNotices.DescribeDelivery(presses, refused);
        BubblePopCapabilityState.Text = PointerPanelNotices.DescribeCapability(_bubblePop.LastPlacement);

        // Last, and from this page's ONE repaint entry rather than from a clock of its own: a
        // scripted session moves on its own tick, but every other thing that repaints this page can
        // also change what its readout should say (its start armed every module above).
        RenderScriptedSession();
    }

    /// <summary>
    /// The bubbles-per-minute slider writes the dial and re-times the live spawn timer — the port's
    /// standing convention, and upstream's own <c>RefreshFrequency()</c> on the same
    /// gesture (<c>Features/BubblePopFeatureControl.xaml.cs:116</c>).
    /// </summary>
    private void OnBubblePopFrequencyMoved()
    {
        if (_syncing)
        {
            return;
        }

        _bubblePop.SetPerMinute((int)Math.Round(BubblePopFrequencySlider.Value));
        _ = _session.BubblePopPreset.Save();
        Refresh();
    }

    /// <summary>
    /// The size slider. It changes the NEXT bubble, never one already on screen: this port's pointer
    /// surface refuses to resize a placed window, which is upstream's own #494 deadlock
    /// (<c>docs/primers/BUBBLE_POP_PRIMER.md</c> §9.1) turned into a rule rather than a comment.
    /// </summary>
    private void OnBubblePopSizeMoved()
    {
        if (_syncing)
        {
            return;
        }

        _bubblePop.SetSizePercent((int)Math.Round(BubblePopSizeSlider.Value));
        _ = _session.BubblePopPreset.Save();
        Refresh();
    }

    /// <summary>The extra-speed slider. Applied at the next spawn, where upstream applies it — in
    /// the bubble's own constructor (<c>Services/BubbleService.cs:2831-2834</c>).</summary>
    private void OnBubblePopSpeedMoved()
    {
        if (_syncing)
        {
            return;
        }

        _bubblePop.SetSpeedBoostPercent((int)Math.Round(BubblePopSpeedSlider.Value));
        _ = _session.BubblePopPreset.Save();
        Refresh();
    }

    /// <summary>
    /// The Visuals size slider — WPF's <c>SliderSize_Changed</c>
    /// (<c>Features/VisualsFeatureControl.xaml.cs:67-76</c>): write the setting, save, and that is
    /// all. It changes the NEXT flash, never one already on screen, for the reason the two below
    /// share (D174).
    /// </summary>
    private void OnVisualsScaleMoved()
    {
        if (_syncing)
        {
            return;
        }

        _visuals.SetImageScalePercent((int)Math.Round(VisualsScaleSlider.Value));
        _ = _session.VisualsPreset.Save();
        Refresh();
    }

    /// <summary>
    /// The Visuals opacity slider — WPF's <c>SliderOpacity_Changed</c> (<c>:78-87</c>).
    ///
    /// <para><b>It reaches the next flash, not one already up, and that is a real divergence</b>
    /// rather than a rounding of upstream. WPF recomputes <c>maxAlpha</c> on every composition
    /// frame and re-tints live windows (<c>Services/Flash/FlashService.cs:2072</c>, applied
    /// <c>:2108-2117</c>). Here the value becomes a layered window's <c>LWA_ALPHA</c> at placement,
    /// and changing it afterwards means re-<c>Present</c>ing — which clears click-through to run
    /// its differential hit test and restores it (<c>Overlay/Win32OverlayPresence.cs:558</c>,
    /// <c>:566</c>, <c>:574</c>). Opening that gap on a surface whose whole contract is that the
    /// user's clicks pass through it costs more than the re-tint is worth. D174.</para>
    /// </summary>
    private void OnVisualsOpacityMoved()
    {
        if (_syncing)
        {
            return;
        }

        _visuals.SetFlashOpacityPercent((int)Math.Round(VisualsOpacitySlider.Value));
        _ = _session.VisualsPreset.Save();
        Refresh();
    }

    /// <summary>
    /// The Visuals duration slider — WPF's <c>SliderDuration_Changed</c> (<c>:100-109</c>). It sets
    /// the lifetime the NEXT flash's surfaces are given; upstream is the same, because the lifetime
    /// is computed once per flash and handed to each window (<c>FlashService.cs:1073</c>).
    /// </summary>
    private void OnVisualsDurationMoved()
    {
        if (_syncing)
        {
            return;
        }

        _visuals.SetFlashDurationSeconds((int)Math.Round(VisualsDurationSlider.Value));
        _ = _session.VisualsPreset.Save();
        Refresh();
    }

    /// <summary>
    /// The games-per-hour slider writes the dial and re-paces the live schedule, the port's standing
    /// convention, and upstream's own <c>RefreshSchedule</c>
    /// (<c>Services/BubbleCountService.cs:735-739</c>).
    /// </summary>
    private void OnBubbleCountFrequencyMoved()
    {
        if (_syncing)
        {
            return;
        }

        _bubbleCount.SetPerHour((int)Math.Round(BubbleCountFrequencySlider.Value));
        _ = _session.BubbleCountPreset.Save();
        Refresh();
    }

    /// <summary>
    /// The difficulty slider. It changes the NEXT game, never the one on screen: upstream reads the
    /// setting once, at trigger time (<c>Services/BubbleCountService.cs:243</c>), and a target that
    /// moved under a user mid-clip would be a count nobody could get right.
    /// </summary>
    private void OnBubbleCountDifficultyMoved()
    {
        if (_syncing)
        {
            return;
        }

        var value = (int)Math.Round(BubbleCountDifficultySlider.Value);
        _bubbleCount.SetDifficulty((BubbleCountDifficulty)value);
        _ = _session.BubbleCountPreset.Save();
        Refresh();
    }

    /// <summary>
    /// The questions-per-hour slider writes the dial and re-paces the live schedule, the port's
    /// standing convention — and here it is upstream's own behaviour rather than a convention: the
    /// service recomputes its interval from the CURRENT setting on every tick
    /// (<c>Services/Quiz/PopQuizService.cs:163-171</c>), so a raised rate takes effect at the next
    /// question rather than after the old interval expires.
    /// </summary>
    private void OnPopQuizFrequencyMoved()
    {
        if (_syncing)
        {
            return;
        }

        _popQuiz.SetPerHour((int)Math.Round(PopQuizFrequencySlider.Value));
        _ = _session.PopQuizPreset.Save();
        Refresh();
    }

    /// <summary>
    /// The cards-per-hour slider writes the dial and re-paces the live schedule — the same order the
    /// flash frequency slider uses, and upstream writes then saves
    /// (<c>Features/LockCardFeatureControl.xaml.cs:97-106</c>). The re-pace is this port's standing
    /// convention, so a raised frequency takes effect now rather than after the old
    /// interval expires.
    /// </summary>
    private void OnLockCardFrequencyMoved()
    {
        if (_syncing)
        {
            return;
        }

        _lockCard.SetPerHour((int)Math.Round(LockCardFrequencySlider.Value));
        _ = _session.LockCardPreset.Save();
        Refresh();
    }

    /// <summary>
    /// The repeats slider writes the dial and does NOT re-pace: the count is read at the moment a
    /// card is shown (<c>Services/LockCard/LockCardService.cs:294</c>), and a card whose target moved
    /// under the user mid-typing would be the cruellest live-apply on this page.
    /// </summary>
    private void OnLockCardRepeatsMoved()
    {
        if (_syncing)
        {
            return;
        }

        _lockCard.SetRepeats((int)Math.Round(LockCardRepeatsSlider.Value));
        _ = _session.LockCardPreset.Save();
        Refresh();
    }

    /// <summary>Strict mode, same rule as the repeats dial: read at show time
    /// (<c>LockCardService.cs:295</c>), so it changes the next card and never the one on screen.</summary>
    private void OnLockCardStrictToggled()
    {
        if (_syncing)
        {
            return;
        }

        var target = LockCardStrictToggle.IsChecked == true;
        if (target == _lockCard.Preset.Strict)
        {
            return;
        }

        _lockCard.SetStrict(target);
        _ = _session.LockCardPreset.Save();
        Refresh();
    }

    /// <summary>
    /// What the ramp is holding right now, as the panel reports it. Read off the MODULE's own custody
    /// list and the dials' own current values — never off the ramp's arithmetic — so a line that says
    /// "10% → 13%" is two facts the user can check against the other module's panel rather than a
    /// prediction.
    /// </summary>
    private IReadOnlyList<RampDialHold> HeldDials()
    {
        var holds = new List<RampDialHold>(_ramp.Dials.Count);
        foreach (var dial in _ramp.Dials)
        {
            if (_ramp.BaseValueFor(dial.Id) is { } baseValue)
            {
                holds.Add(new RampDialHold(dial.Label, baseValue, dial.Read()));
            }
        }

        return holds;
    }

    private static EffectDotState PaintDot(Shape dot, ISessionEffect effect)
    {
        var state = effect.Dot;
        dot.Classes.Set("armed", state == EffectDotState.Armed);
        dot.Classes.Set("live", state == EffectDotState.Live);
        return state;
    }

    /// <summary>
    /// The same two classes, from a state that did not come from an <see cref="ISessionEffect"/>.
    /// The Scheduler row is the only one on this page with no module behind it AND a dot in front of it,
    /// so this overload exists rather than a fake effect wrapper: an <c>ISessionEffect</c> the
    /// engine never arms would be a lie in the type system to save four lines here.
    /// </summary>
    private static EffectDotState PaintSchedulerDot(Shape dot, EffectDotState state)
    {
        dot.Classes.Set("armed", state == EffectDotState.Armed);
        dot.Classes.Set("live", state == EffectDotState.Live);
        return state;
    }

    /// <summary>
    /// Where the images went, according to the SURFACE.
    ///
    /// <para>This line used to be a fixed sentence saying the drawing half was not ported. The overlay
    /// made that false on Windows and left it true on Linux, and no fixed sentence can be both. The
    /// replacement asserts nothing about the platform — it reports the presenter's own last typed
    /// outcome, so a build where the overlay refuses shows the refusal's own reason and manual gate,
    /// and a build where it works says so only because the OS confirmed it.</para>
    ///
    /// <para>Before anything has been attempted it names the mechanism and says nothing has been
    /// drawn yet. That is deliberate: a user must not have to press START and watch to find out how
    /// this effect reaches the screen, and "nothing has been drawn yet" is a fact about this session
    /// rather than a claim about a surface nobody has asked.</para>
    /// </summary>
    public static string DescribeSurface(CapabilityState? placement) => placement switch
    {
        null => "Flashes are drawn on an always-on-top, click-through overlay above your other "
            + "windows. Nothing has been drawn yet.",
        CapabilityState.Available => "The last flash was placed on an always-on-top overlay surface "
            + "above your other windows.",
        CapabilityState.Unavailable u => $"Nothing was drawn on screen: {u.Reason.Detail}",
        CapabilityState.Degraded d => $"Partly drawn: {d.SurvivingSemantics}. {d.Reason.Detail}",
        CapabilityState.PermissionRequired p => $"Nothing was drawn on screen: {p.Reason.Detail}",
        CapabilityState.DependencyMissing m => $"Nothing was drawn on screen: {m.Reason.Detail}",
        CapabilityState.Faulted f => $"Nothing was drawn on screen: {f.Reason.Detail}",
        // The hierarchy is closed (CapabilityState's constructor is private), so this arm is
        // unreachable today; it is the codebase's own convention for these switches
        // (ChaosTunnelService.Describe) and it prints the state rather than inventing a sentence.
        _ => placement.ToString() ?? string.Empty,
    };

    /// <summary>The same rule for the Subliminals card, with the module's own nouns. Derived from
    /// the presenter's last typed outcome for the reason above, never from a platform check.</summary>
    public static string DescribeSubliminalSurface(CapabilityState? placement) => placement switch
    {
        null => "Subliminal cards are drawn on an always-on-top, click-through overlay above your "
            + "other windows. Nothing has been drawn yet.",
        CapabilityState.Available => "The last subliminal was placed on an always-on-top overlay "
            + "surface above your other windows.",
        CapabilityState.Unavailable u => $"Nothing was drawn on screen: {u.Reason.Detail}",
        CapabilityState.Degraded d => $"Partly drawn: {d.SurvivingSemantics}. {d.Reason.Detail}",
        CapabilityState.PermissionRequired p => $"Nothing was drawn on screen: {p.Reason.Detail}",
        CapabilityState.DependencyMissing m => $"Nothing was drawn on screen: {m.Reason.Detail}",
        CapabilityState.Faulted f => $"Nothing was drawn on screen: {f.Reason.Detail}",
        _ => placement.ToString() ?? string.Empty,
    };

    /// <summary>
    /// The same rule again for the tint — and the tense is different on purpose. The other two
    /// modules place something that is gone a moment later, so their line is about the LAST one;
    /// this one places something that stays, so its line is about what is on screen NOW.
    /// </summary>
    public static string DescribePinkFilterSurface(CapabilityState? placement) => placement switch
    {
        null => "The tint is drawn on an always-on-top, click-through overlay above your other "
            + "windows. Nothing has been drawn yet.",
        CapabilityState.Available => "The tint is on an always-on-top overlay surface above your "
            + "other windows.",
        CapabilityState.Unavailable u => $"Nothing is drawn on screen: {u.Reason.Detail}",
        CapabilityState.Degraded d => $"Partly drawn: {d.SurvivingSemantics}. {d.Reason.Detail}",
        CapabilityState.PermissionRequired p => $"Nothing is drawn on screen: {p.Reason.Detail}",
        CapabilityState.DependencyMissing m => $"Nothing is drawn on screen: {m.Reason.Detail}",
        CapabilityState.Faulted f => $"Nothing is drawn on screen: {f.Reason.Detail}",
        _ => placement.ToString() ?? string.Empty,
    };

    /// <summary>
    /// The row's dot has three states and so does this line, because the line is what a screen
    /// reader gets instead of the dot. They are derived from the SAME value.
    /// </summary>
    internal static string DescribeState(EffectDotState dot, int flashCount, FlashEvent? last)
    {
        var head = dot switch
        {
            EffectDotState.Live => "Running: the next flash is on the clock.",
            EffectDotState.Armed => "Armed. Nothing is scheduled until the session starts.",
            _ => "Switched off. Nothing will happen, session or no session.",
        };

        if (flashCount == 0)
        {
            return head;
        }

        var drew = last is null
            ? string.Empty
            : $" The last one drew {last.ImagesDrawn} image{(last.ImagesDrawn == 1 ? "" : "s")}.";
        return $"{head} {flashCount} flash{(flashCount == 1 ? "" : "es")} so far.{drew}";
    }

    /// <summary>The same three states for a second PACED module, whose <c>Live</c> means exactly
    /// what the flash module's does: a firing is on the clock.</summary>
    internal static string DescribeSubliminalState(EffectDotState dot, int count, SubliminalEvent? last)
    {
        var head = dot switch
        {
            EffectDotState.Live => "Running: the next subliminal is on the clock.",
            EffectDotState.Armed => "Armed. Nothing is scheduled until the session starts.",
            _ => "Switched off. Nothing will happen, session or no session.",
        };

        if (count == 0)
        {
            return head;
        }

        var held = last is null
            ? string.Empty
            : $" The last one held for {last.HeldMilliseconds} ms.";
        return $"{head} {count} subliminal{(count == 1 ? "" : "s")} so far.{held}";
    }

    /// <summary>
    /// The words for a CONTINUOUS module, and the one place on this page where they had to change.
    ///
    /// <para>There is no clock and no count, so <c>Live</c> cannot say "the next one is scheduled" —
    /// it says the tint is up, which is the only thing this module's <c>Live</c> is entitled to
    /// mean. The consequence is that <see cref="EffectDotState.Armed"/> covers <b>four different
    /// situations</b> here where a paced module's covers one, because <see cref="OwnedSessionEffect"/>
    /// returns <c>Armed</c> for anything that is not <c>Off</c> and not really running — and for
    /// this module "really running" is the SCREEN.</para>
    ///
    /// <para><b>Why they are four sentences and not one (a final review caught it).</b> This method used
    /// to answer every <c>Armed</c> with "Nothing is drawn until the session starts." On Linux the
    /// overlay refuses by design (see <see cref="PinkFilterEffect"/>), so a running session with the
    /// dial on lands in that arm — and every Linux user would have read, for the whole of every
    /// session, an instruction to start a session they had already started. A message that
    /// misdescribes state is the exact failure an earlier packet was sent to fix one string earlier, and one
    /// arm of a switch is all it takes to reintroduce it. Each situation now names its own cause,
    /// and the running-but-not-drawn one names the SURFACE rather than the session.</para>
    ///
    /// <para><b>The paced siblings are not exposed the same way</b> and are deliberately left alone:
    /// their <c>WorkIsRunning</c> is <c>ScheduleArmed</c>, which is surface-independent, so a running
    /// session whose overlay refuses still reads <c>Live</c> there — correctly, because their
    /// schedule really is on the clock.</para>
    /// </summary>
    /// <param name="dot">The module's own derived state — the same value the row's dot paints.</param>
    /// <param name="tint">The tint the dials currently describe, for the zero-opacity case.</param>
    /// <param name="sessionRunning">Whether a session owns the rack right now. Read from the engine,
    /// never inferred from the dot: inferring it is what produced the defect above.</param>
    /// <param name="placement">The surface's last verbatim outcome, for naming the refusal.</param>
    public static string DescribePinkFilterState(
        EffectDotState dot, PinkFilterTint tint, bool sessionRunning, CapabilityState? placement) => dot switch
    {
        EffectDotState.Live => "Running: the tint is on your screen for as long as the session lasts.",

        // Running, and the user's own dial is the reason nothing is drawn — so the remedy is theirs.
        EffectDotState.Armed when sessionRunning && tint.IsInvisible =>
            "Running, but nothing is on your screen: the opacity is at 0%. Move the slider up.",

        // Running, and the SURFACE is the reason. This is the Linux case, and the arm that did not
        // exist before final review.
        EffectDotState.Armed when sessionRunning =>
            RefusalCode(placement) is { } code
                ? "Running, but nothing is on your screen: this build could not put the tint's "
                    + $"overlay surface up ({code})."
                : "Running, but nothing is on your screen: the tint's overlay surface is not up.",

        EffectDotState.Armed when tint.IsInvisible =>
            "Armed, but the opacity is at 0%, so there is nothing to draw. Move the slider up.",
        EffectDotState.Armed => "Armed. Nothing is drawn until the session starts.",
        _ => "Switched off. Nothing will happen, session or no session.",
    };

    /// <summary>
    /// What the operating system last said about the bouncing logo's surface, in the user's terms.
    ///
    /// <para>It says <b>per-pixel</b> on purpose. Every other drawing module in this port composites
    /// at one uniform opacity over an opaque rectangle, and the difference is the entire reason this
    /// row exists at all - a reader who saw "always-on-top overlay" here would reasonably assume the
    /// same mechanism.</para>
    /// </summary>
    public static string DescribeBouncingTextSurface(CapabilityState? placement) => placement switch
    {
        null => "The words are composited with per-pixel transparency on an always-on-top, "
            + "click-through surface: the desktop shows through everywhere the letters are not. "
            + "Nothing has been drawn yet.",
        CapabilityState.Available => "The words are on an always-on-top, click-through surface with "
            + "per-pixel transparency, above your other windows.",
        CapabilityState.Unavailable u => $"Nothing is drawn on screen: {u.Reason.Detail}",
        CapabilityState.Degraded d => $"Partly drawn: {d.SurvivingSemantics}. {d.Reason.Detail}",
        CapabilityState.PermissionRequired p => $"Nothing is drawn on screen: {p.Reason.Detail}",
        CapabilityState.DependencyMissing m => $"Nothing is drawn on screen: {m.Reason.Detail}",
        CapabilityState.Faulted f => $"Nothing is drawn on screen: {f.Reason.Detail}",
        _ => placement.ToString() ?? string.Empty,
    };

    /// <summary>Which words this module may show. The pool line's job, as the flash and subliminal
    /// panels already do it.</summary>
    public static string DescribeBouncingTextPool(IReadOnlyList<string> phrases)
    {
        ArgumentNullException.ThrowIfNull(phrases);
        return phrases.Count == 0
            ? $"No words are enabled, so the logo falls back to '{BouncingTextField.FallbackText}'."
            : $"{phrases.Count} word(s) in the pool; one is picked at random and re-rolled on about "
                + "one bounce in ten.";
    }

    /// <summary>
    /// The live line for the bouncing logo. Its Live clause is the strictest in the port: the dot is
    /// lit only while the operating system's own copy of the surface still carries the frame.
    /// </summary>
    /// <param name="dot">What the dot is showing.</param>
    /// <param name="presentation">The dials as they stand.</param>
    /// <param name="showing">Whether a surface is up at all, which is NOT the same as the dot.</param>
    /// <param name="bounces">How many wall bounces this run has taken.</param>
    /// <param name="sessionRunning">Whether a session is running at all.</param>
    public static string DescribeBouncingTextState(
        EffectDotState dot,
        BouncingTextPresentation presentation,
        bool showing,
        int bounces,
        bool sessionRunning)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        return dot switch
        {
            EffectDotState.Live =>
                $"Bouncing at speed {presentation.SpeedSetting}, {presentation.FontSize} px, "
                + $"{presentation.OpacityPercent}% opacity. {bounces} bounce(s) so far.",
            EffectDotState.Armed when presentation.IsInvisible =>
                "Armed, and the opacity dial is at 0%, so nothing would be drawn even during a session.",
            EffectDotState.Armed when sessionRunning && showing =>
                "The surface is up and the operating system no longer returns its content, so nothing "
                + "is on screen.",
            EffectDotState.Armed when sessionRunning =>
                "Armed and nothing is on screen.",
            EffectDotState.Armed => "Armed. Nothing is drawn until the session starts.",
            _ => "Switched off. Nothing will happen, session or no session.",
        };
    }

    /// <summary>
    /// The same rule again for the spiral. Its tense is the tint's — the layer stays, so the line is
    /// about what is on screen NOW rather than about the last one.
    /// </summary>
    public static string DescribeSpiralSurface(CapabilityState? placement) => placement switch
    {
        null => "The spiral is drawn on an always-on-top, click-through overlay above your other "
            + "windows. Nothing has been drawn yet.",
        CapabilityState.Available => "The spiral is on an always-on-top overlay surface above your "
            + "other windows.",
        CapabilityState.Unavailable u => $"Nothing is drawn on screen: {u.Reason.Detail}",
        CapabilityState.Degraded d => $"Partly drawn: {d.SurvivingSemantics}. {d.Reason.Detail}",
        CapabilityState.PermissionRequired p => $"Nothing is drawn on screen: {p.Reason.Detail}",
        CapabilityState.DependencyMissing m => $"Nothing is drawn on screen: {m.Reason.Detail}",
        CapabilityState.Faulted f => $"Nothing is drawn on screen: {f.Reason.Detail}",
        _ => placement.ToString() ?? string.Empty,
    };

    /// <summary>
    /// Which spiral this module would draw, and where to put one when there is none — the same job
    /// the flash panel's pool line does, and for the same reason WPF gives: an empty library with no
    /// folder named is the most common first-run dead end (<c>FlashService.cs:589-597</c>).
    ///
    /// <para>The FILE NAME only, never the full path: the media-logging rule the DTRH manifest holds
    /// applies to what a panel prints as much as to what a log writes.</para>
    /// </summary>
    public static string DescribeSpiralLibrary(string? spiralPath, string spiralsFolder) =>
        spiralPath is null
            ? $"No spiral to draw. Put a .gif, .png or .jpg in {spiralsFolder} and this module will find it."
            : $"Drawing {PortablePath.FileName(spiralPath)}.";

    /// <summary>
    /// The words for the MOVING module, and the place where the dot's third meaning becomes a
    /// sentence.
    ///
    /// <para>The other three modules each have one shape of "on but doing nothing". This one has
    /// <b>two</b>, and telling them apart is the whole point of the state: a spiral that is
    /// <i>up and turning</i> is running; a spiral that is <i>up and has stopped turning</i> is a
    /// frozen picture the user is entitled to be told about, because everything about the screen
    /// looks right. And a <b>still</b> spiral — a one-frame file — is up, not turning, and
    /// perfectly healthy, because WPF starts no frame timer for one either
    /// (<c>OverlayService.cs:1370</c>). Three outcomes that a boolean cannot carry.</para>
    ///
    /// <para><b>No arm may tell a running session to start a session.</b> That was a
    /// final-review blocker one module earlier (its record §9.1), and it is prevented here the same
    /// way: every arm branches on <paramref name="sessionRunning"/>, read from the engine and never
    /// inferred from the dot.</para>
    /// </summary>
    /// <param name="dot">The module's own derived state — the same value the row's dot paints.</param>
    /// <param name="presentation">The dials' current presentation, for the zero-opacity case.</param>
    /// <param name="hasSpiral">Whether the library resolved a file at all.</param>
    /// <param name="showing">Whether a surface is up, regardless of whether it is still moving.</param>
    /// <param name="frameCount">Frames in the open clip; 1 means a still image.</param>
    /// <param name="sessionRunning">Whether a session owns the rack right now.</param>
    /// <param name="placement">The surface's last verbatim outcome, for naming a refusal by code.</param>
    public static string DescribeSpiralState(
        EffectDotState dot,
        SpiralPresentation presentation,
        bool hasSpiral,
        bool showing,
        int frameCount,
        bool sessionRunning,
        CapabilityState? placement) => dot switch
    {
        // Live and still: the file is a single frame, so nothing was ever going to move. Said out
        // loud, because a user looking at a motionless spiral under a green dot would otherwise be
        // reading a contradiction.
        EffectDotState.Live when frameCount <= 1 =>
            "Running: your spiral is a single still frame, so it sits on your screen without turning. "
            + "That is the file, not a fault.",
        EffectDotState.Live =>
            $"Running: the spiral is turning on your screen, {frameCount} frames on a loop, for as long "
            + "as the session lasts.",

        // THE STATE THIS MODULE ADDED. On screen, and stopped. Neither of the other continuous
        // modules can be in it, and no dot before this one could report it.
        EffectDotState.Armed when sessionRunning && showing =>
            "Running, and the spiral is on your screen — but it has STOPPED TURNING, so what you are "
            + "looking at is a frozen frame.",

        EffectDotState.Armed when sessionRunning && presentation.IsInvisible =>
            "Running, but nothing is on your screen: the opacity is at 0%. Move the slider up.",
        EffectDotState.Armed when sessionRunning && !hasSpiral =>
            "Running, but nothing is on your screen: there is no spiral for this module to draw.",
        EffectDotState.Armed when sessionRunning =>
            RefusalCode(placement) is { } code
                ? "Running, but nothing is on your screen: this build could not put the spiral's "
                    + $"overlay surface up ({code})."
                : "Running, but nothing is on your screen: the spiral's overlay surface is not up.",

        EffectDotState.Armed when presentation.IsInvisible =>
            "Armed, but the opacity is at 0%, so there is nothing to draw. Move the slider up.",
        EffectDotState.Armed when !hasSpiral =>
            "Armed, but there is no spiral for this module to draw yet.",
        EffectDotState.Armed => "Armed. Nothing is drawn until the session starts.",
        _ => "Switched off. Nothing will happen, session or no session.",
    };

    /// <summary>
    /// The stable reason code out of any refusing <see cref="CapabilityState"/>, or null when the
    /// state is <see cref="CapabilityState.Available"/> or nothing has been attempted.
    ///
    /// <para>The CODE and not the detail, on purpose: the detail is a paragraph on the platform
    /// where this matters most — the Linux backend's refusal carries its whole manual gate — and it
    /// is already printed verbatim, once, by <see cref="DescribePinkFilterSurface"/>. Saying it
    /// twice in one panel is not twice as honest. The code is short, stable, and the thing a bug
    /// report quotes.</para>
    /// </summary>
    private static string? RefusalCode(CapabilityState? placement) => placement switch
    {
        CapabilityState.Unavailable u => u.Reason.Code,
        CapabilityState.Degraded d => d.Reason.Code,
        CapabilityState.PermissionRequired p => p.Reason.Code,
        CapabilityState.DependencyMissing m => m.Reason.Code,
        CapabilityState.Faulted f => f.Reason.Code,
        _ => null,
    };

    private static string DescribePhrasePool(int activePhrases) => activePhrases > 0
        ? $"{activePhrases} phrase{(activePhrases == 1 ? "" : "s")} active in the pool."
        : "No phrase in the pool is active, so the schedule will run and nothing will be shown.";

    private string DescribePool(FlashEvent? last)
    {
        // Only ever said about a flash that really came due: before then, "your folder is
        // empty" would be a claim about a folder nothing has looked in.
        if (last is { PoolWasEmpty: true })
        {
            return $"That flash had no images to draw. Put images in {_session.ImagesFolder} and the next one will find them.";
        }

        return string.Empty;
    }
}
