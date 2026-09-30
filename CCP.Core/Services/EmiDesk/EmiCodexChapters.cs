using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// ONE BLOCK OF A CHAPTER. The renderer (Resources/web/codex) draws these properly; the native
/// fail-soft reader (EmiCodexWindow, both heads) draws them as plain scrolling text. Every
/// field is optional on purpose - a chapter file written by hand, half-merged, or from a later
/// wave with a block type this build has never heard of must still READ, not throw.
/// </summary>
public sealed class CodexBlock
{
    /// <summary>p | steps | figure | callout | limit. Anything else renders as a paragraph.</summary>
    [JsonProperty("type")] public string? Type { get; set; }

    [JsonProperty("text")] public string? Text { get; set; }

    /// <summary>The ordered lines of a <c>steps</c> block.</summary>
    [JsonProperty("items")] public List<string>? Items { get; set; }

    /// <summary>The figure vocabulary word (stack-drop, pulse, layers...). CSS only, never art.</summary>
    [JsonProperty("kind")] public string? Kind { get; set; }

    [JsonProperty("caption")] public string? Caption { get; set; }
}

/// <summary>EMI in the margin: exactly one reaction per chapter, never an explanation.</summary>
public sealed class CodexMargin
{
    [JsonProperty("t")] public string? T { get; set; }
    [JsonProperty("face")] public string? Face { get; set; }
}

/// <summary>
/// One chapter = one screen, as it is written in <c>Resources/web/codex/chapters/&lt;id&gt;.json</c>.
/// Deserialised by the C# lane ONLY for the fail-soft reader; the page reads the same files itself.
/// </summary>
public sealed class CodexChapter
{
    [JsonProperty("id")] public string? Id { get; set; }
    [JsonProperty("volume")] public int Volume { get; set; }
    [JsonProperty("order")] public int Order { get; set; }
    [JsonProperty("title")] public string? Title { get; set; }
    [JsonProperty("blurb")] public string? Blurb { get; set; }

    /// <summary>An EmiTargets id, or null. Drives "TAKE ME THERE".</summary>
    [JsonProperty("target")] public string? Target { get; set; }

    /// <summary>A <c>TutorialType</c> NAME, or null. Never an ordinal.</summary>
    [JsonProperty("tour")] public string? Tour { get; set; }

    [JsonProperty("margin")] public CodexMargin? Margin { get; set; }
    [JsonProperty("blocks")] public List<CodexBlock>? Blocks { get; set; }

    /// <summary>A title that is always safe to put on a list row.</summary>
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title) ? Title!.Trim()
        : !string.IsNullOrWhiteSpace(Id) ? Id!.Replace('-', ' ')
        : "untitled";
}

/// <summary>
/// The book's pure half, shared by both heads: the fail-soft chapter reader and the bookmark.
/// Moved out of the WPF head's EmiCodex (which keeps the WebView2 host and delegates here);
/// each head passes its own chapters folder.
/// </summary>
public static class EmiCodexChapters
{
    private const string LogTag = "EmiCodex";

    /// <summary>The website manual, opened in the user's own browser.</summary>
    public const string ManualUrl = "https://cclabs.app/guide.html";

    /// <summary>
    /// Every chapter in <paramref name="dir"/>, in reading order, skipping anything unreadable.
    ///
    /// <para>NOTHING here throws and nothing here is cached: one malformed chapter costs that
    /// chapter and no more - a parse that gives up on the folder turns one bad merge into an empty
    /// book. Order is volume, then <c>order</c>, then id, so a chapter that forgot its
    /// <c>order</c> still lands somewhere stable.</para>
    /// </summary>
    public static IReadOnlyList<CodexChapter> Read(string? dir)
    {
        var list = new List<CodexChapter>();
        try
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                Log.Debug("[{Tag}] no chapters folder at {Dir}", LogTag, dir);
                return list;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    if (string.IsNullOrWhiteSpace(json)) continue;
                    var ch = JsonConvert.DeserializeObject<CodexChapter>(json);
                    if (ch == null) continue;
                    // A file with no id is unreachable by the bookmark and by "take me there";
                    // fall back to the file name rather than dropping the words on the floor.
                    if (string.IsNullOrWhiteSpace(ch.Id)) ch.Id = Path.GetFileNameWithoutExtension(file);
                    list.Add(ch);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[{Tag}] chapter {File} is unreadable, skipped", LogTag, Path.GetFileName(file));
                }
            }

            list.Sort((a, b) =>
            {
                int v = a.Volume.CompareTo(b.Volume);
                if (v != 0) return v;
                int o = a.Order.CompareTo(b.Order);
                if (o != 0) return o;
                return string.CompareOrdinal(a.Id ?? string.Empty, b.Id ?? string.Empty);
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[{Tag}] chapter scan failed", LogTag);
        }
        return list;
    }

    /// <summary>The chapter last open, or null. Persisted in <see cref="EmiState"/>.</summary>
    public static string? Bookmark
    {
        get
        {
            try
            {
                var id = EmiState.Current.CodexChapter;
                return string.IsNullOrWhiteSpace(id) ? null : id;
            }
            catch (Exception ex) { Log.Debug(ex, "[{Tag}] bookmark read failed", LogTag); return null; }
        }
    }

    /// <summary>Remember the open chapter. Ignores blank ids and a repeat of what is already
    /// stored, so a page that re-announces its chapter cannot churn the state file.</summary>
    public static void NoteChapter(string? chapterId)
    {
        if (string.IsNullOrWhiteSpace(chapterId)) return;
        try
        {
            var id = chapterId!.Trim();
            var s = EmiState.Current;
            if (string.Equals(s.CodexChapter, id, StringComparison.Ordinal)) return;
            s.CodexChapter = id;
            EmiState.SaveSoon();
        }
        catch (Exception ex) { Log.Debug(ex, "[{Tag}] bookmark write failed", LogTag); }
    }
}
