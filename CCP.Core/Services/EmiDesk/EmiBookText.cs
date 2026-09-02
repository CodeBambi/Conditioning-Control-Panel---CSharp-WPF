using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// One emphasised or plain run of a book card's line.
/// </summary>
/// <param name="Text">The characters, with no markup left in them.</param>
/// <param name="Hot">True when this run is a key word and should be drawn loud.</param>
public readonly record struct EmiTextRun(string Text, bool Hot);

/// <summary>
/// The tiny inline-emphasis parser the book's cards are written in.
///
/// <para><b>Why there is markup at all.</b> The owner's note on the first draft was that the cards
/// read like a manual and gave no purchase to the eye: "In bold the key words, and colored"
/// (2026-08-30). A card is read in about two seconds by somebody who is mid-task, so the words that
/// carry the meaning have to be findable without reading the sentence. That means the copy has to
/// say WHICH words those are, which means the copy needs markup, which means the smallest markup
/// that a writer can use without thinking: <c>*asterisks*</c>.</para>
///
/// <para><b>The whole grammar.</b> A pair of asterisks wraps a hot run. Everything else is plain.
/// There is no escape character, no nesting and no other mark - a card is one short line and any
/// more grammar than this would be a thing to get wrong rather than a thing to use.</para>
///
/// <para><b>It never throws and it never eats text.</b> An unpaired asterisk is a literal asterisk,
/// an empty pair contributes nothing, and the concatenation of every run's text is always the input
/// with its PAIRED asterisks removed. A card whose author fumbled the markup renders as prose with
/// a stray star in it, which is a typo; a card that renders as nothing is a blank panel, which is a
/// bug. Those two failures are not close in cost, so this leans the whole way to the first.</para>
/// </summary>
public static class EmiBookText
{
    /// <summary>
    /// Split a line into plain and hot runs. Never returns null, never throws, and returns a single
    /// plain run for a line with no markup in it.
    /// </summary>
    public static IReadOnlyList<EmiTextRun> Parse(string? line)
    {
        var runs = new List<EmiTextRun>();
        if (string.IsNullOrEmpty(line)) return runs;

        try
        {
            int i = 0;
            while (i < line.Length)
            {
                int open = line.IndexOf('*', i);
                if (open < 0) break;

                // An asterisk with no partner is not markup, it is an asterisk. Stop looking and
                // let the tail below carry the rest of the line through verbatim.
                int close = line.IndexOf('*', open + 1);
                if (close < 0) break;

                if (open > i) Add(runs, line.Substring(i, open - i), hot: false);
                if (close > open + 1) Add(runs, line.Substring(open + 1, close - open - 1), hot: true);
                i = close + 1;
            }

            if (i < line.Length) Add(runs, line.Substring(i), hot: false);
        }
        catch (Exception)
        {
            // Whatever went wrong, the words matter more than the emphasis.
            runs.Clear();
            runs.Add(new EmiTextRun(line, false));
        }

        if (runs.Count == 0) runs.Add(new EmiTextRun(line, false));
        return runs;
    }

    /// <summary>The line with its markup taken off, for measuring, tests and the log.</summary>
    public static string Strip(string? line)
    {
        var runs = Parse(line);
        if (runs.Count == 1) return runs[0].Text;

        var sb = new System.Text.StringBuilder();
        foreach (var r in runs) sb.Append(r.Text);
        return sb.ToString();
    }

    /// <summary>Merge a run onto the tail when they carry the same weight, so a line never turns
    /// into a stack of one-character runs the text engine has to lay out separately.</summary>
    private static void Add(List<EmiTextRun> runs, string text, bool hot)
    {
        if (text.Length == 0) return;
        if (runs.Count > 0 && runs[^1].Hot == hot)
        {
            runs[^1] = new EmiTextRun(runs[^1].Text + text, hot);
            return;
        }
        runs.Add(new EmiTextRun(text, hot));
    }
}
