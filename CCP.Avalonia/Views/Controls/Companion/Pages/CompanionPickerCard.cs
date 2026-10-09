// PORTED from WPF 7.1.5 ConditioningControlPanel/Views/Controls/Companion/CompanionPickerCard.xaml(.cs):
// the ONE place that picks the companion's look and personality, side by side with a portrait, the
// personality's own sample lines and the XP perk. Code only on this head (the WPF XAML is one Border
// over a two-column Grid). Same layout numbers: portrait 120x150 r10, name 20 SemiBold, labels 11,
// combos MinHeight 30 with a 12 px gutter, sample bubble r10 padding 14,10, links 11 in #B084DC,
// perk badge 22 r11.
//
// Deviations (each a ponytail):
//   - LIVE face only. The PREVIEW face (another mod's companion, read-only) is the mod manager's;
//     it lands with that host.
//   - Look: filled from the tube's EffectiveAvatarSets (WPF PickableAvatarSets) and picked through
//     its SelectAvatarSet(int). The tube has no AvatarSetTitle on this head, so the titles are
//     worked out here by the same rule (custom label, the set's persona, the legacy title).
//   - Portrait: the glyph only. Needs AvatarTubeWindow.EmoteIdleClipUri (first frame of the idle
//     emote) or a mod-folder pose resolver on this head.
//   - "More personality options" (PersonalityStudio) and the perk picker (V2.PerkPicker) are not
//     ported, so both links stay hidden, exactly as WPF hides them with no host / v2 off.
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages
{
    /// <summary>Avatar + personality, side by side with a live preview. Owns no switching logic of
    /// its own: it reads <see cref="PersonalityService.Shared"/> and writes through the same gated
    /// path as the tube's Personality submenu (ExplicitContentGate, then SetActivePreset).</summary>
    public sealed class CompanionPickerCard : UserControl
    {
        private static readonly IBrush LinkInk = new SolidColorBrush(Color.FromRgb(0xB0, 0x84, 0xDC));
        private static readonly IBrush Salmon = new SolidColorBrush(Color.FromRgb(0xFA, 0x80, 0x72));

        private bool _syncing;

        internal TextBlock TxtLiveName { get; }
        internal ComboBox CmbAvatar { get; }
        internal ComboBox CmbPersonality { get; }
        internal TextBlock TxtAvatarHint { get; }
        internal Button BtnTurnOn { get; }
        internal Button BtnTubeFit { get; }
        internal StackPanel SamplePanel { get; } = new();
        internal TextBlock TxtPerkGlyph { get; }
        internal TextBlock TxtPerk { get; }
        internal TextBlock TxtPreviewGlyph { get; }

        /// <summary>The shell that owns the tube; null in a bare test host.</summary>
        internal Func<Windows.MainShellWindow?>? Shell { get; set; }

        public CompanionPickerCard()
        {
            TxtPreviewGlyph = new TextBlock
            {
                Text = "✧", FontSize = 38,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            Ink(TxtPreviewGlyph, "TextMutedBrush");
            var portrait = new Border
            {
                Width = 120, Height = 150, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 18, 0),
                VerticalAlignment = VerticalAlignment.Top, BorderThickness = new Thickness(1), ClipToBounds = true,
                Child = TxtPreviewGlyph,
            };
            portrait[!Border.BackgroundProperty] = portrait.GetResourceObservable("DarkerBgBrush").ToBinding();
            portrait[!Border.BorderBrushProperty] = portrait.GetResourceObservable("PanelAccentBrush").ToBinding();

            TxtLiveName = new TextBlock
            {
                FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis,
            };

            CmbAvatar = new ComboBox { MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Stretch, Name = "CmbAvatar" };
            CmbPersonality = new ComboBox { MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Stretch, Name = "CmbPersonality" };
            CmbPersonality.SelectionChanged += CmbPersonality_SelectionChanged;
            CmbAvatar.SelectionChanged += CmbAvatar_SelectionChanged;

            var lookCol = new StackPanel { Children = { Label("modmgr_label_look"), CmbAvatar } };
            var persCol = new StackPanel { Children = { Label("modmgr_label_personality"), CmbPersonality } };
            Grid.SetColumn(persCol, 2);
            var combos = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*"), Children = { lookCol, persCol } };

            TxtAvatarHint = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 4, 0, 0) };
            Ink(TxtAvatarHint, "TextMutedBrush");

            BtnTurnOn = Link("modmgr_avatar_turn_on", new Thickness(0, 3, 0, 0));
            BtnTurnOn.IsVisible = false;
            BtnTurnOn.Click += (_, _) => { Shell?.Invoke()?.SetAvatarEnabled(true); Refresh(); };

            var samples = new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10), Margin = new Thickness(0, 10, 0, 0),
                Child = SamplePanel,
            };
            samples[!Border.BackgroundProperty] = samples.GetResourceObservable("PanelAccentBrush").ToBinding();

            BtnTubeFit = Link("btn_tube_fit", new Thickness(0, 8, 0, 0));
            ToolTip.SetTip(BtnTubeFit, Loc.Get("tooltip_tube_fit"));
            BtnTubeFit.Click += BtnTubeFit_Click;

            var live = new StackPanel { Children = { TxtLiveName, combos, TxtAvatarHint, BtnTurnOn, samples, BtnTubeFit } };

            TxtPerkGlyph = new TextBlock
            {
                FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"),
            };
            var badge = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Top, Child = TxtPerkGlyph };
            badge[!Border.BackgroundProperty] = badge.GetResourceObservable("PanelAccentBrush").ToBinding();
            var perkLabel = new TextBlock
            {
                Text = Loc.Get("modmgr_perk_label"), FontSize = 11, FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0),
            };
            Ink(perkLabel, "TextMutedBrush");
            Grid.SetColumn(perkLabel, 1);
            TxtPerk = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(TxtPerk, 2);
            var perkRow = new Grid
            {
                Name = "PerkRow", Margin = new Thickness(0, 10, 0, 0),
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
                Children = { badge, perkLabel, TxtPerk },
            };
            ToolTip.SetTip(perkRow, Loc.Get("modmgr_perk_tooltip"));

            var right = new StackPanel { Children = { live, perkRow } };
            Grid.SetColumn(right, 1);
            Content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { portrait, right } };

            AttachedToVisualTree += (_, _) => { PersonalityService.Shared.PersonalityChanged += OnPersonalityChanged; Refresh(); };
            DetachedFromVisualTree += (_, _) => PersonalityService.Shared.PersonalityChanged -= OnPersonalityChanged;
        }

        /// <summary>Re-reads the live companion. Cheap; call it after a mod switch.</summary>
        public void Refresh()
        {
            _syncing = true;
            try
            {
                TxtLiveName.Text = LiveName();
                ShowPerk();
                FillAvatars();
                FillPersonalities();
            }
            catch (Exception ex) { Log.Warning(ex, "[CompanionPicker] refresh failed"); }
            finally { _syncing = false; }
        }

        /// <summary>The companion's own name (the active mod's identity), not the look's name.</summary>
        private static string LiveName()
        {
            var name = CoreMods.ActiveModPackage?.Manifest?.Identity?.CompanionName;
            if (string.IsNullOrWhiteSpace(name)) name = Loc.Get("modmgr_companion_fallback");
            return CoreMods.MakeModAware(name!);
        }

        /// <summary>WPF ShowPerk: CompanionService.ActivePerk, read straight from the settings.</summary>
        private void ShowPerk()
        {
            var s = CoreSettings.Current;
            var def = CompanionDefinition.GetById(s?.ActiveCompanionId ?? 0);
            var type = CompanionPerks.Resolve(s?.CompanionPerk, def.BonusType, CompanionExperience.IsV2Enabled);
            var perk = CompanionPerks.For(type);
            TxtPerkGlyph.Text = perk.Glyph;
            TxtPerk.Text = (CompanionExperience.IsV2Enabled ? Loc.Get(CompanionPerks.NameKey(type)) + ": " : string.Empty) + Loc.Get(perk.LocKey);
            if (perk.Negative)
            {
                TxtPerk.Foreground = Salmon;
                TxtPerkGlyph.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            }
            else
            {
                Ink(TxtPerk, "TextSecondaryBrush");
                Ink(TxtPerkGlyph, "SectionInkBrush");
            }
        }

        private void FillAvatars()
        {
            CmbAvatar.Items.Clear();
            var tube = Shell?.Invoke()?.Tube;
            BtnTurnOn.IsVisible = tube == null;
            BtnTubeFit.IsVisible = tube != null;
            CmbAvatar.IsEnabled = false;
            if (tube == null)
            {
                TxtAvatarHint.Text = Loc.Get("modmgr_avatar_off_hint");
                TxtAvatarHint.IsVisible = true;
                return;
            }
            FillLooks(tube.EffectiveAvatarSets(), tube.CurrentAvatarSet);
        }

        /// <summary>The look list: one item per pickable set, the live one selected. One look
        /// means nothing to pick, so the combo greys and the hint says why (WPF FillAvatars).</summary>
        internal void FillLooks(int[] sets, int current)
        {
            bool single = sets.Length <= 1;
            bool wasSyncing = _syncing;
            _syncing = true;   // selecting the live set below is not a pick
            try
            {
                CmbAvatar.Items.Clear();
                foreach (var set in sets)
                {
                    // A TextBlock, not a string: Avalonia reads "_" in a string as an access key.
                    var item = new ComboBoxItem { Content = new TextBlock { Text = SetTitle(set, single) }, Tag = set };
                    CmbAvatar.Items.Add(item);
                    if (set == current) CmbAvatar.SelectedItem = item;
                }
            }
            finally { _syncing = wasSyncing; }
            CmbAvatar.IsEnabled = !single;
            TxtAvatarHint.IsVisible = single;
            if (single) TxtAvatarHint.Text = Loc.Get("modmgr_avatar_single_hint");
        }

        /// <summary>Test seam: the tube's SelectAvatarSet. Null = the live tube.</summary>
        internal Func<int, bool>? SelectLook { get; set; }

        private void CmbAvatar_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_syncing || CmbAvatar.SelectedItem is not ComboBoxItem { Tag: int set }) return;
            try
            {
                // The tube's own door: the same switch its arrows take (saves the pick, switches
                // the persona behind sets 3+, reloads the art).
                bool switched = SelectLook != null ? SelectLook(set) : Shell?.Invoke()?.Tube?.SelectAvatarSet(set) == true;
                if (switched) Refresh();
            }
            catch (Exception ex) { Log.Warning(ex, "[CompanionPicker] look switch failed"); Refresh(); }
        }

        // WPF AvatarTubeWindow.AvatarSetTitle, minus the portrait-skin branch (portrait mode is not
        // on this head): a one-look mod reads as the companion, then the mod's own label, the
        // persona the set belongs to, the legacy title. Mod-aware, not upper-cased.
        private static readonly string[] SetTitleKeys =
        {
            "avatar_title_basic_bimbo", "avatar_title_dumb_airhead", "avatar_title_synthetic_blowdoll",
            "avatar_title_perfect_fuckpuppet", "avatar_title_brainwashed_slavedoll",
            "avatar_title_platinum_puppet", "avatar_title_bambi_cow",
        };

        internal static string SetTitle(int set, bool single)
        {
            if (single) return LiveName();
            string title;
            var custom = CoreMods.ActiveModPackage?.Manifest?.CustomAvatarSets?.FirstOrDefault(c => c.SetNumber == set);
            CompanionId? persona = set switch
            {
                3 => CompanionId.OGBambiSprite, 4 => CompanionId.CultBunny, 5 => CompanionId.BrainParasite,
                6 => CompanionId.BambiTrainer, 7 => CompanionId.BimboCow, _ => null,
            };
            if (custom != null && !string.IsNullOrWhiteSpace(custom.Label)) title = custom.Label;
            else if (persona.HasValue)
                title = CompanionDefinition.GetById(persona.Value).GetDisplayName(CoreSettings.Current?.SlutModeEnabled ?? false);
            else title = Loc.Get(SetTitleKeys[Math.Clamp(set - 1, 0, SetTitleKeys.Length - 1)]);
            return CoreMods.MakeModAware(title ?? string.Empty);
        }

        private void FillPersonalities()
        {
            CmbPersonality.Items.Clear();
            var svc = PersonalityService.Shared;
            var active = svc.GetActivePreset();
            foreach (var preset in svc.GetAllPresets())
            {
                // A TextBlock, not a string: Avalonia reads "_" in a string as an access key.
                var item = new ComboBoxItem { Content = new TextBlock { Text = DisplayName(preset) }, Tag = preset.Id };
                CmbPersonality.Items.Add(item);
                if (preset.Id == active?.Id) CmbPersonality.SelectedItem = item;
            }
            CmbPersonality.IsEnabled = CmbPersonality.Items.Count > 1;
            ShowSamples(active);
        }

        private static string DisplayName(PersonalityPreset p) =>
            CoreMods.Service?.GetPersonalityDisplayName(p.Name) ?? p.Name;

        private async void CmbPersonality_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_syncing || CmbPersonality.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            bool ok;
            try { ok = await ActivateAsync(id); }
            catch (Exception ex) { Log.Warning(ex, "[CompanionPicker] switch failed"); ok = false; }
            // A refusal or a failed switch puts the combo back on whatever is really active.
            if (!ok) Refresh();
            else ShowSamples(PersonalityService.Shared.GetPresetById(id));
        }

        /// <summary>WPF MainWindow.ActivatePersonalityPreset: the explicit-content acknowledgement
        /// gate, then the switch (the same path the tube's Personality submenu takes here).</summary>
        private async Task<bool> ActivateAsync(string presetId)
        {
            var preset = PersonalityService.Shared.GetPresetById(presetId);
            if (preset == null) return false;
            var settings = CoreSettings.Current;
            if (settings != null && ExplicitContentGate.RequiresAcknowledgement(preset, settings.SlutModeEnabled))
            {
                var promptSettings = settings.CompanionPrompt;
                if (!ExplicitContentGate.IsAlreadyAcknowledged(promptSettings))
                {
                    var owner = TopLevel.GetTopLevel(this) as Window;
                    if (!await new ExplicitContentAcknowledgementDialog().ShowDialogSafe<bool>(owner)) return false;
                    if (promptSettings != null)
                    {
                        ExplicitContentGate.MarkAcknowledged(promptSettings);
                        CoreSettings.Save();
                    }
                }
            }
            return PersonalityService.Shared.SetActivePreset(presetId);
        }

        private void OnPersonalityChanged(object? sender, PersonalityPreset preset) =>
            // Another door (chip row, tube menu) switched her. Follow it.
            Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Normal);

        private void ShowSamples(PersonalityPreset? preset)
        {
            SamplePanel.Children.Clear();
            if (preset == null) return;
            var lines = PersonalitySamples.For(preset);
            if (lines.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(preset.Description))
                    SamplePanel.Children.Add(Line(CoreMods.MakeModAware(preset.Description), italic: false));
                return;
            }
            foreach (var line in lines)
                SamplePanel.Children.Add(Line("“" + CoreMods.MakeModAware(line) + "”", italic: true));
        }

        // A sample line is body copy (13, TextSecondary); the quote marks and the italic say it is
        // the personality talking.
        private static TextBlock Line(string text, bool italic)
        {
            var line = new TextBlock
            {
                Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                FontStyle = italic ? FontStyle.Italic : FontStyle.Normal, Margin = new Thickness(0, 0, 0, 4),
            };
            Ink(line, "TextSecondaryBrush");
            return line;
        }

        private async void BtnTubeFit_Click(object? sender, RoutedEventArgs e)
        {
            try { await new TubeFitDialog().ShowDialogSafe<object?>(TopLevel.GetTopLevel(this) as Window); }
            catch (Exception ex) { Log.Warning(ex, "[CompanionPicker] tube fit failed to open"); }
        }

        private static TextBlock Label(string key)
        {
            var tb = new TextBlock { Text = Loc.Get(key), FontSize = 11, Margin = new Thickness(0, 0, 0, 3) };
            Ink(tb, "TextMutedBrush");
            return tb;
        }

        /// <summary>WPF PickerLinkTemplate: bare text, underlined on hover.</summary>
        private static Button Link(string key, Thickness margin)
        {
            var text = new TextBlock { Text = Loc.Get(key), FontSize = 11, Foreground = LinkInk, TextWrapping = TextWrapping.Wrap };
            var b = new Button
            {
                Content = text, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(0), Margin = margin, HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            b.PointerEntered += (_, _) => text.TextDecorations = TextDecorations.Underline;
            b.PointerExited += (_, _) => text.TextDecorations = null;
            return b;
        }

        private static void Ink(TextBlock tb, string key) =>
            tb[!TextBlock.ForegroundProperty] = tb.GetResourceObservable(key).ToBinding();
    }
}
