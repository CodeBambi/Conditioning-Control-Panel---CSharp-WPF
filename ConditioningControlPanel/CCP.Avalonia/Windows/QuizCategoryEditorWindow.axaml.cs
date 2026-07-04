using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Quiz;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Moderation;
using ConditioningControlPanel.Models.Quiz;

using Microsoft.Extensions.DependencyInjection;
using IModerationLog = ConditioningControlPanel.IModerationLog;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the custom quiz-category editor.
/// </summary>
public partial class QuizCategoryEditorWindow : Window
{
    private readonly ILogger<QuizCategoryEditorWindow> _logger;
    private readonly IDialogService? _dialogService;
    private readonly IQuizService _quizService;
    private readonly IPromptValidator _promptValidator;
    private readonly IModerationLog _moderationLog;

    private QuizCategoryDefinition? _existing;
    private string _selectedColor = "#FF69B4";
    private bool _isPreviewRunning;

    private static string GetThemeAccentHex()
    {
        if (Application.Current?.TryFindResource("PinkColor", out var res) == true && res is Color c)
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        return "#FF69B4";
    }

    private static readonly string[] PresetColors = new[]
    {
        "#FF69B4", "#9B59B6", "#E67E22", "#3498DB",
        "#E74C3C", "#2ECC71", "#F1C40F", "#1ABC9C"
    };

    private static readonly (int Min, int Max)[] DefaultRanges = new[]
    {
        (0, 25), (26, 50), (51, 70), (71, 85), (86, 100)
    };

    public QuizCategoryDefinition? Result { get; private set; }
    public bool? DialogResult { get; set; }

    public QuizCategoryEditorWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<QuizCategoryEditorWindow>>();
        _dialogService = App.Services?.GetService<IDialogService>();
        _quizService = App.Services.GetRequiredService<IQuizService>();
        _promptValidator = App.Services.GetRequiredService<IPromptValidator>();
        _moderationLog = App.Services.GetRequiredService<IModerationLog>();
        _selectedColor = GetThemeAccentHex();
        BuildColorPicker();
        BuildArchetypeRows();
        ApplyPolicyBannerState();
    }

    public QuizCategoryEditorWindow(QuizCategoryDefinition? existing) : this()
    {
        _existing = existing;

        if (existing != null)
        {
            TxtTitle.Text = Loc.Get("label_edit_custom_category");
            TxtName.Text = existing.Name;
            TxtDescription.Text = existing.Description;
            TxtPrompt.Text = existing.SystemPromptTemplate;
            SelectColor(existing.Color);
            PopulateArchetypes(existing.Archetypes);
            BtnDelete.IsVisible = true;
        }
    }

    private void BuildColorPicker()
    {
        ColorPicker.Children.Clear();
        foreach (var hex in PresetColors)
        {
            Color color;
            try { color = Color.Parse(hex); }
            catch { continue; }

            var ellipse = new Ellipse
            {
                Width = 32,
                Height = 32,
                Fill = new SolidColorBrush(color),
                Stroke = new SolidColorBrush(Colors.Transparent),
                StrokeThickness = 2,
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = hex
            };
            ellipse.PointerPressed += ColorSwatch_Click;
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

    private void ColorSwatch_Click(object? sender, PointerPressedEventArgs e)
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
        return new TextBox
        {
            MaxLength = maxLength,
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.White),
            Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4),
            CaretBrush = new SolidColorBrush(Colors.White),
            Text = placeholder
        };
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

    private void CboTemplate_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CboTemplate.SelectedItem is not ComboBoxItem item) return;
        var templateId = item.Tag?.ToString();
        if (string.IsNullOrEmpty(templateId)) return;

        var builtIn = _quizService.GetBuiltInCategories()
            .FirstOrDefault(c => c.Id == templateId);
        if (builtIn == null) return;

        var prompt = GetBuiltInPromptText(templateId, builtIn);
        if (!string.IsNullOrEmpty(prompt))
            TxtPrompt.Text = prompt;

        PopulateArchetypes(builtIn.Archetypes);
    }

    private static string GetBuiltInPromptText(string categoryId, QuizCategoryDefinition def)
    {
        var archetypeLines = string.Join("\n", def.Archetypes.Select(a => $"- {a.MinPercentage}-{a.MaxPercentage}%: {a.Name}"));
        return $"You are a quiz master for a \"{def.Name}\" personality quiz.\n\n" +
               "TONE: [Describe the voice and attitude - e.g. warm, teasing, authoritative]\n\n" +
               "QUESTION THEMES - You MUST rotate through these, one per question, no repeats:\n" +
               "1. [Theme 1]\n2. [Theme 2]\n3. [Theme 3]\n4. [Theme 4]\n5. [Theme 5]\n" +
               "6. [Theme 6]\n7. [Theme 7]\n8. [Theme 8]\n9. [Theme 9]\n10. [Theme 10]\n\n" +
               "INTENSITY SCALING - Scale with score percentage:\n" +
               "- LOW (below 50%): [Mild, everyday scenarios]\n" +
               "- MEDIUM (50-74%): [More intense, specific scenarios]\n" +
               "- HIGH (75%+): [Deep, extreme scenarios]\n\n" +
               "RESULT ARCHETYPES (assigned at the end based on score):\n" +
               archetypeLines +
               "\n\nFORMAT - You MUST use EXACTLY this format, nothing else:\n" +
               "Q: [your question here]\n" +
               "A: [mild answer] | 1\n" +
               "B: [moderate answer] | 2\n" +
               "C: [spicy answer] | 3\n" +
               "D: [extreme answer] | 4\n\n" +
               "Do NOT include any other text before or after the question format. Just the question and 4 answers.";
    }

    private async void BtnPreview_Click(object? sender, PointerPressedEventArgs e)
    {
        if (_isPreviewRunning) return;
        var prompt = TxtPrompt.Text?.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            ShowPreviewResult("Enter a system prompt first.", false);
            return;
        }

        _isPreviewRunning = true;
        TxtPreviewHint.Text = Loc.Get("label_generating");

        try
        {
            using var svc = App.Services?.GetRequiredService<IQuizService>();
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
            TxtPreviewHint.Text = Loc.Get("label_generate_a_sample_question");
        }
    }

    private void ShowPreviewResult(string text, bool success)
    {
        TxtPreviewResult.Text = text;
        TxtPreviewResult.Foreground = new SolidColorBrush(
            success ? Color.FromRgb(0xA0, 0xA0, 0xB0) : Color.FromRgb(0xFF, 0x66, 0x66));
        PreviewResultPanel.IsVisible = true;
    }

    private async void BtnSave_Click(object? sender, PointerPressedEventArgs e)
    {
        var name = TxtName.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_missing_name"),
                    Loc.Get("msg_please_enter_a_category_name"),
                    DialogSeverity.Warning);
            }
            return;
        }

        if (name.Length > 30) name = name[..30];

        var prompt = TxtPrompt.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(prompt))
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_missing_prompt"),
                    Loc.Get("msg_please_enter_a_system_prompt_for_the_ai"),
                    DialogSeverity.Warning);
            }
            return;
        }

        var archetypes = CollectArchetypes();
        if (archetypes.Count < 2)
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_need_archetypes"),
                    Loc.Get("msg_please_define_at_least_2_archetypes"),
                    DialogSeverity.Warning);
            }
            return;
        }

        var builtInNames = _quizService.GetBuiltInCategories().Select(c => c.Name.ToLowerInvariant());
        if (builtInNames.Contains(name.ToLowerInvariant()) && _existing?.Name.ToLowerInvariant() != name.ToLowerInvariant())
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_name_conflict"),
                    Loc.Get("msg_this_name_conflicts_with_a_built_in_category"),
                    DialogSeverity.Warning);
            }
            return;
        }

        RunPromptValidation(prompt);

        Result = new QuizCategoryDefinition
        {
            Id = _existing?.Id ?? $"custom_{Guid.NewGuid():N}"[..20],
            Name = name,
            Description = TxtDescription.Text?.Trim() ?? "",
            SystemPromptTemplate = prompt,
            Color = _selectedColor,
            IsBuiltIn = false,
            Archetypes = archetypes
        };

        DialogResult = true;
        Close(true);
    }

    private void RunPromptValidation(string prompt)
    {
        try
        {
            var result = _promptValidator.Validate(prompt);
            if (result.Clean)
            {
                TxtPrompt.BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
                TxtPrompt.BorderThickness = new Thickness(1);
                ValidatorBanner.IsVisible = false;
                return;
            }

            TxtPrompt.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xC7, 0x6B));
            TxtPrompt.BorderThickness = new Thickness(2);

            TxtValidatorBanner.Text = string.Format(
                Loc.Get("prompt_validator_banner") ?? "Prompt validation flagged {0} pattern(s).",
                result.MatchedPatterns.Count);
            ValidatorBanner.IsVisible = true;

            // Fail-closed like CompanionPromptEditorDialog: a non-clean result is written to the
            // real moderation log (mirrors WPF QuizCategoryEditorWindow -> quiz_category source).
            try
            {
                _moderationLog.RecordEdit("SystemPromptTemplate", result.MatchedPatterns.Count, "quiz_category");
            }
            catch (Exception logEx)
            {
                _logger?.LogWarning(logEx, "QuizCategoryEditorWindow: failed to record moderation edit");
            }

            _logger?.LogInformation(
                "PromptValidator flagged QuizCategoryEditorWindow system prompt ({Count} matches)",
                result.MatchedPatterns.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "QuizCategoryEditorWindow: prompt validation failed");
        }
    }

    private async void BtnDelete_Click(object? sender, PointerPressedEventArgs e)
    {
        if (_existing == null) return;

        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_delete_category"),
            Loc.GetF("msg_delete_category", _existing.Name)) ?? Task.FromResult(false));

        if (!confirmed) return;

        _quizService.DeleteCustomCategory(_existing.Id);
        Result = null;
        DialogResult = true;
        Close(true);
    }

    private void BtnCancel_Click(object? sender, PointerPressedEventArgs e)
    {
        DialogResult = false;
        Close(false);
    }

    private void BtnClose_Click(object? sender, PointerPressedEventArgs e)
    {
        DialogResult = false;
        Close(false);
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close(false);
        }
    }

    private void ActionBtn_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border b)
            b.Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
    }

    private void ActionBtn_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border b)
            b.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
    }

    private void CloseBtn_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is TextBlock tb) tb.Foreground = new SolidColorBrush(Colors.White);
    }

    private void CloseBtn_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is TextBlock tb) tb.Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x80));
    }

    private void ApplyPolicyBannerState()
    {
        bool acked = false;
        try
        {
            var settings = App.Services?.GetService<ISettingsService>()?.Current;
            dynamic? companion = settings?.CompanionPrompt;
            acked = companion?.PromptEditorDisclaimerAcknowledged == true;
        }
        catch { /* dynamic property may not exist */ }

        if (PolicyBannerFull != null)
            PolicyBannerFull.IsVisible = !acked;
        if (PolicyBannerSlim != null)
            PolicyBannerSlim.IsVisible = acked;
    }

    private void BtnPolicyGotIt_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var settings = App.Services?.GetService<ISettingsService>()?.Current;
            dynamic? companion =
settings?.CompanionPrompt;
            if (companion != null)
            {
                companion.PromptEditorDisclaimerAcknowledged = true;
            }
        }
        catch { }
        ApplyPolicyBannerState();
    }

    private void BtnPolicyRead_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                "https://app.cclabs.app/policies/prohibited-content") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "QuizCategoryEditorWindow: failed to open policy URL");
        }
    }
}
