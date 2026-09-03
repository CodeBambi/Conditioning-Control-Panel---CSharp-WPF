using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// HER OPTIONS: the little panel the gear opens beside her.
    ///
    /// <para><b>Why it exists.</b> Wave 3 put a six-pixel-dot glyph top-left that opened her ring.
    /// The owner read it as a move handle - "the three dots to move emi, not needed, we drag her" -
    /// and asked for a gear that opens her options instead (2026-08-30). Since those dots were the
    /// ring's only VISIBLE way in (nothing on a desktop advertises a right click), the first thing
    /// in this panel is "Open her cards".</para>
    ///
    /// <para><b>The window recipe is not optional</b> and is copied from her other two windows:
    /// SystemDecorations None, TransparencyLevelHint Transparent, ShowActivated false,
    /// ShowInTaskbar false, Topmost. She is a desktop ornament, not an application window: no focus
    /// theft, nothing in the task switcher, and no click that costs the user the window they were
    /// actually working in.</para>
    ///
    /// <para><b>And it is not modal, deliberately.</b> §10.8b of the primer: anything modal on a
    /// summon path needs the <c>_summonGen</c> generation guard, because the dismiss that happens
    /// while the dialog is up desyncs <c>IsOut</c> and strands her on screen. A non-modal panel that
    /// closes itself on a click outside has none of that surface.</para>
    ///
    /// <para><b>Every editor in here is mouse-only</b>, which is what the WPF original's
    /// WS_EX_NOACTIVATE forced and what this port keeps. The summon chord is SHOWN rather than
    /// captured and points at the settings tab, which does own the rebind.</para>
    ///
    /// PORTED from ConditioningControlPanel/Windows/EmiDesk/EmiOptionsWindow.xaml.cs. What changed,
    /// and why:
    ///
    /// <list type="bullet">
    ///   <item><b>The four Win32 entry points are gone.</b> <c>GetWindowLong</c>/<c>SetWindowLong</c>
    ///         stamping <c>WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE</c> in <c>OnSourceInitialized</c> map
    ///         one-for-one onto <c>ShowInTaskbar="False"</c> and <c>ShowActivated="False"</c> in the
    ///         markup, so <c>OnSourceInitialized</c> and the two DllImports go with them. NOT
    ///         equivalent in one respect worth naming: WS_EX_NOACTIVATE makes activation
    ///         *impossible*, where <c>ShowActivated="False"</c> only declines it at show time - a
    ///         click on this panel can still focus it on X11.</item>
    ///   <item><b>The two global hooks have no equivalent on this head</b> and are stubbed. See
    ///         <see cref="InstallHooks"/> for exactly which two behaviours that costs.</item>
    ///   <item><b>The owner is optional.</b> <c>EmiDeskWindow</c> is the widget this panel hangs
    ///         off and it is ported, so the constructor takes it and every <c>_owner.*</c> call is
    ///         real. It stays NULLABLE for one reason: <c>--render-view</c> needs a parameterless
    ///         constructor, and a headless render has no widget to hang off. Every owner call is
    ///         therefore <c>?.</c> with the same fallback the unowned panel used.</item>
    ///   <item><b>Localize() is gone.</b> Its sixteen <c>Loc.Get</c> assignments are
    ///         <c>{loc:Str}</c> in the markup, so a language change re-renders them (CLAUDE.md).
    ///         The one string it set that is NOT static - the summon chord, which is either the
    ///         bound chord or <c>emi_desk_hotkey_unbound</c> - is still chosen in code, in
    ///         <see cref="LoadValues"/>.</item>
    ///   <item><b>WPF DIPs become physical pixels.</b> <c>Left</c>/<c>Top</c> are DIPs and
    ///         <c>BodyScreenRect</c> was physical, which is why the original divides by
    ///         <c>DipScale</c> on both sides of every sum. Avalonia's <c>Position</c> is already a
    ///         <c>PixelPoint</c> and <c>Screens.WorkingArea</c> a <c>PixelRect</c>, so the placement
    ///         arithmetic works in physical pixels throughout and scales only the window's own
    ///         DIP size. Same result, one conversion instead of six.</item>
    ///   <item><b>The namespace is no longer the flat WPF one.</b> The WinRT <c>Windows</c>-shadowing
    ///         trap the original's header describes is a WPF-head problem; this head has no OCR
    ///         service and no WinRT.</item>
    /// </list>
    /// </summary>
    public partial class EmiOptionsWindow : Window
    {
        /// <summary>Air between her silhouette and the panel's near edge, in DIPs.</summary>
        private const double BodyGap = 12.0;

        /// <summary>How long a width drag rests before the number is written to settings.</summary>
        private const int WidthPersistMs = 400;

        private readonly EmiDeskWindow? _owner;

        private readonly Button _btnPanelClose;
        private readonly Button _btnOpenCards;
        private readonly Slider _sldWidth;
        private readonly TextBlock _txtWidth;
        private readonly TextBlock _txtHotkey;
        private readonly CheckBox _chkMute;
        private readonly CheckBox _chkOffers;
        private readonly CheckBox _chkGlass;
        private readonly RadioButton _spiceInnocent;
        private readonly RadioButton _spiceSuggestive;
        private readonly RadioButton _spiceAnything;
        private readonly EmiRingPicker _ringPicker;

        private bool _open;
        private bool _closingForGood;

        /// <summary>Suppresses the change handlers while the code is filling the controls in.</summary>
        private bool _loading;

        private DispatcherTimer? _widthPersist;

        // ---------------------------------------------------------------- ctor

        /// <summary>The render constructor: a panel with no widget behind it. See the header.</summary>
        public EmiOptionsWindow() : this(null) { }

        /// <summary>
        /// Builds the panel for one widget. Created folded; <see cref="OpenPanel"/> shows it.
        /// </summary>
        public EmiOptionsWindow(EmiDeskWindow? owner)
        {
            _owner = owner;
            AvaloniaXamlLoader.Load(this);

            _btnPanelClose = this.FindControl<Button>("BtnPanelClose")!;
            _btnOpenCards = this.FindControl<Button>("BtnOpenCards")!;
            _sldWidth = this.FindControl<Slider>("SldWidth")!;
            _txtWidth = this.FindControl<TextBlock>("TxtWidth")!;
            _txtHotkey = this.FindControl<TextBlock>("TxtHotkey")!;
            _chkMute = this.FindControl<CheckBox>("ChkMute")!;
            _chkOffers = this.FindControl<CheckBox>("ChkOffers")!;
            _chkGlass = this.FindControl<CheckBox>("ChkGlass")!;
            _spiceInnocent = this.FindControl<RadioButton>("SpiceInnocent")!;
            _spiceSuggestive = this.FindControl<RadioButton>("SpiceSuggestive")!;
            _spiceAnything = this.FindControl<RadioButton>("SpiceAnything")!;
            _ringPicker = this.FindControl<EmiRingPicker>("RingPicker")!;

            WireControls();

            // Filled here rather than only in OpenPanel: a headless render never opens the panel,
            // and an unfilled panel would prove nothing about the switches or the segments.
            LoadValues();

            // The chrome on her body stays lit while the pointer is in here, so the gear that
            // opened this is still on screen to close it again after the round trip.
            PointerEntered += (_, _) => Hold(true);
            PointerExited += (_, _) => Hold(false);

            // She moves, she resizes: the panel is anchored to her edge and has to come along.
            if (_owner is not null)
            {
                _owner.Moved += OnOwnerMoved;
                _owner.Resized += OnOwnerResized;
            }
        }

        /// <summary>The panel folded. The desk window drops its chrome hold on this.</summary>
        public event EventHandler? PanelClosed;

        /// <summary>"Open her cards" was clicked. The desk window owns what a ring is; this only asks.</summary>
        public event EventHandler? CardsRequested;

        /// <summary>True while the panel is on screen.</summary>
        public bool IsOpen => _open;

        /// <summary>This window's own scale. Never assume 1.0 on a multi-monitor desk.</summary>
        private double DipScale
        {
            get
            {
                try
                {
                    if (RenderScaling > 0) return RenderScaling;
                }
                catch { /* no toplevel yet */ }
                try
                {
                    var s = Screens?.Primary;
                    if (s != null && s.Scaling > 0) return s.Scaling;
                }
                catch { /* no screens under a headless backend */ }
                return 1.0;
            }
        }

        // ---------------------------------------------------------------- wiring

        private void WireControls()
        {
            _btnPanelClose.Click += (_, _) => ClosePanel();

            _btnOpenCards.Click += (_, _) =>
            {
                try
                {
                    // Fold first: the fan is anchored on the same body and would open underneath this.
                    ClosePanel();
                    CardsRequested?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[EmiDesk] options cards request failed");
                }
            };

            _sldWidth.Minimum = EmiDeskWindow.MinBodyWidth;
            _sldWidth.Maximum = EmiDeskWindow.MaxBodyWidth;
            _sldWidth.ValueChanged += OnWidthChanged;
            // A track click is not a drag, so the debounce is what actually persists both. Kept
            // anyway: it is what makes the write land the instant the thumb is let go.
            _sldWidth.AddHandler(Thumb.DragCompletedEvent, (EventHandler<VectorEventArgs>)((_, _) => PersistWidth()));

            _chkMute.IsCheckedChanged += OnMuteChanged;
            _chkOffers.IsCheckedChanged += OnOffersChanged;
            _chkGlass.IsCheckedChanged += OnGlassChanged;

            // WPF wired Checked only. Avalonia has the one event for both edges, and a radio group
            // raises it on the segment being cleared too, so the guard is on IsChecked here.
            _spiceInnocent.IsCheckedChanged += (_, _) => { if (_spiceInnocent.IsChecked == true) OnSpicePicked(0); };
            _spiceSuggestive.IsCheckedChanged += (_, _) => { if (_spiceSuggestive.IsChecked == true) OnSpicePicked(1); };
            _spiceAnything.IsCheckedChanged += (_, _) => { if (_spiceAnything.IsChecked == true) OnSpicePicked(2); };
        }

        private void Hold(bool on)
        {
            try { _owner?.HoldChromeForPanel(on); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] options chrome hold failed"); }
        }

        // ---------------------------------------------------------------- open / close

        /// <summary>Fill the controls from settings, place the panel beside her and start listening.</summary>
        public void OpenPanel()
        {
            if (_closingForGood) return;
            try
            {
                LoadValues();
                // Rebuilt rather than refreshed: availability and lock are DELEGATES on a target,
                // and both can have changed since the last time this was opened.
                _ringPicker.Rebuild();

                PlaceWindow();
                if (!IsVisible)
                {
                    Show();
                    // The window has no scale of its own until it is mapped, so the first placement
                    // is always done twice: once to get it on screen, once with its real scale and
                    // its real measured height.
                    PlaceWindow();
                }

                _open = true;
                UpdateHotRects();
                InstallHooks();

                Log.Information("[EmiDesk] options panel open");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] options panel failed to open");
                try { ClosePanel(); } catch { /* nothing else to try */ }
            }
        }

        /// <summary>Fold the panel. Idempotent, and safe to call from a posted continuation.</summary>
        public void ClosePanel()
        {
            try
            {
                RemoveHooks();
                StopWidthPersist();
                if (!_open && !IsVisible) return;
                _open = false;
                Hide();

                Log.Debug("[EmiDesk] options panel closed");
                try { PanelClosed?.Invoke(this, EventArgs.Empty); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] PanelClosed handler threw"); }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] options panel close failed");
            }
        }

        /// <summary>Let the panel go for good: app shutdown, or the widget closing.</summary>
        public void Kill()
        {
            try
            {
                _closingForGood = true;
                ClosePanel();
                if (_owner is not null)
                {
                    _owner.Moved -= OnOwnerMoved;
                    _owner.Resized -= OnOwnerResized;
                }
                Close();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] options kill failed");
            }
        }

        // ---------------------------------------------------------------- values

        /// <summary>Fill the controls in from the live settings and from her current width.</summary>
        private void LoadValues()
        {
            try
            {
                _loading = true;

                var s = CoreSettings.Current;
                _chkMute.IsChecked = s.EmiDeskMuteAvatar;
                _chkOffers.IsChecked = s.EmiDeskOffers;
                _chkGlass.IsChecked = s.EmiDeskGlass;

                // The three segments ARE the 0..2 scale the lines file carries. No translation.
                int spice = Math.Max(0, Math.Min(2, s.EmiDeskSpice));
                _spiceInnocent.IsChecked = spice == 0;
                _spiceSuggestive.IsChecked = spice == 1;
                _spiceAnything.IsChecked = spice == 2;

                var chord = s.EmiDeskHotkey;
                // Chosen in code, not markup: which of the two strings applies depends on whether
                // a chord is bound. Safe as an assignment - TxtHotkey carries no {loc:Str}, so
                // there is no binding for a language change to put back (CLAUDE.md).
                _txtHotkey.Text = string.IsNullOrWhiteSpace(chord)
                    ? Loc.Get("emi_desk_hotkey_unbound")
                    : chord;

                // Her CURRENT width, not the stored one: the grip writes settings on mouse-up, so
                // mid-drag the window is the truth and the file is one drag behind.
                double w = _owner?.BodyWidth ?? EmiDeskWindow.DefaultBodyWidth;
                _sldWidth.Value = Math.Max(_sldWidth.Minimum, Math.Min(_sldWidth.Maximum, w));
                _txtWidth.Text = ((int)Math.Round(_sldWidth.Value)).ToString();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] options load failed");
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>Run one settings write and persist it. CoreSettings.Current is never null.</summary>
        private static void Persist(Action write)
        {
            try
            {
                write();
                CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] options settings write failed");
            }
        }

        private void OnMuteChanged(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = _chkMute.IsChecked == true;
            // Compare before writing. Avalonia raises IsCheckedChanged on a PROGRAMMATIC set too,
            // and this handler has a SIDE EFFECT - a spurious raise would silently wipe the user's
            // "don't ask again" without the switch ever having moved.
            if (CoreSettings.Current.EmiDeskMuteAvatar == on) return;
            Persist(() =>
            {
                CoreSettings.Current.EmiDeskMuteAvatar = on;
                // Same rule as the settings tab: flipping the switch clears "don't ask again",
                // because the user has just changed their mind about the whole arrangement.
                CoreSettings.Current.EmiDeskMuteDontAsk = false;
            });
        }

        private void OnOffersChanged(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = _chkOffers.IsChecked == true;
            if (CoreSettings.Current.EmiDeskOffers == on) return;
            Persist(() => CoreSettings.Current.EmiDeskOffers = on);
        }

        private void OnGlassChanged(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = _chkGlass.IsChecked == true;
            if (CoreSettings.Current.EmiDeskGlass == on) return;
            Persist(() => CoreSettings.Current.EmiDeskGlass = on);
        }

        private void OnSpicePicked(int spice)
        {
            if (_loading) return;
            int v = Math.Max(0, Math.Min(2, spice));
            if (CoreSettings.Current.EmiDeskSpice == v) return;
            Persist(() => CoreSettings.Current.EmiDeskSpice = v);
        }

        /// <summary>
        /// The size slider is LIVE: she resizes under the pointer, exactly as she does on the grip.
        /// Settings are written on a short rest instead, because a drag across the band is a hundred
        /// value changes and a hundred writes of the whole settings file.
        /// </summary>
        private void OnWidthChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_loading) return;
            try
            {
                _txtWidth.Text = ((int)Math.Round(e.NewValue)).ToString();
                _owner?.ApplyBodyWidth(e.NewValue);
                _owner?.ClampIntoWorkArea();
                ArmWidthPersist();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] options width change failed");
            }
        }

        private void ArmWidthPersist()
        {
            try
            {
                if (_widthPersist == null)
                {
                    _widthPersist = new DispatcherTimer(DispatcherPriority.Background)
                    {
                        Interval = TimeSpan.FromMilliseconds(WidthPersistMs),
                    };
                    _widthPersist.Tick += (_, _) => PersistWidth();
                }
                _widthPersist.Stop();
                _widthPersist.Start();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] options width debounce failed");
            }
        }

        private void StopWidthPersist()
        {
            try { _widthPersist?.Stop(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] options width debounce stop failed"); }
        }

        private void PersistWidth()
        {
            StopWidthPersist();
            if (_owner is null) return;
            try
            {
                double w = _owner.BodyWidth;
                // Half a pixel, as WPF did: the slider reports every intermediate value of a drag
                // and a save rewrites the whole settings file.
                if (Math.Abs(CoreSettings.Current.EmiDeskWidth - w) > 0.5)
                {
                    CoreSettings.Current.EmiDeskWidth = w;
                    CoreSettings.Save();
                }
                _owner.SavePlacement();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] options width persist failed");
            }
        }

        // ---------------------------------------------------------------- placement

        /// <summary>
        /// The work area of the monitor she is standing on, in PHYSICAL pixels.
        ///
        /// <para>WPF asked WinForms' <c>Screen.FromPoint</c>. Avalonia's <c>Screens</c> answers the
        /// same question with the same units, so the only thing lost is the WinForms reference.</para>
        /// </summary>
        private PixelRect WorkArea()
        {
            try
            {
                var screens = Screens;
                if (screens == null || screens.ScreenCount == 0)
                    return new PixelRect(0, 0, 1920, 1080);

                // The monitor SHE is standing on, not the one this panel happens to be over: the
                // panel is placed relative to her, so a straddle has to clamp into her screen's
                // work area. With no owner, this window's own screen is the best guess left.
                var body = _owner?.BodyScreenRect;
                var screen = body is null
                    ? screens.ScreenFromWindow(this) ?? screens.Primary
                    : screens.ScreenFromPoint(new PixelPoint(
                          (int)Math.Round(body.Value.X + body.Value.Width / 2),
                          (int)Math.Round(body.Value.Y + body.Value.Height / 2))) ?? screens.Primary;
                return screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] options work-area probe failed");
                return new PixelRect(0, 0, 1920, 1080);
            }
        }

        /// <summary>
        /// Put the panel beside her, on the side that has room, clamped into the work area of the
        /// monitor she is standing on.
        ///
        /// <para>PHYSICAL PIXELS throughout, which is the one simplification the port buys: WPF's
        /// <c>Left</c>/<c>Top</c>/<c>Width</c> were DIPs while <c>BodyScreenRect</c> was physical,
        /// and mixing the two is the documented trap that ate the gaze work. Avalonia's
        /// <c>Position</c> is already a <c>PixelPoint</c>, so only this window's own DIP size is
        /// scaled and nothing else has to be.</para>
        ///
        /// <para>The gear is on her LEFT, so the panel goes left of her by preference and the
        /// pointer travels the shortest way to what it just opened. It flips to her right when the
        /// left side has no room, and only then falls back to overlapping her.</para>
        /// </summary>
        private void PlaceWindow()
        {
            try
            {
                var work = WorkArea();
                double s = DipScale;
                if (s <= 0) s = 1.0;

                // Never taller than the desk. SizeToContent grows the window to its content, so
                // without this cap a long pin wall would run off the bottom instead of scrolling.
                MaxHeight = Math.Max(220, work.Height / s - 24);

                // Measure with the cap applied: the height is content-driven and not known until now.
                UpdateLayout();

                // With no owner (the headless render) the panel sits at the work area's top-left
                // corner - the same fallback the clamp below would produce for a body pinned there.
                var b = _owner?.BodyScreenRect;
                var bodyPx = b is null
                    ? new PixelRect(work.X, work.Y, 1, 1)
                    : new PixelRect((int)Math.Round(b.Value.X), (int)Math.Round(b.Value.Y),
                                    (int)Math.Round(b.Value.Width), (int)Math.Round(b.Value.Height));

                double w = (Bounds.Width > 1 ? Bounds.Width : Width) * s;
                double h = (Bounds.Height > 1 ? Bounds.Height : MinHeight) * s;
                double gap = BodyGap * s;

                double left = bodyPx.X - gap - w;
                if (left < work.X)
                {
                    double right = bodyPx.Right + gap;
                    if (right + w <= work.Right) left = right;
                }

                // Top-aligned with her head, because that is where the gear is.
                double top = bodyPx.Y;

                Position = new PixelPoint(
                    (int)Math.Round(Math.Max(work.X, Math.Min(work.Right - w, left))),
                    (int)Math.Round(Math.Max(work.Y, Math.Min(work.Bottom - h, top))));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] options placement failed");
            }
        }

        /// <summary>She moved. The panel is anchored to her edge and has to come along.</summary>
        private void OnOwnerMoved(object? sender, EventArgs e)
        {
            if (!_open) return;
            try
            {
                PlaceWindow();
                UpdateHotRects();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] options follow-move failed");
            }
        }

        /// <summary>She was resized, by the grip or by this panel's own slider.</summary>
        /// <remarks>The grip can drive this too, so the slider follows her rather than the other
        /// way round; <c>_loading</c> keeps that from bouncing straight back into
        /// <c>ApplyBodyWidth</c>.</remarks>
        private void OnOwnerResized(object? sender, double width)
        {
            if (!_open) return;
            try
            {
                _loading = true;
                try
                {
                    _sldWidth.Value = Math.Max(_sldWidth.Minimum, Math.Min(_sldWidth.Maximum, width));
                    _txtWidth.Text = ((int)Math.Round(width)).ToString();
                }
                finally { _loading = false; }

                PlaceWindow();
                UpdateHotRects();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] options follow-resize failed");
            }
        }

        /// <summary>
        /// Freeze the rectangles a click-away hook is allowed to read - the panel itself and her
        /// body - so the hook never walks a live visual tree.
        /// </summary>
        // ponytail: dead until InstallHooks has something to install. The snapshot exists only to
        // be read on a hook thread, and there is no hook thread on this head; rebuilding it here
        // would be arithmetic nobody reads. Restore the WPF body with the hook.
        private void UpdateHotRects() { }

        // ---------------------------------------------------------------- the hooks

        /// <summary>
        /// WPF installed a low-level mouse hook and a low-level keyboard hook here, both
        /// <c>SetWindowsHookEx</c> in <c>Services.GlobalMouseHook</c> / <c>GlobalKeyboardHook</c>.
        ///
        /// <para>ponytail: no equivalent on this head. X11 has no supported desktop-wide input hook
        /// (XRecord is a debugging extension, not something to ship, and Wayland forbids it
        /// outright), so BOTH behaviours the hooks bought are lost for now:</para>
        /// <list type="bullet">
        ///   <item><b>Click-away no longer folds the panel.</b> A click anywhere off the panel and
        ///         off her body closed it; now only the x, "Open her cards", or the caller does.</item>
        ///   <item><b>Escape no longer folds the panel.</b> It was a GLOBAL Escape, deliberately:
        ///         the window can never hold the keyboard, so a local <c>KeyDown</c> here would not
        ///         be the same feature and is not offered as one.</item>
        /// </list>
        /// <para>The honest replacement is a compositor-side dismissal (an X11 pointer grab, or a
        /// layer-shell surface on Wayland), which is its own layer.</para>
        ///
        /// <para>WPF's <c>OnGlobalDown</c>, <c>OnGlobalKey</c> and the <c>Post</c> helper that
        /// marshalled them back onto the UI thread go with the hooks: they existed only to be
        /// called from a hook thread. On Avalonia that marshalling is one
        /// <c>Dispatcher.UIThread.Post</c> when there is finally something to marshal.</para>
        /// </summary>
        private void InstallHooks() { }

        /// <summary>Symmetrical stub. See <see cref="InstallHooks"/>.</summary>
        private void RemoveHooks() { }
    }
}
