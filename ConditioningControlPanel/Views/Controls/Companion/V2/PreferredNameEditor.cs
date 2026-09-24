using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion.Brain;

namespace ConditioningControlPanel.Views.Controls.Companion.V2;

/// <summary>An explicit local preference, independent of automatic memory extraction.</summary>
internal sealed class PreferredNameEditor : StackPanel
{
    private readonly IMemoryStore? _owner;
    private readonly TextBox _name;
    private readonly TextBlock _notice;
    public PreferredNameEditor()
    {
        Margin = new Thickness(0, 0, 0, 16);
        Children.Add(new TextBlock { Text = Loc.Get("companion_v2_calls_you"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var row = new DockPanel();
        var save = new Button { Content = Loc.Get("companion_memory_edit_save"), Margin = new Thickness(10, 0, 0, 0), MinWidth = 70 };
        DockPanel.SetDock(save, Dock.Right);
        row.Children.Add(save);
        _name = new TextBox { Padding = new Thickness(10, 7, 10, 7), Background = new SolidColorBrush(Color.FromRgb(37, 26, 48)), Foreground = Brushes.White };
        System.Windows.Automation.AutomationProperties.SetName(_name, Loc.Get("companion_v2_calls_you"));
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
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_notice, System.Windows.Automation.AutomationLiveSetting.Polite);
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
        if (store == null || !ReferenceEquals(store, _owner)) { _name.Clear(); IsEnabled = false; return; }
        var name = Normalize(_name.Text);
        if (!MemoryStore.IsStorable(App.ModerationGuard, name, MemoryFact.SourceUserEdited))
        {
            _name.Clear();
            _notice.Text = Loc.Get("companion_v2_name_refused");
            return;
        }
        store.UpdateProfileSignal(MemoryStore.KeyPreferredName, name.Length == 0 ? null : name);
        _name.Text = name;
        _notice.Text = Loc.Get("companion_v2_name_saved");
    }
}
