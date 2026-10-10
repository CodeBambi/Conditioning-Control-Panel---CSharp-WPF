using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// Draws one companion line into a TextBlock the way WPF <c>BuildLinkedInlines</c> does: plain
    /// Runs plus pink underlined link Runs. Avalonia has no Hyperlink inline, and an
    /// InlineUIContainer is one unbreakable box (a long title would stop wrapping), so the links
    /// stay ordinary Runs and the click is resolved by hit-testing the TextBlock's own layout
    /// against the character ranges kept here. Wrapping is untouched.
    /// </summary>
    internal static class CompanionLinkedText
    {
        /// <summary>WPF's link colour: Color.FromRgb(255, 176, 224).</summary>
        internal static readonly IBrush LinkBrush = new SolidColorBrush(Color.FromRgb(255, 176, 224)).ToImmutable();

        internal readonly record struct LinkRange(int Start, int Length, string Url);

        private static readonly AttachedProperty<IReadOnlyList<LinkRange>?> RangesProperty =
            AvaloniaProperty.RegisterAttached<TextBlock, IReadOnlyList<LinkRange>?>("Ranges", typeof(CompanionLinkedText));

        private static readonly AttachedProperty<bool> HookedProperty =
            AvaloniaProperty.RegisterAttached<TextBlock, bool>("Hooked", typeof(CompanionLinkedText));

        /// <summary>
        /// The companion's line, for a chat log row: bind it and the row renders linked.
        /// (WPF: Tag + ChatHistoryText_Loaded.)
        /// </summary>
        public static readonly AttachedProperty<string?> TextProperty =
            AvaloniaProperty.RegisterAttached<TextBlock, string?>("Text", typeof(CompanionLinkedText));

        public static string? GetText(TextBlock tb) => tb.GetValue(TextProperty);
        public static void SetText(TextBlock tb, string? value) => tb.SetValue(TextProperty, value);

        static CompanionLinkedText()
        {
            TextProperty.Changed.AddClassHandler<TextBlock>((tb, e) => Apply(tb, e.NewValue as string));
            // A linked block whose plain Text is written again (the bubble starts typing its next
            // line, the thinking dots) is no longer showing that line: its links go with it.
            TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((tb, _) => Clear(tb));
        }

        /// <summary>WPF's link table: live mod pool + known titles + the built-in catalogue.</summary>
        internal static Dictionary<string, string> CurrentTable()
        {
            try
            {
                return CompanionLinkSegments.BuildTable(
                    CoreMods.Service?.GetVideoLinks(),
                    CoreModsHooks.KnownVideoLinksProvider?.Invoke(),
                    HypnotubeDefaultLinks.KnownVideoTitles);
            }
            catch (Exception ex)
            {
                Log.Warning("Companion link table failed: {Type}", ex.GetType().Name);
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>Write <paramref name="text"/> into <paramref name="tb"/> with its links live.</summary>
        internal static IReadOnlyList<LinkRange> Apply(TextBlock tb, string? text, IReadOnlyDictionary<string, string>? table = null)
        {
            var segments = CompanionLinkSegments.Parse(text, table ?? CurrentTable());
            var ranges = new List<LinkRange>();

            if (!segments.Any(s => s.IsLink))
            {
                // Nothing to link: plain Text, exactly as before.
                tb.Inlines?.Clear();
                tb.Text = string.Concat(segments.Select(s => s.Text));
                tb.SetValue(RangesProperty, null);
                return ranges;
            }

            var inlines = new InlineCollection();
            int at = 0;
            foreach (var s in segments)
            {
                if (s.IsLink)
                {
                    inlines.Add(new Run(s.Text) { Foreground = LinkBrush, TextDecorations = TextDecorations.Underline });
                    ranges.Add(new LinkRange(at, s.Text.Length, s.Url!));
                    Log.Information("Auto-linked video on {Host} ({Chars} chars of link text)",
                        ConditioningControlPanel.Services.Logging.UrlLog.Host(s.Url), s.Text.Length);
                }
                else inlines.Add(new Run(s.Text));
                at += s.Text.Length;
            }
            tb.Inlines = inlines;
            tb.SetValue(RangesProperty, ranges);
            Hook(tb);
            return ranges;
        }

        /// <summary>Drop the links (the bubble is about to type a new line as plain text).</summary>
        internal static void Clear(TextBlock tb)
        {
            if (tb.GetValue(RangesProperty) == null) return;
            tb.SetValue(RangesProperty, null);
            tb.Inlines?.Clear();
            tb.Cursor = null;
        }

        internal static IReadOnlyList<LinkRange> RangesOf(TextBlock tb) =>
            tb.GetValue(RangesProperty) ?? (IReadOnlyList<LinkRange>)Array.Empty<LinkRange>();

        /// <summary>The link under a point in the TextBlock's own coordinates, or null.</summary>
        internal static string? UrlAt(TextBlock tb, Point point)
        {
            var ranges = tb.GetValue(RangesProperty);
            if (ranges == null || ranges.Count == 0) return null;
            try
            {
                var p = new Point(point.X - tb.Padding.Left, point.Y - tb.Padding.Top);
                var hit = tb.TextLayout.HitTestPoint(p);
                if (!hit.IsInside) return null;
                return UrlAtIndex(ranges, hit.TextPosition);
            }
            catch { return null; }
        }

        internal static string? UrlAtIndex(IReadOnlyList<LinkRange> ranges, int index)
        {
            foreach (var r in ranges)
                if (index >= r.Start && index < r.Start + r.Length) return r.Url;
            return null;
        }

        private static void Hook(TextBlock tb)
        {
            if (tb.GetValue(HookedProperty)) return;
            tb.SetValue(HookedProperty, true);
            tb.PointerMoved += (s, e) =>
            {
                if (s is not TextBlock t) return;
                var over = UrlAt(t, e.GetPosition(t)) != null;
                t.Cursor = over ? new Cursor(StandardCursorType.Hand) : null;
            };
            tb.PointerExited += (s, _) => { if (s is TextBlock t) t.Cursor = null; };
            tb.PointerPressed += (s, e) =>
            {
                if (s is not TextBlock t || !e.GetCurrentPoint(t).Properties.IsLeftButtonPressed) return;
                var url = UrlAt(t, e.GetPosition(t));
                if (url == null) return;
                e.Handled = true;   // a link press is never a bubble drag or a dismiss
                CompanionLinkLauncher.Open(url);
            };
        }
    }
}
