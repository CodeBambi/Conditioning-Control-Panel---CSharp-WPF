using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Shows a short-lived one-time QR code (proxy /v2/auth/device/authorize) that the
    /// CCP mobile app scans to sign in as this account. The phone redeems the code for
    /// its own device token, so this desktop session is never invalidated. Codes live
    /// ~3 minutes; a countdown timer auto-refreshes the UI state on expiry.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/LinkPhoneDialog.xaml.cs over the same Core
    /// <see cref="V2AuthService.AuthorizeMobileLinkAsync"/> and QRCoder. Deviations:
    ///  - The fetch runs on <c>Opened</c> (WPF <c>Loaded</c>); the timer stops on <c>Closed</c>.
    ///  - <c>DragMove()</c> -> <c>BeginMoveDrag(e)</c>.
    ///  - <see cref="Auth"/> and <see cref="Clock"/> are test seams (fake wire, stepped clock).
    /// </summary>
    public partial class LinkPhoneDialog : Window
    {
        /// <summary>Test seam: the auth client the next dialog uses.</summary>
        internal static Func<V2AuthService> Auth = () => new V2AuthService();
        /// <summary>Test seam: the clock the countdown reads (WPF DateTimeOffset.UtcNow).</summary>
        internal static TimeProvider Clock = TimeProvider.System;

        private readonly V2AuthService _v2Auth = Auth();
        private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly Image _imgQrCode;
        private readonly TextBlock _txtLinkCode;
        private readonly TextBlock _txtStatus;
        private readonly Button _btnRefresh;
        private DateTimeOffset _expiresAt;
        private bool _fetching;

        /// <summary>The fetch in flight (tests await it).</summary>
        internal Task Fetch { get; private set; } = Task.CompletedTask;
        internal bool CountdownRunning => _countdown.IsEnabled;

        public LinkPhoneDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _imgQrCode = this.FindControl<Image>("ImgQrCode")!;
            _txtLinkCode = this.FindControl<TextBlock>("TxtLinkCode")!;
            _txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            _btnRefresh = this.FindControl<Button>("BtnRefresh")!;
            _txtStatus.Text = Loc.Get("status_link_phone_fetching");

            _countdown.Tick += (_, _) => Tick();
            _btnRefresh.Click += (_, _) => Fetch = FetchCodeAsync();
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
            PointerPressed += Window_PointerPressed;
            // --render-all shows every dialog; it must never reach the account server.
            Opened += (_, _) => { if (!RenderProof.Rendering) Fetch = FetchCodeAsync(); };
            Closed += (_, _) => _countdown.Stop();
        }

        private async Task FetchCodeAsync()
        {
            if (_fetching) return;
            _fetching = true;
            _countdown.Stop();
            _btnRefresh.IsEnabled = false;
            _txtStatus.Text = Loc.Get("status_link_phone_fetching");
            _imgQrCode.Source = null;
            _txtLinkCode.Text = "--- ---";

            try
            {
                var result = await _v2Auth.AuthorizeMobileLinkAsync();
                if (!result.Success || string.IsNullOrEmpty(result.LinkCode))
                {
                    _txtStatus.Text = result.Error switch
                    {
                        "not_logged_in" => "You need to be logged in to link a phone.",
                        "invalid_auth_token" => "Your session has expired — please log in again.",
                        "legacy_user_reauth_required" => "Your session has expired — please log in again.",
                        "rate_limited" => "Too many codes requested. Wait a minute and try again.",
                        _ => $"Couldn't get a link code ({result.Error ?? "unknown error"}). Try again."
                    };
                    _btnRefresh.IsEnabled = true;
                    return;
                }

                // Format ABC-DEF for readability; the app strips the dash on entry.
                var code = result.LinkCode;
                _txtLinkCode.Text = code.Length == 6 ? $"{code[..3]}-{code[3..]}" : code;
                RenderQr(result.QrPayload ?? $"ccpmobile://link?c={code}");

                _expiresAt = result.ExpiresAt;
                if (!IsVisible) return; // closed while fetching: no timer for a closed window
                _countdown.Start();
                Tick();
            }
            finally
            {
                _fetching = false;
                if (!_countdown.IsEnabled) _btnRefresh.IsEnabled = true;
            }
        }

        /// <summary>WPF Countdown_Tick.</summary>
        internal void Tick()
        {
            var remaining = _expiresAt - Clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                _countdown.Stop();
                _imgQrCode.Source = null;
                _txtLinkCode.Text = "--- ---";
                _txtStatus.Text = Loc.Get("msg_link_phone_code_expired");
                _btnRefresh.IsEnabled = true;
                return;
            }
            _btnRefresh.IsEnabled = true;
            _txtStatus.Text = $"Code expires in {remaining.Minutes}:{remaining.Seconds:D2}";
        }

        /// <summary>WPF RenderQr: QRCoder ECC M, 10 px modules, dark pink #8B0A50 on white.</summary>
        private void RenderQr(string payload)
        {
            try
            {
                using var generator = new QRCoder.QRCodeGenerator();
                using var data = generator.CreateQrCode(payload, QRCoder.QRCodeGenerator.ECCLevel.M);
                var bytes = new QRCoder.PngByteQRCode(data).GetGraphic(10, new byte[] { 0x8B, 0x0A, 0x50 }, new byte[] { 0xFF, 0xFF, 0xFF });
                _imgQrCode.Source = new global::Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(bytes));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[LinkPhone] Failed to render QR code");
                _txtStatus.Text = Loc.Get("msg_link_phone_qr_failed");
            }
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }
    }
}
