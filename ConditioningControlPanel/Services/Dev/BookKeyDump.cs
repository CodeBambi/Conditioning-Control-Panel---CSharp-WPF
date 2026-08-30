using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Services.Dev;

/// <summary>
/// Emits every <c>emi_book_*</c> key the deck needs, as a JSON fragment, so the localization file
/// can be synced from the deck rather than transcribed by hand.
///
/// <para><b>Why this exists.</b> A card carries its English twice: once inline in the record, where
/// it is the fallback that renders on a build whose language file predates the card, and once in
/// <c>en.json</c>, which is the string a translator actually sees. Those two must match byte for
/// byte - <c>EmiBookCardsTests.Every_card_key_is_in_english_and_matches_its_literal</c> is the test
/// that says so, and it exists because a mismatch is invisible: the card looks perfect on the
/// machine that wrote it and is never translated anywhere else. The deck now spans six files and
/// twenty-odd cards, so hand-copying a hundred and fifty strings between them is a typo generator.
/// This reads the strings out of the deck itself, which cannot disagree with the deck.</para>
///
/// <para><b>It writes a FRAGMENT, not a file to paste over en.json.</b> The output is the
/// <c>emi_book_*</c> block and nothing else: splice it into the language file where that block
/// already lives. Ordering follows the deck's reading order so the diff stays readable.</para>
///
/// <para>Emphasis markup is emitted verbatim. The <c>*asterisks*</c> are part of the copy, they are
/// parsed at render time by <see cref="EmiBookText"/>, and a translator has to be able to move them
/// with the words they mark.</para>
/// </summary>
public static class BookKeyDump
{
    /// <summary>Write the fragment to <paramref name="path"/>. Never throws.</summary>
    public static void Run(string path)
    {
        try
        {
            var rows = new List<KeyValuePair<string, string>>
            {
                new("emi_book_catch_label", EmiBookCards.L("emi_book_catch_label", "the catch:")),
                new("emi_book_go", EmiBookCards.L("emi_book_go", "TAKE ME THERE")),
                new("emi_book_walk", EmiBookCards.L("emi_book_walk", "WALK ME THROUGH IT")),
                new("emi_book_close", EmiBookCards.L("emi_book_close", "close the book")),
                new("emi_book_stage", EmiBookCards.L("emi_book_stage", "DEMO")),
            };

            for (int i = 0; i < EmiBookCards.TabKeys.Count; i++)
                rows.Add(new("emi_book_tab_" + EmiBookCards.TabKeys[i], EmiBookCards.TabNamesEn[i]));

            foreach (var c in EmiBookCards.All)
            {
                rows.Add(new(c.KeyStem + "_title", c.TitleEn));
                rows.Add(new(c.KeyStem + "_gist", c.GistEn));
                for (int i = 0; i < c.NudgesEn.Count; i++)
                    rows.Add(new($"{c.KeyStem}_nudge{i + 1}", c.NudgesEn[i]));
                rows.Add(new(c.KeyStem + "_catch", c.CatchEn));
            }

            var sb = new StringBuilder();
            var opts = new JsonWriterOptions { Indented = false };
            foreach (var r in rows)
            {
                // JsonSerializer rather than a hand-rolled quote: the copy carries apostrophes and
                // will one day carry a backslash or a non-ASCII character, and the language files
                // are strict-JSON clean as of 2026-07-29. Keep them that way.
                sb.Append("  ")
                  .Append(JsonSerializer.Serialize(r.Key))
                  .Append(": ")
                  .Append(JsonSerializer.Serialize(r.Value))
                  .Append(",\n");
            }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // No BOM: this is a fragment that gets spliced into a file that already has its own.
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Log.Information("[EmiDesk] book keys dumped: {N} rows to {Path}", rows.Count, path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[EmiDesk] book key dump failed");
        }
    }
}
