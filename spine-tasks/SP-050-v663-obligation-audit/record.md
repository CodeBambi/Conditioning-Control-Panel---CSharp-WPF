# SP-050 record — Host-obligation audit across the remaining v6.6.3 deltas

**Task:** spine-tasks/SP-050-v663-obligation-audit · **Review Level:** 2 · **Shape:** zero-product-code audit (SP-030 admission-record shape)
**Engine-review presence (T-2):** recorded per `spine_review_step` call below.

**Ground-truth discipline:** the payload tree (`ConditioningControlPanel/Resources/web/`) in this worktree is **byte-identical to main** (b35facb6) — `git diff main --name-only -- ConditioningControlPanel/Resources/web` = 0 files — so payload citations from the working tree are main-true. The WPF `Services/` tree differs from main in 20+ files (merge-resolution debris, per `client/docs/main-sync-2026-08-04.md`); **all WPF host citations are against git `main`** (read via `git show main:…` / `git grep … main`).

---

## Engine-review log (T-2)

| Step | Type | Result | Artifact |
|------|------|--------|----------|
| 1 | plan | (recorded at the Step-1 boundary below) | `.reviews/` |
| 2 | plan | (pending) | `.reviews/` |
| 3 | plan | (pending) | `.reviews/` |

---

## Step 1 — delta inventory + payload-side enumeration + pre-approach consult

### The six deltas (from `client/docs/main-sync-2026-08-04.md`, release trains 6.4.0→6.6.3)

1. Brain Drain rework + Brain Melt
2. FX overhaul (AmbientFxCanvas, tiers, reduced motion)
3. Hourglass
4. Bottomless Fall
5. NUX first-run
6. Weekly Intake Pass

### Payload-side facts (each `payload file:line`; tree = main-verified)

#### Delta 1 — Brain Drain rework + Brain Melt (payload side)

- **DTRH-side:** `braindrain` is a bubble variant of kind `live`, payload `{ kind:'overlay', overlay:'braindrain' }` (`game/variants.js:46-48`; sprite/weight/fuse :48; codex entry `game/catalog.js:398`; debut spawn `game/chaosRun.js:3649-3652`; detonation sfx `chaosRun.js:579`). Since the 2026-07 hard cutover **every visual effect renders in-world** — `game/payloadFx.js:15` ("ONE reused element per kind"), the braindrain wash at `payloadFx.js:111-122` (`showBraindrain`, hold + fade); the `fire-payload` bridge keeps ONLY video/audio kinds and a visual kind arriving there is logged as a page/host version mismatch (main `DtrhHostService.cs:524-529` comment, `:547-549` handler). **No bridge message carries braindrain.**
- **Intake-side:** the braindrain wash is a page-internal big-reward visual (`intake/core/contracts.js:700` (gifs property doc), `:820` (big-reward variant pool)) — self-driven, no host message.
- **Payload-side verdict facts:** nothing in either payload asks the host for anything Brain-Drain-specific beyond what b1–b5 already landed.

#### Delta 2 — FX overhaul (payload side)

- **Quality tiers are page-internal:** `shared/quality.js:1-50` — the `Q` knob block (desktop defaults; `setQuality('mobile')` lightens MSAA/DPR/bloom/fog/wall/tube); the tier is chosen page-side (`shared/capability.js:40-44`: coarse pointer / small viewport → `mobile`).
- **Reduced motion is a page-side probe:** `shared/capability.js:35` (`matchMedia('(prefers-reduced-motion: reduce)')`), `:57` (reduced → 2D mode, `canTry3d:false`).
- **The DTRH page consumes NO host FX setting:** `boot.js` init handling and `engine/settings.js:48,67,183` carry only bubble-motion prefs (`runMotion`: Mixed/FloatUp/RainDown/RoamBounce) — gameplay motion, not chrome FX. The page never reads an FX tier or reduced-motion flag from `init`.

#### Delta 3 — Hourglass (payload side)

- **The unlock:** `custom_duration`, Depth branch, 300 sparks (`game/catalog.js:72-74`) — bought through the **existing `purchase-upgrade` meta op** (`game/warren.js:1013`; ownership read `warren.js:108` `ownsCustomDur()`).
- **The dial:** a free 2min..2h length dial under the presets (`warren.js:105`, `:609` (custom length only for Hourglass owners), `:699-734` (dial + tip)); non-owners snap back to a preset (`warren.js:142-145`).
- **The wire:** `buildSetup()` (`warren.js:164-181`) — "Same shape the host has always persisted"; page-side run clamp lifts to 7200 (`game/chaosRun.js:144`).
- **The Esc-freeze gate (crafted hourglass — the OTHER hourglass):** recipe `the_hourglass` (`game/crafting.js:104` — grid `CCC/.P./CCC`); Esc truly freezes mid-descent only with ≥1 crafted (`chaosRun.js:4384-4386` `canHoldPause`; soft-pause copy `engine/scene.js:435-437`, `:1480-1483`; run-ends-behind-card warning `scene.js:740`; lesson line `chaosRun.js:3739`). **Already in the greenfield's 21-recipe whitelist** (`client/src/CcpClient.Desktop/Features/Dtrh/DtrhMeta.cs:66` = main `ChaosCraftingIds.cs:27`).

#### Delta 4 — Bottomless Fall (payload side)

- **The unlock:** `endless_mode`, Depth branch, 600 sparks (`game/catalog.js:75-77`) — same `purchase-upgrade` op (`warren.js:1013`; ownership read `warren.js:109` `ownsEndless()`).
- **The toggle + wire:** an ∞ toggle beside the presets for owners (`warren.js:690`); `buildSetup()` carries `endless: !!setup.endless && ownsEndless()` (`warren.js:134-139`, `:170-171`).
- **The run:** page reads `rc.endless` from run-config (`chaosRun.js:147`); endless runs self-extend (`chaosRun.js:231` `ENDLESS_BASE_SEC`, `:348-351` lap/lift state, `:450` band lift, `:2526-2538` `tickEndless` + no closing states, `:2770` tick call, `:3001` draft top-up, `:3770`/`:3874` region-cycle arm/disarm, `:3944` lap count); biome loops wrap I→IV (`game/biomes.js:511`; `game/regions.js:149`); Deepening filler boons (`game/boons.js:272`); recap headline = laps (`game/overlays.js:277-278`).

#### Delta 5 — NUX first-run (payload side)

- **NOTHING.** The NUX commit `10b555e2` ("feat(nux): first-run experience — revive dead tour, feature intro cards, premium celebration") touches exactly 7 WPF files — `MainWindow.Patreon.cs`, `MainWindow.SubscribeStar.cs`, `MainWindow.TabNavigation.cs`, `MainWindow.xaml.cs`, `Models/AppSettings.cs`, `Windows/FeatureIntroPopup.xaml(.cs)` — zero payload files. The first-run mod picker (commit `3f31a9c9`, packs wave 2) is WPF dialogs (`ModPickerDialog`, `ModManagerDialog`, `ModPackCatalog`) + `ModService` two-root probing + 35 loc keys × 9 languages — again zero `Resources/web` changes. (DTRH's own scripted-first-run/VN beats predate v6.6.3 and are b4-landed.)

#### Delta 6 — Weekly Intake Pass (payload side)

- **The intake web-core bridge vocabulary** (`intake/web-shim.js` — "exactly like dtrh/bridge.js", :6):
  - page→host: `ready {protocol}` (:94 — host flushes queued init on receipt), `log` (:89), `quiz-result {result:QuizRunResult}` (:188-190 — "the whole point"; C# drafts a session from it), `pong` (:222), `heartbeat` ~2s rAF (:224-227), `boot-error` (:231), `intake-close` (:242 — abort semantics: NO QuizRunResult, no session drafted), `exit` (`boot.js:1842`), `fullscreen-set {on}` (`ui/fullscreen.js:102` — "never touch the browser API: enter/leave post `fullscreen-set` and C# …").
  - host→page: `init {config:BootConfig, ai}` (:107, mapped :122-141 — niche, caps, endless, steerValve, priorRun, **ai{serverBase, authToken}**, m2Test, media, subjectId, **subliminals {text, audio?}**), `ping` (:222), `payload-state {kind:'video', on}` (:218-220 — native mandatory-video cover, DTRH parity).
- **Serving origins:** heavy audio (VO/sfx/music) moved OUT of the installer — served under `https://ccp.game/intake/…` (legacy full install) or `https://ccp.content/intake/…` (downloaded pack) (`intake/core/audioSrc.js:4-12`); manifests `sfx_manifest.json`/`vo_manifest.json` stay fetched resources (:26; `render/audio.js:449-451`, `:1104`); chimes borrowed from the dtrh tree (`render/audio.js:222-229`, `:247-249`).
- **VO corpus + persona banks + captcha families + corruption + menu/music/voice** are all page-internal (`intake/` tree: `core/`, `render/`, `ui/`, `banks/` — engine, render, 4 captcha families, 4 persona banks per the main-sync inventory).

### Pre-approach consult (Step 1 gate)

**Mode:** solo (T-7: council unproven; `kimi-api` unregistered — PROMPT Do-NOT). **Requested route:** Opus 5 main (2026-08-04 rewire). **Actual answering model:** NOT surfaced by the consult tool response (no model identity header — recorded honestly, same provenance discipline as SP-022…027, SP-049). **Three calls:** the first two verdicts TRUNCATED mid-sentence (the SP-027 truncation class); the third call (terse-completion request) completed the verdict. Truncations recorded, never silently stitched.

**Verdict: PLAN + three CORRECTIONS — the enumeration is strong; two verdict-shaping errors would make the record dishonest if left. All adopted:**

1. **CORRECTION 1 (adopted): "payload self-drives" must NOT collapse into "NOTHING".** The SP-049 lesson was a small obligation because the WPF HOST was small; for FX (delta 2) and NUX (delta 5) the WPF host carries the ENTIRE feature. The obligation-table vocabulary gains a **fifth cell: `BLOCKED-ON-<named prerequisite rows>`**, and **every cell gets an explicit scope** (`DTRH host surface` vs `client shell`). NUX: the client must provide a first-run surface (one-shot intro-card window class), a persisted seen-list (`SeenFeatureIntros`-equivalent), the post-welcome tour hand-off, and the premium-celebration hook — 100% client obligation, but **dependency-BLOCKED** (no tutorial/tour subsystem, no entitlement/tier providers, no dashboard tabs to hang cards on). FX: DTRH host surface NOTHING (page-internal tiers/probe) / client shell REAL, BLOCKED on dashboard/chrome rows.
2. **The FX reduced-motion finding (adopted):** since the DTRH page probes `prefers-reduced-motion` itself (`capability.js:35,57`), the client owes a **capability-probe obligation, not a message**: the embedded webview must INHERIT the OS/user reduced-motion state — verify what WebView2/WebKitGTK report for `matchMedia('(prefers-reduced-motion: reduce)')` on both platforms. Honest limit shape: open probe question, unverified on this laptop; Linux unproven (WSL zero-distros class). Consequence if wrong: a reduced-motion user silently gets the 3D descent — user-observable, belongs in the table.
3. **CORRECTION 2 (adopted): Hourglass + Bottomless Fall = TWO deltas, ONE packet.** Separate user-observable features sharing exactly one seam (the run-setup/run-config persist+deal path). Splitting touches the same 2-3 files twice and forces a second regression pass. Size **S** (one clamp gate + one additive bool through persist → init → run-config + the habit-rail exclusion); evidence class = unit + one headed round-trip (`endless` reaching `rc.endless`, `chaosRun.js:147`). **Two board rows** (two user-observable behaviors) pointing at one packet — rows not merged.
4. **CORRECTION 3 (adopted): the intake serving origins carry obligations beyond messages/routes.** The audio moved OUT of the installer (`audioSrc.js:4-12`) — the client owes a **content-pack acquisition + resolution path** (two-root probing, `ccp.content` origin), an asset-pipeline dependency, likely its own row. `init` also carries an **AI auth token** (`ai.serverBase`/`authToken`) and **subliminals** (user text + possibly data-URI audio) — privacy-boundary items the verdict must name, never silently widen.
5. **Final binding (adopted):** five verdict values (add `BLOCKED-ON-<row>`); sizing verdicts state dependency rows by name; a delta whose prerequisites are absent is sized `—` (unsizable), not `L`.

---

## Step 2 — WPF host-side enumeration + the obligation table

(pending — four wpf-archaeologist reports + direct DtrhHostService/ChaosModels reads against main)

---

## Step 3 — sizing verdicts + board-row filings + pre-completion consult

(pending)

---

## Step 4 — verification

(pending)
