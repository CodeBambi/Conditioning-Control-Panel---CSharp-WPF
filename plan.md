# Arcademy slices 1-2 — plan (worktree agent-af067db5b3612ad35)

Board row: "Port the Arcademy upstream surface, decomposed into its eight slices" (P1 OPEN).
Scope: slice 1 (payload serving + boot handshake) and slice 2 (init projection + set-setting
echo loop) ONLY. Slices 3-8 are out of scope and are named where they touch a field.

## Citations verified before writing a line

* `ConditioningControlPanel/Services/Arcademy/ArcademyHostService.cs:116` —
  `public static readonly bool DoorAvailable = false;` (static readonly, not const, so the
  guard in `Launch` is not CS0162-unreachable). VERIFIED.
* `ConditioningControlPanel/Views/Tabs/PlayTabView.xaml:1312` — `<Grid x:Name="SlotArcademy"
  … Visibility="Collapsed">`. VERIFIED.
* `ConditioningControlPanel/MainWindow/MainWindow.PlayTab.cs:106-112` — the card's visibility
  re-asserted from `ArcademyHostService.DoorAvailable` on every repaint. VERIFIED.
* `ArcademyHostService.cs:136-141` — `Launch()` refuses on the door BEFORE the T2 bar
  (`TierGate.DemandLab`, :146) and before AudioOnlySession (:153). VERIFIED.
* The borrow: `Resources/web/arcademy/shell/shell.js:76`
  (`new URL('../../dtrh/assets/bubbles/effects/spirals/' + file, import.meta.url)`) and
  `games/the-deep-end/pressure.js:182` (`SPIRAL_DIR: '../../../dtrh/assets/bubbles/effects/spirals/'`).
  VERIFIED — both resolve to `<origin>/dtrh/assets/…` from a page served under `<origin>/arcademy/`.
* `engine/index.js:184` — six `provider/assets/ae-ph-*.svg` placeholder tiles. VERIFIED
  (payload census: 91 files = 77 js, 6 svg, 4 png, 1 mp3, 1 md, 1 html, 1 css).

## Design (reuse, do not invent)

* Payload glob: a fifth linked read-only `Content` glob in `CcpClient.Desktop.csproj`
  (`web/arcademy/**` → `payload/arcademy`), byte-identical, legacy tree stays the owner.
* THE BORROW is solved the way Goon/Intake already solve theirs
  (`Features/Goon/GoonParticipant.cs:126-133`, `Features/Intake/IntakeParticipant.cs:94-102`):
  `LoopbackServer` is given roots, not a new server. Arcademy's roots are BOTH
  `<base>/payload` — the output mirror of `Resources/web` — and the page URL is
  `/dtrh/arcademy/index.html`. Then `../../dtrh/assets/…` from `/dtrh/arcademy/shell/shell.js`
  resolves to `/dtrh/dtrh/assets/…` → `<base>/payload/dtrh/assets/…`, i.e. the real DTRH tree.
  Nothing is copied; the `/dtrh/` prefix is the reused server's hardcoded route class.
* `.svg` → `image/svg+xml` is added to `LoopbackServer`'s PAYLOAD MIME table only, never to
  `UserMime` (user files must not be served as a scriptable document type). The
  `IntakeServingTests` `.svg → 415` pin is rewritten to assert the new truth plus a still-denied
  extension, so deny-by-default stays proven.
* Boot handshake (`ArcademyHostService.cs:388-404`): on `ready` → exactly one `init` per boot,
  then `fullscreen {on}`. `SeedNativeState` (:415) is slice 5 and is NOT built.
* Echo loop (`:1164-1188`, `:1192-1319`, `:1777-1829`): set-setting → clamp → persist → echo the
  POST-CLAMP value; suppression flag so the app-side watch does not double-post; repush the whole
  projected key set when the settings instance is replaced (`PersistenceStore.SettingsReplaced`).
* Door: `ArcademyDoor.Available` (static readonly false) + `ArcademyLaunch.Attend()`, which
  refuses BEFORE constructing anything — no participant, no listener, no window.
* `lexicon`/`palette` project as the MOD-RESOLVED table, which is empty in this build (no mod
  root). Measured, not assumed: every one of the 318 neutral rows is rendered identically by the
  page without the host table, because `core/lexicon.js` `t(key, fallback)` falls back to the
  caller's inline value and those values are the C# table's values verbatim.

## Files

src: `Features/Arcademy/{ArcademyDoor,ArcademyServingRoots,ArcademyParticipant,ArcademyLaunch,
ArcademySettingsDocument,ArcademyProtocol,ArcademySession,ArcademyLocalAssets}.cs`,
`CcpClient.Desktop.csproj`, `Features/Dtrh/LoopbackServer.cs` (one MIME row).
tests: `CcpClient.Tests/{ArcademyServingTests,ArcademyProtocolTests}.cs` + the reworked
`IntakeServingTests` svg pin. No headless tests: nothing here has a visual tree.

Not proven by any of this: no headed evidence, no WebView, no rendering, no Linux run.
