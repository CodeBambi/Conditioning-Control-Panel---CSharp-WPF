using System;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

// Namespace is Controls (not Controls.Header) so MainShellWindow.axaml reaches it through its
// existing fx: prefix: Avalonia XAML allows xmlns declarations on the root element only.
namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// PORTED from WPF 7.1.5 Controls/SparkleWallet.xaml(.cs) (nav polish wave 6: 29 x 138, the
    /// "?" on the pill). The header's SP pill: the croupier EMI, a "Sparkle Points" label and the
    /// balance; hover waves her arm, a credit pops the balance and throws four sparkles, a click
    /// opens EMI's invite to the Back Room, the "?" opens what sparkles are for.
    ///
    /// <para>Built in code (no axaml) so the header owns one control, as WPF did. Two ponytails:
    /// the face is a mono TextBlock in the visor, as EmiDeskWindow's own placeholder is (the WPF
    /// EmiFace renderer is not ported), and the sprite PNGs load from avares only once the csproj
    /// packs Assets/emi/wallet_croupier_*.png (seam request); until then a gold coin stands in.</para>
    /// </summary>
    public sealed class SparkleWallet : UserControl
    {
        public const double PillWidth = 138;
        public const double PillHeight = 29;

        private static readonly IBrush Lilac = Brush("#B982B5");
        private static readonly IBrush Gold = Brush("#FFD580");

        private readonly Button _wallet;
        private readonly Border _outline;
        private readonly TextBlock _label = new() { Foreground = Brush("#D9BCD9"), FontSize = 8, LineHeight = 9, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _balanceText = new() { Text = "0", Foreground = Brush("#FFF0CA"), FontSize = 15, FontWeight = FontWeight.Bold, LineHeight = 17 };
        private readonly StackPanel _balanceRow;
        private readonly ScaleTransform _balancePulse = new(1, 1);
        private readonly RotateTransform _armTurn = new();
        private readonly Control _arm;
        private readonly Canvas _sparkleLayer = new() { IsHitTestVisible = false, ClipToBounds = false };
        private readonly Path[] _sparkles = new Path[4];
        private readonly Border _gainBadge;
        private readonly TextBlock _gainText = new() { Foreground = Brush("#FFE8A9"), FontSize = 10, FontWeight = FontWeight.Bold };
        private readonly TranslateTransform _gainShift = new();
        private readonly Popup _invitePopup, _helpPopup;
        private readonly TextBlock _inviteText = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#F7E5F3"), FontSize = 14, Margin = new Thickness(0, 8, 0, 15) };
        private readonly Button _visit, _close, _help, _helpLater, _helpVisit;
        private readonly TextBlock _helpTitle = new() { Foreground = Brush("#FF9BCD"), FontWeight = FontWeight.Bold, FontSize = 15, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _helpIntro = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#F7E5F3"), FontSize = 13, Margin = new Thickness(0, 8, 0, 10) };
        private readonly TextBlock[] _helpHeads = new TextBlock[4];
        private readonly TextBlock[] _helpBodies = new TextBlock[4];
        private readonly DispatcherTimer _gainTimer;
        private int _balance;
        private long _rewardShown;
        private bool _listening;

        public event EventHandler? VisitRequested;

        /// <summary>The balance as shown (tests read it).</summary>
        public string BalanceShown => _balanceText.Text ?? "";

        public SparkleWallet()
        {
            Width = PillWidth;
            Height = PillHeight;
            UseLayoutRounding = true;

            // ---- the pill: croupier EMI | label over balance ----
            var sprite = new Grid { Width = 954, Height = 995 };
            var body = LoadSprite("wallet_croupier_body.png");
            var arm = LoadSprite("wallet_croupier_arm.png");
            if (body != null && arm != null)
            {
                sprite.Children.Add(new Image { Source = body, Stretch = Stretch.Fill });
                _arm = new Image { Source = arm, Stretch = Stretch.Fill };
            }
            else
            {
                // Stand-in until the sprites are packed: a gold coin with an arm-shaped glint.
                sprite.Children.Add(new Ellipse { Margin = new Thickness(60), Fill = Gold, Stroke = Brush("#FFF0C5"), StrokeThickness = 40 });
                _arm = new Path
                {
                    Data = Geometry.Parse("M 700,300 L 820,120 L 860,160 L 760,340 Z"),
                    Fill = Brush("#FFF0C5"),
                };
            }
            _arm.RenderTransformOrigin = new RelativePoint(0.848, 0.676, RelativeUnit.Relative);
            _arm.RenderTransform = _armTurn;
            sprite.Children.Add(_arm);
            // The visor glass rect, measured off the sprite (302,279 425x385) and inset 18 px.
            sprite.Children.Add(new TextBlock
            {
                Text = "^_^",
                Foreground = Brush("#FF9BCD"),
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                FontWeight = FontWeight.Bold,
                FontSize = 150,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(320, 297, 0, 0),
                Width = 389,
                Height = 349,
                TextAlignment = TextAlignment.Center,
                Padding = new Thickness(0, 90, 0, 0),
            });
            var mascot = new Viewbox { Width = 30, Height = 28, IsHitTestVisible = false, Child = sprite };

            var gem = new Path
            {
                Data = Geometry.Parse("M0,8 L6,0 12,8 6,16 Z M0,8 L12,8 M6,0 L4,8 6,16 8,8 Z"),
                Width = 8, Height = 11, Stretch = Stretch.Fill, Fill = Gold, Stroke = Brush("#FFF0C5"), StrokeThickness = 0.6,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
            };
            _balanceRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = _balancePulse,
                Children = { gem, _balanceText },
            };
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), Children = { _label, _balanceRow } };
            Grid.SetColumn(words, 1);
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("31,*"), Children = { mascot, words } };

            _outline = new Border
            {
                Background = Brush("#271A32"),
                BorderBrush = Lilac,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(2, 0, 23, 0),
            };
            _wallet = new Button
            {
                Name = "WalletButton",
                Background = Brushes.Transparent,
                BorderThickness = default,
                Padding = default,
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Content = content,
                Template = new FuncControlTemplate<Button>((b, ns) =>
                {
                    _outline.Child = new ContentPresenter
                    {
                        Name = "PART_ContentPresenter",
                        [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty],
                    }.RegisterInNameScope(ns);
                    return _outline;
                }),
            };

            // ---- reward sparkles, gain badge ----
            for (var i = 0; i < _sparkles.Length; i++)
            {
                var sparkle = new Path
                {
                    Data = Geometry.Parse("M0,-4 L2.4,0 L0,4 L-2.4,0 Z"),
                    Fill = i % 2 == 0 ? Brushes.LightGoldenrodYellow : Brushes.HotPink,
                    Opacity = 0,
                    RenderTransform = new TranslateTransform(),
                };
                Canvas.SetLeft(sparkle, 50 + i * 18);
                Canvas.SetTop(sparkle, 13 + i % 2 * 8);
                _sparkleLayer.Children.Add(sparkle);
                _sparkles[i] = sparkle;
            }
            _gainBadge = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -8, 24, 0),
                Background = Brush("#34223F"),
                BorderBrush = Gold,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5, 1),
                IsVisible = false,
                IsHitTestVisible = false,
                RenderTransform = _gainShift,
                Child = _gainText,
            };

            // ---- the invite card ----
            _close = PlainButton("×", Brush("#E9CCE9"), 19);
            _close.HorizontalAlignment = HorizontalAlignment.Right;
            _close.Width = 26;
            _close.Height = 26;
            _visit = FilledButton();
            _invitePopup = Card(_wallet, 310, new StackPanel
            {
                Children =
                {
                    new Grid { Children = { new TextBlock { Text = "EMI", Foreground = Brush("#FF9BCD"), FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center }, _close } },
                    _inviteText,
                    _visit,
                },
            });

            // ---- the "?" and its card ----
            _help = new Button
            {
                Name = "HelpButton",
                Width = 17, Height = 17,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
                Background = Brush("#33201F2E"),
                BorderBrush = Lilac,
                Foreground = Brush("#F2DDF0"),
            };
            if (Application.Current?.TryFindResource("HelpButtonStyle", out var helpTheme) == true && helpTheme is ControlTheme ct)
                _help.Theme = ct;
            _helpLater = PlainButton("", Brush("#E9CCE9"), 13);
            _helpLater.Padding = new Thickness(12, 8);
            _helpLater.BorderBrush = Brush("#C996C3");
            _helpLater.BorderThickness = new Thickness(1);
            _helpLater.Margin = new Thickness(0, 0, 8, 0);
            _helpVisit = FilledButton();
            var helpStack = new StackPanel { Children = { _helpTitle, _helpIntro } };
            for (int i = 0; i < 4; i++)
            {
                _helpHeads[i] = new TextBlock { Foreground = Gold, FontWeight = FontWeight.SemiBold, FontSize = 13 };
                _helpBodies[i] = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#E9D5E6"), FontSize = 12.5, Margin = new Thickness(0, 2, 0, i == 3 ? 12 : 9) };
                helpStack.Children.Add(_helpHeads[i]);
                helpStack.Children.Add(_helpBodies[i]);
            }
            helpStack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { _helpLater, _helpVisit } });
            _helpPopup = Card(_wallet, 360, helpStack);

            Content = new Grid { Children = { _wallet, _sparkleLayer, _gainBadge, _invitePopup, _help, _helpPopup } };

            _gainTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            _gainTimer.Tick += (_, _) => ClearReward();

            _wallet.Click += (_, _) => { RefreshText(); _helpPopup.IsOpen = false; _invitePopup.IsOpen = !_invitePopup.IsOpen; };
            _wallet.PointerEntered += (_, _) => Wave();
            _wallet.PointerExited += (_, _) => StopWave();
            _wallet.GotFocus += (_, _) => Wave();
            _wallet.LostFocus += (_, _) => StopWave();
            _close.Click += (_, _) => { ClosePopup(); _wallet.Focus(); };
            _visit.Click += (_, _) => { ClosePopup(); VisitRequested?.Invoke(this, EventArgs.Empty); };
            _help.Click += (_, _) => { RefreshText(); _invitePopup.IsOpen = false; _helpPopup.IsOpen = !_helpPopup.IsOpen; };
            _helpLater.Click += (_, _) => { ClosePopup(); _help.Focus(); };
            _helpVisit.Click += (_, _) => { ClosePopup(); VisitRequested?.Invoke(this, EventArgs.Empty); };
            _invitePopup.Opened += (_, _) => _visit.Focus();
            _helpPopup.Opened += (_, _) => _helpVisit.Focus();
            RefreshText();
        }

        protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_listening) { LocalizationManager.Instance.LanguageChanged += OnLanguageChanged; _listening = true; }
            RefreshText();
        }

        protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            if (_listening) { LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged; _listening = false; }
            ClosePopup(); StopWave(); ClearReward();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && !IsVisible) { ClosePopup(); StopWave(); ClearReward(); }
        }

        private void OnLanguageChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshText);

        public void SetBalance(int balance)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => SetBalance(balance)); return; }
            _balance = Math.Max(0, balance);
            _balanceText.Text = _balance.ToString("N0", CultureInfo.CurrentCulture);
            AutomationProperties.SetName(_wallet, Loc.GetF("sparkle_wallet_balance", _balanceText.Text));
        }

        public void ShowReward(int amount)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => ShowReward(amount)); return; }
            if (amount <= 0 || !IsEffectivelyVisible) return;
            _rewardShown = Math.Min(int.MaxValue, _rewardShown + amount);
            _gainText.Text = Loc.GetF("sparkle_wallet_gain", _rewardShown.ToString("N0", CultureInfo.CurrentCulture));
            _gainBadge.IsVisible = true;
            _gainShift.Y = 0;
            if (AnimateInteractions)
                Run(_gainShift, 240, new QuadraticEaseOut(),
                    (0, TranslateTransform.YProperty, Env.Level == MotionLevel.Reduced ? 3.0 : 8.0),
                    (1, TranslateTransform.YProperty, 0.0));
            RewardSparkles();
            _gainTimer.Stop(); _gainTimer.Start();
        }

        public void RefreshText()
        {
            _label.Text = Loc.Get("sparkle_wallet_label");
            var hint = Loc.Get("sparkle_help_tooltip");
            ToolTip.SetTip(_wallet, hint);
            ToolTip.SetTip(_help, hint);
            AutomationProperties.SetName(_help, Loc.Get("sparkle_help_title"));
            _help.Content = "?";
            _helpTitle.Text = Loc.Get("sparkle_help_title");
            _helpIntro.Text = Loc.Get("sparkle_help_intro");
            string[] heads = { "backroom", "win", "pictures", "honest" };
            for (int i = 0; i < 4; i++)
            {
                _helpHeads[i].Text = Loc.Get($"sparkle_help_{heads[i]}_title");
                _helpBodies[i].Text = Loc.Get($"sparkle_help_{heads[i]}_body");
            }
            _helpVisit.Content = Loc.Get("sparkle_help_visit");
            _helpLater.Content = Loc.Get("sparkle_help_later");
            _inviteText.Text = Loc.Get("sparkle_wallet_invite");
            _visit.Content = Loc.Get("sparkle_wallet_visit");
            ToolTip.SetTip(_close, Loc.Get("sparkle_wallet_close"));
            AutomationProperties.SetName(_close, Loc.Get("sparkle_wallet_close"));
            SetBalance(_balance);
            if (_rewardShown > 0) _gainText.Text = Loc.GetF("sparkle_wallet_gain", _rewardShown.ToString("N0", CultureInfo.CurrentCulture));
        }

        public void ClosePopup() { _invitePopup.IsOpen = false; _helpPopup.IsOpen = false; }

        private static bool AnimateInteractions => Env.AllowTransitions && Env.CurrentTier != PerformanceTier.Performance;

        private void Wave()
        {
            StopWave();
            if (!AnimateInteractions) return;
            var scale = Env.Level == MotionLevel.Reduced ? .45 : 1;
            var anim = new Animation { Duration = TimeSpan.FromMilliseconds(620), Easing = new SineEaseInOut() };
            foreach (var (ms, angle) in new[] { (0, 0d), (100, -17d), (220, 13d), (340, -13d), (470, 8d), (620, 0d) })
                anim.Children.Add(new KeyFrame { Cue = new Cue(ms / 620.0), Setters = { new Setter(RotateTransform.AngleProperty, angle * scale) } });
            _waveRun = anim.RunAsync(_armTurn);
        }

        private System.Threading.Tasks.Task? _waveRun;

        private void StopWave() => _armTurn.Angle = 0;

        private void RewardSparkles()
        {
            if (!AnimateInteractions) return;
            var peak = Env.Level == MotionLevel.Reduced ? 1.025 : 1.09;
            Run(_balancePulse, 280, new SineEaseInOut(),
                (0, ScaleTransform.ScaleXProperty, 1.0), (0.34, ScaleTransform.ScaleXProperty, peak), (1, ScaleTransform.ScaleXProperty, 1.0));
            Run(_balancePulse, 280, new SineEaseInOut(),
                (0, ScaleTransform.ScaleYProperty, 1.0), (0.34, ScaleTransform.ScaleYProperty, peak), (1, ScaleTransform.ScaleYProperty, 1.0));
            if (!Env.AllowParticles) return;
            for (var i = 0; i < _sparkles.Length; i++)
            {
                var s = _sparkles[i];
                var rise = -8 - i % 2 * 4;
                Run(s, 520, new LinearEasing(),
                    (0, OpacityProperty, 0.0), ((70 + i * 20) / 520.0, OpacityProperty, 1.0), (1, OpacityProperty, 0.0));
                Run((TranslateTransform)s.RenderTransform!, 520, new LinearEasing(),
                    (0, TranslateTransform.YProperty, 3.0), (1, TranslateTransform.YProperty, (double)rise));
            }
        }

        private void ClearReward()
        {
            _gainTimer.Stop();
            _rewardShown = 0;
            _gainShift.Y = 0;
            _gainBadge.IsVisible = false;
        }

        /// <summary>A one-shot keyframe run that leaves the property where WPF's FillBehavior.Stop did.</summary>
        private static void Run(Animatable target, int ms, Easing easing, params (double cue, AvaloniaProperty prop, double value)[] keys)
        {
            var anim = new Animation { Duration = TimeSpan.FromMilliseconds(ms), Easing = easing, FillMode = FillMode.None };
            foreach (var (cue, prop, value) in keys)
                anim.Children.Add(new KeyFrame { Cue = new Cue(cue), Setters = { new Setter(prop, value) } });
            _ = anim.RunAsync(target);
        }

        // ---- helpers ----

        private static Popup Card(Control target, double width, Control body) => new()
        {
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 7,
            IsLightDismissEnabled = true,
            Child = new Border
            {
                Width = width,
                Background = Brush("#24182E"),
                BorderBrush = Brush("#C996C3"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(17),
                Child = body,
            },
        };

        private static Button PlainButton(string text, IBrush fg, double size) => new()
        {
            Content = text,
            Background = Brushes.Transparent,
            BorderThickness = default,
            Foreground = fg,
            FontSize = size,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        private static Button FilledButton() => new()
        {
            Padding = new Thickness(12, 8),
            Background = Brush("#E493C3"),
            Foreground = Brush("#261329"),
            BorderThickness = default,
            FontWeight = FontWeight.SemiBold,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        private static IImage? LoadSprite(string file)
        {
            try
            {
                var uri = new Uri("avares://CCP.Avalonia/Resources/emi/" + file);
                return AssetLoader.Exists(uri) ? new Bitmap(AssetLoader.Open(uri)) : null;
            }
            catch { return null; }
        }

        private static IBrush Brush(string hex) => new ImmutableSolidColorBrush(Color.Parse(hex));
    }
}
