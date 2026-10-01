using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// The gold V2 box at the foot of a feature page (owner, 2026-10-01). One card per feature: a
    /// small "v2" tag top left, the BASIC supporter sign top right, then the effect's quiet
    /// <see cref="SuperSwitch"/> with its name and one plain line, then <see cref="Body"/>: the
    /// effect's own options, or (Flashes, Bubble Pop) the Back Room v2 prize rows merged in.
    ///
    /// <para>States. LIT (Basic and up, or this week's 10 s try running): full gold, switch
    /// usable. DIM (free account): the Super part dims and the switch is locked; a click shakes it
    /// and shows the normal tier refusal. Free accounts keep the weekly corner: "Try it, 10 s" on
    /// this week's effect, a countdown ring while it runs, "Used. Back Monday" after, Get Basic
    /// otherwise. The <see cref="Body"/> is never dimmed by the box: a v2 prize row is lit or dim
    /// by its own ownership, not by the tier.</para>
    ///
    /// <para>The box reads only <see cref="SuperAccess"/> and <see cref="SuperPreview"/>. Nothing
    /// Super sits on a dashboard tile any more.</para>
    /// </summary>
    [ContentProperty(nameof(Body))]
    public sealed class SuperBox : Border
    {
        private static readonly Color Gold = Color.FromRgb(0xFF, 0xCF, 0x6B);
        private static readonly Color Mint = Color.FromRgb(0x5F, 0xFF, 0xD0);
        private const double RingR = 9, RingCirc = 2 * Math.PI * RingR;
        private const double DimOpacity = 0.5;

        public static readonly DependencyProperty EffectProperty = DependencyProperty.Register(
            nameof(Effect), typeof(SuperEffect), typeof(SuperBox),
            new PropertyMetadata(SuperEffect.FlickerDeck, (d, _) => ((SuperBox)d).OnEffectChanged()));

        public SuperEffect Effect
        {
            get => (SuperEffect)GetValue(EffectProperty);
            set => SetValue(EffectProperty, value);
        }

        public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
            nameof(Body), typeof(object), typeof(SuperBox),
            new PropertyMetadata(null, (d, e) => ((SuperBox)d).OnBodyChanged(e.NewValue)));

        /// <summary>The effect's own rows under the switch. Never dimmed by the box.</summary>
        public object? Body
        {
            get => GetValue(BodyProperty);
            set => SetValue(BodyProperty, value);
        }

        private readonly Border _v2Tag;
        private readonly Border _signHost = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        private readonly TierBadge _sign = new() { Tier = 1, MaxWidthOverride = 64 };
        private readonly SuperSwitch _switch = new() { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0) };
        private readonly TextBlock _title = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Gold) };
        private readonly TextBlock _twist = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Foreground = Brushes.White, Margin = new Thickness(0, 1, 0, 0) };
        private readonly TextBlock _baseOff = new() { FontSize = 11, FontStyle = FontStyles.Italic, Opacity = 0.6, Foreground = Brushes.White, Margin = new Thickness(0, 3, 0, 0) };
        private readonly StackPanel _superPart = new();
        private readonly StackPanel _corner = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 0, 0, 0) };
        private readonly ContentPresenter _body = new() { Margin = new Thickness(0, 8, 0, 0) };
        private readonly Path _ringArc;
        private readonly TextBlock _ringText = new() { FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Mint), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly Grid _ring;
        private DispatcherTimer? _ringTimer;

        public SuperBox()
        {
            CornerRadius = new CornerRadius(12);
            Padding = new Thickness(12, 8, 12, 10);
            Margin = new Thickness(0, 10, 0, 2);
            BorderThickness = new Thickness(1.2);

            _v2Tag = new Border
            {
                Padding = new Thickness(6, 1, 7, 2),
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x1A, 0x1A, 0x2E)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x8A)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = Loc.Get("badge_v2"), FontSize = 9, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x8A)),
                },
            };
            _signHost.Child = _sign;
            _sign.HorizontalAlignment = HorizontalAlignment.Right;

            var head = new Grid { Margin = new Thickness(0, 0, 0, 6), MinHeight = 22 };
            head.Children.Add(_v2Tag);
            head.Children.Add(_signHost);

            _ringArc = new Path
            {
                Stroke = new SolidColorBrush(Mint), StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeDashCap = PenLineCap.Round,
                Data = new EllipseGeometry(new Point(12, 12), RingR, RingR),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(-90),
            };
            _ring = new Grid { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
            _ring.Children.Add(new Ellipse { Width = 2 * RingR + 2, Height = 2 * RingR + 2, Stroke = new SolidColorBrush(Color.FromRgb(0x33, 0x26, 0x4F)), StrokeThickness = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            _ring.Children.Add(_ringArc);
            _ring.Children.Add(_ringText);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(_title);
            text.Children.Add(_twist);
            text.Children.Add(_baseOff);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_switch, 0);
            Grid.SetColumn(text, 1);
            Grid.SetColumn(_corner, 2);
            row.Children.Add(_switch);
            row.Children.Add(text);
            row.Children.Add(_corner);

            // The dim part: head and switch row. The corner (Try it) is a child of the row but
            // stays readable because the row only fades its text and switch, see Paint.
            _superPart.Children.Add(head);
            _superPart.Children.Add(row);

            var root = new StackPanel();
            root.Children.Add(_superPart);
            root.Children.Add(_body);
            Child = root;

            Loaded += (_, _) =>
            {
                SuperAccess.HookTierEvents();
                SuperAccess.HookBaseEvents();
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

            OnBodyChanged(null);
            Paint(lit: false);
        }

        /// <summary>The quiet switch inside the box (tests and hosts).</summary>
        public SuperSwitch Switch => _switch;

        /// <summary>True when the box draws lit (tier or a running try), false when dim.</summary>
        public bool IsLit { get; private set; }

        private void OnEffectChanged()
        {
            _switch.Effect = Effect;
            Refresh();
        }

        private void OnBodyChanged(object? body)
        {
            _body.Content = body;
            _body.Visibility = body == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnAccessChanged(SuperEffect e) { if (e == Effect) Ui(Refresh); }

        private void OnPreviewChanged() => Ui(Refresh);

        private void OnLockedPoke(SuperEffect e)
        {
            if (e != Effect || IsLit) return;
            Ui(() => SuperSwitch.Shake(_signHost));
        }

        private void Ui(Action a)
        {
            if (Dispatcher.CheckAccess()) a();
            else Dispatcher.BeginInvoke(a);
        }

        /// <summary>LIT or DIM: the gold border, the background and the Super part's fade.</summary>
        internal void Paint(bool lit)
        {
            IsLit = lit;
            BorderBrush = new SolidColorBrush(Color.FromArgb(lit ? (byte)0xB3 : (byte)0x4D, Gold.R, Gold.G, Gold.B));
            Background = new SolidColorBrush(Color.FromArgb(lit ? (byte)0x1C : (byte)0x0C, Gold.R, Gold.G, Gold.B));
            double o = lit ? 1.0 : DimOpacity;
            _title.Opacity = o;
            _twist.Opacity = 0.7 * o;
            _v2Tag.Opacity = o;
            _switch.Opacity = lit ? 1.0 : 0.75;
            // The supporter sign is what a free player is asked for, so it stays readable.
            _signHost.Opacity = lit ? 0.85 : 0.9;
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
            bool unlocked = SuperAccess.IsUnlocked(effect);
            bool thisWeek = !paid && SuperPreview.ThisWeek == effect;
            bool baseOn = SuperAccess.IsBaseOn(effect);

            Paint(lit: unlocked);
            _baseOff.Text = Loc.Get("super_base_off");
            // Only worth saying when the add-on would otherwise run: a dim box has its own story.
            _baseOff.Visibility = !baseOn && (trying || (unlocked && SuperAccess.IsSwitchedOn(effect)))
                ? Visibility.Visible : Visibility.Collapsed;
            _switch.Refresh(animate: false);

            _corner.Children.Clear();
            StopRing();
            if (paid && !trying) return;

            if (trying)
            {
                _corner.Children.Add(_ring);
                StartRing();
            }
            else if (thisWeek && SuperPreview.CanTry(effect) && baseOn)
            {
                _corner.Children.Add(MakeChip(Loc.Get("super_try"), Mint, () =>
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
                        Text = Loc.Get("super_back_monday"), FontSize = 10.5, Opacity = 0.65, Foreground = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
                    });
                }
                _corner.Children.Add(MakeChip(Loc.Get("super_get_basic"), Gold, () =>
                    TierGate.DemandPremium(Loc.GetF("super_switch_name", name))));
            }
        }

        /// <summary>A small outlined chip in the box's own colours: tinted fill, thin ring, coloured text.</summary>
        private static Button MakeChip(string text, Color ink, Action click)
        {
            var b = new Button
            {
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = true,
                Template = ChipTemplate(),
                Background = new SolidColorBrush(Color.FromArgb(0x24, ink.R, ink.G, ink.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xB3, ink.R, ink.G, ink.B)),
                Foreground = new SolidColorBrush(ink),
                Content = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold },
            };
            b.Click += (_, _) => click();
            return b;
        }

        private static ControlTemplate? _chipTemplate;

        private static ControlTemplate ChipTemplate()
        {
            if (_chipTemplate != null) return _chipTemplate;
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(999));
            border.SetValue(Border.PaddingProperty, new Thickness(9, 2, 9, 3));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            var rel = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent);
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = rel });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = rel });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            _chipTemplate = new ControlTemplate(typeof(Button)) { VisualTree = border };
            _chipTemplate.Seal();
            return _chipTemplate;
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
