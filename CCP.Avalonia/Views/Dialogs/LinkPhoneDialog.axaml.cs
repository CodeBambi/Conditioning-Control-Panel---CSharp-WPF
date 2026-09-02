using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Shows a short-lived one-time QR code (proxy /v2/auth/device/authorize) that the
    /// CCP mobile app scans to sign in as this account. The phone redeems the code for
    /// its own device token, so this desktop session is never invalidated. Codes live
    /// ~3 minutes; a countdown timer auto-refreshes the UI state on expiry.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/LinkPhoneDialog.xaml.cs. Deviations:
    ///  - <c>V2AuthService.AuthorizeMobileLinkAsync</c> and QRCoder both live in the WPF head,
    ///    so <see cref="FetchCode"/> is a stub with placeholder data. Everything downstream of it
    ///    - the ABC-DEF formatting, the countdown, the expiry reset - is the original logic.
    ///  - The stub runs from the constructor, not from <c>Loaded</c>, so the headless render does
    ///    not depend on <c>Loaded</c> timing: a placeholder that never ran would be
    ///    indistinguishable from one that failed.
    ///  - <c>Unloaded</c> -> <c>Closed</c> for stopping the timer. --render-all runs every view in
    ///    one process; a 1s timer still ticking against a closed window is pure noise.
    ///  - <c>DragMove()</c> -> <c>BeginMoveDrag(e)</c>; Serilog is dropped, the head has no
    ///    reference to it and the stub has nothing to log.
    /// </summary>
    public partial class LinkPhoneDialog : Window
    {
        private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly Image _imgQrCode;
        private readonly TextBlock _txtLinkCode;
        private readonly TextBlock _txtStatus;
        private readonly Button _btnRefresh;
        private DateTimeOffset _expiresAt;

        public LinkPhoneDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _imgQrCode = this.FindControl<Image>("ImgQrCode")!;
            _txtLinkCode = this.FindControl<TextBlock>("TxtLinkCode")!;
            _txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            _btnRefresh = this.FindControl<Button>("BtnRefresh")!;

            _countdown.Tick += Countdown_Tick;
            _btnRefresh.Click += (_, _) => FetchCode();
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
            PointerPressed += Window_PointerPressed;
            Closed += (_, _) => _countdown.Stop();

            FetchCode();
        }

        /// <summary>
        /// ponytail: needs V2AuthService.AuthorizeMobileLinkAsync + QRCoder, wired when they move
        /// to Core. Until then this hands the real display path a placeholder code and expiry, so
        /// the formatting, the QR scaling and the countdown are all still exercised.
        /// </summary>
        private void FetchCode()
        {
            _countdown.Stop();
            _btnRefresh.IsEnabled = false;

            var code = "ABCDEF";

            // Format ABC-DEF for readability; the app strips the dash on entry.
            _txtLinkCode.Text = code.Length == 6 ? $"{code[..3]}-{code[3..]}" : code;
            RenderQr($"ccpmobile://link?c={code}");

            _expiresAt = DateTimeOffset.UtcNow.AddMinutes(3);
            _countdown.Start();
            Countdown_Tick(null, EventArgs.Empty);
        }

        private void Countdown_Tick(object? sender, EventArgs e)
        {
            var remaining = _expiresAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                _countdown.Stop();
                _imgQrCode.Source = null;
                _txtLinkCode.Text = "--- ---";
                _txtStatus.Text = "Code expired — get a new one.";
                _btnRefresh.IsEnabled = true;
                return;
            }
            _btnRefresh.IsEnabled = true;
            _txtStatus.Text = $"Code expires in {remaining.Minutes}:{remaining.Seconds:D2}";
        }

        /// <summary>
        /// Placeholder stand-in for the QRCoder render (dark-pink modules on the white card).
        /// A real payload needs the generator; a 25x25 bitmap blown up to 220 still proves the
        /// Image draws and that BitmapInterpolationMode="None" keeps the modules square.
        /// </summary>
        private void RenderQr(string payload)
        {
            const int n = 25;
            var bmp = new WriteableBitmap(new PixelSize(n, n), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Opaque);
            using (var fb = bmp.Lock())
            {
                var row = new byte[fb.RowBytes];
                for (var y = 0; y < n; y++)
                {
                    for (var x = 0; x < n; x++)
                    {
                        // Deterministic from the payload so a different code looks different, and
                        // a 7x7 finder square in each of the three usual corners.
                        var finder = (x < 7 || x >= n - 7) && y < 7 || x < 7 && y >= n - 7;
                        var dark = finder
                            ? x % 6 != 1 && y % 6 != 1
                            : ((x * 7 + y * 13 + payload.Length) % 5) < 2;
                        row[x * 4 + 0] = dark ? (byte)0x50 : (byte)0xFF; // B
                        row[x * 4 + 1] = dark ? (byte)0x0A : (byte)0xFF; // G
                        row[x * 4 + 2] = dark ? (byte)0x8B : (byte)0xFF; // R
                        row[x * 4 + 3] = 0xFF;
                    }
                    Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, fb.RowBytes);
                }
            }
            _imgQrCode.Source = bmp;
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }
    }
}
