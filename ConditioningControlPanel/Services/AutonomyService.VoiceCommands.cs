using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Services.Speech;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// "Hey Bambi" voice COMMAND layer (v2) — the user-initiated mic (wake-word / push-to-talk)
    /// first listens against a closed command grammar and, if it hears one, drives an app feature
    /// and confirms in-character. Anything it doesn't recognise falls through to the existing
    /// Spoken-Mantra flow, so saying nothing useful still gets you a mantra.
    ///
    /// Stays squarely in the offline engine's sweet spot ("say a known thing -> trigger that"):
    /// the grammar is constrained to the intent aliases, so <see cref="SpeechService"/> returns one
    /// of them (or [unk]); we then pick the best intent with the same fuzzy <see cref="SpeechService.Similarity"/>
    /// the mantra mechanic uses. Self-protecting: needs speech available + loud speech to fire, and
    /// the safety word routes to the same teardown as the panic key, so a false positive is the SAFE
    /// direction.
    ///
    /// v2 adds: a much wider verb set (start/stop most features, one-shots, session + volume control),
    /// command chaining (a short follow-up window so you can stack commands without re-waking), a
    /// polite re-listen on a near-miss, "again"/"more" to repeat the last command, "what can I say"
    /// help, and terse acks for utility verbs.
    /// </summary>
    public partial class AutonomyService
    {
        /// <summary>An intent = a set of spoken aliases -> one app action + per-mod confirmation.</summary>
        private sealed class VoiceCommandIntent
        {
            public string Name = "";
            public string[] Aliases = Array.Empty<string>();
            /// <summary>Run on the UI thread when matched. Null + <see cref="IsMantra"/> = fall back to a mantra.</summary>
            public Action? Execute;
            /// <summary>When it returns true the command is refused: Execute is skipped and
            /// <see cref="BlockedConfirm"/> is spoken instead of <see cref="Confirm"/>.</summary>
            public Func<bool>? Blocked;
            /// <summary>mod-key -> refusal line used when <see cref="Blocked"/> fires.</summary>
            public Dictionary<string, string> BlockedConfirm = new();
            public bool IsMantra;
            /// <summary>"again"/"one more"/"more"/"harder" — re-run the last actionable command instead of a fixed action.</summary>
            public bool IsReplay;
            /// <summary>Don't open a follow-up (chaining) window after this one runs — e.g. panic, stop-listening.</summary>
            public bool NoChain;
            /// <summary>Eligible to be the target of a later "again"/"more". False for help, replay, panic, stop-listening.</summary>
            public bool Repeatable = true;
            /// <summary>Short, text-only confirmation (skips the voiced manifest lookup) — used for utility verbs
            /// like pause / mute / volume so they don't get a full giggled bark every time.</summary>
            public bool TerseAck;
            /// <summary>mod-key ("bambi"/"sissy"/"circe") -> confirmation line. Fallback text only —
            /// the live, voiced line is pulled from the bark manifest by <see cref="VoiceRuleId"/>.</summary>
            public Dictionary<string, string> Confirm = new();
            /// <summary>
            /// Bark-manifest rule id whose variant pool holds this command's voiced confirmations
            /// (text + per-mod audio). Picked via <see cref="BarkService.PickVoiceLine"/> so the
            /// spoken clip always matches the bubble. Null = use <see cref="Confirm"/>, text-only.
            /// </summary>
            public string? VoiceRuleId;
        }

        // Minimum fuzzy similarity. Similarity is WORD-level (1 - wordEditDistance/maxWords), so each
        // dropped/wrong word costs 1/maxWords — brutal on the short aliases that dominate this grammar.
        // At the old 0.62 a bare noun ("bubbles") scored only 0.5 vs its 2-word alias ("bubbles on") and
        // was rejected, as was any 2-word alias one word off — which silently ate roughly half of real
        // commands. Lowered to 0.5 = "at least half the words align", which recovers single-noun and
        // one-word-off utterances. This is safe to relax because recognition is GRAMMAR-CONSTRAINED:
        // Vosk can only emit alias words (or [unk]/empty), so non-commands fail at the engine, not here —
        // this gate mainly rejects [unk]/garbage. It never prevented command cross-talk anyway (near-
        // neighbours collide acoustically at ~1.0, above any threshold); distinct phrasing handles that.
        // Don't go below 0.5: that starts accepting phrases sharing under half their words.
        private const double VoiceCommandMatchThreshold = 0.5;

        // Utility / terse verbs (mute, unmute, volume, pause…) have short 2-word aliases that collide
        // with a half-garbled utterance on a SINGLE shared word — which scores exactly the 0.5 content
        // floor and false-fires (e.g. "sound mantra" hitting unmute's "sound on"). When genuinely said
        // they're distinctive multi-word phrases that clear a higher bar comfortably, so require it of
        // them while content nouns (bubbles, spiral…) keep the recall-first 0.5 floor. Safety (panic) is
        // NOT terse, so it stays at 0.5 and still fires easily — a false positive there just stops things.
        private const double UtilityCommandMatchThreshold = 0.6;

        // After a successful command we keep listening for a few quick follow-ups (no wake word needed)
        // so you can stack "bubbles ... flashes ... deeper" in one breath. Capped so it always winds down.
        private const int MaxChainedCommands = 3;

        // The last actionable command run this session — the target of "again" / "one more" / "more".
        private static VoiceCommandIntent? _lastVoiceIntent;

        private static List<VoiceCommandIntent>? _voiceCommandIntents;

        // The user asked to be held; voice must not be the way out (#706). Mirrors the contract
        // AvatarTubeWindow.IsEngineStopLocked() already enforces for the tube's Stop button, so the
        // two entry points can't disagree about whether a lock is honoured.
        //
        // Guard EVERY intent that reaches a privileged teardown, not just the obvious ones: the
        // original report was `video_off`, but session `pause` funnels into SessionEngine.PauseSession
        // -> App.Video.Stop() through a completely different file, and its aliases ("wait", "hold on")
        // are the ones a frustrated user actually reaches for. Panic stays unguarded — it is the
        // intended way out and routes to the same teardown on purpose.
        private static bool StopLocked() =>
            App.Lockdown?.IsActive == true ||
            App.Video?.IsStrictActive == true ||
            (App.BubbleCount?.IsBusy == true && App.Settings?.Current?.BubbleCountStrictLock == true);

        // ── Alias expansion ────────────────────────────────────────────────────
        // Toggle features share a wide, consistent spoken vocabulary so natural phrasings all land.
        // (The v1 intents notably lacked the bare "<noun> on" / "<noun> off" form, so "bubbles off"
        // scored ~0 and fell through — these templates always include it.) `bare` = the noun without an
        // article ("bubbles"); `the` = with an article ("the bubbles"); `extra` appends bespoke phrasings.
        // Deduped; the odd ungrammatical combo ("show me spiral") is harmless — Vosk only has to match
        // what a user might actually say, and unsaid phrasings just never fire.
        private static string[] OnAliases(string bare, string the, params string[] extra)
        {
            var list = new List<string>
            {
                $"{bare} on", $"turn on {the}", $"turn {the} on", $"start {the}", $"start {bare}",
                $"begin {the}", $"show me {bare}", $"show me {the}", $"give me {bare}", $"give me {the}",
                $"i want {bare}", $"bring up {the}", $"enable {bare}",
            };
            list.AddRange(extra);
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string[] OffAliases(string bare, string the, params string[] extra)
        {
            var list = new List<string>
            {
                $"{bare} off", $"turn off {the}", $"turn {the} off", $"stop {the}", $"stop {bare}",
                $"end {the}", $"no more {bare}", $"hide {the}", $"get rid of {the}", $"kill {the}",
                $"cut {the}", $"enough {bare}", $"disable {bare}",
            };
            list.AddRange(extra);
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>The command set. Built lazily; Execute closures call the static App services.</summary>
        private static List<VoiceCommandIntent> VoiceCommandIntents => _voiceCommandIntents ??= new()
        {
            // ── Safety ────────────────────────────────────────────────────────────
            // Routes to the exact panic-key teardown. A false positive just stops things.
            new VoiceCommandIntent
            {
                Name = "panic",
                Aliases = new[] { "red", "stop everything", "stop it all", "shut it all down", "safe word", "i'm done", "make it all stop", "make it stop", "everything off", "turn everything off", "kill everything", "that's too much", "i need to stop", "all stop", "emergency stop" },
                Execute = () => App.MainWindowRef?.TriggerPanicFromRemote(),
                VoiceRuleId = "voicecmd_panic",
                NoChain = true,
                Repeatable = false,
                Confirm = new()
                {
                    ["bambi"] = "okay okay! all stop~ you're safe, cutie",
                    ["sissy"] = "shh... everything's off. you're safe now, good girl.",
                    ["circe"] = "stopped. you're safe.",
                },
            },

            // ── Bubbles ───────────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "bubbles_on",
                Aliases = OnAliases("bubbles", "the bubbles", "show me some bubbles", "more bubbles"),
                Execute = () => App.Bubbles?.Start(bypassLevelCheck: true),
                VoiceRuleId = "voicecmd_bubbles_on",
                Confirm = new()
                {
                    ["bambi"] = "yay! bubbles~ pop pop pop!",
                    ["sissy"] = "mmm, bubbles for my good girl~",
                    ["circe"] = "bubbles. as you asked.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "bubbles_off",
                Aliases = OffAliases("bubbles", "the bubbles"),
                Execute = () => App.Bubbles?.Stop(),
                VoiceRuleId = "voicecmd_bubbles_off",
                Confirm = new()
                {
                    ["bambi"] = "aww, no more bubbles~ okayy!",
                    ["sissy"] = "all done, good girl.",
                    ["circe"] = "bubbles off.",
                },
            },

            // ── Video ─────────────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "video_on",
                Aliases = new[] { "show me a video", "play a video", "play a hypnotube video", "play hypnotube", "give me a video", "i want a video", "put on a video", "play me a video", "start a video", "play video", "video on", "show me a clip" },
                Execute = () => App.Video?.TriggerVideo(),
                VoiceRuleId = "voicecmd_video_on",
                Confirm = new()
                {
                    ["bambi"] = "ooh a video! watch closely~",
                    ["sissy"] = "eyes on the screen for me~",
                    ["circe"] = "watch. don't look away.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "video_off",
                Aliases = OffAliases("video", "the video", "no more videos", "stop playing"),
                // Strict lock / lockdown: the whole point is that the video can't be dismissed, and
                // VideoService.Stop() is a privileged teardown that clears _strictActive before the
                // window's Closing veto can see it — so the refusal has to live here (#706). Panic
                // stays unguarded: it routes to the same teardown and is the intended way out.
                Blocked = StopLocked,
                BlockedConfirm = new()
                {
                    ["bambi"] = "nuh-uh~ you locked yourself in, silly! keep watching~",
                    ["sissy"] = "no, good girl. you asked to be locked in — eyes on the screen.",
                    ["circe"] = "you locked it yourself. the video stays.",
                },
                Execute = () => App.Video?.Stop(),
                VoiceRuleId = "voicecmd_video_off",
                Confirm = new()
                {
                    ["bambi"] = "video's gone~ hehe",
                    ["sissy"] = "that's enough watching, good girl.",
                    ["circe"] = "off it goes.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "video_pause",
                Aliases = new[] { "pause the video", "pause video", "pause this video", "pause the clip", "hold the video" },
                // Pausing indefinitely neuters a strict video just as thoroughly as stopping it (#706).
                Blocked = StopLocked,
                BlockedConfirm = new()
                {
                    ["bambi"] = "hehe, no pausing this one~ keep those eyes open!",
                    ["sissy"] = "not this one, good girl. it plays through.",
                    ["circe"] = "no. it plays through.",
                },
                Execute = () => App.Video?.PausePrimary(),
                TerseAck = true,
                NoChain = true,
                Confirm = new()
                {
                    ["bambi"] = "video on hold~ hehe",
                    ["sissy"] = "paused for you, good girl~",
                    ["circe"] = "video paused.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "video_resume",
                Aliases = new[] { "resume the video", "play the video", "unpause the video", "continue the video", "resume video", "play the clip", "keep playing", "continue playing" },
                Execute = () => App.Video?.PlayPrimary(),
                TerseAck = true,
                NoChain = true,
                Confirm = new()
                {
                    ["bambi"] = "playing again~ watch!",
                    ["sissy"] = "eyes back on it, good girl~",
                    ["circe"] = "video resumed.",
                },
            },

            // ── Flashes (one-shot only) ───────────────────────────────────────────
            // The start/stop toggles were dropped on purpose: their aliases collided acoustically
            // with the one-shot below, and "flashes on" only arms the periodic flasher (which needs
            // a running session) — so she'd confirm but nothing visibly happened. The single-flash
            // trigger fires immediately and reliably. Same pattern for mind wipe / lock cards / quiz.
            new VoiceCommandIntent
            {
                Name = "flash_once",
                Aliases = new[] { "flash me", "one flash", "give me a flash", "flash once", "just one flash", "quick flash", "a single flash", "one quick flash" },
                // TriggerFlash() bails when the service isn't running; TriggerFlashOnce() is the
                // standalone one-shot (sets its own images path) so "flash me" fires without a session.
                Execute = () => App.Flash?.TriggerFlashOnce(),
                VoiceRuleId = "voicecmd_flash_once",
                Confirm = new()
                {
                    ["bambi"] = "blink~! hehe",
                    ["sissy"] = "there~ did you catch it, good girl?",
                    ["circe"] = "flash.",
                },
            },

            // ── Subliminals ─────────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "subliminals_on",
                Aliases = OnAliases("subliminals", "the subliminals"),
                Execute = () => App.Subliminal?.Start(),
                VoiceRuleId = "voicecmd_subliminals_on",
                Confirm = new()
                {
                    ["bambi"] = "sneaky words~ they go riiight in!",
                    ["sissy"] = "let the words sink in, good girl~",
                    ["circe"] = "subliminals on.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "subliminals_off",
                Aliases = OffAliases("subliminals", "the subliminals"),
                Execute = () => App.Subliminal?.Stop(),
                VoiceRuleId = "voicecmd_subliminals_off",
                Confirm = new()
                {
                    ["bambi"] = "okayy, words off~",
                    ["sissy"] = "that's enough for now, good girl.",
                    ["circe"] = "subliminals off.",
                },
            },

            // ── Bouncing text ───────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "bouncing_on",
                Aliases = OnAliases("bouncing text", "the bouncing text"),
                Execute = () => App.BouncingText?.Start(),
                VoiceRuleId = "voicecmd_bouncing_on",
                Confirm = new()
                {
                    ["bambi"] = "boing boing words~ wheee!",
                    ["sissy"] = "watch them dance for you~",
                    ["circe"] = "bouncing text on.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "bouncing_off",
                Aliases = OffAliases("bouncing text", "the bouncing text"),
                Execute = () => App.BouncingText?.Stop(),
                VoiceRuleId = "voicecmd_bouncing_off",
                Confirm = new()
                {
                    ["bambi"] = "aww okay, no more boingy words~",
                    ["sissy"] = "all settled, good girl.",
                    ["circe"] = "bouncing text off.",
                },
            },

            // ── Spiral overlay ──────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "spiral_on",
                Aliases = OnAliases("spiral", "the spiral"),
                Execute = () => App.Overlay?.ShowOverlaySustained("spiral", 0.5),
                VoiceRuleId = "voicecmd_spiral_on",
                Confirm = new()
                {
                    ["bambi"] = "spirally~ look how pretty it spins!",
                    ["sissy"] = "follow the spiral down, good girl~",
                    ["circe"] = "spiral on. look into it.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "spiral_off",
                Aliases = OffAliases("spiral", "the spiral"),
                Execute = () => App.Overlay?.HideOverlaySustained("spiral"),
                VoiceRuleId = "voicecmd_spiral_off",
                Confirm = new()
                {
                    ["bambi"] = "spiral's gone~ poof!",
                    ["sissy"] = "eyes back to me now, good girl.",
                    ["circe"] = "spiral off.",
                },
            },

            // ── Pink filter ─────────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "pink_on",
                Aliases = OnAliases("pink filter", "the pink filter", "make it pink", "go pink"),
                // OverlayService keys this overlay "pink_filter"; "pink" hits the unknown-kind no-op.
                Execute = () => App.Overlay?.ShowOverlaySustained("pink_filter", 0.4),
                VoiceRuleId = "voicecmd_pink_on",
                Confirm = new()
                {
                    ["bambi"] = "everything's pink now~ so cute!",
                    ["sissy"] = "bathe in the pink, good girl~",
                    ["circe"] = "pink filter on.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "pink_off",
                Aliases = OffAliases("pink filter", "the pink filter", "make it normal"),
                Execute = () => App.Overlay?.HideOverlaySustained("pink_filter"),
                VoiceRuleId = "voicecmd_pink_off",
                Confirm = new()
                {
                    ["bambi"] = "okayy, un-pink~",
                    ["sissy"] = "back to normal, good girl.",
                    ["circe"] = "pink filter off.",
                },
            },

            // ── Mind wipe (one-shot only; on/off toggles dropped — see Flashes) ─────
            new VoiceCommandIntent
            {
                Name = "wipe_once",
                Aliases = new[] { "wipe my mind", "wipe me", "empty my head", "blank my mind", "wipe my brain", "clear my mind", "empty my mind", "erase my thoughts" },
                Execute = () => App.MindWipe?.TriggerOnce(),
                VoiceRuleId = "voicecmd_wipe_once",
                Confirm = new()
                {
                    ["bambi"] = "poof~! all gone, hehe",
                    ["sissy"] = "empty and pretty, good girl~",
                    ["circe"] = "blank.",
                },
            },

            // ── Lock cards (one-shot only; on/off toggles dropped — see Flashes) ────
            new VoiceCommandIntent
            {
                Name = "lock_once",
                Aliases = new[] { "lock me", "lock card now", "give me a lock card", "show me a lock card", "a lock card", "one lock card", "lock me up", "lock me down" },
                Execute = () => App.LockCard?.ShowLockCard(),
                VoiceRuleId = "voicecmd_lock_once",
                Confirm = new()
                {
                    ["bambi"] = "say it for me~ go go go!",
                    ["sissy"] = "prove it to me, good girl~",
                    ["circe"] = "say it. now.",
                },
            },

            // ── Pop quiz (one-shot only; on/off toggles dropped — see Flashes) ──────
            new VoiceCommandIntent
            {
                Name = "quiz_once",
                Aliases = new[] { "quiz me", "quiz me now", "give me a quiz", "test me", "pop quiz", "quiz time", "ask me a question", "give me a question" },
                Execute = () => App.PopQuiz?.ShowPopQuiz(),
                VoiceRuleId = "voicecmd_quiz_once",
                Confirm = new()
                {
                    ["bambi"] = "pop quiz~! hehe ready?",
                    ["sissy"] = "let's see what you remember, good girl~",
                    ["circe"] = "answer this.",
                },
            },

            // ── Keyword triggers ────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "keyword_on",
                Aliases = OnAliases("keyword triggers", "the keyword triggers", "trigger words on"),
                Execute = () => App.KeywordTriggers?.Start(),
                VoiceRuleId = "voicecmd_keyword_on",
                Confirm = new()
                {
                    ["bambi"] = "trigger words armed~ ooh!",
                    ["sissy"] = "your words have power now, good girl~",
                    ["circe"] = "keyword triggers on.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "keyword_off",
                Aliases = OffAliases("keyword triggers", "the keyword triggers", "trigger words off"),
                Execute = () => App.KeywordTriggers?.Stop(),
                VoiceRuleId = "voicecmd_keyword_off",
                Confirm = new()
                {
                    ["bambi"] = "okayy, words are safe again~",
                    ["sissy"] = "triggers disarmed, good girl.",
                    ["circe"] = "keyword triggers off.",
                },
            },

            // ── One-shot toys ───────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "count_once",
                Aliases = new[] { "count for me", "count the bubbles", "give me a counting game", "make me count", "let me count", "counting game", "time to count", "i want to count" },
                // forceTest: true — TriggerGame() bails when the engine isn't running; the force path is
                // built to run standalone (it sets its own videos path), so the voice command fires it now.
                Execute = () => App.BubbleCount?.TriggerGame(forceTest: true),
                VoiceRuleId = "voicecmd_count_once",
                Confirm = new()
                {
                    ["bambi"] = "counting time~ one, two, ooh!",
                    ["sissy"] = "count them all for me, good girl~",
                    ["circe"] = "count them.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "freeze_once",
                Aliases = new[] { "freeze", "freeze me", "freeze for me", "bambi freeze", "freeze now", "hold still", "stay still", "don't move" },
                Execute = () => App.Subliminal?.TriggerBambiFreeze(),
                VoiceRuleId = "voicecmd_freeze_once",
                Confirm = new()
                {
                    ["bambi"] = "freeze~! good girl, don't move!",
                    ["sissy"] = "still now, good girl. freeze~",
                    ["circe"] = "freeze.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "shake_once",
                Aliases = new[] { "shake the screen", "shake it", "shake me", "earthquake", "shake things up", "make it shake", "shake everything", "shake the room" },
                Execute = () => App.ScreenShake?.Shake(60, 1200),
                VoiceRuleId = "voicecmd_shake_once",
                Confirm = new()
                {
                    ["bambi"] = "wheee~ shakey shakey!",
                    ["sissy"] = "feel that, good girl~",
                    ["circe"] = "shaking.",
                },
            },

            // ── Deeper ──────────────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "deeper",
                Aliases = new[] { "deeper", "go deeper", "take me deeper", "sink deeper", "drop deeper", "deeper now", "further down", "take me down", "drop me down", "make me go deeper", "i want to go deeper" },
                Execute = () => App.BrainDrain?.Start(bypassLevelCheck: true),
                VoiceRuleId = "voicecmd_deeper",
                Confirm = new()
                {
                    ["bambi"] = "deeper~ down down down, hehe",
                    ["sissy"] = "that's it... deeper for me, good girl~",
                    ["circe"] = "deeper. sink.",
                },
            },

            // ── Takeover (Autonomy) ─────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "takeover_on",
                Aliases = new[] { "take over", "take control", "you're in charge", "take over for me", "you take over", "take the wheel", "you drive", "you're in control", "control me", "i give up control" },
                Execute = () => App.Autonomy?.Start(),
                VoiceRuleId = "voicecmd_takeover_on",
                Confirm = new()
                {
                    ["bambi"] = "ooh, my turn~ i've got you now, hehe!",
                    ["sissy"] = "good girl. let go — i'll take it from here~",
                    ["circe"] = "i have control now.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "takeover_off",
                Aliases = new[] { "stop taking over", "stop the takeover", "you can stop now", "give me control back", "stop taking control", "i want control back", "let me drive", "give me back control", "take over off", "release control" },
                // Lockdown mode: Takeover cannot be released while locked in (#514).
                Blocked = () => App.Lockdown?.IsActive == true,
                BlockedConfirm = new()
                {
                    ["bambi"] = "nuh-uh~ lockdown says i keep control, hehe!",
                    ["sissy"] = "not during lockdown, good girl. i keep control.",
                    ["circe"] = "lockdown is active. control stays with me.",
                },
                Execute = () => App.Autonomy?.Stop(),
                VoiceRuleId = "voicecmd_takeover_off",
                Confirm = new()
                {
                    ["bambi"] = "okayy, you're back in charge~ for now hehe",
                    ["sissy"] = "control's yours again, good girl.",
                    ["circe"] = "control returned.",
                },
            },

            // ── Session control (terse) ─────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "pause",
                Aliases = new[] { "pause", "pause the session", "pause everything", "hold on", "pause please", "pause it", "wait", "hold up", "take a break", "one moment", "give me a moment" },
                // PauseSessionFromRemote -> SessionEngine.PauseSession, which calls App.Video.Stop()
                // along with every other feature teardown — the same privileged exit `video_off` is
                // refused for, reached by a shorter word (#706). Note this one also outranked the
                // panic key during lockdown, which returns early before it can pause anything.
                Blocked = StopLocked,
                BlockedConfirm = new()
                {
                    ["bambi"] = "no pausing, silly~ you're locked in til it's done!",
                    ["sissy"] = "no pausing, good girl. you asked to be held — sit with it.",
                    ["circe"] = "no. you don't get to pause this.",
                },
                Execute = () => App.MainWindowRef?.PauseSessionFromRemote(),
                TerseAck = true,
                NoChain = true,
                Confirm = new()
                {
                    ["bambi"] = "paused~ take your time, cutie!",
                    ["sissy"] = "paused, good girl. breathe~",
                    ["circe"] = "paused.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "resume",
                Aliases = new[] { "resume", "resume the session", "continue", "keep going", "unpause", "carry on", "go on", "let's continue", "back to it", "resume please", "continue the session" },
                Execute = () => App.MainWindowRef?.ResumeSessionFromRemote(),
                TerseAck = true,
                NoChain = true,
                Confirm = new()
                {
                    ["bambi"] = "back to it~ yay!",
                    ["sissy"] = "good girl, let's continue~",
                    ["circe"] = "resumed.",
                },
            },

            // ── Volume / audio (terse) ──────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "mute",
                // Deliberately NO bare "quiet" alias: Vosk can emit the near-homophone "quiet" when the
                // user says "quieter" (a different, non-destructive command), which would hard-mute ALL
                // audio. "be quiet" keeps the intent reachable without the one-word collision.
                Aliases = new[] { "be quiet", "mute", "silence", "shush", "hush", "mute it", "mute everything", "quiet please", "stop the sound", "stop the audio", "no sound", "kill the sound", "turn off the sound" },
                // Mute = master volume 0 + mute whispers + mute avatar, reflected across ALL the UI that
                // mirrors those toggles (master slider, both whisper checkboxes, mute-avatar, avatar menu).
                // MainWindow owns that sync because the controls are wired manually, not data-bound.
                // (KillAllAudio is NOT used here — it's a panic-grade teardown that also stops Autonomy
                // and the overlays, i.e. it would kill the very voice loop that heard "mute".)
                Execute = () => App.MainWindowRef?.ApplyVoiceMute(true),
                TerseAck = true,
                NoChain = true,
                Confirm = new()
                {
                    ["bambi"] = "shh~ okayy!",
                    ["sissy"] = "quiet now, good girl.",
                    ["circe"] = "silenced.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "unmute",
                // The inverse of "mute": clear the avatar mute flag and, if master is sitting at 0
                // (where mute left it), lift it back to a comfortable level so she's audible again.
                Aliases = new[] { "unmute", "un mute", "unmute yourself", "unmute everything", "sound on", "audio on", "turn the sound on", "turn the sound back on", "turn sound back on", "you can talk again", "i can't hear you" },
                Execute = () => App.MainWindowRef?.ApplyVoiceMute(false),
                TerseAck = true,
                NoChain = true,
                Confirm = new()
                {
                    ["bambi"] = "yay~ sound's back on!",
                    ["sissy"] = "there, you can hear me again, good girl~",
                    ["circe"] = "sound on.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "louder",
                Aliases = new[] { "louder", "turn it up", "volume up", "more volume", "turn up the volume", "crank it up", "louder please", "make it louder", "raise the volume", "pump it up" },
                Execute = () => App.MainWindowRef?.AdjustMasterVolume(+15),
                TerseAck = true,
                NoChain = true,
                Confirm = new()
                {
                    ["bambi"] = "louder~ okayy!",
                    ["sissy"] = "turning it up for you, good girl~",
                    ["circe"] = "louder.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "quieter",
                Aliases = new[] { "quieter", "turn it down", "volume down", "less volume", "lower the volume", "turn down the volume", "quieter please", "make it quieter", "not so loud", "softer", "tone it down" },
                Execute = () => App.MainWindowRef?.AdjustMasterVolume(-15),
                TerseAck = true,
                NoChain = true,
                Confirm = new()
                {
                    ["bambi"] = "quieter~ hehe okay!",
                    ["sissy"] = "softer now, good girl~",
                    ["circe"] = "quieter.",
                },
            },

            // ── Mic control (terse, ends the chain) ─────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "stop_listening",
                Aliases = new[] { "stop listening", "stop the mic", "turn off the mic", "you can stop listening", "mic off", "stop the microphone", "turn off the microphone", "microphone off", "stop hearing me", "close the mic", "mute the mic" },
                Execute = () => App.Autonomy?.StopVoiceInput(),
                TerseAck = true,
                NoChain = true,
                Repeatable = false,
                Confirm = new()
                {
                    ["bambi"] = "okayy, ears off~ bye bye mic!",
                    ["sissy"] = "mic off, good girl. just say my name to call me back~",
                    ["circe"] = "microphone off.",
                },
            },

            // ── "again" / "more" — repeat the last actionable command ───────────────
            new VoiceCommandIntent
            {
                Name = "replay",
                Aliases = new[] { "again", "one more", "do it again", "one more time", "more", "harder", "repeat", "repeat that", "do that again", "once more", "encore" },
                IsReplay = true,
                Repeatable = false,
                Confirm = new()
                {
                    ["bambi"] = "um... there's nothing to repeat yet~",
                    ["sissy"] = "there's nothing to repeat yet, good girl~",
                    ["circe"] = "nothing to repeat.",
                },
            },

            // ── "what can I say" help (text-only) ───────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "help",
                Aliases = new[] { "what can i say", "what can you do", "help", "commands", "what can i ask for", "what are my options", "what are the commands", "what should i say", "list commands", "what do you understand" },
                TerseAck = true,
                Repeatable = false,
                // The help line is long; without this the chained "anything else?" prompt overwrites
                // it within ~1.4s before it can be read. NoChain keeps the list on screen.
                NoChain = true,
                Confirm = new()
                {
                    ["bambi"] = "ooh lots! try: bubbles, flash me, a video, the spiral, deeper, quiz me, lock me, freeze, pause, quieter — or say red to stop everything~!",
                    ["sissy"] = "you can ask for: bubbles, flash me, a video, the spiral, deeper, quiz me, lock me, pause, quieter — or say red and i'll stop everything, good girl~",
                    ["circe"] = "try: bubbles, flash me, video, spiral, deeper, quiz me, lock me, pause, quieter — or 'red' to stop everything.",
                },
            },

            // Explicit mantra request — defer to the existing Spoken-Mantra flow (no confirm needed).
            new VoiceCommandIntent
            {
                Name = "mantra",
                Aliases = new[] { "give me a mantra", "make me say it", "i want a mantra", "let me say something", "say a mantra", "mantra please", "give me something to say", "let me speak" },
                IsMantra = true,
            },
        };

        /// <summary>"bambi" / "sissy" / "circe" for the active mod (defaults to sissy's voice).</summary>
        private static string ModKey()
        {
            var id = App.Mods?.ActiveModId ?? "";
            if (id.Contains("bambi", StringComparison.OrdinalIgnoreCase)) return "bambi";
            if (id.Contains("sissy", StringComparison.OrdinalIgnoreCase)) return "sissy";
            if (id.Contains("locked", StringComparison.OrdinalIgnoreCase)) return "circe";
            return "sissy";
        }

        /// <summary>The outcome of one command-listen window — drives chaining and the mantra fallback.</summary>
        private enum VoiceCmdOutcome
        {
            /// <summary>Nothing usable heard (silence / too quiet / engine unavailable).</summary>
            Silence,
            /// <summary>Heard loud speech but it matched no command (eligible for one polite re-listen).</summary>
            NoMatch,
            /// <summary>Heard an explicit "give me a mantra" request.</summary>
            Mantra,
            /// <summary>A command ran — keep a follow-up window open for chaining.</summary>
            Handled,
            /// <summary>A command ran but the chain must end now (panic / stop-listening).</summary>
            HandledFinal,
        }

        /// <summary>
        /// The serialized command driver. Listens once for a command, re-listens once on a near-miss,
        /// then (on a hit) keeps a short follow-up window open so commands can be chained. Returns true
        /// when a command was handled (caller should NOT then run a mantra); false to fall through to
        /// the mantra flow (no match, an explicit mantra request, silence, or speech unavailable).
        /// </summary>
        private async Task<bool> TryHandleVoiceCommandAsync()
        {
            // Tier 0 — listen first (the dots bubble is already up; nothing has been spoken). This short
            // primary window catches "hey bambi <command>" said in one breath or after a brief pause.
            var outcome = await ListenForCommandAsync().ConfigureAwait(false);

            // Stayed silent? NOW she speaks the wake ack out loud ("you called?") and opens a longer
            // window — the "called her, then took a beat to think" path.
            if (outcome == VoiceCmdOutcome.Silence)
            {
                SpeakPendingWakeAck();
                outcome = await ListenForCommandAsync(isReprompt: true).ConfigureAwait(false);
            }

            // Heard something loud that didn't match — give one polite "say that again?" before giving up.
            if (outcome == VoiceCmdOutcome.NoMatch)
                outcome = await ListenForCommandAsync(isRetry: true).ConfigureAwait(false);

            if (outcome == VoiceCmdOutcome.Handled)
            {
                // Command chaining: stack a few quick follow-ups without re-waking. Ends on the first
                // non-command turn (silence / no-match / a final command), or after the cap.
                for (int i = 0; i < MaxChainedCommands; i++)
                {
                    var next = await ListenForCommandAsync(chained: true).ConfigureAwait(false);
                    if (next == VoiceCmdOutcome.Handled) continue;
                    // An explicit "give me a mantra" as a follow-up should still deliver a mantra, exactly
                    // like a first-turn request — fall through to the caller's mantra flow instead of being
                    // swallowed by the already-handled return.
                    if (next == VoiceCmdOutcome.Mantra) return false;
                    break; // silence / no-match / a final command ends the chain
                }
                return true;
            }
            if (outcome == VoiceCmdOutcome.HandledFinal)
                return true;

            // Mantra request, no-match (post-retry), or silence -> let the caller run the mantra flow.
            return false;
        }

        /// <summary>
        /// Speak the wake acknowledgement stashed by OnWakeWordHeard ("you called?"). Tier 0 only does
        /// this AFTER the primary listen times out in silence — the spoken re-prompt before the second,
        /// longer listen window. No-op if nothing is stashed.
        /// </summary>
        private void SpeakPendingWakeAck()
        {
            var text = _pendingWakeAckText;
            var audio = _pendingWakeAckAudio;
            if (string.IsNullOrWhiteSpace(text)) return;
            if (Application.Current?.Dispatcher == null) return;
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    App.AvatarWindow?.GigglePriority(text, playSound: audio != null, aiGenerated: false,
                        phraseAudioPath: audio, barkVoice: audio != null);
                }
                catch { }
            });
        }

        /// <summary>
        /// One command-listen window: show the listening bubble, run a grammar-constrained recognition,
        /// fuzzy-match it to an intent, and (on a hit) execute + confirm. The default (primary) turn is a
        /// short Tier 0 onset window opened with no spoken ack. <paramref name="isReprompt"/> is the longer
        /// window after she's said "you called?" out loud; <paramref name="chained"/> uses a shorter window
        /// and an "anything else?" prompt; <paramref name="isRetry"/> shows a "say that again?" prompt after
        /// a near-miss.
        /// </summary>
        private async Task<VoiceCmdOutcome> ListenForCommandAsync(bool chained = false, bool isRetry = false, bool isReprompt = false)
        {
            try
            {
                if (App.Speech?.IsAvailable != true) return VoiceCmdOutcome.Silence;

                var intents = VoiceCommandIntents;
                var grammar = intents.SelectMany(i => i.Aliases).Distinct().ToList();
                if (grammar.Count == 0) return VoiceCmdOutcome.Silence;

                // On a chained follow-up, let the previous command's confirmation bubble stay up for a
                // beat before we replace it with the "anything else?" listening dots — otherwise the
                // confirmation is overwritten within a frame and never read.
                if (chained) await Task.Delay(1400).ConfigureAwait(false);

                // Keep the speech bubble up with animated dots for the whole listen window. The primary and
                // re-prompt turns show the wake-ack words (read == heard); the primary shows them silently
                // as the dots cue, the re-prompt has just spoken them aloud. Retry/chain use tailored text.
                string? listeningLine;
                if (isRetry) listeningLine = RetryPrompt();
                else if (chained) listeningLine = ChainPrompt();
                else
                {
                    listeningLine = _pendingWakeAckText;
                    if (string.IsNullOrWhiteSpace(listeningLine))
                    {
                        var wl = App.Bark?.PickVoiceLine("voicecmd_wake");
                        listeningLine = (wl is { } l && !string.IsNullOrWhiteSpace(l.Text)) ? l.Text : ListeningPrompt();
                    }
                }
                if (Application.Current?.Dispatcher != null)
                    _ = Application.Current.Dispatcher.InvokeAsync(() =>
                        { try { App.AvatarWindow?.ShowListeningBubble(listeningLine); } catch { } });

                // Echo guard (Tier 0/1). Only needed when she JUST spoke and the user might be on speakers:
                //   • primary turn: nothing was spoken (silent dots) — open the mic immediately, no wait.
                //   • re-prompt:    she just said the ack aloud — wait for her clip + a short echo tail.
                //   • chained:      the previous command's confirmation may still be playing.
                //   • retry:        a near-miss is never spoken — nothing to wait for.
                //   • headphones mode: her voice can't bleed in, so allow barge-in and never wait.
                bool sheJustSpoke = isReprompt || chained;
                bool headphones = App.Settings?.Current?.SpeechHeadphonesMode == true;
                if (sheJustSpoke && !headphones)
                    await WaitForAvatarQuietAsync(waitForStart: isReprompt).ConfigureAwait(false);

                // Window sizing (Tier 0). Primary + re-prompt use a short *onset* deadline — you have that
                // long to START talking; once you do, the generous hard cap lets you finish (Vosk usually
                // finalizes on the trailing pause well before then). So "hey bambi … <beat> … command"
                // isn't clipped, yet dead air still bounces to the spoken "you called?" re-prompt quickly.
                // Chained follow-ups and the near-miss retry stay simple hard windows.
                RecognizeOptions opts =
                    chained    ? new RecognizeOptions { Timeout = TimeSpan.FromSeconds(4) } :
                    isRetry    ? new RecognizeOptions { Timeout = TimeSpan.FromSeconds(6) } :
                    isReprompt ? new RecognizeOptions { Timeout = TimeSpan.FromSeconds(8), OnsetTimeout = TimeSpan.FromSeconds(4) } :
                                 new RecognizeOptions { Timeout = TimeSpan.FromSeconds(8), OnsetTimeout = TimeSpan.FromSeconds(2.5) };

                PhraseResult res;
                try
                {
                    res = await App.Speech.RecognizeOneOfAsync(grammar, opts).ConfigureAwait(false);
                }
                finally
                {
                    // Drop the dots indicator. If a command matched, the confirmation bubble has already
                    // taken over (ShowGiggle clears the listening flag) so this no-ops on visibility.
                    if (Application.Current?.Dispatcher != null)
                        _ = Application.Current.Dispatcher.InvokeAsync(() =>
                            { try { App.AvatarWindow?.HideListeningBubble(); } catch { } });
                }

                if (res.Unavailable || !res.LoudEnough || string.IsNullOrWhiteSpace(res.Transcript))
                    return VoiceCmdOutcome.Silence;

                var heard = SpeechService.Normalize(res.Transcript);
                var (best, bestScore) = MatchVoiceIntent(heard);

                if (best == null)
                {
                    App.Logger?.Information(
                        "AutonomyService: voice command no-match (heard '{Heard}', best {Score:0.00})", heard, bestScore);
                    return VoiceCmdOutcome.NoMatch;
                }

                App.Logger?.Information("AutonomyService: voice command '{Name}' (heard '{Heard}', score {Score:0.00})",
                    best.Name, heard, bestScore);

                // Explicit "give me a mantra" -> let the funnel run the Spoken-Mantra flow.
                if (best.IsMantra) return VoiceCmdOutcome.Mantra;

                // "again" / "more" -> resolve to the last actionable command (if any).
                var target = best;
                if (best.IsReplay)
                {
                    target = _lastVoiceIntent;
                    if (target == null)
                    {
                        if (Application.Current?.Dispatcher != null)
                            await Application.Current.Dispatcher.InvokeAsync(() => ExecuteIntentAndConfirm(best));
                        return VoiceCmdOutcome.Handled; // acknowledged ("nothing to repeat") — still chainable
                    }
                }

                var toRun = target;
                if (Application.Current?.Dispatcher != null)
                    await Application.Current.Dispatcher.InvokeAsync(() => ExecuteIntentAndConfirm(toRun));

                // Remember the last actionable command so a later "again"/"more" can replay it.
                // A Blocked (refused) command must not become the replay target, or "again"
                // repeats the refusal instead of the last real effect.
                if (best.Repeatable && best.Blocked?.Invoke() != true) _lastVoiceIntent = best;

                return best.NoChain ? VoiceCmdOutcome.HandledFinal : VoiceCmdOutcome.Handled;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AutonomyService: ListenForCommandAsync failed");
                return VoiceCmdOutcome.Silence;
            }
        }

        /// <summary>
        /// Resolve a heard (grammar-constrained) phrase to the best command intent, or (null, bestScore)
        /// when nothing clears that intent's required threshold. Two guards over a plain argmax:
        ///   • per-intent floor: terse utility verbs need <see cref="UtilityCommandMatchThreshold"/> — their
        ///     short aliases otherwise grab any one-word overlap at the 0.5 content floor;
        ///   • "mantra" keyword priority: that token appears in NO other intent's aliases, so a
        ///     grammar-constrained transcript containing it is an explicit mantra request — route it to the
        ///     mantra intent even when a utility alias coincidentally ties (the original "sound mantra" bug).
        /// Shared by the listen flow and the one-breath inline-command path so both score identically.
        /// </summary>
        private static (VoiceCommandIntent? intent, double score) MatchVoiceIntent(string heard)
        {
            heard = SpeechService.Normalize(heard);
            if (heard.Length == 0) return (null, 0);

            VoiceCommandIntent? best = null;
            double bestScore = 0;
            foreach (var intent in VoiceCommandIntents)
                foreach (var alias in intent.Aliases)
                {
                    var s = SpeechService.Similarity(SpeechService.Normalize(alias), heard);
                    if (s > bestScore) { bestScore = s; best = intent; }
                }

            // "mantra" is unique to the mantra intent's grammar, so its presence is an explicit request —
            // honour it over a coincidental utility-alias tie. The < 0.85 guard is belt-and-braces: a
            // near-exact match to some other alias can't actually contain the word "mantra".
            if (best?.IsMantra != true && bestScore < 0.85
                && heard.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("mantra"))
            {
                var mantra = VoiceCommandIntents.FirstOrDefault(i => i.IsMantra);
                if (mantra != null) return (mantra, Math.Max(bestScore, VoiceCommandMatchThreshold));
            }

            if (best == null) return (null, bestScore);
            double required = best.TerseAck ? UtilityCommandMatchThreshold : VoiceCommandMatchThreshold;
            return bestScore >= required ? (best, bestScore) : (null, bestScore);
        }

        /// <summary>Run an intent's action and speak its confirmation. Must be called on the UI thread.</summary>
        private static void ExecuteIntentAndConfirm(VoiceCommandIntent intent)
        {
            bool blocked = false;
            try { blocked = intent.Blocked?.Invoke() == true; } catch { }
            if (blocked)
            {
                var refuseKey = ModKey();
                var refusal = intent.BlockedConfirm.TryGetValue(refuseKey, out var r) && !string.IsNullOrWhiteSpace(r)
                    ? r
                    : intent.BlockedConfirm.Values.FirstOrDefault() ?? "no~";
                try { App.AvatarWindow?.GigglePriority(refusal, playSound: false, aiGenerated: false); } catch { }
                return;
            }

            try { intent.Execute?.Invoke(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "AutonomyService: voice command '{Name}' execute failed", intent.Name); }

            // Utility verbs get a short, text-only ack. Feature verbs prefer the voiced manifest variant
            // (text + matching clip for the active mod), falling back to the inline per-mod text.
            string confirm;
            string? audio = null;
            var voiced = (!intent.TerseAck && intent.VoiceRuleId != null)
                ? App.Bark?.PickVoiceLine(intent.VoiceRuleId)
                : null;
            if (voiced is { } line && !string.IsNullOrWhiteSpace(line.Text))
            {
                confirm = line.Text;
                audio = line.Audio;
            }
            else
            {
                var modKey = ModKey();
                confirm = intent.Confirm.TryGetValue(modKey, out var c) && !string.IsNullOrWhiteSpace(c)
                    ? c
                    : intent.Confirm.Values.FirstOrDefault() ?? "okay~";
            }

            try { App.AvatarWindow?.GigglePriority(confirm, playSound: audio != null, aiGenerated: false,
                phraseAudioPath: audio, barkVoice: audio != null); } catch { }
        }

        /// <summary>First-turn "I'm listening" prompt when no voiced wake line is available.</summary>
        private static string ListeningPrompt() => ModKey() switch
        {
            "bambi" => "mmm? i'm listening~",
            "circe" => "i'm listening.",
            _       => "i'm listening, good girl~",
        };

        /// <summary>Follow-up prompt shown during command chaining.</summary>
        private static string ChainPrompt() => ModKey() switch
        {
            "bambi" => "ooh, anything else?~",
            "circe" => "anything else?",
            _       => "anything else, good girl?~",
        };

        /// <summary>Prompt shown for the one polite re-listen after a near-miss.</summary>
        private static string RetryPrompt() => ModKey() switch
        {
            "bambi" => "hmm? say that again?~",
            "circe" => "again?",
            _       => "sorry love, say that again?~",
        };

        /// <summary>
        /// Holds until the avatar has stopped speaking (capped by <paramref name="maxWaitMs"/>), then
        /// waits a short <paramref name="tailMs"/> for speaker echo to decay — so the command mic never
        /// hears her own voice. When <paramref name="waitForStart"/> is set (the wake/PTT turn, where the
        /// ack clip starts after a brief bubble lead-in), it first gives her clip up to
        /// <paramref name="graceMs"/> to actually begin so we don't sail past the wait before she speaks.
        /// </summary>
        private static async Task WaitForAvatarQuietAsync(bool waitForStart, int graceMs = 800, int maxWaitMs = 5000, int tailMs = 300)
        {
            try
            {
                if (waitForStart)
                {
                    int g = 0;
                    while (App.AvatarWindow?.IsSpeakingAudio != true && g < graceMs)
                    {
                        await Task.Delay(50).ConfigureAwait(false);
                        g += 50;
                    }
                }

                int waited = 0;
                while (App.AvatarWindow?.IsSpeakingAudio == true && waited < maxWaitMs)
                {
                    await Task.Delay(60).ConfigureAwait(false);
                    waited += 60;
                }

                if (tailMs > 0) await Task.Delay(tailMs).ConfigureAwait(false);
            }
            catch { /* never let the echo guard wedge the listen */ }
        }
    }
}
