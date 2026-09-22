using System;
using System.Windows;
using System.Windows.Data;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Launcher;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>
/// The speaker button in the title bar. It silences the launcher's OWN cues - the hover melody,
/// the clicks, the open and exit stings - and nothing else: a session running behind the launcher
/// keeps its voice, and so does a game. The master volume is a different control and lives behind
/// the Media button, where it has a slider and a label saying so.
///
/// <para>The state is <c>AppSettings.LauncherSoundEnabled</c>, read by
/// <see cref="LauncherSfx"/> before any cue, so muting is one flag and not six call sites.</para>
/// </summary>
public partial class LauncherWindow
{
    /// <summary>Called from the constructor: paint the button to match the stored state.</summary>
    private void SetUpSoundButton()
    {
        try { PaintSoundButton(); }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] sound button not painted"); }
    }

    private void BtnSound_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            settings.LauncherSoundEnabled = !settings.LauncherSoundEnabled;
            App.Settings?.Save();
            PaintSoundButton();

            // Unmuting answers with a note, so the button proves itself. Muting stays quiet, which
            // is the whole point of pressing it.
            if (settings.LauncherSoundEnabled) LauncherSfx.Click();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] sound not toggled"); }
    }

    private void PaintSoundButton()
    {
        var on = App.Settings?.Current?.LauncherSoundEnabled ?? true;

        SoundWaves.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        SoundSlash.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        SoundCone.Opacity = on ? 1.0 : 0.45;
        BindSoundTooltip();
    }

    /// <summary>
    /// The tooltip for the state the button is in. BOUND, not assigned: the launcher can send you
    /// to the panel where the language picker lives and take you back without being rebuilt, and
    /// LocalizationManager raises PropertyChanged("Item[]") on a switch, so an indexer binding
    /// follows the new language by itself. Assigning the string would freeze it in the old one.
    /// </summary>
    private void BindSoundTooltip()
    {
        var key = (App.Settings?.Current?.LauncherSoundEnabled ?? true)
            ? "launcher_sound_mute"
            : "launcher_sound_unmute";

        BindingOperations.SetBinding(BtnSound, FrameworkElement.ToolTipProperty,
            new Binding($"[{key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
    }
}
