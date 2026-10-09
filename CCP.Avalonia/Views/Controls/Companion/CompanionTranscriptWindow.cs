using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion.Brain;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// The chat zone's "History" link: the whole stored transcript, read-only. Port of WPF
    /// Runtime/CompanionTranscriptWindow.cs - same layout, colours and strings, built in code there too.
    /// </summary>
    internal sealed class CompanionTranscriptWindow : Window
    {
        /// <summary>Render constructor (--render-all): the empty transcript.</summary>
        internal CompanionTranscriptWindow() : this(Array.Empty<CompanionTurn>()) { }

        internal CompanionTranscriptWindow(IReadOnlyList<CompanionTurn> turns)
        {
            Title = Loc.Get("companion_chat_history_title");
            Width = 560;
            Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x12, 0x1F));
            ShowInTaskbar = false;

            var root = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,*,Auto") };

            root.Children.Add(new TextBlock
            {
                Text = Loc.Get("companion_chat_history_title"),
                FontSize = 17,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8F, 0xD0)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            var body = new StackPanel();
            if (turns.Count == 0)
            {
                body.Children.Add(new TextBlock
                {
                    Text = Loc.Get("companion_chat_history_empty"),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x88, 0xAD)),
                    FontStyle = FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 14, 0, 0)
                });
            }
            else
            {
                foreach (var turn in turns) body.Children.Add(BuildRow(turn));
            }

            var scroll = new ScrollViewer { Content = body, Margin = new Thickness(0, 8, 0, 8) };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            var note = new TextBlock
            {
                Text = Loc.Get("companion_memory_storage_note"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x64, 0x86)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(note, 2);
            root.Children.Add(note);

            Content = root;
        }

        private static Control BuildRow(CompanionTurn turn)
        {
            bool mine = turn.Kind == TurnKind.UserChat;
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 9), HorizontalAlignment = HorizontalAlignment.Stretch };
            panel.Children.Add(new TextBlock
            {
                Text = mine ? Loc.Get("companion_chat_history_you") : Loc.Get("companion_chat_history_her"),
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(mine ? Color.FromRgb(0x8F, 0xB4, 0xD9) : Color.FromRgb(0xFF, 0x8F, 0xD0))
            });
            panel.Children.Add(new TextBlock
            {
                Text = turn.Text,
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD2, 0xEA)),
                TextWrapping = TextWrapping.Wrap
            });
            return panel;
        }

        /// <summary>Test seam: what was opened last (null until History is clicked).</summary>
        internal static CompanionTranscriptWindow? LastShown { get; private set; }

        /// <summary>WPF ShowFor: load the stored session (a failed load shows the empty state) and open modal.</summary>
        internal static void ShowFor(Window? owner)
        {
            IReadOnlyList<CompanionTurn> turns = Array.Empty<CompanionTurn>();
            try { turns = new CompanionSessionStore().Load().Turns; }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Companion room: transcript load failed"); }

            var window = new CompanionTranscriptWindow(turns);
            LastShown = window;
            _ = window.ShowDialogSafe(owner);
        }
    }
}
