# SP-071 census — every blocking wait in client/src/**

Grep: `\.Wait\(|\.Result\b|\.GetAwaiter\(\)\.GetResult\(\)|\.Join\(` over `client/src/**/*.cs` (45 raw
hits; `string.Join` false positives removed; `lock (_initLock)` in `SoundArbitration.Dispose` added by
hand — it is the blocking wait the regex cannot see and the subject of this packet). 2026-08-14.

Verdicts: **FIXED-HERE** / **SEPARATE-PACKET** (named in PROMPT) / **BOUNDED-OK** /
**NON-BLOCKING** (`.Result` on a completed task) / **EXIT-PATH** (process dying; hang survivable) /
**FOLLOW-UP** (unbounded, in-process, UI-reachable — orchestrator files rows, not this packet).

| # | Site | Reaching thread(s) | Bounded? | Waits on | If it never returns | Verdict |
|---|------|--------------------|----------|----------|--------------------|---------|
| 1 | `Audio/SoundArbitration.cs:1087-1091` `lock (_initLock)` in `Dispose` | UI thread (DtrhHostWindow close handler :153 → `TeardownBarkPipeline` :258); any caller of `Dispose` | **NO** | an in-flight native device init (probe holds `_initLock` across `TryInit`) | the whole dispatcher stops; non-modal child window, so the process lives on wedged | **FIXED-HERE** |
| 2 | `Audio/SoundFlowAudioBackend.cs:108` → `Audio/AudioSeams.cs:150` `OffSyncContext.Run` (`Task.Run(work).GetAwaiter().GetResult()`) | UI thread via the play seam (CreatePlayer from PlayVoice/PlayWhisper/PlaySfx) | **NO** | native `AssetDataProvider` ctor (file/metadata read) | dispatcher blocked mid-session | **SEPARATE-PACKET** — changes a synchronous seam contract; a late completion adds itself to `MasterMixer` (orphan = ghost play + leak; disposal races device teardown) |
| 3 | `Features/Dtrh/SoundFlowDtrhAudio.cs:100` (`Task.Run(CreatePlayerCore).GetAwaiter().GetResult()`) | UI thread via DTRH effects play | **NO** | native `AssetDataProvider` ctor | dispatcher blocked mid-session | **SEPARATE-PACKET** — same row as #2 |
| 4 | `Features/Dtrh/DtrhHostWindow.axaml.cs:228` `_barkStore.StartAsync(...).GetAwaiter().GetResult()` | UI thread (host open / bark pipeline init) | no | in-process store start (disk load) | host open stalls the dispatcher | FOLLOW-UP (disk rarely wedges permanently) |
| 5 | `Features/Dtrh/DtrhHostWindow.axaml.cs:257,259,260` `.Wait(TimeSpan.FromSeconds(2))` flush/stop | UI thread (close handler) | YES (2s) | in-process store flush/stop | returns after 2s, best-effort | **BOUNDED-OK** — the local precedent for this packet's budget |
| 6 | `Features/Dtrh/DtrhHostWindow.axaml.cs:1198` `t.Result` | threadpool continuation (`ContinueWith`, `TaskScheduler.Default`, after `IsFaulted` check) | n/a | nothing — task already complete | n/a | **NON-BLOCKING** |
| 7 | `Features/Dtrh/DtrhLoomWindow.axaml.cs:344,348` `st.Result` | threadpool continuation (same shape as #6) | n/a | nothing — task already complete | n/a | **NON-BLOCKING** |
| 8 | `Features/Dtrh/DtrhSaveSlots.cs:467,469` `StopAsync()/StartAsync().GetAwaiter().GetResult()` | UI thread (slot delete in the picker) | no | in-process store stop/start (disk) | picker action stalls the dispatcher | FOLLOW-UP |
| 9 | `Features/Intake/IntakeHostContext.cs:84,95` `StartAsync(...).GetAwaiter().GetResult()` | UI thread (intake host create) | no | in-process store start (disk) | intake open stalls the dispatcher | FOLLOW-UP |
| 10 | `Features/Intake/IntakeHostContext.cs:126-130` `.Wait(TimeSpan.FromSeconds(2..3))` flush/stop | Dispose path (host teardown) | YES (2-3s) | in-process store flush/stop | returns after budget, best-effort | **BOUNDED-OK** |
| 11 | `Persistence/AssetSelectionDocument.cs:61` `store.StartAsync(...).GetAwaiter().GetResult()` | UI thread (via IntakeHostContext.Create / DTRH host open) | no | in-process store start (disk) | opener stalls the dispatcher | FOLLOW-UP |
| 12 | `Persistence/PersistenceStore.cs:282` `tail.Result` | async teardown path, after `Task.WhenAny(tail, Task.Delay(boundedWait))` already returned `tail` | n/a | nothing — `tail` provably complete | n/a | **NON-BLOCKING** (bounded upstream) |
| 13 | `Persistence/SecretStores.cs:145,158,170` `Run(...).GetAwaiter().GetResult()` | caller of the secret store (AI settings/pipeline paths) | YES (~5s — `Run` carries a 5s CTS, :269-279) | external `secret-tool` subprocess (D-Bus) | typed unavailable after 5s | **BOUNDED-OK** |
| 14 | `Ai/AiMemoryStore.cs:272` `_store.SaveImmediate().GetAwaiter().GetResult()` | UI thread (`CompanionViewModel.cs:315` → `Memory.Clear()`, privacy op), under `_gate` | no | in-process chained writer (disk save) | privacy clear stalls the dispatcher while holding the store gate | FOLLOW-UP |
| 15 | `App.axaml.cs:92` `_host.ShutdownAsync().GetAwaiter().GetResult()` | UI thread, `desktop.Exit` event | no | app-wide async drain | shutdown hang at process exit | **EXIT-PATH** (process is dying; user kill is the remedy upstream too) |
| 16 | `Program.cs:157` startup `RunAsync(...).GetAwaiter().GetResult()` | main thread, before the lifetime exists | no | startup phases | startup stalls before any UI | **EXIT-PATH** (no dispatcher to wedge yet) |
| 17 | `Program.cs:165,168` `ShutdownAsync().GetAwaiter().GetResult()` | main thread, startup failure/cancel | no | partial teardown drain | exit-path hang | **EXIT-PATH** |
| 18 | `Program.cs:263` `host!.ShutdownAsync().GetAwaiter().GetResult()` | main thread, panic path (contract §6) | no | teardown drain | panic-exit hang | **EXIT-PATH** |

**Named separate packet (PROMPT decree):** sites 2+3 — one row, central acceptance = orphan disposal.
**This packet closes site 1 only.** Sites 4/8/9/11/14 are the same *shape* on disk-backed in-process
stores — named here as follow-up candidates, deliberately unfixed (out of File Scope).
