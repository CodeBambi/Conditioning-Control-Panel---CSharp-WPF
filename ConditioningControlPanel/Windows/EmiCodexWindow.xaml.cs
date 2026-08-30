using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel
{
    /// <summary>
    /// THE PLAIN COPY of the book: the native fail-soft body of <see cref="EmiCodex"/>.
    ///
    /// <para>Four faults land here and they are all the same fault to the reader - no WebView2
    /// runtime, no bundle in this build, a navigation that reported failure, a browser process that
    /// died. In every one of them this window reads the SAME <c>chapters/*.json</c> the hosted page
    /// reads and lays them out as scrolling text, with the website manual one click away. There is
    /// no state in which the book is an empty hole.</para>
    ///
    /// <para><b>It renders nothing it does not understand as nothing.</b> The figure vocabulary is
    /// CSS in the page and cannot exist here, so a figure becomes its caption; a block type from a
    /// later wave becomes a paragraph. Losing the drawing is acceptable, losing the sentence is
    /// not.</para>
    ///
    /// <para>The window is deliberately ordinary: opaque, titled, resizable, owned by main when
    /// there is one. It is not chrome and it is not her - she never comes out here.</para>
    /// </summary>
    public partial class EmiCodexWindow : Window
    {
        // ---- palette, literal on purpose (see the XAML header) ----------------------
        private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xE8));
        private static readonly Brush Quiet = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xBC));
        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0xFF, 0x9E, 0xCF));
        private static readonly Brush CalloutFill = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x36));
        private static readonly Brush CalloutEdge = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x60));
        private static readonly Brush LimitEdge = new SolidColorBrush(Color.FromRgb(0xC8, 0x7A, 0x4B));

        /// <summary>One row of the contents list. A volume header is a row with no chapter.</summary>
        private sealed record Row(CodexChapter? Chapter, string Text, bool IsHeader);

        private readonly List<Row> _rows = new();
        private bool _selecting;

        /// <param name="why">Which rung of the fail-soft ladder brought us here: <c>runtime</c>,
        /// <c>bundle</c>, <c>navigation</c>, <c>process</c> or <c>error</c>. It only ever changes
        /// the one explanatory line under the heading - the reader itself is identical.</param>
        public EmiCodexWindow(string? why = null)
        {
            InitializeComponent();

            try { WhyText.Text = ExplainFor(why); }
            catch (Exception ex) { Log.Debug(ex, "[EmiCodex] plain reader note failed"); }

            try { Build(); }
            catch (Exception ex)
            {
                // Even the reader fails soft. An exception here leaves the header, the website
                // button and the close button, which is still a window somebody can get out of.
                Log.Warning(ex, "[EmiCodex] plain reader failed to build its contents");
            }
        }

        /// <summary>
        /// The one honest sentence about why the book looks like this. Falls back to the generic
        /// note for a reason nobody has heard of, so a new rung on the ladder is never a blank.
        /// </summary>
        private static string ExplainFor(string? why) => (why ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "runtime" => EmiCodex.SafeLoc("emi_codex_why_runtime",
                "The illustrated book needs the WebView2 runtime, which is not installed here. This is the same text, plain."),
            "bundle" => EmiCodex.SafeLoc("emi_codex_why_bundle",
                "The illustrated book is not part of this build yet. This is the same text, plain."),
            "navigation" => EmiCodex.SafeLoc("emi_codex_why_navigation",
                "The illustrated book did not load. This is the same text, plain."),
            "process" => EmiCodex.SafeLoc("emi_codex_why_process",
                "The illustrated book stopped responding. This is the same text, plain."),
            _ => EmiCodex.SafeLoc("emi_codex_plain_note", "This is the book as plain text."),
        };

        // =====================================================================================
        //  contents
        // =====================================================================================

        private void Build()
        {
            var chapters = EmiCodex.ReadChapters();
            if (chapters.Count == 0)
            {
                ShowEmpty();
                return;
            }

            int volume = int.MinValue;
            foreach (var ch in chapters)
            {
                if (ch.Volume != volume)
                {
                    volume = ch.Volume;
                    _rows.Add(new Row(null, VolumeLabel(volume), true));
                }
                _rows.Add(new Row(ch, ch.DisplayTitle, false));
            }

            foreach (var row in _rows) ChapterList.Items.Add(BuildRow(row));

            // THE BOOKMARK. A bookmark pointing at a chapter that no longer exists is not an error
            // and not a reason to show nothing: fall through to the first chapter there is.
            int start = IndexOfChapter(EmiCodex.Bookmark);
            if (start < 0) start = _rows.FindIndex(r => !r.IsHeader);
            if (start >= 0) ChapterList.SelectedIndex = start;
        }

        private static string VolumeLabel(int volume)
        {
            string roman = volume switch
            {
                1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI",
                _ => volume.ToString(),
            };
            try { return Localization.Loc.GetF("emi_codex_volume", roman); }
            catch { return "Volume " + roman; }
        }

        private int IndexOfChapter(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return -1;
            return _rows.FindIndex(r => r.Chapter != null
                && string.Equals(r.Chapter.Id, id, StringComparison.Ordinal));
        }

        private static UIElement BuildRow(Row row)
        {
            if (row.IsHeader)
            {
                return new TextBlock
                {
                    Text = row.Text.ToUpperInvariant(),
                    Foreground = Quiet,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 10, 0, 2),
                    IsHitTestVisible = false,
                };
            }
            return new TextBlock
            {
                Text = row.Text,
                Foreground = Ink,
                TextWrapping = TextWrapping.Wrap,
            };
        }

        private void ChapterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_selecting) return;
            try
            {
                int i = ChapterList.SelectedIndex;
                if (i < 0 || i >= _rows.Count) return;

                // Volume headers are rows too, so that the list needs no grouping machinery. They
                // are not selectable content: step past one onto the chapter under it.
                if (_rows[i].IsHeader)
                {
                    int next = _rows.FindIndex(i + 1, r => !r.IsHeader);
                    if (next < 0) return;
                    _selecting = true;
                    try { ChapterList.SelectedIndex = next; }
                    finally { _selecting = false; }
                    i = next;
                }

                var ch = _rows[i].Chapter;
                if (ch == null) return;
                Render(ch);
                EmiCodex.NoteChapter(ch.Id);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiCodex] plain reader could not turn the page");
            }
        }

        // =====================================================================================
        //  one chapter
        // =====================================================================================

        private void ShowEmpty()
        {
            PageBody.Children.Clear();
            PageBody.Children.Add(new TextBlock
            {
                Text = EmiCodex.SafeLoc("emi_codex_empty",
                    "The book is not in this build. Everything in it is on the website manual."),
                Foreground = Quiet,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
            });
        }

        private void Render(CodexChapter ch)
        {
            PageBody.Children.Clear();
            try { PageScroll.ScrollToTop(); } catch { }

            PageBody.Children.Add(new TextBlock
            {
                Text = ch.DisplayTitle,
                Foreground = Accent,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            });

            if (!string.IsNullOrWhiteSpace(ch.Blurb))
            {
                PageBody.Children.Add(new TextBlock
                {
                    Text = ch.Blurb!.Trim(),
                    Foreground = Quiet,
                    FontSize = 13,
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }

            foreach (var block in ch.Blocks ?? new List<CodexBlock>())
            {
                try { AddBlock(block); }
                catch (Exception ex) { Log.Debug(ex, "[EmiCodex] a block would not render, skipped"); }
            }

            // Her one line in the margin, last, and clearly hers. She never explains the chapter,
            // so it reads as an aside rather than as a summary.
            var margin = ch.Margin;
            if (margin != null && !string.IsNullOrWhiteSpace(margin.T))
            {
                var text = margin.T!.Trim();
                if (!string.IsNullOrWhiteSpace(margin.Face)) text += "   " + margin.Face!.Trim();
                PageBody.Children.Add(new TextBlock
                {
                    Text = text,
                    Foreground = Accent,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 20, 0, 0),
                    Opacity = 0.9,
                });
            }
        }

        private void AddBlock(CodexBlock block)
        {
            var type = (block.Type ?? string.Empty).Trim().ToLowerInvariant();
            switch (type)
            {
                case "steps":
                    AddSteps(block);
                    return;

                case "figure":
                    // There is no figure here and there never will be: the vocabulary is CSS and
                    // SVG drawn inside the page. The caption is the part that carries meaning, so
                    // the caption is what survives.
                    if (!string.IsNullOrWhiteSpace(block.Caption)) AddQuiet(block.Caption!);
                    return;

                case "callout":
                    AddBoxed(block.Text, CalloutEdge, null);
                    return;

                case "limit":
                    AddBoxed(block.Text, LimitEdge,
                        EmiCodex.SafeLoc("emi_codex_limit_label", "Worth knowing"));
                    return;

                default:
                    // "p", and anything a later wave invents. A block type this build has never
                    // heard of still has words in it.
                    AddParagraph(block.Text);
                    return;
            }
        }

        private void AddParagraph(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            PageBody.Children.Add(new TextBlock
            {
                Text = text!.Trim(),
                Foreground = Ink,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                Margin = new Thickness(0, 14, 0, 0),
            });
        }

        private void AddQuiet(string text)
        {
            PageBody.Children.Add(new TextBlock
            {
                Text = text.Trim(),
                Foreground = Quiet,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 14, 0, 0),
            });
        }

        private void AddSteps(CodexBlock block)
        {
            var items = block.Items?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (items == null || items.Count == 0) return;

            var stack = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            for (int i = 0; i < items.Count; i++)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = (i + 1) + ".  " + items[i].Trim(),
                    Foreground = Ink,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20,
                    Margin = new Thickness(10, i == 0 ? 0 : 6, 0, 0),
                });
            }
            PageBody.Children.Add(stack);
        }

        private void AddBoxed(string? text, Brush edge, string? label)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var stack = new StackPanel();
            if (!string.IsNullOrWhiteSpace(label))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = label!.ToUpperInvariant(),
                    Foreground = edge,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 4),
                });
            }
            stack.Children.Add(new TextBlock
            {
                Text = text!.Trim(),
                Foreground = Ink,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
            });

            PageBody.Children.Add(new Border
            {
                Background = CalloutFill,
                BorderBrush = edge,
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 16, 0, 0),
                Child = stack,
            });
        }

        // =====================================================================================
        //  the two buttons
        // =====================================================================================

        private void ManualButton_Click(object sender, RoutedEventArgs e) => EmiCodex.OpenManualInBrowser();

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try { Close(); } catch (Exception ex) { Log.Debug(ex, "[EmiCodex] plain reader close failed"); }
        }
    }
}
