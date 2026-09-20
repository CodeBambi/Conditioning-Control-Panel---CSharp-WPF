using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls;

/// <summary>A balance display with an explicit reward cue. Balance refreshes never imply a reward.</summary>
public partial class SparkleWallet : UserControl
{
    private readonly DispatcherTimer _gainTimer;
    private readonly System.Windows.Shapes.Polygon[] _sparkles = new System.Windows.Shapes.Polygon[4];
    private int _balance;
    private long _rewardShown;
    private bool _listening;
    private bool _dismissedByWalletPress;
    private bool _dismissedByHelpPress;
    public event EventHandler? VisitRequested;

    public SparkleWallet()
    {
        InitializeComponent();
        MascotFace.Draw("^_^");
        for (var i = 0; i < _sparkles.Length; i++)
        {
            var sparkle = new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection { new(0, -5), new(3, 0), new(0, 5), new(-3, 0) },
                Fill = i % 2 == 0 ? Brushes.LightGoldenrodYellow : Brushes.HotPink,
                Opacity = 0, RenderTransform = new TranslateTransform()
            };
            Canvas.SetLeft(sparkle, 83 + i * 28); Canvas.SetTop(sparkle, 20 + i % 2 * 13);
            SparkleLayer.Children.Add(sparkle); _sparkles[i] = sparkle;
        }
        _gainTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1.6) };
        _gainTimer.Tick += (_, _) => ClearReward();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => { if (!IsVisible) { ClosePopup(); StopWave(); ClearReward(); } };
        WalletButton.Click += (_, _) =>
        {
            // StaysOpen=false closes on mouse-down outside the popup, before this Click arrives.
            if (_dismissedByWalletPress) { _dismissedByWalletPress = false; return; }
            RefreshText(); HelpPopup.IsOpen = false; InvitePopup.IsOpen = !InvitePopup.IsOpen;
        };
        InvitePopup.Closed += (_, _) =>
        {
            _dismissedByWalletPress = Mouse.LeftButton == MouseButtonState.Pressed && WalletButton.IsMouseOver;
        };
        WalletButton.MouseEnter += (_, _) => Wave();
        WalletButton.MouseLeave += (_, _) => { _dismissedByWalletPress = false; StopWave(); };
        WalletButton.GotKeyboardFocus += (_, _) => Wave();
        WalletButton.LostKeyboardFocus += (_, _) => StopWave();
        CloseButton.Click += (_, _) => { ClosePopup(); WalletButton.Focus(); };
        VisitButton.Click += (_, _) => { ClosePopup(); VisitRequested?.Invoke(this, EventArgs.Empty); };
        InvitePopup.Opened += (_, _) => VisitButton.Focus();
        InvitePopup.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            ClosePopup(); WalletButton.Focus(); e.Handled = true;
        };
        HelpButton.Click += (_, _) =>
        {
            if (_dismissedByHelpPress) { _dismissedByHelpPress = false; return; }
            RefreshText(); InvitePopup.IsOpen = false; HelpPopup.IsOpen = !HelpPopup.IsOpen;
        };
        HelpPopup.Closed += (_, _) =>
        {
            _dismissedByHelpPress = Mouse.LeftButton == MouseButtonState.Pressed && HelpButton.IsMouseOver;
        };
        HelpButton.MouseLeave += (_, _) => _dismissedByHelpPress = false;
        HelpLaterButton.Click += (_, _) => { ClosePopup(); HelpButton.Focus(); };
        HelpVisitButton.Click += (_, _) => { ClosePopup(); VisitRequested?.Invoke(this, EventArgs.Empty); };
        HelpPopup.Opened += (_, _) => HelpVisitButton.Focus();
        HelpPopup.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            ClosePopup(); HelpButton.Focus(); e.Handled = true;
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_listening) { LocalizationManager.Instance.LanguageChanged += OnLanguageChanged; _listening = true; }
        RefreshText();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_listening) { LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged; _listening = false; }
        ClosePopup(); StopWave(); ClearReward();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshText();

    /// <summary>Call for every balance update, including login, restore, spending and rewards.</summary>
    public void SetBalance(int balance)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => SetBalance(balance))); return; }
        _balance = Math.Max(0, balance);
        BalanceText.Text = _balance.ToString("N0", CultureInfo.CurrentCulture);
        AutomationProperties.SetName(WalletButton, Loc.GetF("sparkle_wallet_balance", BalanceText.Text));
    }

    /// <summary>Call only after a real local SP award. Never call from balance/property-change handlers.</summary>
    public void ShowReward(int amount)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ShowReward(amount))); return; }
        if (amount <= 0 || !IsLoaded || !IsVisible) return;
        _rewardShown = Math.Min(int.MaxValue, _rewardShown + amount);
        GainText.Text = Loc.GetF("sparkle_wallet_gain", _rewardShown.ToString("N0", CultureInfo.CurrentCulture));
        GainBadge.Visibility = Visibility.Visible;
        GainShift.BeginAnimation(TranslateTransform.YProperty, null);
        GainShift.Y = 0;
        if (AnimateInteractions)
        {
            var rise = new DoubleAnimation(MotionFx.Level == MotionLevel.Reduced ? 3 : 8, 0, TimeSpan.FromMilliseconds(240))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
            Timeline.SetDesiredFrameRate(rise, 24);
            GainShift.BeginAnimation(TranslateTransform.YProperty, rise);
        }
        RewardSparkles();
        _gainTimer.Stop(); _gainTimer.Start();
    }

    public void RefreshText()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(RefreshText)); return; }
        WalletLabel.Text = Loc.Get("sparkle_wallet_label");
        // One line on hover for both the count and the help circle; the card carries the rest.
        var hint = Loc.Get("sparkle_help_tooltip");
        WalletButton.ToolTip = hint;
        HelpButton.ToolTip = hint;
        AutomationProperties.SetName(HelpButton, Loc.Get("sparkle_help_title"));
        HelpTitle.Text = Loc.Get("sparkle_help_title");
        HelpIntro.Text = Loc.Get("sparkle_help_intro");
        HelpHead1.Text = Loc.Get("sparkle_help_backroom_title"); HelpBody1.Text = Loc.Get("sparkle_help_backroom_body");
        HelpHead2.Text = Loc.Get("sparkle_help_win_title"); HelpBody2.Text = Loc.Get("sparkle_help_win_body");
        HelpHead3.Text = Loc.Get("sparkle_help_pictures_title"); HelpBody3.Text = Loc.Get("sparkle_help_pictures_body");
        HelpHead4.Text = Loc.Get("sparkle_help_honest_title"); HelpBody4.Text = Loc.Get("sparkle_help_honest_body");
        HelpVisitButton.Content = Loc.Get("sparkle_help_visit");
        HelpLaterButton.Content = Loc.Get("sparkle_help_later");
        InviteText.Text = Loc.Get("sparkle_wallet_invite");
        VisitButton.Content = Loc.Get("sparkle_wallet_visit");
        CloseButton.ToolTip = Loc.Get("sparkle_wallet_close");
        AutomationProperties.SetName(CloseButton, Loc.Get("sparkle_wallet_close"));
        SetBalance(_balance);
        if (_rewardShown > 0) GainText.Text = Loc.GetF("sparkle_wallet_gain", _rewardShown.ToString("N0", CultureInfo.CurrentCulture));
    }

    public void ClosePopup() { InvitePopup.IsOpen = false; HelpPopup.IsOpen = false; }

    private static bool AnimateInteractions => MotionFx.AllowTransitions && PerformanceProfile.CurrentTier != PerformanceTier.Performance;

    private void Wave()
    {
        StopWave();
        if (!AnimateInteractions) return;
        var scale = MotionFx.Level == MotionLevel.Reduced ? .45 : 1;
        var wave = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(620), FillBehavior = FillBehavior.Stop };
        foreach (var (ms, angle) in new[] { (0, 0d), (100, -17d), (220, 13d), (340, -13d), (470, 8d), (620, 0d) })
            wave.KeyFrames.Add(new EasingDoubleKeyFrame(angle * scale, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
        Timeline.SetDesiredFrameRate(wave, 24);
        ArmTurn.BeginAnimation(RotateTransform.AngleProperty, wave);
    }

    private void StopWave()
    {
        ArmTurn.BeginAnimation(RotateTransform.AngleProperty, null);
        ArmTurn.Angle = 0;
    }

    private void RewardSparkles()
    {
        StopRewardSparkles();
        if (!AnimateInteractions) return;
        var pulse = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(280), FillBehavior = FillBehavior.Stop };
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(MotionFx.Level == MotionLevel.Reduced ? 1.025 : 1.09,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(95)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280)),
            new SineEase { EasingMode = EasingMode.EaseInOut }));
        Timeline.SetDesiredFrameRate(pulse, 24);
        BalancePulse.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        BalancePulse.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        if (!MotionFx.AllowParticles) return;
        for (var i = 0; i < _sparkles.Length; i++)
        {
            var sparkle = _sparkles[i];
            var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(520), FillBehavior = FillBehavior.Stop };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70 + i * 20))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520))));
            var rise = new DoubleAnimation(5, -12 - i % 2 * 6, TimeSpan.FromMilliseconds(520)) { FillBehavior = FillBehavior.Stop };
            Timeline.SetDesiredFrameRate(fade, 24); Timeline.SetDesiredFrameRate(rise, 24);
            sparkle.BeginAnimation(OpacityProperty, fade);
            ((TranslateTransform)sparkle.RenderTransform).BeginAnimation(TranslateTransform.YProperty, rise);
        }
    }

    private void StopRewardSparkles()
    {
        BalancePulse.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        BalancePulse.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        foreach (var sparkle in _sparkles)
        {
            sparkle.BeginAnimation(OpacityProperty, null);
            ((TranslateTransform)sparkle.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
        }
    }

    private void ClearReward()
    {
        _gainTimer.Stop(); _rewardShown = 0;
        StopRewardSparkles();
        GainShift.BeginAnimation(TranslateTransform.YProperty, null);
        GainShift.Y = 0; GainBadge.Visibility = Visibility.Collapsed;
    }
}
