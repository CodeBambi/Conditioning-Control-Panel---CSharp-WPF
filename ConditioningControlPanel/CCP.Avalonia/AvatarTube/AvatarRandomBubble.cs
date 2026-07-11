using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Chaos;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    /// <summary>
    /// A clickable bubble that spawns near the avatar and floats upward.
    ///
    /// 2026-07-11 retest-2 fix 2 (Engram obs #6): the first port created a FRESH
    /// transparent topmost Window + a 50ms DispatcherTimer per bubble and only tore them
    /// down when the bubble floated off-screen or popped - N live windows x N timers
    /// accumulated into a UI-thread freeze and orphaned bubble windows survived tube
    /// close/theme switches. The WPF head already solved the window half of this with a
    /// KEEP-ALIVE fixed-size window pool (WPF AvatarRandomBubble.cs:37-46, freeze #494:
    /// a fresh layered Window.Show() runs a synchronous first-realization render, and
    /// one per bubble under load is a deadlock trigger). This port restores that pool
    /// and adds the owner-mandated live-set contract: a hard concurrency ceiling, a
    /// tracked live set, and guaranteed disposal of every live bubble on tube close and
    /// theme/reskin. All members are UI-thread only.
    /// </summary>
    internal class AvatarRandomBubble
    {
        private readonly Window _window;
        private readonly DispatcherTimer _animTimer;
        private readonly Random _random;
        private readonly Action _onPop;
        private readonly Image _bubbleImage;

        private double _posX, _posY;
        private double _startX;
        private double _speed;
        private double _timeAlive;
        private double _wobbleOffset;
        private double _angle;
        private double _scale = 1.0;
        private double _fadeAlpha = 1.0;
        private int _animType;
        private bool _isPopping;
        private bool _isAlive = true;

        private readonly int _size;
        private readonly double _screenTop;

        // KEEP-ALIVE window pool (WPF AvatarRandomBubble.cs:37-46): windows are a FIXED
        // size and realized once, then Hide()/Show() reused; the bubble image is centred
        // inside, so it can vary size without ever resizing the window. Pool ceiling
        // matches WPF POOL_MAX (WPF AvatarRandomBubble.cs:44).
        private const int WindowDim = 170; // fixed reusable size (max bubble 130 + slack)
        private const int PoolMax = 6;
        private static readonly Stack<Window> Pool = new();

        // Live-set contract (obs #6 retest-2 fix 2, NEW - no WPF precedent): every live
        // bubble is tracked, spawns above the ceiling are refused, and DestroyAll() is
        // called on tube close and theme/reskin so N bubbles produce ZERO net
        // window/timer accumulation.
        private static readonly List<AvatarRandomBubble> Live = new();

        /// <summary>Hard ceiling on concurrently live bubbles (matches the WPF pool size).</summary>
        internal const int MaxLive = 6;

        /// <summary>Currently live (animating) bubbles. UI-thread only.</summary>
        internal static int LiveCount => Live.Count;

        /// <summary>Idle pooled windows waiting for reuse. UI-thread only.</summary>
        internal static int PooledWindowCount => Pool.Count;

        /// <summary>True when a new bubble may spawn without breaching the ceiling.</summary>
        internal static bool CanSpawn => Live.Count < MaxLive;

        // The bubble art is identical for every bubble; decode it once instead of once
        // per spawn (the per-spawn decode ran on the UI thread).
        private static Bitmap? _bubbleBitmapCache;

        /// <summary>
        /// Destroys every live bubble (stops its timer, returns its window to the pool).
        /// Called on tube close and on theme/reskin (obs #6 fix 2: dispose-before-recreate
        /// so a theme switch never leaves orphaned bubble windows/timers behind).
        /// </summary>
        internal static void DestroyAll()
        {
            foreach (var bubble in Live.ToArray())
                bubble.Destroy();
        }

        /// <summary>
        /// Closes every pooled window for real. Called on tube close so nothing outlives
        /// the tube; theme switches keep the pool (windows are contentless while pooled).
        /// </summary>
        internal static void DrainPool()
        {
            while (Pool.Count > 0)
            {
                var w = Pool.Pop();
                try { w.Close(); } catch { /* already closing */ }
            }
        }

        public AvatarRandomBubble(global::Avalonia.Point avatarScreenPos, Random random, Action onPop)
            : this(avatarScreenPos, random, onPop, App.Services.GetService<IAssetLoader>())
        {
        }

        public AvatarRandomBubble(global::Avalonia.Point avatarScreenPos, Random random, Action onPop, IAssetLoader? assetLoader)
        {
            _random = random;
            _onPop = onPop;

            _size = random.Next(80, 130);
            _speed = 1.0 + random.NextDouble() * 1.0;
            _animType = random.Next(4);
            _wobbleOffset = random.NextDouble() * 100;
            _angle = random.Next(360);

            // avatarScreenPos is already physical px (PointToScreen); Window.Position is
            // physical too, so no DPI conversion is needed on this side.
            _startX = avatarScreenPos.X + 50 + random.Next(-30, 30);
            _posX = _startX;
            _posY = avatarScreenPos.Y;
            _screenTop = -_size - 50;

            // Bubble image centred inside the fixed-size window; the IMAGE varies size,
            // the WINDOW never does (WPF AvatarRandomBubble.cs:69-80: "size varies,
            // window doesn't" - a resize is the freeze trigger, moving is safe).
            _bubbleImage = new Image
            {
                Width = _size,
                Height = _size,
                Stretch = Stretch.Uniform,
                Source = LoadBubbleImage(assetLoader),
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                IsHitTestVisible = true
            };

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(1, 1));
            transformGroup.Children.Add(new RotateTransform(0));
            _bubbleImage.RenderTransform = transformGroup;

            _bubbleImage.PointerPressed += (s, e) =>
            {
                Pop();
                e.Handled = true;
            };

            // Alpha-1 background keeps the whole square hit-testable
            // (WPF AvatarRandomBubble.cs:117-123).
            var grid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                IsHitTestVisible = true
            };
            grid.Children.Add(_bubbleImage);
            grid.PointerPressed += (s, e) =>
            {
                Pop();
                e.Handled = true;
            };

            // Rent a fixed-size, kept-alive window and drop in this bubble's content.
            // NO window-level click handler: it would accumulate across pooled reuses
            // (WPF AvatarRandomBubble.cs:134-136); the grid + image handlers rebuilt
            // fresh every spawn already cover the whole surface.
            _window = RentWindow();
            _window.Content = grid;
            _window.Opacity = 1;
            UpdateWindowPos();

            _window.Show();

            // WPF parity (AvatarRandomBubble.cs:146-147 HideFromAltTab: WS_EX_TOOLWINDOW |
            // WS_EX_NOACTIVATE): without NOACTIVATE a click on the bubble activates it and
            // destroying it hands activation around - churn the attached tube then reacts
            // to. ApplyOverlayExStyles is the repo's sanctioned helper; transparent:false
            // keeps the bubble clickable. Re-applied per spawn: pooled windows must be
            // re-based every reuse (overlay-clickthrough rule: stale ex-styles on recycled
            // windows caused the unpoppable-bubble bug).
            ChaosWin32Helper.ApplyOverlayExStyles(_window, transparent: false);

            Live.Add(this);

            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _animTimer.Tick += Animate;
            _animTimer.Start();
        }

        private static Bitmap? LoadBubbleImage(IAssetLoader? loader)
        {
            try
            {
                return _bubbleBitmapCache ??= AvaloniaBitmapHelper.LoadResource("bubble.png");
            }
            catch
            {
                return null;
            }
        }

        private void Animate(object? sender, EventArgs e)
        {
            if (!_isAlive) return;

            if (_isPopping)
            {
                _scale += 0.06;
                _fadeAlpha -= 0.1;
                _angle += 3;
                if (_fadeAlpha <= 0) { Destroy(); return; }
            }
            else
            {
                _timeAlive += 0.03;
                _posY -= _speed;
                double offset = _animType switch
                {
                    0 => Math.Sin(_timeAlive * 2) * 25,
                    1 => Math.Sin(_timeAlive * 2.5) * 30,
                    2 => Math.Cos(_timeAlive * 1.8) * 25,
                    _ => Math.Sin(_timeAlive) * 30 + Math.Cos(_timeAlive * 2) * 15
                };
                _angle = (_angle + (_animType == 2 ? -1.0 : 0.5)) % 360;
                _posX = _startX + offset;
                if (_posY < _screenTop) { Destroy(); return; }
            }

            try
            {
                var wobble = 0.06 * Math.Sin(_timeAlive * 2.5 + _wobbleOffset);
                var currentScale = _scale + wobble;
                if (_bubbleImage.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
                {
                    if (tg.Children[0] is ScaleTransform st) { st.ScaleX = currentScale; st.ScaleY = currentScale; }
                    if (tg.Children[1] is RotateTransform rt) rt.Angle = _angle;
                }
                _window.Opacity = _fadeAlpha;
                UpdateWindowPos();
            }
            catch
            {
                Destroy();
            }
        }

        /// <summary>
        /// Positions the fixed-size window so the centred bubble image sits where the old
        /// per-size window put it (WPF AvatarRandomBubble.cs:257-263 UpdateWindowPos).
        /// </summary>
        private void UpdateWindowPos()
        {
            _window.Position = new PixelPoint(
                (int)(_posX + _size / 2.0 - WindowDim / 2.0 + 22),
                (int)(_posY + _size / 2.0 - WindowDim / 2.0));
        }

        public void Pop()
        {
            if (!_isAlive || _isPopping) return;
            _isPopping = true;
            _onPop?.Invoke();
        }

        /// <summary>
        /// Tears this bubble down: stops its timer, removes it from the live set and
        /// returns its window to the pool. Idempotent; internal so the tube window can
        /// force-destroy on close/reskin (obs #6 fix 2).
        /// </summary>
        internal void Destroy()
        {
            if (!_isAlive) return;
            _isAlive = false;
            _animTimer.Stop();
            Live.Remove(this);
            ReturnWindow();
        }

        /// <summary>WPF ReturnWindow parity (AvatarRandomBubble.cs:298-311).</summary>
        private void ReturnWindow()
        {
            var w = _window;
            try
            {
                w.Content = null;
                w.Opacity = 1;
                w.Hide();
            }
            catch
            {
                try { w.Close(); } catch { /* already closing */ }
                return;
            }

            if (Pool.Count < PoolMax && !Pool.Contains(w))
            {
                Pool.Push(w);
                return;
            }
            try { w.Close(); } catch { /* already closing */ }
        }

        /// <summary>WPF RentWindow parity (AvatarRandomBubble.cs:264-296).</summary>
        private static Window RentWindow()
        {
            if (Pool.Count > 0)
                return Pool.Pop();

            return new Window
            {
                WindowDecorations = WindowDecorations.None,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                Width = WindowDim,
                Height = WindowDim,
                Cursor = new Cursor(StandardCursorType.Hand),
                IsHitTestVisible = true
            };
        }
    }
}
