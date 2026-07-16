using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Barks panel for the mod creator: authors the mod-side
    /// <c>resources/sounds/companion_audio/bark_rules.json</c> manifest that
    /// <see cref="Services.Bark.BarkRuleLoader"/> field-level-merges over the embedded base
    /// manifest at runtime. Two card modes: NEW RULE (full fields) and OVERRIDE BASE RULE
    /// (id + variant_pool only — trigger/mood/cooldown/etc inherit from the base rule).
    /// Each card keeps its raw <see cref="JObject"/> as the backing store so fields this
    /// UI does not surface (e.g. pool_ref) survive a load → export round trip.
    /// </summary>
    public partial class ModCreatorWindow
    {
        // ─── Barks state ─────────────────────────────────────────
        private StackPanel? _barkCardsPanel;
        private readonly List<BarkCardState> _barkCards = new();
        private readonly List<string> _barkBaseTriggers = new();
        private readonly List<string> _barkBaseRuleIds = new();

        private sealed class BarkVariantRowState
        {
            public StackPanel Row = null!;
            public TextBox TextBox = null!;
            public TextBlock FileLabel = null!;
            public Button PlayButton = null!;
            /// <summary>Absolute path to copy the mp3 from on export. Null = no audio or missing source.</summary>
            public string? SourcePath;
            /// <summary>Filename referenced in JSON and written beside bark_rules.json.</summary>
            public string? TargetFileName;
        }

        private sealed class BarkCardState
        {
            public Border Card = null!;
            /// <summary>Raw backing rule object; unsurfaced fields round-trip through here.</summary>
            public JObject Json = new();
            public bool LoadedFromFile;
            public ComboBox ModeCombo = null!;
            public StackPanel NewFieldsPanel = null!;
            public StackPanel OverridePanel = null!;
            public TextBox IdBox = null!;
            public TextBox TriggerBox = null!;
            public TextBox PriorityBox = null!;
            public TextBox CooldownBox = null!;
            public TextBox ChanceBox = null!;
            public CheckBox RepeatableCheck = null!;
            public ComboBox ScopeCombo = null!;
            public ComboBox ClassCombo = null!;
            public TextBox MoodBox = null!;
            public TextBox ConditionsBox = null!;
            public ComboBox? OverrideIdCombo;   // present when the base manifest parsed
            public TextBox? OverrideIdBox;      // fallback when it did not
            public StackPanel VariantsPanel = null!;
            public readonly List<BarkVariantRowState> Variants = new();
            public bool IsOverride => ModeCombo.SelectedIndex == 1;
        }

        // ─── Section build ───────────────────────────────────────
        private void BuildBarksSection()
        {
            var panel = CreateSectionPanel("barks");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Barks"));
            stack.Children.Add(CreateSectionDescription(
                "Reactive companion voicelines that fire on in-app events (bubble pops, level ups, feature opens...). " +
                "Each rule listens to a trigger and speaks one line from its variant pool. Text-only variants are valid " +
                "- they show as a speech bubble with no audio. You can add brand-new rules, or override a base rule's " +
                "lines while inheriting its trigger, mood and cooldown."));

            LoadBarkBaseManifest();

            _barkCardsPanel = new StackPanel();
            stack.Children.Add(_barkCardsPanel);

            var addRuleBtn = CreateBarkAccentButton("+ Add rule");
            addRuleBtn.Margin = new Thickness(0, 6, 0, 0);
            addRuleBtn.Click += (_, _) => AddBarkRuleCard(null, null);
            stack.Children.Add(addRuleBtn);
        }

        /// <summary>Parse the embedded base manifest (same location BarkRuleLoader uses) for trigger/id pickers.</summary>
        private void LoadBarkBaseManifest()
        {
            _barkBaseTriggers.Clear();
            _barkBaseRuleIds.Clear();
            try
            {
                var path = Services.Bark.BarkRuleLoader.EmbeddedManifestPath;
                if (!File.Exists(path)) return;
                var arr = JArray.Parse(File.ReadAllText(path));
                foreach (var obj in arr.OfType<JObject>())
                {
                    var trig = obj.Value<string>("trigger");
                    if (!string.IsNullOrWhiteSpace(trig) && !_barkBaseTriggers.Contains(trig, StringComparer.OrdinalIgnoreCase))
                        _barkBaseTriggers.Add(trig);
                    var id = obj.Value<string>("id");
                    if (!string.IsNullOrWhiteSpace(id) && !_barkBaseRuleIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                        _barkBaseRuleIds.Add(id);
                }
                _barkBaseTriggers.Sort(StringComparer.OrdinalIgnoreCase);
                _barkBaseRuleIds.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // Base manifest unreadable: pickers degrade to free text boxes.
                _barkBaseTriggers.Clear();
                _barkBaseRuleIds.Clear();
            }
        }

        // ─── Card ────────────────────────────────────────────────
        private BarkCardState AddBarkRuleCard(JObject? loaded, string? audioDir)
        {
            var card = new BarkCardState();
            var body = new StackPanel();
            card.Card = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#23233F")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#35355A")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10),
                MaxWidth = 640,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = body
            };

            // Header row: mode picker + remove
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var removeBtn = new Button
            {
                Content = "✕ Remove",
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            removeBtn.Click += (_, _) =>
            {
                _barkCardsPanel?.Children.Remove(card.Card);
                _barkCards.Remove(card);
            };
            DockPanel.SetDock(removeBtn, Dock.Right);
            header.Children.Add(removeBtn);

            var modeLabel = new TextBlock
            {
                Text = "Mode ",
                Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(modeLabel);
            card.ModeCombo = CreateBarkCombo(200, "New rule", "Override base rule");
            card.ModeCombo.SelectedIndex = 0;
            header.Children.Add(card.ModeCombo);
            body.Children.Add(header);

            // ── New-rule fields ──
            card.NewFieldsPanel = new StackPanel();
            body.Children.Add(card.NewFieldsPanel);

            card.NewFieldsPanel.Children.Add(CreateFieldLabel("Rule id (unique, snake_case)"));
            card.IdBox = CreateDarkTextBox("e.g. my_custom_bark");
            card.IdBox.Width = 260;
            card.IdBox.LostFocus += (_, _) =>
            {
                var v = GetTextBoxValue(card.IdBox);
                if (!string.IsNullOrWhiteSpace(v)) SetTextBoxValue(card.IdBox, BarkSlug(v));
            };
            card.NewFieldsPanel.Children.Add(card.IdBox);

            card.NewFieldsPanel.Children.Add(CreateFieldLabel("Trigger (event that fires this bark)"));
            var trigRow = new StackPanel { Orientation = Orientation.Horizontal };
            card.TriggerBox = CreateDarkTextBox("e.g. ChaosRunStarted");
            card.TriggerBox.Width = 240;
            trigRow.Children.Add(card.TriggerBox);
            if (_barkBaseTriggers.Count > 0)
            {
                var trigPick = CreateBarkCombo(200, _barkBaseTriggers.ToArray());
                trigPick.Margin = new Thickness(8, 0, 0, 4);
                trigPick.SelectionChanged += (_, _) =>
                {
                    if (trigPick.SelectedItem is string t) SetTextBoxValue(card.TriggerBox, t);
                };
                trigRow.Children.Add(trigPick);
            }
            card.NewFieldsPanel.Children.Add(trigRow);

            // Numeric row: priority / cooldown / chance
            var numRow = new StackPanel { Orientation = Orientation.Horizontal };
            card.PriorityBox = AddBarkInlineField(numRow, "Priority", "0", 60, first: true);
            card.CooldownBox = AddBarkInlineField(numRow, "Cooldown ms", "0", 80);
            card.ChanceBox = AddBarkInlineField(numRow, "Chance 0-1", "1.0", 60);
            card.NewFieldsPanel.Children.Add(numRow);

            // Enum row: repeatable / scope / class
            var enumRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            card.RepeatableCheck = new CheckBox
            {
                Content = "Repeatable",
                IsChecked = true,
                Foreground = new SolidColorBrush(Color.FromRgb(192, 192, 192)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            enumRow.Children.Add(card.RepeatableCheck);
            enumRow.Children.Add(CreateBarkInlineLabel("Scope"));
            card.ScopeCombo = CreateBarkCombo(100, "session", "tier", "lifetime");
            card.ScopeCombo.SelectedIndex = 0;
            enumRow.Children.Add(card.ScopeCombo);
            enumRow.Children.Add(CreateBarkInlineLabel("Class"));
            card.ClassCombo = CreateBarkCombo(110, "normal", "easter_egg", "safety");
            card.ClassCombo.SelectedIndex = 0;
            enumRow.Children.Add(card.ClassCombo);
            card.NewFieldsPanel.Children.Add(enumRow);

            card.NewFieldsPanel.Children.Add(CreateFieldLabel("Mood (comma-separated flavor words)"));
            card.MoodBox = CreateDarkTextBox("e.g. teasing, soft");
            card.MoodBox.Width = 260;
            card.NewFieldsPanel.Children.Add(card.MoodBox);

            card.NewFieldsPanel.Children.Add(CreateFieldLabel("Conditions (advanced, raw JSON object - leave empty for none)"));
            card.ConditionsBox = CreateDarkTextBox("{ \"level_gte\": 10 }", multiline: true, height: 56);
            card.ConditionsBox.Width = 400;
            card.ConditionsBox.FontFamily = new FontFamily("Consolas");
            card.NewFieldsPanel.Children.Add(card.ConditionsBox);

            // ── Override fields ──
            card.OverridePanel = new StackPanel { Visibility = Visibility.Collapsed };
            body.Children.Add(card.OverridePanel);
            card.OverridePanel.Children.Add(CreateFieldLabel("Base rule to override (your variants replace its lines)"));
            if (_barkBaseRuleIds.Count > 0)
            {
                card.OverrideIdCombo = CreateBarkCombo(300, _barkBaseRuleIds.ToArray());
                card.OverridePanel.Children.Add(card.OverrideIdCombo);
            }
            else
            {
                card.OverrideIdBox = CreateDarkTextBox("base rule id");
                card.OverrideIdBox.Width = 300;
                card.OverridePanel.Children.Add(card.OverrideIdBox);
            }

            card.ModeCombo.SelectionChanged += (_, _) =>
            {
                card.NewFieldsPanel.Visibility = card.IsOverride ? Visibility.Collapsed : Visibility.Visible;
                card.OverridePanel.Visibility = card.IsOverride ? Visibility.Visible : Visibility.Collapsed;
            };

            // ── Variant pool ──
            body.Children.Add(CreateSubHeader("Variant pool"));
            card.VariantsPanel = new StackPanel();
            body.Children.Add(card.VariantsPanel);
            var addVariantBtn = CreateBarkAccentButton("+ Add variant");
            addVariantBtn.FontSize = 11;
            addVariantBtn.Padding = new Thickness(10, 4, 10, 4);
            addVariantBtn.Margin = new Thickness(0, 4, 0, 0);
            addVariantBtn.Click += (_, _) => AddBarkVariantRow(card, "", null, null);
            body.Children.Add(addVariantBtn);

            _barkCards.Add(card);
            _barkCardsPanel?.Children.Add(card.Card);

            if (loaded != null) PopulateBarkCard(card, loaded, audioDir);
            else AddBarkVariantRow(card, "", null, null); // start with one empty line

            return card;
        }

        private void PopulateBarkCard(BarkCardState card, JObject loaded, string? audioDir)
        {
            card.Json = (JObject)loaded.DeepClone();
            card.LoadedFromFile = true;
            var id = loaded.Value<string>("id") ?? "";

            // A pool-only overlay (no trigger field) is an override of a base rule.
            bool isOverride = loaded["trigger"] == null;
            card.ModeCombo.SelectedIndex = isOverride ? 1 : 0;
            card.NewFieldsPanel.Visibility = isOverride ? Visibility.Collapsed : Visibility.Visible;
            card.OverridePanel.Visibility = isOverride ? Visibility.Visible : Visibility.Collapsed;

            if (isOverride)
            {
                if (card.OverrideIdCombo != null)
                {
                    var match = _barkBaseRuleIds.FirstOrDefault(b => string.Equals(b, id, StringComparison.OrdinalIgnoreCase));
                    if (match != null) card.OverrideIdCombo.SelectedItem = match;
                    else { card.OverrideIdCombo.Items.Add(id); card.OverrideIdCombo.SelectedItem = id; }
                }
                else if (card.OverrideIdBox != null)
                {
                    SetTextBoxValue(card.OverrideIdBox, id);
                }
            }

            SetTextBoxValue(card.IdBox, id);
            SetTextBoxValue(card.TriggerBox, loaded.Value<string>("trigger") ?? "");
            SetTextBoxValue(card.PriorityBox, loaded["priority"] != null ? loaded.Value<int>("priority").ToString(CultureInfo.InvariantCulture) : "");
            SetTextBoxValue(card.CooldownBox, loaded["cooldown_ms"] != null ? loaded.Value<int>("cooldown_ms").ToString(CultureInfo.InvariantCulture) : "");
            SetTextBoxValue(card.ChanceBox, loaded["chance"] != null ? loaded.Value<double>("chance").ToString(CultureInfo.InvariantCulture) : "");
            card.RepeatableCheck.IsChecked = loaded.Value<bool?>("repeatable") ?? true;
            SelectBarkComboValue(card.ScopeCombo, loaded.Value<string>("scope") ?? "session");
            SelectBarkComboValue(card.ClassCombo, loaded.Value<string>("class") ?? "normal");
            SetTextBoxValue(card.MoodBox, loaded.Value<string>("mood") ?? "");
            if (loaded["conditions"] is JObject cond)
                SetTextBoxValue(card.ConditionsBox, cond.ToString(Formatting.Indented));

            if (loaded["variant_pool"] is JArray pool)
            {
                foreach (var token in pool)
                {
                    if (token.Type == JTokenType.String)
                    {
                        AddBarkVariantRow(card, token.Value<string>() ?? "", null, null);
                    }
                    else if (token is JObject vo)
                    {
                        var text = (string?)(vo["text"] ?? vo["display"]) ?? "";
                        var audio = (string?)(vo["audio"] ?? vo["file"]);
                        if (string.IsNullOrWhiteSpace(audio))
                        {
                            AddBarkVariantRow(card, text, null, null);
                        }
                        else
                        {
                            // Re-link to the mp3 in the same folder; missing → keep the name, flag it.
                            var src = audioDir != null ? Path.Combine(audioDir, audio) : null;
                            AddBarkVariantRow(card, text, audio, src != null && File.Exists(src) ? src : null);
                        }
                    }
                }
            }
            if (card.Variants.Count == 0) AddBarkVariantRow(card, "", null, null);
        }

        // ─── Variant rows ────────────────────────────────────────
        private void AddBarkVariantRow(BarkCardState card, string text, string? targetFileName, string? sourcePath)
        {
            var row = new BarkVariantRowState { TargetFileName = targetFileName, SourcePath = sourcePath };
            row.Row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };

            row.TextBox = CreateDarkTextBox("Line text...");
            row.TextBox.Width = 280;
            row.TextBox.Margin = new Thickness(0, 0, 6, 0);
            if (!string.IsNullOrEmpty(text)) SetTextBoxValue(row.TextBox, text);
            row.Row.Children.Add(row.TextBox);

            var attachBtn = new Button
            {
                Content = "Attach mp3…",
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 80)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 10,
                Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Row.Children.Add(attachBtn);

            row.FileLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 128)),
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                MaxWidth = 160,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            row.Row.Children.Add(row.FileLabel);

            row.PlayButton = new Button
            {
                Content = "▶",
                Width = 24, Height = 24,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 100)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Play preview"
            };
            row.PlayButton.Click += (_, _) =>
            {
                if (row.SourcePath != null) ToggleAudioPreview(row.SourcePath, row.PlayButton);
            };
            row.Row.Children.Add(row.PlayButton);

            var removeBtn = new Button
            {
                Content = "✕",
                Width = 20, Height = 20,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 9,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            removeBtn.Click += (_, _) =>
            {
                card.VariantsPanel.Children.Remove(row.Row);
                card.Variants.Remove(row);
            };
            row.Row.Children.Add(removeBtn);

            attachBtn.Click += (_, _) =>
            {
                var ofd = new OpenFileDialog { Title = "Select bark voiceline", Filter = "MP3 Audio|*.mp3" };
                if (ofd.ShowDialog() != true) return;
                row.SourcePath = ofd.FileName;
                // New attachments get a collision-free name derived from the rule id.
                row.TargetFileName = NextBarkAudioName(GetBarkCardId(card));
                UpdateBarkVariantFileLabel(row);
            };

            UpdateBarkVariantFileLabel(row);
            card.Variants.Add(row);
            card.VariantsPanel.Children.Add(row.Row);
        }

        private static void UpdateBarkVariantFileLabel(BarkVariantRowState row)
        {
            if (row.TargetFileName == null)
            {
                row.FileLabel.Text = "text only";
                row.FileLabel.FontStyle = FontStyles.Italic;
                row.FileLabel.Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 128));
                row.PlayButton.Visibility = Visibility.Collapsed;
                return;
            }
            bool missing = row.SourcePath == null || !File.Exists(row.SourcePath);
            row.FileLabel.Text = missing ? row.TargetFileName + " (missing)" : row.TargetFileName;
            row.FileLabel.FontStyle = FontStyles.Normal;
            row.FileLabel.Foreground = missing
                ? new SolidColorBrush(Color.FromRgb(255, 150, 100))
                : Brushes.White;
            row.PlayButton.Visibility = missing ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Collision-free target filename for a freshly attached mp3: {ruleId}_{n}.mp3.</summary>
        private string NextBarkAudioName(string ruleId)
        {
            var baseId = string.IsNullOrWhiteSpace(ruleId) ? "bark" : BarkSlug(ruleId);
            var taken = new HashSet<string>(
                _barkCards.SelectMany(c => c.Variants)
                          .Where(v => v.TargetFileName != null)
                          .Select(v => v.TargetFileName!),
                StringComparer.OrdinalIgnoreCase);
            for (int n = 1; ; n++)
            {
                var name = $"{baseId}_{n}.mp3";
                if (!taken.Contains(name)) return name;
            }
        }

        // ─── Export ──────────────────────────────────────────────
        private void WriteBarksTo(string resourcesDir)
        {
            if (_barkCards.Count == 0) return;

            var arr = new JArray();
            var copies = new List<(string Source, string Target)>();
            var skipped = new List<string>();
            var badConditions = new List<string>();

            foreach (var card in _barkCards)
            {
                var id = GetBarkCardId(card);
                if (string.IsNullOrWhiteSpace(id)) { skipped.Add("(no id)"); continue; }

                var cardCopies = new List<(string Source, string Target)>();
                var pool = BuildBarkVariantPool(card, cardCopies);
                var o = card.Json;

                if (card.IsOverride)
                {
                    if (pool.Count == 0) { skipped.Add(id); continue; }
                    if (!card.LoadedFromFile) o.RemoveAll(); // fresh override: emit only {id, variant_pool}
                    o["id"] = id;
                    o["variant_pool"] = pool;
                }
                else
                {
                    var trigger = GetTextBoxValue(card.TriggerBox).Trim();
                    if (string.IsNullOrEmpty(trigger)) { skipped.Add(id); continue; }
                    o["id"] = id;
                    o["trigger"] = trigger;
                    // Only emit numeric fields the author actually filled in, so a loaded
                    // rule that omitted them round-trips without gaining explicit zeros.
                    SetBarkIntField(o, "priority", card.PriorityBox);
                    SetBarkIntField(o, "cooldown_ms", card.CooldownBox);

                    var chanceText = GetTextBoxValue(card.ChanceBox).Trim();
                    if (double.TryParse(chanceText, NumberStyles.Float, CultureInfo.InvariantCulture, out var chance))
                        o["chance"] = Math.Clamp(chance, 0.0, 1.0);
                    else
                        o.Remove("chance");

                    o["repeatable"] = card.RepeatableCheck.IsChecked == true;
                    o["scope"] = card.ScopeCombo.SelectedItem as string ?? "session";
                    o["class"] = card.ClassCombo.SelectedItem as string ?? "normal";

                    var mood = GetTextBoxValue(card.MoodBox).Trim();
                    if (mood.Length > 0) o["mood"] = mood; else o.Remove("mood");

                    var condText = GetTextBoxValue(card.ConditionsBox).Trim();
                    if (condText.Length == 0)
                    {
                        o.Remove("conditions");
                    }
                    else
                    {
                        try { o["conditions"] = JObject.Parse(condText); }
                        catch { badConditions.Add(id); } // keep whatever the backing store had
                    }

                    if (pool.Count > 0) o["variant_pool"] = pool;
                    else o.Remove("variant_pool");
                    if (pool.Count == 0 && string.IsNullOrWhiteSpace(o.Value<string>("pool_ref")))
                    {
                        skipped.Add(id);
                        continue;
                    }
                }

                arr.Add(o.DeepClone());
                copies.AddRange(cardCopies);
            }

            if (badConditions.Count > 0)
            {
                MessageBox.Show(this,
                    "The Conditions JSON is invalid for these bark rules:\n\n  " +
                    string.Join("\n  ", badConditions) +
                    "\n\nThose rules were exported without the invalid conditions.",
                    "Barks - invalid conditions", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            if (arr.Count == 0)
            {
                if (skipped.Count > 0)
                    TxtStatus.Text = $"Barks: nothing exported ({skipped.Count} incomplete rule(s) skipped)";
                return;
            }

            var dir = Path.Combine(resourcesDir, "sounds", "companion_audio");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Services.Bark.BarkRuleLoader.ManifestFileName),
                arr.ToString(Formatting.Indented));

            foreach (var (source, target) in copies.DistinctBy(c => c.Target, StringComparer.OrdinalIgnoreCase))
            {
                try { File.Copy(source, Path.Combine(dir, target), overwrite: true); }
                catch { /* unreadable source: JSON still references the name */ }
            }

            var notes = new List<string>();
            if (skipped.Count > 0) notes.Add($"{skipped.Count} incomplete rule(s) skipped ({string.Join(", ", skipped)})");
            if (badConditions.Count > 0) notes.Add($"invalid conditions dropped on {string.Join(", ", badConditions)}");
            if (notes.Count > 0) TxtStatus.Text = "Barks: " + string.Join("; ", notes);
        }

        /// <summary>
        /// Build variant_pool JSON: object {text, audio} for voiced lines, bare string for
        /// text-only (both shapes are accepted by BarkVariantJsonConverter.ReadJson).
        /// </summary>
        private JArray BuildBarkVariantPool(BarkCardState card, List<(string Source, string Target)> copies)
        {
            var pool = new JArray();
            foreach (var v in card.Variants)
            {
                var text = GetTextBoxValue(v.TextBox).Trim();
                if (text.Length == 0 && v.TargetFileName == null) continue; // empty row

                if (v.TargetFileName != null)
                {
                    pool.Add(new JObject { ["text"] = text, ["audio"] = v.TargetFileName });
                    if (v.SourcePath != null && File.Exists(v.SourcePath))
                        copies.Add((v.SourcePath, v.TargetFileName));
                }
                else
                {
                    pool.Add(new JValue(text));
                }
            }
            return pool;
        }

        // ─── Import ──────────────────────────────────────────────
        private void LoadBarksFrom(string resourcesDir)
        {
            ClearBarksSection();
            var dir = Path.Combine(resourcesDir, "sounds", "companion_audio");
            var path = Path.Combine(dir, Services.Bark.BarkRuleLoader.ManifestFileName);
            if (!File.Exists(path)) return;

            try
            {
                var arr = JArray.Parse(File.ReadAllText(path));
                foreach (var obj in arr.OfType<JObject>())
                    AddBarkRuleCard(obj, dir);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ModCreator: failed to load bark_rules.json from {Path}", path);
                TxtStatus.Text = "Barks: could not parse the mod's bark_rules.json";
            }
        }

        private void ClearBarksSection()
        {
            _barkCards.Clear();
            _barkCardsPanel?.Children.Clear();
        }

        // ─── Helpers ─────────────────────────────────────────────
        private string GetBarkCardId(BarkCardState card)
        {
            if (card.IsOverride)
            {
                if (card.OverrideIdCombo != null) return card.OverrideIdCombo.SelectedItem as string ?? "";
                return GetTextBoxValue(card.OverrideIdBox).Trim();
            }
            return BarkSlug(GetTextBoxValue(card.IdBox));
        }

        private static string BarkSlug(string s) =>
            Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');

        private void SetBarkIntField(JObject o, string field, TextBox box)
        {
            var text = GetTextBoxValue(box).Trim();
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                o[field] = v;
            else
                o.Remove(field);
        }

        private static void SelectBarkComboValue(ComboBox combo, string value)
        {
            foreach (var item in combo.Items)
            {
                if (item is string s && string.Equals(s, value?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private ComboBox CreateBarkCombo(double width, params string[] items)
        {
            var combo = new ComboBox
            {
                Width = width,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            if (TryFindResource("DarkComboBoxStyle") is Style dark)
            {
                combo.Style = dark;
                combo.FontSize = 12;
            }
            foreach (var item in items) combo.Items.Add(item);
            return combo;
        }

        private Button CreateBarkAccentButton(string content)
        {
            var accent = App.Mods?.GetAccentColorHex() ?? "#FF69B4";
            return new Button
            {
                Content = content,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A4A")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent)),
                Padding = new Thickness(16, 8, 16, 8),
                Cursor = Cursors.Hand,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }

        private static TextBlock CreateBarkInlineLabel(string text) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0)
        };

        private TextBox AddBarkInlineField(StackPanel row, string label, string placeholder, double width, bool first = false)
        {
            var lbl = CreateBarkInlineLabel(label);
            if (first) lbl.Margin = new Thickness(0, 0, 0, 0);
            row.Children.Add(lbl);
            var box = CreateDarkTextBox(placeholder);
            box.Width = width;
            box.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(box);
            return box;
        }
    }
}
