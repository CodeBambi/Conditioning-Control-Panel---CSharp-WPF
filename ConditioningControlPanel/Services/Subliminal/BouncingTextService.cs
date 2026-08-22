using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Bouncing Text - DVD screensaver style text that bounces across screens
/// Unlocks at Level 60, awards 15 XP per bounce (max 150 XP/min, 2s cooldown between rewards).
/// Supports one or two independent logos, three color modes (random / fixed / rainbow cycle)
/// and a set of per-frame transform effects (breathing, wobble, spin, velocity tilt,
/// squash &amp; stretch, corner burst).
/// </summary>
public class BouncingTextService : IDisposable
{
    private readonly Random _random = new();
    private readonly List<BouncingTextWindow> _windows = new();
    private bool _isRunning;

    // Composition-clock state. We drive motion off CompositionTarget.Rendering
    // (vsync-aligned, one callback per rendered frame) rather than a DispatcherTimer
    // (quantized to the ~15.6ms OS tick, which beats against the display refresh and
    // produces "low frame rate" judder). _lastRenderTime feeds delta-time movement.
    private TimeSpan _lastRenderTime = TimeSpan.MinValue;

    /// <summary>Per-logo bounce state. One or two of these exist while running.</summary>
    private sealed class Logo
    {
        public string Text = "";
        public double PosX, PosY;
        public double VelX, VelY;
        public double TextWidth = 200, TextHeight = 60;
        public Color Color;
        public int HueIndex;          // rainbow-cycle position
        public double Phase;          // per-logo offset so two logos never breathe/wobble in sync
        public double SpinAngle;      // accumulated continuous-spin angle (deg)
        public double Tilt;           // smoothed velocity-lean angle (deg)
        public double SquashTimer = -1; // seconds since last wall hit; <0 = idle
        public bool SquashAxisX;      // true = hit a vertical wall (X velocity reversed)
        public double BurstTimer = -1;  // seconds since last corner hit; <0 = idle
    }

    private readonly List<Logo> _logos = new();
    private List<string>? _poolOverride; // AI/session-supplied pool, kept for mid-run re-rolls
    private double _fxTime;              // shared effects clock (seconds)

    // Text size - base size that gets scaled by settings
    private const int BASE_FONT_SIZE = 72;
    private int _currentFontSize = BASE_FONT_SIZE;

    // Corner hit detection - tolerance in pixels (corners are hard to hit exactly)
    private const double CORNER_TOLERANCE = 15.0;

    // Anti-exploit: XP rate limiting for bounces (shared across logos so a second
    // text doesn't double the XP income)
    private DateTime _lastBounceXpTime = DateTime.MinValue;
    private static readonly TimeSpan BounceXpCooldown = TimeSpan.FromSeconds(2); // Short cooldown to prevent double-count on corner hits
    private int _bounceXpThisMinute;
    private DateTime _bounceXpMinuteStart = DateTime.MinValue;
    private const int MaxBounceXpPerMinute = 150;

    private double _minX, _minY, _maxX, _maxY;

    // Z-order re-assertion accumulator (re-assert topmost every ~0.5s of real time;
    // frame-rate-independent now that the tick rate varies with the display refresh).
    private double _zReassertAccum;

    // Ordered hue wheel for the rainbow-cycle color mode (advances one step per bounce).
    private static readonly Color[] RainbowWheel = BuildRainbowWheel();

    public bool IsRunning => _isRunning;

    /// <summary>
    /// Current bounding rects of the bouncing logos in PHYSICAL virtual-desktop
    /// pixels, padded to absorb motion between OCR self-exclusion cache refreshes.
    /// Consumed by <see cref="App.GetCcpWindowRectsCached"/> so the avatar's
    /// awareness OCR doesn't read the app's own bouncing words (#287). The
    /// full-screen overlay window itself is dropped from the exclusion set by the
    /// per-monitor span filter, so these small moving rects have to be supplied
    /// separately. One rect per logo, in shared virtual-desktop DIP space.
    ///
    /// Caller must invoke on the UI thread (the animation timer mutates these
    /// fields there). Returns empty when not running.
    /// </summary>
    public System.Drawing.Rectangle[] GetActiveTextScreenRects()
    {
        if (!_isRunning) return Array.Empty<System.Drawing.Rectangle>();
        try
        {
            var dpiScale = GetDpiScale();
            var rects = new System.Drawing.Rectangle[_logos.Count];
            for (int i = 0; i < _logos.Count; i++)
            {
                var l = _logos[i];
                // Pad generously: the text drifts a few px/frame and the OCR rect
                // cache is ~250ms stale, and the transform effects can scale the
                // text up to ~1.5x and rotate it (a rotated rect's bbox grows by up
                // to (w+h)/2 per axis). Bouncing text is large and isolated, so
                // modest over-exclusion costs nothing.
                double padX = l.TextWidth * 0.5 + l.TextHeight * 0.5 + 80;
                double padY = l.TextWidth * 0.5 + l.TextHeight * 0.5 + 80;
                int left   = (int)Math.Floor((l.PosX - padX) * dpiScale);
                int top    = (int)Math.Floor((l.PosY - padY) * dpiScale);
                int right  = (int)Math.Ceiling((l.PosX + l.TextWidth + padX) * dpiScale);
                int bottom = (int)Math.Ceiling((l.PosY + l.TextHeight + padY) * dpiScale);
                rects[i] = new System.Drawing.Rectangle(left, top, right - left, bottom - top);
            }
            return rects;
        }
        catch
        {
            return Array.Empty<System.Drawing.Rectangle>();
        }
    }

    public event EventHandler? OnBounce;
    /// <summary>Fires on a true/near DVD corner hit (distinct from a plain wall bounce). Used by the bark egg.</summary>
    public event EventHandler? OnCornerHit;

    public void Start(bool bypassLevelCheck = false, List<string>? pool = null)
    {
        if (_isRunning) return;

        var settings = App.Settings.Current;

        // Note: We don't check BouncingTextEnabled here because Start() is called
        // explicitly when we want to start (either by toggle or by session)

        _isRunning = true;
        _poolOverride = pool != null && pool.Count > 0 ? pool.ToList() : null;
        _fxTime = 0;

        // Calculate font size based on settings (50-300% of base)
        _currentFontSize = (int)(BASE_FONT_SIZE * settings.BouncingTextSize / 100.0);

        // Calculate screen bounds
        CalculateScreenBounds(settings.DualMonitorEnabled);

        // One logo, or two independent ones when the second-text toggle is on
        _logos.Clear();
        int logoCount = settings.BouncingTextSecondText ? 2 : 1;
        for (int i = 0; i < logoCount; i++)
        {
            var logo = new Logo
            {
                Phase = _random.NextDouble() * Math.PI * 2,
                HueIndex = _random.Next(RainbowWheel.Length),
            };
            logo.Text = SelectRandomText();
            MeasureTextSize(logo);

            // Random starting position (ensure text starts fully within bounds)
            logo.PosX = _minX + _random.NextDouble() * Math.Max(1, (_maxX - _minX - logo.TextWidth));
            logo.PosY = _minY + _random.NextDouble() * Math.Max(1, (_maxY - _minY - logo.TextHeight));

            // Random velocity in DIP/second (speed based on setting). The base is the
            // old 3-5 px/tick value scaled by 60 so the on-screen feel is unchanged at
            // 60 FPS, but motion is now delta-time driven so it stays correct at any rate.
            var speed = settings.BouncingTextSpeed / 10.0; // 1-10 maps to 0.1-1.0 multiplier
            var baseSpeed = (3.0 + _random.NextDouble() * 2.0) * 60.0; // 180-300 DIP/sec
            logo.VelX = baseSpeed * speed * (_random.Next(2) == 0 ? 1 : -1);
            logo.VelY = baseSpeed * speed * (_random.Next(2) == 0 ? 1 : -1);

            logo.Color = NextColor(logo, settings);
            _logos.Add(logo);
        }

        // Create windows for each screen
        CreateWindows(settings.DualMonitorEnabled, settings.BouncingTextOpacity, logoCount, settings.BouncingTextOutline);

        // Drive motion off the composition clock (vsync-aligned, one callback per
        // rendered frame) instead of a DispatcherTimer — see _lastRenderTime note.
        _lastRenderTime = TimeSpan.MinValue;
        _zReassertAccum = 0;
        CompositionTarget.Rendering -= Animate; // guard against a double subscribe
        CompositionTarget.Rendering += Animate;

        // During mandatory video the old code kept Animate subscribed and hid the windows + returned
        // every vsync — a permanent no-op render callback that never let the composition loop sleep
        // (a contributor to the idle/long-session freeze, #453). Instead, pause the loop entirely on
        // VideoStarted and resume on VideoEnded. When "Show Over Videos" is on we deliberately keep
        // animating on top of the video instead.
        if (App.Video != null)
        {
            App.Video.VideoStarted -= OnVideoStartedPause;
            App.Video.VideoStarted += OnVideoStartedPause;
            App.Video.VideoEnded -= OnVideoEndedResume;
            App.Video.VideoEnded += OnVideoEndedResume;
            if (App.Video.IsPlaying) OnVideoStartedPause(null, EventArgs.Empty);
        }

        App.Logger?.Information("BouncingTextService started - Logos: {Count}, Text: {Text}, ColorMode: {Mode}",
            _logos.Count, _logos[0].Text, settings.BouncingTextColorMode);
    }

    public void Stop()
    {
        _isRunning = false;

        CompositionTarget.Rendering -= Animate;
        if (App.Video != null)
        {
            App.Video.VideoStarted -= OnVideoStartedPause;
            App.Video.VideoEnded -= OnVideoEndedResume;
        }

        // Always close and clear windows, even if we thought we weren't running
        foreach (var window in _windows)
        {
            try { window.Close(); } catch { }
        }
        _windows.Clear();
        _logos.Clear();
        _poolOverride = null;

        App.Logger?.Information("BouncingTextService stopped");
    }

    /// <summary>Restart with current settings — used when a structural setting
    /// (second text, outlined style) changes while running.</summary>
    public void Restart()
    {
        if (!_isRunning) return;
        var pool = _poolOverride;
        Stop();
        Start(true, pool);
    }

    /// <summary>Sleep the bounce render loop while a mandatory video plays (VideoStarted). Marshals to
    /// the UI thread since the video event can fire off a playback thread. No-op when the user asked
    /// for the text to stay visible over videos (BouncingTextAlwaysOnTop).</summary>
    private void OnVideoStartedPause(object? sender, EventArgs e)
    {
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp == null) return;
        if (!disp.CheckAccess()) { try { disp.BeginInvoke(new Action(() => OnVideoStartedPause(sender, e))); } catch { } return; }
        if (App.Settings?.Current?.BouncingTextAlwaysOnTop == true) return;
        CompositionTarget.Rendering -= Animate;
        foreach (var w in _windows) { try { if (w.IsLoaded) w.Hide(); } catch { } }
    }

    /// <summary>Resume the bounce render loop when the video ends (VideoEnded), if still running.</summary>
    private void OnVideoEndedResume(object? sender, EventArgs e)
    {
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp == null) return;
        if (!disp.CheckAccess()) { try { disp.BeginInvoke(new Action(() => OnVideoEndedResume(sender, e))); } catch { } return; }
        if (!_isRunning) return;
        foreach (var w in _windows) { try { if (w.IsLoaded && !w.IsVisible) w.Show(); } catch { } }
        _lastRenderTime = TimeSpan.MinValue; // reset dt so the first resumed frame doesn't jump
        CompositionTarget.Rendering -= Animate;
        CompositionTarget.Rendering += Animate;
    }

    private string SelectRandomText()
    {
        var settings = App.Settings.Current;
        var enabledTexts = _poolOverride != null && _poolOverride.Count > 0
            ? _poolOverride
            : settings.BouncingTextPool
                .Where(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();

        if (enabledTexts.Count == 0)
            return "GOOD GIRL";
        return enabledTexts[_random.Next(enabledTexts.Count)];
    }

    /// <summary>
    /// Measure the actual rendered size of the logo's text
    /// </summary>
    private void MeasureTextSize(Logo logo)
    {
        try
        {
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            // MainWindow can be null during startup/shutdown; GetDpi(null) throws an
            // NRE that the catch below swallows into a noisy [WRN] (#305). Resolve a
            // DPI source that may be null and fall back to 1.0 PixelsPerDip.
            var dpiSource = Application.Current?.MainWindow
                            ?? (Visual?)_windows.FirstOrDefault();
            double pixelsPerDip = dpiSource != null
                ? VisualTreeHelper.GetDpi(dpiSource).PixelsPerDip
                : 1.0;
            var formattedText = new FormattedText(
                logo.Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                _currentFontSize,
                Brushes.White,
                new NumberSubstitution(),
                pixelsPerDip);

            logo.TextWidth = formattedText.Width;
            logo.TextHeight = formattedText.Height;

            App.Logger?.Debug("Measured text '{Text}': {W}x{H}", logo.Text, logo.TextWidth, logo.TextHeight);
        }
        catch (Exception ex)
        {
            // Fallback to estimation if measurement fails
            logo.TextWidth = _currentFontSize * logo.Text.Length * 0.6;
            logo.TextHeight = _currentFontSize * 1.2;
            App.Logger?.Warning(ex, "Failed to measure text, using estimate: {W}x{H}", logo.TextWidth, logo.TextHeight);
        }
    }

    private void CalculateScreenBounds(bool dualMonitor)
    {
        var screens = dualMonitor
            ? App.GetAllScreensCached()
            : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

        // Get DPI scale
        var dpiScale = GetDpiScale();

        // Find total bounds across all screens
        _minX = screens.Min(s => s.Bounds.X) / dpiScale;
        _minY = screens.Min(s => s.Bounds.Y) / dpiScale;
        _maxX = screens.Max(s => s.Bounds.X + s.Bounds.Width) / dpiScale;
        _maxY = screens.Max(s => s.Bounds.Y + s.Bounds.Height) / dpiScale;
    }

    private void CreateWindows(bool dualMonitor, int opacity, int logoCount, bool outline)
    {
        var screens = dualMonitor
            ? App.GetAllScreensCached()
            : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

        foreach (var screen in screens)
        {
            var window = new BouncingTextWindow(screen, _currentFontSize, opacity, logoCount, outline);
            window.Show();
            _windows.Add(window);
        }

        // Update text in all windows
        for (int i = 0; i < _logos.Count; i++)
            UpdateWindowsText(i);
    }

    private void Animate(object? sender, EventArgs e)
    {
        if (!_isRunning) return;

        // Delta time from the composition clock. Establish a baseline on the first
        // frame, ignore duplicate callbacks, and clamp after a stall so the text
        // never teleports across the screen.
        double dt = 1.0 / 60.0;
        if (e is RenderingEventArgs r)
        {
            if (_lastRenderTime == TimeSpan.MinValue)
            {
                _lastRenderTime = r.RenderingTime;
                return;
            }
            dt = (r.RenderingTime - _lastRenderTime).TotalSeconds;
            _lastRenderTime = r.RenderingTime;
            if (dt <= 0) return;
            if (dt > 0.1) dt = 0.1;
        }

        var settings = App.Settings.Current;
        bool overVideos = settings.BouncingTextAlwaysOnTop;

        // Hide bouncing text while a mandatory video is playing (unless Show Over Videos is on)
        if (App.Video?.IsPlaying == true && !overVideos)
        {
            foreach (var w in _windows) { if (w.IsLoaded) w.Hide(); }
            return;
        }
        else
        {
            foreach (var w in _windows) { if (w.IsLoaded && !w.IsVisible) w.Show(); }
        }

        _fxTime += dt;
        _lastDt = dt;

        for (int i = 0; i < _logos.Count; i++)
            StepLogo(_logos[i], i, dt, settings);

        // Re-assert z-order every ~500ms — bouncing text is long-lived and will
        // lose topmost when competing with flash/video/overlay windows
        _zReassertAccum += dt;
        if (_zReassertAccum >= 0.5)
        {
            _zReassertAccum = 0;
            foreach (var window in _windows)
                window.ReassertTopmost();
        }

        // Update position + effect transforms in all windows
        for (int i = 0; i < _logos.Count; i++)
        {
            var l = _logos[i];
            var (sx, sy, angle) = ComputeEffectTransform(l, settings);
            foreach (var window in _windows)
            {
                window.UpdatePosition(i, l.PosX, l.PosY);
                window.UpdateTransform(i, sx, sy, angle);
            }
        }
    }

    /// <summary>Advance one logo's motion, handle wall/corner bounces and the bounce payload.</summary>
    private void StepLogo(Logo l, int index, double dt, Models.AppSettings settings)
    {
        // Move (delta-time based; velocities are DIP/second)
        l.PosX += l.VelX * dt;
        l.PosY += l.VelY * dt;

        bool bouncedX = false;
        bool bouncedY = false;

        // Calculate the RIGHT and BOTTOM edges of the text
        double textRight = l.PosX + l.TextWidth;
        double textBottom = l.PosY + l.TextHeight;

        // Bounce off LEFT edge (text's left edge hits screen's left edge)
        if (l.PosX <= _minX)
        {
            l.PosX = _minX;
            l.VelX = Math.Abs(l.VelX);
            bouncedX = true;
        }
        // Bounce off RIGHT edge (text's right edge hits screen's right edge)
        else if (textRight >= _maxX)
        {
            l.PosX = _maxX - l.TextWidth;
            l.VelX = -Math.Abs(l.VelX);
            bouncedX = true;
        }

        // Bounce off TOP edge (text's top edge hits screen's top edge)
        if (l.PosY <= _minY)
        {
            l.PosY = _minY;
            l.VelY = Math.Abs(l.VelY);
            bouncedY = true;
        }
        // Bounce off BOTTOM edge (text's bottom edge hits screen's bottom edge)
        else if (textBottom >= _maxY)
        {
            l.PosY = _maxY - l.TextHeight;
            l.VelY = -Math.Abs(l.VelY);
            bouncedY = true;
        }

        bool bounced = bouncedX || bouncedY;
        bool cornerHit = false;

        // Check for corner hit (both X and Y bounce at the same time!)
        if (bouncedX && bouncedY)
        {
            cornerHit = true;
        }
        // Also check for "near corner" hits - when very close to a corner during a single-axis bounce
        else if (bounced)
        {
            cornerHit = IsNearCorner(l.PosX, l.PosY, textRight, textBottom);
        }

        if (cornerHit)
        {
            App.Logger?.Information("🎯 CORNER HIT! Position: ({X}, {Y})", l.PosX, l.PosY);
            App.Achievements?.TrackCornerHit();
            OnCornerHit?.Invoke(this, EventArgs.Empty);

            if (settings.BouncingTextFxCornerBurst)
            {
                l.BurstTimer = 0;
                double cx = l.PosX + l.TextWidth / 2;
                double cy = l.PosY + l.TextHeight / 2;
                foreach (var window in _windows)
                    window.SpawnCornerBurst(cx, cy, l.Color);
            }
        }

        // On bounce: change color, award XP, maybe change text
        if (bounced)
        {
            l.Color = NextColor(l, settings);
            if (settings.BouncingTextFxSquashStretch)
            {
                l.SquashTimer = 0;
                l.SquashAxisX = bouncedX;
            }

            var now = DateTime.UtcNow;

            // Reset per-minute counter if a new minute has started
            if ((now - _bounceXpMinuteStart).TotalSeconds >= 60)
            {
                _bounceXpThisMinute = 0;
                _bounceXpMinuteStart = now;
            }

            if (now - _lastBounceXpTime >= BounceXpCooldown && _bounceXpThisMinute < MaxBounceXpPerMinute)
            {
                App.Progression?.AddXP(15, XPSource.BouncingText);
                _lastBounceXpTime = now;
                _bounceXpThisMinute += 15;
            }
            OnBounce?.Invoke(this, EventArgs.Empty);

            // Haptic pulse on bounce
            _ = App.Haptics?.BouncingTextBounceAsync();

            // 10% chance to change text on bounce
            if (_random.NextDouble() < 0.1)
            {
                l.Text = SelectRandomText();
                MeasureTextSize(l); // Re-measure when text changes
            }

            UpdateWindowsText(index);
        }
    }

    /// <summary>Combine the enabled per-frame effects into one scale/rotation set for a logo.</summary>
    private (double sx, double sy, double angle) ComputeEffectTransform(Logo l, Models.AppSettings settings)
    {
        double sx = 1.0, sy = 1.0, angle = 0.0;

        // Breathing: slow hypnotic scale pulse
        if (settings.BouncingTextFxBreathing)
        {
            double breath = 1.0 + 0.08 * Math.Sin(_fxTime * (Math.PI * 2 / 3.2) + l.Phase);
            sx *= breath;
            sy *= breath;
        }

        // Wobble: gentle autoreversing tilt
        if (settings.BouncingTextFxWobble)
            angle += 6.0 * Math.Sin(_fxTime * (Math.PI * 2 / 2.3) + l.Phase);

        // Continuous spin
        if (settings.BouncingTextFxSpin)
        {
            l.SpinAngle = (l.SpinAngle + 40.0 * _lastDt) % 360.0;
            angle += l.SpinAngle;
        }

        // Velocity tilt: lean into the direction of travel (mirrored so the text
        // never flips upside down when moving left)
        if (settings.BouncingTextFxVelocityTilt)
        {
            double target = Math.Atan2(l.VelY * Math.Sign(l.VelX == 0 ? 1 : l.VelX), Math.Abs(l.VelX))
                            * (180.0 / Math.PI) * 0.25;
            target = Math.Clamp(target, -12.0, 12.0);
            l.Tilt += (target - l.Tilt) * Math.Min(1.0, _lastDt * 6.0);
            angle += l.Tilt;
        }
        else
        {
            l.Tilt = 0;
        }

        // Squash & stretch: damped compress-then-overshoot on the axis that hit the wall
        if (l.SquashTimer >= 0)
        {
            l.SquashTimer += _lastDt;
            if (l.SquashTimer > 0.45)
            {
                l.SquashTimer = -1;
            }
            else
            {
                double t = l.SquashTimer;
                double deform = 0.35 * Math.Exp(-6.0 * t) * Math.Cos(24.0 * t);
                if (l.SquashAxisX) { sx *= 1.0 - deform; sy *= 1.0 + deform * 0.7; }
                else               { sy *= 1.0 - deform; sx *= 1.0 + deform * 0.7; }
            }
        }

        // Corner burst: quick celebratory pop on top of everything else
        if (l.BurstTimer >= 0)
        {
            l.BurstTimer += _lastDt;
            if (l.BurstTimer > 0.5)
            {
                l.BurstTimer = -1;
            }
            else
            {
                double pop = 1.0 + 0.4 * Math.Exp(-l.BurstTimer / 0.15);
                sx *= pop;
                sy *= pop;
            }
        }

        return (sx, sy, angle);
    }

    // The dt of the current Animate frame, stashed so effect timers don't need it
    // threaded through every call.
    private double _lastDt;

    /// <summary>
    /// Check if the text is near any corner within tolerance
    /// </summary>
    private bool IsNearCorner(double left, double top, double right, double bottom)
    {
        // Top-left corner
        bool nearTopLeft = left <= _minX + CORNER_TOLERANCE && top <= _minY + CORNER_TOLERANCE;
        // Top-right corner
        bool nearTopRight = right >= _maxX - CORNER_TOLERANCE && top <= _minY + CORNER_TOLERANCE;
        // Bottom-left corner
        bool nearBottomLeft = left <= _minX + CORNER_TOLERANCE && bottom >= _maxY - CORNER_TOLERANCE;
        // Bottom-right corner
        bool nearBottomRight = right >= _maxX - CORNER_TOLERANCE && bottom >= _maxY - CORNER_TOLERANCE;

        return nearTopLeft || nearTopRight || nearBottomLeft || nearBottomRight;
    }

    private void UpdateWindowsText(int index)
    {
        var l = _logos[index];
        foreach (var window in _windows)
        {
            window.UpdateText(index, l.Text, l.Color);
        }
    }

    /// <summary>Pick the next color for a logo according to the color-mode setting.</summary>
    private Color NextColor(Logo logo, Models.AppSettings settings)
    {
        switch (settings.BouncingTextColorMode)
        {
            case 1: // Fixed: the user's chosen color, no re-roll
                return GetFixedColor(settings);
            case 2: // Rainbow cycle: walk the ordered hue wheel one step per bounce
                logo.HueIndex = (logo.HueIndex + 1) % RainbowWheel.Length;
                return RainbowWheel[logo.HueIndex];
            default:
                return GetRandomColor();
        }
    }

    private static Color GetFixedColor(Models.AppSettings settings)
    {
        var hex = settings.BouncingTextFixedColor;
        if (!string.IsNullOrWhiteSpace(hex))
        {
            hex = hex.Trim().TrimStart('#');
            if (hex.Length == 6)
            {
                try
                {
                    return Color.FromRgb(
                        Convert.ToByte(hex.Substring(0, 2), 16),
                        Convert.ToByte(hex.Substring(2, 2), 16),
                        Convert.ToByte(hex.Substring(4, 2), 16));
                }
                catch { }
            }
        }
        return Color.FromRgb(255, 105, 180); // hot pink fallback
    }

    private Color GetRandomColor()
    {
        // Bright, vibrant colors
        var colors = new[]
        {
            Color.FromRgb(255, 105, 180), // Hot Pink
            Color.FromRgb(255, 20, 147),  // Deep Pink
            Color.FromRgb(138, 43, 226),  // Blue Violet
            Color.FromRgb(255, 0, 255),   // Magenta
            Color.FromRgb(0, 255, 255),   // Cyan
            Color.FromRgb(255, 255, 0),   // Yellow
            Color.FromRgb(0, 255, 0),     // Lime
            Color.FromRgb(255, 165, 0),   // Orange
            Color.FromRgb(255, 69, 0),    // Red Orange
            Color.FromRgb(50, 205, 50),   // Lime Green
        };
        return colors[_random.Next(colors.Length)];
    }

    /// <summary>Ordered 12-step hue wheel (full saturation/value) for the rainbow-cycle mode.</summary>
    private static Color[] BuildRainbowWheel()
    {
        var wheel = new Color[12];
        for (int i = 0; i < wheel.Length; i++)
        {
            double h = i * 360.0 / wheel.Length;
            wheel[i] = HsvToRgb(h, 1.0, 1.0);
        }
        return wheel;
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = ((int)(h / 60.0) % 6) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private double GetDpiScale()
    {
        try
        {
            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>
    /// Refresh when settings change (speed, size, opacity, fixed color, show-over-videos)
    /// </summary>
    public void Refresh()
    {
        if (!_isRunning) return;

        var settings = App.Settings.Current;

        // Update speed
        var speed = settings.BouncingTextSpeed / 10.0;
        foreach (var l in _logos)
        {
            var currentSpeed = Math.Sqrt(l.VelX * l.VelX + l.VelY * l.VelY);
            var targetSpeed = (3.0 + _random.NextDouble() * 2.0) * 60.0 * speed; // DIP/sec
            var scale = targetSpeed / Math.Max(0.1, currentSpeed);
            l.VelX *= scale;
            l.VelY *= scale;
        }

        // Check if font size changed - if so, update and re-measure
        var newFontSize = (int)(BASE_FONT_SIZE * settings.BouncingTextSize / 100.0);
        if (newFontSize != _currentFontSize)
        {
            _currentFontSize = newFontSize;
            foreach (var l in _logos)
                MeasureTextSize(l); // Re-measure with new font size

            // Update font size in all windows
            foreach (var window in _windows)
            {
                window.UpdateFontSize(_currentFontSize);
            }
        }

        // Live opacity
        foreach (var window in _windows)
            window.UpdateOpacity(settings.BouncingTextOpacity);

        // Switching to Fixed color applies immediately (Random/Rainbow apply on next bounce)
        if (settings.BouncingTextColorMode == 1)
        {
            var fixedColor = GetFixedColor(settings);
            for (int i = 0; i < _logos.Count; i++)
            {
                _logos[i].Color = fixedColor;
                UpdateWindowsText(i);
            }
        }

        // Show-over-videos toggled mid-video: resume or pause the loop accordingly
        if (App.Video?.IsPlaying == true)
        {
            if (settings.BouncingTextAlwaysOnTop)
                OnVideoEndedResume(null, EventArgs.Empty);
            else
                OnVideoStartedPause(null, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

/// <summary>
/// Transparent window that displays the bouncing text (one visual per logo)
/// </summary>
internal class BouncingTextWindow : Window
{
    /// <summary>One rendered logo: either a TextBlock (drop-shadow style) or an
    /// OutlinedText (crisp stroke style), plus its per-frame effect transforms.</summary>
    private sealed class LogoVisual
    {
        public FrameworkElement Element = null!;
        public TextBlock? Tb;
        public OutlinedText? Ot;
        public ScaleTransform Scale = null!;
        public RotateTransform Rotate = null!;
    }

    private readonly List<LogoVisual> _visuals = new();
    private readonly Canvas _canvas;
    private readonly System.Windows.Forms.Screen _screen;
    private readonly double _dpiScale;
    private readonly bool _outline;
    private IntPtr _hwnd;

    public BouncingTextWindow(System.Windows.Forms.Screen screen, int fontSize = 48, int opacity = 100, int logoCount = 1, bool outline = false)
    {
        _screen = screen;
        _dpiScale = GetDpiScale();
        _outline = outline;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;

        // Cover the entire screen
        Left = screen.Bounds.X / _dpiScale;
        Top = screen.Bounds.Y / _dpiScale;
        Width = screen.Bounds.Width / _dpiScale;
        Height = screen.Bounds.Height / _dpiScale;

        // Canvas for positioning
        _canvas = new Canvas();
        Content = _canvas;

        for (int i = 0; i < logoCount; i++)
            _visuals.Add(CreateLogoVisual(fontSize, opacity));

        // Make click-through and force Win32 TOPMOST (more reliable than WPF Topmost property)
        SourceInitialized += (s, e) =>
        {
            MakeClickThrough();
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        };
    }

    private LogoVisual CreateLogoVisual(int fontSize, int opacity)
    {
        var v = new LogoVisual
        {
            Scale = new ScaleTransform(1, 1),
            Rotate = new RotateTransform(0),
        };
        var transform = new TransformGroup();
        transform.Children.Add(v.Scale);
        transform.Children.Add(v.Rotate);

        if (_outline)
        {
            v.Ot = new OutlinedText
            {
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Fill = Brushes.HotPink,
                Stroke = Brushes.Black,
                StrokeThickness = Math.Max(2.0, fontSize / 22.0),
                Opacity = opacity / 100.0,
                RenderTransform = transform,
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = false,
            };
            v.Element = v.Ot;
        }
        else
        {
            v.Tb = new TextBlock
            {
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.HotPink,
                Opacity = opacity / 100.0,
                RenderTransform = transform,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 10,
                    ShadowDepth = 3
                }
            };
            v.Element = v.Tb;
        }

        _canvas.Children.Add(v.Element);
        return v;
    }

    public void UpdateText(int index, string text, Color color)
    {
        if (index < 0 || index >= _visuals.Count) return;
        var v = _visuals[index];
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        if (v.Tb != null)
        {
            v.Tb.Text = text;
            v.Tb.Foreground = brush;
        }
        else if (v.Ot != null)
        {
            v.Ot.Text = text;
            v.Ot.Fill = brush;
            v.Ot.Build();
        }
    }

    public void UpdateFontSize(int fontSize)
    {
        foreach (var v in _visuals)
        {
            if (v.Tb != null)
            {
                v.Tb.FontSize = fontSize;
            }
            else if (v.Ot != null)
            {
                v.Ot.FontSize = fontSize;
                v.Ot.StrokeThickness = Math.Max(2.0, fontSize / 22.0);
                v.Ot.Build();
            }
        }
    }

    public void UpdateOpacity(int opacity)
    {
        foreach (var v in _visuals)
            v.Element.Opacity = opacity / 100.0;
    }

    public void UpdatePosition(int index, double x, double y)
    {
        if (index < 0 || index >= _visuals.Count) return;
        var v = _visuals[index];

        // Convert global position to local screen position
        var localX = x - (_screen.Bounds.X / _dpiScale);
        var localY = y - (_screen.Bounds.Y / _dpiScale);

        // OutlinedText draws its glyphs inset by (StrokeThickness + 6) padding, so
        // shift the element so the glyphs land on the same spot the bounce math uses.
        if (v.Ot != null)
        {
            double pad = v.Ot.StrokeThickness + 6;
            localX -= pad;
            localY -= pad;
        }

        // Just position the text and let WPF clip it to the window naturally. The
        // previous "is any part visible on this screen?" check used Width/Height
        // (this window's bounds, computed from the desktop DPI scale) as the
        // boundary, which goes wrong on mixed-DPI multi-monitor setups: the text
        // would appear to "hide and come back" inside a region of the screen
        // because the visibility math thought we were off-screen when we weren't.
        // (Bug #188.) The bouncing math in BouncingTextService keeps positions
        // inside the virtual desktop bounds anyway, so any window that covers part
        // of where the text is will render it; windows that don't cover that
        // region just render the text off-canvas and WPF clips it. No visibility
        // toggle needed.
        Canvas.SetLeft(v.Element, localX);
        Canvas.SetTop(v.Element, localY);
        v.Element.Visibility = Visibility.Visible;
    }

    /// <summary>Apply the combined per-frame effect transform (mutates the existing
    /// transforms; setting an unchanged DP value is a no-op, so identity frames cost nothing).</summary>
    public void UpdateTransform(int index, double scaleX, double scaleY, double angle)
    {
        if (index < 0 || index >= _visuals.Count) return;
        var v = _visuals[index];
        v.Scale.ScaleX = scaleX;
        v.Scale.ScaleY = scaleY;
        v.Rotate.Angle = angle;
    }

    /// <summary>Expanding, fading ring spawned at a corner hit (virtual-desktop DIP center).</summary>
    public void SpawnCornerBurst(double x, double y, Color color)
    {
        try
        {
            const double SIZE = 240;
            var localX = x - (_screen.Bounds.X / _dpiScale);
            var localY = y - (_screen.Bounds.Y / _dpiScale);

            var strokeBrush = new SolidColorBrush(color);
            strokeBrush.Freeze();
            var scale = new ScaleTransform(0.1, 0.1);
            var ring = new System.Windows.Shapes.Ellipse
            {
                Width = SIZE,
                Height = SIZE,
                Stroke = strokeBrush,
                StrokeThickness = 6,
                Opacity = 0.9,
                IsHitTestVisible = false,
                RenderTransform = scale,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            Canvas.SetLeft(ring, localX - SIZE / 2);
            Canvas.SetTop(ring, localY - SIZE / 2);
            _canvas.Children.Add(ring);

            var dur = TimeSpan.FromMilliseconds(550);
            var grow = new System.Windows.Media.Animation.DoubleAnimation(0.1, 1.3, dur)
            {
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0.9, 0.0, dur);
            fade.Completed += (s, e) => { try { _canvas.Children.Remove(ring); } catch { } };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            ring.BeginAnimation(OpacityProperty, fade);
        }
        catch { /* purely cosmetic - never let a burst take down the bounce loop */ }
    }

    private void MakeClickThrough()
    {
        _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        // WS_EX_TRANSPARENT: clicks pass through
        // WS_EX_TOOLWINDOW: not shown in alt-tab
        // WS_EX_NOACTIVATE: never steals keyboard/mouse focus
        SetWindowLong(_hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    public void ReassertTopmost()
    {
        if (_hwnd != IntPtr.Zero)
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private double GetDpiScale()
    {
        try
        {
            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    #region Win32

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    #endregion
}
