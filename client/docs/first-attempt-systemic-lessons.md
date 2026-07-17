# First-attempt systemic lessons

This complements `first-attempt-lessons.md`. That file records feature-level outcomes; this file records cross-cutting failure patterns found in first-attempt code and git history that can cause an otherwise good greenfield port to drift.

## Evidence method

- Code establishes what the first attempt actually did.
- Git history establishes which apparently complete areas later required correction, re-opening, deletion, or rewiring.
- Commit subjects are leads, not proof. Any mechanism adopted by the greenfield client still requires current source and runtime evidence.
- These lessons do not authorize copying first-attempt architecture.

## P0 lessons

### Capability must be an observed result, not an OS or registration guess

- **Evidence:** `CCP.Avalonia/Platform/AvaloniaPlatformCapabilities.cs:24-65` reports overlays and screen capture from broad desktop/OS assumptions and identifies several capabilities by checking whether DI returned a fallback type. `CCP.Avalonia.Desktop.Linux/Platform/WebKitGtkBrowserHost.cs:9-49` is registered as a Linux browser host but opens an external browser, returns no embedded control, and has inert script/events. `LinuxOverlaySurface.cs:9-78` deliberately converts backend faults into logged no-ops.
- **History:** `57cbfc81` later documented invisible-window desktop-lockout and a Wayland backend claiming per-region behavior while stubbing it; `a5c4efe6` corrected backend-selection and fabricated-API assumptions. `2d10209c` copied DTRH assets to Linux although a working host was still absent.
- **Disposition:** `REJECT` capability-by-platform, capability-by-registration, and capability-by-assets-present. `ADAPT` graceful fallback only for explicitly optional behavior.
- **Greenfield consequence:** Capabilities use a typed state such as available, unavailable with reason, degraded with named semantics, permission required, dependency missing, or faulted. A promised feature is enabled only after the selected backend passes a runtime probe. A no-op may keep the app alive but cannot report success or support.

### Startup order and hidden globals became architecture

- **Evidence:** `CCP.Avalonia/App.axaml.cs:45-220` builds a global service provider, invokes order-sensitive head overrides, wires static secret stores and `CoreApp.Services`, runs migrations, initializes localization/mod/theme/Chaos state, starts trigger wiring, and blocks on background initialization before creating UI. `CCP.Avalonia/ServiceCollectionExtensions.cs:96-220` registers broad fallbacks and many optional dependencies. UI and services contain hundreds of `App.Services.GetService` calls.
- **History:** `e9501ce8` restored secret persistence, missing migrations, and exit flush; `92329231` added shutdown disposal and crash-dialog guards; `ad3dc3c5` and `8213b1d2` wired orphaned dialogs and reactions; ProfileSync landed in multiple explicitly `unwired` commits before `80e14429` supplied live wiring.
- **Disposition:** `REJECT` global service location, static bridge wiring, constructor side effects, and “registered means integrated.”
- **Greenfield consequence:** Define a small explicit startup state machine with ordered, cancellable phases and typed failures. Composition-root validation must prove required services, platform overrides, and startup participants before the main window appears. Constructors remain cheap. Each background participant has one owner and a matching shutdown path.

### Persistence is a transaction and schema, not a call to serialize

- **Evidence:** `CCP.Core/Services/Settings/SettingsService.cs:70-190` contains recovery, partial-member parsing, corruption quarantine, and multiple migrations because malformed or renamed members previously risked resetting user data. `:374-444` uses debounced temp-file replacement, background cloud backup, whole-object replacement, and a `CurrentReplaced` event. Many views subscribe directly to the current settings object, making replacement/rebinding part of correctness.
- **History:** `b694b543` added corrupt-settings quarantine and first file-I/O tests; `a2d1b9a8` aligned cross-head formats; `e9501ce8` restored migrations/exit flush; `03d91c86` chained telemetry writes to remove a write-order race; `750d2615` added an atomic progression writer; `f403261d`/`eeef31e2` show stale version state causing a popup every launch.
- **Disposition:** `ACCEPT` atomic replacement, corruption preservation, explicit migration, and crash recovery. `REJECT` scattered saves, detached unordered writes, and silent defaults over unreadable user data.
- **Greenfield consequence:** Before feature models grow, define one persistence contract: schema version, migration journal, unknown/member-error policy, atomic write and flush semantics, corruption recovery, backup/import boundaries, secret exclusion, concurrent writer ownership, replacement notification, and deterministic tests with failure injection.

### Lifecycle completion must be owned and awaitable

- **Evidence:** The first attempt contains widespread detached `Task.Run`, `async void`, blanket catches, timers, event subscriptions, and best-effort disposal. `App.axaml.cs` marks UI exceptions handled and continues. Native media needed special deferred teardown drains. Numerous singleton services implement disposal, but disposal depends on the composition root reaching them correctly.
- **History:** `db4ec5d0` drained deferred video teardown to stop exit segfaults; `9aab5206` moved LibVLC stop off the UI thread after a native access violation; `f4a556a2` released an interaction slot on abnormal teardown; `8f4db7ca` broke an audio-ducking crash-recovery boot loop; `b828fea5`, `09d13f55`, `eaa33975`, and `0f338f87` fixed timer, consumer, view-model, and bubble leaks; `4d65e564` and `410bef87` repeatedly re-closed a session interaction race.
- **Disposition:** `REJECT` unowned detached work, timer-as-lifecycle, broad continue-after-fault, and best-effort cleanup for stateful/native resources.
- **Greenfield consequence:** Every long operation has an owner, cancellation generation, completion task, and terminal outcome. Start/stop/dispose are idempotent and ordered. Panic, normal close, initialization failure, device/display loss, and process shutdown each have testable teardown. Native-resource operations are serialized where required and tested in Release, not only Debug.

### UI-thread ownership must be explicit

- **Evidence:** Services, callbacks, and event handlers cross UI/native/background threads while updating bound state. The first attempt frequently catches failures near callbacks instead of expressing dispatch ownership.
- **History:** `40a4b7c1` fixed a haptics status-brush cross-thread crash; `5958038f` fixed off-thread bound-property mutation; `09d13f55` fixed a webcam start race; `22798e8e` corrected compositor lifecycle and physical-pixel/input behavior.
- **Disposition:** `ADAPT` asynchronous work, but reject implicit callback-thread assumptions.
- **Greenfield consequence:** Each event/stream documents its delivery context. Domain state changes off-thread; UI projection occurs through one deliberate dispatch boundary. Tests inject out-of-order completion, cancellation, and background-thread callbacks.

## P1 lessons

### Asset presence, lookup, and packaging need one manifest

- **Evidence:** `CCP.Avalonia/CCP.Avalonia.csproj:12-91` manually links many resource families while desktop heads separately copy runtime content. `AvaloniaModResourceResolver.cs:10-180` supports embedded assets, files, legacy pack URIs, mod overrides, extension fallback, and several normalization paths. Localization loads output files from a fixed directory (`CCP.Core/Localization/LocalizationManager.cs:119-174`).
- **History:** `0c43277f` bundled a missing default spiral; `2d10209c` added web assets to Linux/macOS heads; `abe37ceb` removed stale duplicate localization trees; `3be5c732` fixed localization and folder status; `c32b616c` later fixed recursive media discovery.
- **Disposition:** `REJECT` per-feature path logic and duplicated csproj copy lists. `ACCEPT` mod override semantics only after their trust boundary is specified.
- **Greenfield consequence:** Create one typed asset catalogue/manifest that declares logical ID, source class, case-sensitive relative path, packaging mode, optionality, mod override policy, license/provenance, and target heads. Build/publish tests verify every required asset from packaged output on Windows and Linux, including case sensitivity.

### Error swallowing hid product failure

- **Evidence:** Repository searches find hundreds of broad catches, empty catches, fallback values, and fire-and-forget paths across the Avalonia head. Some are correct teardown guards, but many convert required operations into apparently successful no-ops. The global UI exception handler in `App.axaml.cs:70-94` marks every UI exception handled.
- **History:** `8c6b12c0` found companion reactive dialogue silently dead; `e140e0ff` found a placeholder screen; `ad3dc3c5` found orphaned dialogs; `fb0dfd1c` corrected a task target that was only a stub; `e3853b49` cleared stale WIP/status documents.
- **Disposition:** `REJECT` catch-and-continue without a typed outcome and user consequence.
- **Greenfield consequence:** Classify failures as recoverable/degraded/fatal/cancelled. Required operations surface a typed result and bounded user-visible state. Logs are diagnostic, not the only failure channel. Global handlers log and initiate controlled shutdown or a narrowly proven recovery; they do not bless arbitrary continued execution.

### Release and packaged output are separate products to verify

- **Evidence:** Native packages, content-copy rules, and debug-only diagnostics differ across heads and configurations. The smoke harness was Debug-only while native teardown failures occurred in Release.
- **History:** `094db1a2`, `9de6c109`, `d3b5b824`, and `9aab5206` trace an intermittent Release native crash that ordinary debug/smoke evidence did not settle. Version-alignment commits repeatedly fixed permanent update prompts (`1a628562`, `6014fef9`, `eeef31e2`).
- **Disposition:** `REJECT` Debug build success as release readiness.
- **Greenfield consequence:** Every milestone verifies Debug development behavior, Release execution, and published artifact startup/resource/native dependency behavior. Version derives from one source and is asserted across assemblies, update metadata, and packaging.

### “Unwired but verified” is not a shippable intermediate state

- **Evidence:** Large service surfaces and interfaces can pass unit tests while no user path resolves or invokes them.
- **History:** ProfileSync commits `c4b2583a`, `a3215fc9`, `fafd22b0`, `34fc5f16`, `44ea16fa`, and `4f051ab0` explicitly landed foundations/actions as `unwired`; `80e14429` later added live wiring. Other corrective commits wired 13 orphaned reactions and several orphaned dialogs.
- **Disposition:** `REJECT` marking a capability done from isolated code/tests. `ADAPT` unwired foundations only as explicitly non-product architecture tasks.
- **Greenfield consequence:** Every product slice includes a composition-root path and one end-to-end invocation test. Unwired code is labelled infrastructure, cannot satisfy a capability row, and should not be added far ahead of its first consumer.

### Git history is part of archaeology

- **Evidence:** Commit sequences reveal repeated re-open/re-close cycles, stale tracker corrections, deleted dead paths, and later fixes to areas previously described as complete.
- **Disposition:** `ACCEPT` history as a lesson source, not current truth.
- **Greenfield consequence:** Before porting a substantial feature, inspect focused history for its WPF and first-attempt paths. Look for later `fix`, `revert`, `re-open`, `leak`, `race`, `crash`, `unwired`, and deletion commits. Cite decisive commits in task research, then verify against the final code.

## Required foundation gates for attempt two

Before broad feature implementation, the greenfield bootstrap should establish:

1. startup/shutdown phase ownership and composition-root validation;
2. typed runtime capabilities with reasons and probes;
3. persistence/schema/migration/atomic-write contract;
4. async operation and lifecycle ownership rules;
5. asset/mod/localization catalogue and packaged-output validation;
6. Debug, Release, and publish verification lanes;
7. an end-to-end rule that prevents unwired code from closing capability rows;
8. focused git-history archaeology in every non-trivial feature task.

These gates should be implemented only as needed by the first vertical slices. Do not build a generic framework before a concrete consumer exists.