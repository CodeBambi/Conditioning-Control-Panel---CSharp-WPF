using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel
{
    /// <summary>
    /// "Advanced" panel of the mod creator: avatar tube layout offsets and
    /// enhancement tree label/tooltip overrides. Partial-class side file of
    /// <see cref="ModCreatorWindow"/> (see ModCreatorWindow.xaml.cs).
    /// </summary>
    public partial class ModCreatorWindow
    {
        // ─── Advanced Section State ──────────────────────────────
        // Avatar tube layout (blank = app default; ranges mirror ModService.SanitizeManifest)
        private TextBox? _advAvatarOffsetX, _advAvatarDetachedOffsetX, _advAvatarScale;
        private TextBox? _advAvatarOffsetY, _advAvatarDetachedOffsetY;

        // Bubble Pop sprite scale (blank = the app's own size band; range mirrors
        // BubbleSizing.ModScaleMin/Max, which is what actually honours the number).
        private TextBox? _advBubbleScale;

        // Enhancement tree label overrides
        private TextBox? _advTreeTitle, _advTreeSubtitle, _advTreeWarning;
        private TextBox? _advPointsLabel, _advStatsTitle, _advTabTooltip;
        private TextBox? _advPinkRushName, _advPinkRushDescription;
        private TextBox? _advLuckyFlashLabel, _advLuckyBubbleLabel;

        // Tooltip override rows (skill id → tooltip text)
        private readonly List<(TextBox Key, TextBox Value)> _advBoostTooltipRows = new();
        private readonly List<(TextBox Key, TextBox Value)> _advStatPillTooltipRows = new();
        private StackPanel? _advBoostTooltipsPanel, _advStatPillTooltipsPanel;

        // ─── Advanced Section ────────────────────────────────────
        private void BuildAdvancedSection()
        {
            var panel = CreateSectionPanel("advanced");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Advanced"));
            stack.Children.Add(CreateSectionDescription("Fine-tuning for power users. Adjust where the avatar sits inside a custom tube image, and rename the enhancement (skill) tree's labels and tooltips. Everything here is optional -- leave fields blank to keep the app defaults."));

            // ── Sub-group 1: Avatar Tube Layout ──
            stack.Children.Add(CreateSubHeader("Avatar Tube Layout"));
            stack.Children.Add(CreateSectionDescription("Pixel offsets for the avatar inside the tube window, for tube images whose glass area is not in the default position. Positive X shifts right, positive Y shifts down. Blank = default (0)."));

            stack.Children.Add(CreateFieldLabel("Avatar Offset X (-1000 to 1000)"));
            _advAvatarOffsetX = CreateAdvNumberBox();
            stack.Children.Add(_advAvatarOffsetX);

            stack.Children.Add(CreateFieldLabel("Avatar Detached Offset X (-1000 to 1000)"));
            _advAvatarDetachedOffsetX = CreateAdvNumberBox();
            stack.Children.Add(_advAvatarDetachedOffsetX);

            stack.Children.Add(CreateFieldLabel("Avatar Scale (0.1 to 3.0, e.g. 1.25)"));
            _advAvatarScale = CreateAdvNumberBox();
            stack.Children.Add(_advAvatarScale);

            stack.Children.Add(CreateFieldLabel("Avatar Offset Y (-500 to 500)"));
            _advAvatarOffsetY = CreateAdvNumberBox();
            stack.Children.Add(_advAvatarOffsetY);

            stack.Children.Add(CreateFieldLabel("Avatar Detached Offset Y (-500 to 500)"));
            _advAvatarDetachedOffsetY = CreateAdvNumberBox();
            stack.Children.Add(_advAvatarDetachedOffsetY);

            // ── Sub-group 2: Bubble Size ──
            stack.Children.Add(CreateSubHeader("Bubble Size"));
            stack.Children.Add(CreateSectionDescription("Multiplies the on-screen size of the bubble.png you supplied under UI Assets. Worth setting because perceived size comes from how much transparent margin the sprite has, not from the box it is drawn in: the app's own bubble has a soft padded rim, so a full-bleed replacement reads dramatically bigger at the same box size. Correct it once here instead of leaving every user of your mod to find the bubble size slider and compensate by hand. Blank = no change. This multiplies with the user's own setting, and they keep the final say."));

            stack.Children.Add(CreateFieldLabel("Bubble Scale (0.5 to 1.5, e.g. 0.85)"));
            _advBubbleScale = CreateAdvNumberBox();
            stack.Children.Add(_advBubbleScale);

            // ── Sub-group 3: Skill Tree Overrides ──
            // (manifest field names stay `treeTitle`/`tabTooltip` etc. — only the wording changed)
            stack.Children.Add(CreateSubHeader("Skill Tree Overrides"));
            stack.Children.Add(CreateSectionDescription("Rename the labels of the skill tree tab. The grey hint text shows the default wording each field replaces. Max 200 characters per label."));

            stack.Children.Add(CreateFieldLabel("Tree Title"));
            _advTreeTitle = CreateDarkTextBox("Bimbo Skill Tree");
            stack.Children.Add(_advTreeTitle);

            stack.Children.Add(CreateFieldLabel("Tree Subtitle"));
            _advTreeSubtitle = CreateDarkTextBox("you earn sparkle points from leveling up + every 100 bubbles popped~");
            stack.Children.Add(_advTreeSubtitle);

            stack.Children.Add(CreateFieldLabel("Tree Warning"));
            _advTreeWarning = CreateDarkTextBox("once you pick a path, there's no going back~");
            stack.Children.Add(_advTreeWarning);

            stack.Children.Add(CreateFieldLabel("Points Label"));
            _advPointsLabel = CreateDarkTextBox("Sparkle Points");
            stack.Children.Add(_advPointsLabel);

            stack.Children.Add(CreateFieldLabel("Stats Title"));
            _advStatsTitle = CreateDarkTextBox("Ditzy Data Stats");
            stack.Children.Add(_advStatsTitle);

            stack.Children.Add(CreateFieldLabel("Tab Tooltip"));
            _advTabTooltip = CreateDarkTextBox("Bimbo Skill Tree");
            stack.Children.Add(_advTabTooltip);

            stack.Children.Add(CreateFieldLabel("Pink Rush Name"));
            _advPinkRushName = CreateDarkTextBox("PINK RUSH!");
            stack.Children.Add(_advPinkRushName);

            stack.Children.Add(CreateFieldLabel("Pink Rush Description"));
            _advPinkRushDescription = CreateDarkTextBox("3x XP for 60 seconds!");
            stack.Children.Add(_advPinkRushDescription);

            stack.Children.Add(CreateFieldLabel("Lucky Flash Label"));
            _advLuckyFlashLabel = CreateDarkTextBox("Lucky Flash");
            stack.Children.Add(_advLuckyFlashLabel);

            stack.Children.Add(CreateFieldLabel("Lucky Bubble Label"));
            _advLuckyBubbleLabel = CreateDarkTextBox("Lucky Bubble");
            stack.Children.Add(_advLuckyBubbleLabel);

            // ── Boost tooltips (skill id → tooltip) ──
            stack.Children.Add(CreateSubHeader("Boost Tooltips"));
            stack.Children.Add(CreateSectionDescription("Custom hover tooltips for skill tree boost nodes, keyed by skill id (e.g. pink_rush, lucky_bimbo). Max 30 entries, 500 characters per tooltip."));

            _advBoostTooltipsPanel = new StackPanel();
            stack.Children.Add(_advBoostTooltipsPanel);
            stack.Children.Add(CreateAdvTooltipAddButton("+ Add boost tooltip",
                () => AddAdvTooltipRow(_advBoostTooltipsPanel, _advBoostTooltipRows, "", "")));

            // ── Stat pill tooltips (skill id → tooltip) ──
            stack.Children.Add(CreateSubHeader("Stat Pill Tooltips"));
            stack.Children.Add(CreateSectionDescription("Custom hover tooltips for the stat pills in the stats panel, keyed by skill id. Max 30 entries, 500 characters per tooltip."));

            _advStatPillTooltipsPanel = new StackPanel();
            stack.Children.Add(_advStatPillTooltipsPanel);
            stack.Children.Add(CreateAdvTooltipAddButton("+ Add stat pill tooltip",
                () => AddAdvTooltipRow(_advStatPillTooltipsPanel, _advStatPillTooltipRows, "", "")));
        }

        // ─── UI Helpers ──────────────────────────────────────────
        private TextBox CreateAdvNumberBox()
        {
            var tb = CreateDarkTextBox();
            tb.Width = 100;
            return tb;
        }

        private Button CreateAdvTooltipAddButton(string label, Action onClick)
        {
            var btn = new Button
            {
                Content = label,
                Style = (Style)FindResource("SecondaryButton"),
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
            };
            btn.Click += (_, _) => onClick();
            return btn;
        }

        private void AddAdvTooltipRow(StackPanel? hostPanel, List<(TextBox Key, TextBox Value)> rows, string keyText, string valueText)
        {
            if (hostPanel == null) return;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var keyBox = new TextBox
            {
                Style = (Style)FindResource("DarkTextBox"),
                Text = keyText,
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                ToolTip = "Skill id",
            };
            Grid.SetColumn(keyBox, 0);
            row.Children.Add(keyBox);

            var arrow = new TextBlock
            {
                Text = "→",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080")),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            };
            Grid.SetColumn(arrow, 1);
            row.Children.Add(arrow);

            var valueBox = new TextBox
            {
                Style = (Style)FindResource("DarkTextBox"),
                Text = valueText,
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                MaxWidth = 500,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ToolTip = "Tooltip text (max 500 characters)",
            };
            Grid.SetColumn(valueBox, 2);
            row.Children.Add(valueBox);

            var delBtn = new Button
            {
                Content = "×",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 16,
                Cursor = Cursors.Hand,
                Padding = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            delBtn.Click += (_, _) =>
            {
                var idx = hostPanel.Children.IndexOf(row);
                if (idx >= 0)
                {
                    hostPanel.Children.Remove(row);
                    if (idx < rows.Count)
                        rows.RemoveAt(idx);
                }
            };
            Grid.SetColumn(delBtn, 3);
            row.Children.Add(delBtn);

            hostPanel.Children.Add(row);
            rows.Add((keyBox, valueBox));
        }

        // ─── Apply to Manifest ───────────────────────────────────
        private void ApplyAdvancedToManifest(Models.ModManifest manifest)
        {
            // ── Tube layout: only emit if at least one field was filled in ──
            var layout = new Models.ModTubeLayout();
            bool anyLayout = false;

            if (TryGetAdvInt(_advAvatarOffsetX, out var offX))
            {
                layout.AvatarOffsetX = Math.Clamp(offX, -1000, 1000);
                anyLayout = true;
            }
            if (TryGetAdvInt(_advAvatarDetachedOffsetX, out var detX))
            {
                layout.AvatarDetachedOffsetX = Math.Clamp(detX, -1000, 1000);
                anyLayout = true;
            }
            if (TryGetAdvDouble(_advAvatarScale, out var scale))
            {
                layout.AvatarScale = Math.Clamp(scale, 0.1, 3.0);
                anyLayout = true;
            }
            if (TryGetAdvInt(_advAvatarOffsetY, out var offY))
            {
                layout.AvatarOffsetY = Math.Clamp(offY, -500, 500);
                anyLayout = true;
            }
            if (TryGetAdvInt(_advAvatarDetachedOffsetY, out var detY))
            {
                layout.AvatarDetachedOffsetY = Math.Clamp(detY, -500, 500);
                anyLayout = true;
            }

            manifest.TubeLayout = anyLayout ? layout : null;

            // ── Bubble scale ──
            // Parsed through ModBubbleScaleRules rather than read raw so a hand-written value is
            // corrected on the way out as well as on the way in. Until this line existed the field
            // was write-only from the editor's point of view: BuildManifestFromForm builds a FRESH
            // ModManifest, so an author who typed bubbleScale into mod.json by hand lost it silently
            // the next time they opened their mod here and exported.
            manifest.BubbleScale = ModBubbleScaleRules.Parse(GetTextBoxValue(_advBubbleScale));

            // ── Enhancement overrides: only emit if anything is non-empty ──
            var eo = new Models.ModEnhancementOverrides
            {
                TreeTitle = GetAdvString(_advTreeTitle, 200),
                TreeSubtitle = GetAdvString(_advTreeSubtitle, 200),
                TreeWarning = GetAdvString(_advTreeWarning, 200),
                PointsLabel = GetAdvString(_advPointsLabel, 200),
                StatsTitle = GetAdvString(_advStatsTitle, 200),
                TabTooltip = GetAdvString(_advTabTooltip, 200),
                PinkRushName = GetAdvString(_advPinkRushName, 200),
                PinkRushDescription = GetAdvString(_advPinkRushDescription, 200),
                LuckyFlashLabel = GetAdvString(_advLuckyFlashLabel, 200),
                LuckyBubbleLabel = GetAdvString(_advLuckyBubbleLabel, 200),
                BoostTooltips = CollectAdvTooltips(_advBoostTooltipRows),
                StatPillTooltips = CollectAdvTooltips(_advStatPillTooltipRows),
            };

            bool anyOverride =
                eo.TreeTitle != null || eo.TreeSubtitle != null || eo.TreeWarning != null ||
                eo.PointsLabel != null || eo.StatsTitle != null || eo.TabTooltip != null ||
                eo.PinkRushName != null || eo.PinkRushDescription != null ||
                eo.LuckyFlashLabel != null || eo.LuckyBubbleLabel != null ||
                eo.BoostTooltips != null || eo.StatPillTooltips != null;

            manifest.EnhancementOverrides = anyOverride ? eo : null;
        }

        // ─── Populate from Manifest ──────────────────────────────
        private void PopulateAdvancedFromManifest(Models.ModManifest manifest)
        {
            ClearAdvancedSection();

            if (manifest.TubeLayout is { } tl)
            {
                // 0 is the app default for offsets -- leave the box blank so
                // re-export doesn't pin values the author never touched.
                if (tl.AvatarOffsetX != 0)
                    SetTextBoxValue(_advAvatarOffsetX, tl.AvatarOffsetX.ToString(CultureInfo.InvariantCulture));
                if (tl.AvatarDetachedOffsetX != 0)
                    SetTextBoxValue(_advAvatarDetachedOffsetX, tl.AvatarDetachedOffsetX.ToString(CultureInfo.InvariantCulture));
                if (tl.AvatarScale.HasValue)
                    SetTextBoxValue(_advAvatarScale, tl.AvatarScale.Value.ToString(CultureInfo.InvariantCulture));
                if (tl.AvatarOffsetY != 0)
                    SetTextBoxValue(_advAvatarOffsetY, tl.AvatarOffsetY.ToString(CultureInfo.InvariantCulture));
                if (tl.AvatarDetachedOffsetY != 0)
                    SetTextBoxValue(_advAvatarDetachedOffsetY, tl.AvatarDetachedOffsetY.ToString(CultureInfo.InvariantCulture));
            }

            // Blank when the manifest said nothing usable, so a mod that never declared a bubble
            // scale does not gain one on re-export -- and a garbage value is scrubbed rather than
            // shown, because the box is what the author will trust.
            SetTextBoxValue(_advBubbleScale, ModBubbleScaleRules.Format(manifest.BubbleScale));

            if (manifest.EnhancementOverrides is { } eo)
            {
                SetTextBoxValue(_advTreeTitle, eo.TreeTitle);
                SetTextBoxValue(_advTreeSubtitle, eo.TreeSubtitle);
                SetTextBoxValue(_advTreeWarning, eo.TreeWarning);
                SetTextBoxValue(_advPointsLabel, eo.PointsLabel);
                SetTextBoxValue(_advStatsTitle, eo.StatsTitle);
                SetTextBoxValue(_advTabTooltip, eo.TabTooltip);
                SetTextBoxValue(_advPinkRushName, eo.PinkRushName);
                SetTextBoxValue(_advPinkRushDescription, eo.PinkRushDescription);
                SetTextBoxValue(_advLuckyFlashLabel, eo.LuckyFlashLabel);
                SetTextBoxValue(_advLuckyBubbleLabel, eo.LuckyBubbleLabel);

                if (eo.BoostTooltips != null)
                    foreach (var kvp in eo.BoostTooltips)
                        AddAdvTooltipRow(_advBoostTooltipsPanel, _advBoostTooltipRows, kvp.Key, kvp.Value);

                if (eo.StatPillTooltips != null)
                    foreach (var kvp in eo.StatPillTooltips)
                        AddAdvTooltipRow(_advStatPillTooltipsPanel, _advStatPillTooltipRows, kvp.Key, kvp.Value);
            }
        }

        // ─── Clear ───────────────────────────────────────────────
        private void ClearAdvancedSection()
        {
            SetTextBoxValue(_advAvatarOffsetX, null);
            SetTextBoxValue(_advAvatarDetachedOffsetX, null);
            SetTextBoxValue(_advAvatarScale, null);
            SetTextBoxValue(_advAvatarOffsetY, null);
            SetTextBoxValue(_advAvatarDetachedOffsetY, null);
            SetTextBoxValue(_advBubbleScale, null);

            SetTextBoxValue(_advTreeTitle, null);
            SetTextBoxValue(_advTreeSubtitle, null);
            SetTextBoxValue(_advTreeWarning, null);
            SetTextBoxValue(_advPointsLabel, null);
            SetTextBoxValue(_advStatsTitle, null);
            SetTextBoxValue(_advTabTooltip, null);
            SetTextBoxValue(_advPinkRushName, null);
            SetTextBoxValue(_advPinkRushDescription, null);
            SetTextBoxValue(_advLuckyFlashLabel, null);
            SetTextBoxValue(_advLuckyBubbleLabel, null);

            _advBoostTooltipsPanel?.Children.Clear();
            _advBoostTooltipRows.Clear();
            _advStatPillTooltipsPanel?.Children.Clear();
            _advStatPillTooltipRows.Clear();
        }

        // ─── Value Helpers ───────────────────────────────────────
        private bool TryGetAdvInt(TextBox? tb, out int value)
        {
            value = 0;
            var text = GetTextBoxValue(tb).Trim();
            if (text.Length == 0) return false;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private bool TryGetAdvDouble(TextBox? tb, out double value)
        {
            value = 0;
            var text = GetTextBoxValue(tb).Trim();
            if (text.Length == 0) return false;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>Trimmed textbox value capped at <paramref name="maxLength"/>, or null when blank.</summary>
        private string? GetAdvString(TextBox? tb, int maxLength)
        {
            var text = GetTextBoxValue(tb).Trim();
            if (text.Length == 0) return null;
            return text.Length > maxLength ? text[..maxLength] : text;
        }

        /// <summary>Rows → dictionary; skips blank keys/values, caps values at 500 chars and the dictionary at 30 entries (SanitizeManifest limits). Null when empty.</summary>
        private static Dictionary<string, string>? CollectAdvTooltips(List<(TextBox Key, TextBox Value)> rows)
        {
            Dictionary<string, string>? dict = null;
            foreach (var (keyBox, valueBox) in rows)
            {
                var key = keyBox.Text.Trim();
                var value = valueBox.Text.Trim();
                if (key.Length == 0 || value.Length == 0) continue;
                if (value.Length > 500) value = value[..500];

                dict ??= new Dictionary<string, string>();
                if (dict.Count >= 30 && !dict.ContainsKey(key)) continue;
                dict[key] = value;
            }
            return dict;
        }
    }

    /// <summary>
    /// The editor's half of a manifest's <c>bubbleScale</c>: what the stored number becomes in the
    /// Advanced box, and what the box becomes on export. Split out of the window for the same
    /// reason <see cref="ModImageSlotRules"/> and <see cref="ModArtFramingRules"/> were -- these are
    /// decisions, they need no Dispatcher, and they are the part worth pinning with tests.
    ///
    /// <para><b>Why rules at all for one double.</b> The field had no round trip:
    /// <c>BuildManifestFromForm</c> builds a FRESH <see cref="Models.ModManifest"/>, so a value an
    /// author typed into <c>mod.json</c> by hand -- the only way to set it before this panel gained a
    /// box -- vanished the next time they opened the mod in the editor and re-exported. That also
    /// means the incoming number can be anything a text editor can hold, including NaN, and it has to
    /// come out as something the runtime would actually honour: <see cref="Services.BubbleSizing"/>
    /// clamps to 0.5..1.5 and ignores non-finite values, and the editor agrees with it rather than
    /// showing a figure the app would quietly refuse to use.</para>
    /// </summary>
    internal static class ModBubbleScaleRules
    {
        /// <summary>
        /// Decimals kept. An ambient bubble is 150-250 DIP, so a thousandth of a multiplier is a
        /// quarter of a pixel -- past that it is noise in the box and noise in mod.json.
        /// </summary>
        internal const int StoredDecimals = 3;

        /// <summary>
        /// A manifest value reduced to something worth showing and worth writing back, or null for
        /// "the mod said nothing". Non-finite is DROPPED rather than clamped: NaN is a typo, not a
        /// request for the smallest legal bubble, and <c>BubbleSizing</c> already treats it as
        /// absent -- clamping it to 0.5 here would invent a decision the author never made.
        /// </summary>
        internal static double? Sanitize(double? raw)
        {
            if (!raw.HasValue) return null;

            var value = raw.Value;
            if (double.IsNaN(value) || double.IsInfinity(value)) return null;

            return Math.Round(
                Math.Clamp(value, Services.BubbleSizing.ModScaleMin, Services.BubbleSizing.ModScaleMax),
                StoredDecimals);
        }

        /// <summary>
        /// The box text for a manifest value: invariant culture (the same reason
        /// <c>ModArtFramingRegistry.TryParsePoint</c> is invariant -- a decimal comma round-trips
        /// into a different number on a different machine), and blank when there is nothing to show
        /// so a re-export cannot pin a value the author never chose.
        /// </summary>
        internal static string? Format(double? raw) =>
            Sanitize(raw)?.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>
        /// What the box means on export. Blank or unparseable is null, which keeps the key out of
        /// mod.json entirely. An explicit 1.0 is KEPT: it changes nothing, but "this art is
        /// deliberately the app's own size" is a statement an author may want on the record.
        /// </summary>
        internal static double? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return null;

            return Sanitize(value);
        }
    }
}
