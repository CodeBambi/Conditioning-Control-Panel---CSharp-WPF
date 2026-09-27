using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The launcher's hover melody. These rows are the musical promises: every note is in the key and
/// on the ladder, the line always moves and never lurches, a sweep is a phrase with a register,
/// two sweeps are two tunes, and every note it can ask for is a file that ships.
/// </summary>
public class LauncherMelodyTests
{
    private static readonly DateTime T0 = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A sweep across the grid: `crossings` tile entries, a comfortable pace apart.</summary>
    private static List<int> Line(LauncherMelody melody, int crossings, double gapMs = 320)
    {
        var now = T0;
        var line = new List<int>();
        for (int i = 0; i < crossings; i++)
        {
            line.Add(melody.Next(now).Rung);
            now = now.AddMilliseconds(gapMs);
        }
        return line;
    }

    [Fact]
    public void Every_note_it_can_play_is_a_rung_that_exists_and_is_in_the_plucks_range()
    {
        for (int seed = 0; seed < 300; seed++)
            foreach (var rung in Line(new LauncherMelody(new Random(seed)), 40, 250))
            {
                Assert.InRange(rung, 0, LauncherMelody.Rungs - 1);
                Assert.InRange(rung, LauncherMelody.Low, LauncherMelody.High);
            }
    }

    [Fact]
    public void A_note_is_never_silent_and_never_louder_than_the_hover_level()
    {
        var melody = new LauncherMelody(new Random(1));
        var now = T0;
        for (int i = 0; i < 50; i++)
        {
            Assert.InRange(melody.Next(now).Level, 0.5, 1.0);
            now = now.AddMilliseconds(250);
        }
    }

    [Fact]
    public void The_line_always_moves_and_never_lurches()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var line = Line(new LauncherMelody(new Random(seed)), 50);
            for (int i = 1; i < line.Count; i++)
            {
                var move = Math.Abs(line[i] - line[i - 1]);
                Assert.True(move >= 1, "the line never stalls on one note");
                Assert.True(move <= 5, $"a move of {move} degrees is a lurch, not a melody");
            }
        }
    }

    [Fact]
    public void A_phrase_keeps_to_its_own_register()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var line = Line(new LauncherMelody(new Random(seed)), 40);
            Assert.True(line.Max() - line.Min() < LauncherMelody.Span,
                "a phrase roams its window, not the whole ladder");
        }
    }

    [Fact]
    public void Two_sweeps_are_two_different_tunes()
    {
        var melody = new LauncherMelody(new Random(3));
        var first = Line(melody, 12);

        var later = T0.AddMilliseconds(LauncherMelody.PhraseGapMs + 12 * 320);
        var second = new List<int>();
        for (int i = 0; i < 12; i++) { second.Add(melody.Next(later).Rung); later = later.AddMilliseconds(320); }

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_pause_a_backwards_clock_and_a_reset_all_open_a_new_phrase()
    {
        var melody = new LauncherMelody(new Random(11));
        Line(melody, 6);

        // Nothing to assert about WHICH note opens a phrase - the register row above pins that.
        // This is about not throwing and not stalling on any of the three ways in.
        var after = T0.AddMilliseconds(LauncherMelody.PhraseGapMs + 6 * 320);
        Assert.InRange(melody.Next(after).Rung, LauncherMelody.Low, LauncherMelody.High);
        Assert.InRange(melody.Next(T0.AddHours(-1)).Rung, LauncherMelody.Low, LauncherMelody.High);

        melody.Reset();
        Assert.InRange(melody.Next(T0.AddMilliseconds(900)).Rung, LauncherMelody.Low, LauncherMelody.High);
    }

    [Fact]
    public void The_ladder_is_the_rooms_pentatonic_and_rung_five_is_C5()
    {
        var semis = Enumerable.Range(0, LauncherMelody.Rungs).Select(LauncherMelody.Semitones).ToArray();
        Assert.Equal(0, semis[LauncherMelody.RootRung]);
        Assert.Equal(new[] { -12, -10, -8, -5, -3, 0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24, 26, 28 }, semis);
        for (int i = 1; i < semis.Length; i++) Assert.True(semis[i] > semis[i - 1], "the ladder must only rise");
    }

    [Fact]
    public void Every_rung_has_a_rendered_note_on_disk()
    {
        var dir = Repo("ConditioningControlPanel", "Resources", "sounds", "launcher");
        for (int rung = 0; rung < LauncherMelody.Rungs; rung++)
        {
            var file = Path.Combine(dir, $"wood_{rung:00}.wav");
            Assert.True(File.Exists(file), $"missing {file} - re-run scripts/render-launcher-notes.mjs");
        }

        // The A/B voices came out when the pluck was picked. If one turns up again, it is either a
        // half-finished revert or 2.7 MB of installer nobody asked for.
        foreach (var dropped in new[] { "note", "glass" })
            Assert.False(File.Exists(Path.Combine(dir, $"{dropped}_00.wav")),
                $"{dropped} was not the pick - it should not be in the tree");
    }

    private static string Repo(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "ConditioningControlPanel.sln")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir! }.Concat(parts).ToArray());
    }
}
