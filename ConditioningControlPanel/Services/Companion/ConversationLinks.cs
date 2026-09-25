using System;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>
/// Presentation for a companion reply that names a sanctioned video title on the v2 conversation
/// page. The model is told titles only (the app owns links), so it often wraps the title in
/// brackets or leaves a "[PLAY THE VIDEO]" placeholder where it imagines a link. The page draws a
/// real link button for the title; this keeps the visible text free of the dead brackets.
/// </summary>
internal static class ConversationLinks
{
    // A single bracket pair on one line. Nested or doubled brackets are left alone.
    private static readonly Regex Bracketed = new(@"(?<!\[)\[([^\[\]\r\n]{1,160})\](?!\])", RegexOptions.CultureInvariant);

    // What a model writes where it pictures a link it cannot make.
    private static readonly Regex Placeholder = new(
        @"^\s*(?:(?:click|tap|play|watch|open|start|view|here'?s?)\b.*\b(?:video|link|here|it|this|now|file|clip)\b|video\s*link|link|video|link here|click here)[\s.!:~-]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Spaces = new(@"[ \t]{2,}", RegexOptions.CultureInvariant);
    private static readonly Regex SpaceBeforePunct = new(@"[ \t]+([.,!?;:~])", RegexOptions.CultureInvariant);
    private static readonly Regex DanglingColon = new(@":[ \t]*(?=[.!?]|$)", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// "[Title]" becomes "Title"; a placeholder that contains the title keeps only the title; a
    /// placeholder without it is dropped. Any other bracketed text is the persona's own and stays.
    /// </summary>
    internal static string Tidy(string? text, string? title)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var hasTitle = !string.IsNullOrWhiteSpace(title);
        var changed = false;
        var result = Bracketed.Replace(text, m =>
        {
            var inner = m.Groups[1].Value;
            if (hasTitle)
            {
                var at = inner.IndexOf(title!, StringComparison.OrdinalIgnoreCase);
                if (at >= 0) { changed = true; return inner.Substring(at, title!.Length); }
            }
            if (Placeholder.IsMatch(inner)) { changed = true; return string.Empty; }
            return m.Value;
        });
        if (!changed) return text;
        result = Spaces.Replace(result, " ");
        result = SpaceBeforePunct.Replace(result, "$1");
        result = DanglingColon.Replace(result, string.Empty);
        return result.Trim();
    }
}
