// PORTED from ConditioningControlPanel/Services/Friends/FriendsSfx.cs (7.1.5): the drawer's small sounds,
// same files and scales. One clock (FriendsSfxGate, Core): a 130 ms floor between any two, incoming
// ones further apart and halved while a session runs. Silent at master volume 0 or with no file.
// ponytail: WPF also stays silent while AudioService.IsOutputSuppressed, the mandatory video plays or
// an EMI line holds the room; this head has no such probes yet. No mod sound override (ChaosSfx's note).
using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Friends;

internal static class FriendsSfx
{
    private static readonly object Gate = new();
    private static readonly FriendsSfxGate Rules = new();

    /// <summary>Test seam: what a pass plays (path, level, tag). Null = CoreAudio.</summary>
    internal static Action<string, float, string>? Player { get; set; }

    public static void DrawerOpen() => Play("chaos/cards_in.mp3", 0.14f, "friends-open");
    public static void DrawerClose() => Play("chaos/ui_unequip.mp3", 0.10f, "friends-close");
    public static void Click() => Play("chaos/ui_click.mp3", 0.12f, "friends-click");
    public static void Sent() => Play("chaos/chip_pop.mp3", 0.16f, "friends-sent");
    public static void Accepted() => Play("chaos/ui_unlock.mp3", 0.18f, "friends-accepted");
    public static void Dismiss() => Play("chaos/sink.mp3", 0.10f, "friends-sink");
    public static void Denied() => Play("chaos/ui_denied.mp3", 0.14f, "friends-denied");
    public static void Join() => Play("chaos/reveal_chime.mp3", 0.20f, "friends-join");
    public static void PokeIn(bool inGame) => Play("bubbles/Pop3.mp3", inGame ? 0.10f : 0.16f, "friends-poke-in", incoming: true);
    public static void Knock() => Play("chaos/dling.mp3", 0.18f, "friends-knock", incoming: true);
    public static void Request() => Play("chaos/dling.mp3", 0.16f, "friends-request", incoming: true);

    private static void Play(string rel, float scale, string tag, bool incoming = false)
    {
        try
        {
            int level = CoreSettings.Current?.MasterVolume ?? 0;
            if (level <= 0) return;
            float master = Math.Clamp(level / 100f, 0f, 1f);
            lock (Gate) { if (!Rules.TryPass(DateTime.UtcNow, incoming)) return; }
            var path = ContentLocator.Resolve(System.IO.Path.Combine("Resources", "sounds", rel.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            if (Player == null && (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))) return;
            var vol = Math.Clamp(master * scale * FriendsSfxGate.Level(incoming, CoreSession.IsSessionRunning), 0f, 1f);
            if (Player != null) Player(rel, vol, tag);
            else CoreAudio.PlayOneShot(path!, vol, tag);
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] sfx {Tag} failed: {E}", tag, ex.Message); }
    }
}
