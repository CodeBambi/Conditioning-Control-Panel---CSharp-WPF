using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// THE PLAIN COPY of the book: the native fail-soft body of EMI's codex.
    ///
    /// <para>Four faults land here and they are all the same fault to the reader - no web view
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
    /// <para>PORTED from ConditioningControlPanel/Windows/EmiCodexWindow.xaml.cs. It hosts no web
    /// view - it is the thing that runs when the web view cannot - so nothing here talks to
    /// <c>controls:WebHost</c>. Deviations, all forced:</para>
    /// <list type="bullet">
    ///   <item>The whole <c>EmiCodex</c> service stays in the WPF head (it is built on
    ///     Microsoft.Web.WebView2), so <see cref="Codex"/> below stubs three of the four calls this
    ///     window makes, sample chapters included. The fourth - opening the manual - is not stubbed:
    ///     its WPF body is a ShellExecute, which is xdg-open here, and it ports verbatim. Every
    ///     layout path - headers, blurb, steps, figure captions, callouts, limits, the margin line -
    ///     is the real one.</item>
    ///   <item>The <c>CodexChapter</c>/<c>CodexBlock</c>/<c>CodexMargin</c> records are copied from
    ///     Services/EmiDesk/EmiCodex.cs minus their <c>[JsonProperty]</c> attributes: this head
    ///     never deserialises them, and Newtonsoft is a Core reference, not one of ours.</item>
    ///   <item><c>ScrollToTop()</c> -&gt; <c>ScrollToHome()</c>; <c>FontStyles</c>/<c>FontWeights</c>
    ///     -&gt; <c>FontStyle</c>/<c>FontWeight</c>; <c>Brush</c> -&gt; <c>IBrush</c>.</item>
    ///   <item>The defaulted <c>why</c> parameter is split into two constructors: a defaulted one is
    ///     still a parameter, so <c>--render-all</c>'s parameterless lookup would not find it.</item>
    ///   <item><c>SelectionChanged</c> is wired in the constructor rather than in markup.</item>
    /// </list>
    ///
    /// <para>The window is deliberately ordinary: opaque, titled, resizable, owned by main when
    /// there is one. It is not chrome and it is not her - she never comes out here.</para>
    /// </summary>
    public partial class EmiCodexWindow : Window
    {
        // ---- palette, literal on purpose (see the XAML header) ----------------------
        private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xE8));
        private static readonly IBrush Quiet = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xBC));
        private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0xFF, 0x9E, 0xCF));
        private static readonly IBrush CalloutFill = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x36));
        private static readonly IBrush CalloutEdge = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x60));
        private static readonly IBrush LimitEdge = new SolidColorBrush(Color.FromRgb(0xC8, 0x7A, 0x4B));

        /// <summary>One row of the contents list. A volume header is a row with no chapter.</summary>
        private sealed record Row(CodexChapter? Chapter, string Text, bool IsHeader);

        private readonly List<Row> _rows = new();
        private bool _selecting;

        private readonly TextBlock _whyText;
        private readonly ListBox _chapterList;
        private readonly ScrollViewer _pageScroll;
        private readonly StackPanel _pageBody;

        /// <summary>The generic note, which is also what <c>--render-all</c> constructs.</summary>
        public EmiCodexWindow() : this(null) { }

        /// <param name="why">Which rung of the fail-soft ladder brought us here: <c>runtime</c>,
        /// <c>bundle</c>, <c>navigation</c>, <c>process</c> or <c>error</c>. It only ever changes
        /// the one explanatory line under the heading - the reader itself is identical.</param>
        public EmiCodexWindow(string? why)
        {
            AvaloniaXamlLoader.Load(this);

            _whyText = this.FindControl<TextBlock>("WhyText")!;
            _chapterList = this.FindControl<ListBox>("ChapterList")!;
            _pageScroll = this.FindControl<ScrollViewer>("PageScroll")!;
            _pageBody = this.FindControl<StackPanel>("PageBody")!;

            _chapterList.SelectionChanged += ChapterList_SelectionChanged;
            this.FindControl<Button>("ManualButton")!.Click += (_, _) => Codex.OpenManualInBrowser();
            this.FindControl<Button>("CloseButton")!.Click += (_, _) =>
            {
                try { Close(); } catch (Exception ex) { Log.Debug(ex, "[EmiCodex] plain reader close failed"); }
            };

            try { _whyText.Text = ExplainFor(why); }
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
            "runtime" => Codex.SafeLoc("emi_codex_why_runtime",
                "The illustrated book needs the WebView2 runtime, which is not installed here. This is the same text, plain."),
            "bundle" => Codex.SafeLoc("emi_codex_why_bundle",
                "The illustrated book is not part of this build yet. This is the same text, plain."),
            "navigation" => Codex.SafeLoc("emi_codex_why_navigation",
                "The illustrated book did not load. This is the same text, plain."),
            "process" => Codex.SafeLoc("emi_codex_why_process",
                "The illustrated book stopped responding. This is the same text, plain."),
            _ => Codex.SafeLoc("emi_codex_plain_note", "This is the book as plain text."),
        };

        // =====================================================================================
        //  contents
        // =====================================================================================

        private void Build()
        {
            var chapters = Codex.ReadChapters();
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

            foreach (var row in _rows) _chapterList.Items.Add(BuildRow(row));

            // THE BOOKMARK. A bookmark pointing at a chapter that no longer exists is not an error
            // and not a reason to show nothing: fall through to the first chapter there is.
            int start = IndexOfChapter(Codex.Bookmark);
            if (start < 0) start = _rows.FindIndex(r => !r.IsHeader);
            if (start >= 0) _chapterList.SelectedIndex = start;
        }

        private static string VolumeLabel(int volume)
        {
            string roman = volume switch
            {
                1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI",
                _ => volume.ToString(),
            };
            try { return Loc.GetF("emi_codex_volume", roman); }
            catch { return "Volume " + roman; }
        }

        private int IndexOfChapter(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return -1;
            return _rows.FindIndex(r => r.Chapter != null
                && string.Equals(r.Chapter.Id, id, StringComparison.Ordinal));
        }

        private static Control BuildRow(Row row)
        {
            if (row.IsHeader)
            {
                return new TextBlock
                {
                    Text = row.Text.ToUpperInvariant(),
                    Foreground = Quiet,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
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

        private void ChapterList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_selecting) return;
            try
            {
                int i = _chapterList.SelectedIndex;
                if (i < 0 || i >= _rows.Count) return;

                // Volume headers are rows too, so that the list needs no grouping machinery. They
                // are not selectable content: step past one onto the chapter under it.
                if (_rows[i].IsHeader)
                {
                    int next = _rows.FindIndex(i + 1, r => !r.IsHeader);
                    if (next < 0) return;
                    _selecting = true;
                    try { _chapterList.SelectedIndex = next; }
                    finally { _selecting = false; }
                    i = next;
                }

                var ch = _rows[i].Chapter;
                if (ch == null) return;
                RenderChapter(ch);
                Codex.NoteChapter(ch.Id);
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
            _pageBody.Children.Clear();
            _pageBody.Children.Add(new TextBlock
            {
                Text = Codex.SafeLoc("emi_codex_empty",
                    "The book is not in this build. Everything in it is on the website manual."),
                Foreground = Quiet,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
            });
        }

        /// <summary>WPF called this <c>Render</c>; that name is taken on an Avalonia Control.</summary>
        private void RenderChapter(CodexChapter ch)
        {
            _pageBody.Children.Clear();
            try { _pageScroll.ScrollToHome(); } catch { }

            _pageBody.Children.Add(new TextBlock
            {
                Text = ch.DisplayTitle,
                Foreground = Accent,
                FontSize = 22,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            });

            if (!string.IsNullOrWhiteSpace(ch.Blurb))
            {
                _pageBody.Children.Add(new TextBlock
                {
                    Text = ch.Blurb!.Trim(),
                    Foreground = Quiet,
                    FontSize = 13,
                    FontStyle = FontStyle.Italic,
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
                _pageBody.Children.Add(new TextBlock
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
                        Codex.SafeLoc("emi_codex_limit_label", "Worth knowing"));
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
            _pageBody.Children.Add(new TextBlock
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
            _pageBody.Children.Add(new TextBlock
            {
                Text = text.Trim(),
                Foreground = Quiet,
                FontStyle = FontStyle.Italic,
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
            _pageBody.Children.Add(stack);
        }

        private void AddBoxed(string? text, IBrush edge, string? label)
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
                    FontWeight = FontWeight.SemiBold,
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

            _pageBody.Children.Add(new Border
            {
                Background = CalloutFill,
                BorderBrush = edge,
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = stack,
            });
        }
    }

    /// <summary>
    /// The four things this window asks of the book, and nothing else.
    ///
    /// ponytail: needs Services/EmiDesk/EmiCodex (WPF head), wired when it moves to Core. It cannot
    /// come over as-is - it is built on Microsoft.Web.WebView2 and System.Windows - so the reader
    /// gets a stub of its surface rather than a rewrite of its innards. The sample chapters are
    /// placeholder data, and exist so the render proof exercises every block type the reader draws.
    /// </summary>
    internal static class Codex
    {
        /// <summary>The website manual. Same constant as EmiCodex.ManualUrl.</summary>
        internal const string ManualUrl = "https://cclabs.app/guide.html";

        /// <summary>A localised string that can never throw and never comes back blank. Copied from
        /// EmiCodex.SafeLoc: the reader runs on paths where no language file has loaded yet.</summary>
        internal static string SafeLoc(string key, string fallback)
        {
            try
            {
                var s = Loc.Get(key);
                return string.IsNullOrWhiteSpace(s) || string.Equals(s, key, StringComparison.Ordinal) ? fallback : s;
            }
            catch { return fallback; }
        }

        /// <summary>ponytail: needs EmiCodex.ReadChapters (reads Resources/web/codex/chapters/*.json
        /// through the head's own paths), wired when it moves to Core. Placeholder chapters until
        /// then - two volumes, every block type once.</summary>
        internal static IReadOnlyList<CodexChapter> ReadChapters() => new List<CodexChapter>
        {
            new()
            {
                Id = "what-this-is",
                Volume = 1,
                Order = 1,
                Title = "What this is",
                Blurb = "The book, as plain text. Same words, no pictures.",
                Blocks = new List<CodexBlock>
                {
                    new() { Type = "p", Text = "Everything the illustrated book says is here too. The drawings are CSS inside the page and cannot be redrawn out here, so where a figure stood you get its caption instead." },
                    new() { Type = "steps", Items = new List<string> { "Pick a chapter on the left.", "Read it on the right.", "The manual on the web is one click away, at the bottom." } },
                    new() { Type = "figure", Kind = "stack-drop", Caption = "Figure: the fail-soft ladder, four rungs, all landing here." },
                    new() { Type = "callout", Text = "A bookmark that points at a chapter which no longer exists is not an error. It falls through to the first chapter there is." },
                    new() { Type = "limit", Text = "This reader draws no figures and runs no demos. Losing the drawing is acceptable; losing the sentence is not." },
                },
                Margin = new CodexMargin { T = "you got the paperback edition. still mine.", Face = "^_^" },
            },
            new()
            {
                Id = "how-to-read-it",
                Volume = 1,
                Order = 2,
                Title = "How to read it",
                Blurb = "One chapter is one screen. Volumes group them.",
                Blocks = new List<CodexBlock>
                {
                    new() { Type = "p", Text = "Volume headers are rows in the same list, so the contents needs no grouping machinery. They are not selectable: clicking one steps onto the chapter beneath it." },
                },
            },
            new()
            {
                Id = "when-it-breaks",
                Volume = 2,
                Order = 1,
                Title = "When it breaks",
                Blurb = "There is no state in which the book is an empty hole.",
                Blocks = new List<CodexBlock>
                {
                    new() { Type = "p", Text = "No runtime, no bundle, a navigation that failed, a browser process that died - four faults, one reader. The line under the heading is the only thing that changes between them." },
                },
                Margin = new CodexMargin { T = "if this page is blank, something is very wrong.", Face = ">_<" },
            },
        };

        /// <summary>ponytail: needs EmiCodex.Bookmark (persisted last chapter), wired when it moves
        /// to Core. Null means "start at the first chapter", which is the same fallback the
        /// original takes for a stale bookmark.</summary>
        internal static string? Bookmark => null;

        /// <summary>ponytail: needs EmiCodex.NoteChapter (persists the bookmark), wired when it
        /// moves to Core.</summary>
        internal static void NoteChapter(string? chapterId) { _ = chapterId; }

        /// <summary>
        /// <c>EmiCodex.OpenManualInBrowser</c>, which is portable in full: WPF's
        /// <c>Process.Start(ManualUrl) { UseShellExecute = true }</c> is ShellExecute on Windows and
        /// xdg-open on Linux, so the WPF body ports verbatim including its warning. The URL is the
        /// same const the service carries.
        /// </summary>
        internal static void OpenManualInBrowser()
        {
            try { Process.Start(new ProcessStartInfo(ManualUrl) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Warning(ex, "[EmiCodex] could not open the manual in a browser"); }
        }
    }

    /// <summary>
    /// ONE BLOCK OF A CHAPTER. Copied from Services/EmiDesk/EmiCodex.cs without its
    /// <c>[JsonProperty]</c> attributes - this head never deserialises one. Every field is optional
    /// on purpose: a chapter from a later wave with a block type this build has never heard of must
    /// still READ, not throw.
    /// </summary>
    internal sealed class CodexBlock
    {
        /// <summary>p | steps | figure | callout | limit. Anything else renders as a paragraph.</summary>
        public string? Type { get; set; }

        public string? Text { get; set; }

        /// <summary>The ordered lines of a <c>steps</c> block.</summary>
        public List<string>? Items { get; set; }

        /// <summary>The figure vocabulary word (stack-drop, pulse, layers...). CSS only, never art.</summary>
        public string? Kind { get; set; }

        public string? Caption { get; set; }
    }

    /// <summary>EMI in the margin: exactly one reaction per chapter, never an explanation.</summary>
    internal sealed class CodexMargin
    {
        public string? T { get; set; }
        public string? Face { get; set; }
    }

    /// <summary>One chapter = one screen, as it is written in
    /// <c>Resources/web/codex/chapters/&lt;id&gt;.json</c>.</summary>
    internal sealed class CodexChapter
    {
        public string? Id { get; set; }
        public int Volume { get; set; }
        public int Order { get; set; }
        public string? Title { get; set; }
        public string? Blurb { get; set; }
        public string? Target { get; set; }
        public string? Tour { get; set; }
        public CodexMargin? Margin { get; set; }
        public List<CodexBlock>? Blocks { get; set; }

        /// <summary>A title that is always safe to put on a list row.</summary>
        public string DisplayTitle =>
            !string.IsNullOrWhiteSpace(Title) ? Title!.Trim()
            : !string.IsNullOrWhiteSpace(Id) ? Id!.Replace('-', ' ')
            : "untitled";
    }
}
