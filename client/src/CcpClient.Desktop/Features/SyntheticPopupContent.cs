using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CcpClient.Desktop.Features;

/// <summary>
/// Synthetic demonstrator content for the feature popup (board row "Prove feature-popup
/// scrolling"). NOT product settings — the first real feature popup supersedes the
/// demonstrator. Variants: TALL (forces capping; final control below the fold), SHORT
/// (compact, no scrollbar), NESTED (inner list scrolls itself, then chains to the popup —
/// ScrollViewer.IsScrollChainingEnabled defaults true in 12.1.0).
/// </summary>
public static class SyntheticPopupContent
{
    /// <summary>UIA needle for the bottom-most control of tall/nested content.</summary>
    public const string FinalControlName = "popup final control";

    private static readonly IBrush RowBrush = new SolidColorBrush(Color.Parse("#FFE8E0EE"));

    public static Control BuildTall()
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(Row("TALL variant — 30 synthetic rows force working-area capping; the final control starts below the fold."));
        for (var i = 1; i <= 30; i++)
        {
            panel.Children.Add(i % 5 == 0
                ? (Control)new CheckBox { Content = $"Synthetic toggle row {i}", Foreground = RowBrush }
                : Row($"Synthetic setting row {i} (demonstrator content)"));
        }

        panel.Children.Add(FinalButton());
        return panel;
    }

    public static Control BuildShort()
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(Row("SHORT variant — three rows; the popup stays compact and no scrollbar appears."));
        panel.Children.Add(Row("Synthetic setting A"));
        panel.Children.Add(new CheckBox { Content = "Synthetic toggle B", Foreground = RowBrush });
        return panel;
    }

    public static Control BuildNested()
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(Row("NESTED variant — the inner list scrolls itself while it can, then chains remaining wheel movement to the popup."));
        var list = new ListBox { Height = 200 };
        for (var i = 1; i <= 40; i++)
        {
            list.Items.Add(new ListBoxItem { Content = $"Inner list item {i}" });
        }

        panel.Children.Add(list);
        for (var i = 1; i <= 12; i++)
        {
            panel.Children.Add(Row($"Row {i} below the nested list"));
        }

        panel.Children.Add(FinalButton());
        return panel;
    }

    private static TextBlock Row(string text) => new()
    {
        Text = text,
        Foreground = RowBrush,
        TextWrapping = TextWrapping.Wrap,
    };

    private static Button FinalButton()
    {
        var button = new Button
        {
            Content = "Final control (below the fold)",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Avalonia.Thickness(0, 12, 0, 0),
        };
        AutomationProperties.SetName(button, FinalControlName);
        return button;
    }
}
