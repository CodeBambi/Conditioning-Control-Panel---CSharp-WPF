using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Threading;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>One frame of a chain: a face, a hold in ms, and the frame's options.</summary>
/// <param name="Text">The kaomoji (or special string) on the glass for this frame.</param>
/// <param name="Ms">How long the frame is held, in milliseconds.</param>
/// <param name="Small">THINKING dots mode (30 % size, -28 % lift).</param>
/// <param name="SetsBubble">
/// True when this frame carries a bubble instruction. face.js/chains.js distinguish
/// "no bubble key" (leave the bubble alone) from "bubble: null" (clear it); C# has no
/// `'bubble' in o`, so the flag carries that difference.
/// </param>
/// <param name="Bubble">The bubble text, or null to clear it, when <paramref name="SetsBubble"/>.</param>
public sealed record EmiFrame(string Text, int Ms, bool Small = false, bool SetsBubble = false, string? Bubble = null);

/// <summary>
/// A chain: the sequence, the pose held for the whole run, and the one-shot fx / body move.
/// Ported verbatim from <c>Resources/web/arcademy/emi/chains.js</c>.
/// </summary>
/// <param name="Id">Chain id, the key in <see cref="EmiChains.Chains"/>.</param>
/// <param name="Label">Human label from the lock (kept for logs and the dev pass).</param>
/// <param name="Seq">The frames.</param>
/// <param name="Fx">One-shot fx kind at frame 0 (<c>hearts</c>, <c>sparks</c>, <c>tears</c>, <c>storm</c>, <c>bang</c>).</param>
/// <param name="Move">One-shot body move at frame 0 (<c>bounce</c>, <c>nod</c>, <c>droop</c>, <c>shiver</c>, <c>thud</c>).</param>
/// <param name="Flat">Suppress the 90 degree rotation of classic sideways faces for every frame.</param>
/// <param name="BodyFrame">The pose held for the WHOLE chain (a per-frame pose reads as a broken sprite).</param>
public sealed record EmiChain(
    string Id,
    string Label,
    IReadOnlyList<EmiFrame> Seq,
    string? Fx = null,
    string? Move = null,
    bool Flat = false,
    string? BodyFrame = null);

/// <summary>Callbacks a chain drives. All optional; a null hook is a no-op.</summary>
public sealed class EmiChainHooks
{
    /// <summary>Paint a face: (text, small, flat).</summary>
    public Action<string, bool, bool>? Draw;

    /// <summary>Set the speech bubble; null clears it.</summary>
    public Action<string?>? Bubble;

    /// <summary>Play a one-shot body move (bounce / nod / droop / shiver / thud).</summary>
    public Action<string>? Move;

    /// <summary>Play a one-shot fx burst (hearts / sparks / tears / storm / bang).</summary>
    public Action<string>? Fx;

    /// <summary>Swap the body pose PNG (a key of <see cref="EmiChains.BodyFrameFile"/>).</summary>
    public Action<string>? BodyFrame;

    /// <summary>Fires after the LAST frame's hold has elapsed. Never fires on Cancel.</summary>
    public Action? Done;
}

/// <summary>
/// The canon face sets, the canon chains, the pose map, and the chain player.
///
/// VERBATIM from <c>Resources/web/arcademy/emi/chains.js</c> plus the pose/sway data from
/// <c>widget.js</c>. These strings and millisecond numbers ARE the design (EMI-DESIGN-LOCK.md,
/// owner-approved 2026-08-23). Do not retune them here; a retune is an owner call and lands in
/// the lock first.
///
/// Talk rule (locked): EMI never mouths words. Words go in the SPEECH BUBBLE. The face stays
/// <c>0_0</c> while the bubble types <c>.</c> <c>..</c> <c>...</c> and switches to the reaction
/// face when the line lands. Wordless yes = NOD. Timer / load = THINKING dots. Noticing the
/// player = GLANCE.
/// </summary>
public static class EmiChains
{
    // ---------------------------------------------------------------- face sets

    /// <summary>FLAT (everyday): 16 ascii + 3 promoted round-eye faces. CUT: OwO, UwU.</summary>
    public static readonly IReadOnlyList<string> Flat = new[]
    {
        "._.", "^_^", "^_~", ">.<", "@_@", "-_-", "o_o", "T_T", ">_<", "=_=", "\u00AC_\u00AC",
        "^___^", "x_x", "*_*", "0_0", ";_;", "(\u25C9_\u25C9)", "(\u2299_\u2299)", "(\u25D4_\u25D4)"
    };

    /// <summary>KAOMOJI (rare / special): the 11 picked. Rendered +10 % size and +10 % lift.</summary>
    public static readonly IReadOnlyList<string> Kao = new[]
    {
        "( \u0361\u00B0 \u035C\u0296 \u0361\u00B0)", "(\u00AC\u203F\u00AC)", "(\u25E0\u203F\u25E0)",
        "(\u2310\u25A0_\u25A0)", "(\u0CA0\u203F\u0CA0)", "(\u2716\u256D\u256E\u2716)",
        "(\u273F\u25E1\u203F\u25E1)", "(\u25D5\u203F\u25D5)", "(\u0CA5_\u0CA5)",
        "(\uFF61\u2665\u203F\u2665\uFF61)", "(\u2267\u25E1\u2266)"
    };

    /// <summary>SIDEWAYS CLASSIC (kept, secondary). These auto-rotate 90 degrees.</summary>
    public static readonly IReadOnlyList<string> Side = new[]
    {
        ":)", ":D", ";)", ":'(", ">:(", ":O", ":P", ":|", "<3", "XD", ":3", ">:)", ":/", "B)"
    };

    /// <summary>SPECIAL / EVENT TEXT (plus any release-name string).</summary>
    public static readonly IReadOnlyList<string> Special = new[]
    {
        "\\o/", "GG", "#ERR", "ZzZ", "!!!", "???", "LV UP", "6.7",
        "\u2665\u2665\u2665", "\u2605\u2605\u2605", "404", "brb"
    };

    /// <summary>The settled, pleased face a wordless positive beat lands on.</summary>
    public const string SettleFace = "^_^";

    /// <summary>The resting face: what she wears when nothing is happening.</summary>
    public const string RestFace = "0_0";

    // ---------------------------------------------------------------- body poses

    // ------------------------------------------------------------ THE SKIN LAW
    //
    // THE OUTFIT / SKIN LAYER IS THE TOPMOST THING IN EMI'S COMPOSITION. It is drawn above the
    // face and above the glass. Face art may never paint over a garment.
    //
    // The bug that wrote the law down (owner, 2026-08-30): she wore the labcoat and the collar
    // was BURIED under her screen. Her face is not part of the body PNG - it is a canvas laid
    // over the glass rect, and the takeover glass is a second canvas on the same rect - so
    // anything a garment draws across that rect is behind two layers of her own face unless the
    // garment gets a layer of its own IN FRONT of them. A coat behind the screen is not a coat.
    //
    // The desk obeys it structurally rather than by convention: `OutfitOverImage` in
    // EmiDeskWindow.xaml is authored AFTER `FaceLayer` inside `BodyRoot`, and
    // EmiDeskLayerOrderTests pins that order, so a XAML edit that reorders them fails the suite
    // instead of shipping a buried collar. The web twin is `.emi-over` at z-index 2 (widget.css)
    // over the face canvas at z1; same law, same stack.

    /// <summary>
    /// The pose sheet. Every frame is the same 859x869 canvas with the same screen rect, so the
    /// face lands in exactly the same place whichever one is up. <c>body.png</c> keeps its name
    /// and is CEREMONY ONLY. Values are file names under
    /// <c>Resources/web/arcademy/art/emi/</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> BodyFrameFile =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["celebration"] = "body.png",       // arms up: stamps, wins, reveals, LV UP
            ["idle"] = "body-idle.png",         // arms down: THE RESTING POSE + sway centre
            ["sad"] = "body-sad.png",           // cry, K.O., a broken streak, the exit flinch
            ["shock"] = "body-shock.png",       // shock, wake, rage, glitch, a rare drop
            ["smug"] = "body-smug.png",         // smug, suspicious, the dork-canon lines
            ["pet"] = "body-pet.png",           // pets, love, the pet streak
            ["sway1"] = "body-sway1.png",
            ["sway2"] = "body-sway2.png",
            ["sway3"] = "body-sway3.png",
            ["sway4"] = "body-sway4.png"
        };

    /// <summary>The idle sway walk. The centre frame is HELD; every other step is one step-ms.</summary>
    public static readonly IReadOnlyList<string> SwayCycle = new[]
    {
        "idle", "sway2", "sway1", "sway2", "idle", "sway3", "sway4", "sway3"
    };

    /// <summary>One step of the ping-pong sway walk, in ms.</summary>
    public const int SwayStepMs = 200;

    /// <summary>The pause when the sway passes back through centre, low bound (ms).</summary>
    public const int SwayCentreMinMs = 600;

    /// <summary>The pause when the sway passes back through centre, high bound (ms).</summary>
    public const int SwayCentreMaxMs = 900;

    /// <summary>
    /// The default map: a face family to a pose, for everything that is NOT a chain with its own
    /// <see cref="EmiChain.BodyFrame"/> (raw holds, and every line <see cref="MakeSay"/> builds,
    /// which is resolved frame by frame so the typing dots stay at idle and the pose lands with
    /// the reaction face). Anything absent is idle, deliberately: a face nobody paired is a face
    /// she has no strong feeling about.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> FaceBodyFrame =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // celebration
            ["^_^"] = "celebration",
            ["^___^"] = "celebration",
            ["^_~"] = "celebration",
            ["\\o/"] = "celebration",
            ["GG"] = "celebration",
            ["LV UP"] = "celebration",
            ["\u2605\u2605\u2605"] = "celebration",
            [":D"] = "celebration",
            ["XD"] = "celebration",
            // sad
            [";_;"] = "sad",
            ["T_T"] = "sad",
            ["x_x"] = "sad",
            ["(\u0CA5_\u0CA5)"] = "sad",
            ["(\u2716\u256D\u256E\u2716)"] = "sad",
            [":'("] = "sad",
            // shock
            ["o_o"] = "shock",
            ["(\u25C9_\u25C9)"] = "shock",
            ["(\u2299_\u2299)"] = "shock",
            [">_<"] = "shock",
            [">.<"] = "shock",
            ["!!!"] = "shock",
            ["???"] = "shock",
            ["#ERR"] = "shock",
            ["404"] = "shock",
            [":O"] = "shock",
            // smug
            ["\u00AC_\u00AC"] = "smug",
            ["(\u00AC\u203F\u00AC)"] = "smug",
            ["(\u0CA0\u203F\u0CA0)"] = "smug",
            ["(\u2310\u25A0_\u25A0)"] = "smug",
            ["( \u0361\u00B0 \u035C\u0296 \u0361\u00B0)"] = "smug",
            ["(\u25D4_\u25D4)"] = "smug",
            ["B)"] = "smug",
            [">:)"] = "smug",
            // pet
            ["(\uFF61\u2665\u203F\u2665\uFF61)"] = "pet",
            ["(\u273F\u25E1\u203F\u25E1)"] = "pet",
            ["(\u2267\u25E1\u2266)"] = "pet",
            ["(\u25E0\u203F\u25E0)"] = "pet",
            ["(\u25D5\u203F\u25D5)"] = "pet",
            ["*_*"] = "pet",
            ["\u2665\u2665\u2665"] = "pet",
            ["<3"] = "pet"
        };

    /// <summary>The pose a raw face string wears. Unpaired faces rest at idle.</summary>
    public static string FrameForFace(string? text)
        => text != null && FaceBodyFrame.TryGetValue(text, out var f) ? f : "idle";

    /// <summary>The pose sheet key, or null when it is not one of ours (never throws on junk).</summary>
    public static string? FrameKey(string? key)
        => key != null && BodyFrameFile.ContainsKey(key) ? key : null;

    /// <summary>
    /// Absolute path to a pose PNG next to the exe, or null when the art is missing. The art lives
    /// under <c>Resources/web/arcademy/art/emi/</c> and ships as Content (see the csproj's
    /// <c>Resources\web\**\*</c> glob), so it is beside the binary at runtime.
    /// </summary>
    public static string? BodyPath(string? frame)
    {
        try
        {
            var key = FrameKey(frame) ?? "idle";
            var path = Path.Combine(AppContext.BaseDirectory,
                "Resources", "web", "arcademy", "art", "emi", BodyFrameFile[key]);
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] body path lookup failed for {Frame}", frame);
            return null;
        }
    }

    // ------------------------------------------------------- the outfit overlay

    /// <summary>
    /// THE WARDROBE SHEETS THAT EXIST AS ART, and the whole of what the desk knows about outfits.
    ///
    /// <para><b>The desk wears none of them today.</b> Nothing in <c>Services/EmiDesk/</c> or
    /// <c>Windows/EmiDesk/</c> picks an outfit, prices one, unlocks one or draws one: the desk
    /// paints the standard ten-pose set and nothing else. These four names are the campus's
    /// (<c>OUTFITS</c> in <c>Resources/web/arcademy/emi/widget.js</c>), written down here so that
    /// the day a sheet does reach the desk it resolves through the SAME contract the web already
    /// uses rather than through a second one invented on the spot. Do not read a wardrobe, a
    /// picker or a purchase into this list; there is none on this side.</para>
    ///
    /// <para><b>The ART, however, is already here.</b> The desk renders out of the campus's own
    /// <c>Resources/web/arcademy/art/emi/</c> tree, which ships as Content, so all four sheets -
    /// and the ten <c>swim/over-body-*.png</c> overlay frames - are sitting beside the exe right
    /// now. <see cref="OverPath"/> finds them, which is why this layer is a real guarantee and not
    /// a placeholder: hand <c>EmiDeskWindow.SetOutfit</c> the name "swim" and the goggles go up,
    /// over her face, where they belong.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> Outfits = new[] { "varsity", "labcoat", "cheer", "swim" };

    /// <summary>
    /// The naming contract, shared with the campus verbatim. A sheet is one folder down from the
    /// standard art and keeps the standard file names - <c>&lt;outfit&gt;/body-idle.png</c> beside
    /// <c>body-idle.png</c> - and the part of the garment that crosses her glass ships as a
    /// sibling with an <c>over-</c> prefix: <c>&lt;outfit&gt;/over-body-idle.png</c>. That second
    /// file is the same 859x869 canvas, transparent everywhere except the prop, and it is what THE
    /// SKIN LAW above lays back over her face.
    ///
    /// <para><b>It is optional and it is silent.</b> Most sheets have no overlay and never will
    /// (on the web only <c>swim</c> ships one, for the goggles). A missing file is not an error:
    /// <see cref="OverPath"/> answers null, the layer stays collapsed, and the caller is expected
    /// to ask ONCE per outfit and cache the verdict for the sitting - never per frame.</para>
    /// </summary>
    public static string OverFileName(string? frame)
    {
        var key = FrameKey(frame) ?? "idle";
        return "over-" + BodyFrameFile[key];
    }

    /// <summary>Where a wardrobe sheet lives: one folder down from the standard art.</summary>
    public static string OutfitDir(string? outfit) => outfit ?? string.Empty;

    /// <summary>
    /// Absolute path to a garment's OVERLAY frame - the art that rides over her face - or null when
    /// there is no outfit, the name is junk, or the sheet simply has no overlay. Never throws.
    ///
    /// <para>Twin of <see cref="BodyPath"/> one folder down, and it deliberately does NOT fall back
    /// to the standard art: there is no standard overlay, and half an overlay is worse than none.
    /// The standard set answers null here for every pose, which is the correct answer.</para>
    /// </summary>
    public static string? OverPath(string? outfit, string? frame)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(outfit)) return null;
            var dir = OutfitDir(outfit);
            if (dir.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            var path = Path.Combine(AppContext.BaseDirectory,
                "Resources", "web", "arcademy", "art", "emi", dir, OverFileName(frame));
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] overlay path lookup failed for {Outfit}/{Frame}", outfit, frame);
            return null;
        }
    }

    /// <summary>The prototype hung a body move off each fx kind. Kept as data.</summary>
    public static readonly IReadOnlyDictionary<string, string> BodyForFx =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hearts"] = "bounce",
            ["sparks"] = "bounce",
            ["tears"] = "droop",
            ["storm"] = "shiver",
            ["bang"] = "bounce"
        };

    // ---------------------------------------------------------------- the chains

    private static EmiFrame F(string t, int ms) => new(t, ms);
    private static EmiFrame Fs(string t, int ms) => new(t, ms, Small: true);
    private static EmiFrame Fb(string t, int ms, string? bubble) => new(t, ms, SetsBubble: true, Bubble: bubble);

    /// <summary>
    /// CHAINS[id]. Every entry below is verbatim from chains.js except the three marked DESK,
    /// which the desk widget needs and the campus never had a use for.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, EmiChain> Chains =
        new Dictionary<string, EmiChain>(StringComparer.Ordinal)
        {
            ["wink"] = new("wink", "WINK", new[] { F("^_^", 420), F("^_~", 260), F("^_^", 600) },
                BodyFrame: "idle"),

            ["blink"] = new("blink", "BLINK (idle)", new[] { F("0_0", 1400), F("-_-", 110), F("0_0", 1200) },
                BodyFrame: "idle"),

            ["wake"] = new("wake", "WAKE UP",
                new[] { F("-_-", 500), F("o_o", 220), F("0_0", 260), F("(\u2299_\u2299)", 700) },
                BodyFrame: "shock"),

            ["shock"] = new("shock", "SHOCK",
                new[] { F("o_o", 160), F("0_0", 160), F("(\u25C9_\u25C9)", 900) },
                Fx: "bang", Move: "bounce", BodyFrame: "shock"),

            ["sus"] = new("sus", "SUSPICIOUS",
                new[] { F("\u00AC_\u00AC", 500), F("-_-", 220), F("\u00AC_\u00AC", 800) },
                BodyFrame: "smug"),

            ["thinking"] = new("thinking", "THINKING",
                new[]
                {
                    Fs(".", 260), Fs("..", 260), Fs("...", 260),
                    Fs(".", 260), Fs("..", 260), Fs("...", 420),
                    F("0_0", 900)
                },
                Flat: true, BodyFrame: "idle"),

            ["glance"] = new("glance", "GLANCE",
                new[] { F("0_0", 300), F("o_o", 640), F("0_0", 900) },
                BodyFrame: "idle"),

            ["nod"] = new("nod", "NOD", new[] { F("0_0", 1400) }, Move: "nod", BodyFrame: "idle"),

            ["say"] = new("say", "SAY (bubble)",
                new[]
                {
                    Fb("0_0", 420, "."), Fb("0_0", 420, ".."), Fb("0_0", 520, "..."),
                    Fb("^_^", 1800, "nice one!"), Fb("0_0", 200, null)
                },
                BodyFrame: "idle"),

            ["sayNod"] = new("sayNod", "SAY + NOD",
                new[]
                {
                    Fb("0_0", 420, "."), Fb("0_0", 420, ".."), Fb("0_0", 520, "..."),
                    Fb("0_0", 1800, "again?"), Fb("0_0", 200, null)
                },
                Move: "nod", BodyFrame: "idle"),

            ["cry"] = new("cry", "CRY", new[] { F(";_;", 500), F("T_T", 1400) },
                Fx: "tears", Move: "droop", BodyFrame: "sad"),

            ["rage"] = new("rage", "RAGE",
                new[] { F(">.<", 200), F(">_<", 200), F(">.<", 200), F(">_<", 700) },
                Fx: "storm", Move: "shiver", BodyFrame: "shock"),

            ["reveal"] = new("reveal", "EVENT REVEAL", new[] { F("._.", 420), F("0_0", 1200) },
                Fx: "sparks", Move: "bounce", BodyFrame: "celebration"),

            ["glitch"] = new("glitch", "GLITCH",
                new[]
                {
                    F("x_x", 90), F("#ERR", 90), F("@_@", 90),
                    F("#ERR", 90), F("x_x", 90), F("0_0", 600)
                },
                BodyFrame: "shock"),

            ["love"] = new("love", "LOVESTRUCK",
                new[] { F("0_0", 260), F("*_*", 420), F("(\uFF61\u2665\u203F\u2665\uFF61)", 1400) },
                Fx: "hearts", Move: "bounce", BodyFrame: "pet"),

            // GLEE is the lock's "three pets in a row" and "a streak stamp lands" beat: the
            // squeezed-eye kaomoji, not the lovestruck one.
            ["glee"] = new("glee", "GLEE", new[] { F("^_^", 300), F("(\u2267\u25E1\u2266)", 1400) },
                Fx: "hearts", Move: "bounce", BodyFrame: "celebration"),

            ["cool"] = new("cool", "COOL", new[] { F("-_-", 300), F("(\u2310\u25A0_\u25A0)", 1400) },
                Fx: "sparks", Move: "bounce", BodyFrame: "celebration"),

            ["dizzy"] = new("dizzy", "DIZZY",
                new[] { F("@_@", 260), F("=_=", 260), F("@_@", 260), F("x_x", 800) },
                BodyFrame: "idle"),

            ["smug"] = new("smug", "SMUG", new[] { F("^_^", 300), F("(\u00AC\u203F\u00AC)", 1200) },
                BodyFrame: "smug"),

            ["ko"] = new("ko", "K.O.",
                new[] { F(">_<", 220), F("x_x", 420), F("(\u2716\u256D\u256E\u2716)", 1400) },
                Fx: "tears", Move: "droop", BodyFrame: "sad"),

            // ---- DESK additions -------------------------------------------------
            // The campus builds these at the call site (widget.js `pet()` raws them); the desk
            // needs them as named chains because the ring, the glass and the offers all reach for
            // them by id. The frames are the ones widget.js raws, unchanged.

            // A single head-pat: the settled face, hearts, a bounce, the pet pose.
            ["pet"] = new("pet", "PET", new[] { F(SettleFace, 900) },
                Fx: "hearts", Move: "bounce", BodyFrame: "pet"),

            // The third pat inside the window: `glee`'s frames wearing the PET pose, which is what
            // widget.js does with its call-site bodyFrame override.
            ["petStreak"] = new("petStreak", "PET STREAK",
                new[] { F("^_^", 300), F("(\u2267\u25E1\u2266)", 1400) },
                Fx: "hearts", Move: "bounce", BodyFrame: "pet"),

            // Nobody has touched her in a long while. No line, no guilt: she just dozes.
            ["sleepy"] = new("sleepy", "SLEEPY",
                new[] { F("-_-", 700), F("=_=", 700), F("ZzZ", 1600) },
                BodyFrame: "idle")
        };

    /// <summary>Look a chain up by id; null for an unknown id (never throws).</summary>
    public static EmiChain? Get(string? id)
        => id != null && Chains.TryGetValue(id, out var c) ? c : null;

    // ---------------------------------------------------------------- say

    /// <summary>
    /// A one-off SAY chain. The <c>say</c> / <c>sayNod</c> entries above are templates for the
    /// tester; real speech is built here so the caller's line rides the same locked
    /// <c>.</c> / <c>..</c> / <c>...</c> cadence. Verbatim from chains.js <c>makeSay</c>.
    /// </summary>
    public static EmiChain MakeSay(string? line, string reactionFace = "^_^", int holdMs = 1800)
        => new("say", "SAY",
            new[]
            {
                Fb("0_0", 420, "."),
                Fb("0_0", 420, ".."),
                Fb("0_0", 520, "..."),
                Fb(string.IsNullOrEmpty(reactionFace) ? "^_^" : reactionFace,
                   Math.Max(400, holdMs), line ?? string.Empty),
                Fb("0_0", 200, null)
            });

    /// <summary>
    /// How long a landed line hangs, the campus formula (widget.js <c>sayHoldMs</c>, already
    /// carrying the owner's 2026-08-25 +35 %): floor 4050 ms, base 1890 ms, 61 ms per character.
    /// There is deliberately no ceiling; the growth is linear and her longest line is under 120
    /// characters.
    /// </summary>
    public static int SayHoldMs(string? line)
    {
        int len = line?.Length ?? 0;
        return Math.Max(4050, 1890 + 61 * len);
    }

    // ---------------------------------------------------------------- the player

    /// <summary>
    /// Plays one chain on the dispatcher, the WPF twin of chains.js <c>playChain</c>: fx fires
    /// once at frame 0 (when the chain has one), the body move fires once at frame 0, the pose is
    /// set once for the whole chain, and <see cref="EmiChainHooks.Done"/> fires after the LAST
    /// frame's hold. <see cref="Cancel"/> kills the pending timer and does NOT call Done.
    ///
    /// One player owns one surface: <see cref="Play"/> cancels whatever was running first.
    /// </summary>
    public sealed class Player : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private DispatcherTimer? _timer;
        private EmiChain? _chain;
        private EmiChainHooks? _hooks;
        private int _index;
        private bool _disposed;

        /// <summary>The chain currently running, or null.</summary>
        public EmiChain? Current => _chain;

        /// <summary>True while a chain is on screen.</summary>
        public bool IsLive => _chain != null;

        /// <param name="dispatcher">The surface's dispatcher. Every hook is raised on it.</param>
        public Player(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        /// <summary>
        /// Start a chain. <paramref name="bodyFrameOverride"/> is the call-site pose override the
        /// campus uses for the pet streak (a shared chain, a different pose).
        /// </summary>
        public void Play(EmiChain? chain, EmiChainHooks hooks, string? bodyFrameOverride = null)
        {
            if (_disposed) return;
            if (!_dispatcher.CheckAccess())
            {
                try { _dispatcher.BeginInvoke(new Action(() => Play(chain, hooks, bodyFrameOverride))); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chain marshal failed"); }
                return;
            }

            Cancel();
            if (chain == null || chain.Seq.Count == 0)
            {
                try { hooks?.Done?.Invoke(); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chain done hook threw"); }
                return;
            }

            _chain = chain;
            _hooks = hooks;
            _index = 0;

            try
            {
                hooks?.Bubble?.Invoke(null);            // a new chain always starts clean
                var pose = FrameKey(bodyFrameOverride) ?? FrameKey(chain.BodyFrame);
                if (pose != null) hooks?.BodyFrame?.Invoke(pose);
                if (!string.IsNullOrEmpty(chain.Move)) hooks?.Move?.Invoke(chain.Move!);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] chain setup hook threw");
            }

            Step();
        }

        private void Step()
        {
            if (_disposed || _chain == null) return;
            var chain = _chain;
            var hooks = _hooks;
            var frame = chain.Seq[_index];

            try
            {
                if (frame.SetsBubble) hooks?.Bubble?.Invoke(frame.Bubble);
                hooks?.Draw?.Invoke(frame.Text, frame.Small, chain.Flat);
                if (_index == 0 && !string.IsNullOrEmpty(chain.Fx)) hooks?.Fx?.Invoke(chain.Fx!);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] chain frame hook threw");
            }

            _index++;

            _timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(1, frame.Ms))
            };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            try
            {
                if (sender is DispatcherTimer t)
                {
                    t.Stop();
                    t.Tick -= OnTick;
                }
                if (_disposed || _chain == null) return;
                _timer = null;

                if (_index < _chain.Seq.Count)
                {
                    Step();
                    return;
                }

                var hooks = _hooks;
                _chain = null;
                _hooks = null;
                try { hooks?.Done?.Invoke(); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chain done hook threw"); }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] chain tick failed");
            }
        }

        /// <summary>Kill the running chain. Done is NOT called.</summary>
        public void Cancel()
        {
            try
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Tick -= OnTick;
                    _timer = null;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] chain cancel failed");
            }
            _chain = null;
            _hooks = null;
            _index = 0;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _disposed = true;
            Cancel();
        }
    }
}
