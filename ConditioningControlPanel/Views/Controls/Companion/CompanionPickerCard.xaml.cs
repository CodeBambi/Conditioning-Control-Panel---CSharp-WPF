using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

        public CompanionPickerCard()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (App.Personality != null) App.Personality.PersonalityChanged += OnPersonalityChanged;
                Refresh();
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
            if (App.AvatarWindow?.SelectAvatarSet(set) == true) ShowPreview(PreviewFor(set));
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
                var name = (setNumber == 1 ? "avatar_pose" : $"avatar{setNumber}_pose") + "1.png";
                return ModResourceResolver.ResolveImageDecoded(name, PreviewDecodeWidth)
                       ?? ModResourceResolver.ResolveImage(name);
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
            Dispatcher.BeginInvoke(new Action(Refresh), System.Windows.Threading.DispatcherPriority.Normal);
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

        // ------------------------------------------------------------------ fit

        private void BtnTubeFit_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TubeFitDialog { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
        }
    }
}
