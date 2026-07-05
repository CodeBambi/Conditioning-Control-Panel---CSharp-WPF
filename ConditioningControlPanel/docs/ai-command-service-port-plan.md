# AiCommandService Port Plan (`AllowAiToControlEffects`)

**Goal:** Port the WPF AI-triggers-effects feature so the model can emit commands that the app dispatches to effect seams. Unlocks the signature AI feature for BOTH working providers (LocalAiService + OpenAiService). Source of truth: WPF `Services/Commands/AiCommandService.cs` (224 lines) + the per-type executor classes. Archaeology: 2026-07-05.

## What's ALREADY in Core (no work needed)

- `AiCommandData` + `AICommandType` enum (12 members) + `CommandData` records — `CCP.Core/Models/AiCommandData.cs` + `CCP.Core/Models/CommandData/*.cs`.
- `IAiResponseParser.Parse` — **already populates `ParsedAiResponse.Commands`** (`AiResponseParser.cs` ExtractEffects calls `AiCommandData.ParseCommand`). Registered in Avalonia DI.
- `IPromptService.BuildEnrichmentMessage` — the `[CONTEXT BLOCK]` text is **already in Core** (`CCP.Core/Services/AIService/Enrichment/PromptService.cs`). Lists all 12 command types + the JSON schema.
- Per-effect gates (`AllowAiFlash/Video/Audio/Bubbles/Subliminal/Overlay/Bounce/LockCard/Haptic/GetBackToMe`) + master `AllowAiToControlEffects` + `MaxAiHapticIntensity` (default 0.6) — all in `CompanionPromptSettings` (Core).
- `IAiCommandService` interface (`CCP.Core/Services/Commands/IAICommandService.cs`: BeginBatch / ExecuteCommand / CancelAllCommands).

## The `AICommandType` enum (12): `none, spiral, mantra_lockscreen, bubbles, video, audio, pink, flash_image, subliminal, getbacktome, bounce, haptic`

Per-type `Data` records: `FlashImage(Amount,Duration,Size,Opacity)`, `Bubbles(On,Frequency)`, `Subliminal(Text,Opacity)`, `SpiralPinkFiler(On,Intensity)` [spiral+pink], `Bounce(Words,On)`, `HapticCommandData(Intensity,Duration)`, `MantraLockscreen(Mantra,Amount)`, `Media(Title,Path,Random)` [video+audio], `GetBackToMe(Delay,Token,Commands,Text,JsonOnly)`. Only `getbacktome` carries a `Token` (for cancellation).

## Dispatch order (ExecuteCommand, `AiCommandService.cs:33-101`)

1. null `Data` → drop.
2. no `CompanionPrompt` settings → drop.
3. **MASTER gate**: `!AllowAiToControlEffects` → drop+log.
4. **PER-EFFECT gate**: `IsEffectAllowed(cmd)` switch (spiral+pink→`AllowAiOverlay`; mantra→`AllowAiLockCard`; etc.) → drop+log if off. `none`/unknown→false.
5. **PER-BATCH CAP**: `Interlocked.Increment(ref _batchCount) > MaxCommandsPerResponse(3)` → drop+log. (Counted ONLY for commands passing gates 3+4.) `BeginBatch()` resets `_batchCount=0`.
6. Append live-action feed (cosmetic — event/sink or drop).
7. **TOKEN tracking** (getbacktome only): cancel prior same-token cmd, new CTS into `TokenCancellationSources`.
8. `CommandFactory.CreateCommand(...)` → `await executor.ExecuteAsync()` (depth=0; getbacktome recurses, depth cap 2). try/catch.
9. finally: remove token.

`CancelAllCommands()`: cancels+disposes all `TokenCancellationSources` (only getbacktome in-flight `Task.Delay`s).

## Per-type dispatch + seam status

### ✅ Ports cleanly (Core seam exists, matching signature)
- **spiral** → `IOverlayService.BypassLevelCheck=true; Start(); RefreshOverlays()` + `settings.SpiralOpacity/Enabled` + `Save()`. Intensity clamp 0-30.
- **pink** → same as spiral but `settings.PinkFilterOpacity/PinkFilterEnabled`. Intensity clamp 0-30.
- **mantra_lockscreen** → `ILockCardService.ShowLockCard(phrase, amount, customStrict:true, isTest:false)`. Amount clamp 0-5, Mantra max 200 chars, empty→no-op.
- **video** → `IVideoService.PlayRandomVideo()` (Random or empty Path) OR `PlaySpecificVideo(validatedPath, false)` if not `IsPlaying`. Path validation rejects `..`, resolves under assets, requires File.Exists, else random fallback. Exts: .mp4/.mkv/.avi/.mov/.wmv/.webm.
- **bounce** → `IBouncingTextService.Start(words)` / `Stop()`. (Drop the WPF `true` startNow arg.)
- **getbacktome** → `IAiService.GetBambiReplyExAsync(...)` + `IAvatarWindowService.GigglePriority(...)`. Delay clamp 1-600s, recursion depth ≤2. Token-threaded (cancellable `Task.Delay`).

### ⚠️ Needs seam work (signature/semantic gap)
- **flash_image** → Core `IFlashService.TriggerFlashOnce(imagePath,durationMs,playSound,suppressHaptic)` has NO `amount`/`size`. WPF = N images at Size% for D seconds. Clamp Amount 0-8, Duration 0-10s, Size 0-150%. → **Add seam member** (DIM or overload) `TriggerAiFlash(int amount, int durationMs, int sizePct)`.
- **bubbles** → Core `IBubbleService.Start()` has NO frequency override. WPF `Start(true, freqOverride)`. Frequency clamp 0-10. → **Add** `Start(int? frequencyOverride)` (or a spawn-rate setter).
- **subliminal** → Core `FlashSubliminalCustom(text, overrideDurationMs, suppressHaptic)` — 2nd arg is DURATION not OPACITY. WPF opacity 0-60. → **Add** opacity param/overload.
- **haptic** → Core `IHapticsService` has `SetSyncPatternAsync(float[],ms)`/`TestAsync(intensityPct,ms)`/`StopAsync()` but NO `ApplyVibrationModeAsync(intensity,ms,VibrationMode.Pulse)`. → **Add** a pulse method OR synthesize a pulse `float[]` for `SetSyncPatternAsync`. Intensity clamp 0..`MaxAiHapticIntensity`.
- **audio** → Core `IAudioPlayer.PlayAsync(path,ct)` + `SetVolume(double)`, NO `PlaySound(path,vol)`. → **Adapter**: `SetVolume(1.0); _=PlayAsync(path)`. Exts .mp3/.wav/.wma/.ogg/.flac/.aac/.m4a; random scans assets/audio.

## Location + threading

- **Impl lives in `CCP.Avalonia`** (NOT pure Core): needs `Dispatcher.UIThread` (Avalonia) for UI marshal + the real Avalonia effect-service impls. `IAiCommandService` (Core) is the seam; concrete `AiCommandService` + executors go in `CCP.Avalonia/Services/Commands/`.
- Every UI-touching executor marshals via `Dispatcher.UIThread.Invoke(...)`. `ExecuteCommand` is `async void` fire-and-forget (try/catch inside). `getbacktome` uses `await Task.Delay` off-thread + `Dispatcher.UIThread.Post` for avatar speech.
- Inject into the concrete: `IFlashService, IBubbleService, ISubliminalService, IOverlayService, IVideoService, IAudioPlayer, IHapticsService, IBouncingTextService, ILockCardService, IAiService, IAvatarWindowService?, ISettingsService, ILogger`.

## Enrichment wiring (provider-side, the `[CONTEXT BLOCK]`)

- **OpenAiService** (`BuildMessages`, TODO at `OpenAiService.cs:316`): when `AllowAiToControlEffects && _commands != null`, `messages.Insert(1, _promptService.BuildEnrichmentMessage(factsJson, now))`. Inject `IPromptService?` + a facts/knowledge source.
- **LocalAiService**: currently has NO `IAiCommandService`/`IPromptService` injection at all. Add them; manage the enrichment at `_messages` index 1 (replace-in-place if present, else insert; remove when effects disabled). Also: the provider MUST clear/not-dispatch commands on an output-moderation block (WPF `LocalAiService.cs:564` clears `_currentCommands`).
- Gate: enrichment + dispatch BOTH gated on `AllowAiToControlEffects`.

## Commit plan

- **Phase 1 — seam extensions** (Core interfaces, DIMs/overloads): `IFlashService.TriggerAiFlash`, `IBubbleService.Start(freq?)`, `ISubliminalService` opacity overload, `IHapticsService` pulse method. + Avalonia impls. (audio = inline adapter, no seam change.) One commit.
- **Phase 2 — Core dispatcher skeleton** is unnecessary; impl is Avalonia-side. Write `CCP.Avalonia/Services/Commands/AiCommandService.cs` (BeginBatch/ExecuteCommand/CancelAllCommands + the 11 executors inline or as classes). One commit.
- **Phase 3 — register + enrichment wiring**: register `IAiCommandService` in `ServiceCollectionExtensions`; wire `[CONTEXT BLOCK]` into OpenAiService + LocalAiService (inject `IPromptService`/`IAiCommandService`/facts). One commit.
- Each commit: slnf 0 / WPF 0 / Core 542 / smoke baseline. Adversarial review before commit (state-mutating — AI triggers effects).

## Verification (per goal gates + security)

- All 4 gates green per commit.
- Security: master+per-effect gates faithful; per-response cap 3; media path-traversal defense (`..` rejection + assets-root + File.Exists + random fallback); haptic intensity clamped to `MaxAiHapticIntensity`; moderation gate before dispatch (output-block → no dispatch). AI-controlled effects is security-relevant → adversarial review required.
- Honest tracking: task-board DONE row when truly complete; do NOT claim parity until the 5 seam gaps are resolved (not degraded).
