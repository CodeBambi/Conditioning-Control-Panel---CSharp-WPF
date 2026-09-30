using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>SubliminalWhisperShow against WPF SubliminalService.TriggerBambiFreeze / PlayBambiReset:
/// duck + whisper first, card 300 ms later, Reset 4-8 s on a passing 90% roll; muted = card at once,
/// no Reset. Uses the shipped neutral freeze/reset clips linked into the output (no mod layer here).</summary>
public sealed class SubliminalWhisperShowTests
{
    private sealed record Run(List<string> Log, List<(int Ms, Action Then)> Timers);

    private static void With(bool audible, double roll, Action<Run> body)
    {
        var s = CoreSettings.Current;
        var (en, mu, d, lvl) = (s.SubAudioEnabled, s.SubAudioMuted, s.AudioDuckingEnabled, s.DuckingLevel);
        var (draw, after, rollFn) = (SubliminalWhisperShow.Draw, SubliminalWhisperShow.After, SubliminalWhisperShow.Roll);
        var (play, duck, unduck) = (CoreAudio.PlayOneShotProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider);
        var freeze = CoreSubliminal.BambiFreezeProvider;
        CoreSubliminal.BambiFreezeProvider = SubliminalWhisperShow.Freeze;   // as App.axaml.cs seeds it
        var run = new Run(new(), new());
        (s.SubAudioEnabled, s.SubAudioMuted, s.AudioDuckingEnabled, s.DuckingLevel) = (true, !audible, true, 70);
        SubliminalWhisperShow.Draw = t => run.Log.Add("card " + t);
        SubliminalWhisperShow.After = (ms, then) => run.Timers.Add((ms, then));
        SubliminalWhisperShow.Roll = () => roll;
        CoreAudio.PlayOneShotProvider = (p, v, tag, _, done) => { run.Log.Add($"play {Path.GetFileName(p)} {tag}"); done?.Invoke(); };
        CoreAudio.DuckProvider = n => run.Log.Add("duck " + n);
        CoreAudio.UnduckProvider = _ => run.Log.Add("unduck");
        try { body(run); }
        finally
        {
            (s.SubAudioEnabled, s.SubAudioMuted, s.AudioDuckingEnabled, s.DuckingLevel) = (en, mu, d, lvl);
            (SubliminalWhisperShow.Draw, SubliminalWhisperShow.After, SubliminalWhisperShow.Roll) = (draw, after, rollFn);
            (CoreAudio.PlayOneShotProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider) = (play, duck, unduck);
            CoreSubliminal.BambiFreezeProvider = freeze;
        }
    }

    private static void Fire(Run run, int ms)
    {
        var i = run.Timers.FindIndex(t => t.Ms == ms);
        Assert.True(i >= 0, $"no {ms} ms timer in [{string.Join(",", run.Timers.ConvertAll(t => t.Ms))}]");
        var t = run.Timers[i];
        run.Timers.RemoveAt(i);
        t.Then();
    }

    [Fact]
    public void Freeze_whispers_then_card_then_reset() => With(audible: true, roll: 0.9, run =>
    {
        CoreSubliminal.TriggerBambiFreeze();
        Assert.Equal(new[] { "duck 70", "play freeze.mp3 whisper" }, run.Log);   // no card yet
        Fire(run, 500);                                                         // unduck after the clip
        Fire(run, 300);                                                         // haptic lead + visual gap
        Assert.Equal(new[] { "duck 70", "play freeze.mp3 whisper", "unduck", "card Freeze" }, run.Log);
        var reset = Assert.Single(run.Timers);
        Assert.InRange(reset.Ms, 4000, 7999);
        Fire(run, reset.Ms);
        Assert.Equal("play reset.mp3 whisper", run.Log[^1]);
        Fire(run, 500);
        Fire(run, 300);
        Assert.Equal("card Reset", run.Log[^1]);
        Assert.Empty(run.Timers);
    });

    [Fact]
    public void Ambient_phrase_whispers_for_20_xp_else_card_for_10()
    {
        var xp = new List<double>();
        var prev = CoreProgression.AddXPProvider;
        CoreProgression.AddXPProvider = (a, _) => xp.Add(a);
        try
        {
            With(audible: true, roll: 0.5, run =>
            {
                SubliminalWhisperShow.Phrase("reset");                            // has a neutral clip
                Assert.Equal(new[] { "duck 70", "play reset.mp3 whisper" }, run.Log);
                Fire(run, 300);
                Assert.Equal("card reset", run.Log[^1]);
                SubliminalWhisperShow.Phrase("no clip for this");
                Assert.Equal("card no clip for this", run.Log[^1]);             // no clip: card at once
            });
            Assert.Equal(new[] { 20.0, 10.0 }, xp);
        }
        finally { CoreProgression.AddXPProvider = prev; }
    }

    [Fact]
    public void Freeze_muted_draws_at_once_and_a_failed_roll_skips_reset()
    {
        With(audible: false, roll: 0.5, run =>
        {
            CoreSubliminal.TriggerBambiFreeze();
            Assert.Equal(new[] { "card Freeze" }, run.Log);                      // whispers muted: no audio
            Assert.Empty(run.Timers);                                           // and no Reset (WPF else-branch)
        });
        With(audible: true, roll: 0.95, run =>
        {
            CoreSubliminal.TriggerBambiFreeze();
            Fire(run, 500);
            Fire(run, 300);
            Assert.Empty(run.Timers);                                           // the 10% skip
        });
    }
}
