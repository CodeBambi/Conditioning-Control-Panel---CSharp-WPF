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
2026-08-18** (in `task-board.md` — cited without a line, because the board is rewritten every wave),
and the answer was better than the three options that row had offered:

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
the entitlement row of `task-board.md` · scope: one further step, not the whole feature

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

### 2. The haptic dependency - ANSWERED, AND NOW BUILT
the haptic rows of `task-board.md` · this section is kept only so the answer is findable

**You admitted BOTH routes on 2026-08-23, and both are now implemented and merged.** Buttplug.io through
Intiface Central and Lovense through Connect/Remote are driven TOGETHER by a composite sink, which is
upstream's own shape - its device manager connects every enabled provider concurrently, and its own class
doc says the old single-active-provider design meant *"a Lovense user could not also drive an Intiface toy"*.

Two things were decided during the build that you should know about, because both changed user-visible
behaviour:

- **The capability probe no longer contacts a server for a feature you have not switched on.** It had been
  firing an HTTP GET at `127.0.0.1:20010` on *every launch* of a default install, for a feature whose master
  toggle was off. It is now gated on the same conjunction upstream uses for its own auto-connect.
- **Both provider flags default OFF**, matching upstream, and the two checkboxes ship with them - so the
  refusal names a control that exists instead of telling you to tick something that does not.

**What is still owed is yours and it is not code:** install Intiface Central or Lovense Connect, pair a toy,
tick the route, and report whether a level is felt and whether a stop silences it. Nothing in this
repository has driven a real toy on any platform, no test opens a socket to a real haptic server, and the
rows say so.

### 3. For You Feed gaze - the 2026-08-22 answer stands, and a SECOND blocker was cleared 2026-08-23
the gaze row of `task-board.md` · nothing further is owed by you here

Your 2026-08-22 decision is unchanged: the local BlazeFace/FaceMesh/Iris pipeline is the default, a
third-party engine is a visible choice only after a documented admission, and the selector is not permission
to send webcam data anywhere. The third-party side was then closed UNAVAILABLE on 2026-08-23 - every
mainstream engine fails the commercial-rights bar on TRAINING DATA rather than code licence.

**A scoping pass then filed the row BLOCKED on a webcam-capability decision. That was a misreading and the
row is unblocked.** `capability-inventory.md`'s "Expanding sensors... requires a consent-contract revision
and owner review" governs EXPANSION BEYOND the contract the same section already writes down - and that
section already admits the local engine, names it the default, and specifies its full privacy contract
(memory-only derivatives, audio never opened, camera only after current explicit consent AND an explicit
start). Implementing as specified executes an existing admission rather than widening a boundary.

**What would still be expansion, named so it cannot be quietly stretched:** any remote or third-party
engine, any persistence of a frame or derivative, any transmission of anything camera-derived, the
microphone, or telemetry over camera data.

### 4. Goon Game beyond practice mode - STILL YOURS, AND DELIBERATELY SO
the Goon Game row of `task-board.md` · the row is `BLOCKED` and says why in its own text

**I was given full authority for the 2026-08-23 session and used it on every other open question. This one I
declined.** Not for lack of evidence - the census, the four upstream `GOON_*.md` contracts and the shipped
client tree settle every factual question. It is what the decision opens:

- It is the port's **first outbound network boundary**. Today the only `HttpClient` here is a loopback probe.
- With no relay server a successful connection is **direct**, so **each player learns the other's public IP
  address** - and that composes with a `/join` route that is free and needs no account.
- A second channel carries **your own photos and videos to another person**, not to a service.
- Two of the eight routes are `/report` and `/blocked`, which are **moderation** - explicitly left untouched
  by your 2026-08-14 authorization. Admitting them REVERSES a standing decision rather than making a new one.
- Hosting is tier-2 gated behind an `X-Auth-Token` header, so it also depends on decision 1 below.

**Practice mode already ships and needs none of it.** So the cost of waiting is zero and the cost of being
wrong is a home IP address and private media reaching a stranger. A join-as-guest-only subset is the smallest
thing that could be admitted separately - it needs neither the token nor the moderation routes - but it still
exposes the guest's IP to the host, so it is a smaller decision, not a free one.

### 5. The leaderboard
`task-board.md:29` · smallest of the five

What leaves the machine is an identifier, a display name and a version. **Not a score upload** — where
scores are submitted is a question the census declined to guess at. The decision is whether the port
reproduces this at all.

---

## Answered 2026-08-23

Four decisions the board had been carrying, answered in one pass. Each is recorded on its row; this is the
derived copy.

**Haptics — admit BOTH provider routes.** Buttplug.io through Intiface Central and Lovense through
Connect/Remote. Both are clients of a separate server the user installs, so no driver or kernel boundary
opens. Buttplug brings one NuGet package the shipping app already references; Lovense brings none at all.
The owner can run headed device checks, so the wire is proven here and the device result is reported back —
the row does not close on a build.

**For You Feed gaze — research first, admit later.** The third-party engine is unnamed, and naming it is a
decision about a biometric and network boundary. A comparison of fully-local candidates is gathered as
EVIDENCE; no dependency and no code until the provider is named. The 2026-08-22 decision stands: both
engines, user-selectable, current consent, separate calibration, no silent fallback.

**Hosted-page motion — port `MotionLevel` rather than force the flag.** The obvious fix was an accessibility
regression: forcing `--force-prefers-no-reduced-motion` with no user-facing control overrides an OS
preference a user cannot then restore, which this port's own motion-inheritance seam names as the betrayal
direction. Upstream avoids that by owning a `MotionLevel` setting and driving the flag from it. Default is
`Full`, matching upstream.

**Citation inventory — build the tool before doing the work.** 297 entries and 81 tier-1 verdicts are not a
slice; a regenerate mode that recomputes `changedAtSync` mechanically turns the bulk into tooling and leaves
only the verdicts as judgment.

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
