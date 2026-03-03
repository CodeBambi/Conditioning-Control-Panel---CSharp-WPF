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
    /// Unified login dialog that handles provider selection and new user registration.
    /// </summary>
    public partial class LoginDialog : Window
    {
        private static readonly HttpClient _http = new();
        private readonly string _serverUrl = "https://codebambi-proxy.vercel.app";
        private CancellationTokenSource? _checkCts;

        // Track which provider was tried first
        private string? _firstProvider;
        private string? _firstProviderToken;
        private bool _isNameAvailable;
        private bool _isAccountRegisterMode;  // True = register mode, false = login mode
        private string? _pendingInviteCode;
        private string? _pendingPassword;

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

        /// <summary>Clear all sensitive data from memory and UI fields.</summary>
        private void ClearSensitiveData()
        {
            _pendingInviteCode = null;
            _pendingPassword = null;
            TxtPassword.Password = "";
            TxtPasswordConfirm.Password = "";
        }

        /// <summary>Sanitize server error messages before showing to user (audit C3).</summary>
        private static string SanitizeError(string? error)
        {
            if (string.IsNullOrEmpty(error)) return "An error occurred";
            // Strip anything that looks like internal info (stack traces, paths, Redis keys)
            if (error.Contains("ECONNREFUSED") || error.Contains("Redis") || error.Contains("redis")
                || error.Contains("stack") || error.Contains("\\") || error.Contains("/api/"))
                return "Server error. Please try again later.";
            return error;
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

        private void BtnLoginAccount_Click(object sender, RoutedEventArgs e)
        {
            ShowAccountPanel(isRegister: false);
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
                    ShowError(SanitizeError(authResponse.Error));
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

                // User needs registration - go straight to username picker
                _firstProvider = provider;
                _firstProviderToken = accessToken;
                ShowUsernamePanel();
            }
            catch (OperationCanceledException)
            {
                ShowProviderSelection();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Login failed for {Provider}", provider);
                ShowError("Login failed. Please try again.");
            }
        }

        #endregion

        #region Username Entry

        private void ShowUsernamePanel()
        {
            ProviderPanel.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
            UsernamePanel.Visibility = Visibility.Visible;
            AccountPanel.Visibility = Visibility.Collapsed;

            TxtUsernameTitle.Text = "Choose your display name";
            TxtUsernameSubtitle.Text = "This will be shown on the leaderboard";

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
                SetAvailabilityStatus("Error checking name", Brushes.Orange, false);
                App.Logger?.Warning(ex, "Name availability check failed");
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
                string endpoint;
                if (_firstProvider == "invite")
                {
                    // Invite code flow: use lightweight unauthenticated endpoint
                    endpoint = $"{_serverUrl}/v2/auth/check-name?name={Uri.EscapeDataString(name)}";
                }
                else if (_firstProvider == "discord")
                {
                    endpoint = $"{_serverUrl}/user/check-display-name-discord?name={Uri.EscapeDataString(name)}";
                }
                else
                {
                    endpoint = $"{_serverUrl}/user/check-display-name?name={Uri.EscapeDataString(name)}";
                }

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
            if (!_isNameAvailable) return;

            // Invite code registration doesn't use _firstProviderToken
            if (_firstProvider != "invite" && string.IsNullOrEmpty(_firstProviderToken)) return;

            var displayName = TxtUsername.Text.Trim();

            // Disable button during async (audit C2)
            BtnConfirmUsername.IsEnabled = false;
            ShowLoading("Creating your account...");

            try
            {
                var v2Auth = new V2AuthService();
                V2AuthResponse authResponse;

                if (_firstProvider == "invite")
                {
                    if (string.IsNullOrEmpty(_pendingInviteCode) || string.IsNullOrEmpty(_pendingPassword))
                    {
                        ClearSensitiveData();
                        ShowError("Session expired. Please try again.");
                        return;
                    }
                    authResponse = await v2Auth.RegisterAsync(_pendingInviteCode, displayName, _pendingPassword);
                    ClearSensitiveData(); // Clear immediately after use (audit C1)
                }
                else if (_firstProvider == "discord")
                    authResponse = await v2Auth.AuthenticateWithDiscordAsync(_firstProviderToken!, displayName);
                else
                    authResponse = await v2Auth.AuthenticateWithPatreonAsync(_firstProviderToken!, displayName);

                if (authResponse.Success && authResponse.User != null)
                {
                    v2Auth.ApplyUserDataToSettings(authResponse.User, authResponse.AuthToken);
                    App.UnifiedUserId = authResponse.User.UnifiedId;

                    if (_firstProvider != "invite")
                        UpdateServiceProperties(_firstProvider!, authResponse.User.UnifiedId, authResponse.User.DisplayName);

                    Result = new LoginResult
                    {
                        Success = true,
                        IsLegacyUser = false,
                        ShouldShowOgWelcome = false,
                        UnifiedId = authResponse.User.UnifiedId,
                        DisplayName = authResponse.User.DisplayName,
                        Provider = _firstProvider
                    };

                    DialogResult = true;
                    Close();
                }
                else
                {
                    ClearSensitiveData();
                    ShowError(SanitizeError(authResponse.Error));
                }
            }
            catch (Exception ex)
            {
                ClearSensitiveData();
                App.Logger?.Error(ex, "Failed to create account");
                ShowError("Failed to create account. Please try again.");
            }
        }

        #endregion

        #region Account Login (Invite Code + Password)

        private void ShowAccountPanel(bool isRegister)
        {
            _isAccountRegisterMode = isRegister;

            ProviderPanel.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
            UsernamePanel.Visibility = Visibility.Collapsed;
            AccountPanel.Visibility = Visibility.Visible;

            // Clear all fields
            TxtInviteCode.Text = "";
            TxtLoginDisplayName.Text = "";
            TxtPassword.Password = "";
            TxtPasswordConfirm.Password = "";
            TxtAccountError.Text = "";
            BtnAccountSubmit.IsEnabled = true;

            if (isRegister)
            {
                TxtAccountTitle.Text = "Create Account";
                BtnAccountSubmit.Content = "Next";

                // Show invite code + password + confirm; hide display name
                LblInviteCodeHint.Visibility = Visibility.Visible;
                LblInviteCode.Visibility = Visibility.Visible;
                TxtInviteCode.Visibility = Visibility.Visible;
                LblDisplayName.Visibility = Visibility.Collapsed;
                TxtLoginDisplayName.Visibility = Visibility.Collapsed;
                LblPasswordConfirm.Visibility = Visibility.Visible;
                TxtPasswordConfirm.Visibility = Visibility.Visible;

                TxtAccountToggle.Inlines.Clear();
                TxtAccountToggle.Inlines.Add(new System.Windows.Documents.Run("Already have an account? ") { Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)) });
                TxtAccountToggle.Inlines.Add(new System.Windows.Documents.Run("Login") { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)), TextDecorations = TextDecorations.Underline });

                TxtInviteCode.Focus();
            }
            else
            {
                TxtAccountTitle.Text = "Login";
                BtnAccountSubmit.Content = "Login";

                // Show display name + password; hide invite code + confirm
                LblInviteCodeHint.Visibility = Visibility.Collapsed;
                LblInviteCode.Visibility = Visibility.Collapsed;
                TxtInviteCode.Visibility = Visibility.Collapsed;
                LblDisplayName.Visibility = Visibility.Visible;
                TxtLoginDisplayName.Visibility = Visibility.Visible;
                LblPasswordConfirm.Visibility = Visibility.Collapsed;
                TxtPasswordConfirm.Visibility = Visibility.Collapsed;

                TxtAccountToggle.Inlines.Clear();
                TxtAccountToggle.Inlines.Add(new System.Windows.Documents.Run("Don't have an account? ") { Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)) });
                TxtAccountToggle.Inlines.Add(new System.Windows.Documents.Run("Create one") { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)), TextDecorations = TextDecorations.Underline });

                TxtLoginDisplayName.Focus();
            }
        }

        private void TxtAccountToggle_Click(object sender, MouseButtonEventArgs e)
        {
            ShowAccountPanel(!_isAccountRegisterMode);
        }

        private void BtnAccountBack_Click(object sender, MouseButtonEventArgs e)
        {
            ClearSensitiveData();
            ShowProviderSelection();
        }

        private async void BtnAccountSubmit_Click(object sender, RoutedEventArgs e)
        {
            var password = TxtPassword.Password;

            // Validate password (shared for both modes)
            if (password.Length < 8)
            {
                TxtAccountError.Text = "Password must be at least 8 characters";
                return;
            }

            // Disable button during async (audit C2)
            BtnAccountSubmit.IsEnabled = false;

            if (_isAccountRegisterMode)
            {
                var inviteCode = TxtInviteCode.Text.Trim();

                // Validate invite code
                if (string.IsNullOrWhiteSpace(inviteCode))
                {
                    TxtAccountError.Text = "Please enter your invite code";
                    BtnAccountSubmit.IsEnabled = true;
                    return;
                }

                // Validate confirm password
                if (TxtPasswordConfirm.Password != password)
                {
                    TxtAccountError.Text = "Passwords do not match";
                    BtnAccountSubmit.IsEnabled = true;
                    return;
                }

                // Save credentials and go to username panel
                _pendingInviteCode = inviteCode;
                _pendingPassword = password;
                _firstProvider = "invite";
                ShowUsernamePanel();
            }
            else
            {
                var displayName = TxtLoginDisplayName.Text.Trim();

                // Validate display name
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    TxtAccountError.Text = "Please enter your display name";
                    BtnAccountSubmit.IsEnabled = true;
                    return;
                }

                // Login mode
                await TryAccountLoginAsync(displayName, password);
            }
        }

        private async Task TryAccountLoginAsync(string displayName, string password)
        {
            ShowLoading("Logging in...");

            try
            {
                var v2Auth = new V2AuthService();
                var authResponse = await v2Auth.LoginAsync(displayName, password);

                // Clear password from memory immediately after use (audit C1)
                ClearSensitiveData();

                if (!authResponse.Success)
                {
                    ShowAccountPanel(_isAccountRegisterMode);
                    TxtLoginDisplayName.Text = displayName;
                    TxtAccountError.Text = SanitizeError(authResponse.Error);
                    return;
                }

                if (authResponse.User != null)
                {
                    v2Auth.ApplyUserDataToSettings(authResponse.User, authResponse.AuthToken);
                    App.UnifiedUserId = authResponse.User.UnifiedId;

                    Result = new LoginResult
                    {
                        Success = true,
                        IsLegacyUser = false,
                        ShouldShowOgWelcome = false,
                        UnifiedId = authResponse.User.UnifiedId,
                        DisplayName = authResponse.User.DisplayName,
                        Provider = "account"
                    };

                    DialogResult = true;
                    Close();
                }
                else
                {
                    ShowAccountPanel(_isAccountRegisterMode);
                    TxtLoginDisplayName.Text = displayName;
                    TxtAccountError.Text = "Unexpected response from server";
                }
            }
            catch (Exception ex)
            {
                ClearSensitiveData();
                App.Logger?.Error(ex, "Account login failed");
                ShowAccountPanel(_isAccountRegisterMode);
                TxtLoginDisplayName.Text = displayName;
                TxtAccountError.Text = "Login failed. Please try again.";
            }
        }

        #endregion

        #region UI Helpers

        private void ShowProviderSelection()
        {
            ProviderPanel.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;
            UsernamePanel.Visibility = Visibility.Collapsed;
            AccountPanel.Visibility = Visibility.Collapsed;
            BtnLoginDiscord.IsEnabled = true;
            BtnLoginPatreon.IsEnabled = true;
        }

        private void ShowLoading(string message)
        {
            TxtLoadingMessage.Text = message;
            ProviderPanel.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Visible;
            UsernamePanel.Visibility = Visibility.Collapsed;
            AccountPanel.Visibility = Visibility.Collapsed;
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

            // Clear sensitive data (audit C1)
            ClearSensitiveData();

            DialogResult = false;
            Close();
        }

        #endregion
    }
}
