// PORTED from WPF 7.1.5 ConditioningControlPanel/Views/Controls/Companion/V2/PreferredNameEditor.cs and
// ConversationRecap.cs: the name the companion calls you (an explicit local preference, independent of
// automatic memory extraction) and the rolling recap that can accompany a later message. Both sit on
// Companion > AI above the memory wall. Deviation: no AutomationLiveSetting (Avalonia has none).
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion.Brain;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;

/// <summary>An explicit local preference, independent of automatic memory extraction.</summary>
internal sealed class PreferredNameEditor : StackPanel
{
    private readonly IMemoryStore? _owner;
    private readonly TextBox _name;
    private readonly TextBlock _notice;

    public PreferredNameEditor()
    {
        Margin = new Thickness(0, 0, 0, 16);
        Children.Add(new TextBlock { Text = Loc.Get("companion_v2_calls_you"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var row = new DockPanel();
        var save = new Button { Content = new TextBlock { Text = Loc.Get("companion_memory_edit_save") }, Margin = new Thickness(10, 0, 0, 0), MinWidth = 70 };
        DockPanel.SetDock(save, Dock.Right);
        row.Children.Add(save);
        _name = new TextBox { Padding = new Thickness(10, 7, 10, 7), Background = new SolidColorBrush(Color.FromRgb(37, 26, 48)), Foreground = Brushes.White };
        AutomationProperties.SetName(_name, Loc.Get("companion_v2_calls_you"));
        App.Brain?.EnsureCurrentAccount();
        var store = App.Brain?.Memory;
        if (store?.Profile.TryGetValue(MemoryStore.KeyPreferredName, out var value) == true) _name.Text = value?.ToString() ?? string.Empty;
        _owner = store;
        save.IsEnabled = _name.IsEnabled = store != null;
        save.Click += (_, _) => Save();
        row.Children.Add(_name);
        Children.Add(row);
        Children.Add(new TextBlock { Text = Loc.Get("companion_v2_name_hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), FontSize = 11 });
        _notice = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), FontSize = 11 };
        Children.Add(_notice);
    }

    internal static string Normalize(string? input)
    {
        var clean = Regex.Replace(input ?? string.Empty, @"\s+", " ").Trim();
        var elements = StringInfo.ParseCombiningCharacters(clean);
        return elements.Length > 64 ? clean[..elements[64]] : clean;
    }

    private void Save()
    {
        App.Brain?.EnsureCurrentAccount();
        var store = App.Brain?.Memory;
        if (store == null || !ReferenceEquals(store, _owner)) { _name.Text = string.Empty; IsEnabled = false; return; }
        var name = Normalize(_name.Text);
        if (!MemoryStore.IsStorable(ConditioningControlPanel.CoreModerationLog.Guard, name, global::ConditioningControlPanel.Services.Companion.Brain.MemoryFact.SourceUserEdited))
        {
            _name.Text = string.Empty;
            _notice.Text = Loc.Get("companion_v2_name_refused");
            return;
        }
        store.UpdateProfileSignal(MemoryStore.KeyPreferredName, name.Length == 0 ? null : name);
        _name.Text = name;
        _notice.Text = Loc.Get("companion_v2_name_saved");
    }
}

/// <summary>Shows the exact rolling context that can accompany a later message.</summary>
internal sealed class ConversationRecap : StackPanel
{
    internal ConversationRecap()
    {
        App.Brain?.EnsureCurrentAccount();
        var owner = App.Brain?.Memory;
        var quotes = App.Brain?.SummaryQuotes;
        if (quotes == null || quotes.Count == 0) { IsVisible = false; return; }
        Margin = new Thickness(0, 0, 0, 18);
        Children.Add(new TextBlock { Text = Loc.Get("companion_v2_recap"), FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var quote in quotes)
            Children.Add(new SelectableTextBlock { Text = quote, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8) });
        var clear = new Button { Content = new TextBlock { Text = Loc.Get("companion_v2_recap_clear") }, HorizontalAlignment = HorizontalAlignment.Left };
        clear.Click += (_, _) =>
        {
            App.Brain?.EnsureCurrentAccount();
            if (ReferenceEquals(owner, App.Brain?.Memory)) App.Brain?.ClearSummary();
            IsVisible = false;
        };
        Children.Add(clear);
    }
}
