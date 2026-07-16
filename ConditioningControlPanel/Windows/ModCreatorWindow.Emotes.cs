using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel
{
    /// <summary>
    /// "Animated Emotes" panel for the mod editor: import PREPARED emote set folders
    /// (transparent GIF clips + emotes.json map + optional clip_timing.json) that replace
    /// the static avatar of an avatar set with animated clips.
    ///
    /// The sprites themselves are produced by an external pipeline (tools/avatar-emotes/),
    /// NOT by this editor -- this panel only validates and packages an already-prepared
    /// folder. Export copies each valid set to {resources}/emotes/set{N}/, which is exactly
    /// the layout <c>AvatarTubeWindow.ModLocalEmoteFolder</c> loads at runtime
    /// ({InstalledPath}/resources/emotes/set{N}/emotes.json).
    ///
    /// Validation mirrors <c>AvatarTubeWindow.LoadCirceMap</c>: the "referenced clips" union
    /// is idleRotation clips + talking pool (or legacy short/medium/long) + stemPrefix values
    /// + mood values + clickEmotes + expressive pool.
    /// </summary>
    public partial class ModCreatorWindow
    {
        // ─── Animated Emotes state ───────────────────────────────
        private const int EmoteSetMin = 1;
        private const int EmoteSetMax = 7;
        private const long EmoteFolderWarnBytes = 50L * 1024 * 1024; // 50 MB soft cap

        private StackPanel? _emoteRowsPanel;
        private readonly List<EmoteSetRow> _emoteRows = new();
        private bool _emoteSuppressSetChanged; // reentrancy guard while reverting a dedup-rejected pick

        /// <summary>Per-row state for one imported emote set.</summary>
        private sealed class EmoteSetRow
        {
            public Border Root = null!;
            public ComboBox SetCombo = null!;
            public TextBlock FolderLabel = null!;
            public TextBlock StatsLabel = null!;
            public TextBlock IssuesLabel = null!;
            public string? FolderPath;      // source folder on disk (import pick or loaded set dir)
            public bool IsValid;            // errors == 0; invalid rows are excluded from Write
            public int PrevSetNum;          // last accepted set number, for dedup revert
        }

        // ─── Build ───────────────────────────────────────────────
        private void BuildEmotesSection()
        {
            var panel = CreateSectionPanel("emotes");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Animated Emotes"));
            stack.Children.Add(CreateSectionDescription(
                "Import a PREPARED emote set folder: transparent GIF clips plus an emotes.json map, " +
                "and an optional clip_timing.json for talk timing. Sprites are produced by an external " +
                "pipeline, not this editor -- the folder layout is documented in MODDING.md. Each set " +
                "replaces the static avatar of one avatar set number (1-7) with animated clips: a " +
                "weighted idle rotation, talking clips synced to voicelines, and reaction emotes."));

            _emoteRowsPanel = new StackPanel
            {
                MaxWidth = 640,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            stack.Children.Add(_emoteRowsPanel);

            var addBtn = new Button
            {
                Content = "+ Add Emote Set",
                Style = (Style)FindResource("SecondaryButton"),
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 0),
            };
            addBtn.Click += (_, _) =>
            {
                var free = NextFreeEmoteSetNumber();
                if (free == null)
                {
                    MessageBox.Show(
                        $"All avatar set numbers ({EmoteSetMin}-{EmoteSetMax}) already have an emote set row.",
                        "Animated Emotes", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                AddEmoteRow(free.Value, null);
            };
            stack.Children.Add(addBtn);
        }

        private int? NextFreeEmoteSetNumber()
        {
            var used = _emoteRows.Select(EmoteRowSetNumber).ToHashSet();
            for (int n = EmoteSetMin; n <= EmoteSetMax; n++)
                if (!used.Contains(n)) return n;
            return null;
        }

        private static int EmoteRowSetNumber(EmoteSetRow row) => row.SetCombo.SelectedIndex + EmoteSetMin;

        // ─── Rows ────────────────────────────────────────────────
        private void AddEmoteRow(int setNum, string? folder)
        {
            if (_emoteRowsPanel == null) return;

            var row = new EmoteSetRow { PrevSetNum = setNum };

            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252542")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
            };
            row.Root = border;

            var inner = new StackPanel();
            border.Child = inner;

            // ── Top line: [Set N combo] [Import folder…] [folder name] [✕] ──
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var combo = new ComboBox
            {
                Width = 70,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Which avatar set number this emote set animates (matches the Avatars section sets).",
            };
            for (int n = EmoteSetMin; n <= EmoteSetMax; n++)
                combo.Items.Add($"Set {n}");
            combo.SelectedIndex = setNum - EmoteSetMin;
            row.SetCombo = combo;
            combo.SelectionChanged += (_, _) => OnEmoteSetNumberChanged(row);
            Grid.SetColumn(combo, 0);
            top.Children.Add(combo);

            var importBtn = new Button
            {
                Content = "Import folder…",
                Style = (Style)FindResource("SecondaryButton"),
                Padding = new Thickness(10, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            importBtn.Click += (_, _) => ImportEmoteFolder(row);
            Grid.SetColumn(importBtn, 1);
            top.Children.Add(importBtn);

            row.FolderLabel = new TextBlock
            {
                Text = "(no folder imported)",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#909090")),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(row.FolderLabel, 2);
            top.Children.Add(row.FolderLabel);

            var removeBtn = new Button
            {
                Content = "✕",
                Width = 24,
                Height = 24,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 10,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Remove this emote set",
            };
            removeBtn.Click += (_, _) =>
            {
                _emoteRowsPanel?.Children.Remove(row.Root);
                _emoteRows.Remove(row);
            };
            Grid.SetColumn(removeBtn, 3);
            top.Children.Add(removeBtn);

            inner.Children.Add(top);

            // ── Stats line (GIF count + size) ──
            row.StatsLabel = new TextBlock
            {
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#606080")),
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 0),
                Visibility = Visibility.Collapsed,
            };
            inner.Children.Add(row.StatsLabel);

            // ── Validation summary (red errors / orange warnings) ──
            row.IssuesLabel = new TextBlock
            {
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                Visibility = Visibility.Collapsed,
            };
            inner.Children.Add(row.IssuesLabel);

            _emoteRows.Add(row);
            _emoteRowsPanel.Children.Add(border);

            if (!string.IsNullOrEmpty(folder))
            {
                row.FolderPath = folder;
                ValidateEmoteRow(row);
            }
        }

        /// <summary>Dedup guard: a set number already used by another row is rejected and reverted.</summary>
        private void OnEmoteSetNumberChanged(EmoteSetRow row)
        {
            if (_emoteSuppressSetChanged) return;

            int newSet = EmoteRowSetNumber(row);
            if (_emoteRows.Any(r => r != row && EmoteRowSetNumber(r) == newSet))
            {
                MessageBox.Show(
                    $"Set {newSet} is already used by another emote set row. Pick a different set number.",
                    "Animated Emotes", MessageBoxButton.OK, MessageBoxImage.Warning);
                _emoteSuppressSetChanged = true;
                row.SetCombo.SelectedIndex = row.PrevSetNum - EmoteSetMin;
                _emoteSuppressSetChanged = false;
                return;
            }
            row.PrevSetNum = newSet;
        }

        private void ImportEmoteFolder(EmoteSetRow row)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Select a prepared emote set folder (must contain emotes.json)",
            };
            if (dlg.ShowDialog(this) != true) return;

            row.FolderPath = dlg.FolderName;
            ValidateEmoteRow(row);
        }

        // ─── Validation ──────────────────────────────────────────
        /// <summary>
        /// Validates a row's source folder and refreshes its labels. Mirrors the runtime
        /// loader (AvatarTubeWindow.LoadCirceMap): every clip name the map references must
        /// ship as a .gif in the folder. Errors mark the row invalid (excluded from export);
        /// warnings are informational only.
        /// </summary>
        private void ValidateEmoteRow(EmoteSetRow row)
        {
            var errors = new List<string>();
            var warns = new List<string>();
            int gifCount = 0;
            long totalBytes = 0;

            var folder = row.FolderPath;
            row.FolderLabel.Text = string.IsNullOrEmpty(folder)
                ? "(no folder imported)"
                : Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            row.FolderLabel.ToolTip = folder;

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                errors.Add("Folder not found on disk.");
            }
            else
            {
                try
                {
                    var gifNames = new HashSet<string>(
                        Directory.EnumerateFiles(folder, "*.gif", SearchOption.TopDirectoryOnly)
                            .Select(f => Path.GetFileNameWithoutExtension(f) ?? ""),
                        StringComparer.OrdinalIgnoreCase);
                    gifCount = gifNames.Count;
                    totalBytes = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                        .Sum(f => new FileInfo(f).Length);

                    var mapPath = Path.Combine(folder, "emotes.json");
                    if (!File.Exists(mapPath))
                    {
                        errors.Add("emotes.json is missing from the folder.");
                    }
                    else
                    {
                        JObject? map = null;
                        try { map = JObject.Parse(File.ReadAllText(mapPath)); }
                        catch (Exception ex) { errors.Add("emotes.json is not valid JSON: " + ex.Message); }

                        if (map != null)
                        {
                            var referenced = CollectReferencedEmoteClips(map, out bool hasTalkPool);

                            // (3) ERROR: every clip the map references must ship as a .gif.
                            var missing = referenced.Where(c => !gifNames.Contains(c))
                                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
                            if (missing.Count > 0)
                                errors.Add("Referenced clips missing from the folder: " +
                                           string.Join(", ", missing.Select(c => c + ".gif")));

                            // (4a) WARN: GIFs present but never referenced by the map. The runtime
                            // falls back to talkA/talkC when no talk pool is defined, so those two
                            // count as referenced for this check when the pool is absent.
                            var referencedForUnused = new HashSet<string>(referenced, StringComparer.OrdinalIgnoreCase);
                            if (!hasTalkPool) { referencedForUnused.Add("talkA"); referencedForUnused.Add("talkC"); }
                            var unreferenced = gifNames.Where(g => !referencedForUnused.Contains(g))
                                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
                            if (unreferenced.Count > 0)
                                warns.Add("GIFs not referenced by emotes.json (copied but never played): " +
                                          string.Join(", ", unreferenced));

                            // (4c) WARN: nothing to animate the mouth with while speaking.
                            if (!hasTalkPool && !gifNames.Contains("talkA"))
                                warns.Add("No talking pool defined and no talkA.gif - the avatar will not animate while speaking.");
                        }
                    }

                    // (4b) WARN: talk timing sidecar missing.
                    if (!File.Exists(Path.Combine(folder, "clip_timing.json")))
                        warns.Add("clip_timing.json is missing - fallback talk timing will be used.");

                    // (4d) WARN: oversized set.
                    if (totalBytes > EmoteFolderWarnBytes)
                        warns.Add($"Folder is large ({FormatEmoteSize(totalBytes)}) - consider trimming clips (soft limit 50 MB).");
                }
                catch (Exception ex)
                {
                    errors.Add("Could not read the folder: " + ex.Message);
                }
            }

            row.IsValid = errors.Count == 0 && !string.IsNullOrEmpty(folder);

            // Stats line
            row.StatsLabel.Text = $"{gifCount} GIF clip(s), {FormatEmoteSize(totalBytes)}";
            row.StatsLabel.Visibility = string.IsNullOrEmpty(folder) ? Visibility.Collapsed : Visibility.Visible;

            // Issues line: red errors first, then orange warnings.
            row.IssuesLabel.Inlines.Clear();
            var red = new SolidColorBrush(Color.FromRgb(255, 100, 100));
            var orange = new SolidColorBrush(Color.FromRgb(255, 165, 0));
            bool first = true;
            foreach (var e in errors)
            {
                if (!first) row.IssuesLabel.Inlines.Add(new LineBreak());
                row.IssuesLabel.Inlines.Add(new Run("✗ " + e) { Foreground = red });
                first = false;
            }
            foreach (var w in warns)
            {
                if (!first) row.IssuesLabel.Inlines.Add(new LineBreak());
                row.IssuesLabel.Inlines.Add(new Run("⚠ " + w) { Foreground = orange });
                first = false;
            }
            if (errors.Count > 0)
            {
                row.IssuesLabel.Inlines.Add(new LineBreak());
                row.IssuesLabel.Inlines.Add(new Run("This set is invalid and will NOT be exported until fixed.")
                {
                    Foreground = red,
                    FontWeight = FontWeights.SemiBold,
                });
            }
            row.IssuesLabel.Visibility = first ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// Every clip name emotes.json references, built the same way the runtime builds its
        /// known-clips union in LoadCirceMap: idleRotation clips ("idle" when the list is empty,
        /// matching the runtime fallback) + talking pool (or legacy short/medium/long, merged)
        /// + stemPrefix values + mood values + clickEmotes + expressive pool. "_"-prefixed
        /// property names (e.g. "_comment") are skipped.
        /// </summary>
        private static HashSet<string> CollectReferencedEmoteClips(JObject map, out bool hasTalkPool)
        {
            var clips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // idleRotation: [{ clip, weight }]; empty -> runtime rotates the single clip "idle".
            bool anyIdle = false;
            if (map["idleRotation"] is JArray idleArr)
            {
                foreach (var it in idleArr)
                {
                    var c = (string?)it["clip"];
                    if (!string.IsNullOrEmpty(c)) { clips.Add(c!); anyIdle = true; }
                }
            }
            if (!anyIdle) clips.Add("idle");

            // talking.pool (preferred) or legacy talking.short/medium/long merged.
            hasTalkPool = false;
            if (map["talking"] is JObject talk)
            {
                var pool = new List<string>();
                if (talk["pool"] != null)
                {
                    pool.AddRange(ReadEmoteClipList(talk["pool"]));
                }
                else
                {
                    pool.AddRange(ReadEmoteClipList(talk["short"]));
                    pool.AddRange(ReadEmoteClipList(talk["medium"]));
                    pool.AddRange(ReadEmoteClipList(talk["long"]));
                }
                if (pool.Count > 0)
                {
                    hasTalkPool = true;
                    foreach (var c in pool) clips.Add(c);
                }
            }

            // stemPrefix: { lineIdPrefix: clip }
            if (map["stemPrefix"] is JObject sp)
                foreach (var p in sp.Properties())
                {
                    if (p.Name.StartsWith("_", StringComparison.Ordinal)) continue;
                    var v = (string?)p.Value;
                    if (!string.IsNullOrEmpty(v)) clips.Add(v!);
                }

            // mood: { mood: clip }
            if (map["mood"] is JObject mm)
                foreach (var p in mm.Properties())
                {
                    if (p.Name.StartsWith("_", StringComparison.Ordinal)) continue;
                    var v = (string?)p.Value;
                    if (!string.IsNullOrEmpty(v)) clips.Add(v!);
                }

            // clickEmotes: [clip]
            if (map["clickEmotes"] is JArray ce)
                foreach (var it in ce)
                {
                    var v = (string?)it;
                    if (!string.IsNullOrEmpty(v)) clips.Add(v!);
                }

            // expressive.pool: [clip]
            if (map["expressive"] is JObject ex && ex["pool"] != null)
                foreach (var c in ReadEmoteClipList(ex["pool"]))
                    clips.Add(c);

            return clips;
        }

        /// <summary>Mirror of the runtime's ParseClipList: string array or a single string, no fallback.</summary>
        private static List<string> ReadEmoteClipList(JToken? t)
        {
            if (t == null) return new List<string>();
            if (t.Type == JTokenType.Array)
                return t.Select(x => (string?)x).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();
            var single = (string?)t;
            return string.IsNullOrEmpty(single) ? new List<string>() : new List<string> { single! };
        }

        private static string FormatEmoteSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):0.##} GB";
            if (bytes >= 1024L * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
            if (bytes >= 1024L) return $"{bytes / 1024.0:0.#} KB";
            return $"{bytes} B";
        }

        // ─── Write / Load / Clear (side-file seams) ──────────────
        /// <summary>
        /// Copies each VALID imported set folder to {resourcesDir}/emotes/set{N}/ -- directly
        /// under the resources root (NOT under sounds/), matching the runtime's mod-local
        /// lookup {InstalledPath}/resources/emotes/set{N}/emotes.json. Only *.gif, emotes.json
        /// and clip_timing.json are copied; other junk in the source folder is skipped.
        /// No-op when there is nothing valid to write.
        /// </summary>
        private void WriteEmotesTo(string resourcesDir)
        {
            var valid = _emoteRows.Where(r => r.IsValid && !string.IsNullOrEmpty(r.FolderPath)).ToList();
            if (valid.Count == 0) return;

            foreach (var row in valid)
            {
                var dest = Path.Combine(resourcesDir, "emotes", $"set{EmoteRowSetNumber(row)}");
                try
                {
                    if (Directory.Exists(dest)) Directory.Delete(dest, true);
                    CopyEmoteSetFolder(row.FolderPath!, dest);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Mod editor: failed to copy emote set {Folder} -> {Dest}",
                        row.FolderPath, dest);
                }
            }
        }

        /// <summary>Recursive filtered copy: keeps *.gif, emotes.json, clip_timing.json; prunes empty dirs.</summary>
        private static void CopyEmoteSetFolder(string src, string dest)
        {
            Directory.CreateDirectory(dest);

            foreach (var file in Directory.EnumerateFiles(src))
            {
                var name = Path.GetFileName(file);
                bool keep = name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                            || name.Equals("emotes.json", StringComparison.OrdinalIgnoreCase)
                            || name.Equals("clip_timing.json", StringComparison.OrdinalIgnoreCase);
                if (keep)
                    File.Copy(file, Path.Combine(dest, name), overwrite: true);
            }

            foreach (var dir in Directory.EnumerateDirectories(src))
                CopyEmoteSetFolder(dir, Path.Combine(dest, Path.GetFileName(dir)));

            // A subdirectory holding only junk copies nothing -- drop the empty husk.
            if (!Directory.EnumerateFileSystemEntries(dest).Any())
                Directory.Delete(dest);
        }

        /// <summary>
        /// Repopulates the panel from an extracted resources tree: every {resourcesDir}/emotes/set*
        /// directory that contains an emotes.json becomes a row pointing at that folder.
        /// </summary>
        private void LoadEmotesFrom(string resourcesDir)
        {
            ClearEmotesSection();

            var root = Path.Combine(resourcesDir, "emotes");
            if (!Directory.Exists(root)) return;

            var sets = new List<(int Num, string Dir)>();
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root, "set*"))
                {
                    var name = Path.GetFileName(dir);
                    if (!int.TryParse(name.AsSpan(3), out var n)) continue;
                    if (n < EmoteSetMin || n > EmoteSetMax) continue;
                    if (!File.Exists(Path.Combine(dir, "emotes.json"))) continue;
                    sets.Add((n, dir));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Mod editor: emote set scan failed under {Root}", root);
                return;
            }

            foreach (var (num, dir) in sets.OrderBy(s => s.Num))
                AddEmoteRow(num, dir);
        }

        private void ClearEmotesSection()
        {
            _emoteRows.Clear();
            _emoteRowsPanel?.Children.Clear();
        }
    }
}
