// PORTED from WPF 7.1.5 ConditioningControlPanel/Views/Controls/Companion/Pages/CompanionPageHost.cs, plus
// the page chrome every Companion section page shares (WPF Type.PageTitle / Type.PageSubtitle and the
// #14FFFFFF card the four page XAML files repeat). This head has no Type.* scale, so the numbers are
// the WPF style setters, kept in one place.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Localization;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Tabs;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages
{
    /// <summary>
    /// The Companion section pages (Personality, Permissions, Links, AI) host the LIVE controls the
    /// v2 conversation page collapsed away with the old room: the room's zones and Workshop cells,
    /// never copies, so there is one instance of every control on screen.
    /// </summary>
    internal static class CompanionPageHost
    {
        /// <summary>Takes <paramref name="element"/> off whatever holds it and puts it in <paramref name="host"/>.</summary>
        public static void Adopt(Control element, ContentControl host)
        {
            if (ReferenceEquals(host.Content, element)) return;
            Detach(element);
            host.Content = element;
        }

        public static void Detach(Control element)
        {
            switch (element.Parent)
            {
                case Panel panel: panel.Children.Remove(element); break;
                case ContentControl control when ReferenceEquals(control.Content, element): control.Content = null; break;
                case Decorator decorator: decorator.Child = null; break;
                case ContentPresenter presenter: presenter.Content = null; break;
            }
            // A cell handed to a templated ContentPresenter may have only a visual parent.
            switch (element.GetVisualParent())
            {
                case ContentPresenter presenter: presenter.Content = null; break;
                case Panel panel: panel.Children.Remove(element); break;
            }
        }

        /// <summary>The Companion tab, which owns the collapsed room.</summary>
        public static CompanionTabView? Tab(Windows.MainShellWindow? owner) =>
            owner?.Named<CompanionTabView>("CompanionTab");

        /// <summary>The collapsed room, which still owns the zones until a page adopts them.</summary>
        public static CompanionRoomView? Room(Windows.MainShellWindow? owner) => Tab(owner)?.RoomView;

        /// <summary>The Workshop's six live cells (WPF tab.Vm.Shelf).</summary>
        public static WorkshopShelfParts? Shelf(Windows.MainShellWindow? owner) =>
            (Room(owner)?.FindControl<WorkshopAccordion>("WorkshopZone")?.DataContext as WorkshopRuntimeVm)?.Parts;

        public static Windows.MainShellWindow? ShellOf(Visual v) => TopLevel.GetTopLevel(v) as Windows.MainShellWindow;

        // ---- the shared page chrome ----

        public static TextBlock Loc(TextBlock tb, string key)
        {
            tb.Bind(TextBlock.TextProperty, (Binding)new StrExtension(key).ProvideValue(null!));
            return tb;
        }

        /// <summary>WPF Type.PageTitle: Fredoka 26 SemiBold, TextLight, one line.</summary>
        public static TextBlock PageTitle(string key)
        {
            var tb = new TextBlock
            {
                FontFamily = new FontFamily("Fredoka, Segoe UI"), FontSize = 26, FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            tb[!TextBlock.ForegroundProperty] = tb.GetResourceObservable("TextLightBrush").ToBinding();
            return Loc(tb, key);
        }

        /// <summary>WPF Type.PageSubtitle: 13.5, TextSecondary, wraps.</summary>
        public static TextBlock PageSubtitle(string key)
        {
            var tb = new TextBlock { FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 16) };
            tb[!TextBlock.ForegroundProperty] = tb.GetResourceObservable("TextSecondaryBrush").ToBinding();
            return Loc(tb, key);
        }

        /// <summary>The pink 15 px SemiBold card heading the pages use.</summary>
        public static TextBlock CardHeading(string key) =>
            Loc(new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)) }, key);

        /// <summary>WPF Type.CardTitle: 18 Bold, TextLight.</summary>
        public static TextBlock CardTitle(string key)
        {
            var tb = new TextBlock { FontSize = 18, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
            tb[!TextBlock.ForegroundProperty] = tb.GetResourceObservable("TextLightBrush").ToBinding();
            return Loc(tb, key);
        }

        /// <summary>A 12 px TextSecondary note under a heading.</summary>
        public static TextBlock CardNote(string key, double fontSize = 12, Thickness? margin = null)
        {
            var tb = new TextBlock { FontSize = fontSize, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0, 2, 0, 10) };
            tb[!TextBlock.ForegroundProperty] = tb.GetResourceObservable("TextSecondaryBrush").ToBinding();
            return Loc(tb, key);
        }

        /// <summary>The #14FFFFFF card, corner 10, padding 16.</summary>
        public static Border Card(Control child, Thickness? margin = null) => new()
        {
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Margin = margin ?? new Thickness(0, 0, 0, 16),
            Child = child,
        };

        /// <summary>An empty host a live zone or cell is adopted into.</summary>
        public static ContentControl Host(string name, Thickness? margin = null) => new()
        {
            Name = name, Focusable = false, Margin = margin ?? default,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };

        /// <summary>The page scroller: a capped, centred column, as every page XAML does.</summary>
        public static ScrollViewer Page(double maxWidth, params Control[] children)
        {
            var stack = new StackPanel { Margin = new Thickness(24, 16, 24, 24), MaxWidth = maxWidth, HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var c in children) stack.Children.Add(c);
            return new ScrollViewer
            {
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Focusable = false,
                Content = stack,
            };
        }

        /// <summary>The pink page button (WPF PersonalityPage's PageButton style).</summary>
        public static Button PageButton(string key, EventHandler<global::Avalonia.Interactivity.RoutedEventArgs> click)
        {
            var b = new Button
            {
                Content = Loc(new TextBlock(), key),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x69, 0xB4)),
                Padding = new Thickness(14, 7),
                CornerRadius = new CornerRadius(6),
                Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center,
            };
            b.Click += click;
            return b;
        }
    }
}
