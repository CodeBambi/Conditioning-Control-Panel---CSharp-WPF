using System;
using System.Runtime.InteropServices;
using System.Windows;
using SD = System.Drawing;
using WF = System.Windows.Forms;

namespace ConditioningControlPanel.Services.Fyp;

/// <summary>
/// The "ghost mode" mirror: a plain, empty window that shows a LIVE DWM thumbnail of the
/// For You feed window while the real one is parked off-screen. This is the OnTopReplica
/// technique, and it is the ONLY way this app can present a WebView2 surface as a genuinely
/// translucent, genuinely click-through pane:
///
///   * SetLayeredWindowAttributes on a window that HOSTS WebView2 turns its content solid
///     BLACK — DWM's layered redirection never sees the Chromium child processes'
///     cross-process DirectComposition visuals. (Shipped once, reproduced live 2026-08-03.)
///   * The blending rules were pixel-measured in isolation (2026-08-03, magenta-source /
///     green-backdrop probe):
///       - A thumbnail at DWM_TNP_OPACITY blends against the DESTINATION WINDOW'S OWN SURFACE,
///         not against what is behind the window.
///       - LWA_ALPHA multiplies the surface AND the thumbnail TOGETHER (alpha 128 halved a
///         full-opacity thumbnail), so no constant alpha can hide the surface yet keep the
///         mirror: alpha 1 blanks both, alpha 255 blends the mirror into opaque background.
///       - LWA_COLORKEY removes ONLY the surface pixels from composition; the thumbnail then
///         blends against whatever is behind the window — measured exact (0.35*src + 0.65*rear),
///         fullscreen-sized destinations included (DWM fullscreen optimization is not a problem).
///   * The mirror MUST be a GDI-surfaced (WinForms) window, NOT WPF: a WPF window renders its
///     surface through D3D and LWA_COLORKEY never matches it — the key color composes as a
///     solid sheet and the mirror degrades to "thumbnail over near-black" (observed live).
///     The probe that measured the exact blends above ran on a WinForms window; this class is
///     that configuration verbatim.
///
/// The surface is painted RGB(1,1,1) and color-keyed away (1,1,1 and not pure black, so
/// genuinely black pixels INSIDE the mirrored feed can never match the key — verified: content
/// blacks stay opaque), and the user's translucency rides DWM_TNP_OPACITY alone.
///
/// The real window keeps rendering (and playing audio, and receiving blink/gaze events) at its
/// parking spot off the virtual desktop; this window paints the picture of it, see-through and
/// input-transparent, covering the feed's home monitor. Nothing here ever touches the real
/// window's frame. Chromium must be told not to throttle the parked window — see the occlusion
/// browser flags in <see cref="FypHostService"/>, without which the mirror freezes.
/// </summary>
internal sealed class FypGhostOverlay
{
    private readonly IntPtr _sourceHwnd;
    private readonly WF.Screen _screen;
    private readonly MirrorForm _form;
    private readonly Action _onGear;
    private readonly Action _onMute;
    private readonly Func<bool> _isMuted;
    private GhostButton? _gearBtn;
    private GhostButton? _muteBtn;
    private IntPtr _thumb;
    private double _opacity;
    private bool _closed;

    /// <param name="sourceHwnd">HWND of the real feed window (the thumbnail source). Must still
    /// be on its home monitor when this runs — the ctor captures that monitor, so construct the
    /// overlay BEFORE parking the window.</param>
    /// <param name="bounds">Where the real window WAS, in DIPs (kept for the host's log).</param>
    /// <param name="opacity">0.01..1.0; the feed's persisted window-opacity setting.</param>
    /// <param name="onGear">Ghost gear button clicked (host un-ghosts and opens options).</param>
    /// <param name="onMute">Ghost speaker button clicked (host flips mute).</param>
    /// <param name="isMuted">Current mute state, for the speaker glyph.</param>
    public FypGhostOverlay(IntPtr sourceHwnd, Rect bounds, double opacity,
        Action onGear, Action onMute, Func<bool> isMuted)
    {
        _ = bounds;
        _sourceHwnd = sourceHwnd;
        _opacity = ClampOpacity(opacity);
        _onGear = onGear;
        _onMute = onMute;
        _isMuted = isMuted;
        _screen = WF.Screen.FromHandle(sourceHwnd);
        _form = new MirrorForm();
        _form.FormClosed += (_, _) => Unregister();
    }

    /// <summary>Show the mirror fullscreen on the feed's home monitor and start the live
    /// thumbnail. The thumbnail letterboxes at the source's aspect ratio; the bars are invisible
    /// because the color-keyed surface never composes.</summary>
    public void Show()
    {
        if (_closed) return;
        try
        {
            // Manual monitor-fill bounds in physical px (covers the taskbar too — fine, the
            // mirror is click-through). Never a Maximized state change: frameworks reassert
            // their cached styles on state transitions.
            _form.Bounds = _screen.Bounds;
            _form.Show();
            ApplyColorKey();
            EnsureThumbnail();
            ShowButtons();
        }
        catch (Exception ex) { App.Logger?.Debug("FypGhostOverlay.Show: {E}", ex.Message); }
    }

    /// <summary>The two controls that stay CLICKABLE in ghost mode. They cannot live on the
    /// mirror (its whole surface is WS_EX_TRANSPARENT by design), so each is its own tiny
    /// topmost window without that style — small clickable islands over a click-through sea.
    /// Positions echo the page's own chrome: gear top-left, speaker top-right.</summary>
    private void ShowButtons()
    {
        try
        {
            if (_closed || _gearBtn != null) return;
            var b = _screen.Bounds;
            // Physical px, sized off the monitor's scale so they match the page's 40-DIP buttons.
            int size = Math.Max(40, (int)Math.Round(b.Height * 0.037));
            int pad = size / 2;
            _gearBtn = new GhostButton("⚙", "Feed options (leaves ghost mode)",
                new SD.Rectangle(b.X + pad, b.Y + pad, size, size), () => _onGear());
            _muteBtn = new GhostButton(_isMuted() ? "🔇" : "🔊", "Mute / unmute",
                new SD.Rectangle(b.Right - pad - size, b.Y + pad, size, size), () => _onMute());
        }
        catch (Exception ex) { App.Logger?.Debug("FypGhostOverlay.ShowButtons: {E}", ex.Message); }
    }

    /// <summary>Re-draw the speaker glyph after the host flips mute.</summary>
    public void RefreshMuteGlyph()
    {
        try { _muteBtn?.SetGlyph(_isMuted() ? "🔇" : "🔊"); }
        catch (Exception ex) { App.Logger?.Debug("FypGhostOverlay.RefreshMuteGlyph: {E}", ex.Message); }
    }

    /// <summary>Live opacity update (the page's slider). Re-sends the thumbnail properties —
    /// the alpha rides DWM_TNP_OPACITY, not the window (see the class comment).</summary>
    public void SetOpacity(double v)
    {
        _opacity = ClampOpacity(v);
        UpdateThumbnailBounds();
    }

    /// <summary>Drop the registration and register a fresh thumbnail on the same source. The
    /// host calls this after healing a minimize of the parked window (Show Desktop reaches it
    /// too): DWM freezes a minimized source's thumbnail on its last frame and does not reliably
    /// resume the OLD registration once the window is restored off-screen.</summary>
    public void RefreshThumbnail()
    {
        if (_closed) return;
        Unregister();
        EnsureThumbnail();
    }

    /// <summary>Drop the thumbnail and close the mirror and its buttons. Idempotent.</summary>
    public void Close()
    {
        if (_closed) return;
        _closed = true;
        Unregister();
        try { _gearBtn?.Close(); }
        catch (Exception ex) { App.Logger?.Debug("FypGhostOverlay.Close gear: {E}", ex.Message); }
        try { _muteBtn?.Close(); }
        catch (Exception ex) { App.Logger?.Debug("FypGhostOverlay.Close mute: {E}", ex.Message); }
        _gearBtn = null;
        _muteBtn = null;
        try { _form.Close(); _form.Dispose(); }
        catch (Exception ex) { App.Logger?.Debug("FypGhostOverlay.Close: {E}", ex.Message); }
    }

    private void ApplyColorKey()
    {
        try
        {
            // Key out the RGB(1,1,1) surface — NOT LWA_ALPHA, which would multiply the
            // thumbnail too (see the class comment). With the surface gone, the thumbnail's
            // DWM_TNP_OPACITY blends the feed straight against the desktop behind the window.
            SetLayeredWindowAttributes(_form.Handle, COLOR_KEY, 0, LWA_COLORKEY);
        }
        catch (Exception ex) { App.Logger?.Debug("FypGhostOverlay.ApplyColorKey: {E}", ex.Message); }
    }

    private void EnsureThumbnail()
    {
        try
        {
            if (_closed || _sourceHwnd == IntPtr.Zero) return;
            if (_thumb == IntPtr.Zero)
            {
                int hr = DwmRegisterThumbnail(_form.Handle, _sourceHwnd, out var thumb);
                if (hr != 0 || thumb == IntPtr.Zero)
                {
                    App.Logger?.Warning("FypGhostOverlay: DwmRegisterThumbnail failed (0x{HR:X8})", hr);
                    return;
                }
                _thumb = thumb;
            }
            UpdateThumbnailBounds();
        }
        catch (Exception ex) { App.Logger?.Debug("FypGhostOverlay.EnsureThumbnail: {E}", ex.Message); }
    }

    /// <summary>Point the thumbnail at our client area (physical px throughout — WinForms and
    /// rcDestination agree, no DIP math). Letterbox: scale the source's client rect to fit,
    /// centered; a stretched mirror would distort the moment the aspects differ.</summary>
    private void UpdateThumbnailBounds()
    {
        try
        {
            if (_closed || _thumb == IntPtr.Zero) return;
            int destW = Math.Max(1, _form.ClientSize.Width);
            int destH = Math.Max(1, _form.ClientSize.Height);

            int x0 = 0, y0 = 0, x1 = destW, y1 = destH;
            if (GetClientRect(_sourceHwnd, out var src) && src.Right > 0 && src.Bottom > 0)
            {
                double scale = Math.Min((double)destW / src.Right, (double)destH / src.Bottom);
                int fitW = Math.Max(1, (int)Math.Round(src.Right * scale));
                int fitH = Math.Max(1, (int)Math.Round(src.Bottom * scale));
                x0 = (destW - fitW) / 2; y0 = (destH - fitH) / 2;
                x1 = x0 + fitW; y1 = y0 + fitH;
            }

            var props = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE
                          | DWM_TNP_SOURCECLIENTAREAONLY | DWM_TNP_OPACITY,
                rcDestination = new RECT { Left = x0, Top = y0, Right = x1, Bottom = y1 },
                // The user's translucency — with the surface keyed away, DWM blends the
                // thumbnail against the desktop at exactly this alpha (pixel-verified).
                opacity = (byte)Math.Round(Math.Clamp(_opacity, OpacityMin, 1.0) * 255.0),
                fVisible = true,
                fSourceClientAreaOnly = true,   // clip the source's title bar / border away
            };
            DwmUpdateThumbnailProperties(_thumb, ref props);
        }
        catch (Exception ex) { App.Logger?.Debug("FypGhostOverlay.UpdateThumbnailBounds: {E}", ex.Message); }
    }

    private void Unregister()
    {
        var t = _thumb;
        _thumb = IntPtr.Zero;
        if (t == IntPtr.Zero) return;
        try { DwmUnregisterThumbnail(t); }
        catch (Exception ex) { App.Logger?.Debug("FypGhostOverlay.Unregister: {E}", ex.Message); }
    }

    private const double OpacityMin = 0.01;
    private static double ClampOpacity(double v)
        => double.IsFinite(v) ? Math.Clamp(v, OpacityMin, 1.0) : 1.0;

    /// <summary>The mirror surface. Ex-styles are baked into CreateParams so they exist from
    /// creation — nothing is stamped behind a framework's back, nothing can reassert over them.
    /// LAYERED = required for the colorkey. TRANSPARENT = every click, wheel and hover falls
    /// straight through to the desktop. NOACTIVATE = never takes focus. TOOLWINDOW = no taskbar
    /// button and no Alt-Tab entry.</summary>
    private sealed class MirrorForm : WF.Form
    {
        public MirrorForm()
        {
            FormBorderStyle = WF.FormBorderStyle.None;
            StartPosition = WF.FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Text = "For You";
            // The color key — every surface pixel must be exactly RGB(1,1,1).
            BackColor = SD.Color.FromArgb(1, 1, 1);
        }

        protected override bool ShowWithoutActivation => true;

        protected override WF.CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }
    }

    /// <summary>One round, clickable button window for ghost mode. NOACTIVATE keeps clicks from
    /// stealing focus (mouse messages still arrive without activation); crucially it does NOT
    /// carry WS_EX_TRANSPARENT, so unlike everything else on the ghost screen it eats its
    /// clicks. Styled after the page's .round-btn chrome: dark disc, pink on hover.</summary>
    private sealed class GhostButton : WF.Form
    {
        private readonly Action _onClick;
        private string _glyph;
        private bool _hover;

        public GhostButton(string glyph, string tooltip, SD.Rectangle bounds, Action onClick)
        {
            _glyph = glyph;
            _onClick = onClick;
            FormBorderStyle = WF.FormBorderStyle.None;
            StartPosition = WF.FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Text = "For You";
            Cursor = WF.Cursors.Hand;
            DoubleBuffered = true;
            BackColor = SD.Color.FromArgb(18, 18, 31);
            Bounds = bounds;
            Region = new SD.Region(EllipsePath(bounds.Width, bounds.Height));
            new WF.ToolTip().SetToolTip(this, tooltip);
            Show();
            // A see-through feed can sit at 1% — keep the buttons discoverable but quiet.
            // Plain SLWA alpha is safe here: no DWM thumbnail rides this window.
            Opacity = 0.82;
        }

        public void SetGlyph(string glyph)
        {
            _glyph = glyph;
            Invalidate();
        }

        protected override bool ShowWithoutActivation => true;

        protected override WF.CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(WF.MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == WF.MouseButtons.Left)
            {
                try { _onClick(); }
                catch (Exception ex) { App.Logger?.Debug("GhostButton click: {E}", ex.Message); }
            }
        }

        protected override void OnPaint(WF.PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var fill = new SD.SolidBrush(_hover
                ? SD.Color.FromArgb(255, 105, 180)
                : SD.Color.FromArgb(31, 31, 54));
            using var rim = new SD.Pen(SD.Color.FromArgb(255, 105, 180), 1.5f);
            g.FillEllipse(fill, 1, 1, Width - 3, Height - 3);
            g.DrawEllipse(rim, 1, 1, Width - 3, Height - 3);
            using var font = new SD.Font("Segoe UI Emoji", Height * 0.38f, SD.GraphicsUnit.Pixel);
            var sz = g.MeasureString(_glyph, font);
            using var text = new SD.SolidBrush(SD.Color.White);
            g.DrawString(_glyph, font, text, (Width - sz.Width) / 2f, (Height - sz.Height) / 2f);
        }

        private static System.Drawing.Drawing2D.GraphicsPath EllipsePath(int w, int h)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            p.AddEllipse(0, 0, w, h);
            return p;
        }
    }

    // ------------------------------- native -------------------------------

    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int LWA_COLORKEY = 0x00000001;
    private const uint COLOR_KEY = 0x00010101;   // COLORREF for RGB(1,1,1) — must match BackColor

    private const int DWM_TNP_RECTDESTINATION = 0x00000001;
    private const int DWM_TNP_OPACITY = 0x00000004;
    private const int DWM_TNP_VISIBLE = 0x00000008;
    private const int DWM_TNP_SOURCECLIENTAREAONLY = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_THUMBNAIL_PROPERTIES
    {
        public int dwFlags;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmUnregisterThumbnail(IntPtr thumb);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES props);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte alpha, int dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
}
