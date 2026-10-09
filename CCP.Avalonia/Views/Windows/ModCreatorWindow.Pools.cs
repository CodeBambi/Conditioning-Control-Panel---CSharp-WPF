using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// "Pools &amp; Triggers" panel, ported from ConditioningControlPanel/Windows/ModCreatorWindow.Pools.cs:
    /// subliminal pool, lock card phrases, bouncing text pool (phrase → enabled) and custom triggers.
    /// Same caps, same trim/dedupe/last-wins rules, so the manifest it writes is byte-for-byte the WPF one.
    /// Deliberate divergence: row panels are a fixed 500 wide (WPF MaxWidth 500 + Left sizes an empty
    /// TextBox to a stub), so a new row is a full-width box.
    /// </summary>
    public partial class ModCreatorWindow
    {
        // Caps mirror ModService.SanitizeManifest (WPF ModCreatorWindow.Pools.cs:23-28).
        private const int PoolSubliminalMaxEntries = 500;
        private const int PoolLockCardMaxEntries = 200;
        private const int PoolBouncingTextMaxEntries = 500;
        private const int PoolPhraseMaxLength = 500;
        private const int CustomTriggerMaxEntries = 50;
        private const int CustomTriggerMaxLength = 200;

        private StackPanel? _poolSubliminalPanel;
        private StackPanel? _poolLockCardPanel;
        private StackPanel? _poolBouncingTextPanel;
        private StackPanel? _customTriggerPanel;

        private readonly List<(CheckBox Enabled, TextBox Text)> _poolSubliminalRows = new();
        private readonly List<(CheckBox Enabled, TextBox Text)> _poolLockCardRows = new();
        private readonly List<(CheckBox Enabled, TextBox Text)> _poolBouncingTextRows = new();
        private readonly List<TextBox> _customTriggerRows = new();

        private void BuildPoolsSection()
        {
            var panel = CreateSectionPanel("pools");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Pools & Triggers"));
            stack.Children.Add(CreateSectionDescription(
                "Phrase pools your mod ships with. Subliminal Pool: short texts flashed on screen " +
                "by the subliminal feature. Lock Card Phrases: lines the user must type to dismiss " +
                "a lock card. Bouncing Text Pool: lines shown in the DVD-style bouncing screensaver. " +
                "Custom Triggers: trigger words the companion listens for. The checkbox controls " +
                "whether a phrase starts enabled. Limits: 500 subliminal phrases, 200 lock card " +
                "phrases, 500 bouncing text lines (500 characters each), and 50 custom triggers " +
                "(200 characters each) -- extra rows are dropped on export."));

            _poolSubliminalPanel = BuildPoolGroup(stack, "Subliminal Pool",
                "Short texts flashed briefly on screen. Keep them punchy -- a few words each.",
                _poolSubliminalRows);
            _poolLockCardPanel = BuildPoolGroup(stack, "Lock Card Phrases",
                "Phrases the user must type out exactly to unlock a lock card.",
                _poolLockCardRows);
            _poolBouncingTextPanel = BuildPoolGroup(stack, "Bouncing Text Pool",
                "Lines that drift around the screen in the DVD-style bouncing text overlay.",
                _poolBouncingTextRows);

            stack.Children.Add(CreateSubHeader("Custom Triggers"));
            stack.Children.Add(CreatePoolHint("Trigger words or phrases the companion listens for during voice features."));

            _customTriggerPanel = new StackPanel { Width = 500, HorizontalAlignment = HorizontalAlignment.Left };
            stack.Children.Add(_customTriggerPanel);

            var addTriggerBtn = CreatePoolAddButton("+ Add Trigger", new Thickness(0, 4, 0, 0));
            addTriggerBtn.Click += (_, _) => AddCustomTriggerRow("");
            stack.Children.Add(addTriggerBtn);
        }

        private StackPanel BuildPoolGroup(StackPanel stack, string title, string hint,
            List<(CheckBox Enabled, TextBox Text)> rows)
        {
            stack.Children.Add(CreateSubHeader(title));
            stack.Children.Add(CreatePoolHint(hint));

            var rowsPanel = new StackPanel { Width = 500, HorizontalAlignment = HorizontalAlignment.Left };
            stack.Children.Add(rowsPanel);

            var addBtn = CreatePoolAddButton("+ Add", new Thickness(0, 4, 0, 12));
            addBtn.Click += (_, _) => AddPoolRow(rowsPanel, rows, "", true);
            stack.Children.Add(addBtn);
            return rowsPanel;
        }

        private static TextBlock CreatePoolHint(string text) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.Parse("#606080")),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 600,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        private Button CreatePoolAddButton(string label, Thickness margin) => new()
        {
            // TextBlock content: Avalonia's Button would read '_' as an access key.
            Content = new TextBlock { Text = label },
            Theme = (ControlTheme)this.FindResource("SecondaryButton")!,
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = margin,
        };

        private static Button CreateRowRemoveButton() => new()
        {
            Content = "✕",
            Width = 24,
            Height = 24,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            FontSize = 10,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        private void AddPoolRow(StackPanel? rowsPanel, List<(CheckBox Enabled, TextBox Text)> rows,
            string text, bool enabled)
        {
            if (rowsPanel == null) return;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chk = new CheckBox
            {
                IsChecked = enabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            ToolTip.SetTip(chk, "Enabled by default when the mod is activated");
            Grid.SetColumn(chk, 0);
            row.Children.Add(chk);

            var tb = new TextBox
            {
                Theme = DarkTextBoxTheme,
                Text = text,
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                MaxLength = PoolPhraseMaxLength,
            };
            Grid.SetColumn(tb, 1);
            row.Children.Add(tb);

            var removeBtn = CreateRowRemoveButton();
            removeBtn.Click += (_, _) =>
            {
                rowsPanel.Children.Remove(row);
                rows.RemoveAll(r => r.Enabled == chk && r.Text == tb);
            };
            Grid.SetColumn(removeBtn, 2);
            row.Children.Add(removeBtn);

            rows.Add((chk, tb));
            rowsPanel.Children.Add(row);
        }

        private void AddCustomTriggerRow(string text)
        {
            if (_customTriggerPanel == null) return;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tb = new TextBox
            {
                Theme = DarkTextBoxTheme,
                Text = text,
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                MaxLength = CustomTriggerMaxLength,
            };
            Grid.SetColumn(tb, 0);
            row.Children.Add(tb);

            var removeBtn = CreateRowRemoveButton();
            removeBtn.Click += (_, _) =>
            {
                _customTriggerPanel?.Children.Remove(row);
                _customTriggerRows.Remove(tb);
            };
            Grid.SetColumn(removeBtn, 1);
            row.Children.Add(removeBtn);

            _customTriggerRows.Add(tb);
            _customTriggerPanel.Children.Add(row);
        }

        private void ApplyPoolsToManifest(ModManifest manifest)
        {
            manifest.SubliminalPool = BuildPoolDictionary(_poolSubliminalRows, PoolSubliminalMaxEntries);
            manifest.LockCardPhrases = BuildPoolDictionary(_poolLockCardRows, PoolLockCardMaxEntries);
            manifest.BouncingTextPool = BuildPoolDictionary(_poolBouncingTextRows, PoolBouncingTextMaxEntries);

            var triggers = _customTriggerRows
                .Select(tb => GetTextBoxValue(tb).Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .Select(t => t.Length > CustomTriggerMaxLength ? t[..CustomTriggerMaxLength] : t)
                .Distinct()
                .Take(CustomTriggerMaxEntries)
                .ToList();
            manifest.CustomTriggers = triggers.Count > 0 ? triggers : null;
        }

        /// <summary>Blank rows skipped, keys trimmed and capped, last row wins on duplicates, count capped; null when empty.</summary>
        private static Dictionary<string, bool>? BuildPoolDictionary(
            List<(CheckBox Enabled, TextBox Text)> rows, int maxEntries)
        {
            var dict = new Dictionary<string, bool>();
            foreach (var (chk, tb) in rows)
            {
                var text = GetTextBoxValue(tb).Trim();
                if (string.IsNullOrEmpty(text)) continue;
                if (text.Length > PoolPhraseMaxLength) text = text[..PoolPhraseMaxLength];
                dict[text] = chk.IsChecked == true;
            }

            if (dict.Count == 0) return null;
            if (dict.Count > maxEntries)
                dict = dict.Take(maxEntries).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            return dict;
        }

        private void PopulatePoolsFromManifest(ModManifest manifest)
        {
            ClearPoolsSection();
            if (manifest.SubliminalPool != null)
                foreach (var kvp in manifest.SubliminalPool)
                    AddPoolRow(_poolSubliminalPanel, _poolSubliminalRows, kvp.Key, kvp.Value);
            if (manifest.LockCardPhrases != null)
                foreach (var kvp in manifest.LockCardPhrases)
                    AddPoolRow(_poolLockCardPanel, _poolLockCardRows, kvp.Key, kvp.Value);
            if (manifest.BouncingTextPool != null)
                foreach (var kvp in manifest.BouncingTextPool)
                    AddPoolRow(_poolBouncingTextPanel, _poolBouncingTextRows, kvp.Key, kvp.Value);
            if (manifest.CustomTriggers != null)
                foreach (var trigger in manifest.CustomTriggers)
                    AddCustomTriggerRow(trigger);
        }

        private void ClearPoolsSection()
        {
            _poolSubliminalRows.Clear();
            _poolSubliminalPanel?.Children.Clear();
            _poolLockCardRows.Clear();
            _poolLockCardPanel?.Children.Clear();
            _poolBouncingTextRows.Clear();
            _poolBouncingTextPanel?.Children.Clear();
            _customTriggerRows.Clear();
            _customTriggerPanel?.Children.Clear();
        }
    }
}
