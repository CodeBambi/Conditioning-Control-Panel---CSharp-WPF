// PORTED from WPF LauncherWindow.Sound.cs + LauncherWindow.xaml:452-464 (the title-bar speaker).
// It silences only the launcher's own cues (LauncherSfx reads LauncherSoundEnabled before each),
// never a session or the master volume. The unmute note is the window-wide Button.Click cue in
// LauncherWindow.Fx.cs, which runs after this handler, so muting stays quiet as in WPF.
using System;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class LauncherWindow
    {
        private void BtnSound_Click(object? sender, RoutedEventArgs e)
        {
            var settings = CoreSettings.Current;
            settings.LauncherSoundEnabled = !settings.LauncherSoundEnabled;
            try { CoreSettings.Save(); }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] saving LauncherSoundEnabled failed"); }
            PaintSoundButton();
        }

        /// <summary>WPF PaintSoundButton; the tooltip is bound so a language switch follows it.</summary>
        private void PaintSoundButton()
        {
            var on = CoreSettings.Current.LauncherSoundEnabled;
            SoundWaves.IsVisible = on;
            SoundSlash.IsVisible = !on;
            SoundCone.Opacity = on ? 1.0 : 0.45;
            BtnSound.Bind(ToolTip.TipProperty, new Binding($"[{(on ? "launcher_sound_mute" : "launcher_sound_unmute")}]")
                { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
        }
    }
}
