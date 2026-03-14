using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    public partial class QuizCategoryEditorWindow : Window
    {
        private QuizCategoryDefinition? _existing;
        private string _selectedColor = "#FF69B4";
        private bool _isPreviewRunning;

        private static readonly string[] PresetColors = new[]
        {
            "#FF69B4", "#9B59B6", "#E67E22", "#3498DB",
            "#E74C3C", "#2ECC71", "#F1C40F", "#1ABC9C"
        };

        // Default percentage ranges for 5 archetypes
        private static readonly (int Min, int Max)[] DefaultRanges = new[]
        {
            (0, 25), (26, 50), (51, 70), (71, 85), (86, 100)
        };

        public QuizCategoryDefinition? Result { get; private set; }

        public QuizCategoryEditorWindow(QuizCategoryDefinition? existing = null)
        {
            InitializeComponent();
            _existing = existing;

            BuildColorPicker();
            BuildArchetypeRows();

            if (existing != null)
            {
                TxtTitle.Text = "EDIT CUSTOM CATEGORY";
                TxtName.Text = existing.Name;
                TxtDescription.Text = existing.Description;
                TxtPrompt.Text = existing.SystemPromptTemplate;
                SelectColor(existing.Color);
                PopulateArchetypes(existing.Archetypes);
                BtnDelete.Visibility = Visibility.Visible;
            }
        }

        private void BuildColorPicker()
        {
            ColorPicker.Children.Clear();
            foreach (var hex in PresetColors)
            {
                Color color;
                try { color = (Color)ColorConverter.ConvertFromString(hex); }
                catch { continue; }

                var ellipse = new Ellipse
                {
                    Width = 32, Height = 32,
                    Fill = new SolidColorBrush(color),
                    Stroke = new SolidColorBrush(Colors.Transparent),
                    StrokeThickness = 2,
                    Margin = new Thickness(0, 0, 8, 0),
                    Cursor = Cursors.Hand,
                    Tag = hex
                };
                ellipse.MouseLeftButtonDown += ColorSwatch_Click;
                ColorPicker.Children.Add(ellipse);
            }
            SelectColor(_selectedColor);
        }

        private void SelectColor(string hex)
        {
            _selectedColor = hex;
            foreach (var child in ColorPicker.Children)
            {
                if (child is Ellipse e)
                {
                    bool selected = e.Tag?.ToString() == hex;
                    e.Stroke = new SolidColorBrush(selected ? Colors.White : Colors.Transparent);
                    e.StrokeThickness = selected ? 2.5 : 2;
                }
            }
        }

        private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Ellipse el && el.Tag is string hex)
                SelectColor(hex);
        }

        private void BuildArchetypeRows()
        {
            ArchetypeRows.Children.Clear();
            string[] defaultNames = { "Tier 1 (Low)", "Tier 2", "Tier 3 (Mid)", "Tier 4", "Tier 5 (Max)" };

            for (int i = 0; i < 5; i++)
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var txtName = MakeTextBox(defaultNames[i], 30);
                txtName.Tag = $"arch_name_{i}";
                Grid.SetColumn(txtName, 0);
                grid.Children.Add(txtName);

                var txtMin = MakeTextBox(DefaultRanges[i].Min.ToString(), 3);
                txtMin.Tag = $"arch_min_{i}";
                txtMin.Margin = new Thickness(4, 0, 0, 0);
                Grid.SetColumn(txtMin, 1);
                grid.Children.Add(txtMin);

                var txtMax = MakeTextBox(DefaultRanges[i].Max.ToString(), 3);
                txtMax.Tag = $"arch_max_{i}";
                txtMax.Margin = new Thickness(4, 0, 0, 0);
                Grid.SetColumn(txtMax, 2);
                grid.Children.Add(txtMax);

                var txtDesc = MakeTextBox("", 100);
                txtDesc.Tag = $"arch_desc_{i}";
                txtDesc.Margin = new Thickness(4, 0, 0, 0);
                Grid.SetColumn(txtDesc, 3);
                grid.Children.Add(txtDesc);

                ArchetypeRows.Children.Add(grid);
            }
        }

        private static TextBox MakeTextBox(string placeholder, int maxLength)
        {
            var tb = new TextBox
            {
                MaxLength = maxLength,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 4, 6, 4),
                CaretBrush = new SolidColorBrush(Colors.White),
                Text = placeholder
            };
            return tb;
        }

        private void PopulateArchetypes(List<QuizArchetypeDefinition> archetypes)
        {
            for (int i = 0; i < Math.Min(5, archetypes.Count); i++)
            {
                var arch = archetypes[i];
                SetArchField($"arch_name_{i}", arch.Name);
                SetArchField($"arch_min_{i}", arch.MinPercentage.ToString());
                SetArchField($"arch_max_{i}", arch.MaxPercentage.ToString());
                SetArchField($"arch_desc_{i}", arch.Description);
            }
        }

        private void SetArchField(string tag, string value)
        {
            foreach (Grid grid in ArchetypeRows.Children)
            {
                foreach (var child in grid.Children)
                {
                    if (child is TextBox tb && tb.Tag?.ToString() == tag)
                    {
                        tb.Text = value;
                        return;
                    }
                }
            }
        }

        private string GetArchField(string tag)
        {
            foreach (Grid grid in ArchetypeRows.Children)
            {
                foreach (var child in grid.Children)
                {
                    if (child is TextBox tb && tb.Tag?.ToString() == tag)
                        return tb.Text?.Trim() ?? "";
                }
            }
            return "";
        }

        private List<QuizArchetypeDefinition> CollectArchetypes()
        {
            var list = new List<QuizArchetypeDefinition>();
            for (int i = 0; i < 5; i++)
            {
                var name = GetArchField($"arch_name_{i}");
                if (string.IsNullOrWhiteSpace(name)) continue;

                int.TryParse(GetArchField($"arch_min_{i}"), out int min);
                int.TryParse(GetArchField($"arch_max_{i}"), out int max);
                var desc = GetArchField($"arch_desc_{i}");

                list.Add(new QuizArchetypeDefinition
                {
                    Name = name,
                    MinPercentage = Math.Clamp(min, 0, 100),
                    MaxPercentage = Math.Clamp(max, 0, 100),
                    Description = desc
                });
            }
            return list;
        }

        private void CboTemplate_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboTemplate.SelectedItem is not ComboBoxItem item) return;
            var templateId = item.Tag?.ToString();
            if (string.IsNullOrEmpty(templateId)) return;

            var builtIn = QuizService.GetBuiltInCategories()
                .FirstOrDefault(c => c.Id == templateId);
            if (builtIn == null) return;

            // Use the built-in category's system prompt as a template
            var service = new QuizService();
            try
            {
                // For built-in categories, get the system prompt by starting a quiz and reading it
                // Instead, just build the prompt directly via reflection-free approach
                var prompt = GetBuiltInPromptText(templateId);
                if (!string.IsNullOrEmpty(prompt))
                    TxtPrompt.Text = prompt;
            }
            finally
            {
                service.Dispose();
            }

            // Also copy archetypes as a starting point
            PopulateArchetypes(builtIn.Archetypes);
        }

        private static string GetBuiltInPromptText(string categoryId)
        {
            // Create a temporary QuizService to get the prompt
            // We call the static BuildSystemPrompt via the category enum
            if (!Enum.TryParse<QuizCategory>(categoryId, true, out var cat))
                return "";

            // Use a disposable service to access the prompt builder
            using var svc = new QuizService();
            // Start and cancel immediately just to get the prompt text
            // Actually, we can call the definition-based method through FindCategory
            var def = QuizService.FindCategory(categoryId);
            if (def == null) return "";

            // For built-in categories, we know the prompt templates
            // Just provide a helpful placeholder since the actual prompts are private
            return $@"You are a quiz master for a ""{def.Name}"" personality quiz.

TONE: [Describe the voice and attitude — e.g. warm, teasing, authoritative]

QUESTION THEMES — You MUST rotate through these, one per question, no repeats:
1. [Theme 1]
2. [Theme 2]
3. [Theme 3]
4. [Theme 4]
5. [Theme 5]
6. [Theme 6]
7. [Theme 7]
8. [Theme 8]
9. [Theme 9]
10. [Theme 10]

INTENSITY SCALING — Scale with score percentage:
- LOW (below 50%): [Mild, everyday scenarios]
- MEDIUM (50-74%): [More intense, specific scenarios]
- HIGH (75%+): [Deep, extreme scenarios]

RESULT ARCHETYPES (assigned at the end based on score):
{string.Join("\n", def.Archetypes.Select(a => $"- {a.MinPercentage}-{a.MaxPercentage}%: {a.Name}"))}

FORMAT — You MUST use EXACTLY this format, nothing else:
Q: [your question here]
A: [mild answer] | 1
B: [moderate answer] | 2
C: [spicy answer] | 3
D: [extreme answer] | 4

Do NOT include any other text before or after the question format. Just the question and 4 answers.";
        }

        private async void BtnPreview_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isPreviewRunning) return;
            var prompt = TxtPrompt.Text?.Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                ShowPreviewResult("Enter a system prompt first.", false);
                return;
            }

            _isPreviewRunning = true;
            TxtPreviewHint.Text = "  — generating...";

            try
            {
                using var svc = new QuizService();
                // Build a temporary category definition to test
                var tempDef = new QuizCategoryDefinition
                {
                    Id = "preview_temp",
                    Name = TxtName.Text?.Trim() ?? "Preview",
                    SystemPromptTemplate = prompt,
                    Archetypes = CollectArchetypes()
                };

                var question = await svc.StartQuizAsync(tempDef);
                if (question != null)
                {
                    var text = $"Q: {question.QuestionText}\n" +
                               $"A: {question.Answers[0]} | {question.Points[0]}\n" +
                               $"B: {question.Answers[1]} | {question.Points[1]}\n" +
                               $"C: {question.Answers[2]} | {question.Points[2]}\n" +
                               $"D: {question.Answers[3]} | {question.Points[3]}";
                    ShowPreviewResult(text, true);
                }
                else
                {
                    ShowPreviewResult("AI couldn't generate a valid question. Check your prompt format.", false);
                }
            }
            catch (Exception ex)
            {
                ShowPreviewResult($"Error: {ex.Message}", false);
            }
            finally
            {
                _isPreviewRunning = false;
                TxtPreviewHint.Text = "  — generate a sample question";
            }
        }

        private void ShowPreviewResult(string text, bool success)
        {
            TxtPreviewResult.Text = text;
            TxtPreviewResult.Foreground = new SolidColorBrush(
                success ? Color.FromRgb(0xA0, 0xA0, 0xB0) : Color.FromRgb(0xFF, 0x66, 0x66));
            PreviewResultPanel.Visibility = Visibility.Visible;
        }

        private void BtnSave_Click(object sender, MouseButtonEventArgs e)
        {
            var name = TxtName.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please enter a category name.", "Missing Name",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (name.Length > 30) name = name[..30];

            var prompt = TxtPrompt.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(prompt))
            {
                MessageBox.Show("Please enter a system prompt for the AI.", "Missing Prompt",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var archetypes = CollectArchetypes();
            if (archetypes.Count < 2)
            {
                MessageBox.Show("Please define at least 2 archetypes.", "Need Archetypes",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check for name collision with built-in categories
            var builtInNames = QuizService.GetBuiltInCategories().Select(c => c.Name.ToLowerInvariant());
            if (builtInNames.Contains(name.ToLowerInvariant()) && _existing?.Name.ToLowerInvariant() != name.ToLowerInvariant())
            {
                MessageBox.Show("This name conflicts with a built-in category. Please choose a different name.",
                    "Name Conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = new QuizCategoryDefinition
            {
                Id = _existing?.Id ?? $"custom_{Guid.NewGuid():N}".Substring(0, 20),
                Name = name,
                Description = TxtDescription.Text?.Trim() ?? "",
                SystemPromptTemplate = prompt,
                Color = _selectedColor,
                IsBuiltIn = false,
                Archetypes = archetypes
            };

            DialogResult = true;
            Close();
        }

        private void BtnDelete_Click(object sender, MouseButtonEventArgs e)
        {
            if (_existing == null) return;

            var result = MessageBox.Show(
                $"Delete the \"{_existing.Name}\" category? This cannot be undone.",
                "Delete Category", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            QuizService.DeleteCustomCategory(_existing.Id);
            Result = null;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, MouseButtonEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnClose_Click(object sender, MouseButtonEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        private void ActionBtn_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border b)
                b.Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
        }

        private void ActionBtn_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border b)
                b.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
        }

        private void CloseBtn_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is TextBlock tb) tb.Foreground = new SolidColorBrush(Colors.White);
        }

        private void CloseBtn_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is TextBlock tb) tb.Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x80));
        }
    }
}
