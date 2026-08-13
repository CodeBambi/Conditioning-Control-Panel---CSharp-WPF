using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z0 header band + Z1 the Companion Card. See the XAML header for the visual spec.
    ///
    /// <para>This control owns the Companion tab's <b>single ambient loop</b>: the portrait ring
    /// breathing 1.000 ↔ 1.015. The FX plan allows exactly one Forever storyboard per tab, and
    /// this is where it is spent — nothing else on the page may add another.</para>
    ///
    /// <para>The loop is parked in three situations, so a hidden or sleeping hero is never still
    /// burning a composition clock: on unload, while <see cref="ICompanionHeroCardVm.IsCompanionEnabled"/>
    /// is false (the mockup's <c>animation:none</c> asleep state), and whenever the viewmodel is
    /// swapped out.</para>
    ///
    /// <para><b>It also owns the portrait's geometry and its mod repaint.</b> See
    /// <see cref="ApplyAvatarArt"/>: the viewmodel decides WHICH bitmap (resolved through
    /// <c>ModResourceResolver</c>), this file decides where it sits inside the ring, and
    /// <c>App.Mods.ModChanged</c> is what makes both re-answer. Nothing else repaints this tab on a
    /// mod switch — <c>MainWindow.ApplyActiveModChange</c> never reaches the Companion room — so
    /// without the hook the ring kept the previous mod's bust, name and flavour until something
    /// unrelated happened to call <c>Sync</c>.</para>
    /// </summary>
    public partial class CompanionHeroCard : UserControl
    {
        /// <summary>
        /// How much of the ring's inner diameter the figure's own INK may occupy. Below 1 on
        /// purpose: art that touches a circular frame reads as jammed into it, and the busts are
        /// tall enough that a hair's-breadth margin at the crown is the difference between a
        /// portrait and a crop.
        /// </summary>
        private const double PortraitInkFill = 0.86;

        /// <summary>Alpha at or under which a probe pixel is padding, not art.</summary>
        private const byte PortraitAlphaFloor = 8;

        /// <summary>
        /// Width the ink probe downscales to before it reads pixels. The answer wanted is a
        /// FRACTION of the image, so this is all the resolution the measurement needs — and it
        /// caps the read at ~40KB whether the mod ships a 540px bust or a 4K one.
        /// </summary>
        private const int PortraitProbeWidth = 96;

        private Storyboard? _breathe;
        private ICompanionHeroCardVm? _observed;

        /// <summary>Guards the ModChanged hook: Loaded fires again on every re-parent.</summary>
        private bool _modHooked;

        public CompanionHeroCard()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += OnDataContextChanged;
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public ICompanionHeroCardVm? ViewModel
        {
            get => DataContext as ICompanionHeroCardVm;
            set => DataContext = value;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Known Issues #2: never animate before the element is loaded and templated.
            if (!IsLoaded) return;

            Observe(ViewModel);

            // The neighbours' idiom (Features/BubblePopFeatureControl, Views/Tabs/ExclusivesTabView):
            // hook on Loaded, unhook on Unloaded, and never let a re-parent double-subscribe.
            if (!_modHooked && App.Mods != null)
            {
                App.Mods.ModChanged += OnModChanged;
                _modHooked = true;
            }

            // DispatcherPriority.Normal, never Loaded — Loaded is starved in this app and the
            // breathe would silently never start.
            Dispatcher.BeginInvoke(new Action(RefreshAmbientState), DispatcherPriority.Normal);

            // Same priority, same reason: the Portrait binding has not flushed to the brush yet
            // when Loaded runs, and the Viewbox is computed FROM that bitmap.
            Dispatcher.BeginInvoke(new Action(ApplyAvatarArt), DispatcherPriority.Normal);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Observe(null);
            StopAmbientLoop();

            if (_modHooked && App.Mods != null) App.Mods.ModChanged -= OnModChanged;
            _modHooked = false;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Observe(ViewModel);
            RefreshAmbientState();
            CentrePortrait();
        }

        /// <summary>ModChanged can be raised off the UI thread; marshal before touching the brush.</summary>
        private void OnModChanged(object? sender, Models.ModPackage mod)
            => Dispatcher.BeginInvoke(new Action(ApplyAvatarArt));

        /// <summary>
        /// Subscribes to the live viewmodel so the asleep state can park the loop, and — more
        /// importantly — unsubscribes from the previous one. A hero that is re-pointed at a new
        /// companion must not leave a handler rooted in the old viewmodel.
        /// </summary>
        private void Observe(ICompanionHeroCardVm? vm)
        {
            if (ReferenceEquals(_observed, vm)) return;

            if (_observed != null) _observed.PropertyChanged -= OnViewModelPropertyChanged;
            _observed = vm;
            if (_observed != null) _observed.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var name = e.PropertyName;
            bool all = string.IsNullOrEmpty(name);

            // A new bitmap means new opaque bounds, so the Viewbox has to be recomputed. This does
            // NOT re-enter the viewmodel — CentrePortrait only measures — which is what keeps a
            // Portrait change from looping back into another Sync.
            if (all || string.Equals(name, nameof(ICompanionHeroCardVm.Portrait), StringComparison.Ordinal))
                CentrePortrait();

            if (all || string.Equals(name, nameof(ICompanionHeroCardVm.IsCompanionEnabled), StringComparison.Ordinal))
                RefreshAmbientState();
        }

        /// <summary>Starts or parks the breathe to match the current state. Safe to call any time.</summary>
        public void RefreshAmbientState()
        {
            if (IsLoaded && ViewModel?.IsCompanionEnabled != false) StartAmbientLoop();
            else StopAmbientLoop();
        }

        /// <summary>Starts (or restarts) the portrait breathe. Idempotent.</summary>
        public void StartAmbientLoop()
        {
            try
            {
                if (!IsLoaded || _breathe != null) return;
                if (TryFindResource("CmpPortraitBreatheStoryboard") is not Storyboard proto) return;

                var sb = proto.Clone();
                foreach (var tl in sb.Children)
                {
                    if (tl is DoubleAnimation da) Storyboard.SetTarget(da, PortraitRingScale);
                }
                sb.Begin(this, isControllable: true);
                _breathe = sb;
            }
            catch (InvalidOperationException)
            {
                // A failed decorative animation must never take the tab down with it.
                _breathe = null;
            }
        }

        /// <summary>Stops the ambient loop and releases the clock.</summary>
        public void StopAmbientLoop()
        {
            try
            {
                _breathe?.Stop(this);
            }
            catch (InvalidOperationException) { /* already torn down */ }
            finally
            {
                _breathe = null;
            }
        }

        // =====================================================================================
        //  the portrait: mod repaint + optical centring
        // =====================================================================================

        /// <summary>
        /// Re-reads her bust and re-centres it in the ring. Called on Loaded and on every
        /// <c>ModChanged</c> — the authoritative "the art answers differently now" signal, and the
        /// only one this tab gets.
        ///
        /// <para>The split of duties is deliberate. <see cref="Runtime.CompanionHeroRuntimeVm"/>
        /// owns WHICH bitmap: it resolves the active avatar set's pose-1 through
        /// <c>ModResourceResolver.ResolveImageDecoded</c> and keeps the one already painted when
        /// that comes back null, so a mod with no bust of its own never blanks the ring. This
        /// method owns where it sits. A mock viewmodel has nothing to re-read, so the gallery and
        /// the render suites simply fall through to the re-centre.</para>
        /// </summary>
        internal void ApplyAvatarArt()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ApplyAvatarArt)); return; }

            try
            {
                // Re-resolving also re-reads her name, mod chip and flavour, which are MakeModAware
                // and went stale on exactly the same signal.
                (ViewModel as Runtime.CompanionHeroRuntimeVm)?.Sync();
                CentrePortrait();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Companion hero: avatar art refresh failed: {E}", ex.Message);
            }
        }

        /// <summary>
        /// Points the portrait brush's Viewbox at a square centred on the art's own opaque bounds.
        ///
        /// <para>The brush is mutated in place and never frozen — it carries a Binding on
        /// ImageSource, so it could not be frozen anyway, and a fresh brush per repaint would drop
        /// that binding on the floor.</para>
        /// </summary>
        private void CentrePortrait()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(CentrePortrait)); return; }

            try
            {
                if (PortraitBrush == null) return;
                PortraitBrush.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
                PortraitBrush.Viewbox = InkViewbox(PortraitBrush.ImageSource);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Companion hero: portrait centring failed: {E}", ex.Message);
            }
        }

        /// <summary>
        /// The relative Viewbox that puts <paramref name="source"/>'s opaque bounds dead centre in a
        /// square viewport, at <see cref="PortraitInkFill"/> of its width.
        ///
        /// <para>A SQUARE region of the source, not the ink rectangle itself: the viewport is the
        /// ring's 132x132 bounding box, so a square in equals a square out and <c>Stretch=Uniform</c>
        /// becomes exact rather than approximate. The square is allowed to run off the edge of the
        /// source — outside a brush's Viewbox is simply transparent, which is what lets a tall thin
        /// figure sit in a circle without being widened.</para>
        ///
        /// <para>Falls back to the whole image (0,0,1,1 — the default, i.e. the old behaviour minus
        /// the crop) whenever the art cannot be measured.</para>
        /// </summary>
        internal static Rect InkViewbox(ImageSource? source)
        {
            var whole = new Rect(0, 0, 1, 1);

            if (source is not BitmapSource bmp) return whole;
            double w = bmp.PixelWidth, h = bmp.PixelHeight;
            if (w <= 0 || h <= 0) return whole;

            var ink = OpaqueBounds(bmp);
            if (ink is not { } f) return whole;

            // fractions back onto the source's own pixel grid, where "square" means something
            double iw = f.W * w, ih = f.H * h;
            double side = Math.Max(iw, ih) / PortraitInkFill;
            if (side <= 0) return whole;

            double cx = (f.X + f.W / 2) * w;
            double cy = (f.Y + f.H / 2) * h;
            return new Rect((cx - side / 2) / w, (cy - side / 2) / h, side / w, side / h);
        }

        /// <summary>
        /// The bounding box of everything in <paramref name="bmp"/> that is not transparent
        /// padding, as fractions of the image. Null when it cannot be read, or when the art is
        /// transparent end to end and there is nothing to centre on.
        /// </summary>
        private static (double X, double Y, double W, double H)? OpaqueBounds(BitmapSource bmp)
        {
            try
            {
                double scale = Math.Min(1.0, PortraitProbeWidth / (double)bmp.PixelWidth);
                BitmapSource probe = scale < 1.0
                    ? new TransformedBitmap(bmp, new ScaleTransform(scale, scale))
                    : bmp;
                if (probe.Format != PixelFormats.Bgra32)
                    probe = new FormatConvertedBitmap(probe, PixelFormats.Bgra32, null, 0);

                int pw = probe.PixelWidth, ph = probe.PixelHeight;
                if (pw <= 0 || ph <= 0) return null;

                int stride = pw * 4;
                var px = new byte[stride * ph];
                probe.CopyPixels(px, stride, 0);

                int minX = pw, minY = ph, maxX = -1, maxY = -1;
                for (int y = 0; y < ph; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < pw; x++)
                    {
                        if (px[row + x * 4 + 3] <= PortraitAlphaFloor) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }

                if (maxX < minX || maxY < minY) return null;

                return (minX / (double)pw, minY / (double)ph,
                        (maxX - minX + 1) / (double)pw, (maxY - minY + 1) / (double)ph);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Companion hero: opaque-bounds probe failed: {E}", ex.Message);
                return null;
            }
        }
    }
}
