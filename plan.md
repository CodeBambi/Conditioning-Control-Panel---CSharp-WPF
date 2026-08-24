# Slice 5 — native-state suspension (working note, removed before report)

## What upstream actually suspends (read, not assumed)

`Services/Arcademy/ArcademyHostService.cs`:
- `Suspend(bool on, string reason)` `:294-303` posts `{type:"suspend", on, reason}`; the PAGE
  drops every effect and pauses the class (`Resources/web/arcademy/shell/shell.js:1250-1272`
  `applySuspend`). Reason vocabulary: `video` | `audio-only` | `panic`.
- Producers: `OnVideoStarted` `:1714`, `OnVideoEnded` `:1716-1730`,
  `OnBrowserVideoPlayingChanged` `:1699-1712`, `AudioOnlySession` `:1832-1852`,
  `OnSettingsCurrentReplaced` `:1791`, and the boot seed `SeedNativeState` `:409-440`.
- Precedence: a PANIC suspend outranks the video's own un-freeze (`:1720-1726`).
- Hooks on before Show `:207-211`, off in `DisposeAll` `:2013-2015` before the meta flush and
  the host dispose.

So the direction is: THE CLASS YIELDS TO NATIVE MEDIA. The packet's paraphrase ("the port's own
native surfaces and media must yield") is the OTHER direction, which exists upstream only as two
narrow courtesy gates keyed on `ArcademyHostService.IsActive`
(`MainWindow/MainWindow.Marquee.cs:919`, `Services/Companion/BarkService.cs:729`) — both in other
lanes' files and both dead here, because this port has no Arcademy host window. Reported, not built.

## Inputs this build has
- mandatory video: REAL (`Effects/MandatoryVideoEffect.Playing` -> `IVideoSurface.Showing`).
- AudioOnlySession: ABSENT (no such setting; already recorded twice on the row).
- browser media: ABSENT (`client/docs/port-completeness-census.md:772` — no browser, no
  browser-media session concept). Not stubbed to false.

## Build
1. `ArcademySession`: `SeedNativeState()` at the end of `Ready()` (after init + fullscreen),
   `NativeVideoChanged(bool)` edge entry with the panic precedence, reusing the existing
   `NativeStateOwnsScreen` predicate.
2. `Features/Arcademy/ArcademyNativeSuspension.cs`: follows a real `MandatoryVideoEffect`,
   level->edge, sets/restores the predicate, unsubscribes on dispose.

## Prove the OUTCOME
The real payload over `arcademy-boot-harness.mjs`: the page's own `boot.js:195-205` narrates
`suspend ON (video)` / `replayed buffered suspend (video)` only after `shell.onSuspend` returned,
so the real shell's freeze path ran. Plus unit facts against a REAL `MandatoryVideoEffect`.
