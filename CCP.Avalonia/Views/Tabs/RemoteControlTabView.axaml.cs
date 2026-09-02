using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class RemoteControlTabView : UserControl
    {
        public RemoteControlTabView()
        {
            AvaloniaXamlLoader.Load(this);

            // Placeholder data. On WPF these come from App.Settings.Current.RemoteEmotePresets and
            // from the live command feed; both live behind services that are still in the WPF head.
            // They are seeded here so --render-all can prove the emote ControlThemes actually draw -
            // an empty ItemsControl would hide a template-less button (CLAUDE.md trap 4).
            this.FindControl<ItemsControl>("LstEmotePresets")!.ItemsSource = new List<EmotePresetSample>
            {
                new("👋", "hi bambi"),
                new("💗", "good girl"),
                new("😈", "deeper"),
                new("🌀", "drop"),
                new("🔔", "focus"),
            };
            this.FindControl<ListBox>("LstRemoteCommandLog")!.ItemsSource = new List<string>
            {
                "20:14  controller connected",
                "20:14  tier set to Light",
            };
        }

        // ponytail: every handler below routes to MainWindow on WPF (Window.GetWindow(this) is
        // MainWindow mw -> mw.<same name>). Needs the MainWindow.RemoteControl partial, wired when
        // RemoteControlService moves to Core. Names kept identical so that wiring diffs cleanly.
        private void BtnCopyRemoteCode_Click(object? sender, RoutedEventArgs e) { }
        private void BtnCopyRemoteLink_Click(object? sender, RoutedEventArgs e) { }
        private void BtnEditEmoteCancel_Click(object? sender, RoutedEventArgs e) { }
        private void BtnEditEmoteSave_Click(object? sender, RoutedEventArgs e) { }
        private void BtnEmoteCustomSend_Click(object? sender, RoutedEventArgs e) { }
        private void BtnEmoteEdit_Click(object? sender, RoutedEventArgs e) { }
        private void BtnEmotePreset_Click(object? sender, RoutedEventArgs e) { }
        private void BtnGateUnlock_Click(object? sender, RoutedEventArgs e) { }
        private void BtnStopRemote_Click(object? sender, RoutedEventArgs e) { }
        private void ChkOptInTag_Click(object? sender, RoutedEventArgs e) { }
        private void ChkOptIntoDirectory_Changed(object? sender, RoutedEventArgs e) { }
        private void ChkRemoteControlEnabled_Changed(object? sender, RoutedEventArgs e) { }
        private void ChkRemoteShareAvatar_Changed(object? sender, RoutedEventArgs e) { }
        private void ChkStopEffectsOnRemoteDisconnect_Changed(object? sender, RoutedEventArgs e) { }
        private void CmbRemoteTier_SelectionChanged(object? sender, SelectionChangedEventArgs e) { }
        private void TierCard_Click(object? sender, PointerReleasedEventArgs e) { }
        private void TxtEditEmoteText_TextChanged(object? sender, TextChangedEventArgs e) { }
        private void TxtEmoteCustom_KeyDown(object? sender, KeyEventArgs e) { }
        private void TxtOptInStatus_TextChanged(object? sender, TextChangedEventArgs e) { }
    }

    /// <summary>
    /// Stand-in for the WPF head's <c>Models/AppSettings.cs:EmotePreset</c>, which has not moved to
    /// Core yet. Only the two members the preset DataTemplate binds; swap the x:DataType for the
    /// real type when the model lands.
    /// </summary>
    public sealed record EmotePresetSample(string Icon, string Text);
}
