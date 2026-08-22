# Owner decisions: what is actually waiting on you

**Status: derived register.** Every entry is derived from a row on `client/docs/task-board.md`, which is the
only live queue, and every claim carries the citation the row carries. Nothing here is new evidence.
If this file and the board disagree, **the board wins** and this file is the defect.

**Written 2026-08-22, because the list I had been giving you was wrong.** For roughly fifteen wave
summaries I closed with a standing line naming five open decisions. That list was never derived from the
board. It was copied forward from an earlier summary and repeated because it had been repeated. Checking
it against the queue produced the three corrections below, and one of them matters a great deal.

---

## The correction that matters: you already answered the entitlement question

**I have been telling you the login-token decision is still open. It is not. You answered it on
2026-08-18** (`task-board.md:202`), and the answer was better than the three options that row had offered:

> *"I have just installed to original app and logged into the app to access the premium feats. Will that work?"*

**It works, and the mechanism was verified against source rather than assumed.** The shipping app stores its
bearer at `%LOCALAPPDATA%/ConditioningControlPanel/auth_token.dat`, encrypted with
`ProtectedData.Protect(..., DataProtectionScope.CurrentUser)` under a compile-time entropy literal
(`Services/Auth/SecureAuthTokenStore.cs:14,17,39,65`). `CurrentUser` scope is the load-bearing fact: any
process running as the same Windows user can decrypt it, so the port can consume the login you already have
instead of building a second sign-in flow. Your file is live and confirms the path.

**So the posture is settled: borrow the shipping app's login.** What that unblocks is not small. Today DTRH
and Graded Intake refuse everybody, including you, because the port has no entitlement service at all.

**This is buildable now and needs nothing further from you.** It has been sitting behind a sentence of mine
that said otherwise.

Three limits belong in the build rather than being discovered later, and the row already names them: it
grants entitlement **on Windows only** (DPAPI has no Linux equivalent, so Linux stays honestly
`Unavailable`); it couples the port to another app's on-disk format, so a decrypt failure must be a loud
typed outcome and never a quiet downgrade to locked, which would look identical to "not a patron"; and it
presumes the shipping app is installed and logged in, so it is a **bridge**, not a destination. The token is
read, used and dropped.

---

## Decision register

### 1. May the port transmit the borrowed token, and read a second cross-app file?
`task-board.md:206` · scope: one further step, not the whole feature

The entitlement capability above works from the token on disk. Reading your actual **tier** additionally
needs a call to `/v2/user/profile`, which means two things the port is currently forbidden to do: it must
**transmit** the bearer, and it needs a `unified_id` that lives in a **second cross-app file**.

The board's own handling rule for that token says it is *"never logged, persisted, copied or transmitted
anywhere."* So this is a narrow, specific permission, not a general one.

**Worth saying plainly: I wrote that rule and then filed a row faulting a lane for not transmitting.** A
reviewer caught the contradiction and corrected the row against me. The lane had flagged the tension itself
and obeyed the stricter document, which was the right call.

**If you say no**, the entitlement bridge still works from the on-disk token; you lose the server-resolved
tier, not the feature.

### 2. The haptic dependency
`task-board.md:34` · the port currently ships "admit nothing"

The blocker is narrower than "haptics". **The two providers do not cost the same:**

- **Lovense needs zero packages.** An HTTP GET against `http://127.0.0.1:20010` using the runtime's own
  `HttpClient`, one file plus a keep-alive.
- **Buttplug needs a package** (`Buttplug` 5.0.1, one reference plus one file), and its .NET 10 compatibility
  and licence were **read from a source comment, not verified against the package**.

So three of the four options are answerable as written; only the Buttplug one needs a check first. Until you
answer, the capability refuses every user and the panel says so in words a user reads. Everything above the
device is already built: the effect modules command the haptic limb with your app's own numbers.

### 3. For You Feed gaze engines - resolved 2026-08-22
`task-board.md:25` · implementation and provider admission remain `WIP`

The owner requires both the current WPF-equivalent local BlazeFace/FaceMesh/Iris pipeline and a
third-party deep-learning gaze engine. The user chooses the active engine in webcam settings; the
local WPF-equivalent path is the default until the alternative is admitted.

The selector is not permission to send webcam data anywhere. Before the third-party option can
become available, its named provider and model, local or remote execution location, commercial
weight and training-data rights, outbound-data policy, retention, and Windows/Linux support need
recorded evidence. Each engine has distinct calibration identity and validity; changing engines
requires selecting a valid calibration or recalibrating. No unavailable engine silently falls back
or keeps the camera running under a different engine.

For You Feed's separate remote-media decision is unchanged: its third-party content source and
the consent that unlocks remote media in Flash, Video, Wallpaper, IntakeHost, and the DTRH asset
manifest remain distinct from this camera-engine decision.

### 4. Goon Game, six items
`task-board.md:322` · this is the port's first outbound network boundary

The port has no outbound network today. Its only `HttpClient` is a loopback probe. So this builds the first
boundary rather than extending one, and two properties are inherent to the transport rather than defects:

- With no relay server, a successful connection is **direct**, so **each player learns the other's public IP
  address** — and that composes with a `/join` route that is free and needs no account.
- A second channel, `goon-media`, carries **your own photos and videos to another person**, not to a service.

Two of its eight routes are `/report` and `/blocked`, which are **moderation** — explicitly named as
untouched by your 2026-08-14 authorization. Practice mode, which already ships, needs none of this.

### 5. The leaderboard
`task-board.md:29` · smallest of the five

What leaves the machine is an identifier, a display name and a version. **Not a score upload** — where
scores are submitted is a question the census declined to guess at. The decision is whether the port
reproduces this at all.

---

## Not a decision, and I should not have listed it as one

**Subliminals keeping its own settings file.** I listed this beside blocking decisions in roughly fifteen
summaries. It is a design choice already made and defended: the shared settings file resets *everything* to
defaults when any part of it is broken, so one bad phrase list would otherwise wipe your flash speed. It
blocks nothing, and it is reversible whenever you want it reversed. Keeping it on a list of things "waiting
on you" implied you owed an answer that nothing was waiting for.

---

## How this file is meant to work

A decision belongs here only when a board row says an owner must answer it and a lane may not. When you
answer one, the answer goes on the **row** first (and in `client/docs/architecture.md` where the row says
so); this file is derived and follows.

**The failure this file exists to prevent is the one that created it:** a list of open questions living only
in owner-facing prose, copied from summary to summary, drifting out of agreement with the queue until it was
telling you that you owed an answer you had already given.
