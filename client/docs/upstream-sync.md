# Upstream sync ledger (WPF `main` → `feat/crossplatform`)

The WPF product keeps shipping while the greenfield client is being built. This file is the
durable record of every sync: what upstream changed, what that costs the port, and what was
filed so nothing is silently left behind.

**Ground rules**

1. The `ConditioningControlPanel/**` tree on this branch is a **read-only archaeology reference**
   and must track `main` exactly. Conflicts inside it resolve to `main` (`--theirs`).
2. `client/**` is the port and is never touched by a sync merge (verify it, every time).
3. Release notes (`notes-v*.txt`) are the feature-level summary; **rows cite code, not notes**.
4. Every sync ends with: client build + tests green, an entry in this ledger, and board rows for
   every port obligation the delta created.
5. The procedure lives in the project skill **`wpf-upstream-sync`** (`.pi/skills/wpf-upstream-sync/`).

---

## Sync 2026-08-11 — v6.6.3 → v6.7.4 (merge `42286638`)

| | |
|---|---|
| Previous baseline | `b35facb6` (main tip 2026-08-04, in-tree `<Version>6.6.3</Version>`) |
| New baseline | `0c9947a6` (main tip 2026-08-11, in-tree `<Version>6.7.4</Version>`) |
| Upstream commits | 403 (147 fix / 97 feat) |
| Merge | `42286638` — 938 files, +221 198 / −13 173 |
| Conflicts | 5, all in the WPF tree: `.gitignore` (kept both), `CCP.Core/Models/AppSettings.cs`, `MainWindow/MainWindow.UiUpdates.cs`, `Services/TutorialService.cs` (took main), `Models/HapticSettings.cs` (delete/modify — restored main's file; the `CCP.Core` copy is abandoned first-attempt residue) |
| Port health after merge | `client/CcpClient.sln` 0W/0E, **683/683** unit green — no drift breakage |
| Port files touched by the merge | **none** (verified) |
| Release notes | `notes-v6.7.1.txt`, `notes-v6.7.2.txt` (v6.7.3/v6.7.4 shipped without notes files) |

### A. New product surfaces (each got its own board row)

| Upstream surface | Evidence | Port obligation |
|---|---|---|
| **Goon Game — 1v1 duels** | `Services/GoonGame/` (25 new files), `Resources/web/goon/` (**184 new payload files**) | Entire new web-core host: P2P media (64 MB video cap, supporter-gated send, ephemeral partner media), 10 s voice notes with consent + push-to-talk, share-link join, Discord rich presence, solo practice |
| **For You Feed on desktop** | `Services/Fyp/` (3 new), `Resources/web/fyp/` (5 M + 1 A) | Mosaic reel host + **ghost mode (see-through AND click-through)** — lands on the port's overlay/click-through work, webcam gaze scrolling, opacity, any monitor, survives Show Desktop |
| **Her Room (companion redesign) + Awareness** | `Services/Companion/` (14 new), `Services/Awareness/` (23 new), `Views/Controls/Companion/`, `Resources/sounds/companion_audio/awareness_apps.json` | **Reconcile against the port's own AI companion (SP-034/035/038/040/042/044/046) and awareness slice** — upstream shipped its own brain, memory, in-character behavior, opt-in awareness with a plain-words privacy dial (nothing / app names / + page titles), app picker, incognito detection, one-hour pause, and a Mute Voice Lines switch |
| **Trainer Card profile redesign** | `Views/Controls/` (113 new), `Resources/banners/` (12), `Resources/cosmetics/` (94), `Resources/achievements/` (6), `Services/Profile/` (3) | Card rebuild, 12 scene banners, 60 wardrobe adornments + editor + click-to-pin, achievement-gated unlocks, 12 blank-subject avatars, leaderboard privacy dialog |
| **Haptics v2 overhaul** | `Models/HapticSettings.cs` (+65: `SetLegacyProviderMirror`, per-provider v2 flags as a SET, schema-3 migration), rebuilt Lovense + Buttplug providers | Multi-toy concurrent providers, FunScript playback, temperament dial, Deeper editor parity |

### B. Behavior changes to code the port has ALREADY ported (parity drift — defect-class)

| Change | Evidence | Impact on landed port code |
|---|---|---|
| **Asset deselection honored everywhere** (#762 #798 #619) | `Services/Chaos/DtrhAssetManifest.cs` (+126/−46: new `EnumerateActive()` as the single "active pool" definition, `ScanItem` skip semantics, `BuildDisabledSet()`), `Services/Quiz/IntakeHostService.cs` (+83: `IsAssetActive(disabled, root, fullPath)`, `TopMarksPercent = 90.0`) | The port's DTRH asset manifest and intake asset provisioning **predate this fix** — user deselection in the Assets tree is not honored, and chaos overlays / Graded Intake were the exact surfaces upstream had to fix. **P0 row filed.** |
| **Graded Intake payload gained accents + AI rework** | `Resources/web/intake/core/accents.js` (**new, +350**), `Resources/web/intake/core/ai.js` (+79/−22) | SP-054 (in flight at merge time) is built against the v6.6.3 intake payload and the pre-merge `IntakeHostService`. Baseline is internally consistent — **delta row filed**, packet not retargeted mid-wave. |
| **`ChaosWebViewHost` +19** | `Chaos/ChaosWebViewHost.cs` | The class the port's web-core hosts are modeled on; re-read before the next host slice. |
| **Chaos overlays** | `Chaos/ChaosFlashOverlay.cs` (+17 — avoid-the-center exclusion box), `Chaos/ChaosGifCascadeOverlay.cs` (+72) | Flash exclusion box is a new user-facing knob on a ported surface. |
| **Video engine now default (out-of-process)** | browser-video engine promoted to default; grace pause on first panic press for mandatory video | The port landed the browser-video handoff spike; the *default* + grace-pause semantics are new. |

### C. Smaller deltas (tracked in the backlog row, itemized here)

- Spiral opacity cap raised 50% → **100%**
- Velvet Vault / **Exclusives tab** replaces the launcher popup (spotlight shelf, backdrop, coming-next peek)
- **Modular installer**: ~1.1 GB moved into downloadable content packs; wedged pack download self-heals on next launch (ties into the port's open content-pack decision)
- Lockdown no longer swallows bare Esc system-wide (#680)
- Audio: playback runaway that killed the app when Windows audio died (#778 #779); ducking no longer silences the app's own new video engine (v6.7.2 hotfix)
- Video: blurred background fits the true aspect ratio (#786)
- Freeze fixes across the board; Brain Drain captures off the UI thread (#777)
- Lock card: paste/undo cheats blocked, AltGr + IME input fixed (#734)
- Quests: voice-command spiral/pink time counts (#719); streaks stop flashing 0
- Programs: spoken mantras credit correctly, session bubble rate, per-task how-to line
- Companion: replies with effects no longer truncated, raw JSON can never leak into the speech bubble
- FYP: undecodable clips show a notice and swap out instead of black tiles
- Localization: **701 keys × 7 languages + 364 zh-Hans** backfilled
- `redist/MicrosoftEdgeWebview2Setup.exe` is now committed (WebView2 load-bearing for FYP, Exclusives, DtRH, Goon Game, video engine)

### D. Gaps this sync exposed in the port's own guards

- **The client asset-manifest parity test only covers trees the client already ships.** A brand-new
  upstream payload tree (`web/goon/`, 184 files) produces **zero** test signal — the suite stayed
  683/683 green while an entire product surface appeared upstream. Coverage of "trees that exist
  upstream but not in the client manifest" is missing; folded into the backlog row.
- **First-attempt residue keeps manufacturing merge conflicts.** `CCP.Core/` (and the `CCP.Avalonia.*`
  projects) hold stale forks of WPF models, so upstream edits to `Models/*.cs` land as
  delete/modify conflicts forever. Resolution rule is in the skill; a cleanup is not port work.
