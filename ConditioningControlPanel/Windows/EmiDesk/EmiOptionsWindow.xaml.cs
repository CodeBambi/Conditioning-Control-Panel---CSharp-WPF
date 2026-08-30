using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using Serilog;

// NAMESPACE TRAP (same note as EmiDeskWindow and EmiRingWindow): everything under Windows\ lives in
// the FLAT ConditioningControlPanel namespace. A ConditioningControlPanel.Windows.* namespace
// shadows the WinRT Windows root and breaks Services\ScreenOcrService.cs with a CS0234 that names a
// file you never touched. Do not "tidy" it.
namespace ConditioningControlPanel;

/// <summary>
/// HER OPTIONS: the little panel the gear opens beside her.
///
/// <para><b>Why it exists.</b> Wave 3 put a six-pixel-dot glyph top-left that opened her ring. The
/// owner read it as a move handle - "the three dots to move emi, not needed, we drag her" - and
/// asked for a gear that opens her options instead (2026-08-30). Since those dots were the ring's
/// only VISIBLE way in (nothing on a desktop advertises a right click), the first thing in this
/// panel is "Open her cards".</para>
///
/// <para><b>The window recipe is not optional</b> and is copied from her other two windows:
/// WindowStyle None, AllowsTransparency, ShowActivated false, ShowInTaskbar false, Topmost, and
/// WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE stamped in OnSourceInitialized. She is a desktop ornament,
/// not an application window: no focus theft, nothing in Alt-Tab, and no click that costs the user
/// the window they were working in.</para>
///
/// <para><b>And it is not modal, deliberately.</b> §10.8b of the primer: anything modal on a summon
/// path needs the <c>_summonGen</c> generation guard, because the dismiss that happens while the
/// dialog is up desyncs <c>IsOut</c> and strands her on screen. A non-modal panel that closes itself
/// on a click outside has none of that surface.</para>
///
/// <para><b>WS_EX_NOACTIVATE means this window can never hold the keyboard</b>, and that is what
/// shapes its contents. Every editor in here is mouse-only. The summon chord is SHOWN rather than
/// captured (a capture needs key events that will never arrive) and points at the settings tab,
/// which can take focus and does own the rebind.</para>
///
/// <para><b>Placement is physical pixels over this window's own DPI</b>, the same arithmetic
/// <c>EmiRingWindow.PlaceWindow</c> uses. <c>BodyScreenRect</c> is PHYSICAL pixels and dividing it
/// by an assumed 1.0 is the coordinate trap that ate the gaze work; at 150 % it would put the panel
/// a third of the way across the desk.</para>
/// </summary>
public partial class EmiOptionsWindow : Window
{
    /// <summary>Air between her silhouette and the panel's near edge, in DIPs.</summary>
    private const double BodyGap = 12.0;

    /// <summary>How long a width drag rests before the number is written to settings.</summary>
    private const int WidthPersistMs = 400;

    private readonly EmiDeskWindow _owner;

    private Services.GlobalMouseHook? _mouse;
    private Services.GlobalKeyboardHook? _keys;

    /// <summary>
    /// The rectangles a global click may land in without folding the panel - the panel itself and
    /// her body - in PHYSICAL pixels. Read on the HOOK thread, so it is swapped whole and never
    /// walked live. Her body is in the list so that a second click on the gear TOGGLES the panel
    /// shut through <c>OnGearClick</c> instead of the hook closing it a hair before the click
    /// arrives and the click opening it straight back up.
    /// </summary>
    private volatile Rect[] _hotPx = Array.Empty<Rect>();

    private bool _open;
    private bool _closingForGood;

    /// <summary>Suppresses the change handlers while the code is filling the controls in.</summary>
    private bool _loading;

    private DispatcherTimer? _widthPersist;

    // ---------------------------------------------------------------- ctor

    /// <summary>Builds the panel for one widget. Created hidden; <see cref="OpenPanel"/> shows it.</summary>
    public EmiOptionsWindow(EmiDeskWindow owner)
    {
        InitializeComponent();
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        SourceInitialized += OnSourceInitialized;

        Localize();
        WireControls();

        // The chrome on her body stays lit while the pointer is in here, so the gear that opened
        // this is still on screen to close it again after the round trip.
        MouseEnter += (_, _) => Hold(true);
        MouseLeave += (_, _) => Hold(false);

        // She moves, she resizes: the panel is anchored to her edge and has to come along.
        _owner.Moved += OnOwnerMoved;
        _owner.Resized += OnOwnerResized;
    }

    /// <summary>The panel folded. The desk window drops its chrome hold on this.</summary>
    public event EventHandler? PanelClosed;

    /// <summary>"Open her cards" was clicked. The desk window owns what a ring is; this only asks.</summary>
    public event EventHandler? CardsRequested;

    /// <summary>True while the panel is on screen.</summary>
    public bool IsOpen => _open;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] options window ex-style failed");
        }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    /// <summary>This window's own DPI scale. Never assume 1.0 on a multi-monitor desk.</summary>
    private double DipScale
    {
        get
        {
            try
            {
                var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice;
                if (m.HasValue && m.Value.M11 > 0) return m.Value.M11;
            }
            catch { /* no source yet */ }
            try
            {
                using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
                return g.DpiX / 96.0;
            }
            catch { return 1.0; }
        }
    }

    // ---------------------------------------------------------------- wiring

    private void Localize()
    {
        try
        {
            TxtTitle.Text = Loc.Get("emi_desk_opt_title");
            BtnOpenCards.Content = Loc.Get("emi_desk_tip_cards");
            TxtCardsHint.Text = Loc.Get("emi_desk_opt_cards_hint");
            TxtHeadOptions.Text = Loc.Get("emi_desk_opt_head_options");
            TxtHeadRing.Text = Loc.Get("emi_desk_opt_head_ring");
            LblHotkey.Text = Loc.Get("emi_desk_opt_hotkey");
            TxtHotkeyHint.Text = Loc.Get("emi_desk_opt_hotkey_hint");
            LblWidth.Text = Loc.Get("emi_desk_opt_width");
            LblMute.Text = Loc.Get("emi_desk_opt_mute");
            LblSpice.Text = Loc.Get("emi_desk_opt_spice");
            LblOffers.Text = Loc.Get("emi_desk_opt_offers");
            LblGlass.Text = Loc.Get("emi_desk_opt_glass");
            SpiceInnocent.Content = Loc.Get("emi_desk_spice_innocent");
            SpiceSuggestive.Content = Loc.Get("emi_desk_spice_suggestive");
            SpiceAnything.Content = Loc.Get("emi_desk_spice_anything");
            BtnPanelClose.ToolTip = Loc.Get("emi_desk_opt_close");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] options localize failed");
        }
    }

    private void WireControls()
    {
        BtnPanelClose.Click += (_, _) => ClosePanel();

        BtnOpenCards.Click += (_, _) =>
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

        SldWidth.Minimum = EmiDeskWindow.MinBodyWidth;
        SldWidth.Maximum = EmiDeskWindow.MaxBodyWidth;
        SldWidth.ValueChanged += OnWidthChanged;
        // A track click is not a drag, so the debounce below is what actually persists both.
        SldWidth.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => PersistWidth()));

        ChkMute.Checked += OnMuteChanged;
        ChkMute.Unchecked += OnMuteChanged;
        ChkOffers.Checked += OnOffersChanged;
        ChkOffers.Unchecked += OnOffersChanged;
        ChkGlass.Checked += OnGlassChanged;
        ChkGlass.Unchecked += OnGlassChanged;

        SpiceInnocent.Checked += (_, _) => OnSpicePicked(0);
        SpiceSuggestive.Checked += (_, _) => OnSpicePicked(1);
        SpiceAnything.Checked += (_, _) => OnSpicePicked(2);
    }

    private void Hold(bool on)
    {
        try { _owner.HoldChromeForPanel(on); }
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
            // Rebuilt rather than refreshed: availability and lock are DELEGATES on a target, and
            // both can have changed since the last time this was opened.
            RingPicker.Rebuild();

            PlaceWindow();
            if (!IsVisible)
            {
                Show();
                // The window has no DPI of its own until it has an HWND, so the first placement is
                // always done twice: once to get it on screen, once with its real scale and its
                // real measured height.
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

    /// <summary>Fold the panel. Idempotent, and safe to call from a hook continuation.</summary>
    public void ClosePanel()
    {
        try
        {
            RemoveHooks();
            StopWidthPersist();
            if (!_open && !IsVisible) return;
            _open = false;
            _hotPx = Array.Empty<Rect>();
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
            _owner.Moved -= OnOwnerMoved;
            _owner.Resized -= OnOwnerResized;
            Close();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] options kill failed");
        }
    }

    // ---------------------------------------------------------------- values

    private void LoadValues()
    {
        try
        {
            _loading = true;

            var s = App.Settings?.Current;
            if (s != null)
            {
                ChkMute.IsChecked = s.EmiDeskMuteAvatar;
                ChkOffers.IsChecked = s.EmiDeskOffers;
                ChkGlass.IsChecked = s.EmiDeskGlass;

                // The three segments ARE the 0..2 scale the lines file carries. No translation.
                int spice = Math.Max(0, Math.Min(2, s.EmiDeskSpice));
                SpiceInnocent.IsChecked = spice == 0;
                SpiceSuggestive.IsChecked = spice == 1;
                SpiceAnything.IsChecked = spice == 2;

                var chord = s.EmiDeskHotkey;
                TxtHotkey.Text = string.IsNullOrWhiteSpace(chord)
                    ? Loc.Get("emi_desk_hotkey_unbound")
                    : chord;
            }

            // Her CURRENT width, not the stored one: the grip writes settings on mouse-up, so
            // mid-drag the window is the truth and the file is one drag behind.
            double w = _owner.BodyWidth;
            SldWidth.Value = Math.Max(SldWidth.Minimum, Math.Min(SldWidth.Maximum, w));
            TxtWidth.Text = ((int)Math.Round(SldWidth.Value)).ToString();
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

    private static void Persist(Action write)
    {
        try
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            write();
            App.Settings?.Save();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] options settings write failed");
        }
    }

    private void OnMuteChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = ChkMute.IsChecked == true;
        Persist(() =>
        {
            App.Settings!.Current.EmiDeskMuteAvatar = on;
            // Same rule as the settings tab: flipping the switch clears "don't ask again",
            // because the user has just changed their mind about the whole arrangement.
            App.Settings!.Current.EmiDeskMuteDontAsk = false;
        });
    }

    private void OnOffersChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = ChkOffers.IsChecked == true;
        Persist(() => App.Settings!.Current.EmiDeskOffers = on);
    }

    private void OnGlassChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = ChkGlass.IsChecked == true;
        Persist(() => App.Settings!.Current.EmiDeskGlass = on);
    }

    private void OnSpicePicked(int spice)
    {
        if (_loading) return;
        int v = Math.Max(0, Math.Min(2, spice));
        Persist(() => App.Settings!.Current.EmiDeskSpice = v);
    }

    /// <summary>
    /// The size slider is LIVE: she resizes under the pointer, exactly as she does on the grip.
    /// Settings are written on a short rest instead, because a drag across the band is a hundred
    /// value changes and a hundred writes of the whole settings file.
    /// </summary>
    private void OnWidthChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        try
        {
            TxtWidth.Text = ((int)Math.Round(e.NewValue)).ToString();
            _owner.ApplyBodyWidth(e.NewValue);
            _owner.ClampIntoWorkArea();
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
        try
        {
            double w = _owner.BodyWidth;
            var s = App.Settings?.Current;
            if (s != null && Math.Abs(s.EmiDeskWidth - w) > 0.5)
            {
                s.EmiDeskWidth = w;
                App.Settings?.Save();
            }
            _owner.SavePlacement();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] options width persist failed");
        }
    }

    // ---------------------------------------------------------------- placement

    private System.Drawing.Rectangle WorkArea()
    {
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens == null || screens.Length == 0)
                return new System.Drawing.Rectangle(0, 0, 1920, 1080);

            var body = _owner.BodyScreenRect;
            var centre = new System.Drawing.Point(
                (int)Math.Round(body.X + body.Width / 2),
                (int)Math.Round(body.Y + body.Height / 2));
            return System.Windows.Forms.Screen.FromPoint(centre).WorkingArea;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] options work-area probe failed");
            return new System.Drawing.Rectangle(0, 0, 1920, 1080);
        }
    }

    /// <summary>
    /// Put the panel beside her, on the side that has room, clamped into the work area of the
    /// monitor she is standing on.
    ///
    /// <para>PHYSICAL PIXELS over THIS window's own DPI, both sides of the sum. Her
    /// <c>BodyScreenRect</c> is physical; every WPF <c>Left</c>/<c>Top</c>/<c>Width</c> here is
    /// DIPs. Mixing the two is the documented trap.</para>
    ///
    /// <para>The gear is on her LEFT, so the panel goes left of her by preference and the pointer
    /// travels the shortest way to what it just opened. It flips to her right when the left side
    /// has no room, and only then falls back to overlapping her.</para>
    /// </summary>
    private void PlaceWindow()
    {
        try
        {
            var work = WorkArea();
            double s = DipScale;
            if (s <= 0) s = 1.0;

            double workL = work.Left / s;
            double workT = work.Top / s;
            double workW = work.Width / s;
            double workH = work.Height / s;

            // Never taller than the desk. SizeToContent grows the window to its content, so
            // without this cap a long pin wall would run off the bottom instead of scrolling.
            MaxHeight = Math.Max(220, workH - 24);

            // Measure with the cap applied: the height is content-driven and not known until now.
            UpdateLayout();

            var bodyPx = _owner.BodyScreenRect;
            double bodyL = bodyPx.Left / s;
            double bodyT = bodyPx.Top / s;
            double bodyR = bodyPx.Right / s;

            double w = ActualWidth > 1 ? ActualWidth : Width;
            double h = ActualHeight > 1 ? ActualHeight : MinHeight;

            double left = bodyL - BodyGap - w;
            if (left < workL)
            {
                double right = bodyR + BodyGap;
                if (right + w <= workL + workW) left = right;
            }

            // Top-aligned with her head, because that is where the gear is.
            double top = bodyT;

            Left = Math.Max(workL, Math.Min(workL + workW - w, left));
            Top = Math.Max(workT, Math.Min(workT + workH - h, top));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] options placement failed");
        }
    }

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

    private void OnOwnerResized(object? sender, double width)
    {
        if (!_open) return;
        try
        {
            // The grip can drive this too, so the slider follows her rather than the other way
            // round. _loading keeps that from bouncing straight back into ApplyBodyWidth.
            _loading = true;
            try
            {
                SldWidth.Value = Math.Max(SldWidth.Minimum, Math.Min(SldWidth.Maximum, width));
                TxtWidth.Text = ((int)Math.Round(width)).ToString();
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
    /// Freeze the rectangles the hook thread is allowed to read. Built on the UI thread and swapped
    /// whole; the hook never walks a live visual tree.
    /// </summary>
    private void UpdateHotRects()
    {
        try
        {
            double s = DipScale;
            if (s <= 0) s = 1.0;

            double w = ActualWidth > 1 ? ActualWidth : Width;
            double h = ActualHeight > 1 ? ActualHeight : 0;

            var panel = new Rect(Left * s, Top * s, Math.Max(1, w * s), Math.Max(1, h * s));

            var bodyPx = _owner.BodyScreenRect;
            var body = new Rect(bodyPx.X, bodyPx.Y, Math.Max(1, bodyPx.Width), Math.Max(1, bodyPx.Height));

            _hotPx = new[] { panel, body };
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] options hot-rect snapshot failed");
            _hotPx = Array.Empty<Rect>();
        }
    }

    // ---------------------------------------------------------------- the hooks

    private void InstallHooks()
    {
        try
        {
            if (_mouse == null)
            {
                _mouse = new Services.GlobalMouseHook { LeftDown = OnGlobalDown, RightDown = OnGlobalDown };
                _mouse.Start();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] options mouse hook failed to install, click-away will not close it");
        }

        try
        {
            if (_keys == null)
            {
                _keys = new Services.GlobalKeyboardHook();
                _keys.KeyPressed += OnGlobalKey;
                _keys.Start();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] options key hook failed to install, Escape will not close it");
        }
    }

    private void RemoveHooks()
    {
        try
        {
            if (_mouse != null)
            {
                _mouse.LeftDown = null;
                _mouse.RightDown = null;
                _mouse.Stop();
                _mouse.Dispose();
                _mouse = null;
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] options mouse hook removal failed"); }

        try
        {
            if (_keys != null)
            {
                _keys.KeyPressed -= OnGlobalKey;
                _keys.Stop();
                _keys.Dispose();
                _keys = null;
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] options key hook removal failed"); }
    }

    /// <summary>
    /// Runs on the HOOK thread. Cheap, touches nothing but the frozen snapshot, and ALWAYS returns
    /// false: swallowing the click would make closing this panel cost the user whatever they were
    /// actually aiming at.
    /// </summary>
    private bool OnGlobalDown(Point ptPx)
    {
        try
        {
            var hot = _hotPx;
            for (int i = 0; i < hot.Length; i++)
            {
                if (hot[i].Contains(ptPx)) return false;
            }
            Post(ClosePanel);
        }
        catch { /* a hook callback never throws */ }
        return false;
    }

    private void OnGlobalKey(Key k)
    {
        try
        {
            if (k != Key.Escape) return;
            Post(ClosePanel);
        }
        catch { /* a hook callback never throws */ }
    }

    private static void Post(Action a)
    {
        try
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.HasShutdownStarted) return;
            d.BeginInvoke(new Action(() =>
            {
                try { a(); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] options posted action threw"); }
            }));
        }
        catch { /* shutting down */ }
    }
}
