using System.Collections.Generic;

namespace ConditioningControlPanel.Services.UI;

/// <summary>
/// The help cards' one piece of markup: <c>**text**</c> marks a key word. Pure, so the split is
/// testable without WPF; <see cref="HelpTooltipBuilder"/> turns the segments into runs.
/// </summary>
public static class HelpMarkup
{
    public const string Marker = "**";

    /// <summary>
    /// Splits <paramref name="text"/> into (text, emphasised) segments. Empty segments are
    /// dropped. An unpaired marker is kept as literal text, so a typo never eats the rest of a
    /// sentence.
    /// </summary>
    public static IReadOnlyList<(string Text, bool Emphasis)> Split(string? text)
    {
        var segments = new List<(string, bool)>();
        if (string.IsNullOrEmpty(text)) return segments;

        int pos = 0;
        bool emphasis = false;
        while (pos < text.Length)
        {
            int next = text.IndexOf(Marker, pos, System.StringComparison.Ordinal);
            if (next < 0) break;
            if (emphasis == false && text.IndexOf(Marker, next + Marker.Length, System.StringComparison.Ordinal) < 0)
                break;                                          // an opener with no closer: literal
            if (next > pos) segments.Add((text.Substring(pos, next - pos), emphasis));
            emphasis = !emphasis;
            pos = next + Marker.Length;
        }
        if (pos < text.Length) segments.Add((text.Substring(pos), emphasis));
        return segments;
    }
}
