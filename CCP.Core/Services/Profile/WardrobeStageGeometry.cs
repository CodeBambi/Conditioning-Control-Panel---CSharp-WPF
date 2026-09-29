using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The one place wardrobe fractions turn into pixels - shared by the live Trainer Card
    /// (MainWindow.ProfileWardrobe) and by the Wardrobe editor's stage, so the preview and the
    /// card cannot drift apart again.
    ///
    /// <para><b>Why this exists.</b> The editor used to mock the card as a fixed 640x240 box while
    /// the real hero is a full-width strip - measured at ~1394x249 DIP on a 1440-wide window, i.e.
    /// aspect 5.6 against the mock's 2.67. Charm placement is stored as a fraction of the card's
    /// width and height, so the same numbers produced two completely different compositions: at
    /// x=0.64 a charm sits 410px from the left of the mock and 890px from the left of the card, and
    /// because a charm's size is a fraction of HEIGHT it also reads half as large against the wide
    /// card. Same story for the banner crop. The fix is to stop inventing a stage aspect and derive
    /// the whole stage from the card that is actually on screen.</para>
    ///
    /// Everything here is pure arithmetic with no WPF types, so it is unit-testable and is tested
    /// (Tests/WardrobeGeometryTests).
    /// </summary>
    public static class WardrobeStageGeometry
    {
        /// <summary>Hero avatar diameter in DIPs (DiscordTabView.xaml: AdornedAvatar AvatarSize).</summary>
        public const double CardAvatarSize = 104d;

        /// <summary>
        /// Avatar box offset from the card's top-left in DIPs: the hero's content Grid margin
        /// (18,16) plus the AdornedAvatar's own margin (6,6).
        /// </summary>
        public const double CardAvatarLeft = 24d;
        public const double CardAvatarTop = 22d;

        /// <summary>
        /// Stand-in card used when the live card has not been measured yet (the Customize dialog
        /// can be opened before the hero has ever been arranged). A 1200x250 strip is a typical
        /// windowed hero and keeps the stage sane rather than dividing by zero.
        /// </summary>
        public const double FallbackCardWidth = 1200d;
        public const double FallbackCardHeight = 250d;

        /// <summary>A to-scale mock of the hero card: the card multiplied by <see cref="Scale"/>.</summary>
        public readonly struct Stage
        {
            public Stage(double width, double height, double scale)
            {
                Width = width;
                Height = height;
                Scale = scale;
            }

            /// <summary>Stage size in DIPs.</summary>
            public double Width { get; }
            public double Height { get; }

            /// <summary>Stage DIPs per card DIP. Everything drawn on the stage is multiplied by it.</summary>
            public double Scale { get; }

            public double AvatarSize => CardAvatarSize * Scale;
            public double AvatarLeft => CardAvatarLeft * Scale;
            public double AvatarTop => CardAvatarTop * Scale;

            /// <summary>Centre of the avatar bubble on the stage.</summary>
            public double AvatarCentreX => AvatarLeft + AvatarSize / 2d;
            public double AvatarCentreY => AvatarTop + AvatarSize / 2d;
        }

        /// <summary>
        /// The largest mock of <paramref name="cardWidth"/> x <paramref name="cardHeight"/> that
        /// fits inside the editor's stage box, at the card's own aspect ratio. Garbage in (zero,
        /// NaN, infinity - an unmeasured card) degrades to the fallback card rather than to a
        /// division by zero.
        /// </summary>
        public static Stage ForCard(double cardWidth, double cardHeight, double maxWidth, double maxHeight)
        {
            if (!IsUsable(cardWidth) || !IsUsable(cardHeight))
            {
                cardWidth = FallbackCardWidth;
                cardHeight = FallbackCardHeight;
            }

            if (!IsUsable(maxWidth)) maxWidth = cardWidth;
            if (!IsUsable(maxHeight)) maxHeight = cardHeight;

            var scale = Math.Min(maxWidth / cardWidth, maxHeight / cardHeight);
            if (!IsUsable(scale)) scale = 1d;

            return new Stage(cardWidth * scale, cardHeight * scale, scale);
        }

        /// <summary>
        /// Where a charm sprite lands on a surface. <paramref name="x"/>/<paramref name="y"/> are
        /// the sprite's centre as a fraction of the surface, and its size is
        /// <see cref="WardrobeCatalog.CharmBaseHeightFraction"/> of the surface HEIGHT times the
        /// wearer's scale - so the same numbers reproduce the same composition on any surface with
        /// the same aspect ratio, and only there. Called by both render paths.
        /// </summary>
        public static (double Left, double Top, double Size) CharmRect(
            double surfaceWidth, double surfaceHeight, double x, double y, double scale)
        {
            if (!IsUsable(surfaceWidth)) surfaceWidth = 0d;
            if (!IsUsable(surfaceHeight)) surfaceHeight = 0d;
            if (!IsUsable(scale)) scale = 1d;

            var size = Math.Max(1d, WardrobeCatalog.CharmBaseHeightFraction * surfaceHeight * scale);
            return (x * surfaceWidth - size / 2d, y * surfaceHeight - size / 2d, size);
        }

        /// <summary>
        /// Where a decoration canvas lands over an avatar bubble. The art is authored so its avatar
        /// circle fills <see cref="WardrobeCatalog.AvatarCircleRatio"/> of the canvas, hence the
        /// canvas is drawn at <c>avatarSize / 0.70</c>, centred on the bubble - the offset lives in
        /// the pixels, which is why no decoration needs anchor metadata.
        /// </summary>
        public static (double Left, double Top, double Canvas) DecorationRect(
            double avatarSize, double avatarLeft, double avatarTop)
        {
            if (!IsUsable(avatarSize)) avatarSize = CardAvatarSize;

            var canvas = avatarSize / WardrobeCatalog.AvatarCircleRatio;
            if (!IsUsable(canvas)) canvas = avatarSize;

            return (avatarLeft + avatarSize / 2d - canvas / 2d,
                    avatarTop + avatarSize / 2d - canvas / 2d,
                    canvas);
        }

        private static bool IsUsable(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
    }
}
