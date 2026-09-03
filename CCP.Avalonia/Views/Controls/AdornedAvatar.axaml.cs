using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// A circular avatar with an optional wardrobe decoration stacked over it.
    ///
    /// PORTED from ConditioningControlPanel/Views/Controls/AdornedAvatar.xaml.cs. Every
    /// DependencyProperty is a StyledProperty with the same name and default; the six change
    /// callbacks collapse into one <see cref="OnPropertyChanged"/> override. The placement model is
    /// unchanged: the art is drawn at <c>AvatarSize / 0.70</c>, centred, and the offset lives in
    /// the pixels.
    ///
    /// Avalonia does not apply WPF's ArrangeCore layout clip, but the symmetric negative margin is
    /// kept verbatim: it is also what makes the 148px canvas contribute only <c>size</c> to layout.
    /// </summary>
    public partial class AdornedAvatar : UserControl
    {
        // ponytail: WardrobeCatalog stays in the WPF head (pack:// art). Ratio copied; GetImage
        // returns null until the catalogue moves to Core, so the plain avatar draws - the same
        // quiet failure the WPF control documents for a missing PNG.
        private const double AvatarCircleRatio = 0.70;
        private static IImage? GetImage(string? decorationId) => null;

        private readonly Grid _root;
        private readonly Border _avatarRing;
        private readonly Image _decoLayer;
        private readonly Ellipse _presenceDot;

        public AdornedAvatar()
        {
            AvaloniaXamlLoader.Load(this);
            _root = this.FindControl<Grid>("Root")!;
            _avatarRing = this.FindControl<Border>("AvatarRing")!;
            _decoLayer = this.FindControl<Image>("DecoLayer")!;
            _presenceDot = this.FindControl<Ellipse>("PresenceDotShape")!;
            ApplySize();
            ApplyDecoration();
        }

        // ---- the avatar picture ---------------------------------------------------------

        /// <summary>The brush the avatar picture is painted with, for callers that write
        /// <c>AvatarBrush.Source</c> directly as MainWindow's profile code does.</summary>
        internal ImageBrush AvatarBrush => (ImageBrush)_avatarRing.Background!;

        /// <summary>The presence dot, for callers that set its Fill/IsVisible directly.</summary>
        internal Ellipse PresenceDot => _presenceDot;

        public static readonly StyledProperty<IImageBrushSource?> AvatarImageProperty =
            AvaloniaProperty.Register<AdornedAvatar, IImageBrushSource?>(nameof(AvatarImage));

        /// <summary>The avatar picture. Equivalent to setting <c>AvatarBrush.Source</c>.</summary>
        public IImageBrushSource? AvatarImage
        {
            get => GetValue(AvatarImageProperty);
            set => SetValue(AvatarImageProperty, value);
        }

        // ---- the decoration -------------------------------------------------------------

        public static readonly StyledProperty<string?> DecorationIdProperty =
            AvaloniaProperty.Register<AdornedAvatar, string?>(nameof(DecorationId));

        /// <summary>Registry id of the equipped decoration, or null for none. Unknown ids and ids
        /// whose art is missing are treated exactly like null.</summary>
        public string? DecorationId
        {
            get => GetValue(DecorationIdProperty);
            set => SetValue(DecorationIdProperty, value);
        }

        public static readonly StyledProperty<CosmeticTransform?> DecorationTransformProperty =
            AvaloniaProperty.Register<AdornedAvatar, CosmeticTransform?>(nameof(DecorationTransform));

        /// <summary>The wearer's saved placement, or null for the classic centred render. Offsets
        /// are fractions of the decoration canvas, so the same transform reproduces the same
        /// composition at 104px or 28px.</summary>
        public CosmeticTransform? DecorationTransform
        {
            get => GetValue(DecorationTransformProperty);
            set => SetValue(DecorationTransformProperty, value);
        }

        // ---- geometry -------------------------------------------------------------------

        public static readonly StyledProperty<double> AvatarSizeProperty =
            AvaloniaProperty.Register<AdornedAvatar, double>(nameof(AvatarSize), 104d);

        /// <summary>Avatar diameter in DIPs. Everything else scales off it.</summary>
        public double AvatarSize
        {
            get => GetValue(AvatarSizeProperty);
            set => SetValue(AvatarSizeProperty, value);
        }

        public static readonly StyledProperty<IBrush?> RingBrushProperty =
            AvaloniaProperty.Register<AdornedAvatar, IBrush?>(nameof(RingBrush));

        /// <summary>Avatar ring colour. Null keeps the XAML default (profile pink).</summary>
        public IBrush? RingBrush
        {
            get => GetValue(RingBrushProperty);
            set => SetValue(RingBrushProperty, value);
        }

        public static readonly StyledProperty<double> RingThicknessProperty =
            AvaloniaProperty.Register<AdornedAvatar, double>(nameof(RingThickness), 3d);

        public double RingThickness
        {
            get => GetValue(RingThicknessProperty);
            set => SetValue(RingThicknessProperty, value);
        }

        public static readonly StyledProperty<bool> ShowPresenceProperty =
            AvaloniaProperty.Register<AdornedAvatar, bool>(nameof(ShowPresence), true);

        /// <summary>Presence dot on/off. Surfaces that have no presence signal turn it off.</summary>
        public bool ShowPresence
        {
            get => GetValue(ShowPresenceProperty);
            set => SetValue(ShowPresenceProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (_root == null) return; // still loading XAML
            if (change.Property == AvatarImageProperty) AvatarBrush.Source = change.GetNewValue<IImageBrushSource?>();
            else if (change.Property == DecorationIdProperty) ApplyDecoration();
            else if (change.Property == DecorationTransformProperty) ApplyDecorationTransform();
            else if (change.Property == AvatarSizeProperty || change.Property == RingBrushProperty
                     || change.Property == RingThicknessProperty || change.Property == ShowPresenceProperty)
                ApplySize();
        }

        private void ApplyDecorationTransform()
        {
            try
            {
                var t = DecorationTransform;
                if (t == null)
                {
                    _decoLayer.RenderTransform = null;
                    return;
                }

                var canvas = AvatarSize / AvatarCircleRatio;
                if (double.IsNaN(canvas) || double.IsInfinity(canvas) || canvas <= 0) canvas = 148d;

                var group = new TransformGroup();
                group.Children.Add(new ScaleTransform(t.Flip ? -t.Scale : t.Scale, t.Scale));
                group.Children.Add(new RotateTransform(t.Rotation));
                group.Children.Add(new TranslateTransform(t.X * canvas, t.Y * canvas));

                _decoLayer.RenderTransformOrigin = RelativePoint.Center;
                _decoLayer.RenderTransform = group;
            }
            catch (Exception ex)
            {
                Log.Debug("AdornedAvatar: transform failed: {E}", ex.Message);
                _decoLayer.RenderTransform = null;
            }
        }

        private void ApplyDecoration()
        {
            try
            {
                var art = GetImage(DecorationId);
                if (art == null)
                {
                    // Also clears the Source, so switching from a good id to a broken one does not
                    // leave the previous decoration painted on the card.
                    _decoLayer.Source = null;
                    _decoLayer.IsVisible = false;
                    return;
                }

                _decoLayer.Source = art;
                _decoLayer.IsVisible = true;
            }
            catch (Exception ex)
            {
                Log.Debug("AdornedAvatar: decoration {Id} failed: {E}", DecorationId ?? "none", ex.Message);
                _decoLayer.Source = null;
                _decoLayer.IsVisible = false;
            }
        }

        /// <summary>
        /// Lays the three layers out from a single number. The presence dot's proportions are the
        /// 104px hero values (20 / 3 / 4) expressed as ratios, so a 28px leaderboard avatar gets a
        /// dot that still reads instead of one that swallows it.
        /// </summary>
        private void ApplySize()
        {
            try
            {
                var size = AvatarSize;
                if (double.IsNaN(size) || double.IsInfinity(size) || size <= 0) size = 104d;

                _root.Width = size;
                _root.Height = size;

                _avatarRing.CornerRadius = new CornerRadius(size / 2d);
                _avatarRing.BorderThickness = new Thickness(RingThickness);
                if (RingBrush != null) _avatarRing.BorderBrush = RingBrush;

                // The one piece of wardrobe geometry in the app.
                var canvas = size / AvatarCircleRatio;
                _decoLayer.Width = canvas;
                _decoLayer.Height = canvas;
                // A symmetric negative margin makes the total desired size (canvas + margin) equal
                // the slot, so the art centres on the avatar and contributes only `size` to layout.
                _decoLayer.Margin = new Thickness(-(canvas - size) / 2d);
                // Translate offsets are canvas-relative, so a size change re-derives them.
                ApplyDecorationTransform();

                var dot = Math.Max(6d, size * 0.1923d);
                _presenceDot.Width = dot;
                _presenceDot.Height = dot;
                _presenceDot.StrokeThickness = Math.Max(1d, size * 0.0288d);
                var inset = size * 0.0385d;
                _presenceDot.Margin = new Thickness(0, 0, inset, inset);
                _presenceDot.IsVisible = ShowPresence;
            }
            catch (Exception ex)
            {
                Log.Debug("AdornedAvatar: layout failed: {E}", ex.Message);
            }
        }
    }
}
