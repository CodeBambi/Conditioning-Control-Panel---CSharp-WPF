using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Unified login dialog that handles provider selection and new user registration.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/LoginDialog.xaml.cs. Deviations:
    ///  - WPF's <c>DialogResult</c> property becomes <c>Close(bool)</c>; Avalonia carries the
    ///    result through <c>ShowDialog&lt;bool&gt;</c>. Only the cancel path can set it here - the
    ///    success paths belong to the stubbed services.
    ///  - <c>DragMove()</c> -> <c>BeginMoveDrag(e)</c>. The three clickable TextBlocks (the
    ///    login/register toggle, Back and the device-code Cancel) mark their PointerPressed
    ///    handled, or the window-level drag handler would fire on the same click.
    ///  - <c>PasswordBox.Password</c> -> <c>TextBox.Text</c> (Avalonia has no PasswordBox; the
    ///    markup uses <c>PasswordChar</c>).
    ///  - <c>CharacterCasing="Upper"</c> has no Avalonia equivalent, so the invite-code box
    ///    upper-cases in a TextChanged handler, preserving the caret.
    ///  - <c>FocusSafely</c> collapses to a plain <c>Focus()</c>. Its dispatcher retry existed for
    ///    a WPF template-trigger crash ("the 'Bd' name-scope bug") that has no Avalonia analogue.
    ///  - Label swaps REBIND rather than assign <c>.Text</c>: those controls carry a
    ///    <c>{loc:Str}</c> binding and a local value is undone on the next language change (see the
    ///    porting notes in CLAUDE.md). Same keys, chosen in code. The two TxtAccountToggle Runs are
    ///    rebound by index instead of the WPF <c>Inlines.Clear()</c>/<c>Add(new Run(...))</c>
    ///    rebuild, for the same reason.
    ///  - <c>ShowUsernamePanel</c>'s two title assignments are dropped: they set exactly the keys
    ///    the markup already binds.
    ///  - <c>SanitizeError</c> and <c>ShowError</c> are gone with their only callers. Both served
    ///    the V2AuthService paths stubbed below, and WPF's <c>MessageBox</c> has no Avalonia
    ///    equivalent and no package may be added; they return with the services.
    ///  - <c>_firstProviderToken</c> is dropped: nothing can set it until the OAuth services move
    ///    to Core, and a field that is only ever null makes the stubs read as dead branches.
    ///
    /// Placeholder behaviour while the services live in the WPF head: a provider button jumps
    /// straight to the display-name picker (the "needs registration" branch), the name-availability
    /// check always says available, and Sign in via Web shows a fixed sample code with no polling.
    /// </summary>
    public partial class LoginDialog : Window
    {
        /// <summary>
        /// ponytail: copied from ConditioningControlPanel/Services/Account/V2DeviceCodeService.cs
        /// (VerificationUrl). Point back at that constant when the service moves to Core.
        /// </summary>
        private const string VerificationUrl = "https://app.cclabs.app/dashboard/link-device";

        private CancellationTokenSource? _checkCts;

        // Track which provider was tried first
        private string? _firstProvider;
        private bool _isNameAvailable;
        private bool _isAccountRegisterMode;  // True = register mode, false = login mode
        private string? _pendingInviteCode;
        private string? _pendingPassword;

        private readonly StackPanel _providerPanel;
        private readonly StackPanel _accountPanel;
        private readonly StackPanel _usernamePanel;
        private readonly StackPanel _loadingPanel;
        private readonly StackPanel _deviceCodePanel;

        private readonly TextBlock _txtAccountTitle;
        private readonly TextBlock _lblInviteCodeHint;
        private readonly TextBlock _lblInviteCode;
        private readonly TextBox _txtInviteCode;
        private readonly TextBlock _lblDisplayName;
        private readonly TextBox _txtLoginDisplayName;
        private readonly TextBlock _lblPasswordConfirm;
        private readonly TextBox _txtPassword;
        private readonly TextBox _txtPasswordConfirm;
        private readonly TextBlock _txtAccountError;
        private readonly Button _btnAccountSubmit;
        private readonly TextBlock _txtAccountSubmitLabel;
        private readonly Run _runToggleLead;
        private readonly Run _runToggleLink;

        private readonly TextBox _txtUsername;
        private readonly TextBlock _txtAvailability;
        private readonly Button _btnConfirmUsername;

        private readonly TextBlock _txtLoadingMessage;

        private readonly TextBlock _txtDeviceCode;
        private readonly TextBox _txtVerificationUrl;
        private readonly TextBlock _txtDeviceStatus;

        private readonly Button _btnLoginDiscord;
        private readonly Button _btnLoginPatreon;

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
            AvaloniaXamlLoader.Load(this);

            _providerPanel = this.FindControl<StackPanel>("ProviderPanel")!;
            _accountPanel = this.FindControl<StackPanel>("AccountPanel")!;
            _usernamePanel = this.FindControl<StackPanel>("UsernamePanel")!;
            _loadingPanel = this.FindControl<StackPanel>("LoadingPanel")!;
            _deviceCodePanel = this.FindControl<StackPanel>("DeviceCodePanel")!;

            _txtAccountTitle = this.FindControl<TextBlock>("TxtAccountTitle")!;
            _lblInviteCodeHint = this.FindControl<TextBlock>("LblInviteCodeHint")!;
            _lblInviteCode = this.FindControl<TextBlock>("LblInviteCode")!;
            _txtInviteCode = this.FindControl<TextBox>("TxtInviteCode")!;
            _lblDisplayName = this.FindControl<TextBlock>("LblDisplayName")!;
            _txtLoginDisplayName = this.FindControl<TextBox>("TxtLoginDisplayName")!;
            _lblPasswordConfirm = this.FindControl<TextBlock>("LblPasswordConfirm")!;
            _txtPassword = this.FindControl<TextBox>("TxtPassword")!;
            _txtPasswordConfirm = this.FindControl<TextBox>("TxtPasswordConfirm")!;
            _txtAccountError = this.FindControl<TextBlock>("TxtAccountError")!;
            _btnAccountSubmit = this.FindControl<Button>("BtnAccountSubmit")!;
            _txtAccountSubmitLabel = this.FindControl<TextBlock>("TxtAccountSubmitLabel")!;

            // FindControl cannot reach a Run - it is an Inline, not a Control - so the two loc Runs
            // come off the collection by position. Index 1 is the literal single space between them.
            var toggle = this.FindControl<TextBlock>("TxtAccountToggle")!;
            _runToggleLead = (Run)toggle.Inlines![0];
            _runToggleLink = (Run)toggle.Inlines![2];

            _txtUsername = this.FindControl<TextBox>("TxtUsername")!;
            _txtAvailability = this.FindControl<TextBlock>("TxtAvailability")!;
            _btnConfirmUsername = this.FindControl<Button>("BtnConfirmUsername")!;

            _txtLoadingMessage = this.FindControl<TextBlock>("TxtLoadingMessage")!;

            _txtDeviceCode = this.FindControl<TextBlock>("TxtDeviceCode")!;
            _txtVerificationUrl = this.FindControl<TextBox>("TxtVerificationUrl")!;
            _txtDeviceStatus = this.FindControl<TextBlock>("TxtDeviceStatus")!;

            _btnLoginDiscord = this.FindControl<Button>("BtnLoginDiscord")!;
            _btnLoginPatreon = this.FindControl<Button>("BtnLoginPatreon")!;

            // Handlers live here rather than in markup, per the porting convention.
            _btnLoginDiscord.Click += (_, _) => TryLoginWithProvider("discord");
            _btnLoginPatreon.Click += (_, _) => TryLoginWithProvider("patreon");
            this.FindControl<Button>("BtnLoginSubscribeStar")!.Click += (_, _) => TryLoginWithProvider("substar");
            this.FindControl<Button>("BtnLoginAccount")!.Click += (_, _) => ShowAccountPanel(isRegister: false);
            this.FindControl<Button>("BtnLoginDeviceCode")!.Click += (_, _) => BtnLoginDeviceCode_Click();

            _btnAccountSubmit.Click += (_, _) => BtnAccountSubmit_Click();
            toggle.PointerPressed += (_, e) => { e.Handled = true; ShowAccountPanel(!_isAccountRegisterMode); };
            this.FindControl<TextBlock>("BtnAccountBack")!.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                ClearSensitiveData();
                ShowProviderSelection();
            };

            _txtUsername.TextChanged += TxtUsername_TextChanged;
            _btnConfirmUsername.Click += (_, _) => BtnConfirmUsername_Click();

            this.FindControl<Button>("BtnDeviceCodeCopy")!.Click += (_, _) => BtnDeviceCodeCopy_Click();
            this.FindControl<Button>("BtnDeviceCodeOpenBrowser")!.Click += (_, _) => OpenVerificationUrl();
            this.FindControl<TextBlock>("BtnDeviceCodeCancel")!.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                ShowProviderSelection();
            };

            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => BtnCancel_Click();

            // WPF's CharacterCasing="Upper". Assigning the same string does not re-raise
            // TextChanged, so this cannot loop.
            _txtInviteCode.TextChanged += (_, _) =>
            {
                var text = _txtInviteCode.Text;
                if (string.IsNullOrEmpty(text)) return;
                var upper = text.ToUpperInvariant();
                if (upper == text) return;
                var caret = _txtInviteCode.CaretIndex;
                _txtInviteCode.Text = upper;
                _txtInviteCode.CaretIndex = caret;
            };

            PointerPressed += Window_PointerPressed;

            // Cancel any in-flight availability check on dialog close so alt+F4 / parent .Close() /
            // session-end don't leave an orphan task running against a hidden window. The WPF
            // original guarded its device-code poll loop here for the same reason; that loop needs
            // the service and is stubbed below, so _checkCts is what is left to cancel.
            Closed += (_, _) =>
            {
                _checkCts?.Cancel();
                _checkCts?.Dispose();
                _checkCts = null;
            };
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        /// <summary>Rebind a control's Text to a loc key, rather than assigning the string. A local
        /// value would win over the {loc:Str} binding until the next language change and then be
        /// silently replaced - see the porting notes in CLAUDE.md.</summary>
        private static void BindLoc(TextBlock target, string key) =>
            target.Bind(TextBlock.TextProperty, LocBinding(key));

        private static void BindLoc(Run target, string key) =>
            target.Bind(Run.TextProperty, LocBinding(key));

        private static Binding LocBinding(string key) => new($"[{key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay,
        };

        /// <summary>Clear all sensitive data from memory and UI fields.</summary>
        private void ClearSensitiveData()
        {
            _pendingInviteCode = null;
            _pendingPassword = null;
            _txtPassword.Text = "";
            _txtPasswordConfirm.Text = "";
            _checkCts?.Cancel();
            _checkCts?.Dispose();
            _checkCts = null;
        }

        #region Provider Selection

        /// <summary>
        /// ponytail: needs App.Discord / App.Patreon / App.SubscribeStar and V2AuthService, wired
        /// when they move to Core. Until then this takes the "user needs registration" branch the
        /// real flow reaches after a successful OAuth handshake, so the display-name picker and
        /// everything downstream of it stays reachable from the provider buttons.
        /// </summary>
        private void TryLoginWithProvider(string provider)
        {
            ShowLoading(Loc.GetF("login_connecting_to_provider", provider));
            _firstProvider = provider;
            ShowUsernamePanel();
        }

        #endregion

        #region Username Entry

        private void ShowUsernamePanel()
        {
            _providerPanel.IsVisible = false;
            _loadingPanel.IsVisible = false;
            _usernamePanel.IsVisible = true;
            _accountPanel.IsVisible = false;
            _deviceCodePanel.IsVisible = false;

            _btnConfirmUsername.IsEnabled = true;
            _txtUsername.Focus();
        }

        private async void TxtUsername_TextChanged(object? sender, TextChangedEventArgs e)
        {
            var name = (_txtUsername.Text ?? "").Trim();

            _checkCts?.Cancel();
            _checkCts?.Dispose();
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
            catch (Exception)
            {
                SetAvailabilityStatus(Loc.Get("login_error_checking_name"), Brushes.Orange, false);
            }
        }

        private void SetAvailabilityStatus(string message, IBrush color, bool available)
        {
            _txtAvailability.Text = message;
            _txtAvailability.Foreground = color;
            _isNameAvailable = available;
            _btnConfirmUsername.IsEnabled = available;
        }

        /// <summary>
        /// ponytail: needs the proxy's /v2/auth/check-name and /user/check-display-name endpoints
        /// (the WPF original picks one of three by provider and signs it with the OAuth token),
        /// wired when V2AuthService moves to Core. Answering "available" keeps the picker usable.
        /// </summary>
        private static Task<bool> CheckNameAvailabilityAsync(string name) => Task.FromResult(true);

        /// <summary>
        /// ponytail: needs V2AuthService.RegisterAsync / AuthenticateWith*Async plus App.Settings
        /// and App.UnifiedUserId, wired when they move to Core. The guards and the loading state
        /// are the original's; the account creation itself is what is missing.
        /// </summary>
        private void BtnConfirmUsername_Click()
        {
            if (!_isNameAvailable) return;

            if (_firstProvider == "invite"
                && (string.IsNullOrEmpty(_pendingInviteCode) || string.IsNullOrEmpty(_pendingPassword)))
            {
                ClearSensitiveData();
                _txtAvailability.Text = Loc.Get("login_session_expired");
                return;
            }

            // Disable button during async (audit C2)
            _btnConfirmUsername.IsEnabled = false;
            ShowLoading(Loc.Get("login_creating_account"));
        }

        #endregion

        #region Account Login (Invite Code + Password)

        private void ShowAccountPanel(bool isRegister)
        {
            _isAccountRegisterMode = isRegister;

            _providerPanel.IsVisible = false;
            _loadingPanel.IsVisible = false;
            _usernamePanel.IsVisible = false;
            _accountPanel.IsVisible = true;
            _deviceCodePanel.IsVisible = false;

            // Clear all fields
            _txtInviteCode.Text = "";
            _txtLoginDisplayName.Text = "";
            _txtPassword.Text = "";
            _txtPasswordConfirm.Text = "";
            _txtAccountError.Text = "";
            _btnAccountSubmit.IsEnabled = true;

            if (isRegister)
            {
                BindLoc(_txtAccountTitle, "label_create_account");
                BindLoc(_txtAccountSubmitLabel, "btn_next");

                // Show invite code + password + confirm; hide display name
                _lblInviteCodeHint.IsVisible = true;
                _lblInviteCode.IsVisible = true;
                _txtInviteCode.IsVisible = true;
                _lblDisplayName.IsVisible = false;
                _txtLoginDisplayName.IsVisible = false;
                _lblPasswordConfirm.IsVisible = true;
                _txtPasswordConfirm.IsVisible = true;

                BindLoc(_runToggleLead, "login_already_have_account");
                BindLoc(_runToggleLink, "btn_login");

                _txtInviteCode.Focus();
            }
            else
            {
                BindLoc(_txtAccountTitle, "btn_login");
                BindLoc(_txtAccountSubmitLabel, "btn_login");

                // Show display name + password; hide invite code + confirm
                _lblInviteCodeHint.IsVisible = false;
                _lblInviteCode.IsVisible = false;
                _txtInviteCode.IsVisible = false;
                _lblDisplayName.IsVisible = true;
                _txtLoginDisplayName.IsVisible = true;
                _lblPasswordConfirm.IsVisible = false;
                _txtPasswordConfirm.IsVisible = false;

                BindLoc(_runToggleLead, "login_dont_have_account");
                BindLoc(_runToggleLink, "btn_create_account");

                _txtLoginDisplayName.Focus();
            }
        }

        private void BtnAccountSubmit_Click()
        {
            var password = _txtPassword.Text ?? "";

            // Validate password (shared for both modes)
            if (password.Length < 8)
            {
                _txtAccountError.Text = Loc.Get("label_password_must_be_at_least_8_characters");
                return;
            }

            // Disable button during async (audit C2)
            _btnAccountSubmit.IsEnabled = false;

            if (_isAccountRegisterMode)
            {
                var inviteCode = (_txtInviteCode.Text ?? "").Trim();

                // Validate invite code
                if (string.IsNullOrWhiteSpace(inviteCode))
                {
                    _txtAccountError.Text = Loc.Get("label_please_enter_your_invite_code");
                    _btnAccountSubmit.IsEnabled = true;
                    return;
                }

                // Validate confirm password
                if ((_txtPasswordConfirm.Text ?? "") != password)
                {
                    _txtAccountError.Text = Loc.Get("label_passwords_do_not_match");
                    _btnAccountSubmit.IsEnabled = true;
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
                var displayName = (_txtLoginDisplayName.Text ?? "").Trim();

                // Validate display name
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    _txtAccountError.Text = Loc.Get("label_please_enter_your_display_name");
                    _btnAccountSubmit.IsEnabled = true;
                    return;
                }

                // Login mode
                TryAccountLogin(displayName, password);
            }
        }

        /// <summary>
        /// ponytail: needs V2AuthService.LoginAsync plus App.Settings / App.UnifiedUserId, wired
        /// when they move to Core. The password is still wiped immediately after the (absent) call,
        /// as audit C1 requires, and the failure path back to the form is the original's.
        /// </summary>
        private void TryAccountLogin(string displayName, string password)
        {
            ShowLoading(Loc.Get("login_logging_in"));

            // Clear password from memory immediately after use (audit C1)
            ClearSensitiveData();

            ShowAccountPanel(_isAccountRegisterMode);
            _txtLoginDisplayName.Text = displayName;
            _txtAccountError.Text = Loc.Get("label_unexpected_response_from_server");
        }

        #endregion

        #region UI Helpers

        private void ShowProviderSelection()
        {
            _providerPanel.IsVisible = true;
            _loadingPanel.IsVisible = false;
            _usernamePanel.IsVisible = false;
            _accountPanel.IsVisible = false;
            _deviceCodePanel.IsVisible = false;
            _btnLoginDiscord.IsEnabled = true;
            _btnLoginPatreon.IsEnabled = true;
        }

        /// <summary>The message is composed at runtime (Loc.GetF), so this assigns rather than
        /// rebinds; the label's {loc:Str} default returns on the next language change, which is the
        /// caveat in CLAUDE.md and is harmless for a transient status line.</summary>
        private void ShowLoading(string message)
        {
            _txtLoadingMessage.Text = message;
            _providerPanel.IsVisible = false;
            _loadingPanel.IsVisible = true;
            _usernamePanel.IsVisible = false;
            _accountPanel.IsVisible = false;
            _deviceCodePanel.IsVisible = false;
        }

        private void BtnCancel_Click()
        {
            // ponytail: WPF logged the authenticated provider out here (App.Discord/SubscribeStar/
            // Patreon .Logout()); wired when those services move to Core.

            // Clear sensitive data (audit C1)
            ClearSensitiveData();

            Result = null;
            Close(false);
        }

        #endregion

        #region SP3 Device-Code Flow

        /// <summary>
        /// ponytail: needs V2DeviceCodeService.InitiateAsync and its /poll loop, wired when the
        /// service moves to Core. The panel is shown with a sample code so the layout, the ABC-DEF
        /// split and the manual-URL fallback are all still exercised; nothing polls.
        /// </summary>
        private void BtnLoginDeviceCode_Click()
        {
            ShowDeviceCodePanel("ABCDEF");
            OpenVerificationUrl();
        }

        private void ShowDeviceCodePanel(string code)
        {
            _providerPanel.IsVisible = false;
            _loadingPanel.IsVisible = false;
            _usernamePanel.IsVisible = false;
            _accountPanel.IsVisible = false;
            _deviceCodePanel.IsVisible = true;

            // Display 6-char code as "ABC-DEF" for legibility.
            _txtDeviceCode.Text = code.Length == 6
                ? $"{code[..3]}-{code[3..]}"
                : code;
            _txtVerificationUrl.Text = VerificationUrl;
            _txtDeviceStatus.Text = "Waiting for browser confirmation...";
        }

        /// <summary>WPF used Process.Start with UseShellExecute; Avalonia's Launcher is the
        /// cross-platform equivalent and needs no shell assumptions.</summary>
        private async void OpenVerificationUrl()
        {
            try { await Launcher.LaunchUriAsync(new Uri(VerificationUrl)); }
            catch (Exception) { /* no browser, or no launcher on this platform - best effort */ }
        }

        private async void BtnDeviceCodeCopy_Click()
        {
            try
            {
                if (Clipboard is null) return;
                await Clipboard.SetTextAsync(_txtDeviceCode.Text?.Replace("-", "") ?? "");
                _txtDeviceStatus.Text = "Code copied. Paste in your browser.";
            }
            catch (Exception)
            {
                // Clipboard can be locked by another app - say nothing, the button just won't confirm.
            }
        }

        #endregion
    }
}
