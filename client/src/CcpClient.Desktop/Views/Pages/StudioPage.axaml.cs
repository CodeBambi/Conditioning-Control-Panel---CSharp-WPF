using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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
///
/// <para><b>SP-105 — the grammar generalises, and one of the rows is continuous.</b> Two more
/// rows land: <c>Subliminals</c>, whose module shipped in SP-101 with no way to switch it on at
/// all (D72), and <c>Pink Filter</c>, the port's first module with no schedule. Both go through
/// the same one dispatch entry, and neither needed a new gesture: the RACK grammar turns out to
/// be indifferent to whether a module is paced. Its <b>dot</b> is not — a continuous module's
/// <see cref="EffectDotState.Live"/> is a claim about the SCREEN rather than about a clock — but
/// that is decided inside the effect (see <see cref="OwnedSessionEffect.WorkIsRunning"/>) and
/// this page reads the same three states off the same property for all three modules.</para>
/// </summary>
public partial class StudioPage : UserControl
{
    private readonly SessionParticipant _session;
    private readonly FlashImagesEffect _flash;
    private readonly SubliminalsEffect _subliminals;
    private readonly PinkFilterEffect _pinkFilter;
    private bool _syncing;

    public StudioPage(LoomLaunch loom, SessionParticipant session)
    {
        ArgumentNullException.ThrowIfNull(loom);
        ArgumentNullException.ThrowIfNull(session);
        InitializeComponent();

        _session = session;
        _flash = session.Flash;
        _subliminals = session.Subliminals;
        _pinkFilter = session.PinkFilter;

        // Row selection swaps the panel in, exactly as WPF's rack drives its row state from
        // the RadioButton's own checked transitions rather than from the click handler
        // (StudioTabView.xaml.cs:664-665), so the panel can never drift out of step with the
        // selection.
        RowFlashImages.IsCheckedChanged += (_, _) => ApplySelection();
        RowSubliminals.IsCheckedChanged += (_, _) => ApplySelection();
        RowSpiralOverlay.IsCheckedChanged += (_, _) => ApplySelection();
        RowPinkFilter.IsCheckedChanged += (_, _) => ApplySelection();

        // The rack row's second gesture (StudioTabView.xaml.cs:660 -> :1109-1133). On the ROW,
        // not on the dot: the dot is 8px and the gesture belongs to the whole entry (:658-659).
        // Right-click is NOT handled on the Spiral Overlay row — that row has no effect to
        // flip, which is WPF's own unhandled case (:659, "Rows with no Toggle fall through
        // unhandled"), and a fake toggle there would be worse than no gesture.
        AddQuickToggle(RowFlashImages, FlashImagesEffect.EffectId);
        AddQuickToggle(RowSubliminals, SubliminalsEffect.EffectId);
        AddQuickToggle(RowPinkFilter, PinkFilterEffect.EffectId);

        FlashEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(FlashEnableToggle, _flash, FlashImagesEffect.EffectId);
        SubliminalEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(SubliminalEnableToggle, _subliminals, SubliminalsEffect.EffectId);
        PinkFilterEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(PinkFilterEnableToggle, _pinkFilter, PinkFilterEffect.EffectId);

        OnSliderMoved(FlashFrequencySlider, OnFrequencyMoved);
        OnSliderMoved(FlashImagesSlider, OnImagesPerFlashMoved);
        OnSliderMoved(SubliminalFrequencySlider, OnSubliminalFrequencyMoved);
        OnSliderMoved(PinkFilterOpacitySlider, OnPinkFilterOpacityMoved);

        _session.Engine.Changed += OnSessionChanged;
        _flash.Fired += _ => Refresh();
        _subliminals.Fired += _ => Refresh();

        LoomButton.Click += (_, _) => loom.Launch();

        LoadDialsFromPreset();
        Refresh();
    }

    /// <summary>The Flash Images module's dot state, as the row is currently painting it
    /// (public so a test reads the RENDERED claim rather than the model it came from).</summary>
    public EffectDotState RenderedFlashDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Subliminals row's dot, same reason (SP-105).</summary>
    public EffectDotState RenderedSubliminalDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Pink Filter row's dot, same reason. This is the one that had to be earned:
    /// a continuous module is <c>Live</c> only while its surface is really up (SP-105).</summary>
    public EffectDotState RenderedPinkFilterDot { get; private set; } = EffectDotState.Off;

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

    private void AddQuickToggle(RadioButton row, string effectId) =>
        row.AddHandler(
            PointerReleasedEvent,
            (_, e) => OnRowPointerReleased(e, effectId),
            RoutingStrategies.Tunnel);

    private void ApplySelection()
    {
        var flashOpen = RowFlashImages.IsChecked == true;
        var subliminalOpen = RowSubliminals.IsChecked == true;
        var spiralOpen = RowSpiralOverlay.IsChecked == true;
        var pinkOpen = RowPinkFilter.IsChecked == true;
        FlashModulePanel.IsVisible = flashOpen;
        SubliminalModulePanel.IsVisible = subliminalOpen;
        SpiralModulePanel.IsVisible = spiralOpen;
        PinkFilterModulePanel.IsVisible = pinkOpen;
        RackHint.IsVisible = !flashOpen && !subliminalOpen && !spiralOpen && !pinkOpen;
    }

    /// <summary>
    /// WPF's right-click quick-toggle. <c>Handled</c> is set so the gesture stops here rather
    /// than also selecting the row (<c>StudioTabView.xaml.cs:1115</c>): a toggle that also
    /// opened the panel would make the two gestures indistinguishable.
    /// </summary>
    private void OnRowPointerReleased(PointerReleasedEventArgs e, string effectId)
    {
        if (e.InitialPressMouseButton != MouseButton.Right)
        {
            return;
        }

        e.Handled = true;
        _session.Engine.QuickToggle(effectId);
        LoadDialsFromPreset();
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

            var subliminal = _session.SubliminalPreset.Current;
            SubliminalEnableToggle.IsChecked = subliminal.Enabled;
            SubliminalFrequencySlider.Value = subliminal.PerMinute;

            var pink = _session.PinkFilterPreset.Current;
            PinkFilterEnableToggle.IsChecked = pink.Enabled;
            PinkFilterOpacitySlider.Value = pink.OpacityPercent;
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
        PinkFilterLiveState.Text = DescribePinkFilterState(RenderedPinkFilterDot, tint);
        PinkFilterSurfaceState.Text = DescribePinkFilterSurface(_pinkFilter.LastPlacement);
    }

    private static EffectDotState PaintDot(Shape dot, ISessionEffect effect)
    {
        var state = effect.Dot;
        dot.Classes.Set("armed", state == EffectDotState.Armed);
        dot.Classes.Set("live", state == EffectDotState.Live);
        return state;
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
    /// The three states for a CONTINUOUS module, and the one place on this page where the words had
    /// to change. There is no clock and no count, so <c>Live</c> cannot say "the next one is
    /// scheduled" — it says the tint is up, which is the only thing this module's <c>Live</c> is
    /// entitled to mean. <c>Armed</c> covers both "no session yet" and "the session is running and
    /// the tint is not on screen", and the second of those is exactly the case a dot that reported
    /// the dial instead of the surface would have got wrong.
    /// </summary>
    internal static string DescribePinkFilterState(EffectDotState dot, PinkFilterTint tint) => dot switch
    {
        EffectDotState.Live => "Running: the tint is on your screen for as long as the session lasts.",
        EffectDotState.Armed when tint.IsInvisible =>
            "Armed, but the opacity is at 0%, so there is nothing to draw. Move the slider up.",
        EffectDotState.Armed => "Armed. Nothing is drawn until the session starts.",
        _ => "Switched off. Nothing will happen, session or no session.",
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
