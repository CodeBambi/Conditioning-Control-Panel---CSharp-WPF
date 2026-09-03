using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class SubliminalFeatureControl : UserControl, ISettingsRebindable
    {
        private bool _isLoading = true;

        public SubliminalFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // Tracks WHICH AppSettings instance the hook is attached to, so a cloud restore - which
        // SWAPS the instance - can be followed instead of leaving this permanently-mounted rack
        // panel listening to, and displaying, the discarded object. See ISettingsRebindable.
        private SettingsHook? _settingsHook;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RebindToCurrentSettings();
            // The hero and side plates are mod art; the rack hosts this control permanently, so a
            // mod switch must repaint them (a popup instance never lived long enough to care).
            ApplyFeatureArt();
            if (App.Mods != null) App.Mods.ModChanged += OnModChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _settingsHook?.Unhook();
            if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
        }

        /// <inheritdoc/>
        public void RebindToCurrentSettings()
        {
            (_settingsHook ??= new SettingsHook(OnSettingsPropertyChanged)).Rebind();
            LoadFromSettings();
        }

        private void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.SubliminalEnabled;
                SliderPerMin.Value = s.SubliminalFrequency;
                TxtPerMin.Text = s.SubliminalFrequency.ToString();
                SliderFrames.Value = s.SubliminalDuration;
                TxtFrames.Text = s.SubliminalDuration.ToString();
                SliderOpacity.Value = s.SubliminalOpacity;
                TxtOpacity.Text = $"{s.SubliminalOpacity}%";
                ChkWhispers.IsChecked = s.SubAudioEnabled;
                SliderWhisperVol.Value = s.SubAudioVolume;
                TxtWhisperVol.Text = $"{s.SubAudioVolume}%";
                ChkSolidMode.IsChecked = s.SubliminalSolidMode;
                Helpers.FontPickerHelper.Populate(CmbFont, s.SubliminalFont, "Arial");
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.SubliminalEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.SubliminalFrequency) ||
                e.PropertyName == nameof(Models.AppSettings.SubliminalDuration) ||
                e.PropertyName == nameof(Models.AppSettings.SubliminalOpacity) ||
                e.PropertyName == nameof(Models.AppSettings.SubAudioEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.SubAudioVolume) ||
                e.PropertyName == nameof(Models.AppSettings.SubliminalSolidMode) ||
                e.PropertyName == nameof(Models.AppSettings.SubliminalFont))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            // Single authority: persists the flag and live-applies start/stop (idempotently).
            App.Subliminal?.SetEnabled(ChkEnable.IsChecked ?? false);
        }

        private void SliderPerMin_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtPerMin.Text = v.ToString();
            s.SubliminalFrequency = v;
            App.Settings?.Save();
        }

        private void SliderFrames_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtFrames.Text = v.ToString();
            s.SubliminalDuration = v;
            App.Settings?.Save();
        }

        private void SliderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtOpacity.Text = $"{v}%";
            s.SubliminalOpacity = v;
            App.Settings?.Save();
        }

        private void ChkWhispers_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SubAudioEnabled = ChkWhispers.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderWhisperVol_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtWhisperVol.Text = $"{v}%";
            s.SubAudioVolume = v;
            App.Settings?.Save();
        }

        private void ChkSolidMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SubliminalSolidMode = ChkSolidMode.IsChecked ?? false;
            App.Settings?.Save();
            // No service bounce needed: each show reads the setting, so the next subliminal
            // uses the new renderer. An in-flight card finishes out on whichever spawned it.
        }

        // No service bounce: the text blocks are built per flash, so the next subliminal picks
        // the new face up on its own.
        private void CmbFont_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var name = Helpers.FontPickerHelper.SelectedName(CmbFont);
            if (string.IsNullOrWhiteSpace(name) || s.SubliminalFont == name) return;
            s.SubliminalFont = name;
            App.Settings?.Save();
        }

        private void BtnManageMessages_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            var oldKeys = new HashSet<string>(s.SubliminalPool.Keys);
            // OrdinalIgnoreCase: the removed-set and ModService's top-up compare
            // case-insensitively, so detection must too or modded defaults slip through.
            var defaults = new HashSet<string>(
                (App.Mods?.GetDefaultSubliminalPool()
                 ?? Models.BuiltInMods.BambiSleep.SubliminalPool
                 ?? new Dictionary<string, bool>()).Keys,
                StringComparer.OrdinalIgnoreCase);
            var dialog = new TextEditorDialog("Subliminal Messages", s.SubliminalPool)
            {
                Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
            };
            if (dialog.ShowDialog() == true && dialog.ResultData != null)
            {
                // Remember hand-added phrases (and forget removed ones) so the cross-mod prune
                // never silently deletes a custom phrase that collides with another mod's default.
                var newKeys = new HashSet<string>(dialog.ResultData.Keys);
                foreach (var key in newKeys)
                    if (!oldKeys.Contains(key)) s.UserAddedSubliminals.Add(key);
                foreach (var key in oldKeys)
                    if (!newKeys.Contains(key)) s.UserAddedSubliminals.Remove(key);

                // Record deleted DEFAULTS, or ModService's top-up puts them straight back on the
                // next launch — the phrase the user deliberately deleted returns forever (#892).
                foreach (var key in oldKeys)
                    if (!newKeys.Contains(key) && defaults.Contains(key))
                        s.RemovedDefaultSubliminals.Add(key);
                // A default they added back is no longer "removed".
                foreach (var key in newKeys)
                    s.RemovedDefaultSubliminals.Remove(key);

                s.SubliminalPool = dialog.ResultData;
                App.Settings?.Save();
                App.Logger?.Information("Subliminal pool updated: {Count} items", dialog.ResultData.Count);
            }
        }

        /// <summary>
        /// Opens the folder a user's own whisper clips go in, creating it first: the button is
        /// worth nothing if it opens nothing, and on a fresh install nobody has put a file there
        /// yet. Points at the assets-folder copy rather than <c>Resources/sub_audio</c> beside the
        /// exe - both are searched at playback time, but only this one survives an update and only
        /// this one is writable when the app is installed under Program Files.
        /// </summary>
        private void BtnWhisperFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folder = App.UserWhisperAudioPath;
                Directory.CreateDirectory(folder);
                Helpers.ExplorerLauncher.OpenFolder(folder);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Could not open the whisper audio folder");
            }
        }

        private void BtnAdvanced_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ColorEditorDialog
            {
                Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
            };
            dialog.ShowDialog();
        }

        // =====================================================================================
        //  feature art (mod-aware)
        // =====================================================================================

        /// <summary>
        /// This page's art under <c>Resources/features/</c>. Verbatim the file the XAML already
        /// declares as its pack:// default on both plates - naming it here changes WHICH lookup
        /// runs, never WHICH file is asked for.
        /// </summary>
        private const string FeatureArtPath = "features/subliminal.png";

        /// <summary>
        /// Pushes the (possibly mod-overridden) feature art into the 72px hero plate and the tall
        /// side plate. Both plates author a pack:// default in XAML, so a null resolve here leaves
        /// the built-in art standing rather than blanking the plate - the same degrade rule
        /// <c>RemoteControlTabView.ApplyFeatureArt</c> follows.
        ///
        /// <para>Two widths, not one: the hero is 240px wide and the side plate is a full-height
        /// column, and <see cref="Services.ModResourceResolver.ResolveImageDecoded"/> keys its cache on the
        /// width, so each is decoded once for the whole session per mod.</para>
        ///
        /// <para>The brushes are mutated in place. Swapping the <c>Border.Background</c> object
        /// would work too and would throw away the XAML-declared Stretch/AlignmentX/Opacity with
        /// it; a frozen brush would silently never repaint at all, which is why they are named
        /// rather than declared inline as literals.</para>
        /// </summary>
        private void ApplyFeatureArt()
        {
            try
            {
                var hero = Services.ModResourceResolver.ResolveImageDecoded(FeatureArtPath, 480);
                if (hero != null && HeroArtBrush is { IsFrozen: false }) HeroArtBrush.ImageSource = hero;

                var side = Services.ModResourceResolver.ResolveImageDecoded(FeatureArtPath, 800);
                if (side != null && SideArtBrush is { IsFrozen: false }) SideArtBrush.ImageSource = side;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("SubliminalFeatureControl.ApplyFeatureArt: {E}", ex.Message);
            }
        }

        /// <summary>
        /// ModChanged can be raised off the UI thread, so every body it reaches is marshalled.
        /// Subscribed on Loaded and dropped on Unloaded: the rack hosts this control
        /// PERMANENTLY, so an unbalanced hook would accumulate one dead handler per re-host.
        /// </summary>
        private void OnModChanged(object? sender, Models.ModPackage mod)
        {
            Dispatcher.BeginInvoke(new Action(ApplyFeatureArt));
        }

    }
}
