using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The measured reason the session editor's description box ships <c>TextWrapping="NoWrap"</c> while
/// upstream's wraps (<c>Windows/SessionEditorWindow.xaml:235</c>) — pinned here so the divergence
/// carries its evidence in the suite rather than only in a comment, and so the day it stops being
/// true this fact says so.
///
/// <para><b>What it is, settled 2026-08-25.</b> A HARNESS artefact, not a product defect, and that
/// was established by measurement rather than assumed. Under this assembly's headless platform the
/// font manager serves <c>Avalonia.Headless.BareMinimum.ttf</c>, and with it a wrapped
/// <c>TextLayout</c> over text containing a BLANK LINE never terminates: the formatter returns a
/// zero-length line with no <c>TextEndOfParagraph</c> break at the blank line's position, and
/// <c>TextLayout.CreateTextLines</c>'s loop advances by that line's length, so it appends a line per
/// iteration forever and the process climbs past 19 GB. This fact walks the SAME formatter with a
/// hard iteration cap, so it records the non-advance instead of hanging on it.</para>
///
/// <para><b>The narrowing, which corrects the original report.</b> It is NOT "text long enough to
/// wrap": the same wrapped layout over 160 characters with no newline formats in 0.42 s and 14
/// lines. The trigger is an EMPTY LINE followed by more text — a leading <c>\n</c> is enough, a
/// TRAILING one is not (<c>CreateTextLines</c> handles the final empty line specially), and
/// <c>WrapWithOverflow</c> hangs on it too. The shipped session descriptions all contain
/// <c>\n\n</c>, which is why opening the editor was the thing that hung.</para>
///
/// <para><b>Why it is the harness and not Avalonia's formatter alone.</b> Measured three ways
/// against the same text and the same wrap width: with Skia's real system fonts it formats in
/// 0.42 s; with headless STUB DRAWING but a real embedded font (Inter) it formats in 0.40 s; only
/// the stub font hangs. So the product — which never uses that font — wraps this text correctly, and
/// no shipped surface is affected. The residual upstream observation, recorded and NOT reported
/// anywhere by this lane because no reproduction against a real font exists: a formatter that
/// returns a zero-length line with no end-of-paragraph break puts
/// <c>TextLayout.CreateTextLines</c> into an unbounded allocating loop with no non-advance guard.
/// </para>
///
/// <para><b>What this fact does NOT prove.</b> It says nothing about how wrapped editable text
/// RENDERS, nothing about caret or selection behaviour in a wrapped box, and nothing about any
/// platform: it is one formatter walk under one font. It is the precondition of the divergence, not
/// a substitute for the headed run that would lift it.</para>
/// </summary>
public class HeadlessWrappedTextArtefactTests
{
    /// <summary>Two paragraphs separated by a blank line — the shape every shipped session
    /// description has, and the whole trigger.</summary>
    private const string BlankLineText = "First paragraph.\n\nSecond paragraph.";

    /// <summary>Enough iterations to walk this text several times over, so a formatter that DOES
    /// advance reaches the end well inside it. Never a wall-clock wait: this is a count.</summary>
    private const int Cap = 8;

    private const double WrapWidth = 200;

    [AvaloniaFact]
    public void WrappedTextWithABlankLine_StopsAdvancing_WhichIsWhyTheDescriptionBoxShipsNoWrap()
    {
        var wrapped = Walk(TextWrapping.Wrap, BlankLineText);
        var unwrapped = Walk(TextWrapping.NoWrap, BlankLineText);

        // The control, and it is what makes the assertion below non-vacuous: the SAME text through
        // the SAME formatter with wrapping off walks to the end of the source and stops. A walk
        // that stalled either way would prove nothing about wrapping.
        Assert.True(
            unwrapped.Count < Cap,
            $"NoWrap took the full {Cap}-iteration cap over {unwrapped.Count} line(s), so this fact is no "
            + "longer contrasting anything and its conclusion about wrapping does not follow");
        Assert.DoesNotContain(unwrapped, line => line.Length == 0);

        // The artefact. The walk reaches the blank line, returns a line of length 0, and every later
        // iteration returns the same zero-length line from the same position: TextLayout's own loop
        // adds the line's Length to its position, so it cannot terminate and cannot stop allocating.
        var stalled = wrapped.FindIndex(line => line.Length == 0);
        Assert.True(
            stalled >= 0,
            "a wrapped walk over text with a blank line no longer produces a zero-length line under this "
            + "harness's font. THAT IS GOOD NEWS AND IT IS NOT A REASON TO EDIT THIS TEST: it means the "
            + "reason the session editor's description box diverges from upstream by shipping NoWrap has "
            + "expired. Restore TextWrapping=\"Wrap\" on SessionEditorWindow.axaml's DescriptionBox, drop "
            + "its divergence comment, and delete this fact.");
        Assert.True(
            wrapped.Count == Cap && wrapped.Skip(stalled).All(line => line.Length == 0),
            $"the wrapped walk stalled at line {stalled} but then recovered, which no longer matches the "
            + "measured artefact; re-measure before trusting either half of the divergence record");
    }

    /// <summary>Walks <see cref="TextFormatter"/> line by line exactly as
    /// <c>TextLayout.CreateTextLines</c> does — advancing the source position by each line's own
    /// length — but stops at <see cref="Cap"/> iterations instead of at a break that may never
    /// come.</summary>
    private static List<TextLine> Walk(TextWrapping wrapping, string text)
    {
        var runProperties = new GenericTextRunProperties(
            new Typeface(FontManager.Current.DefaultFontFamily),
            fontRenderingEmSize: 14,
            foregroundBrush: Brushes.Black);
        var paragraph = new GenericTextParagraphProperties(runProperties, textWrapping: wrapping);
        var source = new WholeText(text, runProperties);

        var lines = new List<TextLine>();
        var position = 0;
        TextLine? previous = null;

        for (var i = 0; i < Cap; i++)
        {
            var line = TextFormatter.Current.FormatLine(source, position, WrapWidth, paragraph, previous?.TextLineBreak);
            if (line is null)
            {
                break;
            }

            lines.Add(line);
            position += line.Length;
            previous = line;

            if (line.TextLineBreak?.TextEndOfLine is TextEndOfParagraph)
            {
                break;
            }
        }

        return lines;
    }

    /// <summary>The minimal text source: one run from the requested index to the end, then the
    /// paragraph terminator. The formatter does its own line-break splitting inside that run.
    /// </summary>
    private sealed class WholeText(string text, TextRunProperties properties) : ITextSource
    {
        public TextRun? GetTextRun(int textSourceIndex) => textSourceIndex >= text.Length
            ? new TextEndOfParagraph()
            : new TextCharacters(text.AsMemory(textSourceIndex), properties);
    }
}
