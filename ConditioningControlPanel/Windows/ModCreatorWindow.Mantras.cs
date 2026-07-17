using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Models;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Mantras panel for the mod editor. Authors the per-mod <c>mantras.json</c>
    /// (see <see cref="MantraSet"/> / <see cref="Services.MantraVoiceService"/>) that powers
    /// Takeover's "say it for me" flow. Written to
    /// <c>resources/sounds/companion_audio/mantras.json</c> with attached clips
    /// copied beside it, matching the runtime lookup in
    /// <c>MantraVoiceService.ResolveSetPath</c> / <c>ResolveAudio</c>.
    /// </summary>
    public partial class ModCreatorWindow
    {
        // ─── Mantras: state ──────────────────────────────────────

        /// <summary>One optional audio attachment (prompt/response/retry/timeout clip).</summary>
        private sealed class MantraAudioRef
        {
            /// <summary>Absolute path to the clip on disk (browse pick, or file inside a loaded mod). Null when nothing attached or the loaded clip is missing.</summary>
            public string? SourcePath;
            /// <summary>Filename written into mantras.json and copied into companion_audio. Null when no clip.</summary>
            public string? FileName;
            /// <summary>True when a loaded mod referenced this clip but the file was not found.</summary>
            public bool Missing;

            public TextBlock Label = null!;
            public Button PlayBtn = null!;
            public Button ClearBtn = null!;
        }

        private sealed class MantraCard
        {
            public string Id = "";                 // preserved from a loaded mod; generated on export otherwise
            public Border Root = null!;
            public TextBox PhraseBox = null!;
            public TextBox PromptBox = null!;
            public TextBlock ContainHint = null!;
            public TextBox ResponseBox = null!;
            public CheckBox EnabledCheck = null!;
            public MantraAudioRef PromptAudio = null!;
            public MantraAudioRef ResponseAudio = null!;
        }

        private sealed class MantraSharedLineRow
        {
            public StackPanel Root = null!;
            public TextBox TextBox = null!;
            public MantraAudioRef Audio = null!;
        }

        private readonly List<MantraCard> _mantraCards = new();
        private readonly List<MantraSharedLineRow> _mantraRetryRows = new();
        private readonly List<MantraSharedLineRow> _mantraTimeoutRows = new();
        private StackPanel? _mantraCardsPanel;
        private StackPanel? _mantraRetryPanel;
        private StackPanel? _mantraTimeoutPanel;

        // ─── Mantras: section UI ─────────────────────────────────
        private void BuildMantrasSection()
        {
            var panel = CreateSectionPanel("mantras");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Mantras"));
            stack.Children.Add(CreateSectionDescription(
                "Voiced mantra prompts for Takeover's \"say it for me\" flow. She speaks the prompt line, " +
                "the user repeats the phrase aloud, and she answers with the response. Audio is optional per " +
                "line - entries without clips are delivered as text only. The prompt text must contain the " +
                "phrase word-for-word."));

            // (1) Mantra cards
            stack.Children.Add(CreateSubHeader("Mantras"));
            _mantraCardsPanel = new StackPanel();
            stack.Children.Add(_mantraCardsPanel);

            var addMantraBtn = CreateMantraAddButton("+ Add mantra");
            addMantraBtn.Click += (_, _) => AddMantraCard(null, null);
            stack.Children.Add(addMantraBtn);

            // (2a) Shared retry lines
            stack.Children.Add(CreateSubHeader("Retry Lines"));
            stack.Children.Add(new TextBlock
            {
                Text = "Shared \"louder / say it again\" lines played when the attempt misses or is too quiet.",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#606080")),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 600,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
            _mantraRetryPanel = new StackPanel();
            stack.Children.Add(_mantraRetryPanel);

            var addRetryBtn = CreateMantraAddButton("+ Add retry line");
            addRetryBtn.Click += (_, _) => AddMantraSharedLineRow(_mantraRetryPanel, _mantraRetryRows, null, null, "retry");
            stack.Children.Add(addRetryBtn);

            // (2b) Shared timeout lines
            stack.Children.Add(CreateSubHeader("Timeout Lines"));
            stack.Children.Add(new TextBlock
            {
                Text = "Shared \"too shy?\" lines played when no speech is heard at all.",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#606080")),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 600,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
            _mantraTimeoutPanel = new StackPanel();
            stack.Children.Add(_mantraTimeoutPanel);

            var addTimeoutBtn = CreateMantraAddButton("+ Add timeout line");
            addTimeoutBtn.Click += (_, _) => AddMantraSharedLineRow(_mantraTimeoutPanel, _mantraTimeoutRows, null, null, "timeout");
            stack.Children.Add(addTimeoutBtn);
        }

        private Button CreateMantraAddButton(string content)
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
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 4),
            };
        }

        // ─── Mantras: mantra cards ───────────────────────────────

        /// <param name="entry">Existing entry to populate from (mod load), or null for a fresh card.</param>
        /// <param name="companionAudioDir">Extracted companion_audio folder to re-link clips from, or null.</param>
        private void AddMantraCard(MantraEntry? entry, string? companionAudioDir)
        {
            var card = new MantraCard { Id = entry?.Id ?? "" };

            var root = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#232342")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#353560")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 12),
                Margin = new Thickness(0, 0, 0, 10),
                MaxWidth = 560,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            card.Root = root;
            var body = new StackPanel();
            root.Child = body;

            // Header row: enabled checkbox + remove
            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            card.EnabledCheck = new CheckBox
            {
                Content = "Enabled",
                IsChecked = entry?.Enabled ?? true,
                Foreground = new SolidColorBrush(Color.FromRgb(192, 192, 192)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(card.EnabledCheck, 0);
            headerRow.Children.Add(card.EnabledCheck);

            var removeBtn = new Button
            {
                Content = "✕ Remove",
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            removeBtn.Click += (_, _) =>
            {
                _mantraCardsPanel?.Children.Remove(card.Root);
                _mantraCards.Remove(card);
            };
            Grid.SetColumn(removeBtn, 1);
            headerRow.Children.Add(removeBtn);
            body.Children.Add(headerRow);

            // Phrase
            body.Children.Add(CreateFieldLabel("Phrase (what the user must say) *"));
            card.PhraseBox = CreateDarkTextBox("good girls obey");
            card.PhraseBox.Width = 350;
            body.Children.Add(card.PhraseBox);

            // Prompt text
            body.Children.Add(CreateFieldLabel("Prompt text (what she says - must contain the phrase)"));
            card.PromptBox = CreateDarkTextBox("Say it for me, sweetie: good girls obey", multiline: true, height: 52);
            body.Children.Add(card.PromptBox);

            card.ContainHint = new TextBlock
            {
                Text = "⚠ Prompt text must contain the phrase word-for-word.",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 112, 112)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };
            body.Children.Add(card.ContainHint);

            card.PhraseBox.TextChanged += (_, _) => UpdateMantraContainHint(card);
            card.PromptBox.TextChanged += (_, _) => UpdateMantraContainHint(card);

            // Prompt audio
            body.Children.Add(CreateFieldLabel("Prompt audio (optional)"));
            card.PromptAudio = new MantraAudioRef();
            body.Children.Add(CreateMantraAudioRow(card.PromptAudio, "Select prompt audio",
                () => GetTextBoxValue(card.PhraseBox), "prompt"));

            // Response text
            body.Children.Add(CreateFieldLabel("Response text (her line when the user nails it)"));
            card.ResponseBox = CreateDarkTextBox("Mmm, good girl~", multiline: true, height: 40);
            body.Children.Add(card.ResponseBox);

            // Response audio
            body.Children.Add(CreateFieldLabel("Response audio (optional)"));
            card.ResponseAudio = new MantraAudioRef();
            body.Children.Add(CreateMantraAudioRow(card.ResponseAudio, "Select response audio",
                () => GetTextBoxValue(card.PhraseBox), "response"));

            // Populate from a loaded entry
            if (entry != null)
            {
                SetTextBoxValue(card.PhraseBox, entry.Phrase);
                SetTextBoxValue(card.PromptBox, entry.PromptText);
                SetTextBoxValue(card.ResponseBox, entry.Response);
                MantraLinkLoadedAudio(card.PromptAudio, entry.PromptAudio, companionAudioDir);
                MantraLinkLoadedAudio(card.ResponseAudio, entry.ResponseAudio, companionAudioDir);
                UpdateMantraContainHint(card);
            }

            _mantraCards.Add(card);
            _mantraCardsPanel?.Children.Add(root);
        }

        private void UpdateMantraContainHint(MantraCard card)
        {
            var phrase = GetTextBoxValue(card.PhraseBox).Trim();
            var prompt = GetTextBoxValue(card.PromptBox).Trim();
            var bad = phrase.Length > 0 && prompt.Length > 0 && !MantraPromptContainsPhrase(prompt, phrase);
            card.ContainHint.Visibility = bad ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool MantraPromptContainsPhrase(string promptText, string phrase)
            => promptText.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0;

        // ─── Mantras: shared retry/timeout rows ──────────────────

        private void AddMantraSharedLineRow(StackPanel? host, List<MantraSharedLineRow> rows,
            MantraLine? line, string? companionAudioDir, string role)
        {
            var row = new MantraSharedLineRow();
            var root = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            row.Root = root;

            row.TextBox = CreateDarkTextBox(role == "retry" ? "Louder, sweetie. Again." : "Too shy? Say it for me.");
            row.TextBox.Width = 260;
            row.TextBox.Margin = new Thickness(0, 0, 8, 0);
            row.TextBox.VerticalAlignment = VerticalAlignment.Center;
            root.Children.Add(row.TextBox);

            row.Audio = new MantraAudioRef();
            var audioRow = CreateMantraAudioRow(row.Audio, role == "retry" ? "Select retry audio" : "Select timeout audio",
                () => GetTextBoxValue(row.TextBox), role);
            audioRow.VerticalAlignment = VerticalAlignment.Center;
            root.Children.Add(audioRow);

            var removeBtn = new Button
            {
                Content = "Remove",
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 11,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            removeBtn.Click += (_, _) =>
            {
                host?.Children.Remove(row.Root);
                rows.Remove(row);
            };
            root.Children.Add(removeBtn);

            if (line != null)
            {
                SetTextBoxValue(row.TextBox, line.Text);
                MantraLinkLoadedAudio(row.Audio, line.Audio, companionAudioDir);
            }

            rows.Add(row);
            host?.Children.Add(root);
        }

        // ─── Mantras: audio attach row ───────────────────────────

        /// <summary>[filename label] [▶ preview] [Browse] [✕ clear] bound to <paramref name="audio"/>.</summary>
        private StackPanel CreateMantraAudioRow(MantraAudioRef audio, string browseTitle,
            Func<string> slugSource, string role)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };

            audio.Label = new TextBlock
            {
                Text = "No audio",
                Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 128)),
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 200,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            row.Children.Add(audio.Label);

            audio.PlayBtn = new Button
            {
                Content = "▶",
                Width = 26, Height = 26,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 100)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 11,
                Visibility = Visibility.Collapsed,
                ToolTip = "Play preview",
                VerticalAlignment = VerticalAlignment.Center,
            };
            audio.PlayBtn.Click += (_, _) =>
            {
                if (audio.SourcePath != null && File.Exists(audio.SourcePath))
                    ToggleAudioPreview(audio.SourcePath, audio.PlayBtn);
            };
            row.Children.Add(audio.PlayBtn);

            var browseBtn = new Button
            {
                Content = "Browse",
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 80)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 10,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            browseBtn.Click += (_, _) =>
            {
                var ofd = new OpenFileDialog
                {
                    Title = browseTitle,
                    Filter = "Audio Files|*.mp3;*.wav|All Files|*.*",
                };
                if (ofd.ShowDialog() != true) return;

                audio.SourcePath = ofd.FileName;
                audio.Missing = false;
                // New attachments always get an auto-generated target name; names loaded
                // from an existing mod are only kept while the clip is left untouched.
                audio.FileName = MantraAutoFileName(slugSource(), role, Path.GetExtension(ofd.FileName));
                UpdateMantraAudioUi(audio);
            };
            row.Children.Add(browseBtn);

            audio.ClearBtn = new Button
            {
                Content = "✕",
                Width = 22, Height = 22,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 10,
                Margin = new Thickness(4, 0, 0, 0),
                Visibility = Visibility.Collapsed,
                ToolTip = "Remove audio",
                VerticalAlignment = VerticalAlignment.Center,
            };
            audio.ClearBtn.Click += (_, _) =>
            {
                audio.SourcePath = null;
                audio.FileName = null;
                audio.Missing = false;
                UpdateMantraAudioUi(audio);
            };
            row.Children.Add(audio.ClearBtn);

            return row;
        }

        private static void UpdateMantraAudioUi(MantraAudioRef audio)
        {
            if (audio.FileName == null)
            {
                audio.Label.Text = "No audio";
                audio.Label.FontStyle = FontStyles.Italic;
                audio.Label.Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 128));
                audio.Label.ToolTip = null;
                audio.PlayBtn.Visibility = Visibility.Collapsed;
                audio.ClearBtn.Visibility = Visibility.Collapsed;
            }
            else if (audio.Missing)
            {
                audio.Label.Text = $"{audio.FileName} (missing)";
                audio.Label.FontStyle = FontStyles.Italic;
                audio.Label.Foreground = new SolidColorBrush(Color.FromRgb(255, 112, 112));
                audio.Label.ToolTip = "Referenced by the loaded mod, but the clip was not found in the package.";
                audio.PlayBtn.Visibility = Visibility.Collapsed;
                audio.ClearBtn.Visibility = Visibility.Visible;
            }
            else
            {
                audio.Label.Text = audio.FileName;
                audio.Label.FontStyle = FontStyles.Normal;
                audio.Label.Foreground = Brushes.White;
                audio.Label.ToolTip = audio.SourcePath;
                audio.PlayBtn.Visibility = Visibility.Visible;
                audio.ClearBtn.Visibility = Visibility.Visible;
            }
        }

        /// <summary>Re-link a clip filename from a loaded mod; flags it "(missing)" when the file is absent.</summary>
        private static void MantraLinkLoadedAudio(MantraAudioRef audio, string? fileName, string? companionAudioDir)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return;

            audio.FileName = fileName;
            var full = companionAudioDir != null ? Path.Combine(companionAudioDir, fileName) : null;
            if (full != null && File.Exists(full))
            {
                audio.SourcePath = full;
                audio.Missing = false;
            }
            else
            {
                audio.SourcePath = null;
                audio.Missing = true;
            }
            UpdateMantraAudioUi(audio);
        }

        // ─── Mantras: naming helpers ─────────────────────────────

        private static string MantraSlug(string text)
        {
            var slug = Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
            if (slug.Length > 24) slug = slug.Substring(0, 24).Trim('_');
            return string.IsNullOrEmpty(slug) ? "clip" : slug;
        }

        /// <summary>Auto-name a NEW attachment (mantra_{slug}_{role}.mp3 style), unique across the panel.</summary>
        private string MantraAutoFileName(string sourceText, string role, string ext)
        {
            if (string.IsNullOrWhiteSpace(ext)) ext = ".mp3";
            var baseName = $"mantra_{MantraSlug(sourceText)}_{role}";
            var name = baseName + ext;
            var n = 2;
            while (MantraFileNameTaken(name))
                name = $"{baseName}_{n++}{ext}";
            return name;
        }

        private bool MantraFileNameTaken(string fileName)
        {
            return AllMantraAudioRefs().Any(a =>
                a.FileName != null && a.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }

        private IEnumerable<MantraAudioRef> AllMantraAudioRefs()
        {
            foreach (var c in _mantraCards)
            {
                yield return c.PromptAudio;
                yield return c.ResponseAudio;
            }
            foreach (var r in _mantraRetryRows) yield return r.Audio;
            foreach (var r in _mantraTimeoutRows) yield return r.Audio;
        }

        // ─── Mantras: write / load / clear ───────────────────────

        /// <summary>
        /// Writes <c>{resourcesDir}/sounds/companion_audio/mantras.json</c> and copies attached
        /// clips beside it. No-op when the panel is empty.
        /// </summary>
        private void WriteMantrasTo(string resourcesDir)
        {
            var set = new MantraSet();
            var skipped = new List<string>();
            var clipsToCopy = new List<MantraAudioRef>();
            var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var card in _mantraCards)
            {
                var phrase = GetTextBoxValue(card.PhraseBox).Trim();
                var prompt = GetTextBoxValue(card.PromptBox).Trim();
                var response = GetTextBoxValue(card.ResponseBox).Trim();

                var hasAnything = phrase.Length > 0 || prompt.Length > 0 || response.Length > 0
                                  || card.PromptAudio.FileName != null || card.ResponseAudio.FileName != null;
                if (!hasAnything) continue; // blank card, ignore silently

                if (phrase.Length == 0)
                {
                    var label = prompt.Length > 0 ? $"\"{MantraTruncate(prompt, 40)}\"" : "(unnamed mantra)";
                    skipped.Add($"{label} - no phrase set (the phrase is the recognition target and is required).");
                    continue;
                }

                // Runtime contract: PromptText must contain Phrase (MantraEntry doc + say-it flow).
                if (prompt.Length > 0 && !MantraPromptContainsPhrase(prompt, phrase))
                {
                    skipped.Add($"\"{MantraTruncate(phrase, 40)}\" - prompt text does not contain the phrase.");
                    continue;
                }

                var id = !string.IsNullOrWhiteSpace(card.Id) ? card.Id : $"mantra_{MantraSlug(phrase)}";
                var uniqueId = id;
                var n = 2;
                while (!usedIds.Add(uniqueId))
                    uniqueId = $"{id}_{n++}";

                set.Mantras.Add(new MantraEntry
                {
                    Id = uniqueId,
                    Phrase = phrase,
                    PromptText = prompt.Length > 0 ? prompt : phrase, // empty prompt falls back to the phrase itself
                    PromptAudio = card.PromptAudio.FileName,
                    Response = response,
                    ResponseAudio = card.ResponseAudio.FileName,
                    Enabled = card.EnabledCheck.IsChecked != false,
                });
                clipsToCopy.Add(card.PromptAudio);
                clipsToCopy.Add(card.ResponseAudio);
            }

            AppendMantraSharedLines(_mantraRetryRows, set.Retry, clipsToCopy);
            AppendMantraSharedLines(_mantraTimeoutRows, set.Timeout, clipsToCopy);

            if (skipped.Count > 0)
            {
                MessageBox.Show(
                    "Some mantras were skipped during export:\n\n- " + string.Join("\n- ", skipped) +
                    "\n\nFix them and export again to include them.",
                    "Mantras - Skipped Entries", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // No-op when there is nothing to ship.
            if (set.Mantras.Count == 0 && set.Retry.Count == 0 && set.Timeout.Count == 0) return;

            var audioDir = Path.Combine(resourcesDir, "sounds", "companion_audio");
            Directory.CreateDirectory(audioDir);

            var json = JsonConvert.SerializeObject(set, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
            File.WriteAllText(Path.Combine(audioDir, "mantras.json"), json);

            foreach (var clip in clipsToCopy)
            {
                if (clip.FileName == null || clip.SourcePath == null || !File.Exists(clip.SourcePath)) continue;
                var dest = Path.Combine(audioDir, clip.FileName);
                // Skip self-copy when re-exporting a mod loaded from this very folder.
                if (string.Equals(Path.GetFullPath(clip.SourcePath), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                    continue;
                File.Copy(clip.SourcePath, dest, overwrite: true);
            }
        }

        private void AppendMantraSharedLines(List<MantraSharedLineRow> rows, List<MantraLine> target,
            List<MantraAudioRef> clipsToCopy)
        {
            foreach (var row in rows)
            {
                var text = GetTextBoxValue(row.TextBox).Trim();
                if (text.Length == 0 && row.Audio.FileName == null) continue; // blank row

                target.Add(new MantraLine { Text = text, Audio = row.Audio.FileName });
                clipsToCopy.Add(row.Audio);
            }
        }

        private static string MantraTruncate(string s, int max)
            => s.Length <= max ? s : s.Substring(0, max) + "…";

        /// <summary>
        /// Parses <c>{resourcesDir}/sounds/companion_audio/mantras.json</c> if present and
        /// repopulates the panel, re-linking clip filenames (missing clips are flagged).
        /// </summary>
        private void LoadMantrasFrom(string resourcesDir)
        {
            ClearMantrasSection();

            var audioDir = Path.Combine(resourcesDir, "sounds", "companion_audio");
            var path = Path.Combine(audioDir, "mantras.json");
            if (!File.Exists(path)) return;

            MantraSet? set;
            try
            {
                set = JsonConvert.DeserializeObject<MantraSet>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ModCreator: failed to parse mantras.json at {Path}", path);
                MessageBox.Show($"Could not parse mantras.json in this mod:\n{ex.Message}",
                    "Mantras - Load Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (set == null) return;

            foreach (var entry in set.Mantras)
                AddMantraCard(entry, audioDir);
            foreach (var line in set.Retry)
                AddMantraSharedLineRow(_mantraRetryPanel, _mantraRetryRows, line, audioDir, "retry");
            foreach (var line in set.Timeout)
                AddMantraSharedLineRow(_mantraTimeoutPanel, _mantraTimeoutRows, line, audioDir, "timeout");
        }

        private void ClearMantrasSection()
        {
            _mantraCards.Clear();
            _mantraRetryRows.Clear();
            _mantraTimeoutRows.Clear();
            _mantraCardsPanel?.Children.Clear();
            _mantraRetryPanel?.Children.Clear();
            _mantraTimeoutPanel?.Children.Clear();
        }
    }
}
