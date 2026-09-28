using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.PieceByPiece;

/// <summary>
/// Which audio clips Distraction mode whispers (Mort's audio ladder, 2026-09-28): the player's
/// OWN brain drain clips first, the same folder the Brain Drain feature plays
/// (<c>&lt;assets&gt;\braindrain</c>, reached through the <c>ccp.assets</c> mapping). With none,
/// a fallback the active mod is allowed to use: the bundled Bambi whisper clips only for mods
/// <see cref="ModAudioPolicy.UsesSharedSubAudio"/> allows, the neutral Circe words for everyone
/// else. Never synthetic speech: an empty list means the page plays no whispers at all.
///
/// <para>Pure (file names in, urls out) so the rule is tested without a disk or a WebView.</para>
/// </summary>
public static class PbpWhisperClips
{
    public const string AssetsHost = "ccp.assets";
    public const string SubAudioHost = "ccp.subaudio";
    public const string WordsHost = "ccp.words";
    public const string BrainDrainFolder = "braindrain";

    /// <summary>The page decodes at most a handful; a long list only costs the frame.</summary>
    public const int MaxClips = 24;

    private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".ogg" };

    public static bool IsClip(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && AudioExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The page's clip urls. <paramref name="brainDrain"/>, <paramref name="subAudio"/> and
    /// <paramref name="words"/> are bare file names in their folders (null when the folder is
    /// missing).
    /// </summary>
    public static IReadOnlyList<string> Build(
        IEnumerable<string>? brainDrain, bool modUsesSubAudio,
        IEnumerable<string>? subAudio, IEnumerable<string>? words)
    {
        var own = Urls(brainDrain, AssetsHost, BrainDrainFolder + "/");
        if (own.Count > 0) return own;
        if (modUsesSubAudio)
        {
            var sub = Urls(subAudio, SubAudioHost, "");
            if (sub.Count > 0) return sub;
        }
        return Urls(words, WordsHost, "");
    }

    private static List<string> Urls(IEnumerable<string>? names, string host, string prefix)
    {
        if (names == null) return new List<string>();
        return names
            .Select(n => Path.GetFileName(n ?? ""))
            .Where(IsClip)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Take(MaxClips)
            .Select(n => $"https://{host}/{prefix}{Uri.EscapeDataString(n)}")
            .ToList();
    }
}
