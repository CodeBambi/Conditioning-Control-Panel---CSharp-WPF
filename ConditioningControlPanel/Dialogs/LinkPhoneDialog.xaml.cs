using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Shows a short-lived one-time QR code (proxy /v2/auth/device/authorize) that the
    /// CCP mobile app scans to sign in as this account. The phone redeems the code for
    /// its own device token, so this desktop session is never invalidated. Codes live
    /// ~3 minutes; a countdown timer auto-refreshes the UI state on expiry.
    /// </summary>
    public partial class LinkPhoneDialog : Window
    {
        private readonly V2AuthService _v2Auth = new();
        private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
        private DateTimeOffset _expiresAt;
        private bool _fetching;

        public LinkPhoneDialog()
        {
            InitializeComponent();
            _countdown.Tick += Countdown_Tick;
            Loaded += async (_, _) => await FetchCodeAsync();
            Unloaded += (_, _) => _countdown.Stop();
        }

        private async System.Threading.Tasks.Task FetchCodeAsync()
        {
            if (_fetching) return;
            _fetching = true;
            _countdown.Stop();
            BtnRefresh.IsEnabled = false;
            TxtStatus.Text = "Fetching code...";
            ImgQrCode.Source = null;
            TxtLinkCode.Text = "--- ---";

            try
            {
                var result = await _v2Auth.AuthorizeMobileLinkAsync();
                if (!result.Success || string.IsNullOrEmpty(result.LinkCode))
                {
                    TxtStatus.Text = result.Error switch
                    {
                        "not_logged_in" => "You need to be logged in to link a phone.",
                        "invalid_auth_token" => "Your session has expired — please log in again.",
                        "legacy_user_reauth_required" => "Your session has expired — please log in again.",
                        "rate_limited" => "Too many codes requested. Wait a minute and try again.",
                        _ => $"Couldn't get a link code ({result.Error ?? "unknown error"}). Try again."
                    };
                    BtnRefresh.IsEnabled = true;
                    return;
                }

                // Format ABC-DEF for readability; the app strips the dash on entry.
                var code = result.LinkCode;
                TxtLinkCode.Text = code.Length == 6 ? $"{code[..3]}-{code[3..]}" : code;
                RenderQr(result.QrPayload ?? $"ccpmobile://link?c={code}");

                _expiresAt = result.ExpiresAt;
                _countdown.Start();
                Countdown_Tick(null, EventArgs.Empty);
            }
            finally
            {
                _fetching = false;
                if (!_countdown.IsEnabled) BtnRefresh.IsEnabled = true;
            }
        }

        private void Countdown_Tick(object? sender, EventArgs e)
        {
            var remaining = _expiresAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                _countdown.Stop();
                ImgQrCode.Source = null;
                TxtLinkCode.Text = "--- ---";
                TxtStatus.Text = "Code expired — get a new one.";
                BtnRefresh.IsEnabled = true;
                return;
            }
            BtnRefresh.IsEnabled = true;
            TxtStatus.Text = $"Code expires in {remaining.Minutes}:{remaining.Seconds:D2}";
        }

        /// <summary>
        /// Renders the QR into ImgQrCode — same QRCoder pattern as
        /// MainWindow.RemoteControl.RefreshRemoteQrCode (PngByteQRCode → BitmapImage).
        /// Dark-pink foreground on the white card behind it for contrast.
        /// </summary>
        private void RenderQr(string payload)
        {
            try
            {
                byte[] fgRgb = { 0x8B, 0x0A, 0x50 };
                byte[] bgRgb = { 0xFF, 0xFF, 0xFF };

                using var generator = new QRCoder.QRCodeGenerator();
                using var data = generator.CreateQrCode(payload, QRCoder.QRCodeGenerator.ECCLevel.M);
                using var qr = new QRCoder.PngByteQRCode(data);
                var bytes = qr.GetGraphic(10, fgRgb, bgRgb);
                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                ImgQrCode.Source = bmp;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[LinkPhone] Failed to render QR code");
                TxtStatus.Text = "Couldn't render the QR — use the code below instead.";
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await FetchCodeAsync();

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
