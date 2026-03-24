using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Dialog for choosing a display name on first login
    /// </summary>
    public partial class UsernamePickerDialog : Window
    {
        private static readonly HttpClient _http = new();
        private readonly string _serverUrl = "https://codebambi-proxy.vercel.app";
        private System.Threading.CancellationTokenSource? _checkCts;
        private bool _isAvailable = false;

        /// <summary>
        /// The chosen display name (null if cancelled)
        /// </summary>
        public string? ChosenDisplayName { get; private set; }

        public UsernamePickerDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Show the dialog configured for a new user
        /// </summary>
        public void ConfigureForNewUser()
        {
            OgWelcomePanel.Visibility = Visibility.Collapsed;
            SuggestionPanel.Visibility = Visibility.Collapsed;
            TxtSubtitle.Text = Loc.Get("label_this_name_will_be_shown_on_the_leaderboard_an");
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private async void TxtUsername_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var name = TxtUsername.Text.Trim();

            // Cancel any pending check
            _checkCts?.Cancel();
            _checkCts = new System.Threading.CancellationTokenSource();
            var token = _checkCts.Token;

            // Validate locally first
            if (string.IsNullOrWhiteSpace(name))
            {
                SetAvailabilityStatus(Loc.Get("login_enter_unique_display_name"), Brushes.Gray, false);
                return;
            }

            if (name.Length < 3)
            {
                SetAvailabilityStatus(Loc.Get("login_name_min_3_chars"), Brushes.Orange, false);
                return;
            }

            if (name.Length > 30)
            {
                SetAvailabilityStatus(Loc.Get("login_name_max_30_chars"), Brushes.Orange, false);
                return;
            }

            // Check server availability after a short delay
            SetAvailabilityStatus(Loc.Get("login_checking_availability"), Brushes.Gray, false);

            try
            {
                await Task.Delay(500, token); // Debounce
                if (token.IsCancellationRequested) return;

                var available = await CheckNameAvailabilityAsync(name);

                if (token.IsCancellationRequested) return;

                if (available)
                {
                    SetAvailabilityStatus(Loc.GetF("login_name_available", name), Brushes.LightGreen, true);
                }
                else
                {
                    SetAvailabilityStatus(Loc.GetF("login_name_already_taken", name), Brushes.Orange, false);
                }
            }
            catch (TaskCanceledException)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                SetAvailabilityStatus(Loc.GetF("login_could_not_check", ex.Message), Brushes.Orange, false);
            }
        }

        private void SetAvailabilityStatus(string message, Brush color, bool available)
        {
            TxtAvailability.Text = message;
            TxtAvailability.Foreground = color;
            _isAvailable = available;
            BtnConfirm.IsEnabled = available;
        }

        private async Task<bool> CheckNameAvailabilityAsync(string name)
        {
            try
            {
                var response = await _http.GetAsync($"{_serverUrl}/user/check-display-name?display_name={Uri.EscapeDataString(name)}");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JObject.Parse(json);
                    return (bool?)result["available"] ?? false;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void BtnUseSuggestion_Click(object sender, RoutedEventArgs e)
        {
            // Legacy suggestion panel - always collapsed, kept for XAML compatibility
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            ChosenDisplayName = null;
            DialogResult = false;
            Close();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (_isAvailable)
            {
                ChosenDisplayName = TxtUsername.Text.Trim();
                DialogResult = true;
                Close();
            }
        }
    }
}
