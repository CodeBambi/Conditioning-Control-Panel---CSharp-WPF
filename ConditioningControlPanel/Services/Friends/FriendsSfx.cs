using System;
using System.IO;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>
/// The friends feature's cues, on the <see cref="Launcher.LauncherSfx"/> shape: one-shots through
/// <c>App.Audio.PlayOneShot</c>, silent when output is suppressed, the master volume is 0, a
/// mandatory video is playing, Emi's hold is up, or the file is missing. Never throws.
///
/// <para>Incoming cues (<see cref="PokeIn"/>, <see cref="Knock"/>, <see cref="Request"/>) stand
/// 1.5 s apart and play at half level during a session (<see cref="FriendsSfxGate"/>). Whether one
/// plays at all (tray-hidden, held by a lockdown) is the landing router's call: it only reaches
/// these while something of ours is on screen. Friends coming online stay silent.</para>
/// </summary>
public static class FriendsSfx
{
    private static readonly object Gate = new();
    private static readonly FriendsSfxGate Rules = new();

    public static void DrawerOpen() => Play("chaos/cards_in.mp3", 0.14f, "friends-open");
    public static void DrawerClose() => Play("chaos/ui_unequip.mp3", 0.10f, "friends-close");
    public static void Click() => Play("chaos/ui_click.mp3", 0.12f, "friends-click");
    public static void Sent() => Play("chaos/chip_pop.mp3", 0.16f, "friends-sent");
    public static void Accepted() => Play("chaos/ui_unlock.mp3", 0.18f, "friends-accepted");
    public static void Dismiss() => Play("chaos/sink.mp3", 0.10f, "friends-sink");
    public static void Denied() => Play("chaos/ui_denied.mp3", 0.14f, "friends-denied");
    public static void Join() => Play("chaos/reveal_chime.mp3", 0.20f, "friends-join");

    /// <summary>A poke landed. Quieter when it lands over a game.</summary>
    public static void PokeIn(bool inGame) => Play("bubbles/Pop3.mp3", inGame ? 0.10f : 0.16f, "friends-poke-in", incoming: true);

    /// <summary>An invite or a watch knocked.</summary>
    public static void Knock() => Play("chaos/dling.mp3", 0.18f, "friends-knock", incoming: true);

    /// <summary>A friend request came in.</summary>
    public static void Request() => Play("chaos/dling.mp3", 0.16f, "friends-request", incoming: true);

    private static bool Audible(out float master)
    {
        master = 0f;
        try
        {
            var audio = App.Audio;
            if (audio == null || audio.IsOutputSuppressed) return false;
            if (App.Video?.IsPlaying == true) return false;   // the mandatory video owns the room
            int level = App.Settings?.Current?.MasterVolume ?? 0;
            if (level <= 0) return false;
            if (EmiLineEngine.Instance.HoldActive) return false;
            master = Math.Clamp(level / 100f, 0f, 1f);
            return master > 0f;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Friends] sfx audibility probe failed");
            return false;
        }
    }

    private static void Play(string rel, float scale, string tag, bool incoming = false)
    {
        try
        {
            if (!Audible(out var master)) return;
            lock (Gate)
            {
                if (!Rules.TryPass(DateTime.UtcNow, incoming)) return;
            }
            var path = ModResourceResolver.ResolveAudioPath(rel);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var level = master * scale * FriendsSfxGate.Level(incoming, App.IsSessionRunning);
            App.Audio?.PlayOneShot(path, Math.Clamp(level, 0f, 1f), tag);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Friends] sfx {Tag} failed", tag);
        }
    }
}
