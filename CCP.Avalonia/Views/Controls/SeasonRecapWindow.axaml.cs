using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// The season-rollover surface: the recap card plus the share actions and a "continue to next
    /// season" button. Also reused as the secondary re-view surface from the profile/stats screen.
    ///
    /// PORTED from ConditioningControlPanel/Controls/SeasonRecapWindow.xaml.cs. Deviations:
    ///  - <c>CardExporter</c> (RenderTargetBitmap, clipboard, Pictures folder) is WPF-head only, so
    ///    the four share actions report <c>recap_toast_error</c> rather than claiming a card was
    ///    copied or saved. <see cref="OpenUrl"/> is kept: UseShellExecute works on Linux.
    ///  - A parameterless constructor with sample data exists for the headless render.
    /// </summary>
    public partial class SeasonRecapWindow : Window
    {
        private readonly SeasonRecapCardViewModel _vm;
        private readonly SeasonRecapCard _card;
        private readonly TextBlock _status;
        private DispatcherTimer? _statusTimer;

        /// <summary>Render constructor: sample data, so --render-all can discover the window.</summary>
        public SeasonRecapWindow() : this(SeasonRecapCardViewModel.Sample()) { }

        public SeasonRecapWindow(SeasonRecapCardViewModel vm)
        {
            AvaloniaXamlLoader.Load(this);
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));

            _card = new SeasonRecapCard { AnimateReveal = true };
            _card.SetViewModel(vm);
            this.FindControl<Border>("PART_CardHost")!.Child = _card;
            _status = this.FindControl<TextBlock>("PART_Status")!;

            this.FindControl<Button>("BtnCopy")!.Click += OnCopy;
            this.FindControl<Button>("BtnSave")!.Click += OnSave;
            this.FindControl<Button>("BtnShareX")!.Click += OnShareX;
            this.FindControl<Button>("BtnShareReddit")!.Click += OnShareReddit;
            this.FindControl<Button>("BtnContinue")!.Click += OnContinue;
            this.FindControl<TextBlock>("TxtContinue")!.Text =
                Loc.GetF("recap_btn_continue", _vm.NextSeasonNumber.ToString("00"));
        }

        // ---------- share actions ----------
        // ponytail: needs CardExporter (WPF RenderTargetBitmap + clipboard + Pictures folder),
        // wired when an Avalonia exporter exists. Until then every action says the card could not
        // be prepared, which is true.
        private void OnCopy(object? sender, RoutedEventArgs e) => ShowStatus(Loc.Get("recap_toast_error"));

        private void OnSave(object? sender, RoutedEventArgs e) => ShowStatus(Loc.Get("recap_toast_error"));

        private void OnShareX(object? sender, RoutedEventArgs e) => ShowStatus(Loc.Get("recap_toast_error"));

        private void OnShareReddit(object? sender, RoutedEventArgs e) => ShowStatus(Loc.Get("recap_toast_error"));

        private void OnContinue(object? sender, RoutedEventArgs e) => Close();

        // ---------- helpers ----------
        private static bool OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SeasonRecap: failed to open URL {Url}", url);
                return false;
            }
        }

        private void ShowStatus(string message)
        {
            _status.Text = message;
            _status.IsVisible = true;

            _statusTimer?.Stop();
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _statusTimer.Tick += (s, e) =>
            {
                _statusTimer?.Stop();
                _status.IsVisible = false;
            };
            _statusTimer.Start();
        }
    }
}
