using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Result window for bubble counting - enter the number, 3 attempts, then mercy card
    /// </summary>
    public partial class BubbleCountResultWindow : Window
    {
        private readonly int _correctAnswer;
        private readonly bool _strictMode;
        private readonly Action<bool> _onComplete;
        
        private int _attemptsRemaining = 3;
        private bool _isCompleted = false;

        public BubbleCountResultWindow(int correctAnswer, bool strictMode, Action<bool> onComplete)
        {
            InitializeComponent();
            
            _correctAnswer = correctAnswer;
            _strictMode = strictMode;
            _onComplete = onComplete;
            
            // Setup UI
            UpdateAttemptsDisplay();
            
            if (_strictMode)
            {
                TxtStrict.Visibility = Visibility.Visible;
                TxtEscHint.Visibility = Visibility.Collapsed;
            }
            
            // Focus input
            Loaded += (s, e) => TxtAnswer.Focus();
            
            // Key handlers
            KeyDown += OnKeyDown;
            TxtAnswer.KeyDown += OnInputKeyDown;
            
            // Only allow numbers
            TxtAnswer.PreviewTextInput += (s, e) =>
            {
                e.Handled = !char.IsDigit(e.Text, 0);
            };
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CheckAnswer();
                e.Handled = true;
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !_strictMode && !_isCompleted)
            {
                Complete(false);
            }
        }

        private void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            CheckAnswer();
        }

        private void CheckAnswer()
        {
            if (_isCompleted) return;
            
            if (!int.TryParse(TxtAnswer.Text.Trim(), out int answer))
            {
                ShowFeedback("Please enter a number!", Colors.Orange);
                TxtAnswer.Clear();
                TxtAnswer.Focus();
                return;
            }
            
            if (answer == _correctAnswer)
            {
                // Correct!
                ShowFeedback("🎉 CORRECT! +100 XP 🎉", Color.FromRgb(50, 205, 50));
                BtnSubmit.IsEnabled = false;
                TxtAnswer.IsEnabled = false;
                
                // Delay then complete
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    Complete(true);
                };
                timer.Start();
            }
            else
            {
                // Wrong answer
                _attemptsRemaining--;
                UpdateAttemptsDisplay();
                
                if (_attemptsRemaining <= 0)
                {
                    // Out of attempts - show mercy card
                    ShowMercyCard();
                }
                else
                {
                    // Give hint
                    string hint = answer < _correctAnswer ? "Too low! Try higher." : "Too high! Try lower.";
                    ShowFeedback($"❌ {hint}", Color.FromRgb(255, 107, 107));
                    TxtAnswer.Clear();
                    TxtAnswer.Focus();
                }
            }
        }

        private void ShowFeedback(string message, Color color)
        {
            TxtFeedback.Text = message;
            TxtFeedback.Foreground = new SolidColorBrush(color);
            TxtFeedback.Visibility = Visibility.Visible;
        }

        private void UpdateAttemptsDisplay()
        {
            TxtAttempts.Text = $"Attempts remaining: {_attemptsRemaining}";
            
            // Color based on attempts
            if (_attemptsRemaining == 1)
            {
                TxtAttempts.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
            }
            else if (_attemptsRemaining == 2)
            {
                TxtAttempts.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0));
            }
        }

        private void ShowMercyCard()
        {
            _isCompleted = true;
            Hide();
            
            // Get a mercy phrase
            var mercyPhrases = new[]
            {
                "I need to pay more attention",
                "I will count more carefully next time",
                "Bubbles are my friends",
                "I love counting bubbles",
                "Practice makes perfect"
            };
            
            var random = new Random();
            var phrase = mercyPhrases[random.Next(mercyPhrases.Length)];
            
            // Show mercy lock card with the correct answer as a hint
            var mercyWindow = new LockCardWindow(
                $"{phrase} (The answer was {_correctAnswer})",
                2, // Type twice
                _strictMode);
            
            mercyWindow.Closed += (s, e) =>
            {
                Complete(false);
            };
            
            LockCardWindow.ShowOnAllMonitors(
                $"{phrase} (The answer was {_correctAnswer})",
                2,
                _strictMode);
        }

        private void Complete(bool success)
        {
            if (_isCompleted && !success) 
            {
                // Already completed via mercy card
                Close();
                _onComplete?.Invoke(false);
                return;
            }
            
            _isCompleted = true;
            Close();
            _onComplete?.Invoke(success);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (!_isCompleted)
            {
                _onComplete?.Invoke(false);
            }
            base.OnClosed(e);
        }
    }
}
