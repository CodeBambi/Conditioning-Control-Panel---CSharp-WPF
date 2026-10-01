# Super effects - contract for the effect lanes

Owner decisions (2026-10-01). Super = BASIC tier (tier 1) and up, every Patreon tier. Free players see the
switch locked, get a 10 s try of ONE effect per week (weekly free rotation, this week's slot is picked by
week index over the `SuperEffect` order), and can never switch the rest on. Super effects are ADD-ONS: they
run on top of the base effect, the player can have both. Creep is the one new effect (it sits beside the pink
filter, it does not replace it).

Design reference, animated, with every timing and number: `handoff-1001/super-effects-mockup.html` in the main
checkout (C:/Projects/Conditioning-Control-Panel---CSharp-WPF/handoff-1001/), published copy
https://claude.ai/artifact/2bZLs83jd5AuuSqaoqK69m . Port the feel, not the canvas code: read the matching
`mk...` function and copy its constants (spring 90/9, timings, counts) into a pure, tested C# class.

## The seam

- `SuperAccess.IsOn(SuperEffect.X)` is the ONLY question an effect asks. Call it where the base effect
  spawns or ticks. When false, behaviour must be byte-identical to today (the base effect is untouched).
- `SuperAccess.Changed` fires when a switch flips or the tier changes: tear the effect down cleanly.
- Never read `AppSettings.SuperEffectsOn` or the tier directly.

## Hard rules (each was broken once)

1. Pure logic first: the effect's maths (springs, timelines, shard generation, ramps) lives in a WPF-free,
   Skia-free, App-free static class with xunit tests, the way `FlashShatter` / `FlashDrag` / `AmbientBubbleMotion`
   do. The layer or window only draws.
2. Panic always wins. Every Super effect stops at once on the panic key and on the emergency exit, like the base
   effect it rides. No Super effect may be more permissive than panic, Lockdown or a leash.
3. `MotionFx` decides motion: Off = still or a 120 ms fade, Reduced = half amplitude and speed. Photosafe means no
   flicker: Lights Down's stutter and Vortex's gif flicker obey it (Lights Down skips the stutter; Vortex's gif
   flash becomes one soft fade).
4. Render through the compositor layers (`Services/Compositor`, Skia) when the base effect does. No new
   fullscreen windows if a layer can carry it. Brain drain keeps `ExcludeFromCapture` semantics.
5. Every user-facing string is a loc key in ALL 9 language files, edited as lines, never re-serialised, no BOM.
   Copy voice: dry, short, plain, no em-dashes, no exclamation marks in chrome, no marketing.
6. No CCP.Core / CCP.Avalonia / CCP.VR edits. No server code in this repo.
7. Media: any picture or gif a Super effect draws comes from the player's own configured source through the
   existing pool (`FlashService` image pool, `RemoteFlashPool`, Scrolller consent rules). Never bundle new
   adult media. Remote fetches check `MediaSource != "local" && HasRemoteMediaConsent`.
8. Perf: no per-frame allocation in a hot path, cache gradients and sprites, respect `PerformanceProfile`.
9. PRs are DRAFT, cap ~600 changed lines, base = `feat/super-base`. Larger work ships as a stack.
10. Release builds in CI: run `dotnet test -c Release` once before calling a lane done.

## Lane map (code to read first)

| Effect | Rides | Read first |
|---|---|---|
| FlickerDeck | flashes | Services/Flash/*, Services/Compositor/FlashLayer.cs, FlashShatter.cs, FlashDrag.cs |
| InnerBloom | bubbles | Services/BubbleService.cs, BubbleFace.cs, Compositor/BubbleLayer.cs, AmbientBubbleMotion.cs |
| Afterglow | subliminals | Services/Subliminal/SubliminalService.cs, Compositor/SubliminalLayer.cs |
| Vortex | spiral overlay | Compositor/SpiralLayer.cs, Services/Notifications/OverlayService.cs |
| Creep | new, beside pink filter | Compositor/PinkTintLayer.cs, Features/PinkFilterFeatureControl.xaml.cs |
| LightsDown | mandatory video | Services/Video/VideoService.cs, AttentionTargets.cs |
| Scrawl | bouncing text | Services/Subliminal/BouncingTextService.cs, Features/BouncingTextFeatureControl |
| Undertow | brain drain and melt | Compositor/BrainDrainLayer.cs, BrainDrainCapturePump.cs |

The scaffold lane (switch control on tiles and panels, weekly preview, loc keys) is separate; lanes expose
their own settings only for sub-options and wire the switch through `SuperAccess.Set`.
