using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// Z8 · BEHAVIOR, ported from the WPF head with its settings logic restored against
    /// <see cref="CoreSettings"/>.
    ///
    /// <para>On WPF the cell forwards to MainWindow, which writes App.Settings. Four of those
    /// forwards are pure settings writes (MainWindow.Patreon.cs:1176/1188/1834/1870) and happen
    /// here directly; the seed they need came from MainWindow.Patreon.cs:1031-1040, which this head
    /// has no equivalent of, so it is folded into <see cref="SyncFromSettings"/>.</para>
    ///
    /// <para>What stays an event: the two shortcut pills (their real editor is MainWindow's
    /// ChatShortcutCaptureDialog flow, which also re-arms Win32 hotkeys) and the browser pause
    /// (WebView2). Those are host actions, not settings.</para>
    /// </summary>
    public partial class WorkshopBehaviorCell : UserControl
    {
        private bool _isLoading = true;

        public event EventHandler? ChatShortcutRequested;
        public event EventHandler? CameraShortcutRequested;
        /// <summary>Raised with the new IsChecked of the browser-pause switch. Not a setting: WPF
        /// mutes and suspends the WebView2 live and persists nothing.</summary>
        public event EventHandler<bool>? PauseBrowserChanged;

        public WorkshopBehaviorCell()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and the seed below reads them.
            InitializeComponent();

            SliderIdleIntervalCompanion.ValueChanged += SliderIdleInterval_ValueChanged;
            SliderBubbleDurationCompanion.ValueChanged += SliderBubbleDuration_ValueChanged;

            BtnChatShortcut.Click += (_, _) => ChatShortcutRequested?.Invoke(this, EventArgs.Empty);
            BtnCameraShortcut.Click += (_, _) => CameraShortcutRequested?.Invoke(this, EventArgs.Empty);

            // WPF wired Checked/Unchecked separately; Avalonia has one event for both.
            ChkMuteWhispersCompanion.IsCheckedChanged += ChkMuteWhispers_Changed;
            ChkVoiceLinesCompanion.IsCheckedChanged += ChkVoiceLines_Changed;
            ChkTubeMidnightGlass.IsCheckedChanged += ChkTubeMidnightGlass_Changed;
            ChkPauseBrowserCompanion.IsCheckedChanged +=
                (_, _) => { if (!_isLoading) PauseBrowserChanged?.Invoke(this, ChkPauseBrowserCompanion.IsChecked == true); };

            SyncFromSettings();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            // The midnight glass is the one row here whose ENABLED state can change while the app is
            // running (the player buys it at the Prize Counter in another window), so the whole cell
            // is re-read on every reveal rather than seeded once at startup.
            SyncFromSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            base.OnDetachedFromVisualTree(e);
        }

        // A cloud restore or a factory reset swaps the instance; repaint from it, on the UI thread.
        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(SyncFromSettings);

        internal void SyncFromSettings()
        {
            _isLoading = true;
            try
            {
                var s = CoreSettings.Current;

                // "Muted" is its own flag, not the inverse of the enable (AppSettings.SubAudioMuted).
                ChkMuteWhispersCompanion.IsChecked = s.SubAudioMuted;
                ChkVoiceLinesCompanion.IsChecked = s.CompanionVoiceLinesMuted;

                // Labels set explicitly rather than left to ValueChanged, which _isLoading blocks.
                SliderIdleIntervalCompanion.Value = s.IdleGiggleIntervalSeconds;
                TxtIdleIntervalCompanion.Text = $"{s.IdleGiggleIntervalSeconds}s";
                SliderBubbleDurationCompanion.Value = s.BubbleDurationSeconds;
                TxtBubbleDurationCompanion.Text = $"{(int)s.BubbleDurationSeconds}s";

                // ChkPauseBrowserCompanion is deliberately NOT seeded: WPF restores it only from a
                // session snapshot (MainWindow.Patreon.cs:1923), never from settings.

                // Paint the midnight-glass row from the two facts that govern it: does the player own
                // tube_midnight, and did they ask for it. Ownership failing to read answers "no",
                // which greys the row - the honest state for a prize we cannot prove was sold.
                // ponytail: needs ArcademyHostService.WalletOwnsSku (ConditioningControlPanel/Services/
                // Arcademy/ArcademyHostService.cs), still in the WPF head; the SKU itself is in Core.
                bool owned = false;
                ChkTubeMidnightGlass.IsEnabled = owned;
                ChkTubeMidnightGlass.IsChecked = owned && s.TubeMidnightGlass;
            }
            catch (Exception ex)
            {
                // A cosmetic row never gets to break the Workshop.
                Log.Debug(ex, "WorkshopBehaviorCell.SyncFromSettings failed");
            }
            finally { _isLoading = false; }
        }

        private void SliderIdleInterval_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var value = (int)SliderIdleIntervalCompanion.Value;
            TxtIdleIntervalCompanion.Text = $"{value}s";
            CoreSettings.Current.IdleGiggleIntervalSeconds = value;
            CoreSettings.Save();
            // ponytail: WPF also restarts the tube's idle timer (AvatarTubeWindow.RestartIdleTimer,
            // ConditioningControlPanel/Views/Windows/), a Win32 layered window not on this head.
        }

        private void SliderBubbleDuration_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var value = SliderBubbleDurationCompanion.Value;
            TxtBubbleDurationCompanion.Text = $"{(int)value}s";
            CoreSettings.Current.BubbleDurationSeconds = value;
            CoreSettings.Save();
        }

        private void ChkMuteWhispers_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            // Flips the dedicated MUTE, not SubAudioEnabled - see AppSettings.SubAudioMuted. Mute is
            // a comfort/safety reflex and stays available during a session; the whispers ENABLE is
            // part of the prescribed dose and is locked while one runs.
            CoreSettings.Current.SubAudioMuted = ChkMuteWhispersCompanion.IsChecked == true;
            CoreSettings.Save();
            // ponytail: WPF also refreshes the tube's quick menu (AvatarTubeWindow.UpdateQuickMenuState,
            // ConditioningControlPanel/Views/Windows/), a Win32 layered window not on this head.
        }

        // #846: mute only the spoken voicelines - the bubble, its text and the giggle cues stay.
        private void ChkVoiceLines_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.CompanionVoiceLinesMuted = ChkVoiceLinesCompanion.IsChecked == true;
            CoreSettings.Save();
        }

        private void ChkTubeMidnightGlass_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.TubeMidnightGlass = ChkTubeMidnightGlass.IsChecked == true;
            CoreSettings.Save();
            // ponytail: WPF repaints the glass now (AvatarTubeWindow.RefreshTubeGlass,
            // ConditioningControlPanel/Views/Windows/), a Win32 layered window not on this head.
        }
    }
}
