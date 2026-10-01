using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// The Super strip inside a feature panel, right under its header: the BASIC sign (only while
    /// locked), "Super &lt;name&gt;" and its one-line twist, the weekly preview corner and the
    /// <see cref="SuperSwitch"/>. Preview corner, free accounts only (mockup `#botSlot`):
    /// this week's effect, unused = "Try it, 10 s"; running = a mint countdown ring;
    /// used = "Used. Back Monday" + Get Basic; any other effect = Get Basic.
    /// Effect behaviour lives in the effect lanes; this strip only reads <see cref="SuperAccess"/>.
    /// </summary>
    public sealed class SuperRow : Border
    {
        private static readonly Color Mint = Color.FromRgb(0x5F, 0xFF, 0xD0);
        private static readonly Color Gold = Color.FromRgb(0xFF, 0xCF, 0x6B);
        private const double RingR = 13, RingCirc = 2 * Math.PI * RingR;

        public static readonly DependencyProperty EffectProperty = DependencyProperty.Register(
            nameof(Effect), typeof(SuperEffect), typeof(SuperRow),
            new PropertyMetadata(SuperEffect.FlickerDeck, (d, _) => ((SuperRow)d).OnEffectChanged()));

        public SuperEffect Effect
        {
            get => (SuperEffect)GetValue(EffectProperty);
            set => SetValue(EffectProperty, value);
        }

        private readonly Border _signHost = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        private readonly TierBadge _sign = new() { Tier = 1, MaxWidthOverride = 92 };
        private readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        private readonly TextBlock _twist = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Opacity = 0.72, Foreground = Brushes.White, Margin = new Thickness(0, 2, 0, 0) };
        private readonly Border _weekTag;
        private readonly StackPanel _corner = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        private readonly SuperSwitch _switch = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly Path _ringArc;
        private readonly TextBlock _ringText = new() { FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Mint), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly Grid _ring;
        private DispatcherTimer? _ringTimer;

        public SuperRow()
        {
            CornerRadius = new CornerRadius(14);
            Padding = new Thickness(14, 10, 14, 10);
            Margin = new Thickness(0, 0, 0, 10);
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x14, 0x0C, 0x26));
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x4F, 0xA3));
            BorderThickness = new Thickness(1.5);

            _sign.HorizontalAlignment = HorizontalAlignment.Left;
            _sign.VerticalAlignment = VerticalAlignment.Center;
            _signHost.Child = _sign;

            _weekTag = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(7, 2, 7, 3),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(0x26, Mint.R, Mint.G, Mint.B)),
                BorderBrush = new SolidColorBrush(Mint),
                BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = Loc.Get("super_free_week"), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Mint) },
            };

            _ringArc = new Path
            {
                Stroke = new SolidColorBrush(Mint), StrokeThickness = 3,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeDashCap = PenLineCap.Round,
                Data = new EllipseGeometry(new Point(16, 16), RingR, RingR),
                StrokeDashArray = new DoubleCollection { RingCirc / 3, RingCirc / 3 },
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(-90),
            };
            _ring = new Grid { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center };
            _ring.Children.Add(new Ellipse { Width = 2 * RingR + 3, Height = 2 * RingR + 3, Stroke = new SolidColorBrush(Color.FromRgb(0x33, 0x26, 0x4F)), StrokeThickness = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            _ring.Children.Add(_ringArc);
            _ring.Children.Add(_ringText);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(_title);
            text.Children.Add(_twist);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_signHost, 0);
            Grid.SetColumn(text, 1);
            Grid.SetColumn(_corner, 2);
            Grid.SetColumn(_switch, 3);
            grid.Children.Add(_signHost);
            grid.Children.Add(text);
            grid.Children.Add(_corner);
            grid.Children.Add(_switch);
            Child = grid;

            Loaded += (_, _) =>
            {
                SuperAccess.HookTierEvents();
                SuperAccess.Changed += OnAccessChanged;
                SuperPreview.StateChanged += OnPreviewChanged;
                SuperSwitch.LockedPoke += OnLockedPoke;
                Refresh();
            };
            Unloaded += (_, _) =>
            {
                SuperAccess.Changed -= OnAccessChanged;
                SuperPreview.StateChanged -= OnPreviewChanged;
                SuperSwitch.LockedPoke -= OnLockedPoke;
                StopRing();
            };
        }

        private void OnEffectChanged()
        {
            _switch.Effect = Effect;
            Refresh();
        }

        private void OnAccessChanged(SuperEffect e) { if (e == Effect) Ui(Refresh); }

        private void OnPreviewChanged() => Ui(Refresh);

        private void OnLockedPoke(SuperEffect e)
        {
            if (e != Effect || _signHost.Visibility != Visibility.Visible) return;
            Ui(() => SuperSwitch.Shake(_signHost));
        }

        private void Ui(Action a)
        {
            if (Dispatcher.CheckAccess()) a();
            else Dispatcher.BeginInvoke(a);
        }

        /// <summary>Repaint every part from <see cref="SuperAccess"/> and <see cref="SuperPreview"/>.</summary>
        public void Refresh()
        {
            var effect = Effect;
            string name = SuperNames.Name(effect);
            _title.Text = Loc.GetF("super_switch_name", name);
            _twist.Text = Loc.Get("super_twist_" + effect.ToString().ToLowerInvariant());

            bool paid = TierGate.HasPremium;
            bool trying = SuperPreview.Trying == effect;
            bool locked = !SuperAccess.IsUnlocked(effect);
            bool thisWeek = !paid && SuperPreview.ThisWeek == effect;

            _signHost.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
            _switch.Refresh(animate: false);

            _corner.Children.Clear();
            StopRing();
            if (paid) return;

            if (thisWeek && !trying) _corner.Children.Add(_weekTag);
            if (trying)
            {
                _corner.Children.Add(_ring);
                StartRing();
            }
            else if (thisWeek && SuperPreview.CanTry(effect))
            {
                _corner.Children.Add(MakeButton(Loc.Get("super_try"), Mint, Color.FromRgb(0x08, 0x2A, 0x22), () =>
                {
                    if (!SuperPreview.TryStart(effect)) SuperSwitch.Shake(_switch);
                }));
            }
            else
            {
                if (thisWeek && SuperPreview.UsedThisWeek)
                {
                    _corner.Children.Add(new TextBlock
                    {
                        Text = Loc.Get("super_back_monday"), FontSize = 11, Opacity = 0.75, Foreground = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
                    });
                }
                _corner.Children.Add(MakeButton(Loc.Get("super_get_basic"), Gold, Color.FromRgb(0x2B, 0x1A, 0x00), () =>
                    TierGate.DemandPremium(Loc.GetF("super_switch_name", name))));
            }
        }

        private static Button MakeButton(string text, Color face, Color ink, Action click)
        {
            var b = new Button
            {
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = true,
                Template = ButtonTemplate(),
                Background = new SolidColorBrush(face),
                Foreground = new SolidColorBrush(ink),
                Content = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.Bold },
            };
            b.Click += (_, _) => click();
            return b;
        }

        private static ControlTemplate? _buttonTemplate;

        private static ControlTemplate ButtonTemplate()
        {
            if (_buttonTemplate != null) return _buttonTemplate;
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(999));
            border.SetValue(Border.PaddingProperty, new Thickness(12, 5, 12, 6));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            _buttonTemplate = new ControlTemplate(typeof(Button)) { VisualTree = border };
            _buttonTemplate.Seal();
            return _buttonTemplate;
        }

        private void StartRing()
        {
            UpdateRing();
            _ringTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(120) };
            _ringTimer.Tick += (_, _) => UpdateRing();
            _ringTimer.Start();
        }

        private void StopRing()
        {
            _ringTimer?.Stop();
            _ringTimer = null;
        }

        /// <summary>The ring empties as the try runs out (mockup: dashoffset = C * (1 - left / 10)).</summary>
        private void UpdateRing()
        {
            double left = SuperPreview.SecondsLeft;
            double frac = left / SuperPreviewRule.TrySeconds;
            double on = RingCirc * frac / _ringArc.StrokeThickness;
            double off = RingCirc / _ringArc.StrokeThickness;
            _ringArc.StrokeDashArray = new DoubleCollection { Math.Max(0.001, on), off };
            _ringText.Text = Loc.GetF("super_try_left", (int)Math.Ceiling(left));
        }
    }
}
