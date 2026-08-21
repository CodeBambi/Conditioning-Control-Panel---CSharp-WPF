namespace CcpClient.Desktop.Features.Goon;

/// <summary>
/// SP-130: the four owner-gated doors of the Goon title screen, refused TYPED.
///
/// <para><b>Why this file exists at all.</b> Practice is one of the title menu's items and four
/// of its siblings lead into goon-game-census.md §6, which is owner-gated and unanswered.
/// <c>client/docs/capability-inventory.md:78</c> governs: <i>"A stub that says running is a
/// failure."</i> So each of the four gets a refusal that names what is missing — never a dead
/// button, never a silent no-op, never a "coming soon" that implies work rather than a
/// decision.</para>
///
/// <para><b>THE TRAP THIS FILE IS THE ANSWER TO.</b> The page already refuses these four — and
/// every one of its sentences is FALSE in this build. Opened, verbatim:
/// <c>ui/strings.js:41</c> <i>"hosting is a supporter perk"</i> (the dimmed Host item,
/// <c>ui/screens/title.js:71-84</c>); <c>ui/sheets.js:116-170</c> maps every reachable Join
/// failure to <i>"could not reach the server / check your connection"</i>, <i>"warming up …
/// try again in a minute"</i> or <i>"signed out"</i>; <c>ui/strings.js:731</c>
/// (<c>S.voice.screenNoPerk</c>, appended by <c>ui/screens/voice.js:138-140</c> when
/// <c>canSend()</c> is false) <i>"sending your voice to an opponent is a supporter perk"</i>;
/// and <c>ui/strings.js:751-752</c> <i>"compression lives in the app … this page is running in
/// a plain browser"</i> (<c>ui/screens/assets.js:83</c>, <c>:587-599</c>). Three imply a
/// purchase would open the door; the fourth is simply untrue — the page IS running in the app.
/// There is no host→page route by which a host can substitute its own sentence: the one
/// server-driven free-text slot, <c>ui/sheets.js:164-170</c>'s <c>detail</c> parenthetical,
/// is populated only for HTTP 402 (<c>net/signaling.js:542</c>). Correcting the strings would
/// mean forking payload bytes, which is a blocking defect, not a shortcut. <b>So the true
/// sentence lives in host chrome, and this type is where it is written once.</b></para>
///
/// <para><b>The caps frame is COMPUTED from this set, never written beside it</b>
/// (<see cref="CapsFor"/>). Deleting a refusal re-opens its door on the wire — which is the
/// point: a door cannot be re-opened without deleting the refusal that explains why it is
/// shut, and <c>GoonPracticeTests</c> pins exactly that by deriving caps from a REDUCED set.
/// A literal-equality pin over hard-coded caps would not red on that edit.</para>
/// </summary>
public static class GoonDoors
{
    /// <summary>The four title-screen doors this build refuses. (The title screen renders up to
    /// EIGHT items — <c>ui/screens/title.js:44-98</c>: Host, Join, Practice, Assets, Voice,
    /// Options, How-it-works and Quit. Its own header comment at <c>:4</c> says "a five-item
    /// menu" and is stale; the other four items need no refusal — Practice is this unit,
    /// Options/How-it-works/Quit are page-local.)</summary>
    public enum GoonDoor
    {
        /// <summary>Mint a room and wait for an opponent (census §6.1, D243).</summary>
        Host,

        /// <summary>Enter someone else's room code (census §6.1, D243).</summary>
        Join,

        /// <summary>Record a 10 s voice note (census §6.3, D244).</summary>
        VoiceNotes,

        /// <summary>Compress and send your own media to an opponent (census §6.2, D248).</summary>
        MediaSetup,
    }

    /// <summary>
    /// One typed refusal. <paramref name="Missing"/> is the load-bearing field — it names what
    /// is ABSENT, in the port, in this build; it is never a schedule, a price or an apology.
    /// <paramref name="PageSaysInstead"/> records the page's own (false-here) sentence beside
    /// it so the two can never drift apart unnoticed.
    /// </summary>
    /// <param name="Door">Which title-screen item this refuses.</param>
    /// <param name="Headline">The one-line refusal, as shown in host chrome.</param>
    /// <param name="Missing">What this build does not have. The honesty payload.</param>
    /// <param name="OwnerGate">The census section and divergence row that own the decision.</param>
    /// <param name="PageSaysInstead">The page's own sentence, with its citation — false here.</param>
    public sealed record GoonDoorRefusal(
        GoonDoor Door,
        string Headline,
        string Missing,
        string OwnerGate,
        string PageSaysInstead);

    /// <summary>The refusals this build ships. Four, one per owner-gated door.</summary>
    public static readonly IReadOnlyList<GoonDoorRefusal> Refused =
    [
        new GoonDoorRefusal(
            GoonDoor.Host,
            "Hosting a duel is not in this build.",
            "This build has no outbound network connection of any kind: no duel server client, "
            + "no signaling, no peer connection. Nothing here is waiting on a payment or a sign-in.",
            "goon-game-census.md §6.1 / wpf-surface-reachability.md D243 — the port's first "
            + "outbound network boundary is an owner decision that has not been made.",
            "The page dims Host and says \"hosting is a supporter perk\" (ui/strings.js:41, "
            + "ui/screens/title.js:71-84). That is upstream's tier-2 gate, and it is false here: "
            + "there is no tier to buy."),
        new GoonDoorRefusal(
            GoonDoor.Join,
            "Joining a duel is not in this build.",
            "The same missing piece as Host: there is no server to ask for a room and no way to "
            + "reach another player's machine. Joining is free upstream; here it is absent.",
            "goon-game-census.md §6.1 / wpf-surface-reachability.md D243 — one owner decision "
            + "covers both doors.",
            "The page can only report a network, server or account fault (ui/sheets.js:116-170) "
            + "— \"could not reach the server\", \"warming up\", \"signed out\". All three blame "
            + "something that exists; nothing here does."),
        new GoonDoorRefusal(
            GoonDoor.VoiceNotes,
            "Voice notes are not in this build.",
            "Recording one opens a microphone. This build opens no capture device of any kind "
            + "and grants no microphone permission to the page (the shipping app grants it "
            + "unprompted; this host does not).",
            "goon-game-census.md §6.3 / wpf-surface-reachability.md D244. capability-inventory.md:69 "
            + "ends \"Audio capture is never opened.\" — a PROHIBITION, not an open question; what is "
            + "undecided is only its scope, because it sits inside 'Webcam, face, and gaze tracking' "
            + "(:66) in a bullet about frames and biometric derivatives. Either way :70 gates a "
            + "microphone as expanding sensors, which needs owner review.",
            "The voice screen says \"sending your voice to an opponent is a supporter perk\" "
            + "(ui/strings.js:731 via ui/screens/voice.js:138-140). Upstream gates only the "
            + "SENDING; here the whole feature is absent."),
        new GoonDoorRefusal(
            GoonDoor.MediaSetup,
            "Sending your own media is not in this build.",
            "There is no compression queue and no peer channel to send over, so nothing is "
            + "prepared for sending and nothing can leave. Your media is listed for this "
            + "machine's own page and goes nowhere else.",
            "goon-game-census.md §6.2 / wpf-surface-reachability.md D248 — user media reaching "
            + "another person is an owner decision.",
            "The assets screen says \"compression lives in the app … this page is running in a "
            + "plain browser\" (ui/strings.js:751-752, ui/screens/assets.js:83). It is running "
            + "in the app; upstream has no card for \"in the app, no cache\"."),
    ];

    /// <summary>True when <paramref name="door"/> is refused by <paramref name="refusals"/>.</summary>
    public static bool IsRefused(IEnumerable<GoonDoorRefusal> refusals, GoonDoor door) =>
        refusals.Any(r => r.Door == door);

    /// <summary>The refusal for one door, or null when it is not refused.</summary>
    public static GoonDoorRefusal? For(IEnumerable<GoonDoorRefusal> refusals, GoonDoor door) =>
        refusals.FirstOrDefault(r => r.Door == door);

    /// <summary>
    /// The <c>caps</c> block of the <c>init</c> frame, <b>computed from the refusal set</b>.
    ///
    /// <para>Five members are transcribed constants with no door behind them
    /// (<c>GoonHostService.cs:333-337</c>). Three are DERIVED, and each derivation is the
    /// upstream predicate re-asked locally:</para>
    /// <list type="bullet">
    /// <item><c>canHost</c> — upstream <c>HostingAllowed()</c> (<c>:339</c> → <c>:909</c>) asks
    /// tier 2. Here: is the Host door refused?</item>
    /// <item><c>mediaTransfer</c> — upstream <c>TransferAllowed()</c> (<c>:338</c> → <c>:894</c>)
    /// asks tier 1, and <b>voice notes ride the same cap</b> (<c>ui/screens/voice.js:125</c>,
    /// <c>ui/screens/lobby.js:403</c>), so BOTH doors gate it.</item>
    /// <item><c>assetCache</c> — not a C#-host field at all; taken from the standalone shape
    /// (<c>bridge.js:420</c>) because this build attaches no cache bridge (upstream:
    /// <c>GoonHostService.cs:383</c>). Here: is the media-setup door refused?</item>
    /// </list>
    /// </summary>
    public static GoonCaps CapsFor(IEnumerable<GoonDoorRefusal> refusals)
    {
        var set = refusals as IReadOnlyCollection<GoonDoorRefusal> ?? refusals.ToList();
        return new GoonCaps(
            // haptics v2 is not merged; toy-* are stubs (GoonHostService.cs:333). NOTE the
            // consequence, stated rather than left to be inferred: boot.js:580 only advertises
            // GoonElement.ToyPatterns when this is truthy, so a duel here exercises EIGHT of the
            // nine element kinds in core/contracts.js:9-19 — true of the shipping WPF host too.
            Haptics: false,
            // GoonHostService.cs:334 -> BrainDrainAllowed() at :880, which is literally `=> true`.
            BrainDrain: true,
            // :335 — in-page spiral veil; exec/ owns the renderer.
            Spiral: true,
            // :336 — no webcam bridge into the page in v1. boot.js:609 drops StaringContest.
            Camera: false,
            // :337.
            Video: true,
            MediaTransfer: !IsRefused(set, GoonDoor.VoiceNotes) && !IsRefused(set, GoonDoor.MediaSetup),
            CanHost: !IsRefused(set, GoonDoor.Host),
            AssetCache: !IsRefused(set, GoonDoor.MediaSetup));
    }

    /// <summary>This build's caps, over this build's refusals.</summary>
    public static GoonCaps Caps => CapsFor(Refused);

    /// <summary>
    /// The refusal a <c>net-post</c> frame is answered with. Derived, not chosen: every server
    /// call the page can make belongs to Host or Join (<c>net/signaling.js:23-29</c> — the five
    /// frozen paths /invite, /join, /leave, /signal and /relay), so the
    /// Join refusal is the sentence the host has to hand. Null when nothing is refused, which
    /// is the state in which this host would have no honest answer to give.
    /// </summary>
    public static GoonDoorRefusal? NetRefusalFor(IEnumerable<GoonDoorRefusal> refusals) =>
        For(refusals, GoonDoor.Join) ?? For(refusals, GoonDoor.Host);

    /// <summary>This build's net-post refusal.</summary>
    public static GoonDoorRefusal? NetRefusal => NetRefusalFor(Refused);
}

/// <summary>
/// The <c>init</c> frame's <c>caps</c> block — eight members, exactly the seven at
/// <c>GoonHostService.cs:333-339</c> plus <c>assetCache</c> from <c>bridge.js:420</c>.
/// <c>solo</c> is deliberately NOT here: it is a frame-ROOT field beside <c>type</c> and
/// <c>protocol</c> (<c>bridge.js:388-391</c>, read at <c>boot.js:323</c> as <c>m.solo</c>),
/// and putting it in caps would invent a shape in the one packet whose rule is
/// transcribe-never-invent.
/// </summary>
public sealed record GoonCaps(
    bool Haptics,
    bool BrainDrain,
    bool Spiral,
    bool Camera,
    bool Video,
    bool MediaTransfer,
    bool CanHost,
    bool AssetCache);
