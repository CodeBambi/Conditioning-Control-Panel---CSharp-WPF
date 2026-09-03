using Avalonia;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Visuals settings panel, ported from the WPF head. Same load/handler shape as the original,
    /// against <see cref="CoreSettings"/>.
    ///
    /// <para>WPF's <c>ISettingsRebindable</c> + <c>SettingsHook</c> pair is reproduced inline:
    /// a cloud restore SWAPS the AppSettings instance, so the PropertyChanged subscription is
    /// tracked per instance and re-pointed on <c>SettingsService.CurrentReplaced</c>. That
    /// interface lives in the WPF head (<c>ConditioningControlPanel/Features/ISettingsRebindable.cs</c>)
    /// and is 15 lines of bookkeeping, so it is not worth a Core file until a second head wants it.</para>
    /// </summary>
    public partial class VisualsFeatureControl : UserControl
    {
        private bool _isLoading = true;
        private AppSettings? _hooked;

        public VisualsFeatureControl()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            SliderSize.ValueChanged += (_, e) =>
            {
                if (_isLoading) return;
                var v = (int)e.NewValue;
                TxtSize.Text = $"{v}%";
                CoreSettings.Current.ImageScale = v;
                CoreSettings.Save();
            };
            SliderOpacity.ValueChanged += (_, e) =>
            {
                if (_isLoading) return;
                var v = (int)e.NewValue;
                TxtOpacity.Text = $"{v}%";
                CoreSettings.Current.FlashOpacity = v;
                CoreSettings.Save();
            };
            SliderFade.ValueChanged += (_, e) =>
            {
                if (_isLoading) return;
                var v = (int)e.NewValue;
                TxtFade.Text = $"{v}%";
                CoreSettings.Current.FadeDuration = v;
                CoreSettings.Save();
            };
            SliderDuration.ValueChanged += (_, e) =>
            {
                if (_isLoading) return;
                var v = (int)e.NewValue;
                TxtDuration.Text = $"{v}s";
                CoreSettings.Current.FlashDuration = v;
                CoreSettings.Save();
            };
            ChkAudio.IsCheckedChanged += (_, _) =>
            {
                if (_isLoading) return;
                var s = CoreSettings.Current;
                var want = ChkAudio.IsChecked ?? false;
                if (s.FlashAudioEnabled == want) return;   // an echo of the seed must not save
                s.FlashAudioEnabled = want;
                CoreSettings.Save();
            };

            LoadFromSettings();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += RebindToCurrentSettings;
            RebindToCurrentSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= RebindToCurrentSettings;
            Unhook();
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>WPF's <c>ISettingsRebindable.RebindToCurrentSettings</c>: detach from whichever
        /// instance we were on, attach to the live one, repaint from it.</summary>
        private void RebindToCurrentSettings()
        {
            Unhook();
            _hooked = CoreSettings.Current;
            _hooked.PropertyChanged += OnSettingsPropertyChanged;
            LoadFromSettings();
        }

        private void Unhook()
        {
            if (_hooked != null) _hooked.PropertyChanged -= OnSettingsPropertyChanged;
            _hooked = null;
        }

        private void LoadFromSettings()
        {
            var s = CoreSettings.Current;
            _isLoading = true;
            try
            {
                SliderSize.Value = s.ImageScale;
                TxtSize.Text = $"{s.ImageScale}%";
                SliderOpacity.Value = s.FlashOpacity;
                TxtOpacity.Text = $"{s.FlashOpacity}%";
                SliderFade.Value = s.FadeDuration;
                TxtFade.Text = $"{s.FadeDuration}%";
                SliderDuration.Value = s.FlashDuration;
                TxtDuration.Text = $"{s.FlashDuration}s";
                ChkAudio.IsChecked = s.FlashAudioEnabled;
            }
            finally { _isLoading = false; }
        }

        /// <summary>Reflects external writes (Intensity Ramp, presets, the session engine) back
        /// into the panel. Marshalled: those writers are not all on the UI thread.</summary>
        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.ImageScale) ||
                e.PropertyName == nameof(AppSettings.FlashOpacity) ||
                e.PropertyName == nameof(AppSettings.FadeDuration) ||
                e.PropertyName == nameof(AppSettings.FlashDuration) ||
                e.PropertyName == nameof(AppSettings.FlashAudioEnabled))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
        }
    }
}
