# Goon Game — Discord Sharing Contract (v1)

Fable-authored. Agents EXTEND-ONLY: field names, verb names, enum strings and ordering
rules below are frozen. If an implementation detail forces a deviation, stop and report
it in your final answer instead of renaming things.

Scope: opt-in Discord sharing inside the Goon Game — (1) avatar shown to the opponent
(VS splash + small HUD bubble), (2) "allow Discord DMs" (opponent gets a Message button),
(3) Goon-Game rich presence, (4) last-opponent record (most recent only), (5) event-reactive
avatar animations.

## 1. Consent model (decided — do not re-litigate)
- Sharer-only gating, NOT mutual. All flags default **false** (rich presence too).
- Local viewer pref `showOpponentAvatars` (page-side, prefs.js, default ON). OFF also
  suppresses the peer-card FETCH, not just the render.
- One-time first-duel confirm before the first match where any sharing flag is on
  (page-side, `GoonSeenSharePrompt` persisted via discord-prefs echo round-trip).
- Own lobby plate shows a "they can see your picture" indicator while sharing is on.
- Rich presence text is FIXED STRINGS only. Never the opponent's name, never free text.
  While GG runs with `GoonRichPresence` off, the app's generic presence must not change.

## 2. C# settings (AppSettings.cs, clone RemoteShareAvatar block ~5346)
| Property | JSON | Default |
|---|---|---|
| `GoonShareAvatar` | `goonShareAvatar` | false |
| `GoonShareDiscordDm` | `goonShareDiscordDm` | false |
| `GoonRichPresence` | `goonRichPresence` | false |
| `GoonSeenSharePrompt` | `goonSeenSharePrompt` | false |
| `GoonLastOpponentJson` | `goonLastOpponentJson` | "" |

`GoonLastOpponentJson` = serialized `{ name, dmId, avatarFile, ts }` (dmId/avatarFile
nullable; avatarFile is a bare filename inside `%LOCALAPPDATA%\...\goon_avatars\`,
never a full path). Written by GoonHostService only.

Sync: ProfileSyncService pushes `goon_share_avatar` + `goon_share_dm` (snake_case) in the
profile sync body, ON CHANGE (RemoteControl precedent MainWindow.RemoteControl.cs:275-284),
plus rides the normal sync. `GoonRichPresence` is LOCAL-ONLY, never synced.

## 3. Server (ccp-server, proxy/) — SSRF rules are MANDATORY
- `discord_avatar_hash` on the user record is written ONLY by `/discord/validate`
  (server-side, from Discord's own response). The client-supplied `avatar_url` from
  /user/sync is NEVER fetched by anything goon.
- Sync ingestion: accept booleans `goon_share_avatar`, `goon_share_dm` (store on user record).
- Room record: at invite (host side) and join (guest side) snapshot the sharer's flags:
  `host_card` / `guest_card` = `{ av: bool, dm: bool, ver: string }` (`ver` opaque,
  derived from avatar hash + dm flag; changes when either changes).
- `/join` response (guest) and `/signal` poll (host, next to `peer_joined`) both gain
  `peer_card_ver: string|null` (null = peer shares nothing).
- **NEW `POST /v2/goon/peercard`** — roomAuth(requireJoined=true), gate 6/min:
  `{}` → `{ name, avatar: "data:image/png;base64,..."|null, avatar_reason: "ok"|"not_shared"|"none"|"unlinked"|"error", dm_id: string|null, ver }`
  - `dm_id` = peer's Discord snowflake ONLY if peer's `dm` flag true, else null.
  - Avatar fetch server-side: rebuild CDN URL from id + `discord_avatar_hash`, always
    `.png`, pin host `cdn.discordapp.com`, NO redirects, ≤256KB, content-type image/*,
    ≤3s timeout. Cache bytes `goon:av:{uid}` keyed with ver. Discord default avatars
    (null hash) → `avatar_reason:"none"`.
  - Revoking a share flag DELETES `goon:av:{uid}` at sync time, not flag-flip only.
- Never log CDN URLs (they contain the snowflake). No lookup-any-user variant, ever.
- DO NOT deploy. Deploys happen from proxy/ by the owner flow only.

## 4. Bridge verbs (GoonHostService <-> page)
Host→page:
- `init` gains `discord: { avatarState: "shared"|"off"|"unlinked", avatarDataUri: string|null, dmShared: bool, richPresence: bool, seenSharePrompt: bool, lastOpponent: { name, avatarDataUri: string|null, dm: bool, ts } | null }`
  (own avatar = host fetches own `DiscordService.GetAvatarUrl(128)` → data URI, no server round trip; unlinked/off → null).
- `discord` — same shape as init.discord minus lastOpponent; echoed after every
  discord-prefs write (page affordances read the ECHO, never the request — fullscreen pattern).
- `peer-card { name, avatarDataUri: string|null, reason, dm: bool, ver }` — the page NEVER
  sees the snowflake; `dm` is a boolean.

Page→host (add cases to the OnPageMessage switch; each needs a line in the SAFETY RAIL
banner explaining admissibility):
- `discord-prefs { shareAvatar?, shareDm?, richPresence?, seenSharePrompt? }` — writes the
  AppSettings flags, triggers push-on-change sync, echoes `discord`.
- `peer-card-req { code, token, role }` — AMENDED (was `{}`): /v2/goon/peercard is
  roomAuth(requireJoined=true) and only the PAGE holds the room credentials, so it passes
  them up; the host still picks the URL itself (constant path — the page cannot steer the
  request), POSTs with 3s timeout + 1 retry, fire-and-forget (NEVER blocks match flow),
  disk-caches the avatar under `%LOCALAPPDATA%\...\goon_avatars\`, stores `dm_id` in a
  private field, posts `peer-card`.
- `discord-open-dm { which: "peer"|"last" }` — host resolves the snowflake from its OWN
  store (never a page-supplied id), un-fullscreens FIRST (browser under fullscreen trap),
  then opens `https://discord.com/users/<id>` in the default browser.
- `discord-link-request {}` — restore main window + focus the Discord/Profile tab ONLY.
  MUST NOT auto-start OAuth (banned-verbs banner note).
- `rp-state { s: "lobby"|"live"|"recap"|"off" }` — host forwards to
  DiscordRichPresenceService.SetGoonActivity ONLY if `GoonRichPresence` is on. Enum only.
- `last-opponent-clear {}` — wipes GoonLastOpponentJson + cached file.

Last-opponent record is written by the HOST on `match-result` (it already has the peer
card cached) — no page-supplied data, no new verb. Overwrite semantics: only the most
recent opponent is ever stored.

## 5. Rich presence (DiscordRichPresenceService)
- `SetGoonActivity(string s)` with s ∈ lobby|live|recap → fixed strings, e.g.
  Details "Goon Game" / State "In the lobby" | "In a duel" | "Match over".
- Assets: `LargeImageKey = "https://cclabs.app/img/goon-game.png"` (DiscordRPC 1.6 accepts
  https URLs), `LargeImageText = "Goon Game"`. Fable deploys the image; if Discord rejects
  the URL the presence simply shows no art — acceptable.
- Connect-on-demand: if global `DiscordRichPresenceEnabled` is off but `GoonRichPresence`
  is on, connect for the GG session and ClearPresence+Disconnect on `rp-state off`.
  If global presence is on, `rp-state off` restores the previous generic activity.

## 6. Page UI containers (D owns) + FX event bus (E owns)
- Avatar DOM: `.gg-ava` with `data-side="you"|"opp"`, containing either
  `.gg-ava-img` (img) or `.gg-ava-tile` (initial-letter fallback, deterministic hue from
  name hash). Box is reserved from first paint (no layout shift when art lands).
- Sites: lobby Discord section (prominent), VS intro splash `.gg-vs-splash` at match
  start (big → shrink into HUD anchors), HUD persistent minis (small), recap plates,
  last-opponent card.
- FX bus: `document` CustomEvent **`gg-ava`**, `detail = { kind, side: "you"|"opp", meta? }`,
  kinds: `land` (payload landed on side), `fire` (side fired a payload), `drop` (drop
  armed), `pop` (bubble popped by you), `emote` (meta.emote), `mercy`, `win`, `lose`,
  `draw`, `cue` (announcer ribbon, meta.element). D emits; E consumes. E exposes
  `avatarFx.attach(rootEl)` from `ui/avatarFx.js` and touches NO existing ui/*.js file.
- Solo driver: bot presents name "Practice Bot", tile avatar, dm=false — section and FX
  must be exercisable in `?solo=1` with no host.

## 7. Ordering / safety invariants
- Peer-card fetch never gates lobby, countdown, or Live. Missing card = tile fallback.
- MERCY untouched: nothing here may add a z-layer over z60 or eat its clicks.
- `bridge.on` THROWS on duplicate type — one owner per verb (boot.js wires, modules subscribe
  through it).
- No looping animations outside the `--gg-deco-play` heat budget; reduced-motion = static.
- Web tests: extend existing selftest suites; new FX pins go in a NEW `test/selftest-avafx.js`.
- Loc: the 9 `Localization/Languages/*.json` get keys for the DiscordTabView rows —
  `\n` escapes only, never literal newlines.
