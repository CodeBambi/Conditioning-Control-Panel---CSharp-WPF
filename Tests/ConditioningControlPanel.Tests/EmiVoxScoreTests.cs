using System;
using System.Linq;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// BLIPESE, the half of it that has no audio device in it.
///
/// <para><see cref="EmiVox.MakeScore"/> is a straight port of the Arcademy's <c>emi/vox.js</c>, and
/// the properties the campus suite pins there are pinned here too: the burst obeys both ceilings,
/// a question ends UP, and the same sentence always sounds like itself. That last one is the whole
/// identity of the voice AND the reason the rendered WAV can be cached by content hash - if the
/// score ever stopped being deterministic, the cache would start handing back the wrong melody
/// rather than sounding wrong out loud, which is a far quieter failure.</para>
///
/// <para>Nothing here touches <see cref="EmiVox"/> the instance: that one needs App.Audio,
/// App.Settings and a writable data path. The score and the renderer are static and pure.</para>
/// </summary>
public class EmiVoxScoreTests
{
    private const string Long =
        "okay okay listen. i know you said one more class but that was four classes ago, sweetheart!";

    [Fact]
    public void TheSameLineAlwaysSoundsLikeItself()
    {
        var a = EmiVox.MakeScore("hi again", "idle");
        var b = EmiVox.MakeScore("hi again", "idle");
        Assert.Equal(a, b);
    }

    [Fact]
    public void TwoDifferentLinesAreTwoDifferentMelodies()
    {
        var a = EmiVox.MakeScore("hi again", "idle");
        var b = EmiVox.MakeScore("bye again", "idle");
        Assert.NotEqual(a.Select(x => x.Pitch), b.Select(x => x.Pitch));
    }

    [Fact]
    public void AMoodChangesTheVoiceWithoutChangingTheWords()
    {
        var idle = EmiVox.MakeScore("good girl", "idle");
        var sad = EmiVox.MakeScore("good girl", "sad");
        Assert.Equal(idle.Count, sad.Count);
        Assert.True(sad[0].Pitch < idle[0].Pitch, "sad is the lowest mood and must sit under idle");
    }

    /// <summary>
    /// THE BURST IS BOUNDED, so a long line can never outlast the bubble it belongs to. Compression
    /// gives up syllables rather than speeding the pace up, so the ceiling holds on any length.
    /// </summary>
    [Fact]
    public void ALongLineIsCompressedIntoBothCeilings()
    {
        var score = EmiVox.MakeScore(Long, "idle");
        Assert.NotEmpty(score);
        Assert.True(score.Count <= EmiVox.VoxDials.MaxBlips,
            $"{score.Count} blips is over the ceiling of {EmiVox.VoxDials.MaxBlips}");
        Assert.True(score[^1].AtMs <= EmiVox.VoxDials.BurstMaxMs,
            $"the burst ran {score[^1].AtMs} ms, over the {EmiVox.VoxDials.BurstMaxMs} ms ceiling");
    }

    /// <summary>Every blip stays inside the window audio.js would clamp it to anyway.</summary>
    [Fact]
    public void NoBlipIsEverSentOutsideThePitchWindow()
    {
        foreach (var mood in EmiVox.Moods.Keys)
        {
            foreach (var line in new[] { Long, "wait, what?", "YES!", "oh... never mind", "hi" })
            {
                foreach (var b in EmiVox.MakeScore(line, mood))
                {
                    Assert.InRange(b.Pitch, EmiVox.VoxDials.PitchMin, EmiVox.VoxDials.PitchMax);
                    Assert.InRange(b.Gain, 0.02, 1.0);
                }
            }
        }
    }

    /// <summary>
    /// A QUESTION ENDS UP. The lift is the one gesture in the voice that takes no jitter, so it is
    /// provable: the last blip of a question must sit above the last blip of the same sentence
    /// said flat.
    /// </summary>
    [Fact]
    public void AQuestionRises()
    {
        var asked = EmiVox.MakeScore("are you still awake in there", "idle");
        var told = EmiVox.MakeScore("are you still awake in there?", "idle");
        Assert.Equal(asked.Count, told.Count);
        Assert.True(told[^1].Pitch > asked[^1].Pitch, "the question mark did not lift the tail");
    }

    /// <summary>An empty or wordless line is silence, not a stray beep.</summary>
    [Fact]
    public void NothingToSayIsSilence()
    {
        Assert.Empty(EmiVox.MakeScore(null, "idle"));
        Assert.Empty(EmiVox.MakeScore("   ", "idle"));
    }

    /// <summary>Syllables are vowel groups, clamped, and a word always gets at least one blip.</summary>
    [Fact]
    public void SyllablesAreCountedTheWayTheCampusCountsThem()
    {
        Assert.Equal(1, EmiVox.Syllables("hi"));
        Assert.Equal(2, EmiVox.Syllables("hello"));
        Assert.Equal(1, EmiVox.Syllables("!!!"));
        Assert.Equal(1, EmiVox.Syllables(""));
        Assert.True(EmiVox.Syllables("responsibilities") <= 4);
    }

    /// <summary>
    /// THE RENDER IS A REAL BUFFER: long enough to hold the last blip, never silent, never
    /// clipped. A burst that came back all zeroes would be a feature that "works" and makes no
    /// sound, which is exactly the bug nobody files.
    /// </summary>
    [Fact]
    public void TheRenderedBurstHasAudioInIt()
    {
        var score = EmiVox.MakeScore("bye bye. see you at school.", "celebration");
        Assert.NotEmpty(score);

        var buf = EmiVox.RenderBurst(score);
        Assert.True(buf.Length > 44100 * (score[^1].AtMs / 1000.0),
            "the buffer ended before the last blip did");

        double peak = buf.Aggregate(0.0, (a, s) => Math.Max(a, Math.Abs(s)));
        Assert.True(peak > 0.01, "the burst rendered silence");
        Assert.True(peak <= 1.0, "the burst clipped");
    }

    /// <summary>The WAV wrapper is the shape NAudio opens with no codec: mono, 16 bit, 44.1 kHz.</summary>
    [Fact]
    public void TheWavHeaderIsPlainMonoPcm()
    {
        var wav = EmiVox.WriteWav(EmiVox.RenderTick());
        Assert.True(wav.Length > 44);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(1, BitConverter.ToUInt16(wav, 20));      // PCM
        Assert.Equal(1, BitConverter.ToUInt16(wav, 22));      // mono
        Assert.Equal(44100u, BitConverter.ToUInt32(wav, 24));
        Assert.Equal(16, BitConverter.ToUInt16(wav, 34));
    }
}
