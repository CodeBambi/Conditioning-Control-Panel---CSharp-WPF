using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

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

        /// <summary>
        /// Whether this is a legacy/OG user
        /// </summary>
        public bool IsLegacyUser { get; set; }

        /// <summary>
        /// Suggested name from legacy data
        /// </summary>
        public string? SuggestedName { get; set; }

        public UsernamePickerDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Show the dialog configured for a legacy user returning
        /// </summary>
        public void ConfigureForLegacyUser(string? suggestedName)
        {
            IsLegacyUser = true;
            SuggestedName = suggestedName;

            OgWelcomePanel.Visibility = Visibility.Visible;
            TxtSubtitle.Text = "Your Season 0 data has been found! Choose your display name for the new season.";

            if (!string.IsNullOrWhiteSpace(suggestedName))
            {
                SuggestionPanel.Visibility = Visibility.Visible;
                BtnUseSuggestion.Content = $"Use \"{suggestedName}\"";
            }
        }

        /// <summary>
        /// Show the dialog configured for a new user
        /// </summary>
        public void ConfigureForNewUser()
        {
            IsLegacyUser = false;
            OgWelcomePanel.Visibility = Visibility.Collapsed;
            SuggestionPanel.Visibility = Visibility.Collapsed;
            TxtSubtitle.Text = "This name will be shown on the leaderboard and to other users.";
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
                SetAvailabilityStatus("Enter a unique display name (3-30 characters)", Brushes.Gray, false);
                return;
            }

            if (name.Length < 3)
            {
                SetAvailabilityStatus("Name must be at least 3 characters", Brushes.Orange, false);
                return;
            }

            if (name.Length > 30)
            {
                SetAvailabilityStatus("Name must be 30 characters or less", Brushes.Orange, false);
                return;
            }

            // Check server availability after a short delay
            SetAvailabilityStatus("Checking availability...", Brushes.Gray, false);

            try
            {
                await Task.Delay(500, token); // Debounce
                if (token.IsCancellationRequested) return;

                var available = await CheckNameAvailabilityAsync(name);

                if (token.IsCancellationRequested) return;

                if (available)
                {
                    SetAvailabilityStatus($"\"{name}\" is available!", Brushes.LightGreen, true);
                }
                else
                {
                    SetAvailabilityStatus($"\"{name}\" is already taken", Brushes.Orange, false);
                }
            }
            catch (TaskCanceledException)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                SetAvailabilityStatus($"Could not check: {ex.Message}", Brushes.Orange, false);
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
                    return result["available"]?.Value<bool>() ?? false;
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
            if (!string.IsNullOrWhiteSpace(SuggestedName))
            {
                TxtUsername.Text = SuggestedName;
            }
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
