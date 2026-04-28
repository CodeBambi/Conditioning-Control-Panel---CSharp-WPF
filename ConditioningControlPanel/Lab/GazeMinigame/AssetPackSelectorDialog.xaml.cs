using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace ConditioningControlPanel.Lab.GazeMinigame
{
    /// <summary>
    /// Modal pack picker. Lets the user add 2+ folders, mark the first as the
    /// "correct" training target (everything else is noise), and confirms with
    /// OK. Returns the ordered list via <see cref="SelectedPacks"/> when
    /// DialogResult==true.
    /// </summary>
    public partial class AssetPackSelectorDialog : System.Windows.Window
    {
        public List<AssetPack> SelectedPacks { get; } = new();

        public AssetPackSelectorDialog()
        {
            InitializeComponent();
            RebuildPackList();
        }

        public AssetPackSelectorDialog(IEnumerable<AssetPack> initial) : this()
        {
            foreach (var p in initial) SelectedPacks.Add(p);
            RebuildPackList();
        }

        private void BtnAddPack_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog
            {
                Description = "Pick an asset pack folder (images and/or videos)",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
                SelectedPath = App.EffectiveAssetsPath,
            };

            if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
            var folder = dlg.SelectedPath;
            if (string.IsNullOrWhiteSpace(folder)) return;

            // Reject duplicates by absolute path.
            foreach (var existing in SelectedPacks)
            {
                if (PathsEqual(existing.Path, folder))
                {
                    System.Windows.MessageBox.Show(this,
                        "That pack is already in the list.",
                        "Duplicate pack", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            var pack = AssetPack.FromFolder(folder);
            if (pack == null)
            {
                System.Windows.MessageBox.Show(this,
                    $"No images or videos found in:\n{folder}\n\nSupported: .png .jpg .jpeg .gif .webp .bmp .mp4 .webm .mov .avi .mkv",
                    "Empty pack", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedPacks.Add(pack);
            RebuildPackList();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void RebuildPackList()
        {
            PackListPanel.Children.Clear();

            if (SelectedPacks.Count == 0)
            {
                PackListPanel.Children.Add(new TextBlock
                {
                    Text = "No packs added yet. Click \"+ Add pack\" to pick a folder.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(8, 16, 8, 16),
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
            }

            for (int i = 0; i < SelectedPacks.Count; i++)
            {
                PackListPanel.Children.Add(BuildPackRow(SelectedPacks[i], i));
            }

            BtnOk.IsEnabled = SelectedPacks.Count >= 2;
        }

        private Border BuildPackRow(AssetPack pack, int index)
        {
            var isCorrect = index == 0;
            var roleColor = isCorrect ? Color.FromRgb(0xFF, 0x69, 0xB4) : Color.FromRgb(0xFF, 0x80, 0x80);
            var roleLabel = isCorrect ? "CORRECT" : "noise";

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                BorderBrush = new SolidColorBrush(roleColor),
                BorderThickness = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Role pill
            var rolePill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x40, roleColor.R, roleColor.G, roleColor.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = roleLabel,
                    Foreground = new SolidColorBrush(roleColor),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                },
            };
            Grid.SetColumn(rolePill, 0);
            grid.Children.Add(rolePill);

            // Name + path
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = pack.Name,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
            });
            stack.Children.Add(new TextBlock
            {
                Text = pack.Path,
                Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);

            // Counts
            var counts = new TextBlock
            {
                Text = $"{pack.ImageCount} img · {pack.VideoCount} vid",
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            };
            Grid.SetColumn(counts, 2);
            grid.Children.Add(counts);

            // Move-up button (only for non-first rows)
            if (index > 0)
            {
                var btnUp = new Button
                {
                    Content = "↑",
                    Width = 28, Height = 24,
                    Margin = new Thickness(0, 0, 4, 0),
                    Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x3A)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    ToolTip = index == 1 ? "Promote to CORRECT" : "Move up",
                };
                btnUp.Click += (_, _) => { var p = SelectedPacks[index]; SelectedPacks.RemoveAt(index); SelectedPacks.Insert(index - 1, p); RebuildPackList(); };
                Grid.SetColumn(btnUp, 3);
                grid.Children.Add(btnUp);
            }

            // Remove button
            var btnRemove = new Button
            {
                Content = "✕",
                Width = 28, Height = 24,
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x1A, 0x1A)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x80, 0x80)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Remove pack",
            };
            btnRemove.Click += (_, _) => { SelectedPacks.RemoveAt(index); RebuildPackList(); };
            Grid.SetColumn(btnRemove, 4);
            grid.Children.Add(btnRemove);

            border.Child = grid;
            return border;
        }

        private static bool PathsEqual(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }
    }
}
