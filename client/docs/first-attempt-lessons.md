# First-attempt lessons

This document records verified lessons from the first Avalonia attempt without inheriting its implementation or completion claims.

## Method

For each lesson, record:

- the problem or successful pattern;
- evidence in code, history, tests, measurements, or current official documentation;
- whether the new client accepts, adapts, or rejects it;
- the concrete design consequence, if any.

## Accepted lessons

### Unified composition for real-time visuals

- **Lesson or claim:** `VERIFIED` The first attempt's successful direction was to compose passive and real-time visuals together in a shared, ordered render surface rather than giving each effect its own window and bitmap presentation path. The product owner identifies this as a success to retain.
- **Exact evidence:**
	- The first attempt routes flash, tint, spiral, and brain-drain visuals into registered layers rather than effect-owned windows: `ConditioningControlPanel/CCP.Avalonia/Services/Flash/AvaloniaFlashService.cs:98-104` and `ConditioningControlPanel/CCP.Avalonia/Services/Overlays/AvaloniaOverlayService.cs:80-93`.
	- Video and mandatory video use the same layer registry: `ConditioningControlPanel/CCP.Avalonia/Services/Video/AvaloniaVideoService.cs:192-220`.
	- A single control delegates all active layers to one custom draw operation and shared canvas: `ConditioningControlPanel/CCP.Avalonia/Compositor/CompositorControl.cs:20-82`.
	- Layer order is explicit and deterministic rather than dependent on effect-window activation order: `ConditioningControlPanel/CCP.Avalonia/Compositor/CompositorLayers.cs:8-45`.
	- The engine owns the update and repaint decision, including skipping repaint when active content is unchanged: `ConditioningControlPanel/CCP.Avalonia/Compositor/CompositorEngine.cs:540-598`.
	- In contrast, WPF creates effect windows and bitmap surfaces per monitor. Its dual-monitor video path creates one `WriteableBitmap` and one fullscreen window for each screen, then copies decoded frames into those surfaces: `ConditioningControlPanel/Services/Video/DualMonitorVideoService.cs:25-36`, `:350-401`, and `:487-510`. Spiral likewise creates windows per selected screen: `ConditioningControlPanel/Services/Notifications/OverlayService.cs:1150-1220`.
- **Current Avalonia v12 verification:** `VERIFIED` Avalonia 12.1 still exposes `ICustomDrawOperation`, `Visual.InvalidateVisual()`, and `ISkiaSharpApiLeaseFeature`. The official API reference says invalidation queues a repaint and identifies the Skia lease API as **unstable**. Avalonia issue [#12247](https://github.com/AvaloniaUI/Avalonia/issues/12247) confirms that custom rendering must be invalidated when its visual content changes. The first attempt currently references Avalonia 12.1.0 in `ConditioningControlPanel/CCP.Avalonia/CCP.Avalonia.csproj:104-108`. Sources: [ICustomDrawOperation](https://api-docs.avaloniaui.net/docs/T_Avalonia_Rendering_SceneGraph_ICustomDrawOperation), [InvalidateVisual](https://api-docs.avaloniaui.net/docs/M_Avalonia_Visual_InvalidateVisual), [ISkiaSharpApiLeaseFeature](https://api-docs.avaloniaui.net/docs/T_Avalonia_Skia_ISkiaSharpApiLeaseFeature), and [Avalonia 12.1.0 release](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.0).
- **Disposition:** `ACCEPT` the unified-composition principle. Do not copy the first attempt's classes, timer cadence, layer interface, Skia lease integration, z constants, or window lifecycle by default.
- **Consequence for the new client:** Real-time conditioning visuals that need to overlap should share a composition domain with explicit ordering and coordinated presentation. A separate native window or render pipeline must be justified by observable interaction, capture, platform, or isolation requirements. The exact Avalonia v12 rendering primitive remains an implementation-time decision because the currently demonstrated Skia lease API is unstable.

### Preserve the DTRH web game, not its Windows-only host

- **Lesson or claim:** `VERIFIED` The useful product in the first attempt is the hosted DTRH web game and the portable separation between page protocol and native obligations. The Windows-only WebView2 host is not itself the feature and is not a cross-platform solution.
- **Exact evidence:**
	- The same bundled payload is copied to desktop outputs, and the first attempt's Windows head hosts it from `https://ccp.game/dtrh/index.html`: `ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/Services/Chaos/DtrhGameHostService.cs:36-47` and `:154-166`.
	- The native responsibilities were extracted into a portable message orchestrator rather than moved into the page: `ConditioningControlPanel/CCP.Core/Services/Chaos/DtrhHostOrchestrator.cs:16-27` and `:119-137`.
	- The actual Windows browser service remains tied to `WebView2BrowserHost`, Windows virtual-host mappings, and Windows browser arguments: `ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/Services/Chaos/DtrhGameHostService.cs:20-27`, `:139-166`, and `:229-232`.
	- The Linux head has no `IChaosWebGameService` implementation. Shipping payload files in its output therefore did not make DTRH functional on Linux.
	- The first-attempt launch view model presents a not-available message when no browser service exists: `ConditioningControlPanel/CCP.Avalonia/ViewModels/Tabs/LabTabViewModel.cs:463-481`.
- **Current Avalonia v12 verification:** `VERIFIED` Official `Avalonia.Controls.WebView 12.0.1` now documents `NativeWebView` on Linux through WPE WebKit, including X11 and Wayland, with WebKitGTK/`NativeWebDialog` fallback options. It also documents `InvokeScript`, `WebMessageReceived`, environment configuration, and native platform handles. The portable page-to-host entry point is `invokeCSharpAction(body)`, while DTRH's current `bridge.js` uses `window.chrome.webview`; portable unchanged-payload compatibility is therefore not established. Sources: [NativeWebView](https://docs.avaloniaui.net/controls/web/nativewebview), [embedding web content](https://docs.avaloniaui.net/docs/app-development/embedding-web-content), [environment options](https://docs.avaloniaui.net/controls/web/webview-environment), [NuGet 12.0.1](https://www.nuget.org/packages/Avalonia.Controls.WebView/12.0.1), and the [official repository](https://github.com/AvaloniaUI/Avalonia.Controls.WebView).
- **Disposition:** `ADAPT`. Copy the game payload and preserve protocol behavior. Reject the old classic game, the WebView2-specific host abstraction, Windows-only availability, and any claim that copied output files equal support. Permit a minimal bridge-only payload edit if the cross-platform spike proves it necessary; do not rewrite the game.
- **Consequence for the new client:** DTRH receives a dedicated interactive window and a small host around the existing page. Package admission, resource origin, bridge compatibility, WebGL/media capability, and performance require runtime evidence on Windows, Linux X11, and Linux Wayland before implementation is approved. Boot failure closes with an actionable error rather than invoking a native fallback.

### Keep shared native video composition, reject browser capture mirroring

- **Lesson or claim:** `VERIFIED` The first attempt's useful video result is one decoded frame rendered through the shared Skia layer stack. Its fullscreen-browser capture mirror is a separate, weaker path that the owner reports behaved badly.
- **Exact evidence:**
	- `VideoLayer` accepts local paths or network locations, uses one LibVLC player, draws an opaque background, and aspect-fits the frame within each monitor: `ConditioningControlPanel/CCP.Avalonia/Compositor/Layers/VideoLayer.cs:281-333` and `:629-667`.
	- The established session ordering puts regular video at 10, mandatory video at 15, spiral at 60, and color tint at 70: `ConditioningControlPanel/CCP.Avalonia/Compositor/CompositorLayers.cs:9-44`.
	- The first attempt records a successful visual verification with video, spiral, and color tint active together: `ConditioningControlPanel/docs/unified-compositor-engine-plan.md:59-69`.
	- The browser mirror instead captures one screen, skips that source monitor, and non-uniformly stretches the captured frame across every target: `ConditioningControlPanel/CCP.Avalonia/Compositor/Layers/BrowserMirrorVideoLayer.cs:13-40` and `:375-414`.
	- The mirror silently does nothing on a head without a frame source: `ConditioningControlPanel/CCP.Avalonia/Services/Video/BrowserMirrorVideoService.cs:60-69`. That cannot satisfy Windows/Linux support.
	- WPF proves direct network locations can enter native LibVLC playback, but it starts separate players per monitor and may omit secondary displays: `ConditioningControlPanel/Services/Video/VideoService.cs:1152-1206`. Preserve the user-visible native presentation idea, not the per-monitor decoder implementation.
- **Current Avalonia v12 verification:** Avalonia exposes `ScreenOrientation` values for landscape, portrait, and their flipped variants, but consistent reporting and whether any backend needs an explicit content transform remain runtime questions. Monitor target geometry must use each screen's physical bounds and scaling; applying an extra rotation without evidence risks double-rotating a desktop the OS already transformed. Source: [ScreenOrientation](https://api-docs.avaloniaui.net/docs/T_Avalonia_Platform_ScreenOrientation).
- **Disposition:** `ADAPT` the native shared-frame UCE path and its layer ordering. `REJECT` fullscreen browser presentation, screen-capture mirroring, non-uniform stretch, independent per-monitor decoders, and silent no-op degradation.
- **Consequence for the new client:** Every supported fullscreen video source uses one native decode/timeline and is aspect-fitted independently on every monitor with black bars beneath spiral and bounded tint. Online-source extraction is a separate gated capability; unsupported `blob:`, MSE, authenticated, or DRM playback reports a limitation instead of falling back to browser rendering.

### UI code presence is not interaction parity

- **Lesson or claim:** `VERIFIED` The first attempt contains a popup `ScrollViewer` and routed card toggle events, but the owner reports that popup scrolling and some quick toggles do not work. Declared controls and handlers are not acceptance evidence.
- **Exact evidence:**
	- WPF's popup is a bounded modeless owned window with a vertical `ScrollViewer`: `ConditioningControlPanel/Features/FeaturePopupWindow.xaml` and `MainWindow/MainWindow.Presets.cs:848-875`.
	- The first-attempt popup manually measures content, catches all sizing failures, and caps against `Screens.Primary` rather than the owner's monitor: `ConditioningControlPanel/CCP.Avalonia/Features/FeaturePopupWindow.axaml.cs:69-113`.
	- WPF quick-toggle dispatches by actual card identity and starts/stops the running service: `ConditioningControlPanel/MainWindow/MainWindow.Presets.cs:777-825`.
	- The first attempt dispatches by localized `card.Title`, and `CanTest` makes plain right-click open a context menu instead of toggling: `ConditioningControlPanel/CCP.Avalonia/Views/Tabs/SettingsTabView.axaml.cs:369-456` and `ConditioningControlPanel/CCP.Avalonia/Features/FeatureCard.axaml.cs:368-410`.
- **Current Avalonia v12 verification:** Official documentation requires a bounded `ScrollViewer`; placing it in an infinite-height layout prevents correct scrolling. `ScrollViewer` supports wheel/touch input, scrollbars, focus bring-into-view, and scroll chaining when correctly bounded. Source: [Avalonia ScrollViewer](https://docs.avaloniaui.net/controls/layout/containers/scrollviewer).
- **Disposition:** `REJECT` implementation-by-markup and localized-title dispatch. `ADAPT` WPF's interaction outcomes using bounded layout and stable feature commands.
- **Consequence for the new client:** Popup scrolling and every card gesture receive headed Windows/Linux acceptance. Automated checks verify stable identity and service side effects; visual rendering alone cannot close the work item.

### Window ports need behavioral classification

- **Lesson or claim:** `VERIFIED` WPF intentionally uses different ownership, activation, taskbar, topmost, resize, and lifetime policies across its windows. The owner reports broad first-attempt window drift, so property-by-property behavior must be inventoried rather than inferred from similar styling.
- **Exact evidence:** WPF examples include the owned modeless non-taskbar feature popup, non-activating notification popups, activating topmost Bubble Count, resizable editors, modal owner-centered dialogs, passive overlays, and the conditionally topmost non-taskbar AvatarTube. Evidence is distributed across `ConditioningControlPanel/Features/FeaturePopupWindow.xaml`, `Windows/**/*.xaml`, `Dialogs/**/*.xaml`, `AvatarTube/AvatarTubeWindow.xaml`, and their show call sites.
- **Current Avalonia v12 verification:** Avalonia independently exposes owner, `Show` versus owner-required `ShowDialog`, activation, taskbar, topmost, resize, decorations, position, and closing behavior. A framework limitation does not require collapsing them into one policy. Source: [Avalonia Window](https://docs.avaloniaui.net/controls/primitives/window).
- **Disposition:** `REJECT` blanket window styling/lifecycle. `ADAPT` each observable WPF window contract through a reviewed manifest.
- **Consequence for the new client:** Shared chrome follows semantic inventory and cannot alter owner/focus/lifetime behavior. Both target platforms receive headed window-lifecycle verification.

### Avatar animation needs visible-frame proof

- **Lesson or claim:** `VERIFIED` The first attempt has extensive animation code, but the owner reports that AvatarTube animations do not work at all. Timers, decoder objects, events, and invalidation calls are therefore untrusted completion evidence.
- **Exact evidence:**
	- WPF starts the initial static pose timer when appropriate and loads animated GIFs before later animation modes take over: `ConditioningControlPanel/AvatarTube/AvatarTubeWindow.xaml.cs:152-185`.
	- WPF animated avatars auto-start and loop, while static poses visibly fade: `ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Avatar.cs:292-383` and `:1355-1375`.
	- The first-attempt constructor creates `_poseTimer` but does not start it for the initial static set; its initial animated branch also does not directly call `LoadAnimatedAvatar`: `ConditioningControlPanel/CCP.Avalonia/AvatarTube/AvatarTubeWindow.axaml.cs:309-367`. Later mode/set paths may start them, which does not repair initial liveness.
	- The first attempt contains a custom GIF player, emote engine, crossfade timers, and a 16 ms float timer: `ConditioningControlPanel/CCP.Avalonia/AvatarTube/AvatarTubeWindow.Avatar.cs`, `CirceEmoteEngine.cs`, and `AvatarTubeWindow.Windowing.cs`. Their existence does not contradict the observed failure.
- **Disposition:** `REJECT` code-level completion claims. `ADAPT` the WPF visual outcomes with rendered-frame liveness probes and deterministic single-pipeline lifecycle.
- **Consequence for the new client:** AvatarTube is not complete until static, GIF, emote, speech, float, pause/resume, mod-switch, and attach/detach behavior visibly animate on Windows and Linux without leaks or duplicate pipelines.

### Whole-app smoke and layer sweeps were slow and visually ineffective

- **Lesson or claim:** `VERIFIED` The owner reports that the first attempt's smoke and layer tests took too long, did not reliably catch visual defects, and still allowed obvious issues such as a strange border around bubbles. Opening many tabs/windows and checking that code paths ran created false confidence without inspecting the pixels that mattered.
- **Exact evidence:**
	- The first-attempt smoke runner conditionally captures many tabs, themes, dialogs, windows, helpers, and feature popups in one long traversal: `ConditioningControlPanel/tests/CCP.Avalonia.Desktop.Windows.Smoke/SmokeTestRunner.cs:143-307`, `:559-570`, and `:852-898`.
	- Its screenshot helper renders a whole `Window` with `RenderTargetBitmap`: `SmokeTestRunner.cs:1076-1094`. This is useful as a low-level targeted capture technique, but does not make the broad traversal effective visual review.
	- Layer verification primarily establishes activation/render deltas and invariants; code-path/layer activity cannot identify an unintended bubble outline, clipping, theme leakage, or malformed geometry without pixel inspection.
- **Disposition:** `REJECT` routine whole-app smoke/layer/screenshot sweeps as the default verification loop. `ADAPT` only the smallest targetable capture primitives and cheap focused automated checks.
- **Consequence for the new client:** Use a three-level policy: fast affected tests on every iteration with no app launch by default; targeted K3 screenshot review and headed behavior only at the visual slice's close gate or when appearance is suspicious; broad theme/language/platform matrices only at named UI milestones and releases. Visual checks must inspect the changed state directly, including defect-specific geometry such as bubble edge/border artifacts.

### Audio ownership and completion were too weak

- **Lesson or claim:** `VERIFIED` The owner reports sound/quips were done badly. The first attempt's generic `AvaloniaAudioPlayer` stops the current media before every play and returns `Task.CompletedTask` immediately, so unrelated voice/phrase/test requests replace each other without truthful completion. `AvatarBarkSpeaker` queues ordinary bark text without its resolved voice asset, and bubble timers approximate audio completion.
- **Exact evidence:** `ConditioningControlPanel/CCP.Avalonia/Platform/AvaloniaAudioPlayer.cs:44-76`; `CCP.Avalonia/Services/Bark/AvatarBarkSpeaker.cs:95-131`; `CCP.Avalonia/AvatarTube/AvatarTubeWindow.axaml.cs` speech/audio paths. `AvaloniaSfxPlayer.cs:41-141` permits overlap but uses unbounded detached work/polling and no admission/completion result.
- **Disposition:** `REJECT` one generic replace-on-play player, text-only queued bark delivery, bubble-timer completion, silent best-effort device routing, and unbounded player-per-cue work. `ADAPT` separate voice/SFX/whisper/media concepts into explicit channel ownership with real lifecycle outcomes.
- **Consequence:** The greenfield audio contract and A-009 govern backend selection and quip delivery. Required cues need observable start, ordinary/priority voice arbitration must be deterministic, and mute keeps text visible.

### AI provider and effect boundaries were not explicit enough

- **Lesson or claim:** `VERIFIED` First-attempt AI code improved typed fallback/refusal and moderation, but still lacked complete typed infrastructure outcomes and end-to-end cancellation. Provider switching could leave late responses, local availability did not prove Ollama reachability, and lenient parsing/partial command semantics were unsuitable authority for intrusive effects.
- **Exact evidence:** `ConditioningControlPanel/CCP.Core/Services/AIService/IAiService.cs`, `LocalAiService.cs`, `OpenAiService.cs`, `AiResponseParser.cs`, and `ConditioningControlPanel/CCP.Avalonia/Services/Commands/AiCommandService.cs`.
- **Disposition:** `ADAPT` provider-neutral transport, client-side moderation, explicit local memory, and opt-in commands. `REJECT` string-inferred failures, no cancellation, silent endpoint fallback, remote host called "local", lenient command repair, partial/no-op effects reported as success, and awareness context transmission without explicit consent.
- **Consequence:** A-010 and the AI capability contract require typed outcomes, cancellation generations, strict commands, actual endpoint disclosure, memory isolation, secret storage, and content-free diagnostics.

### Deep-learning webcam inference was the right direction, not the Windows-only port

- **Lesson or claim:** `VERIFIED` The owner liked the deep-learning camera approach. WPF uses local BlazeFace, FaceMesh, and Iris ONNX models with no runtime network. The first attempt added appearance-based deep gaze options but only registered a real Windows tracker; Linux received a stub and deep-model failure silently fell back to iris despite calibration feature mismatch.
- **Exact evidence:** `ConditioningControlPanel/Services/Webcam/WebcamTrackingService.cs:21-69`; `ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/Services/Webcam/AvaloniaWebcamTrackingService.cs:1063-1183` and `:1888-1924`; Linux head registration search shows no real tracker.
- **Current research:** ONNX Runtime supports Windows/Linux CPU inference and recommends retained sessions/reused buffers. Current OpenCvSharp provides maintained Windows/Linux runtimes, but Linux slim disables camera `videoio`. Sandboxed Linux camera requires XDG Camera portal/PipeWire. First-attempt MobileGaze weights trace to Gaze360, whose dataset restricts commercial use, so weights are not admitted.
- **Disposition:** `ACCEPT` local deep face/landmark/iris inference as the product direction. `ADAPT` gaze calibration/model selection through measured legal Windows/Linux spikes. `REJECT` Windows-only support, fake-running stubs, silent engine fallback, raw-frame persistence/network/logging, and unverified model provenance.
- **Consequence:** A-011 requires one admitted gaze feature/model pair, calibration identity binding, real Linux capture, privacy proof, and legal/performance evidence before deep full-face gaze ships.

### The first attempt needed an official migration baseline

- **Lesson or claim:** `VERIFIED` The first attempt repeatedly recreated WPF concepts ad hoc, contributing to styling, scrolling, right-click, window, animation, and transparency drift. Current official 2026 Avalonia documentation now provides explicit WPF mappings that were not consistently used.
- **Current evidence:** The official migration guide identifies styling, data-template placement, property types, pointer/routed events, and control differences as the main conceptual shifts. The cheat sheet explicitly covers selectors/pseudo-classes instead of triggers, binding shorthand, direct commands, dispatcher semantics, animations, transparent-window limitations, assets, controls, and screen APIs.
- **Disposition:** `ACCEPT` the current official guide/cheat sheet as the translation baseline. `ADAPT` the older expert-guide methodology for dependency audit, incremental vertical slices, small commits, and cross-platform testing. `REJECT` literal code-structure preservation, comment-out migration, XPF/Hybrid, and generic package advice for this greenfield attempt.
- **Consequence:** Every WPF-shaped task first records behavior, then translates using A-012 and current deeper docs. Mechanical syntax conversion cannot close a task without headed Windows/Linux acceptance.

## Adapted lessons

### Click-triggered audio must be acknowledged reliably

- **Lesson or claim:** `VERIFIED` The product owner reports that the first attempt intermittently failed to play expected audio after a click. The exact affected click targets are not yet identified, so this is a verified first-attempt failure report, not yet a complete behavioral contract.
- **Exact evidence:**
	- A bubble click is one concrete WPF behavior: a successful ambient pop selects and starts a pop or lucky-chime sound before announcing the pop and awarding progress: `ConditioningControlPanel/Services/BubbleService.cs:930-963` and `:1840-1877`.
	- The first attempt also requests a pop sound whenever its bubble engine reports a pop: `ConditioningControlPanel/CCP.Avalonia/Services/AvaloniaBubbleService.cs:583-589`.
	- Its one-shot sound path resolves the asset, starts detached background work, creates a new media player for that request, and treats output-device selection as best effort: `ConditioningControlPanel/CCP.Avalonia/Platform/AvaloniaSfxPlayer.cs:41-48`, `:78-126`, and `:128-141`. These facts identify a path that needs reliability validation, but do not by themselves prove the reported intermittent root cause.
	- A separate shared audio path stops the currently held media before each new play request: `ConditioningControlPanel/CCP.Avalonia/Platform/AvaloniaAudioPlayer.cs:44-65`. Sharing that path between unrelated cues could make a newer request replace an earlier one; which reported clicks used it remains `UNVERIFIED`.
- **Current Avalonia v12 verification:** No Avalonia UI API establishes media-playback reliability because playback is supplied by a separate media backend. Package and playback-channel choices remain deferred. The new client must validate the observable result on Windows and Linux rather than infer success from a completed fire-and-forget call.
- **Disposition:** `ADAPT`. Preserve each WPF feature's intended audible acknowledgement, not either old audio implementation. Define whether a cue may overlap, interrupt, queue, or be dropped per feature, and test rapid repeated input, simultaneous media, output-device changes, mute, and zero-volume behavior.
- **Consequence for the new client:** A successful user action that has a specified sound is not complete merely because playback was requested. Its contract and acceptance evidence must cover audible playback on both target platforms under realistic contention. Missing assets or unavailable output must not block the action, but must produce bounded, non-sensitive diagnostics.

### Color overlay must never become fully opaque

- **Lesson or claim:** `VERIFIED` The product owner reports that the first attempt sometimes made the color overlay fully opaque for no intended reason while a web or mandatory video was playing. A 100-percent color overlay is unacceptable in every context. The cause and triggering sequence remain `UNVERIFIED`.
- **Exact evidence:**
	- WPF stores the user-facing pink-filter opacity in a range of 0 to 50 percent: `ConditioningControlPanel/Models/AppSettings.cs:2761-2772`.
	- WPF's overlay pulse temporarily doubles active overlay intensity and historically allowed the pink pulse calculation to reach 100 percent: `ConditioningControlPanel/Services/Notifications/OverlayService.cs:342-381`. Its restore path now explicitly handles ramp-owned opacity because a normal refresh could otherwise leave the boosted value stranded: `:405-437`.
	- The current first-attempt code added an apply-point maximum of 50 percent and documents that it is intended to cover settings, ramps, pulses, and ad-hoc commands: `ConditioningControlPanel/CCP.Avalonia/Services/Overlays/AvaloniaOverlayService.cs:531-568`. Its pulse path also caps the temporary pink value at 50 percent: `:196-239`. This is evidence of a later guard, not evidence that the owner-observed failure never occurred or is fixed in every sequence.
	- The inspected first-attempt video-start path announces playback and top-window state but does not directly request an overlay pulse: `ConditioningControlPanel/CCP.Avalonia/Services/Video/AvaloniaVideoService.cs:902-931`. A repository search found the first-attempt pulse call only in keyword-trigger handling, at `ConditioningControlPanel/CCP.Avalonia/Services/KeywordTriggers/AvaloniaKeywordTriggerService.cs:1380-1394`. Therefore a direct causal link from web or mandatory video startup to the opacity change is `UNVERIFIED`.
- **Current Avalonia v12 verification:** This is application state and composition behavior, not an Avalonia API guarantee. No framework-level fact makes a shared tint immune to stale, overlapping, or incorrectly restored state.
- **Disposition:** `ADAPT`. Keep unified composition, but reject implicit coupling between video lifecycle and persistent overlay intensity. No feature, pulse, ramp, command, error, or media lifecycle may make the color overlay fully opaque. Every temporary intensity owner must remain below the approved safety ceiling and restore the latest valid underlying value after overlap, cancellation, playback error, or teardown.
- **Consequence for the new client:** The rendered color overlay must remain below 100 percent at all times. Starting or ending web or mandatory video must not alter the user's overlay value unless a separately specified effect explicitly requests a bounded temporary change below the approved safety ceiling. Acceptance must observe actual rendered opacity throughout playback and teardown, not only inspect the stored setting.

## Rejected assumptions

- **`REJECT` A browser payload present in the output means the feature is ported.** The first attempt copied DTRH into Linux output but registered no Linux game host.
- **`REJECT` WebView2 virtual hosts and `window.chrome.webview` are portable browser contracts.** They are Windows implementation details. The new client preserves origin and message outcomes, not those names.
- **`REJECT` DTRH needs a classic/native fallback.** The greenfield client intentionally has one web game. Failure must be visible and bounded instead of silently switching products.
- **`REJECT` Fullscreen browser capture is equivalent to video playback.** It captures unrelated browser pixels, distorts target monitors, depends on platform capture support, and cannot guarantee one synchronized media timeline.
- **`REJECT` One decoder per monitor is required for duplication.** The desired behavior is one decoded frame fanned out through per-monitor Skia targets.
- **`REJECT` A `ScrollViewer` declaration proves scrolling.** It must have a finite viewport and pass wheel, touch, keyboard, and thumb interaction tests.
- **`REJECT` Localized card titles are feature identifiers.** Translation and presentation cannot control command routing.
- **`REJECT` Similar-looking windows share one behavior.** Ownership, modality, activation, focus, taskbar, topmost, resize, and lifetime remain per-window decisions.
- **`REJECT` Animation timers or decoder events prove AvatarTube animation.** Acceptance requires changing rendered frames.
- **`REJECT` A long smoke/layer sweep proves visual quality.** Broad traversal is expensive and can miss obvious pixel defects; targeted screenshot review must inspect the affected surface.
- **`REJECT` One generic player can own quips, voice, whispers, and test audio.** These channels have different overlap, priority, and completion contracts.
- **`REJECT` Parsed AI output implies an effect executed safely.** Strict validation, moderation, permission, cancellation, and per-command results are required.
- **`REJECT` Selecting local AI guarantees local-only data.** A configurable non-loopback Ollama host is remote.
- **`REJECT` A webcam service reporting running proves tracking.** Real capture, inference, events, and privacy evidence are required on each platform.
