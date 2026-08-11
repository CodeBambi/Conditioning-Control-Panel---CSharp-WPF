using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// Z2's "History" chip: a read-only viewer over the persisted conversation
    /// (<c>companion\session.json</c>), exactly as design §3 Z2 specifies.
    ///
    /// <para><b>Read-only, and only dialogue.</b> It shows what
    /// <see cref="ICompanionSessionStore.Load"/> hands back, which by construction is UserChat and
    /// AssistantChat turns and nothing else — ambient events, ambient replies and bark echoes are
    /// never persisted. So this window cannot leak browsing commentary into a transcript, and it
    /// cannot show a turn that moderation rolled back: a refused turn never reached the file.</para>
    ///
    /// <para><b>Built in code, on purpose.</b> A dialog with one list in it does not need a XAML
    /// file, a design-time instance or an entry in the theme, and every one of those would be a
    /// place for a duplicate resource key to land.</para>
    /// </summary>
    internal sealed class CompanionTranscriptWindow : Window
    {
        private CompanionTranscriptWindow(IReadOnlyList<CompanionTurn> turns)
        {
            Title = Loc.Get("companion_chat_history_title");
            Width = 560;
            Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x12, 0x1F));
            ShowInTaskbar = false;

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var heading = new TextBlock
            {
                Text = Loc.Get("companion_chat_history_title"),
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8F, 0xD0)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            Grid.SetRow(heading, 0);
            root.Children.Add(heading);

            var body = new StackPanel();
            if (turns.Count == 0)
            {
                body.Children.Add(new TextBlock
                {
                    Text = Loc.Get("companion_chat_history_empty"),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x88, 0xAD)),
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 14, 0, 0)
                });
            }
            else
            {
                foreach (var turn in turns) body.Children.Add(BuildRow(turn));
            }

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = body,
                Margin = new Thickness(0, 8, 0, 8)
            };
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

        private static FrameworkElement BuildRow(CompanionTurn turn)
        {
            bool mine = turn.Kind == TurnKind.UserChat;
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
            panel.Children.Add(new TextBlock
            {
                Text = mine
                    ? Loc.Get("companion_chat_history_you")
                    : Loc.Get("companion_chat_history_her"),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(mine
                    ? Color.FromRgb(0x8F, 0xB4, 0xD9)
                    : Color.FromRgb(0xFF, 0x8F, 0xD0))
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

        /// <summary>
        /// Opens the viewer over <paramref name="owner"/>. Never throws: a broken or missing
        /// session file simply shows the empty state, because the History chip must not be a way to
        /// crash the tab.
        /// </summary>
        public static void ShowFor(Window? owner)
        {
            IReadOnlyList<CompanionTurn> turns = Array.Empty<CompanionTurn>();
            try
            {
                turns = new CompanionSessionStore().Load().Turns;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Companion room: transcript load failed");
            }

            var window = new CompanionTranscriptWindow(turns);
            if (owner != null && !ReferenceEquals(owner, window)) window.Owner = owner;
            window.ShowDialog();
        }
    }
}
