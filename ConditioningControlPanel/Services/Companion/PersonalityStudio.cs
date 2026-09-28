using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>Expert editing happens on a detached copy. Closing the window changes nothing.</summary>
internal static class PersonalityStudio
{
    internal static PersonalityPreset Draft(PersonalityPreset source, CompanionPromptSettings? activeOverride = null)
    {
        var p = activeOverride ?? source.PromptSettings ?? new CompanionPromptSettings();
        return new PersonalityPreset
        {
            Id = Guid.NewGuid().ToString("N"), Name = source.Name, Description = source.Description,
            IsBuiltIn = false, RequiresExplicitAcknowledgement = source.RequiresExplicitAcknowledgement,
            SampleLines = source.SampleLines?.ToList(),
            // Only character text belongs in a preset. Never copy API keys, hosts or effect permissions.
            PromptSettings = new CompanionPromptSettings
            {
                UseCustomPrompt = true, Personality = p.Personality, ExplicitReaction = p.ExplicitReaction,
                SlutModePersonality = p.SlutModePersonality, KnowledgeBase = p.KnowledgeBase,
                ContextReactions = p.ContextReactions, OutputRules = p.OutputRules
            }
        };
    }

    public static void Show(Window owner)
    {
        var service = App.Personality;
        var settings = App.Settings?.Current;
        if (service == null || settings == null) return;
        var source = service.GetActivePreset();
        var draft = Draft(source, settings.CompanionPrompt?.UseCustomPrompt == true ? settings.CompanionPrompt : null);
        var dialog = new Window
        {
            Owner = owner, Title = L("title"), Width = 660, Height = 650, MinWidth = 460, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(22, 17, 32)), Foreground = Brushes.WhiteSmoke
        };
        var grid = new Grid { Margin = new Thickness(24) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var intro = new TextBlock { Text = L("intro"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        grid.Children.Add(intro);
        var form = new StackPanel();
        var name = Field(form, L("name"), source.Name, 60, 34);
        var voice = Field(form, L("voice"), draft.PromptSettings!.Personality, 3600, 180);
        var style = Field(form, L("style"), draft.PromptSettings.OutputRules, 700, 80);
        var samples = Field(form, L("samples"), string.Join(Environment.NewLine, draft.SampleLines ?? new List<string>()), 300, 62);
        var advanced = new Expander { Header = L("advanced"), Margin = new Thickness(0, 12, 0, 0) };
        var extra = new StackPanel();
        var background = Field(extra, L("background"), draft.PromptSettings.KnowledgeBase, 1200, 90);
        var bounds = Field(extra, L("boundaries"), draft.PromptSettings.ExplicitReaction, 800, 70);
        advanced.Content = extra;
        form.Children.Add(advanced);
        var scroller = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroller, 1); grid.Children.Add(scroller);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = L("cancel"), IsCancel = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 10, 0) };
        var save = new Button { Content = L("save"), Padding = new Thickness(16, 8, 16, 8) };
        actions.Children.Add(cancel); actions.Children.Add(save);
        Grid.SetRow(actions, 2); grid.Children.Add(actions);
        void Validate() => save.IsEnabled = !string.IsNullOrWhiteSpace(name.Text) && !string.IsNullOrWhiteSpace(voice.Text);
        name.TextChanged += (_, _) => Validate(); voice.TextChanged += (_, _) => Validate(); Validate();
        save.Click += (_, _) =>
        {
            draft.Name = name.Text.Trim();
            draft.PromptSettings.Personality = voice.Text.Trim();
            draft.PromptSettings.OutputRules = style.Text.Trim();
            draft.PromptSettings.KnowledgeBase = background.Text.Trim();
            draft.PromptSettings.ExplicitReaction = bounds.Text.Trim();
            draft.SampleLines = PersonalitySamples.Clean(samples.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            settings.UserPersonalityPresets.Add(draft);
            if (!service.SetActivePreset(draft.Id))
            {
                settings.UserPersonalityPresets.Remove(draft);
                return;
            }
            BambiSprite.InvalidateStablePrompt();
            dialog.DialogResult = true;
        };
        dialog.Content = grid;
        dialog.ShowDialog();
    }

    private static TextBox Field(Panel panel, string label, string text, int max, double height)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 5) });
        var box = new TextBox
        {
            Text = text, MaxLength = max, Height = height, AcceptsReturn = height > 40,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Foreground = Brushes.Black, Background = Brushes.WhiteSmoke, Padding = new Thickness(8)
        };
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        panel.Children.Add(box);
        return box;
    }

    private static string L(string key) => Loc.Get("companion_studio_" + key);
}