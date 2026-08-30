using System;
using System.IO;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// HER TEXTURE. The three tiny one-shots the desktop widget makes on its own: the pat, and the
/// ring fanning open and folding shut. Deliberately quiet - the owner asked for "some low volume
/// ones", and these fire far more often than anything else she does, so a cue loud enough to
/// notice twice is a cue that gets the whole widget muted.
///
/// <para>THE THREE LAWS IT INHERITS (primer 12.5, the same gate <c>EmiVox</c> keeps):</para>
/// <list type="number">
///   <item>Never mint a <c>WaveOutEvent</c> per cue. Everything routes through
///         <c>App.Audio.PlayOneShot</c>, which owns the device pool, the global voice cap and the
///         failure circuit breaker.</item>
///   <item>SAFETY IS SILENCE. <c>IsOutputSuppressed</c>, a <c>MasterVolume</c> of 0 and
///         <c>EmiLineEngine.HoldActive</c> each mute her - a texture that keeps chirping through a
///         panic hold is the feature reading as broken.</item>
///   <item>No new setting. The master volume already is one.</item>
/// </list>
///
/// <para>Like <see cref="Services.Chaos.ChaosSfx"/>, every cue is an override-then-fallback list:
/// drop a dedicated file at <c>Resources/sounds/emi/&lt;cue&gt;.mp3</c> (or .wav - the resolver
/// swaps extensions) and it wins automatically, with no code change. Until one ships, the cue
/// borrows the closest thing already in the repo, so the feature is audible today rather than
/// blocked on art. A cue whose whole chain is missing is a silent no-op, never an exception -
/// which is exactly why <c>EmiSfxAssetTests</c> asserts the last link of every chain exists.</para>
/// </summary>
public static class EmiSfx
{
    // ---------------------------------------------------------------- the cues

    /// <summary>Trim for the pat. The quietest of the three: it is the one that can be spammed.</summary>
    private const float PatScale = 0.12f;

    /// <summary>Trim for the fan opening.</summary>
    private const float OpenScale = 0.17f;

    /// <summary>Trim for the fan folding shut. Under the open on purpose - leaving is not an event.</summary>
    private const float CloseScale = 0.13f;

    /// <summary>
    /// The floor between two cues of the SAME kind. A pat is one click, and a click can be
    /// double-clicked or held down on a trackpad; without this the pat machine-guns. It is well
    /// under the 6 s pet cooldown, so it never silences a pat the user would call a pat - it only
    /// collapses the ones inside a single gesture.
    /// </summary>
    private const int MinGapMs = 130;

    private static readonly object Gate = new();
    private static DateTime _lastPat = DateTime.MinValue;
    private static DateTime _lastRing = DateTime.MinValue;

    /// <summary>Her head, touched. Fires on both triggers of the one gesture - the click pat and
    /// the 1.2 s hover pet - because the sound is the touch, not the performance it earns.</summary>
    public static void Pat()
    {
        if (!Throttle(ref _lastPat)) return;
        Play(new[] { "emi/pat.mp3", "chaos/chip_pop.mp3", "bubbles/Pop3.mp3" }, PatScale, "emi-sfx-pat");
    }

    /// <summary>The ring fanning out.</summary>
    public static void RingOpen()
    {
        if (!Throttle(ref _lastRing)) return;
        Play(new[] { "emi/ring_open.mp3", "chaos/cards_in.mp3", "chaos/reveal_chime.mp3" },
             OpenScale, "emi-sfx-ring");
    }

    /// <summary>The ring folding back into her.</summary>
    public static void RingClose()
    {
        if (!Throttle(ref _lastRing)) return;
        Play(new[] { "emi/ring_close.mp3", "chaos/ui_unequip.mp3", "chaos/sink.mp3" },
             CloseScale, "emi-sfx-ring");
    }

    // ---------------------------------------------------------------- the plumbing

    /// <summary>The candidate chains, exposed so the asset test can walk them without playing.</summary>
    public static string[][] AllChains() => new[]
    {
        new[] { "emi/pat.mp3", "chaos/chip_pop.mp3", "bubbles/Pop3.mp3" },
        new[] { "emi/ring_open.mp3", "chaos/cards_in.mp3", "chaos/reveal_chime.mp3" },
        new[] { "emi/ring_close.mp3", "chaos/ui_unequip.mp3", "chaos/sink.mp3" },
    };

    private static bool Throttle(ref DateTime slot)
    {
        var now = DateTime.UtcNow;
        lock (Gate)
        {
            if ((now - slot).TotalMilliseconds < MinGapMs) return false;
            slot = now;
            return true;
        }
    }

    /// <summary>
    /// The same audibility gate <c>EmiVox.Audible</c> keeps, for the same reasons. Kept as its own
    /// copy rather than shared: the vox is her VOICE and this is her UI, and the day one of them
    /// grows a rule the other must not inherit, a shared helper is the thing that gets it wrong.
    /// </summary>
    private static bool Audible(out float master)
    {
        master = 0f;
        try
        {
            var audio = App.Audio;
            if (audio == null || audio.IsOutputSuppressed) return false;

            int level = App.Settings?.Current?.MasterVolume ?? 0;
            if (level <= 0) return false;

            if (EmiLineEngine.Instance.HoldActive) return false;

            master = Math.Clamp(level / 100f, 0f, 1f);
            return master > 0f;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] sfx audibility probe failed");
            return false;
        }
    }

    private static void Play(string[] candidates, float scale, string tag)
    {
        if (!Audible(out var master)) return;

        try
        {
            foreach (var rel in candidates)
            {
                string path;
                try { path = ModResourceResolver.ResolveAudioPath(rel); }
                catch { continue; }

                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

                App.Audio?.PlayOneShot(path, Math.Clamp(master * scale, 0f, 1f), tag);
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] sfx {Tag} failed", tag);
        }
    }
}
