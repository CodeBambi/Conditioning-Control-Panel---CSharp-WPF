using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Editor for a user-authored quiz category: name, blurb, accent colour, the AI system prompt
    /// that generates its questions, and the five archetypes the final score maps onto.
    ///
    /// PORTED from ConditioningControlPanel/Windows/QuizCategoryEditorWindow.xaml.cs. Deviations:
    ///  - <c>QuizCategoryDefinition</c> / <c>QuizArchetypeDefinition</c> live in the WPF head's
    ///    Services/Quiz/QuizService.cs, not in CCP.Core, and neither may be touched by this port -
    ///    so they are copied below, trimmed to the fields this view reads or writes (the
    ///    TextEditorDialog/QuizReportWindow precedent).
    ///  - <c>QuizService</c> is not in Core either, so the template dropdown, the AI preview, the
    ///    built-in name-collision check and Delete are stubs. Each carries a ponytail comment.
    ///  - <c>PromptValidator</c> IS in Core, so <see cref="RunPromptValidation"/> runs for real.
    ///  - The four <c>MessageBox.Show</c> calls have no Avalonia equivalent and no package may be
    ///    added; they become ponytail comments, so the empty-field guards still block the save but
    ///    do so silently (CompanionPromptEditorDialog precedent).
    ///  - <c>DialogResult = x; Close()</c> -> <c>Close(x)</c>.
    ///  - The public ctor loses its <c>= null</c> default: with a parameterless render ctor beside
    ///    it, <c>new QuizCategoryEditorWindow()</c> would be ambiguous (CS0121).
    ///  - The Tag-scan pair (<c>SetArchField</c>/<c>GetArchField</c>) is replaced by the
    ///    <c>_arch</c> array the row builder already has to hold. Same behaviour, ~40 fewer lines.
    ///  - <c>TxtTitle</c>/<c>TxtPreviewHint</c> are set from code only, never bound - see the
    ///    header comment in the .axaml.
    /// </summary>
    public partial class QuizCategoryEditorWindow : Window
    {
        private readonly QuizCategoryDefinition? _existing;
        private string _selectedColor = "#FF69B4";
        private bool _policyAcked;

        /// <summary>Row i holds { name, min, max, description } for archetype i.</summary>
        private readonly List<TextBox[]> _arch = new();

        private readonly TextBox _txtName, _txtDescription, _txtPrompt;
        private readonly WrapPanel _colorPicker;
        private readonly TextBlock _txtPreviewHint;

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

        /// <summary>Render/design constructor: an existing category, so --render-view draws the
        /// edit title, the filled boxes, the populated archetype rows AND the Delete button.
        /// Internal, so no production caller can ship the sample.</summary>
        internal QuizCategoryEditorWindow() : this(SampleCategory()) { }

        public QuizCategoryEditorWindow(QuizCategoryDefinition? existing)
        {
            AvaloniaXamlLoader.Load(this);
            _existing = existing;

            _txtName = this.FindControl<TextBox>("TxtName")!;
            _txtDescription = this.FindControl<TextBox>("TxtDescription")!;
            _txtPrompt = this.FindControl<TextBox>("TxtPrompt")!;
            _colorPicker = this.FindControl<WrapPanel>("ColorPicker")!;
            _txtPreviewHint = this.FindControl<TextBlock>("TxtPreviewHint")!;

            _txtPreviewHint.Text = Loc.Get("label_generate_a_sample_question");
            this.FindControl<TextBlock>("TxtTitle")!.Text = Loc.Get(
                existing != null ? "label_edit_custom_category" : "label_create_custom_category");

            BuildColorPicker();
            BuildArchetypeRows();
            ApplyPolicyBannerState();

            if (existing != null)
            {
                _txtName.Text = existing.Name;
                _txtDescription.Text = existing.Description;
                _txtPrompt.Text = existing.SystemPromptTemplate;
                SelectColor(existing.Color);
                PopulateArchetypes(existing.Archetypes);
                this.FindControl<Border>("BtnDelete")!.IsVisible = true;
            }

            // Handlers live here rather than in markup, per the porting convention. Wired after the
            // loads above so the initial SelectedIndex="0" does not fire the template copy.
            this.FindControl<ComboBox>("CboTemplate")!.SelectionChanged += CboTemplate_SelectionChanged;
            this.FindControl<Button>("BtnPolicyGotIt")!.Click += (_, _) => BtnPolicyGotIt_Click();
            this.FindControl<Button>("BtnPolicyReadFull")!.Click += (_, _) => BtnPolicyRead_Click();
            this.FindControl<Button>("BtnPolicyReadSlim")!.Click += (_, _) => BtnPolicyRead_Click();

            WireActionBorder("BtnPreview", BtnPreview_Click);
            WireActionBorder("BtnDelete", BtnDelete_Click);
            WireActionBorder("BtnCancel", _ => Close(false));
            WireActionBorder("BtnSave", BtnSave_Click);

            var close = this.FindControl<TextBlock>("BtnClose")!;
            close.PointerPressed += (_, _) => Close(false);
            close.PointerEntered += (_, _) => close.Foreground = Brushes.White;
            close.PointerExited += (_, _) => close.Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x80));

            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(false); };
        }

        /// <summary>
        /// WPF's MouseLeftButtonDown + the shared ActionBtn_MouseEnter/Leave pair, on one of the
        /// four Borders that stand in for buttons. One deliberate divergence: WPF's MouseLeave set
        /// EVERY border to #15FFFFFF, so Save and Delete lost their pink and red tint permanently
        /// after the first hover. This captures each border's own idle brush and restores that.
        /// </summary>
        private void WireActionBorder(string name, Action<Border> onClick)
        {
            var border = this.FindControl<Border>(name)!;
            var idle = border.Background;
            border.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) onClick(border);
            };
            border.PointerEntered += (_, _) =>
                border.Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
            border.PointerExited += (_, _) => border.Background = idle;
        }

        private void BuildColorPicker()
        {
            _colorPicker.Children.Clear();
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
                    Stroke = Brushes.Transparent,
                    StrokeThickness = 2,
                    Margin = new Thickness(0, 0, 8, 0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Tag = hex
                };
                ellipse.PointerPressed += ColorSwatch_Click;
                _colorPicker.Children.Add(ellipse);
            }
            SelectColor(_selectedColor);
        }

        private void SelectColor(string hex)
        {
            _selectedColor = hex;
            foreach (var child in _colorPicker.Children)
            {
                if (child is Ellipse e)
                {
                    bool selected = e.Tag?.ToString() == hex;
                    e.Stroke = selected ? Brushes.White : Brushes.Transparent;
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
            var rows = this.FindControl<StackPanel>("ArchetypeRows")!;
            rows.Children.Clear();
            _arch.Clear();

            // Hardcoded English in WPF too - there are no loc keys for the five default tier names.
            string[] defaultNames = { "Tier 1 (Low)", "Tier 2", "Tier 3 (Mid)", "Tier 4", "Tier 5 (Max)" };

            for (int i = 0; i < 5; i++)
            {
                var grid = new Grid
                {
                    Margin = new Thickness(0, 0, 0, 6),
                    ColumnDefinitions = new ColumnDefinitions("*,55,55,*")
                };

                var cells = new[]
                {
                    MakeTextBox(defaultNames[i], 30),
                    MakeTextBox(DefaultRanges[i].Min.ToString(), 3),
                    MakeTextBox(DefaultRanges[i].Max.ToString(), 3),
                    MakeTextBox("", 100),
                };

                for (int c = 0; c < cells.Length; c++)
                {
                    if (c > 0) cells[c].Margin = new Thickness(4, 0, 0, 0);
                    Grid.SetColumn(cells[c], c);
                    grid.Children.Add(cells[c]);
                }

                _arch.Add(cells);
                rows.Children.Add(grid);
            }
        }

        private static TextBox MakeTextBox(string text, int maxLength) => new()
        {
            MaxLength = maxLength,
            FontSize = 12,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4),
            CaretBrush = Brushes.White,
            Text = text
        };

        private void PopulateArchetypes(List<QuizArchetypeDefinition> archetypes)
        {
            for (int i = 0; i < Math.Min(_arch.Count, archetypes.Count); i++)
            {
                var arch = archetypes[i];
                _arch[i][0].Text = arch.Name;
                _arch[i][1].Text = arch.MinPercentage.ToString();
                _arch[i][2].Text = arch.MaxPercentage.ToString();
                _arch[i][3].Text = arch.Description;
            }
        }

        private List<QuizArchetypeDefinition> CollectArchetypes()
        {
            var list = new List<QuizArchetypeDefinition>();
            foreach (var row in _arch)
            {
                var name = (row[0].Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                int.TryParse((row[1].Text ?? "").Trim(), out int min);
                int.TryParse((row[2].Text ?? "").Trim(), out int max);

                list.Add(new QuizArchetypeDefinition
                {
                    Name = name,
                    MinPercentage = Math.Clamp(min, 0, 100),
                    MaxPercentage = Math.Clamp(max, 0, 100),
                    Description = (row[3].Text ?? "").Trim()
                });
            }
            return list;
        }

        private void CboTemplate_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (((ComboBox)sender!).SelectedItem is not ComboBoxItem item) return;
            var templateId = item.Tag?.ToString();
            if (string.IsNullOrEmpty(templateId)) return;

            // ponytail: needs QuizService.GetBuiltInCategories()/FindCategory() for the real
            // category name and its archetype table; wired when the quiz service moves to Core.
            // The skeleton below is the WPF original's, minus the RESULT ARCHETYPES block it filled
            // from the built-in definition.
            _txtPrompt.Text = GetBuiltInPromptText(templateId);
        }

        private static string GetBuiltInPromptText(string categoryId) => $@"You are a quiz master for a ""{categoryId}"" personality quiz.

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

FORMAT — You MUST use EXACTLY this format, nothing else:
Q: [your question here]
A: [mild answer] | 1
B: [moderate answer] | 2
C: [spicy answer] | 3
D: [extreme answer] | 4

Do NOT include any other text before or after the question format. Just the question and 4 answers.";

        private void BtnPreview_Click(Border _)
        {
            var prompt = (_txtPrompt.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                ShowPreviewResult("Enter a system prompt first.", false);
                return;
            }

            // ponytail: needs QuizService.StartQuizAsync to round-trip the prompt through the AI
            // provider; wired when the quiz service moves to Core. Until then the panel opens with
            // the same "couldn't generate" copy the WPF original shows on a null question, so the
            // busy/idle hint swap and ShowPreviewResult stay exercised.
            _txtPreviewHint.Text = Loc.Get("label_generating");
            ShowPreviewResult("AI couldn't generate a valid question. Check your prompt format.", false);
            _txtPreviewHint.Text = Loc.Get("label_generate_a_sample_question");
        }

        private void ShowPreviewResult(string text, bool success)
        {
            var box = this.FindControl<TextBlock>("TxtPreviewResult")!;
            box.Text = text;
            box.Foreground = new SolidColorBrush(
                success ? Color.FromRgb(0xA0, 0xA0, 0xB0) : Color.FromRgb(0xFF, 0x66, 0x66));
            this.FindControl<Border>("PreviewResultPanel")!.IsVisible = true;
        }

        private void BtnSave_Click(Border _)
        {
            var name = (_txtName.Text ?? "").Trim();
            // ponytail: WPF showed MessageBox(msg_please_enter_a_category_name) here; no Avalonia
            // equivalent and no package may be added, so the guard blocks silently.
            if (string.IsNullOrWhiteSpace(name)) return;

            if (name.Length > 30) name = name[..30];

            var prompt = (_txtPrompt.Text ?? "").Trim();
            // ponytail: WPF showed MessageBox(msg_please_enter_a_system_prompt_for_the_ai) here.
            if (string.IsNullOrWhiteSpace(prompt)) return;

            var archetypes = CollectArchetypes();
            // ponytail: WPF showed MessageBox(msg_please_define_at_least_2_archetypes) here.
            if (archetypes.Count < 2) return;

            // ponytail: needs QuizService.GetBuiltInCategories() to reject a name that collides with
            // a built-in one (msg_this_name_conflicts_with_a_built_in_category); wired when the
            // quiz service moves to Core.

            // P1.3 PromptValidator: soft validation, warns but does not block save.
            RunPromptValidation(prompt);

            Result = new QuizCategoryDefinition
            {
                Id = _existing?.Id ?? $"custom_{Guid.NewGuid():N}".Substring(0, 20),
                Name = name,
                Description = (_txtDescription.Text ?? "").Trim(),
                SystemPromptTemplate = prompt,
                Color = _selectedColor,
                IsBuiltIn = false,
                Archetypes = archetypes
            };

            Close(true);
        }

        /// <summary>
        /// P1.3 — soft validator over the system prompt textbox. Hits paint the
        /// TextBox yellow and raise the banner. Always returns; save is never blocked.
        /// </summary>
        private void RunPromptValidation(string prompt)
        {
            var result = new PromptValidator().Validate(prompt);
            var banner = this.FindControl<Border>("ValidatorBanner")!;

            if (result.Clean)
            {
                _txtPrompt.BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
                _txtPrompt.BorderThickness = new Thickness(1);
                _txtPrompt.ClearValue(ToolTip.TipProperty);
                banner.IsVisible = false;
                return;
            }

            _txtPrompt.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xC7, 0x6B));
            _txtPrompt.BorderThickness = new Thickness(2);
            ToolTip.SetTip(_txtPrompt, Loc.GetF("prompt_validator_warning", result.MatchedPatterns.Count));

            // The literal 1 is the WPF original's: this editor has exactly one validated field.
            this.FindControl<TextBlock>("TxtValidatorBanner")!.Text = Loc.GetF("prompt_validator_banner", 1);
            banner.IsVisible = true;

            // ponytail: App.ModerationLog?.RecordEdit("SystemPromptTemplate", count, "quiz_category")
            // and the matching App.Logger line, wired when the moderation log moves to Core.
        }

        private void BtnDelete_Click(Border _)
        {
            if (_existing == null) return;

            // ponytail: WPF confirmed with a Yes/No MessageBox and then called
            // QuizService.DeleteCustomCategory(_existing.Id); both are stubbed, so the dialog just
            // closes with a null Result - which is already the "deleted" signal the caller reads.
            Result = null;
            Close(true);
        }

        /// <summary>
        /// CCBill AI Addendum: show full content-policy banner until acked, then slim version.
        /// </summary>
        private void ApplyPolicyBannerState()
        {
            // ponytail: needs App.Settings.Current.CompanionPrompt.PromptEditorDisclaimerAcknowledged,
            // wired when settings move to Core. The default is "not yet acknowledged".
            this.FindControl<Border>("PolicyBannerFull")!.IsVisible = !_policyAcked;
            this.FindControl<Border>("PolicyBannerSlim")!.IsVisible = _policyAcked;
        }

        private void BtnPolicyGotIt_Click()
        {
            _policyAcked = true; // ponytail: persisted via App.Settings.Save() once settings move to Core
            ApplyPolicyBannerState();
        }

        private void BtnPolicyRead_Click()
        {
            // ponytail: needs a launcher for https://app.cclabs.app/policies/prohibited-content;
            // Process.Start(UseShellExecute) is per-platform and belongs behind a Core interface.
        }

        private static QuizCategoryDefinition SampleCategory() => new()
        {
            Id = "custom_sample",
            Name = "Velvet Fog",
            Description = "A slow, warm slide into agreeable emptiness.",
            Color = "#9B59B6",
            SystemPromptTemplate = "You are a quiz master for a \"Velvet Fog\" personality quiz.\n\n"
                                 + "TONE: warm, unhurried, quietly certain.\n\n"
                                 + "FORMAT — You MUST use EXACTLY this format:\n"
                                 + "Q: [your question here]\nA: [mild] | 1\nB: [moderate] | 2\n"
                                 + "C: [spicy] | 3\nD: [extreme] | 4",
            Archetypes =
            {
                new QuizArchetypeDefinition { Name = "Clear Headed", MinPercentage = 0, MaxPercentage = 25, Description = "Still counting the exits." },
                new QuizArchetypeDefinition { Name = "Softening", MinPercentage = 26, MaxPercentage = 50, Description = "The edges have gone kind." },
                new QuizArchetypeDefinition { Name = "Drifting", MinPercentage = 51, MaxPercentage = 70, Description = "Agreeing before the sentence lands." },
                new QuizArchetypeDefinition { Name = "Fogbound", MinPercentage = 71, MaxPercentage = 85, Description = "Thoughts arrive pre-approved." },
                new QuizArchetypeDefinition { Name = "Velvet", MinPercentage = 86, MaxPercentage = 100, Description = "Nothing left to argue with." },
            }
        };
    }

    /// <summary>
    /// One scoring band of a quiz category. Copied from the WPF head's
    /// Services/Quiz/QuizService.cs: the type lives there, not in CCP.Core, and neither the WPF
    /// head nor Core may be touched by this port.
    /// </summary>
    public class QuizArchetypeDefinition
    {
        public string Name { get; set; } = string.Empty;
        public int MinPercentage { get; set; }
        public int MaxPercentage { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    /// <inheritdoc cref="QuizArchetypeDefinition"/>
    /// <remarks>Trimmed to the fields this editor reads or writes: the original also carries
    /// EnumCategory and GetArchetypeName, neither of which this view touches.</remarks>
    public class QuizCategoryDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SystemPromptTemplate { get; set; } = string.Empty;
        public string Color { get; set; } = "#FF69B4";
        public bool IsBuiltIn { get; set; }
        public List<QuizArchetypeDefinition> Archetypes { get; set; } = new();
    }
}
