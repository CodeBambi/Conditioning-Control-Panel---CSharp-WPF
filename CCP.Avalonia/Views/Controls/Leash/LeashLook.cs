// PORTED from WPF 7.1.5 Controls/Leash/LeashLook.cs (the parts the drawer section uses): the
// leash colours, the chunky plate button, the caption and the segmented switch. Flat on this head:
// no Effect, no bevel (Avalonia effect/cache rule); the drawer palette is FriendsDrawer's.
// ponytail: Chain, HeartTag, StickerDisc, WeekCell, Icon glyph art and LeashLook.Help ("?").
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal static class LeashLook
{
    internal enum Tone { Gold, Mint, Pink, Ghost }

    private static IBrush Rgb(byte r, byte g, byte b, byte a = 0xFF) => new SolidColorBrush(Color.FromArgb(a, r, g, b));

    internal static readonly IBrush CardBrush = Rgb(0x24, 0x12, 0x40), SelfCardBrush = Rgb(0x2A, 0x10, 0x3A),
        GoldDeepBrush = Rgb(0x8A, 0x63, 0x1E), MintDeepBrush = Rgb(0x1E, 0x7A, 0x5E), InkBrush = Rgb(0x0B, 0x07, 0x16),
        SelfBorderBrush = Rgb(0xFF, 0x5F, 0xB4, 0x88), GoldWashBrush = Rgb(0xFF, 0xCF, 0x6B, 0x22), GlassBrush = Rgb(0x1A, 0x0E, 0x2E, 0xF2);

    /// <summary>WPF LeashLook.Chunky: a filled plate button with ink text.</summary>
    internal static Button Chunky(string label, Tone tone, string tag, double size = 13)
    {
        var (bg, fg, edge) = tone switch
        {
            Tone.Mint => (FriendsDrawer.Mint, FriendsDrawer.MintInk, MintDeepBrush),
            Tone.Pink => (FriendsDrawer.Pink, InkBrush, FriendsDrawer.Pink),
            Tone.Ghost => ((IBrush)Brushes.Transparent, FriendsDrawer.Text, FriendsDrawer.Line2),
            _ => (FriendsDrawer.Gold, InkBrush, GoldDeepBrush),
        };
        var b = FriendsDrawer.Pill(new TextBlock { Text = label, FontFamily = FriendsDrawer.Display, FontSize = size, FontWeight = FontWeight.SemiBold },
            bg, fg, tag, edge);
        b.CornerRadius = new CornerRadius(10);
        b.Padding = new Thickness(12, 5, 12, 5);
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.Cursor = FriendsDrawer.Hand();
        return b;
    }

    /// <summary>WPF LeashLook.Caption: a small muted heading over a switch.</summary>
    internal static TextBlock Caption(string text, IBrush? fg = null)
    {
        var t = FriendsDrawer.Label(text, 11, fg ?? FriendsDrawer.Muted, FriendsDrawer.Display, FontWeight.Medium);
        t.TextWrapping = TextWrapping.Wrap;
        t.TextTrimming = TextTrimming.None;
        return t;
    }

    internal static TextBlock Wrap(TextBlock t) { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; return t; }

    /// <summary>WPF LeashLook.Segmented: one row of equal choices, the picked one lit. -1 lights none.
    /// Each segment is tagged <c>{tag}:{index}</c>, the lit one <c>{tag}:{index}:on</c>.</summary>
    internal static Grid Segmented(IReadOnlyList<string> labels, int selected, Action<int> pick, string tag, double size = 12)
    {
        var g = new Grid { Tag = tag };
        for (int i = 0; i < labels.Count; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            int idx = i;
            bool on = i == selected;
            var b = FriendsDrawer.Pill(new TextBlock { Text = labels[i], FontFamily = FriendsDrawer.Display, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis },
                on ? FriendsDrawer.Lilac : FriendsDrawer.Raised, on ? InkBrush : FriendsDrawer.Muted,
                tag + ":" + i + (on ? ":on" : ""), on ? FriendsDrawer.Lilac : FriendsDrawer.Line2);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.Padding = new Thickness(4, 4, 4, 4);
            b.Margin = new Thickness(i == 0 ? 0 : 2, 0, i == labels.Count - 1 ? 0 : 2, 0);
            b.Cursor = FriendsDrawer.Hand();
            b.Click += (_, _) => pick(idx);
            Grid.SetColumn(b, i);
            g.Children.Add(b);
        }
        return g;
    }
}
