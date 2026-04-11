using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Click-to-open tile for a feature on the dashboard grid. Shows an icon + title;
    /// when locked, desaturates the content and overlays a padlock + required level.
    /// </summary>
    public partial class FeatureCard : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(FeatureCard),
                new PropertyMetadata("Feature", OnTitleChanged));

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(ImageSource), typeof(FeatureCard),
                new PropertyMetadata(null, OnIconChanged));

        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(FeatureCard),
                new PropertyMetadata(null, OnGlyphChanged));

        public static readonly DependencyProperty LockLevelProperty =
            DependencyProperty.Register(nameof(LockLevel), typeof(int), typeof(FeatureCard),
                new PropertyMetadata(0, OnLockStateChanged));

        public static readonly DependencyProperty IsLockedProperty =
            DependencyProperty.Register(nameof(IsLocked), typeof(bool), typeof(FeatureCard),
                new PropertyMetadata(false, OnLockStateChanged));

        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(FeatureCard),
                new PropertyMetadata(false, OnActiveStateChanged));

        public static readonly DependencyProperty HelpSectionIdProperty =
            DependencyProperty.Register(nameof(HelpSectionId), typeof(string), typeof(FeatureCard),
                new PropertyMetadata(null, OnHelpSectionIdChanged));

        public static readonly RoutedEvent ClickEvent =
            EventManager.RegisterRoutedEvent(nameof(Click), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(FeatureCard));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public ImageSource? Icon
        {
            get => (ImageSource?)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public string? Glyph
        {
            get => (string?)GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        /// <summary>Required level for this feature. 0 means always unlocked.</summary>
        public int LockLevel
        {
            get => (int)GetValue(LockLevelProperty);
            set => SetValue(LockLevelProperty, value);
        }

        public bool IsLocked
        {
            get => (bool)GetValue(IsLockedProperty);
            set => SetValue(IsLockedProperty, value);
        }

        /// <summary>
        /// Highlights the card with a pink glow + border when the underlying
        /// feature is enabled in settings.
        /// </summary>
        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        /// <summary>
        /// ID of the entry in <see cref="Services.HelpContentService"/> whose rich
        /// tooltip should be shown when hovering the "?" icon. Null/unknown IDs
        /// hide the icon entirely.
        /// </summary>
        public string? HelpSectionId
        {
            get => (string?)GetValue(HelpSectionIdProperty);
            set => SetValue(HelpSectionIdProperty, value);
        }

        public event RoutedEventHandler Click
        {
            add => AddHandler(ClickEvent, value);
            remove => RemoveHandler(ClickEvent, value);
        }

        public FeatureCard()
        {
            InitializeComponent();
            ApplyLockState();
            // Build the help tooltip once the card is in the visual tree so that
            // FindResource can walk up into the owning Window's resources where
            // HelpButtonStyle / HelpTooltipStyle / PinkBrush live.
            Loaded += (_, _) => RefreshHelpTooltip();
        }

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FeatureCard c) c.TxtTitle.Text = e.NewValue as string ?? "";
        }

        private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FeatureCard c) return;
            var src = e.NewValue as ImageSource;
            c.ImgIcon.Source = src;
            if (src != null)
            {
                c.ImgIcon.Visibility = Visibility.Visible;
                c.GlyphHost.Visibility = Visibility.Collapsed;
            }
            else if (!string.IsNullOrEmpty(c.Glyph))
            {
                c.ImgIcon.Visibility = Visibility.Collapsed;
                c.GlyphHost.Visibility = Visibility.Visible;
            }
        }

        private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FeatureCard c) return;
            var glyph = e.NewValue as string;
            c.TxtGlyph.Text = glyph ?? "";
            if (c.Icon == null && !string.IsNullOrEmpty(glyph))
            {
                c.GlyphHost.Visibility = Visibility.Visible;
                c.ImgIcon.Visibility = Visibility.Collapsed;
            }
            else if (string.IsNullOrEmpty(glyph))
            {
                c.GlyphHost.Visibility = Visibility.Collapsed;
            }
        }

        private static void OnLockStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FeatureCard c) c.ApplyLockState();
        }

        private static void OnActiveStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FeatureCard c) c.ApplyActiveState();
        }

        private static void OnHelpSectionIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FeatureCard c) c.RefreshHelpTooltip();
        }

        private void RefreshHelpTooltip()
        {
            var id = HelpSectionId;
            if (string.IsNullOrWhiteSpace(id) || !Services.HelpContentService.HasContent(id))
            {
                BtnHelp.Visibility = Visibility.Collapsed;
                BtnHelp.ToolTip = null;
                return;
            }
            // If we haven't been added to the visual tree yet, resource lookup
            // will fail. Wait until Loaded rebuilds us.
            if (!IsLoaded)
            {
                BtnHelp.Visibility = Visibility.Visible;
                return;
            }
            BtnHelp.ToolTip = Services.HelpTooltipBuilder.Build(
                Services.HelpContentService.GetContent(id), this);
            BtnHelp.Visibility = Visibility.Visible;
        }

        private void ApplyLockState()
        {
            if (IsLocked)
            {
                LockedOverlay.Visibility = Visibility.Visible;
                TxtLockLabel.Text = LockLevel > 0 ? $"Lvl {LockLevel}" : "Locked";
                ContentRoot.Opacity = 0.35;
            }
            else
            {
                LockedOverlay.Visibility = Visibility.Collapsed;
                ContentRoot.Opacity = 1.0;
            }
            ApplyActiveState();
        }

        private void ApplyActiveState()
        {
            // Active state is suppressed while the card is locked — a locked feature
            // can't really be "on" even if the underlying setting is true.
            var showActive = IsActive && !IsLocked;
            ActiveBorder.Visibility = showActive ? Visibility.Visible : Visibility.Collapsed;
            ActiveGlow.Opacity = showActive ? 0.55 : 0.0;
        }

        private void OnClick(object sender, MouseButtonEventArgs e)
        {
            // Swallow clicks that originate inside the help button so the user
            // can hover/click the "?" without also opening the feature popup.
            if (e.OriginalSource is DependencyObject src && IsDescendantOf(src, BtnHelp))
                return;
            RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            // Prevent the help click from also opening the feature popup.
            e.Handled = true;
        }

        private static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor)
        {
            var current = node;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor)) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }
    }
}
