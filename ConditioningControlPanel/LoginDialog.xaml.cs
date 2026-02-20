using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Services;
using static ConditioningControlPanel.Services.V2AuthService;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Unified login dialog that handles provider selection, Season 0 recognition,
    /// cross-provider linking, and new user registration.
    /// </summary>
    public partial class LoginDialog : Window
    {
        private static readonly HttpClient _http = new();
        private readonly string _serverUrl = "https://codebambi-proxy.vercel.app";
        private CancellationTokenSource? _checkCts;

        // Track which provider was tried first (for cross-provider linking)
        private string? _firstProvider;
        private string? _firstProviderToken;
        private bool _isNameAvailable;
        private bool _isOgUser;  // Track if this is an OG user recovering their account

        /// <summary>
        /// Result of the login process
        /// </summary>
        public LoginResult? Result { get; private set; }

        public class LoginResult
        {
            public bool Success { get; set; }
            public bool IsLegacyUser { get; set; }
            public bool ShouldShowOgWelcome { get; set; }
            public string? UnifiedId { get; set; }
            public string? DisplayName { get; set; }
            public string? Provider { get; set; }
            public string? LinkedProvider { get; set; }
        }

        public LoginDialog()
        {
            InitializeComponent();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        #region Provider Selection

        private async void BtnLoginDiscord_Click(object sender, RoutedEventArgs e)
        {
            await TryLoginWithProviderAsync("discord");
        }

        private async void BtnLoginPatreon_Click(object sender, RoutedEventArgs e)
        {
            await TryLoginWithProviderAsync("patreon");
        }

        private async Task TryLoginWithProviderAsync(string provider)
        {
            ShowLoading($"Connecting to {provider}...");

            try
            {
                // Start OAuth flow
                string? accessToken = null;
                if (provider == "discord")
                {
                    if (App.Discord == null)
                    {
                        ShowError("Discord service not available");
                        return;
                    }
                    await App.Discord.StartOAuthFlowAsync();
                    accessToken = App.Discord.GetAccessToken();
                }
                else
                {
                    if (App.Patreon == null)
                    {
                        ShowError("Patreon service not available");
                        return;
                    }
                    await App.Patreon.StartOAuthFlowAsync();
                    accessToken = App.Patreon.GetAccessToken();
                }

                if (string.IsNullOrEmpty(accessToken))
                {
                    ShowProviderSelection();
                    return;
                }

                ShowLoading("Checking account...");

                // Try V2 authentication
                var v2Auth = new V2AuthService();
                V2AuthResponse authResponse;

                if (provider == "discord")
                    authResponse = await v2Auth.AuthenticateWithDiscordAsync(accessToken);
                else
                    authResponse = await v2Auth.AuthenticateWithPatreonAsync(accessToken);

                if (!authResponse.Success)
                {
                    ShowError(authResponse.Error ?? "Authentication failed");
                    return;
                }

                // Check the response
                if (authResponse.User != null && !authResponse.NeedsRegistration)
                {
                    // Existing user found - success!
                    v2Auth.ApplyUserDataToSettings(authResponse.User, authResponse.AuthToken);
                    App.UnifiedUserId = authResponse.User.UnifiedId;

                    UpdateServiceProperties(provider, authResponse.User.UnifiedId, authResponse.User.DisplayName);

                    Result = new LoginResult
                    {
                        Success = true,
                        IsLegacyUser = authResponse.User.IsSeason0Og,
                        ShouldShowOgWelcome = authResponse.User.IsSeason0Og && App.Settings?.Current?.HasShownOgWelcome != true,
                        UnifiedId = authResponse.User.UnifiedId,
                        DisplayName = authResponse.User.DisplayName,
                        Provider = provider
                    };

                    DialogResult = true;
                    Close();
                    return;
                }

                // User needs registration - check if this is a legacy user
                if (authResponse.IsLegacyUser && authResponse.LegacyData != null)
                {
                    var legacyDisplayName = authResponse.LegacyData.DisplayName;

                    // ALL legacy/OG users go to username picker so they can confirm or change their name
                    // Their legacy name is pre-filled but they can modify it
                    _firstProvider = provider;
                    _firstProviderToken = accessToken;
                    _isOgUser = true;  // Mark as OG for different UI text and OG badge
                    ShowUsernamePanel();
                    TxtUsername.Text = legacyDisplayName ?? "";  // Pre-fill with old name
                    return;
                }

                // Not found in Season 0 for this provider
                // Save this provider info and ask if they had an account with the other provider
                _firstProvider = provider;
                _firstProviderToken = accessToken;

                ShowNotFoundPanel(provider);
            }
            catch (OperationCanceledException)
            {
                ShowProviderSelection();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Login failed for {Provider}", provider);
                ShowError($"Login failed: {ex.Message}");
            }
        }

        #endregion

        #region Not Found Panel

        private void ShowNotFoundPanel(string triedProvider)
        {
            var otherProvider = triedProvider == "discord" ? "Patreon" : "Discord";

            TxtNotFoundMessage.Text = $"We couldn't find a Season 0 account linked to your {triedProvider}.";
            TxtTryOtherProvider.Text = $"Yes, I used {otherProvider}";

            // Style the button based on the other provider
            if (otherProvider == "Discord")
            {
                BtnTryOtherProvider.Background = new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xF2));
            }
            else
            {
                BtnTryOtherProvider.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x42, 0x4D));
            }

            ProviderPanel.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
            NotFoundPanel.Visibility = Visibility.Visible;
            UsernamePanel.Visibility = Visibility.Collapsed;
        }

        private async void BtnTryOtherProvider_Click(object sender, RoutedEventArgs e)
        {
            if (_firstProvider == null) return;

            var otherProvider = _firstProvider == "discord" ? "patreon" : "discord";
            await TryLinkOtherProviderAsync(otherProvider);
        }

        private async Task TryLinkOtherProviderAsync(string provider)
        {
            ShowLoading($"Connecting to {provider}...");

            try
            {
                // Start OAuth for the other provider
                string? accessToken = null;
                if (provider == "discord")
                {
                    if (App.Discord == null)
                    {
                        ShowError("Discord service not available");
                        return;
                    }
                    await App.Discord.StartOAuthFlowAsync();
                    accessToken = App.Discord.GetAccessToken();
                }
                else
                {
                    if (App.Patreon == null)
                    {
                        ShowError("Patreon service not available");
                        return;
                    }
                    await App.Patreon.StartOAuthFlowAsync();
                    accessToken = App.Patreon.GetAccessToken();
                }

                if (string.IsNullOrEmpty(accessToken))
                {
                    ShowNotFoundPanel(_firstProvider!);
                    return;
                }

                ShowLoading("Checking account...");

                // Try V2 authentication for this provider
                var v2Auth = new V2AuthService();
                V2AuthResponse authResponse;

                if (provider == "discord")
                    authResponse = await v2Auth.AuthenticateWithDiscordAsync(accessToken);
                else
                    authResponse = await v2Auth.AuthenticateWithPatreonAsync(accessToken);

                if (!authResponse.Success)
                {
                    ShowError(authResponse.Error ?? "Authentication failed");
                    return;
                }

                // Check if this provider has Season 0 data
                if (authResponse.IsLegacyUser && authResponse.LegacyData != null)
                {
                    // Found Season 0 data! Create account with legacy name
                    var displayName = authResponse.LegacyData.DisplayName;

                    ShowLoading("Restoring your account...");

                    // Register with the legacy display name
                    if (provider == "discord")
                        authResponse = await v2Auth.AuthenticateWithDiscordAsync(accessToken, displayName);
                    else
                        authResponse = await v2Auth.AuthenticateWithPatreonAsync(accessToken, displayName);

                    if (authResponse.Success && authResponse.User != null)
                    {
                        // Now link the first provider
                        ShowLoading("Linking accounts...");

                        var linkResult = await v2Auth.LinkProviderAsync(
                            authResponse.User.UnifiedId!,
                            _firstProvider!,
                            _firstProviderToken!);

                        // Use link token if available (rotated), otherwise fall back to auth token
                        var effectiveToken = linkResult.AuthToken ?? authResponse.AuthToken;
                        v2Auth.ApplyUserDataToSettings(authResponse.User, effectiveToken);
                        App.UnifiedUserId = authResponse.User.UnifiedId;

                        // Update both service properties
                        UpdateServiceProperties(provider, authResponse.User.UnifiedId, authResponse.User.DisplayName);
                        UpdateServiceProperties(_firstProvider!, authResponse.User.UnifiedId, authResponse.User.DisplayName);

                        // Update linking status
                        if (App.Settings?.Current != null)
                        {
                            App.Settings.Current.HasLinkedDiscord = true;
                            App.Settings.Current.HasLinkedPatreon = true;
                        }

                        Result = new LoginResult
                        {
                            Success = true,
                            IsLegacyUser = true,
                            ShouldShowOgWelcome = App.Settings?.Current?.HasShownOgWelcome != true,
                            UnifiedId = authResponse.User.UnifiedId,
                            DisplayName = authResponse.User.DisplayName,
                            Provider = provider,
                            LinkedProvider = _firstProvider
                        };

                        DialogResult = true;
                        Close();
                        return;
                    }
                }

                // Still not found - they're not in Season 0
                MessageBox.Show(this,
                    "We couldn't find a Season 0 account for either login method.\n\n" +
                    "You can create a new account instead.",
                    "Account Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                ShowUsernamePanel();
            }
            catch (OperationCanceledException)
            {
                ShowNotFoundPanel(_firstProvider!);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Link other provider failed");
                ShowError($"Failed: {ex.Message}");
            }
        }

        private void BtnNewAccount_Click(object sender, RoutedEventArgs e)
        {
            _isOgUser = false;
            ShowUsernamePanel();
        }

        #endregion

        #region Username Entry

        private void ShowUsernamePanel()
        {
            ProviderPanel.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
            NotFoundPanel.Visibility = Visibility.Collapsed;
            UsernamePanel.Visibility = Visibility.Visible;

            // Set different text for OG users vs new users
            if (_isOgUser)
            {
                TxtUsernameTitle.Text = "What was your username?";
                TxtUsernameSubtitle.Text = "Enter the name you used before v5.5";
            }
            else
            {
                TxtUsernameTitle.Text = "Choose your display name";
                TxtUsernameSubtitle.Text = "This will be shown on the leaderboard";
            }

            TxtUsername.Focus();
        }

        private async void TxtUsername_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var name = TxtUsername.Text.Trim();

            _checkCts?.Cancel();
            _checkCts = new CancellationTokenSource();
            var token = _checkCts.Token;

            if (string.IsNullOrWhiteSpace(name))
            {
                SetAvailabilityStatus("Enter a unique name (3-30 characters)", Brushes.Gray, false);
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

            SetAvailabilityStatus("Checking...", Brushes.Gray, false);

            try
            {
                await Task.Delay(400, token);
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
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                SetAvailabilityStatus($"Error: {ex.Message}", Brushes.Orange, false);
            }
        }

        private void SetAvailabilityStatus(string message, Brush color, bool available)
        {
            TxtAvailability.Text = message;
            TxtAvailability.Foreground = color;
            _isNameAvailable = available;
            BtnConfirmUsername.IsEnabled = available;
        }

        private async Task<bool> CheckNameAvailabilityAsync(string name)
        {
            try
            {
                // Use the appropriate endpoint based on provider
                var endpoint = _firstProvider == "discord"
                    ? $"{_serverUrl}/user/check-display-name-discord?name={Uri.EscapeDataString(name)}"
                    : $"{_serverUrl}/user/check-display-name?name={Uri.EscapeDataString(name)}";

                var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                if (!string.IsNullOrEmpty(_firstProviderToken))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _firstProviderToken);
                }

                var response = await _http.SendAsync(request);
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

        private async void BtnConfirmUsername_Click(object sender, RoutedEventArgs e)
        {
            if (!_isNameAvailable || string.IsNullOrEmpty(_firstProviderToken)) return;

            var displayName = TxtUsername.Text.Trim();
            ShowLoading("Creating your account...");

            try
            {
                var v2Auth = new V2AuthService();
                V2AuthResponse authResponse;

                if (_firstProvider == "discord")
                    authResponse = await v2Auth.AuthenticateWithDiscordAsync(_firstProviderToken, displayName);
                else
                    authResponse = await v2Auth.AuthenticateWithPatreonAsync(_firstProviderToken, displayName);

                if (authResponse.Success && authResponse.User != null)
                {
                    v2Auth.ApplyUserDataToSettings(authResponse.User, authResponse.AuthToken);
                    App.UnifiedUserId = authResponse.User.UnifiedId;

                    UpdateServiceProperties(_firstProvider!, authResponse.User.UnifiedId, authResponse.User.DisplayName);

                    // If this was an OG user picking a new name (old one taken), they still get OG status
                    var isOg = _isOgUser || authResponse.User.IsSeason0Og;

                    Result = new LoginResult
                    {
                        Success = true,
                        IsLegacyUser = isOg,
                        ShouldShowOgWelcome = isOg && App.Settings?.Current?.HasShownOgWelcome != true,
                        UnifiedId = authResponse.User.UnifiedId,
                        DisplayName = authResponse.User.DisplayName,
                        Provider = _firstProvider
                    };

                    DialogResult = true;
                    Close();
                }
                else
                {
                    ShowError(authResponse.Error ?? "Failed to create account");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to create account");
                ShowError($"Failed: {ex.Message}");
            }
        }

        #endregion

        #region UI Helpers

        private void ShowProviderSelection()
        {
            ProviderPanel.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;
            NotFoundPanel.Visibility = Visibility.Collapsed;
            UsernamePanel.Visibility = Visibility.Collapsed;
            BtnLoginDiscord.IsEnabled = true;
            BtnLoginPatreon.IsEnabled = true;
        }

        private void ShowLoading(string message)
        {
            TxtLoadingMessage.Text = message;
            ProviderPanel.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Visible;
            NotFoundPanel.Visibility = Visibility.Collapsed;
            UsernamePanel.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            MessageBox.Show(this, message, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            ShowProviderSelection();
        }

        private void UpdateServiceProperties(string provider, string? unifiedId, string? displayName)
        {
            if (provider == "patreon" && App.Patreon != null)
            {
                App.Patreon.UnifiedUserId = unifiedId;
                App.Patreon.DisplayName = displayName;
            }
            else if (provider == "discord" && App.Discord != null)
            {
                App.Discord.UnifiedUserId = unifiedId;
                App.Discord.CustomDisplayName = displayName;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            // Logout any providers that were authenticated during this flow
            if (_firstProvider == "discord")
                App.Discord?.Logout();
            else if (_firstProvider == "patreon")
                App.Patreon?.Logout();

            DialogResult = false;
            Close();
        }

        #endregion
    }
}
