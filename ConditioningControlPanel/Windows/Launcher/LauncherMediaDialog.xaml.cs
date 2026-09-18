using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Launcher;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>
/// "Game media", opened by the launcher's gear. Set the pictures once and every room follows:
/// the three source chips and the niche chips edit the SAME app-wide settings the panel's
/// Assets tab edits (MainWindow.Assets.cs, the remote media picker), with the same one-time
/// consent ask, and every change also points the Back Room back at "follow the app".
///
/// <para>Applies on change; Done only closes. The settings side is
/// <see cref="LauncherMediaSettings.Apply"/>, which is where the rules live and are tested.</para>
/// </summary>
public partial class LauncherMediaDialog : Window
{
    private readonly List<ToggleButton> _sourceChips = new();
    private readonly List<ToggleButton> _nicheChips = new();
    /// <summary>Set while settings are written INTO the controls, so the change handlers can
    /// tell a click from an echo of their own refresh. Starts true: the slider's minimum
    /// coerces its value inside InitializeComponent, which would otherwise commit a 5.</summary>
    private bool _syncing = true;

    public LauncherMediaDialog()
    {
        InitializeComponent();
        BuildChips();
        Refresh();
    }

    // ------------------------------------------------------------------ build

    private void BuildChips()
    {
        var chipStyle = TryFindResource("MediaChip") as Style;

        foreach (var key in LauncherMediaSettings.Sources)
        {
            var chip = new ToggleButton
            {
                Style = chipStyle,
                Tag = key,
                Content = Loc.Get("launcher_media_src_" + key),
                ToolTip = Loc.Get("launcher_media_src_" + key + "_hint")
            };
            chip.Checked += SourceChip_Changed;
            chip.Unchecked += SourceChip_Changed;
            _sourceChips.Add(chip);
            SourceChips.Children.Add(chip);
        }

        foreach (var niche in FypOnlineCoordinator.Catalog)
        {
            var chip = new ToggleButton { Style = chipStyle, Tag = niche.Id, Content = niche.Label };
            chip.Checked += NicheChip_Changed;
            chip.Unchecked += NicheChip_Changed;
            _nicheChips.Add(chip);
            NicheChips.Children.Add(chip);
        }
    }

    /// <summary>Pushes live settings into every control.</summary>
    private void Refresh()
    {
        var s = App.Settings?.Current;
        if (s == null) return;

        _syncing = true;
        try
        {
            var source = s.MediaSource;
            foreach (var chip in _sourceChips)
                chip.IsChecked = string.Equals(chip.Tag as string, source, StringComparison.Ordinal);
            SourceHint.Text = Loc.Get("launcher_media_src_" + source + "_hint");

            RatioSlider.Value = s.RemoteMediaRatio;
            RatioLabel.Text = string.Format(Loc.Get("launcher_media_mixed_share"), s.RemoteMediaRatio);
            RatioRow.Visibility = source == LauncherMediaSettings.SourceMixed ? Visibility.Visible : Visibility.Collapsed;

            var selected = new HashSet<string>(s.FypOnlineNiches ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var chip in _nicheChips)
                chip.IsChecked = chip.Tag is string id && selected.Contains(id);
            NicheBlock.IsEnabled = source != LauncherMediaSettings.SourceLocal;
            NicheBlock.Opacity = NicheBlock.IsEnabled ? 1 : 0.45;
        }
        finally { _syncing = false; }
    }

    // ------------------------------------------------------------------ changes

    private void SourceChip_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        try
        {
            if (sender is not ToggleButton chip || chip.Tag is not string key) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            // Un-clicking the live chip would leave the app with no source at all.
            if (chip.IsChecked != true)
            {
                if (string.Equals(key, s.MediaSource, StringComparison.Ordinal)) chip.IsChecked = true;
                return;
            }
            if (string.Equals(key, s.MediaSource, StringComparison.Ordinal)) return;

            LauncherSfx.Click();

            // Leaving "local" starts fetching third-party content: asked exactly once, and never
            // of someone who already said yes to the For You feed (HasRemoteMediaConsent).
            if (key != LauncherMediaSettings.SourceLocal && !s.HasRemoteMediaConsent && !AskRemoteMediaConsent(s))
            {
                Refresh();   // puts the chips back where they were
                return;
            }

            Commit(s, key, resetChannels: true);
            Refresh();
            Log.Information("[Launcher] game media source -> {Source}", s.MediaSource);
        }
        catch (Exception ex) { Log.Warning(ex, "[Launcher] game media source change failed"); }
    }

    private void NicheChip_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        try
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            var selected = SelectedNiches();
            if (selected.Count == 0 && sender is ToggleButton last)
            {
                // The last niche stays on: an empty list would only fall back to the first
                // catalogue niche anyway, and showing that as "nothing" would be a lie.
                _syncing = true;
                try { last.IsChecked = true; } finally { _syncing = false; }
                LauncherSfx.Denied();
                return;
            }

            LauncherSfx.Click();
            Commit(s, s.MediaSource, resetChannels: true);
        }
        catch (Exception ex) { Log.Warning(ex, "[Launcher] game media niche toggle failed"); }
    }

    private void RatioSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing) return;
        try
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            // Dragging fires this per tick; no channel reset, the ratio is read at pick time.
            // Save is debounced, so a whole drag is one write.
            Commit(s, s.MediaSource, resetChannels: false);
            RatioLabel.Text = string.Format(Loc.Get("launcher_media_mixed_share"), s.RemoteMediaRatio);
        }
        catch (Exception ex) { Log.Debug("[Launcher] game media ratio change failed: {E}", ex.Message); }
    }

    private List<string> SelectedNiches()
    {
        var selected = new List<string>();
        foreach (var chip in _nicheChips)
            if (chip.IsChecked == true && chip.Tag is string id) selected.Add(id);
        return selected;
    }

    /// <summary>Writes the dialog's state and tells the media services. Mirrors the panel's
    /// RemoteSourceChip_Changed: rotation state and the asset pools were built for the old
    /// choice, so both are dropped, and the pool invalidation is what persists.</summary>
    private void Commit(AppSettings s, string source, bool resetChannels)
    {
        LauncherMediaSettings.Apply(s, source, (int)Math.Round(RatioSlider.Value), SelectedNiches());
        if (resetChannels) FypOnlineCoordinator.ResetAllChannels();

        var mw = App.MainWindowRef;
        if (mw != null) mw.InvalidateAssetPoolsAfterSelectionChange();   // also saves
        else App.Settings?.Save();
    }

    /// <summary>The same one-time ask the panel makes (MainWindow.Assets.cs
    /// AskRemoteMediaConsent), word for word, over this dialog.</summary>
    private bool AskRemoteMediaConsent(AppSettings s)
    {
        var answer = MessageBox.Show(this,
            LocOr("msg_remote_media_consent",
                "Pull media from Reddit?\n\n" +
                "The app will stream images and clips from the subreddits you pick, straight from your own machine. " +
                "Nothing is saved to your disk, nothing is uploaded, and none of it goes through our servers.\n\n" +
                "It is adult content and it is not curated by us - you choose the niches and subreddits, and only those are ever fetched.\n\n" +
                "Turn it on?"),
            LocOr("title_remote_media_consent", "Use Reddit media?"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) return false;

        s.RemoteMediaConsented = true;
        App.Settings?.Save();
        return true;
    }

    private static string LocOr(string key, string english)
    {
        try
        {
            var value = Loc.Get(key);
            return string.IsNullOrEmpty(value) || string.Equals(value, key, StringComparison.Ordinal) ? english : value;
        }
        catch { return english; }
    }

    // ------------------------------------------------------------------ chrome

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); }
        catch (InvalidOperationException) { /* DragMove after the button went up; nothing to do */ }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        LauncherSfx.Click();
        Close();
    }

    private void BtnDone_Click(object sender, RoutedEventArgs e)
    {
        LauncherSfx.Click();
        Close();
    }
}
