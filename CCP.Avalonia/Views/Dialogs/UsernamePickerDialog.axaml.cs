using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Dialog for choosing a display name on first login.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/UsernamePickerDialog.xaml.cs. Deviations:
    ///  - <c>DialogResult</c> becomes <c>Close(bool)</c>; Avalonia carries the result through
    ///    <c>ShowDialog&lt;bool?&gt;</c>.
    ///  - <c>MouseLeftButtonDown</c> + <c>DragMove()</c> -> <c>PointerPressed</c> + <c>BeginMoveDrag</c>.
    ///  - Newtonsoft's <c>JObject</c> -> <c>System.Text.Json</c>: the head has no Newtonsoft
    ///    reference and one bool read does not justify adding one.
    ///  - <c>App.Logger</c> in the availability catch is dropped; the logger is on the WPF App.
    ///  - <c>ConfigureForNewUser</c> no longer re-assigns TxtSubtitle.Text. The WPF body set it to
    ///    the very key the XAML already carries, and assigning over a <c>{loc:Str}</c> binding is
    ///    undone on the next language change (see CLAUDE.md).
    ///  - <c>_isAvailable</c> is gone: it only ever mirrored <c>BtnConfirm.IsEnabled</c>.
    ///
    /// ponytail: the display-name endpoint is hardcoded here exactly as in WPF. It belongs behind a
    /// Core API client; hoist it when a second view needs the same call.
    /// </summary>
    public partial class UsernamePickerDialog : Window
    {
        private static readonly HttpClient _http = new();
        private readonly string _serverUrl = "https://codebambi-proxy.vercel.app";
        private CancellationTokenSource? _checkCts;

        private readonly TextBox _txtUsername;
        private readonly TextBlock _txtAvailability;
        private readonly Button _btnConfirm;

        /// <summary>
        /// The chosen display name (null if cancelled)
        /// </summary>
        public string? ChosenDisplayName { get; private set; }

        public UsernamePickerDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _txtUsername = this.FindControl<TextBox>("TxtUsername")!;
            _txtAvailability = this.FindControl<TextBlock>("TxtAvailability")!;
            _btnConfirm = this.FindControl<Button>("BtnConfirm")!;

            // Handlers live here rather than in markup, per the porting convention.
            _txtUsername.TextChanged += TxtUsername_TextChanged;
            this.FindControl<Button>("BtnUseSuggestion")!.Click += (_, _) => BtnUseSuggestion_Click();
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => BtnCancel_Click();
            _btnConfirm.Click += (_, _) => BtnConfirm_Click();
            PointerPressed += Window_PointerPressed;
        }

        /// <summary>
        /// Show the dialog configured for a new user
        /// </summary>
        public void ConfigureForNewUser()
        {
            this.FindControl<Border>("OgWelcomePanel")!.IsVisible = false;
            this.FindControl<StackPanel>("SuggestionPanel")!.IsVisible = false;
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Bubbling, so the TextBox and the buttons have already marked their own presses
            // handled - the same reason WPF's DragMove() never fired from inside them.
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private async void TxtUsername_TextChanged(object? sender, TextChangedEventArgs e)
        {
            var name = (_txtUsername.Text ?? "").Trim();

            // Cancel any pending check
            _checkCts?.Cancel();
            _checkCts = new CancellationTokenSource();
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

        private void SetAvailabilityStatus(string message, IBrush color, bool available)
        {
            _txtAvailability.Text = message;
            _txtAvailability.Foreground = color;
            _btnConfirm.IsEnabled = available;
        }

        private async Task<bool> CheckNameAvailabilityAsync(string name)
        {
            try
            {
                var response = await _http.GetAsync($"{_serverUrl}/user/check-display-name?display_name={Uri.EscapeDataString(name)}");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    return doc.RootElement.TryGetProperty("available", out var flag)
                           && flag.ValueKind == JsonValueKind.True;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _checkCts?.Cancel();
            _checkCts?.Dispose();
            _checkCts = null;
            base.OnClosed(e);
        }

        private void BtnUseSuggestion_Click()
        {
            // Legacy suggestion panel - always collapsed, kept for XAML compatibility
        }

        private void BtnCancel_Click()
        {
            ChosenDisplayName = null;
            Close(false);
        }

        private void BtnConfirm_Click()
        {
            if (!_btnConfirm.IsEnabled) return;

            ChosenDisplayName = (_txtUsername.Text ?? "").Trim();
            Close(true);
        }
    }
}
