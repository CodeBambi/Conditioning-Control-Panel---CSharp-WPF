using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Views.Controls.Companion.V2;

/// <summary>Shows the exact rolling context that can accompany a later message.</summary>
internal sealed class ConversationRecap : StackPanel
{
    internal ConversationRecap()
    {
        var quotes = App.Brain?.SummaryQuotes;
        if (quotes == null || quotes.Count == 0) { Visibility = Visibility.Collapsed; return; }
        Margin = new Thickness(0,0,0,18);
        Children.Add(new TextBlock { Text = Loc.Get("companion_v2_recap"), FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,8) });
        foreach (var quote in quotes)
            Children.Add(new TextBox { Text = quote, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), Margin = new Thickness(0,0,0,8) });
        var clear = new Button { Content = Loc.Get("companion_v2_recap_clear"), HorizontalAlignment = HorizontalAlignment.Left };
        clear.Click += (_, _) => { App.Brain?.ClearSummary(); Visibility = Visibility.Collapsed; };
        Children.Add(clear);
    }
}
