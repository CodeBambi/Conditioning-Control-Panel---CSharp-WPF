using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The juice half of one flash window: glow + corners (WPF SpawnFlashWindow glow block),
    /// a deadline gaze-linger can push (WPF FlashWindow.BoostLifetime), the press (tap = pop,
    /// FlashDraggable = drag) and the exit style a pop plays (WPF FlashExit, drawn per frame
    /// on the content's RenderTransform; never Animation.RunAsync on a Transform).
    /// </summary>
    internal sealed partial class FlashOverlayWindow
    {
        internal FlashFxState? Fx { get; set; }
        internal DateTime ExpiresAt { get; set; } = DateTime.MaxValue;
        internal bool IsLeaving => _popped || _expiring;
        internal FlashExitState? Exit { get; private set; }

        private bool _expiring;
        private double _fadeAlpha;
        private TimeSpan _fadeSpan;
        private DispatcherTimer? _expiryTimer, _pulseTimer;
        private Border? _glowCard;
        private FlashGlowLook _glow;

        /// <summary>Wrap the picture: a rounded clip, and for a glow flash a card with a coloured
        /// BoxShadow inside a pad of blur/2 (the window grows by the same pad). The lucky pulse is
        /// a 30 fps timer that dies with the window; reduced/off motion keeps the glow still.</summary>
        internal void ApplyLook(FlashGlowLook glow, double cornerDip, double padDip, bool animate)
        {
            _glow = glow;
            Content = null;   // free the picture before it moves into the card
            Control content = _image;
            if (cornerDip > 0)
                content = new Border { CornerRadius = new CornerRadius(cornerDip), ClipToBounds = true, Child = _image };
            if (glow.HasGlow)
            {
                _glowCard = new Border { CornerRadius = new CornerRadius(cornerDip), Child = content };
                SetGlow(glow.BlurRadius, glow.Opacity);
                content = new Border { Padding = new Thickness(padDip), Child = _glowCard };
                if (glow.LuckyPulse && animate)
                {
                    var t0 = DateTime.Now;
                    _pulseTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) =>
                    {
                        var (b, o) = FlashFxRules.LuckyPulseAt(glow.BlurRadius, (DateTime.Now - t0).TotalSeconds);
                        SetGlow(b, o);
                    });
                    Opened += (_, _) => _pulseTimer?.Start();
                }
            }
            Content = content;
            Closed += (_, _) => { _pulseTimer?.Stop(); _pulseTimer = null; _expiryTimer?.Stop(); };
        }

        private void SetGlow(double blur, double opacity)
        {
            if (_glowCard == null) return;
            var c = Color.FromArgb((byte)Math.Clamp(opacity * 255, 0, 255),
                (byte)(_glow.Rgb >> 16), (byte)(_glow.Rgb >> 8), (byte)_glow.Rgb);
            _glowCard.BoxShadow = new BoxShadows(new BoxShadow { Blur = blur, Color = c });
        }

        /// <summary>The flash's death deadline: fade out at <see cref="ExpiresAt"/>.</summary>
        private void ScheduleExpiry(double alpha, TimeSpan fade, TimeSpan lifetime)
        {
            _fadeAlpha = alpha;
            _fadeSpan = fade;
            ExpiresAt = DateTime.Now + lifetime;
            ArmExpiry(lifetime);
        }

        private void ArmExpiry(TimeSpan due)
        {
            _expiryTimer?.Stop();
            var t = new DispatcherTimer { Interval = due < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : due };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (_popped || _closed || _expiring) return;
                _expiring = true;
                Fade(_fadeAlpha, 0, _fadeSpan, Close);
            };
            _expiryTimer = t;
            t.Start();
        }

        /// <summary>WPF FlashWindow.BoostLifetime: alive <paramref name="extraMs"/> more from now,
        /// only ever lengthening, never reviving a flash already on its way out (#384).</summary>
        internal void BoostLifetime(int extraMs)
        {
            if (extraMs <= 0 || IsLeaving || _closed) return;
            var until = DateTime.Now.AddMilliseconds(extraMs);
            if (until <= ExpiresAt) return;
            ExpiresAt = until;
            ArmExpiry(TimeSpan.FromMilliseconds(extraMs));
        }

        // ---- Motion: the state the shared drift tick steps, and the pendulum rig ----

        /// <summary>The motion this window plays (drift, pendulum, or a drag's own); null = still.</summary>
        internal FlashMotionState? Motion { get; set; }

        private Control? _rigCard;
        private double _rigSidePx;
        internal double RigAngleRad { get; private set; }
        internal bool IsRig => _rigCard != null;

        /// <summary>
        /// WPF FlashLayer's pendulum draw on a window: the picture card (<paramref name="cardW"/> x
        /// <paramref name="cardH"/> DIP, glow pad included) sits centred in a square window the
        /// size of its diagonal, so it can turn to any angle without being cut, and turns about its
        /// own centre. The window then only MOVES with the swing; it never resizes.
        /// Returns the window side in physical px.
        /// </summary>
        internal int MakeRig(double cardW, double cardH, double scaling)
        {
            var card = Content as Control ?? _image;
            Content = null;
            card.Width = cardW;
            card.Height = cardH;
            card.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
            card.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
            card.RenderTransformOrigin = RelativePoint.TopLeft;
            _rigCard = card;
            var k = scaling > 0 ? scaling : 1.0;
            var side = (int)Math.Ceiling(Math.Sqrt(cardW * cardW + cardH * cardH) * k) + 2;
            _rigSidePx = side;
            Content = new Panel { Children = { card } };
            // A clickable rig takes the click on the picture, not on the empty corners of the square.
            if (IsHitTestVisible) MoveHitSurfaceToCard();
            return side;
        }

        private void MoveHitSurfaceToCard()
        {
            Background = Brushes.Transparent;
            if (_rigCard is Panel p) p.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            else if (_rigCard is Border b) b.Background ??= new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        }

        /// <summary>Turn the rig's card to <paramref name="angleRad"/> (clockwise, SKCanvas sense).</summary>
        internal void SetRigAngle(double angleRad)
        {
            RigAngleRad = angleRad;
            if (_rigCard == null || Exit != null) return;
            _rigCard.RenderTransform = new MatrixTransform(RotateAbout(angleRad, _rigCard.Width / 2, _rigCard.Height / 2));
        }

        private static Matrix RotateAbout(double rad, double cx, double cy) =>
            Matrix.CreateTranslation(-cx, -cy) * Matrix.CreateRotation(rad) * Matrix.CreateTranslation(cx, cy);

        /// <summary>
        /// Put the window where <paramref name="m"/> says (physical px) and return its new rect. A
        /// rig sits centred on the media centre (for a pendulum the AABB centre IS it) and turns to
        /// the swing angle; a drag or a drift moves the rect as is.
        /// </summary>
        internal PixelRect ApplyMotion(FlashMotionState m, PixelSize size)
        {
            if (_rigCard != null)
            {
                SetRigAngle(m.Style == FlashMotionStyle.Pendulum ? m.AngleRad : 0);
                var side = (int)_rigSidePx;
                var p = new PixelPoint((int)Math.Round(m.X + m.W / 2 - side / 2.0), (int)Math.Round(m.Y + m.H / 2 - side / 2.0));
                Position = p;
                return new PixelRect(p, new PixelSize(side, side));
            }
            var at = new PixelPoint((int)Math.Round(m.X), (int)Math.Round(m.Y));
            Position = at;
            return new PixelRect(at, size);
        }

        // ---- Press: tap pops; FlashDraggable lets a hand move it first (WPF FlashDrag) ----

        private static long NowMs() => Environment.TickCount64;

        private void OnFlashPressed(PointerPressedEventArgs e)
        {
            if (!CoreSettings.Current.FlashDraggable || IsLeaving) { Pop(); return; }
            // The drag owns the motion state from here (FlashDrag.Begin turns a pendulum into a
            // still rect and the shared tick moves it), so a let-go flash STAYS where it was put
            // or flies on - the next drift tick reads the same state and never snaps it back.
            var m = FlashOverlay.EnsureMotion(this);
            var p = this.PointToScreen(e.GetPosition(this));
            FlashDrag.Begin(m, p.X, p.Y, NowMs());
            e.Pointer.Capture(this);
            PointerMoved -= OnDragMove;
            PointerReleased -= OnDragUp;
            PointerMoved += OnDragMove;
            PointerReleased += OnDragUp;
        }

        private void OnDragMove(object? sender, PointerEventArgs e)
        {
            if (Motion?.Drag is not { } d || IsLeaving) return;
            var p = this.PointToScreen(e.GetPosition(this));
            FlashDrag.Sample(d, p.X, p.Y, NowMs());
        }

        private void OnDragUp(object? sender, PointerReleasedEventArgs e)
        {
            e.Pointer.Capture(null);
            PointerMoved -= OnDragMove;
            PointerReleased -= OnDragUp;
            var m = Motion;
            if (m?.Drag is not { } d) return;
            var p = this.PointToScreen(e.GetPosition(this));
            var now = NowMs();
            FlashDrag.Sample(d, p.X, p.Y, now);
            // WPF ApplyWorkAreaBounds: a drag can cross screens, so the walls come from the work
            // area of the monitor it was let go over (a flung flash never hides behind the taskbar).
            if (Screens?.ScreenFromPoint(p) is { } under)
            {
                var wa = under.WorkingArea;
                m.BoundsX = wa.X; m.BoundsY = wa.Y; m.BoundsW = wa.Width; m.BoundsH = wa.Height;
            }
            var outcome = FlashDrag.Release(m, now, CoreSettings.Current.MotionLevel);
            // WPF EndLayerDrag: a tap pops exactly as a click; a place or a fling only moves (no hydra).
            if (outcome == FlashDragOutcome.Tap && !IsLeaving) Pop();
            else if (outcome == FlashDragOutcome.Fling) RaiseFlung();   // Natasha's red flash flung away in time: dodged
        }

        // ---- Exit ----

        /// <summary>The control a leave animation moves: the rig's card, or the whole content.</summary>
        private Control? ExitTarget => _rigCard ?? Content as Control;

        /// <summary>WPF SafeCloseFlashWindow on a dismiss: an owned Shatter breaks the picture into
        /// a monitor-wide shard window and this one goes at once; else the exit style (Pop throws
        /// its sparks into a window of their own), or the old quick fade for None.</summary>
        private void PlayExit()
        {
            _expiryTimer?.Stop();
            // Hand-off, not a cut: this window stays up until the shard window has painted, then
            // goes (owner, 2026-10-09: closing it first left a blank frame before the break).
            if (FlashOverlay.TryShatter(this, () =>
                {
                    if (_closed) return;
                    X11Overlay.SetOpacity(this, 0);
                    Close();
                }))
                return;
            var exit = FlashOverlay.PickExit(CoreSettings.Current, new Random());
            if (exit == null || ExitTarget is not { } content)
            {
                Fade(_alpha, 0, TimeSpan.FromMilliseconds(180), Close);
                return;
            }
            Exit = exit;
            FlashOverlay.ThrowSparks(this, exit);
            content.RenderTransformOrigin = RelativePoint.TopLeft;
            var size = _rigCard != null ? new Size(_rigCard.Width, _rigCard.Height) : Bounds.Size;
            var angle = RigAngleRad;
            // Frame-locked (one step per composed frame), never a 16 ms DispatcherTimer: on Windows
            // that fires on the 15.6 ms system tick, 15.6 or 31.2 ms apart, and the exit stutters.
            TimeSpan? last = null;
            var frames = global::ConditioningControlPanel.Avalonia.Controls.Fx.TopLevelFrameSource.For(this);
            Action<TimeSpan>? onFrame = null;
            onFrame = now =>
            {
                if (_closed) { frames.Unsubscribe(onFrame!); return; }
                var dt = last is { } l ? Math.Clamp((now - l).TotalSeconds, 0, 0.1) : 0;
                last = now;
                if (dt > 0) FlashExit.Step(exit, dt);
                ApplyExitFrame(content, exit, size, angle);
                if (exit.Done) { frames.Unsubscribe(onFrame!); Close(); }
            };
            Closed += (_, _) => frames.Unsubscribe(onFrame);
            ApplyExitFrame(content, exit, size, angle);
            frames.Subscribe(onFrame);
        }

        /// <summary>The picture's box in world px, unrotated, glow pad excluded: what a shatter
        /// cuts and what the sparks spray around.</summary>
        internal Rect PictureRectPx()
        {
            var k = DesktopScaling > 0 ? DesktopScaling : 1.0;
            var at = _image.TranslatePoint(new Point(0, 0), this) ?? new Point(0, 0);
            if (_rigCard != null)
            {
                // The rig turns the card; measure the image against the card's own (unturned) box.
                var inCard = _image.TranslatePoint(new Point(0, 0), _rigCard) ?? new Point(0, 0);
                var side = _rigSidePx / k;
                at = new Point((side - _rigCard.Width) / 2 + inCard.X, (side - _rigCard.Height) / 2 + inCard.Y);
            }
            return new Rect(Position.X + at.X * k, Position.Y + at.Y * k, _image.Bounds.Width * k, _image.Bounds.Height * k);
        }

        /// <summary>A still of the picture as it shows right now (the clip or GIF frame
        /// included), at its physical size. Null when it has no size yet.</summary>
        internal Bitmap? Snapshot()
        {
            var k = DesktopScaling > 0 ? DesktopScaling : 1.0;
            var b = _image.Bounds.Size;
            if (b.Width < 1 || b.Height < 1) return null;
            var rtb = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(b.Width * k), (int)Math.Ceiling(b.Height * k)), new Vector(96 * k, 96 * k));
            using (var dc = rtb.CreateDrawingContext())
                if (_image.Source is { } src) dc.DrawImage(src, new Rect(src.Size), FitUniform(src.Size, b));
            return rtb;
        }

        /// <summary>Stretch.Uniform of <paramref name="src"/> centred in <paramref name="box"/>.</summary>
        internal static Rect FitUniform(Size src, Size box)
        {
            if (src.Width <= 0 || src.Height <= 0) return new Rect(box);
            var s = Math.Min(box.Width / src.Width, box.Height / src.Height);
            double w = src.Width * s, h = src.Height * s;
            return new Rect((box.Width - w) / 2, (box.Height - h) / 2, w, h);
        }

        /// <summary>One exit frame on the content: scale/rotate about (0.5, PivotY), drop by
        /// OffsetY, alpha on the content (the compositor alpha stays where the fade-in left it).</summary>
        internal static void ApplyExitFrame(Control content, FlashExitState exit, Size size, double rigAngleRad = 0)
        {
            var f = FlashExit.Sample(exit);
            double px = size.Width * 0.5, py = size.Height * f.PivotY;
            // A rig's card keeps the tilt it had at the pop (WPF draws the exit inside the pivot frame).
            var m = Matrix.CreateTranslation(-px, -py)
                    * Matrix.CreateScale(f.ScaleX, f.ScaleY)
                    * Matrix.CreateRotation(f.RotationDeg * Math.PI / 180)
                    * Matrix.CreateTranslation(px, py + f.OffsetY * size.Height);
            if (rigAngleRad != 0) m *= RotateAbout(rigAngleRad, size.Width / 2, size.Height / 2);
            content.RenderTransform = new MatrixTransform(m);
            content.Opacity = Math.Clamp(f.Alpha, 0, 1);
        }
    }
}
