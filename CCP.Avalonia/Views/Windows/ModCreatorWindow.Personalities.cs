using System.Collections.Generic;
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
    /// "Personalities" panel, ported from ConditioningControlPanel/Windows/ModCreatorWindow.Personalities.cs:
    /// one card per <see cref="ModPersonality"/> with the same caps and the same promptSettings keys.
    /// </summary>
    public partial class ModCreatorWindow
    {
        private sealed class PersonalityCard
        {
            public Border Root = null!;
            public TextBox NameBox = null!;
            public TextBox DescriptionBox = null!;
            public readonly Dictionary<string, TextBox> PromptBoxes = new();
        }

        private readonly List<PersonalityCard> _personalityCards = new();
        private StackPanel? _personalityCardsPanel;

        // SanitizeManifest caps (WPF ModCreatorWindow.Personalities.cs:31-33).
        private const int _personalityMaxCount = 20;
        private const int _personalityMaxNameLength = 100;
        private const int _personalityMaxPromptLength = 5000;

        // The promptSettings keys PersonalityService.GetActiveModPersonalities consumes.
        private static readonly (string Key, string Label, string Hint)[] _personalityPromptFields =
        {
            ("Personality", "Personality",
                "The core prompt: who the companion is, their voice, agenda, and how they address the user. This is the main field - the built-in mods often use only this one."),
            ("ExplicitReaction", "Explicit Reaction",
                "How the companion responds when the user gets explicit or lewd with them."),
            ("SlutModePersonality", "Slut Mode Personality",
                "Alternate, more intense persona used while Slut Mode is enabled in AI settings."),
            ("KnowledgeBase", "Knowledge Base",
                "Facts, lore, and terminology the companion should know and weave into replies."),
            ("ContextReactions", "Context Reactions",
                "How the companion reacts to what the user is doing (gaming, browsing, working, etc.)."),
            ("OutputRules", "Output Rules",
                "Formatting and style rules for replies - length, tone, emoji use, things to never do."),
        };

        private void BuildPersonalitiesSection()
        {
            var panel = CreateSectionPanel("personalities");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Personalities"));
            stack.Children.Add(CreateSectionDescription(
                "Custom AI companion personalities for your mod. When your mod defines at least one, " +
                "these replace the built-in presets in the personality picker while the mod is active, " +
                "and the first personality in the list becomes the mod's default voice. " +
                "Each personality is a set of prompt instructions the cloud AI follows when chatting. " +
                "Only the Name and the Personality prompt are required in practice - leave any other " +
                "field empty to fall back to the app's defaults."));

            _personalityCardsPanel = new StackPanel();
            stack.Children.Add(_personalityCardsPanel);

            var addBtn = new Button
            {
                Content = new TextBlock { Text = "+ Add Personality" },
                Theme = (ControlTheme)this.FindResource("SecondaryButton")!,
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
            };
            addBtn.Click += (_, _) => AddPersonalityCard();
            stack.Children.Add(addBtn);
        }

        private static TextBlock CreatePersonalityHint(string text, double bottom) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.Parse("#707090")),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, bottom),
            MaxWidth = 500,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        private void AddPersonalityCard(ModPersonality? source = null)
        {
            if (_personalityCardsPanel == null) return;

            var card = new PersonalityCard();
            var cardStack = new StackPanel();
            card.Root = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#252542")),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 10, 14, 12),
                Margin = new Thickness(0, 0, 0, 10),
                MaxWidth = 560,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = cardStack,
            };

            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "Personality",
                Foreground = new SolidColorBrush(Color.Parse("#FF69B4")),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(title, 0);
            headerRow.Children.Add(title);

            var removeBtn = new Button
            {
                Content = "✕ Remove",
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            removeBtn.Click += (_, _) =>
            {
                _personalityCardsPanel?.Children.Remove(card.Root);
                _personalityCards.Remove(card);
            };
            Grid.SetColumn(removeBtn, 1);
            headerRow.Children.Add(removeBtn);
            cardStack.Children.Add(headerRow);

            cardStack.Children.Add(CreateFieldLabel("Name *"));
            card.NameBox = CreateDarkTextBox();
            card.NameBox.Width = 250;
            cardStack.Children.Add(card.NameBox);
            cardStack.Children.Add(CreatePersonalityHint(
                "The id is derived automatically from the name (e.g. \"Soft Keeper\" becomes soft-keeper). Cards with an empty name are skipped on export.", 4));

            cardStack.Children.Add(CreateFieldLabel("Description"));
            card.DescriptionBox = CreateDarkTextBox();
            card.DescriptionBox.Width = 500;
            cardStack.Children.Add(card.DescriptionBox);

            cardStack.Children.Add(CreateSubHeader("AI Prompt Settings"));
            foreach (var (key, label, hint) in _personalityPromptFields)
            {
                cardStack.Children.Add(CreateFieldLabel(label));
                cardStack.Children.Add(CreatePersonalityHint(hint, 3));
                var box = CreateDarkTextBox(multiline: true, height: 70);
                box.Width = 500;
                card.PromptBoxes[key] = box;
                cardStack.Children.Add(box);
            }

            if (source != null)
            {
                SetTextBoxValue(card.NameBox, source.Name);
                SetTextBoxValue(card.DescriptionBox, source.Description);
                if (source.PromptSettings != null)
                    foreach (var (key, box) in card.PromptBoxes)
                        if (source.PromptSettings.TryGetValue(key, out var value))
                            SetTextBoxValue(box, value);
            }

            _personalityCards.Add(card);
            _personalityCardsPanel.Children.Add(card.Root);
        }

        private void ApplyPersonalitiesToManifest(ModManifest manifest)
        {
            var list = new List<ModPersonality>();
            foreach (var card in _personalityCards)
            {
                if (list.Count >= _personalityMaxCount) break;

                var name = GetTextBoxValue(card.NameBox).Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name.Length > _personalityMaxNameLength) name = name[.._personalityMaxNameLength];

                var prompts = new Dictionary<string, string>();
                foreach (var (key, box) in card.PromptBoxes)
                {
                    var value = GetTextBoxValue(box).Trim();
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    if (value.Length > _personalityMaxPromptLength) value = value[.._personalityMaxPromptLength];
                    prompts[key] = value;
                }

                var description = GetTextBoxValue(card.DescriptionBox).Trim();
                list.Add(new ModPersonality
                {
                    Id = SanitizeModId(name),
                    Name = name,
                    Description = string.IsNullOrWhiteSpace(description) ? null : description,
                    PromptSettings = prompts.Count > 0 ? prompts : null,
                });
            }
            manifest.Personalities = list.Count > 0 ? list : null;
        }

        private void PopulatePersonalitiesFromManifest(ModManifest manifest)
        {
            ClearPersonalitiesSection();
            if (manifest.Personalities == null) return;
            foreach (var personality in manifest.Personalities)
                AddPersonalityCard(personality);
        }

        private void ClearPersonalitiesSection()
        {
            foreach (var card in _personalityCards)
                _personalityCardsPanel?.Children.Remove(card.Root);
            _personalityCards.Clear();
        }
    }
}
