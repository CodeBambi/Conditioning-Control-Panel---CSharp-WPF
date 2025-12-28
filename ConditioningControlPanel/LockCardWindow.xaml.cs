using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Lock Card window - user must type a phrase multiple times to dismiss
    /// Supports multi-monitor with synced input
    /// </summary>
    public partial class LockCardWindow : Window
    {
        private readonly string _phrase;
        private readonly int _requiredRepeats;
        private readonly bool _strictMode;
        private int _completedRepeats = 0;
        private bool _isCompleted = false;
        private DispatcherTimer? _closeTimer;
        
        // Multi-monitor support
        private readonly bool _isPrimary;
        private static List<LockCardWindow> _allWindows = new();
        private static string _sharedInput = "";

        /// <summary>
        /// Create a lock card window for a specific screen
        /// </summary>
        /// <param name="phrase">The phrase to type</param>
        /// <param name="repeats">Number of times to type it</param>
        /// <param name="strictMode">If true, ESC is disabled</param>
        /// <param name="screen">The screen to show on (null for primary)</param>
        /// <param name="isPrimary">If true, this window handles input</param>
        public LockCardWindow(string phrase, int repeats, bool strictMode, 
            System.Windows.Forms.Screen? screen = null, bool isPrimary = true)
        {
            InitializeComponent();
            
            _phrase = phrase;
            _requiredRepeats = repeats;
            _strictMode = strictMode;
            _isPrimary = isPrimary;
            
            // Set the phrase text
            TxtPhrase.Text = phrase;
            
            // Update progress display
            UpdateProgress();
            
            // Handle strict mode
            if (_strictMode)
            {
                TxtStrict.Text = "🔒 STRICT";
                TxtEscHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtEscHint.Text = "Press ESC to close";
            }
            
            // Position on screen
            if (screen != null)
            {
                PositionOnScreen(screen);
            }
            else
            {
                // Default to primary screen, maximized
                WindowState = WindowState.Maximized;
            }
            
            // Non-primary windows show synced text but input is read-only
            if (!_isPrimary)
            {
                TxtInput.IsReadOnly = true;
                TxtInput.Focusable = false;
                TxtHint.Text = "Input synced from primary monitor";
            }
            
            // Apply custom colors from settings
            ApplyColors();
            
            // Register this window
            _allWindows.Add(this);
        }

        private void PositionOnScreen(System.Windows.Forms.Screen screen)
        {
            // Get DPI scale
            var dpiScale = VisualTreeHelper.GetDpi(this);
            var scaleX = dpiScale.DpiScaleX;
            var scaleY = dpiScale.DpiScaleY;
            
            // Position window to cover the entire screen
            Left = screen.Bounds.Left / scaleX;
            Top = screen.Bounds.Top / scaleY;
            Width = screen.Bounds.Width / scaleX;
            Height = screen.Bounds.Height / scaleY;
        }

        private void ApplyColors()
        {
            try
            {
                var settings = App.Settings.Current;
                
                // Background
                var bgColor = ParseColor(settings.LockCardBackgroundColor, Color.FromRgb(26, 26, 46));
                CardBackground.Color = bgColor;
                
                // Make the outer background semi-transparent version of card bg
                var outerBg = Color.FromArgb(230, bgColor.R, bgColor.G, bgColor.B);
                BackgroundBrush.Color = outerBg;
                
                // Phrase text color
                var textColor = ParseColor(settings.LockCardTextColor, Color.FromRgb(255, 105, 180));
                PhraseBrush.Color = textColor;
                AccentBrush.Color = textColor;
                
                // Input field
                var inputBgColor = ParseColor(settings.LockCardInputBackgroundColor, Color.FromRgb(37, 37, 66));
                InputBackground.Color = inputBgColor;
                
                var inputTextColor = ParseColor(settings.LockCardInputTextColor, Colors.White);
                InputTextBrush.Color = inputTextColor;
                
                // Accent color
                var accentColor = ParseColor(settings.LockCardAccentColor, Color.FromRgb(255, 105, 180));
                InputBorderBrush.Color = accentColor;
                ProgressBar.Background = new SolidColorBrush(accentColor);
                
                // Card glow effect
                if (CardBorder.Effect is System.Windows.Media.Effects.DropShadowEffect glow)
                {
                    glow.Color = accentColor;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to apply lock card colors: {Error}", ex.Message);
            }
        }

        private Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return fallback;
                if (!hex.StartsWith("#")) hex = "#" + hex;
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return fallback;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Focus the input field only on primary
            if (_isPrimary)
            {
                TxtInput.Focus();
                
                App.Logger?.Information("Lock Card shown - Phrase: {Phrase}, Repeats: {Repeats}, Strict: {Strict}, Monitors: {Count}",
                    _phrase, _requiredRepeats, _strictMode, _allWindows.Count);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !_strictMode && !_isCompleted)
            {
                App.Logger?.Information("Lock Card closed via ESC");
                CloseAllWindows();
            }
            
            // Prevent Alt+F4 in strict mode
            if (_strictMode && e.Key == Key.System && e.SystemKey == Key.F4)
            {
                e.Handled = true;
            }
        }

        private void TxtInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_isCompleted || !_isPrimary) return;
            
            var input = TxtInput.Text;
            _sharedInput = input;
            
            // Sync to all other windows
            SyncInputToAllWindows(input);
            
            // Check if the input matches the phrase (case-insensitive)
            if (string.Equals(input.Trim(), _phrase, StringComparison.OrdinalIgnoreCase))
            {
                _completedRepeats++;
                UpdateProgressOnAllWindows();
                
                // Clear input for next repeat
                TxtInput.Clear();
                _sharedInput = "";
                SyncInputToAllWindows("");
                
                // Pulse animation on all windows
                PulseAllWindows();
                
                // Check if completed all repeats
                if (_completedRepeats >= _requiredRepeats)
                {
                    CompleteAllWindows();
                }
                else
                {
                    // Show encouragement on all windows
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
            // Award XP (only once)
            try
            {
                var xpAmount = 10 * _requiredRepeats;
                if (_strictMode) xpAmount = (int)(xpAmount * 1.5);
                App.Progression?.AddXP(xpAmount);
            }
            catch { }
            
            App.Logger?.Information("Lock Card completed - {Repeats} repeats", _requiredRepeats);
            
            foreach (var window in _allWindows)
            {
                window._isCompleted = true;
                window.TxtInput.IsEnabled = false;
                window.TxtHint.Visibility = Visibility.Collapsed;
                window.CompletionPanel.Visibility = Visibility.Visible;
            }
            
            // Auto-close after delay
            _closeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            _closeTimer.Tick += (s, e) =>
            {
                _closeTimer?.Stop();
                CloseAllWindows();
            };
            _closeTimer.Start();
        }

        private void UpdateProgress()
        {
            TxtProgress.Text = $"{_completedRepeats} / {_requiredRepeats}";
            
            // Update progress bar width
            var progressPercent = (double)_completedRepeats / _requiredRepeats;
            var maxWidth = 200;
            ProgressBar.Width = maxWidth * progressPercent;
        }

        private void PulseCard()
        {
            var animation = new DoubleAnimation
            {
                From = 1.0,
                To = 1.05,
                Duration = TimeSpan.FromMilliseconds(150),
                AutoReverse = true
            };
            
            var transform = new ScaleTransform(1, 1);
            CardBorder.RenderTransform = transform;
            CardBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        private string GetEncouragement()
        {
            var remaining = _requiredRepeats - _completedRepeats;
            var messages = new[]
            {
                $"Good! {remaining} more to go...",
                $"That's it! {remaining} left...",
                $"Keep going! {remaining} more...",
                $"Perfect! {remaining} remaining...",
                $"Yes! Only {remaining} more..."
            };
            
            return messages[_completedRepeats % messages.Length];
        }

        private void CloseAllWindows()
        {
            // Create a copy of the list to avoid modification during iteration
            var windowsToClose = new List<LockCardWindow>(_allWindows);
            _allWindows.Clear();
            
            foreach (var window in windowsToClose)
            {
                window._isCompleted = true; // Allow closing
                try
                {
                    window.Close();
                }
                catch { }
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // In strict mode, only allow closing if completed
            if (_strictMode && !_isCompleted)
            {
                e.Cancel = true;
                ShakeCard();
                return;
            }
            
            _closeTimer?.Stop();
            _allWindows.Remove(this);
            base.OnClosing(e);
        }

        private void ShakeCard()
        {
            var animation = new DoubleAnimation
            {
                From = -10,
                To = 10,
                Duration = TimeSpan.FromMilliseconds(50),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3)
            };
            
            animation.Completed += (s, e) =>
            {
                CardBorder.RenderTransform = null;
            };
            
            var transform = new TranslateTransform();
            CardBorder.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        /// <summary>
        /// Create lock card windows for all monitors
        /// </summary>
        public static void ShowOnAllMonitors(string phrase, int repeats, bool strictMode)
        {
            // Clear any existing windows
            _allWindows.Clear();
            _sharedInput = "";
            
            var screens = System.Windows.Forms.Screen.AllScreens;
            LockCardWindow? primaryWindow = null;
            
            foreach (var screen in screens)
            {
                var isPrimary = screen.Primary;
                var window = new LockCardWindow(phrase, repeats, strictMode, screen, isPrimary);
                
                if (isPrimary)
                {
                    primaryWindow = window;
                }
                
                window.Show();
            }
            
            // Focus primary window
            primaryWindow?.Activate();
            primaryWindow?.TxtInput.Focus();
        }
    }
}
