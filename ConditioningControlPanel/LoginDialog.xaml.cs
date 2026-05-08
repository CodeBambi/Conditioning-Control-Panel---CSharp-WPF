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
using ConditioningControlPanel.Localization;

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

        // SP3 device-code flow state
        private CancellationTokenSource? _deviceCts;
        private string? _deviceCode;
        private DateTimeOffset _deviceCodeExpiresAt;

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

            // SP3 Lab gate: cached PatreonTier >= 2 from a prior legacy login.
            // Removed when SP3 promotes from Lab to stable.
            var tier = App.Settings?.Current?.PatreonTier ?? 0;
            if (tier >= 2)
            {
                BtnLoginDeviceCode.Visibility = Visibility.Visible;
            }
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
            _checkCts?.Cancel();
            _checkCts?.Dispose();
            _checkCts = null;
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
            ShowLoading(Loc.GetF("login_connecting_to_provider", provider));

            try
            {
                // Start OAuth flow
                string? accessToken = null;
                if (provider == "discord")
                {
                    if (App.Discord == null)
                    {
                        ShowError(Loc.Get("login_discord_service_not_available"));
                        return;
                    }
                    await App.Discord.StartOAuthFlowAsync();
                    accessToken = App.Discord.GetAccessToken();
                }
                else
                {
                    if (App.Patreon == null)
                    {
                        ShowError(Loc.Get("login_patreon_service_not_available"));
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

                ShowLoading(Loc.Get("login_checking_account"));

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
                ShowError(Loc.Get("login_failed_please_try_again"));
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
            DeviceCodePanel.Visibility = Visibility.Collapsed;

            TxtUsernameTitle.Text = Loc.Get("label_choose_your_display_name");
            TxtUsernameSubtitle.Text = Loc.Get("label_this_will_be_shown_on_the_leaderboard");

            BtnConfirmUsername.IsEnabled = true;
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
                SetAvailabilityStatus(Loc.Get("login_enter_unique_name"), Brushes.Gray, false);
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

            SetAvailabilityStatus(Loc.Get("login_checking"), Brushes.Gray, false);

            try
            {
                await Task.Delay(400, token);
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
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                SetAvailabilityStatus(Loc.Get("login_error_checking_name"), Brushes.Orange, false);
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
            catch (Exception ex)
            {
                App.Logger?.Warning("Name availability check failed: {Error}", ex.Message);
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
            ShowLoading(Loc.Get("login_creating_account"));

            try
            {
                var v2Auth = new V2AuthService();
                V2AuthResponse authResponse;

                if (_firstProvider == "invite")
                {
                    if (string.IsNullOrEmpty(_pendingInviteCode) || string.IsNullOrEmpty(_pendingPassword))
                    {
                        ClearSensitiveData();
                        ShowError(Loc.Get("login_session_expired"));
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
                ShowError(Loc.Get("login_failed_to_create_account"));
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
            DeviceCodePanel.Visibility = Visibility.Collapsed;

            // Clear all fields
            TxtInviteCode.Text = "";
            TxtLoginDisplayName.Text = "";
            TxtPassword.Password = "";
            TxtPasswordConfirm.Password = "";
            TxtAccountError.Text = "";
            BtnAccountSubmit.IsEnabled = true;

            if (isRegister)
            {
                TxtAccountTitle.Text = Loc.Get("label_create_account");
                BtnAccountSubmit.Content = Loc.Get("btn_next");

                // Show invite code + password + confirm; hide display name
                LblInviteCodeHint.Visibility = Visibility.Visible;
                LblInviteCode.Visibility = Visibility.Visible;
                TxtInviteCode.Visibility = Visibility.Visible;
                LblDisplayName.Visibility = Visibility.Collapsed;
                TxtLoginDisplayName.Visibility = Visibility.Collapsed;
                LblPasswordConfirm.Visibility = Visibility.Visible;
                TxtPasswordConfirm.Visibility = Visibility.Visible;

                TxtAccountToggle.Inlines.Clear();
                TxtAccountToggle.Inlines.Add(new System.Windows.Documents.Run(Loc.Get("login_already_have_account") + " ") { Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)) });
                TxtAccountToggle.Inlines.Add(new System.Windows.Documents.Run(Loc.Get("btn_login")) { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)), TextDecorations = TextDecorations.Underline });

                TxtInviteCode.Focus();
            }
            else
            {
                TxtAccountTitle.Text = Loc.Get("btn_login");
                BtnAccountSubmit.Content = Loc.Get("btn_login");

                // Show display name + password; hide invite code + confirm
                LblInviteCodeHint.Visibility = Visibility.Collapsed;
                LblInviteCode.Visibility = Visibility.Collapsed;
                TxtInviteCode.Visibility = Visibility.Collapsed;
                LblDisplayName.Visibility = Visibility.Visible;
                TxtLoginDisplayName.Visibility = Visibility.Visible;
                LblPasswordConfirm.Visibility = Visibility.Collapsed;
                TxtPasswordConfirm.Visibility = Visibility.Collapsed;

                TxtAccountToggle.Inlines.Clear();
                TxtAccountToggle.Inlines.Add(new System.Windows.Documents.Run(Loc.Get("login_dont_have_account") + " ") { Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)) });
                TxtAccountToggle.Inlines.Add(new System.Windows.Documents.Run(Loc.Get("btn_create_account")) { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)), TextDecorations = TextDecorations.Underline });

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
                TxtAccountError.Text = Loc.Get("label_password_must_be_at_least_8_characters");
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
                    TxtAccountError.Text = Loc.Get("label_please_enter_your_invite_code");
                    BtnAccountSubmit.IsEnabled = true;
                    return;
                }

                // Validate confirm password
                if (TxtPasswordConfirm.Password != password)
                {
                    TxtAccountError.Text = Loc.Get("label_passwords_do_not_match");
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
                    TxtAccountError.Text = Loc.Get("label_please_enter_your_display_name");
                    BtnAccountSubmit.IsEnabled = true;
                    return;
                }

                // Login mode
                await TryAccountLoginAsync(displayName, password);
            }
        }

        private async Task TryAccountLoginAsync(string displayName, string password)
        {
            ShowLoading(Loc.Get("login_logging_in"));

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
                    TxtAccountError.Text = Loc.Get("label_unexpected_response_from_server");
                }
            }
            catch (Exception ex)
            {
                ClearSensitiveData();
                App.Logger?.Error(ex, "Account login failed");
                ShowAccountPanel(_isAccountRegisterMode);
                TxtLoginDisplayName.Text = displayName;
                TxtAccountError.Text = Loc.Get("label_login_failed_please_try_again");
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
            DeviceCodePanel.Visibility = Visibility.Collapsed;
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
            DeviceCodePanel.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            MessageBox.Show(this, message, Loc.Get("title_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
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

            // Stop device-code polling if it's running.
            _deviceCts?.Cancel();
            _deviceCode = null;

            // Clear sensitive data (audit C1)
            ClearSensitiveData();

            DialogResult = false;
            Close();
        }

        #region SP3 Device-Code Flow

        private async void BtnLoginDeviceCode_Click(object sender, RoutedEventArgs e)
        {
            ShowLoading("Generating sign-in code...");

            try
            {
                var svc = new V2DeviceCodeService();
                var resp = await svc.InitiateAsync();

                if (!resp.Success || string.IsNullOrEmpty(resp.Code))
                {
                    ShowError(SanitizeError(resp.Error));
                    return;
                }

                _deviceCode = resp.Code;
                _deviceCodeExpiresAt = resp.ExpiresAt;

                ShowDeviceCodePanel(resp.Code);
                OpenVerificationUrl();

                _deviceCts?.Cancel();
                _deviceCts?.Dispose();
                _deviceCts = new CancellationTokenSource();
                _ = PollDeviceCodeLoopAsync(_deviceCts.Token);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[DeviceCode] Initiate exception");
                ShowError(Loc.Get("login_failed_please_try_again"));
            }
        }

        private void ShowDeviceCodePanel(string code)
        {
            ProviderPanel.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
            UsernamePanel.Visibility = Visibility.Collapsed;
            AccountPanel.Visibility = Visibility.Collapsed;
            DeviceCodePanel.Visibility = Visibility.Visible;

            // Display 6-char code as "ABC-DEF" for legibility.
            TxtDeviceCode.Text = code.Length == 6
                ? $"{code.Substring(0, 3)}-{code.Substring(3, 3)}"
                : code;
            TxtDeviceStatus.Text = "Waiting for browser confirmation...";
        }

        private static void OpenVerificationUrl()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = V2DeviceCodeService.VerificationUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[DeviceCode] Failed to open browser");
            }
        }

        /// <summary>
        /// Polls /v2/auth/device/poll until 200 confirmed, 410/404 expired, or
        /// hard expiry (server-returned expires_at). Backs off on 429/503/transient.
        /// </summary>
        private async Task PollDeviceCodeLoopAsync(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_deviceCode)) return;

            var svc = new V2DeviceCodeService();
            int intervalMs = 3000;
            int consecutiveUnknown = 0;

            try
            {
                // Initial wait so the user has time to switch to the browser.
                await Task.Delay(intervalMs, ct);

                while (!ct.IsCancellationRequested)
                {
                    // Hard expiry guard against server-returned expires_at —
                    // don't trust local clock for "is the code expired" logic
                    // beyond this comparison against a server-issued timestamp.
                    if (DateTimeOffset.UtcNow > _deviceCodeExpiresAt)
                    {
                        HandleDeviceCodeExpired();
                        return;
                    }

                    var result = await svc.PollAsync(_deviceCode!, ct);
                    if (ct.IsCancellationRequested) return;

                    switch (result.Status)
                    {
                        case V2DeviceCodeService.PollStatus.Confirmed:
                            HandleDeviceCodeConfirmed(result);
                            return;

                        case V2DeviceCodeService.PollStatus.Pending:
                            intervalMs = 3000;
                            consecutiveUnknown = 0;
                            TxtDeviceStatus.Text = "Waiting for browser confirmation...";
                            break;

                        case V2DeviceCodeService.PollStatus.Expired:
                        case V2DeviceCodeService.PollStatus.NotFound:
                            HandleDeviceCodeExpired();
                            return;

                        case V2DeviceCodeService.PollStatus.RateLimited:
                        case V2DeviceCodeService.PollStatus.ServiceUnavailable:
                            intervalMs = Math.Min(intervalMs * 2, 30000);
                            TxtDeviceStatus.Text = "Connection busy, retrying...";
                            break;

                        case V2DeviceCodeService.PollStatus.BadRequest:
                        case V2DeviceCodeService.PollStatus.Unauthorized:
                            HandleDeviceCodeError("Sign-in failed. Please try again.");
                            return;

                        case V2DeviceCodeService.PollStatus.Unknown:
                        default:
                            consecutiveUnknown++;
                            if (consecutiveUnknown >= 5)
                            {
                                HandleDeviceCodeError("Lost connection to server. Please try again.");
                                return;
                            }
                            intervalMs = Math.Min(intervalMs * 2, 30000);
                            TxtDeviceStatus.Text = "Connection issue, retrying...";
                            break;
                    }

                    await Task.Delay(intervalMs, ct);
                }
            }
            catch (OperationCanceledException) { /* user cancelled */ }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[DeviceCode] Poll loop crashed");
                HandleDeviceCodeError("Unexpected error. Please try again.");
            }
        }

        private void HandleDeviceCodeConfirmed(V2DeviceCodeService.PollResponse result)
        {
            if (string.IsNullOrEmpty(result.AuthToken) || string.IsNullOrEmpty(result.UnifiedId))
            {
                HandleDeviceCodeError("Server returned an incomplete response.");
                return;
            }

            // Store auth_token via DPAPI (same protection as legacy paths).
            // unified_id is plaintext in settings.json — same as every other provider.
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.AuthToken = result.AuthToken;
                App.Settings.Current.UnifiedId = result.UnifiedId;
                App.Settings.Save();
            }
            App.UnifiedUserId = result.UnifiedId;

            Result = new LoginResult
            {
                Success = true,
                IsLegacyUser = false,
                ShouldShowOgWelcome = false,
                UnifiedId = result.UnifiedId,
                DisplayName = App.Settings?.Current?.UserDisplayName,
                Provider = "device_code"
            };

            DialogResult = true;
            Close();
        }

        private void HandleDeviceCodeExpired()
        {
            _deviceCts?.Cancel();
            _deviceCode = null;
            MessageBox.Show(this,
                "Sign-in code expired. Please try again.",
                Loc.Get("title_error"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ShowProviderSelection();
        }

        private void HandleDeviceCodeError(string message)
        {
            _deviceCts?.Cancel();
            _deviceCode = null;
            MessageBox.Show(this,
                message,
                Loc.Get("title_error"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ShowProviderSelection();
        }

        private void BtnDeviceCodeCopy_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_deviceCode)) return;
            try
            {
                Clipboard.SetText(_deviceCode);
                TxtDeviceStatus.Text = "Code copied. Paste in your browser.";
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[DeviceCode] Clipboard copy failed");
            }
        }

        private void BtnDeviceCodeOpenBrowser_Click(object sender, RoutedEventArgs e)
        {
            OpenVerificationUrl();
        }

        private void BtnDeviceCodeCancel_Click(object sender, MouseButtonEventArgs e)
        {
            _deviceCts?.Cancel();
            _deviceCode = null;
            ShowProviderSelection();
        }

        #endregion

        #endregion
    }
}
