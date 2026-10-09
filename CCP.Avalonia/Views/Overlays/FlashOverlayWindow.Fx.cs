using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
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
        private DispatcherTimer? _expiryTimer, _pulseTimer, _exitTimer;
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
            Closed += (_, _) => { _pulseTimer?.Stop(); _pulseTimer = null; _expiryTimer?.Stop(); _exitTimer?.Stop(); };
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

        // ---- Press: tap pops; FlashDraggable lets a hand move it first (WPF FlashDrag) ----

        private PixelPoint? _dragFrom;
        private PixelPoint _winFrom;
        private DateTime _pressAt;
        private double _travel;

        private void OnFlashPressed(PointerPressedEventArgs e)
        {
            if (!CoreSettings.Current.FlashDraggable) { Pop(); return; }
            _dragFrom = this.PointToScreen(e.GetPosition(this));
            _winFrom = Position;
            _pressAt = DateTime.Now;
            _travel = 0;
            e.Pointer.Capture(this);
            PointerMoved -= OnDragMove;
            PointerReleased -= OnDragUp;
            PointerMoved += OnDragMove;
            PointerReleased += OnDragUp;
        }

        private void OnDragMove(object? sender, PointerEventArgs e)
        {
            if (_dragFrom is not { } from || IsLeaving) return;
            var p = this.PointToScreen(e.GetPosition(this));
            var d = p - from;
            _travel = Math.Max(_travel, Math.Sqrt((double)d.X * d.X + (double)d.Y * d.Y));
            Position = new PixelPoint(_winFrom.X + d.X, _winFrom.Y + d.Y);
        }

        private void OnDragUp(object? sender, PointerReleasedEventArgs e)
        {
            e.Pointer.Capture(null);
            PointerMoved -= OnDragMove;
            PointerReleased -= OnDragUp;
            _dragFrom = null;
            // WPF FinishPress: a tap pops exactly as a click; a drag only moves (no hydra).
            if (FlashDrag.IsTap(_travel, (DateTime.Now - _pressAt).TotalMilliseconds)) Pop();
        }

        // ---- Exit ----

        /// <summary>The leave animation a pop plays, or WPF's old quick fade for None.</summary>
        private void PlayExit()
        {
            _expiryTimer?.Stop();
            var exit = FlashOverlay.PickExit(CoreSettings.Current, new Random());
            if (exit == null || Content is not Control content)
            {
                Fade(_alpha, 0, TimeSpan.FromMilliseconds(180), Close);
                return;
            }
            Exit = exit;
            content.RenderTransformOrigin = RelativePoint.TopLeft;
            var last = DateTime.Now;
            _exitTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
            {
                var now = DateTime.Now;
                FlashExit.Step(exit, (now - last).TotalSeconds);
                last = now;
                ApplyExitFrame(content, exit, Bounds.Size);
                if (exit.Done) { _exitTimer?.Stop(); Close(); }
            });
            ApplyExitFrame(content, exit, Bounds.Size);
            _exitTimer.Start();
        }

        /// <summary>One exit frame on the content: scale/rotate about (0.5, PivotY), drop by
        /// OffsetY, alpha on the content (the compositor alpha stays where the fade-in left it).</summary>
        internal static void ApplyExitFrame(Control content, FlashExitState exit, Size size)
        {
            var f = FlashExit.Sample(exit);
            double px = size.Width * 0.5, py = size.Height * f.PivotY;
            var m = Matrix.CreateTranslation(-px, -py)
                    * Matrix.CreateScale(f.ScaleX, f.ScaleY)
                    * Matrix.CreateRotation(f.RotationDeg * Math.PI / 180)
                    * Matrix.CreateTranslation(px, py + f.OffsetY * size.Height);
            content.RenderTransform = new MatrixTransform(m);
            content.Opacity = Math.Clamp(f.Alpha, 0, 1);
        }
    }
}
