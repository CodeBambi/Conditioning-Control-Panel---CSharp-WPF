using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Avatar + personality, side by side with a live preview. See the XAML header: this is the
    /// single picker for both, and it owns no switching logic of its own.
    ///
    /// <para>Existing paths it calls:
    /// <list type="bullet">
    /// <item>look: <see cref="AvatarTubeWindow.PickableAvatarSets"/>, <see cref="AvatarTubeWindow.CurrentAvatarSet"/>,
    /// <see cref="AvatarTubeWindow.AvatarSetTitle"/> and <see cref="AvatarTubeWindow.SelectAvatarSet"/>, which the tube's own
    /// arrows now call too;</item>
    /// <item>personality: <see cref="PersonalityService.GetAllPresets"/> / <see cref="PersonalityService.GetActivePreset"/>
    /// to read, <c>MainWindow.ActivatePersonalityPreset</c> to write (the explicit-content gate lives there);</item>
    /// <item>fit: <see cref="TubeFitDialog"/>, as the old Tube Fit button did.</item>
    /// </list></para>
    /// </summary>
    public partial class CompanionPickerCard : UserControl
    {
        private const int PreviewDecodeWidth = 240;
        private bool _syncing;
        private bool _previewing;

        public CompanionPickerCard()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (App.Personality != null) App.Personality.PersonalityChanged += OnPersonalityChanged;
                if (!_previewing) Refresh();
            };
            Unloaded += (_, _) =>
            {
                if (App.Personality != null) App.Personality.PersonalityChanged -= OnPersonalityChanged;
            };
        }

        /// <summary>Re-reads the live companion. Cheap; call it after a mod switch.</summary>
        public void Refresh()
        {
            _syncing = true;
            try
            {
                _previewing = false;
                LivePanel.Visibility = Visibility.Visible;
                PreviewPanel.Visibility = Visibility.Collapsed;
                TxtPreviewGlyph.ClearValue(TextBlock.ForegroundProperty);
                TxtLiveName.Text = LiveName();
                ShowPerk();
                FillAvatars();
                FillPersonalities();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[CompanionPicker] refresh failed");
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>The companion's own name (the active mod's identity), not the look's name.</summary>
        private static string LiveName()
        {
            var name = App.Mods?.ActiveMod?.Manifest?.Identity?.CompanionName;
            if (string.IsNullOrWhiteSpace(name)) name = Loc.Get("modmgr_companion_fallback");
            return App.Mods?.MakeModAware(name!) ?? name!;
        }

        /// <summary>
        /// The companion's XP perk, read from the current companion definition. The companion
        /// travels with the user across mods, so a preview shows the perk they would keep.
        /// </summary>
        private void ShowPerk()
        {
            var def = CompanionDefinition.GetById(App.Companion?.ActiveCompanion ?? CompanionId.OGBambiSprite);
            var type = App.Companion?.ActivePerk ?? def.BonusType;
            var perk = CompanionPerks.For(type);
            BtnPerkChange.Visibility = CompanionExperience.IsV2Enabled && !_previewing ? Visibility.Visible : Visibility.Collapsed;
            TxtPerkGlyph.Text = perk.Glyph;
            TxtPerk.Text = (CompanionExperience.IsV2Enabled ? Loc.Get(CompanionPerks.NameKey(type)) + ": " : string.Empty) + Loc.Get(perk.LocKey);
            TxtPerk.Foreground = perk.Negative ? Brushes.Salmon : Brushes.White;
            var tone = perk.Negative ? Color.FromRgb(0xFF, 0x6B, 0x6B) : (Color?)null;
            if (tone is { } c) TxtPerkGlyph.Foreground = new SolidColorBrush(c);
            else TxtPerkGlyph.SetResourceReference(TextBlock.ForegroundProperty, "PinkBrush");
        }

        private void Perk_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is not Window owner) return;
            V2.PerkPicker.Show(owner);
            Refresh();
        }

        // ------------------------------------------------------------------ preview

        /// <summary>
        /// Read-only face for a mod the user is looking at but not using: the companion's name,
        /// how many looks and personalities it brings and one line in its voice. Reads the
        /// manifest and the mod's personality list off disk; activates nothing.
        /// </summary>
        public void ShowPreview(ModPackage mod, Color accent)
        {
            LivePanel.Visibility = Visibility.Collapsed;
            PreviewPanel.Visibility = Visibility.Visible;
            _previewing = true;
            ShowPerk();
            try
            {
                var personalities = ModCompanionContent.GetPersonalities(
                    mod.Id, mod.InstalledPath, mod.Manifest.Personalities, out _);
                var singleEmote = (mod.Id == BuiltInMods.BambiSleepId || mod.Id == BuiltInMods.SissyHypnoId)
                                  && (mod.Manifest.SupportedAvatarSets?.Count ?? 0) == 0
                                  && (mod.Manifest.CustomAvatarSets?.Count ?? 0) == 0;
                var info = CompanionPreview.Build(mod.Manifest, personalities, singleEmote,
                    neutral: mod.Id == BuiltInMods.CCPDefaultId);

                TxtPreviewName.Text = info.Name;
                TxtPreviewCounts.Text = Loc.GetF("modmgr_preview_counts",
                    Loc.GetF(info.Looks == 1 ? "modmgr_looks_one" : "modmgr_looks_n", info.Looks),
                    Loc.GetF(info.Personalities == 1 ? "modmgr_personalities_one" : "modmgr_personalities_n", info.Personalities));
                PreviewSampleBorder.Visibility = string.IsNullOrWhiteSpace(info.SampleLine) ? Visibility.Collapsed : Visibility.Visible;
                TxtPreviewSample.Text = "\u201C" + info.SampleLine + "\u201D";
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[CompanionPicker] preview failed for {Mod}", mod.Id);
            }

            TxtPreviewGlyph.Foreground = new SolidColorBrush(accent);
            // An animated companion (the shared avatar0 emote set of CCP Default, Bambi Sleep and
            // Sissy) previews its own idle frame; pose files on disk are the fallback.
            ShowPreview(EmoteStill(AvatarTubeWindow.EmoteIdleClipUri(mod.Id, 1, mod.InstalledPath))
                        ?? PortraitInFolder(mod.InstalledPath));
        }

        /// <summary>Pose 1 from a mod's own folder, never through the resolver (which answers
        /// for the ACTIVE mod). Null when the mod ships no pose art on disk.</summary>
        private static ImageSource? PortraitInFolder(string? installedPath)
        {
            if (string.IsNullOrEmpty(installedPath)) return null;
            foreach (var name in new[] { "avatar3_pose1.png", "avatar_pose1.png" })
            {
                try
                {
                    var full = System.IO.Path.Combine(installedPath, "resources", name);
                    if (!System.IO.File.Exists(full)) continue;
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(full, UriKind.Absolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = PreviewDecodeWidth;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                catch { }
            }
            return null;
        }

        // ------------------------------------------------------------------ look

        private void FillAvatars()
        {
            CmbAvatar.Items.Clear();
            var tube = App.AvatarWindow;
            if (tube == null)
            {
                CmbAvatar.IsEnabled = false;
                BtnTubeFit.Visibility = Visibility.Collapsed;
                ShowHint(Loc.Get("modmgr_avatar_off_hint"));
                ShowPreview(PreviewFor(App.Settings?.Current?.SelectedAvatarSet ?? 1));
                return;
            }

            BtnTubeFit.Visibility = Visibility.Visible;
            var sets = tube.PickableAvatarSets();
            foreach (var set in sets)
            {
                var item = new ComboBoxItem { Content = tube.AvatarSetTitle(set), Tag = set };
                CmbAvatar.Items.Add(item);
                if (set == tube.CurrentAvatarSet) CmbAvatar.SelectedItem = item;
            }

            CmbAvatar.IsEnabled = sets.Length > 1;
            if (sets.Length <= 1) ShowHint(Loc.Get("modmgr_avatar_single_hint"));
            else TxtAvatarHint.Visibility = Visibility.Collapsed;

            ShowPreview(PreviewFor(tube.CurrentAvatarSet));
        }

        private void ShowHint(string text)
        {
            TxtAvatarHint.Text = text;
            TxtAvatarHint.Visibility = Visibility.Visible;
        }

        private void CmbAvatar_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || CmbAvatar.SelectedItem is not ComboBoxItem { Tag: int set }) return;
            if (App.AvatarWindow?.SelectAvatarSet(set) == true) Refresh();
        }

        /// <summary>
        /// Pose 1 of the set, resolved through the mod resolver first (so a themed mod's art wins),
        /// the same way the Companion tab's hero ring finds its portrait. Null for a portrait-skin
        /// mod whose art is not in pose files; the card then shows its glyph.
        /// </summary>
        internal static ImageSource? PreviewFor(int setNumber)
        {
            try
            {
                if (setNumber < 1) setNumber = 1;

                // An animated set shows what the tube really plays. Without this the single-look
                // mods (CCP Default, Bambi Sleep, Sissy all reuse avatar0_emotes on set 1) fell
                // through to avatar_pose1.png, the retired neon-girl art.
                var mod = App.Mods?.ActiveMod;
                var emote = EmoteStill(AvatarTubeWindow.EmoteIdleClipUri(
                    App.Mods?.ActiveModId ?? mod?.Id, setNumber, mod?.InstalledPath));
                if (emote != null) return emote;

                var name = (setNumber == 1 ? "avatar_pose" : $"avatar{setNumber}_pose") + "1.png";
                return ModResourceResolver.ResolveImageDecoded(name, PreviewDecodeWidth)
                       ?? ModResourceResolver.ResolveImage(name);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>First frame of an emote clip, frozen, or null when it cannot be read.</summary>
        internal static ImageSource? EmoteStill(Uri? clip)
        {
            if (clip == null) return null;
            try
            {
                System.IO.Stream? stream = clip.IsFile
                    ? System.IO.File.OpenRead(clip.LocalPath)
                    : Application.GetResourceStream(clip)?.Stream;
                if (stream == null) return null;
                using (stream)
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0) return null;
                    var frame = decoder.Frames[0];
                    frame.Freeze();
                    return frame;
                }
            }
            catch
            {
                return null;
            }
        }

        private void ShowPreview(ImageSource? source)
        {
            ImgPreview.Source = source;
            TxtPreviewGlyph.Visibility = source == null ? Visibility.Visible : Visibility.Collapsed;
            if (source == null || !MotionFx.AllowTransitions) { ImgPreview.Opacity = 1; return; }
            ImgPreview.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(160)));
        }

        // ------------------------------------------------------------------ personality

        private void FillPersonalities()
        {
            CmbPersonality.Items.Clear();
            var svc = App.Personality;
            if (svc == null) { CmbPersonality.IsEnabled = false; ShowSamples(null); return; }

            var active = svc.GetActivePreset();
            foreach (var preset in svc.GetAllPresets())
            {
                var item = new ComboBoxItem { Content = DisplayName(preset), Tag = preset.Id };
                CmbPersonality.Items.Add(item);
                if (preset.Id == active?.Id) CmbPersonality.SelectedItem = item;
            }
            CmbPersonality.IsEnabled = CmbPersonality.Items.Count > 1;
            ShowSamples(active);
        }

        private static string DisplayName(PersonalityPreset p) =>
            App.Mods?.GetPersonalityDisplayName(p.Name) ?? p.Name;

        private void CmbPersonality_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || CmbPersonality.SelectedItem is not ComboBoxItem { Tag: string id }) return;

            // The gate (explicit-content acknowledgement) lives on MainWindow; a refusal or a
            // failed switch puts the combo back on whatever is really active.
            var ok = App.MainWindowRef?.ActivatePersonalityPreset(id) == true;
            if (!ok) Refresh();
            else ShowSamples(App.Personality?.GetPresetById(id));
        }

        private void OnPersonalityChanged(object? sender, PersonalityPreset preset)
        {
            // Another door (chip row, tube menu) switched her. Follow it.
            Dispatcher.BeginInvoke(new Action(() => { if (!_previewing) Refresh(); }), System.Windows.Threading.DispatcherPriority.Normal);
        }

        private void ShowSamples(PersonalityPreset? preset)
        {
            SamplePanel.Children.Clear();
            if (preset == null) return;

            var lines = PersonalitySamples.For(preset);
            if (lines.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(preset.Description))
                    SamplePanel.Children.Add(Line(App.Mods?.MakeModAware(preset.Description) ?? preset.Description, italic: false));
                return;
            }

            foreach (var line in lines)
                SamplePanel.Children.Add(Line("“" + (App.Mods?.MakeModAware(line) ?? line) + "”", italic: true));
        }

        private TextBlock Line(string text, bool italic) => new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
            Foreground = (Brush)FindResource("TextLightBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        };

        // ------------------------------------------------------------------ more options

        private Action<Window>? _morePersonalityOptions;

        /// <summary>
        /// Opens the deeper personality editor. When a host sets it, the card shows a link right
        /// under the personality choice, so it is in view without scrolling; null hides it.
        /// </summary>
        public Action<Window>? MorePersonalityOptions
        {
            get => _morePersonalityOptions;
            set
            {
                _morePersonalityOptions = value;
                BtnMorePersonality.Visibility = value != null ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void BtnMorePersonality_Click(object sender, RoutedEventArgs e)
        {
            if (_morePersonalityOptions == null || Window.GetWindow(this) is not Window owner) return;
            _morePersonalityOptions(owner);
            if (!_previewing) Refresh();
        }

        // ------------------------------------------------------------------ fit

        private void BtnTubeFit_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TubeFitDialog { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
        }
    }
}
