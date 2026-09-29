using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Bug hunt 2026-09-29 (HYGIENE-1): the Prize Parlour's "Try it" button read "è©¦ãã¦ã¿ã" in
/// Japanese (and the same in Korean, Russian and Chinese): UTF-8 that was read as Latin-1 and saved
/// again. Every language file is checked for that shape: a run of U+0080..U+00FF characters whose
/// Latin-1 bytes hold a whole UTF-8 sequence. Real accents never do (an "e" with an acute accent
/// followed by a no-break space is a lead byte with one continuation, not two).
/// </summary>
public class LocalizationEncodingTests
{
    private static readonly string[] Languages = { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    /// <summary>True when a Latin-1 run of the text holds a whole UTF-8 multi-byte sequence.</summary>
    internal static bool LooksDoubleEncoded(string text)
    {
        var run = new List<byte>();
        foreach (var c in text + "\0")
        {
            if (c >= '\u0080' && c <= 'ÿ') { run.Add((byte)c); continue; }
            if (HoldsUtf8Sequence(run)) return true;
            run.Clear();
        }
        return false;
    }

    private static bool HoldsUtf8Sequence(List<byte> bytes)
    {
        for (var i = 0; i < bytes.Count; i++)
        {
            var b = bytes[i];
            var need = b >= 0xC2 && b <= 0xDF ? 1 : b >= 0xE0 && b <= 0xEF ? 2 : b >= 0xF0 && b <= 0xF4 ? 3 : 0;
            if (need == 0 || i + need >= bytes.Count) continue;
            var whole = true;
            for (var k = 1; k <= need; k++)
                if (bytes[i + k] < 0x80 || bytes[i + k] > 0xBF) { whole = false; break; }
            if (whole) return true;
        }
        return false;
    }

    [Fact]
    public void No_language_file_holds_utf8_that_was_read_as_latin1()
    {
        var bad = new List<string>();
        foreach (var lang in Languages)
        {
            var path = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages", lang + ".json");
            foreach (var prop in JObject.Parse(File.ReadAllText(path, Encoding.UTF8)).Properties())
                if (prop.Value.Type == JTokenType.String && LooksDoubleEncoded((string)prop.Value!))
                    bad.Add($"{lang} {prop.Name}: {prop.Value}");
        }
        Assert.True(bad.Count == 0, "double-encoded text:\n" + string.Join("\n", bad));
    }

    [Theory]
    [InlineData("è©¦", true)]              // the start of the old Japanese "Try it"
    [InlineData("Ð\u009FÐ¾", true)]        // Russian, read as Latin-1
    [InlineData("café !", false)]               // French: an accent then a no-break space
    [InlineData("« Bonjour »", false)] // guillemets with no-break spaces
    [InlineData("Straße, Ärger", false)]
    [InlineData("¿Qué? ¡Ya!", false)]
    [InlineData("試してみる", false)] // real Japanese
    public void The_check_tells_mojibake_from_real_accents(string text, bool expected)
        => Assert.Equal(expected, LooksDoubleEncoded(text));
}
