using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The Studio rack.
///
/// <para>Hop 2 of the Loom route (wpf-surface-reachability.md §4, §8.4 verified live):
/// rack row <c>Spiral Overlay</c> -> module panel -> <c>THE LOOM — weave your own spiral</c>
/// -> <see cref="LoomLaunch"/>. A row selects a module and opens NO window
/// (<c>MainWindow.Presets.cs:976-978,1009</c> — "Navigation tiles still navigate to the ONE
/// existing entry, never launch"); only the button on the destination page launches, which is
/// WPF's one-entry rule (<c>MainWindow.Presets.cs:1007</c>).</para>
///
/// <para><b>SP-098 — the second gesture arrives.</b> The <c>Flash Images</c> row carries the
/// rack grammar in full for the first time in the port: left-click opens its panel,
/// right-click quick-toggles the effect, and the dot reports what is really happening
/// (§8.3, and §9 D5/D6, which recorded both as gaps "because the port has nothing to wire
/// yet"). Every gesture on this page routes through ONE dispatch entry —
/// <see cref="SessionEngine.QuickToggle"/> — so the row's right-click and the panel's Enable
/// checkbox cannot drift into two behaviours (the A-004 one-command-path rule the retired
/// demonstrator card established).</para>
/// </summary>
public partial class StudioPage : UserControl
{
    private readonly SessionParticipant _session;
    private readonly FlashImagesEffect _flash;
    private bool _syncing;

    public StudioPage(LoomLaunch loom, SessionParticipant session)
    {
        ArgumentNullException.ThrowIfNull(loom);
        ArgumentNullException.ThrowIfNull(session);
        InitializeComponent();

        _session = session;
        _flash = session.Flash;

        // Row selection swaps the panel in, exactly as WPF's rack drives its row state from
        // the RadioButton's own checked transitions rather than from the click handler
        // (StudioTabView.xaml.cs:664-665), so the panel can never drift out of step with the
        // selection.
        RowFlashImages.IsCheckedChanged += (_, _) => ApplySelection();
        RowSpiralOverlay.IsCheckedChanged += (_, _) => ApplySelection();

        // The rack row's second gesture (StudioTabView.xaml.cs:660 -> :1109-1133). On the ROW,
        // not on the dot: the dot is 8px and the gesture belongs to the whole entry (:658-659).
        // Right-click is NOT handled on the Spiral Overlay row — that row has no effect to
        // flip, which is WPF's own unhandled case (:659, "Rows with no Toggle fall through
        // unhandled"), and a fake toggle there would be worse than no gesture.
        RowFlashImages.AddHandler(PointerReleasedEvent, OnFlashRowPointerReleased, RoutingStrategies.Tunnel);

        FlashEnableToggle.IsCheckedChanged += (_, _) => OnEnableToggled();
        FlashFrequencySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                OnFrequencyMoved();
            }
        };
        FlashImagesSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                OnImagesPerFlashMoved();
            }
        };

        _session.Engine.Changed += OnSessionChanged;
        _flash.Fired += _ => Refresh();

        LoomButton.Click += (_, _) => loom.Launch();

        LoadDialsFromPreset();
        Refresh();
    }

    /// <summary>The Flash Images module's dot state, as the row is currently painting it
    /// (public so a test reads the RENDERED claim rather than the model it came from).</summary>
    public EffectDotState RenderedFlashDot { get; private set; } = EffectDotState.Off;

    private void ApplySelection()
    {
        var flashOpen = RowFlashImages.IsChecked == true;
        var spiralOpen = RowSpiralOverlay.IsChecked == true;
        FlashModulePanel.IsVisible = flashOpen;
        SpiralModulePanel.IsVisible = spiralOpen;
        RackHint.IsVisible = !flashOpen && !spiralOpen;
    }

    /// <summary>
    /// WPF's right-click quick-toggle. <c>Handled</c> is set so the gesture stops here rather
    /// than also selecting the row (<c>StudioTabView.xaml.cs:1115</c>): a toggle that also
    /// opened the panel would make the two gestures indistinguishable.
    /// </summary>
    private void OnFlashRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right)
        {
            return;
        }

        e.Handled = true;
        _session.Engine.QuickToggle(FlashImagesEffect.EffectId);
        LoadDialsFromPreset();
        Refresh();
    }

    /// <summary>
    /// The panel's Enable checkbox. WPF's does the same work as the row's right-click — write
    /// the flag, then start/stop the live service if the engine is running
    /// (<c>Features/FlashFeatureControl.xaml.cs:159-175</c>) — so it routes through the SAME
    /// entry rather than growing a second copy of that body.
    /// </summary>
    private void OnEnableToggled()
    {
        if (_syncing)
        {
            return;
        }

        var target = FlashEnableToggle.IsChecked == true;
        if (target == _flash.Enabled)
        {
            return;
        }

        _session.Engine.QuickToggle(FlashImagesEffect.EffectId);
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
    /// The session's state can move on a thread that is not this one — teardown stops the engine
    /// from the shutdown path. This handler nonetheless touches controls directly, because since
    /// SP-101 the marshalling is the PRODUCER's: every module raises <c>Changed</c> through
    /// <see cref="EffectSignal"/>, which delivers on the UI thread whenever one exists. This page
    /// used to carry its own <c>CheckAccess</c>-or-<c>Post</c> copy, and so did the shell; two
    /// copies agreed and the fifteenth module's panel would not have.
    /// </summary>
    private void OnSessionChanged()
    {
        LoadDialsFromPreset();
        Refresh();
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
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// Repaint what is true right now. The dot's classes come from
    /// <see cref="ISessionEffect.Dot"/> — the effect's own derived state, never a bool this
    /// page keeps — so the row cannot claim a module is running after its schedule is gone.
    /// </summary>
    private void Refresh()
    {
        var preset = _session.Preset.Current;
        FlashFrequencyValue.Text = preset.FlashesPerHour.ToString(System.Globalization.CultureInfo.CurrentCulture);
        FlashImagesValue.Text = preset.ImagesPerFlash.ToString(System.Globalization.CultureInfo.CurrentCulture);

        var dot = _flash.Dot;
        RenderedFlashDot = dot;
        FlashRowDot.Classes.Set("armed", dot == EffectDotState.Armed);
        FlashRowDot.Classes.Set("live", dot == EffectDotState.Live);

        FlashLiveState.Text = DescribeState(dot, _flash.FlashCount, _flash.Last);
        FlashPoolState.Text = DescribePool(_flash.Last);
        FlashSurfaceState.Text = DescribeSurface(_session.Surface.LastPlacement);
    }

    /// <summary>
    /// Where the images went, according to the SURFACE.
    ///
    /// <para>This line used to be a fixed sentence saying the drawing half was not ported. SP-100
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
