using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;

namespace ConditioningControlPanel.Views.Controls.Companion.V2;

internal static class PerkPicker
{
    internal static void Show(Window owner)
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        var window = new Window { Owner = owner, Title = Loc.Get("companion_perk_picker"),
            Width = 510, SizeToContent = SizeToContent.Height, MaxHeight = 710,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(27,18,37)), Foreground = Brushes.White,
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        panel.Children.Add(new TextBlock { Text = window.Title, FontSize = 24, Margin = new Thickness(0,0,0,8) });
        panel.Children.Add(new TextBlock { Text = Loc.Get("companion_perk_hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,14) });
        foreach (var type in Enum.GetValues<CompanionBonusType>())
        {
            var perk = CompanionPerks.For(type);
            var unlocked = CompanionPerks.CanSelect(type, App.Settings?.Current?.PlayerLevel ?? 0);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = Loc.Get(CompanionPerks.NameKey(type)), FontWeight = FontWeights.SemiBold, FontSize = 16 });
            text.Children.Add(new TextBlock { Text = Loc.Get(perk.LocKey), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,5,0,0) });
            if (!unlocked) text.Children.Add(new TextBlock { Text = Loc.GetF("companion_perk_unlock", CompanionPerks.RequiredLevel(type)), Margin = new Thickness(0,5,0,0) });
            var choice = new RadioButton { Content = text, IsChecked = App.Companion?.ActivePerk == type,
                IsEnabled = unlocked,
                Foreground = perk.Negative ? Brushes.Salmon : Brushes.White, Padding = new Thickness(10),
                Margin = new Thickness(0,0,0,10), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            choice.Checked += (_, _) => { if (App.Companion?.SetPerk(type) == true) window.Close(); };
            panel.Children.Add(choice);
        }
        window.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) window.Close(); };
        window.ShowDialog();
    }
}
