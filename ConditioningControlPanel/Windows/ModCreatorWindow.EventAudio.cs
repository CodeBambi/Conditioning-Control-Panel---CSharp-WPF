using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Mod editor panel for EVENT AUDIO: mp3 clips the companion plays when it speaks an
    /// event comment whose text slug-matches the file name. Runtime contract lives in
    /// <see cref="Services.CompanionPhraseService"/> (EventAudioFolder / ResolveEventAudio):
    /// the clip at resources/sounds/event_audio/{Slugify(text)}.mp3 plays whenever the
    /// companion says a line whose Slugify(text) matches the file stem — so the text
    /// entered here must EXACTLY match a phrase the companion actually says.
    /// </summary>
    public partial class ModCreatorWindow
    {
        // ─── Event Audio state ───────────────────────────────────
        private sealed class EventAudioRow
        {
            public StackPanel Container = null!;   // whole row (text row + optional hint line)
            public TextBox TextBox = null!;        // phrase text (read-only for rows loaded from a pack)
            public TextBlock SlugLabel = null!;    // live "{slug}.mp3" preview
            public TextBlock FileLabel = null!;    // attached file name
            public Button PlayButton = null!;
            public string? FilePath;               // absolute path to the source mp3
            public bool IsFixedText;               // loaded from an existing pack: stem IS the slug
        }

        private readonly List<EventAudioRow> _eventAudioRows = new();
        private StackPanel? _eventAudioRowsPanel;
        private TextBlock? _eventAudioStatusLabel;

        // ─── Build ───────────────────────────────────────────────
        private void BuildEventAudioSection()
        {
            var panel = CreateSectionPanel("eventaudio");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Event Audio"));
            stack.Children.Add(CreateSectionDescription(
                "Attach spoken audio to exact companion phrases. The mp3's filename is derived " +
                "from the phrase text via a slug (lowercase, punctuation stripped), and at runtime " +
                "the clip plays whenever the companion speaks a line whose slug matches. The text " +
                "must EXACTLY match a phrase the companion actually says - usually one of the " +
                "entries in your Phrases section."));

            stack.Children.Add(CreateSubHeader("Lines"));

            _eventAudioRowsPanel = new StackPanel();
            var rowsScroll = new ScrollViewer
            {
                MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _eventAudioRowsPanel,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(rowsScroll);

            _eventAudioStatusLabel = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Color.FromRgb(144, 144, 144)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };
            stack.Children.Add(_eventAudioStatusLabel);

            var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal };

            var addBtn = CreateEventAudioActionButton("+ Add line");
            addBtn.Click += (_, _) =>
            {
                AddEventAudioRow("", null, isFixedText: false);
                UpdateStatusBar();
            };
            buttonsRow.Children.Add(addBtn);

            var importBtn = CreateEventAudioActionButton("Import folder…");
            importBtn.Margin = new Thickness(8, 0, 0, 0);
            importBtn.ToolTip = "Add one line per *.mp3 in a folder, using each filename as the phrase text.";
            importBtn.Click += (_, _) => ImportEventAudioFolder();
            buttonsRow.Children.Add(importBtn);

            stack.Children.Add(buttonsRow);
        }

        private Button CreateEventAudioActionButton(string content)
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

        // ─── Rows ────────────────────────────────────────────────
        private EventAudioRow AddEventAudioRow(string initialText, string? filePath, bool isFixedText, bool flagNonSlugName = false)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            container.Children.Add(row);

            var state = new EventAudioRow
            {
                Container = container,
                FilePath = filePath,
                IsFixedText = isFixedText
            };

            // Phrase text
            var textBox = CreateDarkTextBox();
            textBox.Width = 240;
            textBox.Margin = new Thickness(0, 0, 8, 0);
            textBox.VerticalAlignment = VerticalAlignment.Center;
            textBox.Text = initialText;
            if (isFixedText)
            {
                textBox.IsReadOnly = true;
                textBox.Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 192));
                textBox.ToolTip = "Loaded from the pack: the slug filename is shown as the text. Slugs can't be reversed to the original phrase.";
            }
            state.TextBox = textBox;
            row.Children.Add(textBox);

            // Live slug preview
            var slugLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                MaxWidth = 220,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            state.SlugLabel = slugLabel;
            row.Children.Add(slugLabel);

            void RefreshSlug()
            {
                var slug = Services.CompanionPhraseService.Slugify(textBox.Text);
                slugLabel.Text = slug.Length > 0 ? slug + ".mp3" : "(type a phrase)";
                slugLabel.ToolTip = slugLabel.Text;
            }
            textBox.TextChanged += (_, _) => RefreshSlug();
            RefreshSlug();

            // Attach mp3
            var attachBtn = new Button
            {
                Content = "Attach mp3…",
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 80)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 10,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(attachBtn);

            // Attached file name
            var fileLabel = new TextBlock
            {
                Text = "No file",
                Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 128)),
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                MaxWidth = 180,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            state.FileLabel = fileLabel;
            row.Children.Add(fileLabel);

            // Preview play
            var playBtn = new Button
            {
                Content = "▶",
                Width = 24, Height = 24,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 100)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                ToolTip = "Play preview"
            };
            playBtn.Click += (_, _) =>
            {
                if (state.FilePath != null)
                    ToggleAudioPreview(state.FilePath, playBtn);
            };
            state.PlayButton = playBtn;
            row.Children.Add(playBtn);

            void ShowFile(string path)
            {
                state.FilePath = path;
                fileLabel.Text = Path.GetFileName(path);
                fileLabel.FontStyle = FontStyles.Normal;
                fileLabel.Foreground = Brushes.White;
                fileLabel.ToolTip = path;
                playBtn.Visibility = Visibility.Visible;
            }
            if (filePath != null) ShowFile(filePath);

            attachBtn.Click += (_, _) =>
            {
                var ofd = new OpenFileDialog
                {
                    Title = "Select event audio (mp3)",
                    Filter = "MP3 Files|*.mp3"
                };
                if (ofd.ShowDialog() == true)
                {
                    ShowFile(ofd.FileName);
                    UpdateStatusBar();
                }
            };

            // Remove
            var removeBtn = new Button
            {
                Content = "✕",
                Width = 20, Height = 20,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 9,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            removeBtn.Click += (_, _) =>
            {
                _eventAudioRowsPanel?.Children.Remove(container);
                _eventAudioRows.Remove(state);
                UpdateStatusBar();
            };
            row.Children.Add(removeBtn);

            // Orange hint for imported files whose names are not slugs of themselves —
            // they were probably not generated by this pipeline, so the text likely
            // doesn't match a real phrase yet.
            if (flagNonSlugName)
            {
                container.Children.Add(new TextBlock
                {
                    Text = "⚠ Filename isn't a clean slug - probably not generated for event audio. Edit the text to the exact phrase the companion says.",
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 80)),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 2, 0, 0),
                    MaxWidth = 560,
                    HorizontalAlignment = HorizontalAlignment.Left
                });
            }

            _eventAudioRows.Add(state);
            _eventAudioRowsPanel?.Children.Add(container);
            return state;
        }

        private void ImportEventAudioFolder()
        {
            var dlg = new OpenFolderDialog { Title = "Select a folder of event-audio mp3 files" };
            if (dlg.ShowDialog() != true) return;

            string[] files;
            try
            {
                files = Directory.GetFiles(dlg.FolderName, "*.mp3");
            }
            catch
            {
                SetEventAudioStatus("Could not read that folder.");
                return;
            }

            int added = 0, flagged = 0;
            foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                // Skip files already attached to a row
                if (_eventAudioRows.Any(r => string.Equals(r.FilePath, file, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var stem = Path.GetFileNameWithoutExtension(file);
                var isCleanSlug = Services.CompanionPhraseService.Slugify(stem) == stem;
                AddEventAudioRow(stem, file, isFixedText: false, flagNonSlugName: !isCleanSlug);
                added++;
                if (!isCleanSlug) flagged++;
            }

            SetEventAudioStatus(files.Length == 0
                ? "No *.mp3 files found in that folder."
                : $"Imported {added} file(s)" + (flagged > 0 ? $", {flagged} flagged with non-slug names." : "."));
            UpdateStatusBar();
        }

        private void SetEventAudioStatus(string text)
        {
            if (_eventAudioStatusLabel == null) return;
            _eventAudioStatusLabel.Text = text;
            _eventAudioStatusLabel.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        // ─── Export / Load / Clear ───────────────────────────────
        private void WriteEventAudioTo(string resourcesDir)
        {
            // Rows count only when they have both a usable phrase and an attached file.
            var complete = _eventAudioRows
                .Where(r => r.FilePath != null && File.Exists(r.FilePath))
                .Select(r => (Slug: Services.CompanionPhraseService.Slugify(r.TextBox.Text), Path: r.FilePath!))
                .Where(r => r.Slug.Length > 0)
                .ToList();

            if (complete.Count == 0) return; // no-op when empty: mods without the folder stay text-only

            var destDir = Path.Combine(resourcesDir, "sounds", "event_audio");
            Directory.CreateDirectory(destDir);

            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var skipped = new List<string>();
            foreach (var (slug, sourcePath) in complete)
            {
                if (!written.Add(slug))
                {
                    // Slug collision: two rows resolve to the same filename. Keep the first, skip the rest.
                    skipped.Add(slug);
                    continue;
                }
                File.Copy(sourcePath, Path.Combine(destDir, slug + ".mp3"), overwrite: true);
            }

            SetEventAudioStatus(skipped.Count > 0
                ? $"Exported {written.Count} event audio clip(s). Skipped {skipped.Count} duplicate slug(s): {string.Join(", ", skipped.Distinct())}."
                : "");
        }

        private void LoadEventAudioFrom(string resourcesDir)
        {
            ClearEventAudioSection();

            var dir = Path.Combine(resourcesDir, "sounds", "event_audio");
            if (!Directory.Exists(dir)) return;

            foreach (var file in Directory.GetFiles(dir, "*.mp3").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                // The stem IS the slug; a slug can't be reversed to the original phrase,
                // so show it as fixed (read-only) text. Slugify(slug) == slug, so a
                // re-export round-trips to the same filename.
                var stem = Path.GetFileNameWithoutExtension(file);
                AddEventAudioRow(stem, file, isFixedText: true);
            }
            UpdateStatusBar();
        }

        private void ClearEventAudioSection()
        {
            _eventAudioRows.Clear();
            _eventAudioRowsPanel?.Children.Clear();
            SetEventAudioStatus("");
        }
    }
}
