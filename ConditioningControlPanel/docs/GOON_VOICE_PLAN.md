# Goon Game — Voice Notes (P2P audio messages)

Branch `feat/goon-voice` off `f66878cb` (round-5 beta hardening). Worktree
`C:\Projects\ccp-wt-goon-voice`. This doc is the CONTRACT — Fable-authored, agents
extend-only. Names, verbs, prefs keys, caps and limits below are pinned; internals are
the implementing agent's call.

## Product

Duelists can send each other short voice messages (**max 10 seconds**), two ways:

1. **Live**: a mic button in the match HUD. Press-and-hold to record (WhatsApp
   behaviour: HUD hidden when idle, expands into a recording strip while held —
   pulsing red dot + elapsed timer + "slide to cancel"; release = send, slide left
   past threshold = cancel, 10s cap = auto-stop-and-send).
2. **Pre-recorded**: notes recorded ahead of time in a new **"Send Voice Notes"**
   title-menu screen. A note can be **associated with an emote**; firing that emote
   in a match also sends the note, and it plays on the opponent's side together with
   the emote bubble.

Consent is **opt-in, OFF by default**, behind an **acknowledgment gate**: the first
time a player tries to enable it, a modal explains what it does (your real voice is
recorded and sent to your opponent; enabling means you may also HEAR your opponent's
real voice) and requires an explicit "I understand" acknowledgment before the toggle
can turn on. Voice flows in a match ONLY when **both sides** have opted in AND the
peer's build supports it. The receiver enforces locally: with the local opt-in off,
incoming voice frames are dropped unread — never decoded, never played.

P2P only. **The server never sees audio.** No server changes in this feature.

## Wire protocol (pinned)

Voice rides the **control lane** (main data channel / relay), NOT `goon-media` —
the bulk channel does not exist on relay fallback and relay clamps frames at 16KB.
Precedent: `t:'emote'` fire-and-forget family (no cost, no receipts, not a payload).

New message family `t:'voice'` with `sub`:
- `{t:'voice', sub:'meta', id, bytes, durMs, emote|null, parts}` — announces one note.
  `id` = sender-local transfer id (monotonic int). `emote` = emote icon/id when the
  note is emote-attached (receiver shows the bubble tie-in), null for live notes.
- `{t:'voice', sub:'chunk', id, seq, data}` — base64, **max 10KB of base64 text per
  chunk** (relay-safe with JSON envelope under 16KB).
- `{t:'voice', sub:'end', id}` — after the last chunk (ordered lane ⇒ no races).

Follow the `vwin`/`DraftMsg` append-only precedent in `core/wire.js` +
`core/contracts.js`; unknown-`t` messages are dropped by old parsers, and we ALSO
gate on caps so we never send blind.

**Caps discriminator**: `caps.voice = 1` (append-only, `core/caps.js` overrides
pattern, advertised from boot like `transfer`). Absent on the peer ⇒ mic hidden,
never send.

**Consent**: `voice_notes` field on the consent frame — clone the `media_transfer`
pattern EXACTLY (`core/contracts.js` makeConsent default false; NOT part of the
sameSheet fingerprint; cloneSheet keeps the LOCAL value; `match.setLocalVoiceNotes()`
/ `localVoiceNotes` / `remoteVoiceNotes` mirrors of the mediaTransfer members).
C# mirror: `GoonContracts.cs` consent class gets `[JsonProperty("voice_notes")]`
(vwin precedent — additive, Normalize-clamped to bool).

**Limits (pinned, enforce BOTH ends)**:
- `VN_MAX_MS = 10_000` record cap; receiver hard-stops playback at 10_500.
- `VN_MAX_BYTES = 262_144` (256KB) total blob; receiver aborts a transfer whose
  declared or accumulated size exceeds it.
- Sender min gap 4s between sends; receiver drops notes arriving <3s after the last
  accepted one; receiver playback queue max 2 (excess dropped, not queued).
- One in-flight transfer per direction; a new `meta` aborts an unfinished one.
- Live/Countdown/SuddenDeath phases only. Post-match frames ignored.
- If `net/blocklist.js` exposes a peer/content block check used by the media lane,
  the voice receive path consults the same check.

## Recording (pinned)

`MediaRecorder`, preferred `audio/webm;codecs=opus` at ~32kbps, fall back to
whatever `isTypeSupported` allows (Safari → audio/mp4). `getUserMedia({audio:true})`
lazily on first record attempt; permission-denied ⇒ toast + mic HUD shows a muted
state, never a throw. Track stopped (mic released) when not recording — no hot mic.
Recording is capped at 10.0s by a timer that stops the recorder (auto-send in match,
auto-finish in the library screen).

**WebView2 (in-app)**: `GoonHostService.cs` must handle `CoreWebView2.PermissionRequested`
— `PermissionKind.Microphone` ⇒ `Allow` (the in-page opt-in is the real gate); all
other kinds keep default behaviour. Without this the in-app mic silently fails.

## Storage

Pre-recorded notes live in **IndexedDB** (`gg-voice` DB, blobs + {name, durMs,
createdAt}) — works hosted, standalone, and on the phone PWA. Max **8 notes**.
Emote association map lives in prefs: `voiceEmoteMap` = `{ [emoteKey]: noteId }`
(one note per emote). Received notes are session-only, in memory — never persisted.

## Prefs / options / audio (pinned names)

- prefs: `voiceNotesEnabled` (false), `voiceAckSeen` (false), `voiceVolume` (0.9),
  `voiceEmoteMap` ({}).
- `ui/options.js`: 7th volume row `voiceVolume` on the existing `volumeRow` machinery.
- `ui/audio.js`: new `voice` bus → masterBus (sibling of ui/game/music/drone);
  playback helper `playVoiceNote(arrayBufferOrBlob, {onEnd})` — decodeAudioData,
  hard stop at 10.5s, sequential queue (max 2), routed through the voice bus so the
  slider and master both apply exactly once.

## UI

- **HUD mic** (`ui/voice/micHud.js`, mounted from `hud.js`): small mic button,
  visible ONLY when `voiceActive` (local opt-in && remote opt-in && peer caps.voice
  && phase in Countdown/Live/SD && not solo). Placement: right side, must respect
  the MERCY keep-out (mercy is z60 fixed layer — DO NOT TOUCH it or its z-band),
  never overlap the closeness dial, rails, or monitor. Zen mode hides it (open call
  for owner). Pointer capture on the button; works mouse AND touch/pen (desktop is
  mouse — do not gate on touch like pinch does). Sub-250ms tap = "hold to record"
  hint toast, not a recording.
- **Incoming indicator**: small speaking chip near the opponent bezel while a note
  plays (emote-attached notes: the existing bezel emote bubble is the anchor).
- **Voice Notes screen** (`ui/screens/voice.js`, `scr-voice`, title-menu item):
  ack-gate modal + opt-in toggle (toggle disabled until `voiceAckSeen`), record
  button reusing the same recorder module, note list (auto-name "Note N", duration,
  play preview through the voice bus, delete), emote association picker (emote set
  read from `ui/emotes.js`), link/hint to the volume slider in options.
- **Emote hook**: `ui/emotes.js` send path (line ~128 `match.sendEmote`) — after a
  successful emote send, if `voiceEmoteMap[emote]` exists and voice is active, send
  the associated note via the voice service. Never blocks or delays the emote.
- **Lobby** (optional, small): read-only line when both sides opted in ("voice notes
  on for this match"). No toggle in the lobby — the toggle lives in the Voice Notes
  screen only.
- Strings: ALL voice strings live in `strings.js` under `S.voice.*` (added in
  wave 1 by Agent A; wave-2 agents are READ-ONLY on strings.js).

## Service seam (pinned API, `ui/voice/voiceService.js`, built wave 1)

```
createVoiceService({ match, audio, prefs, noteStore|null, logger }) => {
  available(),            // local opt-in && remote opt-in && peer caps && phase ok
  sendBlob(blob, {emote}) // chunks + sends; resolves sent|dropped reason
  sendNote(noteId),       // loads from store, delegates to sendBlob with emote
  onIncoming(fn),         // fires {emote|null, durMs} when a note STARTS playing
  onStateChanged(fn),     // availability edge — micHud subscribes
  dispose()
}
```
Constructed in `boot.js` (wave 1) and threaded into `mountHud` deps (unused until
wave 2) and screen ctx.

## Waves / file ownership (no two agents edit the same file in the same wave)

- **Wave 1 — Agent A (foundation)**: core/contracts.js, core/wire.js, core/caps.js,
  core/match.js, ui/voice/voiceService.js, ui/audio.js, ui/prefs.js, ui/options.js,
  ui/strings.js (full S.voice block — enumerate every string waves 2 needs),
  boot.js wiring (service + hud dep threading + caps advertise),
  C# GoonContracts.cs consent mirror, test/selftest-voice.js (new suite) +
  additive checks in selftest-core.
- **Wave 2 — Agent B (mic + recorder + C#)**: ui/voice/recorder.js,
  ui/voice/micHud.js, ui/hud.js (mount hook), ui/hud.css,
  Services/GoonGame/GoonHostService.cs (PermissionRequested), selftest-hud additions.
- **Wave 2 — Agent C (screen + library + emote assoc)**: ui/voice/noteStore.js
  (IndexedDB), ui/screens/voice.js, ui/screens/title.js (menu item),
  ui/screens/lobby.js (optional indicator), ui/emotes.js (hook), ui/screens.css,
  boot.js (screen registration + actions.goVoice), selftest additions (own file
  test/selftest-voice-ui.js if screens need pins).

Gates per wave: all existing selftest suites stay green (run them), new checks
added for new behaviour, `dotnet build` 0 errors from the repo root project.

## Known traps (inherited)

- strings.js risk-tier table is duplicated and cross-checked by selftest-hud —
  don't touch it.
- `.gg-grad` + `background:` shorthand = invisible text (screens.css loads after
  goon.css).
- One-animation-slot budget: never stack a second animation on an element that
  already animates (put it on a child).
- `.gg-plate` re-enables pointer-events at 0,2,0 specificity — opt-outs must be
  written at the same specificity, later.
- exec/ never imports ui/ (voice is all ui/net/core — keep it that way; nothing
  in exec/ should change).
- Esc ladder: the mic must NOT add an Esc rung; Esc during Live is Mercy. A held
  recording is cancelled by pointer-cancel/loss, not Esc.
- Selftests import modules under node — new modules must be node-import-safe
  (no DOM at import time).
