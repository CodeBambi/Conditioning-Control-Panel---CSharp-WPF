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
///
/// <para><b>SP-108 — the rack gets a SECOND GROUP, and the module in it draws nothing.</b> WPF's
/// rack has four groups (§8.3); this port had rows in one because every module it had ported was an
/// EFFECT that painted an overlay. <c>Intensity Ramp</c> is from TIMING, has no surface, and its
/// whole visible output is the opacity numbers on the Spiral Overlay and Pink Filter panels next to
/// it. The rack grammar needed nothing new for it — left-click opens, right-click quick-toggles
/// through the same one <see cref="SessionEngine.QuickToggle"/> entry — and its <b>panel is the
/// first on this page with no surface line at all</b>, because a sentence about where its pixels
/// went would be a sentence about a capability it deliberately never acquires.</para>
///
/// <para><b>SP-109 — a THIRD group, and the first two rows nobody can see.</b> <c>Mind Wipe</c> and
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
    private readonly IntensityRampEffect _ramp;
    private readonly MindWipeEffect _mindWipe;
    private readonly BrainDrainEffect _brainDrain;
    private readonly LockCardEffect _lockCard;
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
        _spiral = session.Spiral;
        _ramp = session.Ramp;
        _mindWipe = session.MindWipe;
        _brainDrain = session.BrainDrain;
        _lockCard = session.LockCard;

        // Row selection swaps the panel in, exactly as WPF's rack drives its row state from
        // the RadioButton's own checked transitions rather than from the click handler
        // (StudioTabView.xaml.cs:664-665), so the panel can never drift out of step with the
        // selection.
        RowFlashImages.IsCheckedChanged += (_, _) => ApplySelection();
        RowSubliminals.IsCheckedChanged += (_, _) => ApplySelection();
        RowSpiralOverlay.IsCheckedChanged += (_, _) => ApplySelection();
        RowPinkFilter.IsCheckedChanged += (_, _) => ApplySelection();
        RowIntensityRamp.IsCheckedChanged += (_, _) => ApplySelection();
        RowMindWipe.IsCheckedChanged += (_, _) => ApplySelection();
        RowBrainDrain.IsCheckedChanged += (_, _) => ApplySelection();
        RowLockCard.IsCheckedChanged += (_, _) => ApplySelection();

        // The rack row's second gesture (StudioTabView.xaml.cs:660 -> :1109-1133). On the ROW,
        // not on the dot: the dot is 8px and the gesture belongs to the whole entry (:658-659).
        // Right-click is NOT handled on the Spiral Overlay row — that row has no effect to
        // flip, which is WPF's own unhandled case (:659, "Rows with no Toggle fall through
        // unhandled"), and a fake toggle there would be worse than no gesture.
        AddQuickToggle(RowFlashImages, FlashImagesEffect.EffectId);
        AddQuickToggle(RowSubliminals, SubliminalsEffect.EffectId);
        AddQuickToggle(RowSpiralOverlay, SpiralOverlayEffect.EffectId);
        AddQuickToggle(RowPinkFilter, PinkFilterEffect.EffectId);
        AddQuickToggle(RowIntensityRamp, IntensityRampEffect.EffectId);
        AddQuickToggle(RowMindWipe, MindWipeEffect.EffectId);
        AddQuickToggle(RowBrainDrain, BrainDrainEffect.EffectId);
        AddQuickToggle(RowLockCard, LockCardEffect.EffectId);

        FlashEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(FlashEnableToggle, _flash, FlashImagesEffect.EffectId);
        SubliminalEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(SubliminalEnableToggle, _subliminals, SubliminalsEffect.EffectId);
        PinkFilterEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(PinkFilterEnableToggle, _pinkFilter, PinkFilterEffect.EffectId);
        SpiralEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(SpiralEnableToggle, _spiral, SpiralOverlayEffect.EffectId);
        RampEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(RampEnableToggle, _ramp, IntensityRampEffect.EffectId);
        MindWipeEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(MindWipeEnableToggle, _mindWipe, MindWipeEffect.EffectId);
        BrainDrainEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(BrainDrainEnableToggle, _brainDrain, BrainDrainEffect.EffectId);
        LockCardEnableToggle.IsCheckedChanged += (_, _) =>
            OnEnableToggled(LockCardEnableToggle, _lockCard, LockCardEffect.EffectId);

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
        RampCurvePicker.SelectionChanged += (_, _) => OnRampCurvePicked();

        OnSliderMoved(FlashFrequencySlider, OnFrequencyMoved);
        OnSliderMoved(FlashImagesSlider, OnImagesPerFlashMoved);
        OnSliderMoved(SubliminalFrequencySlider, OnSubliminalFrequencyMoved);
        OnSliderMoved(PinkFilterOpacitySlider, OnPinkFilterOpacityMoved);
        OnSliderMoved(SpiralOpacitySlider, OnSpiralOpacityMoved);
        OnSliderMoved(RampDurationSlider, OnRampDurationMoved);
        OnSliderMoved(RampMultiplierSlider, OnRampMultiplierMoved);
        OnSliderMoved(MindWipeFrequencySlider, OnMindWipeFrequencyMoved);
        OnSliderMoved(MindWipeVolumeSlider, OnMindWipeVolumeMoved);
        OnSliderMoved(BrainDrainIntensitySlider, OnBrainDrainIntensityMoved);
        OnSliderMoved(BrainDrainVolumeSlider, OnBrainDrainVolumeMoved);
        OnSliderMoved(LockCardFrequencySlider, OnLockCardFrequencyMoved);
        OnSliderMoved(LockCardRepeatsSlider, OnLockCardRepeatsMoved);

        _session.Engine.Changed += OnSessionChanged;
        _flash.Fired += _ => Refresh();
        _subliminals.Fired += _ => Refresh();
        _mindWipe.Fired += _ => Refresh();
        _brainDrain.Fired += _ => Refresh();

        // BOTH of this module's signals, and both matter: Shown is a card appearing, Resolved is the
        // user answering it OR walking away from it. A page that only repainted on the first would
        // leave "asking you now" on screen after the card was gone.
        _lockCard.Shown += _ => Refresh();
        _lockCard.Resolved += _ => Refresh();

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

    /// <summary>The Spiral Overlay row's dot, same reason — and the one with the strictest rule
    /// behind it: a MOVING module is <c>Live</c> only while its surface is up AND still changing
    /// (SP-106, <see cref="SpiralSurfacePresenter.Running"/>).</summary>
    public EffectDotState RenderedSpiralDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Intensity Ramp row's dot, same reason — and the one whose <c>Live</c> is a claim
    /// about neither a clock nor a screen but about CUSTODY: the module is running exactly while it
    /// holds dials belonging to other modules and owes them back (SP-108,
    /// <see cref="IntensityRampEffect"/>).</summary>
    public EffectDotState RenderedRampDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Mind Wipe row's dot, same reason — and the first on this page whose <c>Live</c>
    /// depends on a fact from OUTSIDE this process: a firing on the clock AND an audio render session
    /// the operating system confirms belongs to us (SP-109, <see cref="AudioCueEffect"/>).</summary>
    public EffectDotState RenderedMindWipeDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Brain Drain row's dot. Same rule as Mind Wipe's, and it is deliberately
    /// <see cref="EffectDotState.Live"/> while the audio half runs even though the row is half of its
    /// upstream: the dot is scoped to what the ROW is, and this row's title says what it is.</summary>
    public EffectDotState RenderedBrainDrainDot { get; private set; } = EffectDotState.Off;

    /// <summary>The Lock Card row's dot — the sixth meaning, and the first that is a claim on the
    /// USER rather than on anything this process owns: a firing on the clock, a desktop this process
    /// can put a window on, AND, while a card is up, the operating system's own confirmation that the
    /// card holds the foreground and the keyboard focus (SP-110,
    /// <see cref="LockCardEffect"/>).</summary>
    public EffectDotState RenderedLockCardDot { get; private set; } = EffectDotState.Off;

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

    /// <summary>
    /// The Spiral Overlay opacity slider writes the setting, re-applies it to whatever is already on
    /// screen and saves — the same order the pink slider above it uses, and WPF's own for this
    /// module (write, then <c>RefreshOverlays()</c>, reconciled at <c>OverlayService.cs:446</c>
    /// -&gt; <c>UpdateSpiralOpacity</c>). The re-apply goes through the module rather than the
    /// surface so the arm state and the dot move with it.
    /// </summary>
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
        var pinkOpen = RowPinkFilter.IsChecked == true;
        var rampOpen = RowIntensityRamp.IsChecked == true;
        var mindWipeOpen = RowMindWipe.IsChecked == true;
        var brainDrainOpen = RowBrainDrain.IsChecked == true;
        var lockCardOpen = RowLockCard.IsChecked == true;
        FlashModulePanel.IsVisible = flashOpen;
        SubliminalModulePanel.IsVisible = subliminalOpen;
        SpiralModulePanel.IsVisible = spiralOpen;
        PinkFilterModulePanel.IsVisible = pinkOpen;
        RampModulePanel.IsVisible = rampOpen;
        MindWipeModulePanel.IsVisible = mindWipeOpen;
        BrainDrainModulePanel.IsVisible = brainDrainOpen;
        LockCardModulePanel.IsVisible = lockCardOpen;
        RackHint.IsVisible = !flashOpen && !subliminalOpen && !spiralOpen && !pinkOpen && !rampOpen
            && !mindWipeOpen && !brainDrainOpen && !lockCardOpen;
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
    /// convention for every module's dial since SP-105, and it matters more here than usual: this dial
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

            var spiral = _session.SpiralPreset.Current;
            SpiralEnableToggle.IsChecked = spiral.Enabled;
            SpiralOpacitySlider.Value = spiral.OpacityPercent;

            var ramp = _session.RampPreset.Current;
            RampEnableToggle.IsChecked = ramp.Enabled;
            RampDurationSlider.Value = ramp.DurationMinutes;
            RampMultiplierSlider.Value = ramp.Multiplier;
            RampEndSessionToggle.IsChecked = ramp.EndSessionOnComplete;
            RampLinkSpiralToggle.IsChecked = ramp.LinkSpiralOpacity;
            RampLinkPinkFilterToggle.IsChecked = ramp.LinkPinkFilterOpacity;
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
    }

    /// <summary>
    /// The cards-per-hour slider writes the dial and re-paces the live schedule — the same order the
    /// flash frequency slider uses, and upstream writes then saves
    /// (<c>Features/LockCardFeatureControl.xaml.cs:97-106</c>). The re-pace is this port's standing
    /// convention since SP-105, so a raised frequency takes effect now rather than after the old
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
    /// The words for a CONTINUOUS module, and the one place on this page where they had to change.
    ///
    /// <para>There is no clock and no count, so <c>Live</c> cannot say "the next one is scheduled" —
    /// it says the tint is up, which is the only thing this module's <c>Live</c> is entitled to
    /// mean. The consequence is that <see cref="EffectDotState.Armed"/> covers <b>four different
    /// situations</b> here where a paced module's covers one, because <see cref="OwnedSessionEffect"/>
    /// returns <c>Armed</c> for anything that is not <c>Off</c> and not really running — and for
    /// this module "really running" is the SCREEN.</para>
    ///
    /// <para><b>Why they are four sentences and not one (SP-105 final review).</b> This method used
    /// to answer every <c>Armed</c> with "Nothing is drawn until the session starts." On Linux the
    /// overlay refuses by design (see <see cref="PinkFilterEffect"/>), so a running session with the
    /// dial on lands in that arm — and every Linux user would have read, for the whole of every
    /// session, an instruction to start a session they had already started. A message that
    /// misdescribes state is the exact failure SP-101 was sent to fix one string earlier, and one
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
            : $"Drawing {System.IO.Path.GetFileName(spiralPath)}.";

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
    /// <para><b>No arm may tell a running session to start a session.</b> That was SP-105's
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
