using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Views.Controls
{
    /// <summary>
    /// A circular avatar with an optional wardrobe decoration stacked over it (Profile redesign
    /// Phase 3). Reusable by design - the profile hero uses it today, and the leaderboard row,
    /// friends rail and duel plate are drop-ins once their payloads carry a decoration id.
    ///
    /// The whole placement model is one line: the art is authored on a square canvas whose avatar
    /// circle is centred at <see cref="WardrobeCatalog.AvatarCircleRatio"/> of the canvas width, so
    /// the decoration is drawn at <c>AvatarSize / 0.70</c>, centred. That is why hats, chokers and
    /// visors need no metadata and no per-item code: the offset lives in the pixels.
    ///
    /// Failure is always the same, quiet thing: an unknown id, a missing PNG or an unreadable one
    /// leaves the plain avatar on screen.
    /// </summary>
    public partial class AdornedAvatar : UserControl
    {
        public AdornedAvatar()
        {
            InitializeComponent();
            ApplySize();
            ApplyDecoration();
        }

        // ---- the avatar picture ---------------------------------------------------------

        /// <summary>
        /// The brush the avatar picture is painted with. Exposed (rather than only the
        /// <see cref="AvatarImage"/> property) because MainWindow's profile code has always written
        /// <c>ProfileViewerAvatar.ImageSource</c> directly, and this control has to be a drop-in
        /// for that ImageBrush without touching ~16 call sites.
        /// </summary>
        internal ImageBrush AvatarBrush => AvatarImageBrush;

        /// <summary>The presence dot, for callers that set its Fill/Visibility directly.</summary>
        internal System.Windows.Shapes.Ellipse PresenceDot => PresenceDotShape;

        public static readonly DependencyProperty AvatarImageProperty = DependencyProperty.Register(
            nameof(AvatarImage), typeof(ImageSource), typeof(AdornedAvatar),
            new PropertyMetadata(null, OnAvatarImageChanged));

        /// <summary>The avatar picture. Equivalent to setting <c>AvatarBrush.ImageSource</c>.</summary>
        public ImageSource? AvatarImage
        {
            get => (ImageSource?)GetValue(AvatarImageProperty);
            set => SetValue(AvatarImageProperty, value);
        }

        private static void OnAvatarImageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AdornedAvatar control && control.AvatarImageBrush != null)
                control.AvatarImageBrush.ImageSource = e.NewValue as ImageSource;
        }

        // ---- the decoration -------------------------------------------------------------

        public static readonly DependencyProperty DecorationIdProperty = DependencyProperty.Register(
            nameof(DecorationId), typeof(string), typeof(AdornedAvatar),
            new PropertyMetadata(null, OnDecorationIdChanged));

        /// <summary>
        /// Registry id of the equipped decoration, or null for none. Unknown ids and ids whose art
        /// is missing are treated exactly like null.
        /// </summary>
        public string? DecorationId
        {
            get => (string?)GetValue(DecorationIdProperty);
            set => SetValue(DecorationIdProperty, value);
        }

        private static void OnDecorationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => (d as AdornedAvatar)?.ApplyDecoration();

        private void ApplyDecoration()
        {
            try
            {
                if (DecoLayer == null) return;

                var art = WardrobeCatalog.GetImage(DecorationId);
                if (art == null)
                {
                    // Also clears the Source, so switching from a good id to a broken one does not
                    // leave the previous decoration painted on the card.
                    DecoLayer.Source = null;
                    DecoLayer.Visibility = Visibility.Collapsed;
                    return;
                }

                DecoLayer.Source = art;
                DecoLayer.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AdornedAvatar: decoration {Id} failed: {E}", DecorationId ?? "none", ex.Message);
                if (DecoLayer != null)
                {
                    DecoLayer.Source = null;
                    DecoLayer.Visibility = Visibility.Collapsed;
                }
            }
        }

        // ---- geometry -------------------------------------------------------------------

        public static readonly DependencyProperty AvatarSizeProperty = DependencyProperty.Register(
            nameof(AvatarSize), typeof(double), typeof(AdornedAvatar),
            new PropertyMetadata(104d, OnGeometryChanged));

        /// <summary>Avatar diameter in DIPs. Everything else scales off it.</summary>
        public double AvatarSize
        {
            get => (double)GetValue(AvatarSizeProperty);
            set => SetValue(AvatarSizeProperty, value);
        }

        public static readonly DependencyProperty RingBrushProperty = DependencyProperty.Register(
            nameof(RingBrush), typeof(Brush), typeof(AdornedAvatar),
            new PropertyMetadata(null, OnGeometryChanged));

        /// <summary>Avatar ring colour. Null keeps the XAML default (profile pink).</summary>
        public Brush? RingBrush
        {
            get => (Brush?)GetValue(RingBrushProperty);
            set => SetValue(RingBrushProperty, value);
        }

        public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
            nameof(RingThickness), typeof(double), typeof(AdornedAvatar),
            new PropertyMetadata(3d, OnGeometryChanged));

        public double RingThickness
        {
            get => (double)GetValue(RingThicknessProperty);
            set => SetValue(RingThicknessProperty, value);
        }

        public static readonly DependencyProperty ShowPresenceProperty = DependencyProperty.Register(
            nameof(ShowPresence), typeof(bool), typeof(AdornedAvatar),
            new PropertyMetadata(true, OnGeometryChanged));

        /// <summary>Presence dot on/off. Surfaces that have no presence signal turn it off.</summary>
        public bool ShowPresence
        {
            get => (bool)GetValue(ShowPresenceProperty);
            set => SetValue(ShowPresenceProperty, value);
        }

        private static void OnGeometryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => (d as AdornedAvatar)?.ApplySize();

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

                if (Root != null)
                {
                    Root.Width = size;
                    Root.Height = size;
                }

                if (AvatarRing != null)
                {
                    AvatarRing.CornerRadius = new CornerRadius(size / 2d);
                    AvatarRing.BorderThickness = new Thickness(RingThickness);
                    if (RingBrush != null) AvatarRing.BorderBrush = RingBrush;
                }

                if (DecoLayer != null)
                {
                    // The one piece of wardrobe geometry in the app.
                    var canvas = size / WardrobeCatalog.AvatarCircleRatio;
                    DecoLayer.Width = canvas;
                    DecoLayer.Height = canvas;
                }

                if (PresenceDotShape != null)
                {
                    var dot = Math.Max(6d, size * 0.1923d);
                    PresenceDotShape.Width = dot;
                    PresenceDotShape.Height = dot;
                    PresenceDotShape.StrokeThickness = Math.Max(1d, size * 0.0288d);
                    var inset = size * 0.0385d;
                    PresenceDotShape.Margin = new Thickness(0, 0, inset, inset);
                    PresenceDotShape.Visibility = ShowPresence ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AdornedAvatar: layout failed: {E}", ex.Message);
            }
        }
    }
}
