using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace ConditioningControlPanel
{
    /// <summary>
    /// "Portraits" panel of the mod editor — authors the emotive-portrait avatar
    /// (<c>sounds/companion_audio/avatar_manifest.json</c> + portrait image folders) consumed at
    /// runtime by <see cref="AvatarPortraitLoader"/> / <see cref="AvatarPortraitSet"/>.
    ///
    /// Two mutually-exclusive modes:
    ///   A) Import — bring in a complete hand-written <c>avatar_manifest.json</c>; every
    ///      <c>skins[].dir</c> + <c>emotions[].portraits[].file</c> is validated with the same
    ///      path math <see cref="AvatarPortraitSet"/> uses (missing files are warnings, not
    ///      errors — the runtime skips them gracefully).
    ///   B) Simple author — pick folders of PNGs; each folder becomes a skin, each PNG stem
    ///      becomes an emotion with that single portrait. A minimal manifest is generated on
    ///      export with sane director defaults.
    /// </summary>
    public partial class ModCreatorWindow
    {
        // ─── Portraits State ─────────────────────────────────────
        // Mode A (import): the deserialized manifest + the folder it was read from
        // (portrait dirs resolve relative to that folder, exactly like AvatarPortraitSet).
        private AvatarPortraitManifest? _portraitImportedManifest;
        private string? _portraitImportedBaseDir;

        // Mode B (simple author): one entry per picked skin folder.
        private readonly List<PortraitSkinEntry> _portraitSkins = new();

        // UI references
        private GroupBox? _portraitImportGroup, _portraitAuthorGroup;
        private TextBlock? _portraitImportSummary;
        private StackPanel? _portraitValidationPanel;
        private StackPanel? _portraitSkinsPanel;
        private ComboBox? _portraitIdleCombo, _portraitDefaultCombo;

        private sealed class PortraitSkinEntry
        {
            public string SourceDir = "";               // absolute folder the user picked
            public string DirName = "";                 // folder name → skin id + portraits/{DirName}
            public TextBox TitleBox = null!;            // editable display label → skins[].title
            public List<string> PngFiles = new();       // actual file names (with extension)
            public Border Row = null!;                  // UI row, for removal
        }

        /// <summary>Emotion name for a portrait file: lowercased filename stem (neutral.png → "neutral").</summary>
        private static string PortraitEmotionFromFile(string fileName) =>
            Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();

        // ─── Section UI ──────────────────────────────────────────
        private void BuildPortraitsSection()
        {
            var panel = CreateSectionPanel("portraits");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Portraits"));
            stack.Children.Add(CreateSectionDescription(
                "Emotive portrait avatar for the companion. When your mod ships an avatar manifest, " +
                "the tube avatar switches to full emotion-driven portraits (one image bucket per " +
                "emotion, per outfit skin) instead of the legacy 4-pose set. Either import a " +
                "hand-written avatar_manifest.json, or build a simple one from folders of PNGs. " +
                "Using one mode disables the other; Clear resets both."));

            // ── Mode A: import an existing manifest ─────────────
            var importStack = new StackPanel();
            _portraitImportGroup = new GroupBox
            {
                Header = "Import existing manifest",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0B0B0")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#353560")),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 4, 0, 10),
                MaxWidth = 620,
                HorizontalAlignment = HorizontalAlignment.Left,
                Content = importStack,
            };
            stack.Children.Add(_portraitImportGroup);

            importStack.Children.Add(CreateFieldLabel(
                "Full control: skins, emotions with multiple portraits each, fx rules, and per-line " +
                "emotion bindings. Portrait folders are resolved relative to the manifest's folder " +
                "and copied into the mod alongside it."));

            var importBtn = new Button
            {
                Content = "Import avatar_manifest.json…",
                Style = (Style)FindResource("SecondaryButton"),
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 6),
            };
            importBtn.Click += (_, _) => PortraitImport_Click();
            importStack.Children.Add(importBtn);

            _portraitImportSummary = new TextBlock
            {
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#909090")),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
                Visibility = Visibility.Collapsed,
            };
            importStack.Children.Add(_portraitImportSummary);

            _portraitValidationPanel = new StackPanel();
            importStack.Children.Add(new ScrollViewer
            {
                Content = _portraitValidationPanel,
                MaxHeight = 150,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });

            // ── Mode B: simple author from PNG folders ──────────
            var authorStack = new StackPanel();
            _portraitAuthorGroup = new GroupBox
            {
                Header = "Simple author",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0B0B0")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#353560")),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 4, 0, 10),
                MaxWidth = 620,
                HorizontalAlignment = HorizontalAlignment.Left,
                Content = authorStack,
            };
            stack.Children.Add(_portraitAuthorGroup);

            authorStack.Children.Add(CreateFieldLabel(
                "Pick a folder of PNGs per outfit skin. The folder name becomes the skin id and each " +
                "PNG's filename becomes an emotion (neutral.png → emotion \"neutral\"). Emotions " +
                "missing from a skin fall back to the first skin at runtime."));

            var addSkinBtn = new Button
            {
                Content = "Add skin folder…",
                Style = (Style)FindResource("SecondaryButton"),
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 6),
            };
            addSkinBtn.Click += (_, _) => PortraitAddSkin_Click();
            authorStack.Children.Add(addSkinBtn);

            _portraitSkinsPanel = new StackPanel();
            authorStack.Children.Add(_portraitSkinsPanel);

            // Idle / default emotion pickers (manifest "idleEmotion" / "defaultEmotion")
            var comboRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };

            var idleStack = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            idleStack.Children.Add(CreateFieldLabel("Idle Emotion"));
            _portraitIdleCombo = new ComboBox { Width = 180 };
            idleStack.Children.Add(_portraitIdleCombo);
            comboRow.Children.Add(idleStack);

            var defaultStack = new StackPanel();
            defaultStack.Children.Add(CreateFieldLabel("Default Emotion"));
            _portraitDefaultCombo = new ComboBox { Width = 180 };
            defaultStack.Children.Add(_portraitDefaultCombo);
            comboRow.Children.Add(defaultStack);

            authorStack.Children.Add(comboRow);

            // ── Clear (resets both modes) ────────────────────────
            var clearBtn = new Button
            {
                Content = "Clear portraits",
                Style = (Style)FindResource("SecondaryButton"),
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            clearBtn.Click += (_, _) => ClearPortraitsSection();
            stack.Children.Add(clearBtn);

            UpdatePortraitModeUi();
        }

        /// <summary>Import active disables simple-author and vice versa; empty state enables both.</summary>
        private void UpdatePortraitModeUi()
        {
            var importActive = _portraitImportedManifest != null;
            var authorActive = _portraitSkins.Count > 0;
            if (_portraitImportGroup != null) _portraitImportGroup.IsEnabled = !authorActive;
            if (_portraitAuthorGroup != null) _portraitAuthorGroup.IsEnabled = !importActive;
        }

        // ─── Mode A: Import ──────────────────────────────────────
        private void PortraitImport_Click()
        {
            var ofd = new OpenFileDialog
            {
                Title = "Import avatar manifest",
                Filter = "Avatar manifest (avatar_manifest.json)|avatar_manifest.json|JSON files (*.json)|*.json",
                FileName = AvatarPortraitLoader.ManifestFileName,
            };
            if (ofd.ShowDialog() != true) return;

            try
            {
                var json = File.ReadAllText(ofd.FileName);
                var manifest = JsonConvert.DeserializeObject<AvatarPortraitManifest>(json);
                if (manifest == null || manifest.Skins.Count == 0 || manifest.Emotions.Count == 0)
                {
                    MessageBox.Show(this,
                        "That manifest has no skins or no emotions — nothing to import.",
                        "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _portraitImportedManifest = manifest;
                _portraitImportedBaseDir = Path.GetDirectoryName(ofd.FileName) ?? "";
                ShowPortraitValidation(manifest, _portraitImportedBaseDir);
                UpdatePortraitModeUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not parse that manifest: " + ex.Message,
                    "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Validate skin dirs + portrait files with the same path math the runtime uses
        /// (baseDir + skins[].dir + portraits[].file) and render green/orange results.
        /// </summary>
        private void ShowPortraitValidation(AvatarPortraitManifest manifest, string baseDir)
        {
            if (_portraitImportSummary == null || _portraitValidationPanel == null) return;
            _portraitValidationPanel.Children.Clear();

            var found = 0;
            var warnings = new List<string>();

            foreach (var skin in manifest.Skins)
            {
                var skinDir = Path.Combine(baseDir, skin.Dir.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(skinDir))
                {
                    warnings.Add($"Skin \"{skin.Id}\": folder missing ({skin.Dir})");
                    continue;
                }
                foreach (var (emotionName, emotion) in manifest.Emotions)
                {
                    foreach (var portrait in emotion.Portraits)
                    {
                        if (string.IsNullOrEmpty(portrait.File)) continue;
                        if (File.Exists(Path.Combine(skinDir, portrait.File))) found++;
                        else warnings.Add($"{skin.Id}/{emotionName}: {portrait.File} not found");
                    }
                }
            }

            var name = string.IsNullOrEmpty(manifest.Character) ? "(unnamed)" : manifest.Character;
            _portraitImportSummary.Text =
                $"Imported \"{name}\" — {manifest.Skins.Count} skins, {manifest.Emotions.Count} emotions, " +
                $"{manifest.Lines.Count} line bindings. Folder: {baseDir}";
            _portraitImportSummary.Visibility = Visibility.Visible;

            _portraitValidationPanel.Children.Add(new TextBlock
            {
                Text = $"✓ {found} portrait file(s) found",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7ED87E")),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 2),
            });

            // Missing files are warnings, not errors — the runtime skips them and falls back
            // (same emotion in skin 0, then the idle emotion).
            foreach (var warning in warnings)
            {
                _portraitValidationPanel.Children.Add(new TextBlock
                {
                    Text = "⚠ " + warning,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB347")),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 1),
                });
            }
        }

        // ─── Mode B: Simple author ───────────────────────────────
        private void PortraitAddSkin_Click()
        {
            var ofd = new OpenFolderDialog { Title = "Pick a folder of portrait PNGs (one skin)" };
            if (ofd.ShowDialog() != true) return;

            var sourceDir = ofd.FolderName;
            var dirName = Path.GetFileName(sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(dirName))
            {
                MessageBox.Show(this, "Pick a normal folder, not a drive root.",
                    "Add skin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_portraitSkins.Any(s => string.Equals(s.DirName, dirName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, $"A skin folder named \"{dirName}\" is already added.",
                    "Add skin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var pngs = Directory.GetFiles(sourceDir, "*.png").OrderBy(f => f).ToList();
            if (pngs.Count == 0)
            {
                MessageBox.Show(this, "That folder contains no PNG files.",
                    "Add skin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var entry = new PortraitSkinEntry
            {
                SourceDir = sourceDir,
                DirName = dirName,
                PngFiles = pngs.Select(Path.GetFileName).Where(f => f != null).Select(f => f!).ToList(),
            };
            AddPortraitSkinRow(entry);
            _portraitSkins.Add(entry);
            RefreshPortraitEmotionCombos();
            UpdatePortraitModeUi();
        }

        private void AddPortraitSkinRow(PortraitSkinEntry entry)
        {
            if (_portraitSkinsPanel == null) return;

            var rowStack = new StackPanel();
            var row = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20203A")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#353560")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 6),
                Child = rowStack,
            };
            entry.Row = row;

            // Header: skin id (folder name), editable label, remove button
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var idText = new TextBlock
            {
                Text = entry.DirName,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF69B4")),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            Grid.SetColumn(idText, 0);
            header.Children.Add(idText);

            entry.TitleBox = new TextBox
            {
                Style = (Style)FindResource("DarkTextBox"),
                Text = entry.DirName,
                FontSize = 12,
                Padding = new Thickness(6, 3, 6, 3),
                MaxWidth = 220,
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = "Display label for this skin (manifest \"title\")",
            };
            Grid.SetColumn(entry.TitleBox, 1);
            header.Children.Add(entry.TitleBox);

            var removeBtn = new Button
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
            removeBtn.Click += (_, _) =>
            {
                _portraitSkins.Remove(entry);
                _portraitSkinsPanel?.Children.Remove(row);
                RefreshPortraitEmotionCombos();
                UpdatePortraitModeUi();
            };
            Grid.SetColumn(removeBtn, 2);
            header.Children.Add(removeBtn);
            rowStack.Children.Add(header);

            // Previews: one small thumbnail + emotion name per PNG
            var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var fileName in entry.PngFiles)
            {
                var cell = new StackPanel { Width = 72, Margin = new Thickness(2) };
                var img = new Image { Height = 64, Stretch = Stretch.Uniform };
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(Path.Combine(entry.SourceDir, fileName), UriKind.Absolute);
                    bmp.DecodePixelWidth = 64;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    img.Source = bmp;
                }
                catch { /* undecodable file — show caption only */ }
                cell.Children.Add(img);
                cell.Children.Add(new TextBlock
                {
                    Text = PortraitEmotionFromFile(fileName),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#909090")),
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center,
                    ToolTip = fileName,
                });
                wrap.Children.Add(cell);
            }
            rowStack.Children.Add(wrap);
        }

        /// <summary>Repopulate the Idle/Default combos from the union of authored emotion names.</summary>
        private void RefreshPortraitEmotionCombos()
        {
            if (_portraitIdleCombo == null || _portraitDefaultCombo == null) return;

            var emotions = _portraitSkins
                .SelectMany(s => s.PngFiles)
                .Select(PortraitEmotionFromFile)
                .Distinct()
                .OrderBy(e => e)
                .ToList();

            foreach (var combo in new[] { _portraitIdleCombo, _portraitDefaultCombo })
            {
                var previous = combo.SelectedItem as string;
                combo.ItemsSource = emotions;
                if (previous != null && emotions.Contains(previous))
                    combo.SelectedItem = previous;
                else if (emotions.Contains("neutral"))
                    combo.SelectedItem = "neutral";
                else if (emotions.Count > 0)
                    combo.SelectedIndex = 0;
            }
        }

        /// <summary>Build the manifest for simple-author mode (director defaults match the built-in mod).</summary>
        private AvatarPortraitManifest BuildAuthoredPortraitManifest()
        {
            var manifest = new AvatarPortraitManifest
            {
                Character = string.IsNullOrEmpty(GetTextBoxValue(_txtModName)) ? "custom" : GetTextBoxValue(_txtModName),
                Version = "1",
                Director = new AvatarDirector { CrossfadeFrames = 4, RotatePerEmotion = true, IdleReturnMs = 4000 },
            };

            foreach (var entry in _portraitSkins)
            {
                var title = entry.TitleBox != null ? entry.TitleBox.Text.Trim() : "";
                manifest.Skins.Add(new AvatarSkin
                {
                    Id = entry.DirName,
                    Dir = "portraits/" + entry.DirName,     // forward slashes, like the built-in manifest
                    Title = string.IsNullOrEmpty(title) ? entry.DirName : title,
                });

                // Union of emotions across skins; each PNG stem = one emotion with one portrait.
                // The file name is shared across skins (LoadBucket joins skinDir + file); skins
                // missing a file are skipped gracefully at runtime.
                foreach (var fileName in entry.PngFiles)
                {
                    var emotion = PortraitEmotionFromFile(fileName);
                    if (manifest.Emotions.ContainsKey(emotion)) continue;
                    manifest.Emotions[emotion] = new AvatarEmotion
                    {
                        Portraits = { new AvatarPortrait { File = fileName } },
                    };
                }
            }

            var fallback = manifest.Emotions.ContainsKey("neutral")
                ? "neutral"
                : manifest.Emotions.Keys.FirstOrDefault() ?? "neutral";
            manifest.IdleEmotion = _portraitIdleCombo?.SelectedItem as string ?? fallback;
            manifest.DefaultEmotion = _portraitDefaultCombo?.SelectedItem as string ?? fallback;
            return manifest;
        }

        // ─── Export / Load / Clear ───────────────────────────────
        private void WritePortraitsTo(string resourcesDir)
        {
            var hasImport = _portraitImportedManifest != null && !string.IsNullOrEmpty(_portraitImportedBaseDir);
            var hasAuthored = _portraitSkins.Count > 0;
            if (!hasImport && !hasAuthored) return;   // no-op when the panel is empty

            var companionDir = Path.Combine(resourcesDir, "sounds", "companion_audio");
            Directory.CreateDirectory(companionDir);
            var manifestPath = Path.Combine(companionDir, AvatarPortraitLoader.ManifestFileName);

            if (hasImport)
            {
                // Re-serialize the imported manifest and copy every referenced skin dir tree,
                // preserving the manifest-relative layout LoadBucket resolves against.
                var manifest = _portraitImportedManifest!;
                File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));
                foreach (var skin in manifest.Skins)
                {
                    if (string.IsNullOrEmpty(skin.Dir)) continue;
                    var rel = skin.Dir.Replace('/', Path.DirectorySeparatorChar);
                    var src = Path.Combine(_portraitImportedBaseDir!, rel);
                    if (Directory.Exists(src))
                        CopyPortraitDirectory(src, Path.Combine(companionDir, rel));
                }
            }
            else
            {
                var manifest = BuildAuthoredPortraitManifest();
                File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));
                foreach (var entry in _portraitSkins)
                {
                    if (Directory.Exists(entry.SourceDir))
                        CopyPortraitDirectory(entry.SourceDir, Path.Combine(companionDir, "portraits", entry.DirName));
                }
            }
        }

        private void LoadPortraitsFrom(string resourcesDir)
        {
            var companionDir = Path.Combine(resourcesDir, "sounds", "companion_audio");
            var manifestPath = Path.Combine(companionDir, AvatarPortraitLoader.ManifestFileName);
            if (!File.Exists(manifestPath)) return;

            try
            {
                var manifest = JsonConvert.DeserializeObject<AvatarPortraitManifest>(File.ReadAllText(manifestPath));
                if (manifest == null || manifest.Skins.Count == 0 || manifest.Emotions.Count == 0) return;

                // A loaded mod's manifest round-trips through import mode: it keeps every field
                // (lines, fx rules, multi-portrait emotions) the simple-author UI can't represent.
                ClearPortraitsSection();
                _portraitImportedManifest = manifest;
                _portraitImportedBaseDir = companionDir;
                ShowPortraitValidation(manifest, companionDir);
                UpdatePortraitModeUi();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ModCreator: failed to load portrait manifest from {Path}", manifestPath);
            }
        }

        private void ClearPortraitsSection()
        {
            _portraitImportedManifest = null;
            _portraitImportedBaseDir = null;
            _portraitSkins.Clear();

            if (_portraitImportSummary != null)
            {
                _portraitImportSummary.Text = "";
                _portraitImportSummary.Visibility = Visibility.Collapsed;
            }
            _portraitValidationPanel?.Children.Clear();
            _portraitSkinsPanel?.Children.Clear();
            if (_portraitIdleCombo != null) _portraitIdleCombo.ItemsSource = null;
            if (_portraitDefaultCombo != null) _portraitDefaultCombo.ItemsSource = null;

            UpdatePortraitModeUi();
        }

        private static void CopyPortraitDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
            foreach (var sub in Directory.GetDirectories(sourceDir))
                CopyPortraitDirectory(sub, Path.Combine(destDir, Path.GetFileName(sub)));
        }
    }
}
