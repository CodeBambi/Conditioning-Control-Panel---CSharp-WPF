using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Services.Overlays;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;

using IModService = ConditioningControlPanel.IModService;
using IInteractionQueueService = ConditioningControlPanel.IInteractionQueueService;
using ILockCardService = ConditioningControlPanel.Core.Services.LockCard.ILockCardService;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the lock-card window.
/// </summary>
public partial class LockCardWindow : Window
{
    private readonly ILogger<LockCardWindow> _logger;


    private readonly string _phrase = string.Empty;
    private readonly int _requiredRepeats;
    private readonly bool _strictMode;
    private int _completedRepeats;
    private bool _isCompleted;
    private DispatcherTimer? _closeTimer;

    private readonly bool _isPrimary;

    private static readonly List<LockCardWindow> _allWindows = new();
    private static string _sharedInput = "";

    private static DateTime _startTime;
    private static int _totalErrors;
    private static int _totalCharsTyped;
    private static bool _isTest;
    private readonly IProgressionService _progression;
    private readonly IModService _mods;
    private readonly IInteractionQueueService _interactionQueue;
    private readonly ILockCardService _lockCard;

    public LockCardWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<LockCardWindow>>();
_progression = App.Services.GetRequiredService<IProgressionService>();
        _mods = App.Services.GetRequiredService<IModService>();
        _interactionQueue = App.Services.GetRequiredService<IInteractionQueueService>();
        _lockCard = App.Services.GetRequiredService<ILockCardService>();
    }

    public LockCardWindow(string phrase, int repeats, bool strictMode,
        ScreenInfo? screen = null, bool isPrimary = true) : this()
    {
        _phrase = phrase;
        _requiredRepeats = repeats;
        _strictMode = strictMode;
        _isPrimary = isPrimary;

        TxtPhrase.Text = phrase;
        UpdateProgress();

        if (_strictMode)
        {
            TxtStrict.Text = "STRICT";
        }
        TxtEscHint.Text = "Press ESC to close";

        PositionWindow(screen);

        if (!_isPrimary)
        {
            TxtInput.IsReadOnly = true;
            TxtInput.Focusable = false;
            TxtHint.Text = Loc.Get("lock_card_input_synced_secondary");
        }

        ApplyColors();
        _allWindows.Add(this);

        Closing += OnWindowClosing;
    }

    /// <summary>
    /// Check if any lock card window is currently open.
    /// </summary>
    public static bool IsAnyOpen() => _allWindows.Count > 0;

    /// <summary>
    /// Create lock card windows for all monitors.
    /// </summary>
    public static void ShowOnAllMonitors(string phrase, int repeats, bool strictMode, bool isTest = false)
    {
        var logger = App.Services.GetRequiredService<ILogger<LockCardWindow>>();
        _allWindows.Clear();
        _sharedInput = "";

        _startTime = DateTime.Now;
        _totalErrors = 0;
        _totalCharsTyped = 0;
        _isTest = isTest;

        var provider = App.Services?.GetService<IScreenProvider>();
        var screens = provider?.GetAllScreens();

        if (screens == null || screens.Count == 0)
        {
            App.Services?.GetRequiredService<ILogger<LockCardWindow>>().LogWarning("LockCardWindow: no screens available");
            return;
        }

        LockCardWindow? primaryWindow = null;

        foreach (var screen in screens)
        {
            var isPrimary = screen == screens[0];
            var window = new LockCardWindow(phrase, repeats, strictMode, screen, isPrimary);
            if (isPrimary)
            {
                primaryWindow = window;
            }
            window.Show();
        }

        primaryWindow?.Activate();
        primaryWindow?.TxtInput.Focus();
    }

    /// <summary>
    /// Force close all lock card windows.
    /// </summary>
    public static void ForceCloseAll()
    {
        var windowsToClose = new List<LockCardWindow>(_allWindows);
        _allWindows.Clear();

        foreach (var window in windowsToClose)
        {
            window._isCompleted = true;
            try { window.Close(); } catch { }
        }

        try
        {
            App.Services.GetRequiredService<IInteractionQueueService>().Complete("LockCard"); // TODO: use InteractionQueueService.InteractionType enum once ported.
        }
        catch { /* legacy service may not be present */ }
    }

    private void PositionWindow(ScreenInfo? screen)
    {
        try
        {
            var target = screen ?? App.Services?.GetService<IScreenProvider>()?.GetPrimaryScreen();
            if (target == null)
            {
                WindowState = WindowState.Maximized;
                return;
            }

            Position = new PixelPoint((int)target.Bounds.X, (int)target.Bounds.Y);
            Width = target.Bounds.Width / target.Scaling;
            Height = target.Bounds.Height / target.Scaling;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "LockCardWindow: failed to position window");
            WindowState = WindowState.Maximized;
        }
    }

    private void ApplyColors()
    {
        try
        {
            var settings = App.Services?.GetService<ISettingsService>()?.Current;

            var bgColor = ParseColor(settings?.LockCardBackgroundColor, Color.FromRgb(26, 26, 46));
            MainGrid.Background = new SolidColorBrush(Color.FromArgb(230, bgColor.R, bgColor.G, bgColor.B));
            CardBorder.Background = new SolidColorBrush(bgColor);

            var accentHex = _mods.GetAccentColorHex();
            var defaultAccent = ParseColor(accentHex, Color.FromRgb(255, 105, 180));

            var textColor = ParseColor(settings?.LockCardTextColor, defaultAccent);
            TxtPhrase.Foreground = new SolidColorBrush(textColor);
            TxtTitle.Foreground = new SolidColorBrush(textColor);

            var inputBgColor = ParseColor(settings?.LockCardInputBackgroundColor, Color.FromRgb(37, 37, 66));
            InputBorder.Background = new SolidColorBrush(inputBgColor);

            var inputTextColor = ParseColor(settings?.LockCardInputTextColor, Colors.White);
            TxtInput.Foreground = new SolidColorBrush(inputTextColor);

            var accentColor = ParseColor(settings?.LockCardAccentColor, defaultAccent);
            InputBorder.BorderBrush = new SolidColorBrush(accentColor);
            ProgressBar.Background = new SolidColorBrush(accentColor);

            CardBorder.BoxShadow = new BoxShadows(new BoxShadow
            {
                Color = Color.FromArgb(128, accentColor.R, accentColor.G, accentColor.B),
                Blur = 30,
                OffsetX = 0,
                OffsetY = 0,
                Spread = 0,
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to apply lock card colors");
        }
    }

    private static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return Color.Parse(hex);
        }
        catch
        {
            return fallback;
        }
    }

    private void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        if (_isPrimary)
        {
            Topmost = true;
            Activate();
            TxtInput.Focus();

            _logger?.LogInformation(
                "Lock Card shown - Phrase: {Phrase}, Repeats: {Repeats}, Strict: {Strict}, Monitors: {Count}",
                _phrase, _requiredRepeats, _strictMode, _allWindows.Count);
        }
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_isCompleted)
        {
            _logger?.LogInformation("Lock Card closed via ESC (strict={Strict})", _strictMode);
            ForceCloseAll();
        }

        // Prevent Alt+F4 in strict mode.
        if (_strictMode && e.Key == Key.F4 && e.KeyModifiers == KeyModifiers.Alt)
        {
            e.Handled = true;
        }

        // Prevent Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+A.
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            if (e.Key is Key.C or Key.V or Key.X or Key.A)
            {
                e.Handled = true;
            }
        }
    }

    private void TxtInput_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isCompleted || !_isPrimary) return;

        var input = TxtInput.Text ?? string.Empty;
        _sharedInput = input;
        _totalCharsTyped++;

        if (input.Length > 0)
        {
            var expectedPrefix = _phrase.Substring(0, Math.Min(input.Length, _phrase.Length));
            if (!string.Equals(input, expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _totalErrors++;
            }
        }

        SyncInputToAllWindows(input);

        if (string.Equals(input.Trim(), _phrase, StringComparison.OrdinalIgnoreCase))
        {
            _completedRepeats++;
            UpdateProgressOnAllWindows();

            TxtInput.Clear();
            _sharedInput = "";
            SyncInputToAllWindows("");

            PulseAllWindows();

            if (_completedRepeats >= _requiredRepeats)
            {
                CompleteAllWindows();
            }
            else
            {
                var hint = GetEncouragement();
                SetHintOnAllWindows(hint);
            }
        }
    }

    private void SyncInputToAllWindows(string input)
    {
        foreach (var window in _allWindows)
        {
            if (window != this && !window._isCompleted)
            {
                window.TxtInput.Text = input;
            }
        }
    }

    private void UpdateProgressOnAllWindows()
    {
        foreach (var window in _allWindows)
        {
            window._completedRepeats = _completedRepeats;
            window.UpdateProgress();
        }
    }

    private void PulseAllWindows()
    {
        foreach (var window in _allWindows)
        {
            window.PulseCard();
        }
    }

    private void SetHintOnAllWindows(string hint)
    {
        foreach (var window in _allWindows)
        {
            window.TxtHint.Text = hint;
            window.TxtHint.Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 100));
        }
    }

    private void CompleteAllWindows()
    {
        var completionTime = (DateTime.Now - _startTime).TotalSeconds;

        if (!_isTest)
        {
            try
            {
                var xpAmount = (50 * _requiredRepeats) + 200;
                if (_strictMode) xpAmount = (int)(xpAmount * 1.5);
                _progression.AddXP(xpAmount, XPSource.LockCard);
            }
            catch { }

            try
            {
                App.Services?.GetService<IAchievementService>()?.TrackLockCardCompletion(
                    completionTime, _totalCharsTyped, _totalErrors, _requiredRepeats);
            }
            catch { /* achievement service may not be present */ }
        }

        _logger?.LogInformation(
            "Lock Card completed - {Repeats} repeats in {Time:F1}s with {Errors} errors{Test}",
            _requiredRepeats, completionTime, _totalErrors, _isTest ? " (TEST)" : "");

        if (!_isTest)
        {
            try
            {
                _lockCard.NotifyCompleted(_phrase, _totalErrors, _requiredRepeats);
            }
            catch { /* legacy service may not be present */ }
        }

        foreach (var window in _allWindows)
        {
            window._isCompleted = true;
            window.TxtInput.IsEnabled = false;
            window.TxtHint.IsVisible = false;
            window.CompletionPanel.IsVisible = true;
        }

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer?.Stop();
            ForceCloseAll();
        };
        _closeTimer.Start();
    }

    private void UpdateProgress()
    {
        TxtProgress.Text = $"{_completedRepeats} / {_requiredRepeats}";

        var progressPercent = (double)_completedRepeats / Math.Max(1, _requiredRepeats);
        var maxWidth = ProgressBarContainer.Bounds.Width > 0 ? ProgressBarContainer.Bounds.Width : 200;
        ProgressBar.Width = maxWidth * progressPercent;
    }

    private void PulseCard()
    {
        try
        {
            CardBorder.RenderTransform = new ScaleTransform(1.05, 1.05);
            CardBorder.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                CardBorder.RenderTransform = new ScaleTransform(1, 1);
            };
            timer.Start();
        }
        catch { }
    }

    private string GetEncouragement()
    {
        var remaining = _requiredRepeats - _completedRepeats;
        var messages = new[]
        {
            Loc.GetF("lockcard_encourage_1", remaining),
            Loc.GetF("lockcard_encourage_2", remaining),
            Loc.GetF("lockcard_encourage_3", remaining),
            Loc.GetF("lockcard_encourage_4", remaining),
            Loc.GetF("lockcard_encourage_5", remaining),
        };

        return messages[_completedRepeats % messages.Length];
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_strictMode && !_isCompleted)
        {
            e.Cancel = true;
            ShakeCard();
        }
    }

    private void ShakeCard()
    {
        try
        {
            CardBorder.RenderTransform = new TranslateTransform(-10, 0);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            var ticks =
0;
            timer.Tick += (_, _) =>
            {
                ticks++;
                if (ticks >= 6)
                {
                    timer.Stop();
                    CardBorder.RenderTransform = null;
                    return;
                }
                CardBorder.RenderTransform = new TranslateTransform(ticks % 2 == 0 ? -10 : 10, 0);
            };
            timer.Start();
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closeTimer?.Stop();
        _allWindows.Remove(this);
        base.OnClosed(e);
    }
}
