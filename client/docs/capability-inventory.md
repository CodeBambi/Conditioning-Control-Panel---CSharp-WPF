# WPF capability inventory

This inventory records observable product behavior. WPF classes and APIs are evidence, not a design to translate. The new client may use different internals when it preserves the same outcome on Windows and Linux.

## Companion quips, voice, and sound

- Reactive quips/barks are selected from the active theme/mod's authored rules in response to real application events. Conditions, chance, cooldown, one-shot scope, priority, disabled phrases, and no-repeat rotation determine whether a line is eligible.
- Ordinary chatter must not become stale: suppress it during active chat, whisper voice, narration, or existing ordinary companion speech; drop expired reactions rather than queueing them long after the event.
- Ordinary quips queue. Priority and safety quips may interrupt current interruptible companion voice and clear stale ordinary speech. Authored uninterruptible voice retains the floor.
- Text, voice asset, mood/emotion, source event, priority, freshness deadline, and cancellation generation travel as one speech item. Never queue text while losing its associated audio.
- Prebaked voice is the product requirement; runtime TTS is not required. Missing voice falls back to visible text without blocking the event.
- Muting voice does not hide the speech bubble. The bubble, voice clip, and AvatarTube speaking/emotion animation describe the same line.
- Audio has explicit ownership:
	- one exclusive companion-voice channel for bark/event/idle/wake/custom voice;
	- bounded-polyphony SFX for clicks, pops, chimes, and other acknowledgements;
	- a whisper/session-voice channel with real busy/completion state;
	- the single video/media channel;
	- reference-counted system ducking where supported.
- Playback reports accepted, started, completed, interrupted, cancelled, missing asset, unavailable device, and decode failure. Submitting a native play request is not completion.
- Required successful-action cues must be heard within the approved latency even while voice/video play. Low-value ambient cues may be coalesced only when their contract permits it.
- One persisted output-device authority and channel gains apply consistently. A stale/unplugged device falls back visibly to system default instead of silent playback loss.
- Panic, cancellation, failure, device loss, and shutdown restore ducking and clear all channel state without orphan audio.

### Sound acceptance

- Exercise voiced/unvoiced ordinary and priority quips, queue freshness, mute text-only behavior, missing assets, mod switching, and disabled phrase persistence.
- Burst required click cues while companion voice and video play; verify each accepted cue audibly starts and bounded polyphony does not grow tasks/players without limit.
- Verify real completion/interruption events, live volume/device changes, stale-device fallback, pause/resume, and normal/error/panic teardown on Windows and Linux.
- Linux ducking is unsupported until the selected PipeWire/PulseAudio environments prove the approved behavior; a missing `pactl` no-op is not support.

### Sound evidence

- WPF bark policy and delivery: `ConditioningControlPanel/Services/Companion/BarkService.cs` and `AvatarTube/AvatarTubeWindow.Speech.cs`.
- WPF phrase audio: `ConditioningControlPanel/Services/Companion/CompanionPhraseService.cs`.
- First-attempt failure evidence: `ConditioningControlPanel/CCP.Avalonia/Platform/AvaloniaAudioPlayer.cs`, `AvaloniaSfxPlayer.cs`, and `Services/Bark/AvatarBarkSpeaker.cs`.

## AI companion and integrations

- Users may select cloud, local Ollama, or an approved OpenAI-compatible provider. Switching affects the next request and never relabels a late old-provider response as the new provider.
- Every inference has one typed outcome: generated response, canned fallback, input/output refusal, unavailable/misconfigured, rate limited, timeout/provider/network failure, cancelled, busy/superseded, malformed structured response, or partial command rejection.
- Infrastructure failure, cancellation, or refusal is never disguised as a successful AI response. Canned personality text remains unbadged and machine-identifiable.
- Input moderation happens before outbound transmission. Sanitized reply and every user-visible/audible/hardware/media/delayed command field are output-moderated before dispatch. Background refusal is silently dropped; interactive refusal uses the approved policy UI and is not retained in memory.
- Interactive chat and ambient awareness share transport/moderation rules but have separate priority, cancellation, memory, fallback, consent, cooldown, and rate-budget behavior.
- Every request captures provider configuration at start and carries cancellation through HTTP, response display, memory writes, retries, and effect dispatch. Closing the surface, disabling AI, provider change, panic, and shutdown suppress late results and delayed work.
- Local memory is provider-owned, explicit, bounded, atomically persisted, inspectable as enabled/disabled, and immediately clearable. It never flows into cloud/OpenAI providers. Visible transcript and model memory are distinct.
- Effect control is opt-in, default off, permissioned per effect, capped, and accepts only a strict versioned structured envelope. Never repair malformed/mixed prose into executable authority. Report accepted, denied, unsupported, cancelled, and failed commands separately.
- AI never claims an effect occurred when it was denied, unsupported, or failed.
- The UI discloses the actual destination and data categories before enabling non-loopback providers. A configurable Ollama host is local only when it resolves to loopback. Invalid endpoints send nothing and never silently fall back to another host.
- Remote endpoints require HTTPS unless explicit loopback HTTP. Never log prompts, replies, titles, URLs, API keys, auth headers, provider response bodies, or command payload text.
- Awareness context such as active application/page title requires separate explicit consent for network transmission. Webcam frames and biometric derivatives never enter AI prompts or network calls.

### AI acceptance

- Force every typed outcome across providers and verify correct localized status, badge provenance, memory behavior, and diagnostics.
- Switch provider, cancel, close, panic, and shut down during requests; verify no late bubble, history write, retry, or effect.
- Fuzz structured output and command fields; malformed/mixed/moderated/out-of-range data executes zero unsafe commands.
- Verify offline mode produces zero AI-related outbound requests and endpoint disclosure matches captured network traffic.
- Verify secrets and logs on Windows and Linux meet the approved storage/redaction contract.

### AI evidence

- WPF providers and moderation: `ConditioningControlPanel/Services/AiService.cs`, `Services/AIService/`, and `AI_AUDIT.md` as a chronological audit ledger.
- First-attempt portable provider code: `ConditioningControlPanel/CCP.Core/Services/AIService/`; use for lessons only.
- Current Ollama API evidence: [chat API](https://docs.ollama.com/api/chat) and [API introduction](https://docs.ollama.com/api/introduction).

## Webcam, face, and gaze tracking

- The product direction is local deep-learning inference: BlazeFace face detection, FaceMesh landmarks, and Iris eye/iris landmarks, with semantic blink/gaze/face events and an evidence-backed calibrated gaze engine.
- Frames, crops, tensors, landmarks, gaze samples, and all per-frame biometric derivatives remain memory-only. They are never saved, logged, transmitted, included in screenshots, telemetry, AI prompts, or crash reports. Audio capture is never opened.
- Expanding sensors, derived data, persistence, networking, diagnostics, or telemetry requires a consent-contract revision and owner review.
- Camera starts only after current explicit consent and an explicit user start/accepted feature prompt. Opening the dashboard, restoring settings, or finding calibration never starts it.
- UI exposes honest states: stopped, permission request, opening, model load/warm-up, tracking, face lost, busy, denied, removed, missing runtime/model, slow inference, and error.
- Camera acquisition and model initialization are cancellable and off the UI thread. Stop/panic/revoke/exit/device removal closes capture and suppresses events without native disposal races.
- Persist stable camera identity where available, calibrated monitor identity/geometry, model/feature/preprocessing identity, model hash/version, quality summary, numeric coefficients, and quick-recal offset. Never use only a transient camera index.
- Calibration belongs to one physical monitor and admitted gaze feature/model. Camera, monitor geometry, model, feature, or preprocessing mismatch invalidates/suspends positional gaze until recalibrated. Silent engine fallback with incompatible calibration is forbidden.
- Deep full-face gaze models remain a spike until their weights and training data are commercially distributable and they outperform the iris baseline under the same Windows/Linux calibration and hardware matrix.
- Normal operation needs no raw-frame preview. Any diagnostic preview requires separate justification and consent-safe handling.
- Windows and Linux must provide real enumeration, capture, calibration, inference, semantic events, and teardown. Linux sandboxed delivery must prove XDG Camera portal/PipeWire behavior when in scope. A stub that says running is a failure.

### Webcam acceptance

- Test consent/revocation, stable device selection, denial/busy/black/frozen/unplug/read-failure/model/runtime errors, repeated lifecycle, and responsive startup.
- Measure accuracy, latency, sustained FPS, CPU, memory, startup, and power on approved low/median/high hardware for each admitted engine.
- Verify BlazeFace/FaceMesh/Iris, blink, gaze, face loss/recovery, calibrated-monitor restriction, quick recalibration, panic, and teardown on Windows and supported Linux X11/Wayland environments.
- Audit filesystem, logs, network, telemetry, screenshots, and crash artifacts to prove no frame or biometric derivative escapes memory.

### Webcam evidence

- WPF privacy and ONNX pipeline: `ConditioningControlPanel/Services/Webcam/WebcamTrackingService.cs`.
- First-attempt Windows tracker/deep-gaze experiment: `ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/Services/Webcam/AvaloniaWebcamTrackingService.cs`; Linux had no real implementation.
- Current candidates requiring admission spikes: [ONNX Runtime C#](https://onnxruntime.ai/docs/get-started/with-csharp.html), [execution providers](https://onnxruntime.ai/docs/execution-providers/), [OpenCvSharp](https://www.nuget.org/packages/OpenCvSharp4/), and the [XDG Camera portal](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.Camera.html).

## Dashboard and feature popups

### Feature-card interaction

- Left-clicking a feature card opens that feature's modeless settings popup.
- Plain right-click anywhere on the body of every unlocked, toggleable card immediately reverses its enabled state. It does not open the settings popup or require a context-menu choice.
- When a session is running, quick-toggle also starts or stops the actual service. Changing only the persisted flag is an inert-UI failure.
- The setting is saved and the card's lit/unlit border updates immediately. The result remains correct after changing language, theme, or mod and after restarting the application.
- Dispatch uses a stable feature identity, not localized display text, artwork, capitalization, visual-tree position, or object-title comparison.
- Locked cards ignore quick-toggle and suppress the active ring. The help button opens help only. `Visuals` and `System` remain neutral because they have no single enabled state.
- Test and advanced actions may use separate buttons or an explicit modified/context gesture, but they never replace plain right-click quick-toggle.

### Feature-popup behavior

- Only one feature popup is open from the dashboard at a time. It is modeless, owned by the dashboard window, centered on that owner, absent from the taskbar, non-resizable, draggable by its title bar, and closed by its close button or Escape.
- Closing returns focus to the owner without unexpectedly unminimizing, resizing, or moving it. Escape remains available to an active key-capture control when that control explicitly owns the key.
- Short content produces a compact popup. Tall content is capped inside the working area of the monitor containing the owner, not the primary monitor by default.
- Overflow content scrolls vertically by mouse wheel, precision trackpad, touch pan, scrollbar track/buttons, and thumb drag. Horizontal scrolling is disabled.
- Nested lists and editors scroll themselves while they can, then chain remaining movement to the popup where appropriate. Keyboard focus brings clipped controls into view.
- Popup bounds and scrolling remain valid on secondary and mixed-scale monitors and after DPI or working-area changes. No setting or action may be unreachable below the viewport.

### Dashboard acceptance evidence

- Exercise every toggleable card while stopped and during a running session. Verify exactly one state transition, correct service start/stop, immediate ring update, persistence, and no popup/context menu from plain right-click.
- Repeat card dispatch in every supported language and all five built-in themes. Verify locked, neutral, and help-button exceptions.
- Open every feature popup on primary and secondary mixed-scale monitors. Reach the final control by wheel, trackpad/touch, keyboard focus, and scrollbar thumb, and record changing `Extent`, `Viewport`, and `Offset` or equivalent observable evidence.
- Resize, minimize, restore, move, and close the owner while a modeless popup is open; verify the WPF-defined owner and focus lifecycle.

### Evidence locations

- WPF cards and quick-toggle dispatch: `ConditioningControlPanel/Features/FeatureCard.xaml.cs` and `ConditioningControlPanel/MainWindow/MainWindow.Presets.cs`.
- WPF popup: `ConditioningControlPanel/Features/FeaturePopupWindow.xaml` and `.xaml.cs`.
- First-attempt card dispatch: `ConditioningControlPanel/CCP.Avalonia/Features/FeatureCard.axaml.cs` and `ConditioningControlPanel/CCP.Avalonia/Views/Tabs/SettingsTabView.axaml.cs`.
- First-attempt popup sizing: `ConditioningControlPanel/CCP.Avalonia/Features/FeaturePopupWindow.axaml` and `.axaml.cs`.

## Desktop window behavior

- Window appearance may share theme resources, but window behavior is defined per surface. Do not apply one blanket dialog/window policy merely because several windows look similar.
- Every retained window must be classified as one of: owned modal dialog, owned modeless tool/editor, standalone interactive window, non-activating notification, interactive topmost attention surface, passive overlay, companion/widget, or startup/progress surface.
- Each window contract records ownership and owner-close behavior, modal/modeless presentation, activation and initial focus, focus restoration, taskbar and Alt-Tab presence, topmost policy, resize/minimum/maximum sizing, startup monitor and placement, decorations and drag/resize regions, and close/cancel/hide/reuse/shutdown behavior.
- Modeless tools leave their owner usable. Modal dialogs block only the intended owner. Notifications and passive surfaces never steal focus. Interactive attention surfaces accept only their intended input. Editors retain the resize behavior needed to reach and edit all content.
- Shared custom chrome must provide the same usable move, resize, close, minimize, and maximize behavior required by that window's contract on Windows and Linux. A visually themed border with broken native behavior is not parity.
- Unsupported topmost, non-activation, taskbar, or ownership behavior on a Linux backend is an explicit product gap. It is not silently replaced with whichever default window behavior happens to appear.

### Window acceptance evidence

- Maintain a per-window manifest backed by WPF call sites. For every retained window, exercise launch, initial focus, owner interaction, owner minimize/restore/close, taskbar/Alt-Tab visibility, move/resize, startup monitor, close/cancel, repeated open, and application shutdown.
- Run the manifest on Windows and supported Linux display backends/window managers. Screenshots prove appearance only; headed interaction evidence proves behavior.

## AvatarTube

### Visual animation modes

- A static multi-pose avatar changes pose at its configured cadence with a visible fade. It never abruptly swaps, remains stuck on pose one, or flashes blank between poses.
- An animated GIF avatar decodes and loops continuously while visible. A successful asset lookup or started timer is insufficient; displayed frames must actually change.
- An emote-enabled avatar plays complete one-shot clips, crossfades between two visual layers without blank or duplicated frames, rotates through idle clips, and returns to idle after speech, reactions, interruption, missing clips, and decode errors.
- Speech chooses talking animation appropriate to the line and audio duration, transitions to its reaction/emotion, then settles into idle. Click reactions and nonverbal events remain distinct from speech.
- The avatar has a subtle continuous vertical float for liveness. That transform affects avatar content only and never changes the top-level tube window's position or size.

### Lifecycle and layout

- Hiding, minimizing the owner, explicit pause, and a covering experience pause animation work. Restoring resumes exactly one pose/GIF/emote/float pipeline without doubling speed or callbacks.
- Switching themes/mods updates tube art, layout, selected supported avatar set, animation source, and emote map in place. Dispose old decoders, frames, crossfade layers, subscriptions, and random bubbles before activating the replacement. Never show two avatars.
- Attach/detach preserves the current visual and animation state. Attached mode follows and scales with the main window without stutter. Detached mode remains independently movable/resizable and follows its own topmost and input contract without changing the main window.
- The tube and avatar remain correctly positioned and scaled across owner movement, resize, minimize/restore, monitor changes, and mixed scaling. Animation transforms never fight layout/anchoring transforms.
- Closing disposes all animation players, timers, frame callbacks, hooks, bubbles, and owner/mod/service subscriptions.

### Rendered-liveness acceptance

- Exercise at least one static multi-pose set, one looping GIF set, and every built-in emote-enabled mod on Windows and Linux.
- Record displayed-frame hashes or equivalent rendered-output samples. Static pose fade, sustained GIF playback, idle emote rotation, A/B crossfade, short/long speech, reaction, click emote, and float must each produce bounded visible changes; method calls and timer ticks alone do not pass.
- Repeat pause/resume, hide/show, owner minimize/restore, attach/detach, monitor move, and all built-in mod switches. Verify no blank interval beyond the approved transition, duplicate avatar, multiplied speed, orphaned player, or increasing timer/subscription count.
- Missing or unsupported animation assets fall back visibly to a valid static avatar and bounded diagnostics, never an invisible tube or busy retry loop.

### Evidence locations

- WPF animation behavior: `ConditioningControlPanel/AvatarTube/AvatarTubeWindow.xaml.cs`, `AvatarTubeWindow.Avatar.cs`, `AvatarTubeWindow.CirceEmotes.cs`, `AvatarTubeWindow.Speech.cs`, and `AvatarTubeWindow.Windowing.cs`.
- First-attempt evidence only: `ConditioningControlPanel/CCP.Avalonia/AvatarTube/AvatarTubeWindow.axaml.cs`, `AvatarTubeWindow.Avatar.cs`, `AvatarTubeWindow.CirceEmotes.cs`, `CirceEmoteEngine.cs`, and `AvatarTubeWindow.Windowing.cs`.

## Video presentation

### One presentation path

- Mandatory, local, direct-URL, and supported online video use one native decoder and the shared Skia composition domain. The browser may discover or authorize a source, but it does not become the fullscreen video renderer.
- Do not fullscreen the browser, capture a browser monitor, stretch that capture onto other monitors, or start one independent player per monitor.
- The same decoded frame and playback timeline are presented on every connected monitor. There is one audible player using the configured video volume, mute state, and output device.
- "Every monitor" is unconditional for this presentation mode. It does not inherit old two-monitor limits or silently omit displays to reduce decoder cost.

### Per-monitor geometry

- Treat each monitor as its own target rectangle. Never render one giant video across the virtual-desktop union and never infer monitor order from array position.
- Support monitors to the left, right, above, or below the primary display, including negative virtual-desktop coordinates, vertical stacks, gaps, mixed resolutions, and mixed scaling.
- Honor the display geometry and orientation reported by the operating system, including portrait and flipped orientations. Do not assume width greater than height means landscape. Do not rotate the decoded image a second time when the OS compositor has already transformed that display; runtime tests decide whether a backend requires an explicit transform.
- Recompute targets when displays are added, removed, rotated, rearranged, or have scaling changed. A display change must not leave a stale or black orphan surface.

### Fit, background, and composition

- Preserve the video's original aspect ratio independently on every monitor. Center it using an aspect-fit calculation; do not stretch, crop, or zoom merely to fill a differently shaped display.
- Fill uncovered letterbox or pillarbox regions with opaque black in the video layer. When the color overlay and spiral are enabled, they render above both the video and its black bars, so those bars naturally take on the bounded overlay color and spiral without a second special background mode.
- Keep the successful UCE ordering concept: video and its background at the bottom, then the established effect layers, with spiral above video and the bounded color overlay above spiral. Higher interactive or attention layers retain their defined relative order.
- Online video must not mutate overlay settings. The rendered color overlay remains below full opacity over the frame and the black bars in every playback, failure, and teardown state.

### Playback lifecycle

- Starting supported online video leaves the browser embedded or hidden as appropriate; it does not create a fullscreen browser window, taskbar entry, or screen-capture loop.
- The native video path owns start, progress, pause, seek, end, decode error, stop, panic, volume/device/mute, and watch-credit events. Mandatory-only policy such as strict dismissal or attention checks remains a mode flag, not a second renderer.
- All monitor surfaces consume the same presented frame identity during a composition cycle. Independent decoders that happen to start together do not satisfy synchronization.
- End, user stop, decode failure, display reconfiguration, browser failure, or application shutdown clears video, background, and audio on every monitor. No frozen capture, permanent black surface, or orphaned audio is acceptable.

### Online-source handoff

- A direct HTTP(S) media URL or supported stream manifest may be handed to the native decoder when its codec, protocol, and required request context are supported.
- An arbitrary web page is not itself a playable media source. Browser `blob:` URLs, Media Source Extensions, authenticated requests, expiring URLs, and DRM may prevent safe transfer to the native decoder.
- The client does not bypass DRM and does not promise universal extraction from every site. Supported sites and source forms require an explicit capability matrix and a browser-to-native handoff spike.
- If a page cannot provide a supported native source, fail visibly with an actionable explanation. Do not silently fall back to browser fullscreen or browser screen capture.
- Never persist decoded frames or captured page content, and never log signed URLs, cookies, authorization headers, DRM data, or page contents.

### Acceptance evidence

- Use a frame-numbered test video to prove that Windows and Linux present the same frame identity on every monitor from one decoder and one playback timeline.
- Exercise layouts with negative X and Y, monitors above and below the primary display, vertical stacks, mixed scaling/resolution, portrait, landscape, and both flipped orientations reported by the OS.
- On each display, verify aspect preservation, centering, opaque black bars, no crop/stretch, and correct behavior after live rotation/rearrangement/hot-plug.
- Exercise video, black bars, spiral, and color overlay simultaneously. Verify established layer ordering and observe actual rendered tint below full opacity.
- Verify one audio output regardless of monitor count and test live volume, mute, output-device change, pause/seek, normal end, panic, decoder failure, display change, and shutdown.
- Test the approved online-source matrix separately. An unsupported page must report failure without opening fullscreen browser video or starting a capture mirror.

### Evidence locations

- WPF direct-URL and mandatory-video behavior: `ConditioningControlPanel/Services/Video/VideoService.cs`.
- First-attempt shared decoder/render behavior: `ConditioningControlPanel/CCP.Avalonia/Compositor/Layers/VideoLayer.cs` and `MandatoryVideoLayer.cs`.
- First-attempt UCE ordering: `ConditioningControlPanel/CCP.Avalonia/Compositor/CompositorLayers.cs`.
- Rejected browser-capture approach: `ConditioningControlPanel/CCP.Avalonia/Compositor/Layers/BrowserMirrorVideoLayer.cs` and `ConditioningControlPanel/CCP.Avalonia/Services/Video/BrowserMirrorVideoService.cs`.

## Down the Rabbit Hole

### Product boundary

- Down the Rabbit Hole is the bundled web roguelike in `ConditioningControlPanel/Resources/web/dtrh/`.
- The greenfield client has no classic or native DTRH mode, no setting that chooses between implementations, and no automatic downgrade to the old game.
- Copy the web payload as one versioned asset tree. Do not optimize, convert, or rewrite its game engine as part of the desktop port.
- A host-only compatibility edit is allowed only when the Windows/Linux browser spike proves that an unchanged payload cannot use the portable browser bridge. Keep any such edit isolated to `bridge.js`, preserve protocol version 1 and all game behavior, and record the copied-source difference.

### Launch and window behavior

- Normal launch first asks the player to choose one of three local save slots. Cancel leaves DTRH closed. Launching an already-open game focuses the existing window and never changes its slot mid-session.
- Quick start skips the picker and reuses the remembered active slot.
- The game opens in one dedicated, opaque, focusable, non-topmost window with a taskbar entry and normal decorations. Initial size may follow the platform, but it starts windowed.
- The page may request borderless fullscreen through the host protocol. The host, not the HTML Fullscreen API, owns that transition so the page retains its Escape-key behavior.
- The browser receives keyboard focus on launch and after a covering native video closes.
- Holding Escape in the page requests a graceful exit. A host close asks the page to shut down and then force-closes after a bounded watchdog if no acknowledgement arrives.

### Save-slot behavior

- There are exactly three independent local save files. The active slot is remembered in application settings.
- Slot cards distinguish an empty journey from an existing save and summarize rank, descents, currency, best score, and last-played time.
- Slot 2 remains locked until any surviving save has crafted the Ragdoll, unless slot 2 already exists. Slot 3 follows the same rule for the Porcelain doll.
- Deleting a save requires confirmation, removes its main and temporary files, and warns when deletion will lock slot 2 or 3 again.
- A pre-slot save migrates into slot 1. Missing or corrupt data cannot brick launch; it produces a fresh/default state while remaining deletable when a corrupt file exists.
- Writes use a temporary file followed by replacement. Interrupted writes are recovered where possible.
- The picker tells the player that saves are local and can open the containing folder with the platform file manager.

### Page boot and content

- The payload is offline and includes its own Three.js/WebGL application, modules, workers, CSS, audio, video, GIF, and image assets.
- The page is served from an HTTP origin, not `file:`. Root-relative `/dtrh/...` imports, module workers, fetches, media seeking, WebAudio, WebGL texture uploads, and CORS-clean user/mod media must work.
- The page announces protocol version 1 and waits for both `init` and `manifest` before starting the engine. Host messages arriving early and page messages arriving early must retain their ordering.
- Host initialization provides volume, active persona/mod content, saved run setup, current meta state, user-media manifest, saved Loom spirals, and optional favorite assets.
- The host exposes only the required roots: bundled game files, user media, bundled Chaos art, saved Loom GIFs, and the active mod's DTRH subfolder. Path traversal and unrelated mod/application files must not be served.
- Browser storage/cache has an application-owned location. The game itself remains host-authoritative for progress and does not replace the three native save slots with browser storage.

### Host protocol and native obligations

Protocol version 1 is a product contract. The portable transport may differ from WebView2's object shape, but these message outcomes remain:

- Page to host: `ready`, `log`, `sfx`, `fire-payload`, `freeze-state`, `meta-command`, `request-run`, `run-started`, `run-ended`, `bark`, `heartbeat`, `asset-stats`, `loom-save`, `loom-delete`, `boot-error`, `report-bug`, `fullscreen-set`, `exit`, `exit-done`, `pong`, and `vn-speaking`.
- Host to page: `init`, `meta`, `manifest`, `loom-list`, `favorites`, `run-config`, `loom-result`, `fullscreen`, `payout-result`, `payload-state`, `end-run`, and `ping`.
- Native-only effects remain native: requested SFX, voice/barks, audio payloads, mandatory covering video, progression/XP/achievement credit, persistence, bug-report opening, and Loom filesystem operations.
- Visual gameplay effects stay in the web game. The host must not recreate the old native DTRH visual overlay stack.
- A freeze request pauses and later resumes both covering video and spoken voice. Run end, browser failure, and teardown always clear a stale freeze.
- While mandatory video covers the game, the page pauses its run state. Ending or failing the video clears the cover state and restores game focus.
- Run configuration is validated and clamped by the host. The first descent remains the scripted onboarding run; later descents use saved setup and unlock gates.
- Run completion banks local meta rewards, progression and bubble credit, records bounded local session statistics, and returns a payout result to the page.

### Failure and recovery

- A genuine boot error, missing browser runtime, failed resource load, or unsupported WebGL path closes the game and reports an actionable error. It never starts the classic/native game because that product path does not exist in the new client.
- The page's progress-aware boot deadline remains bounded. Stale text that promises a classic fallback is not a host contract and should be removed only as a narrowly tracked payload maintenance change.
- A live page emits heartbeat messages. Silence uses a shorter threshold during a run than in the hub. The host may relaunch once, then closes and reports failure rather than looping.
- A browser process failure follows the same bounded recovery policy.
- Diagnostics contain message type and bounded error context, not save contents, user media contents, or sensitive paths.

### Audio and overlay safety while DTRH runs

- A successful game action with an assigned native cue must produce audible acknowledgement unless the user muted that channel or no output is available. Rapid actions and concurrent video/voice must not randomly discard required cues.
- DTRH launch, fullscreen, native payloads, mandatory video start/end, browser recovery, and teardown must not overwrite the user's persistent color-overlay intensity.
- The actually rendered color overlay always remains below full opacity. Temporary requests are bounded below the safety ceiling and restore the latest underlying value after overlap, cancellation, video failure, browser failure, or shutdown.

### Windows acceptance evidence

- Copied payload boots from its packaged origin in the supported Windows browser runtime and completes a scripted first descent.
- Save selection, quick start, fullscreen round-trip, Escape exit, forced close, relaunch-once recovery, native SFX/audio/video, payout, Loom save/delete, user media, bundled art, and active-mod content are exercised end to end.
- WebGL uploads and module workers succeed without CORS or origin errors.
- Mandatory video pauses the run, restores focus, and leaves rendered tint below full opacity through success and forced failure.
- Rapid successful clicks produce their specified audible cues under simultaneous game audio.

### Linux acceptance evidence

- The same copied payload and protocol boot on both a supported X11 session and a supported Wayland session using the admitted Avalonia WebView backend and documented runtime packages.
- WebGL, ES modules/import maps, module workers, WebAudio/autoplay, video decode/seeking, GIF processing, resource origins, and host messaging are exercised, not inferred from a successful navigation.
- Windowed/fullscreen transitions, focus restoration, graceful/forced exit, save files, file-manager opening, native media, payout, Loom, user assets, and mod content match the Windows outcomes.
- Missing WPE/WebKit dependencies or unavailable WebGL fail visibly and safely. An external browser, no-op launch, or silent feature disablement does not count as Linux DTRH support.
- Mandatory video and color-overlay safety pass the same rendered-state checks as Windows.

### Evidence locations

- WPF host behavior: `ConditioningControlPanel/Services/Chaos/DtrhHostService.cs`.
- Launch and quick-start behavior: `ConditioningControlPanel/MainWindow/MainWindow.Lab.cs`.
- Save persistence: `ConditioningControlPanel/Services/Chaos/ChaosMetaStore.cs`.
- Save picker behavior: `ConditioningControlPanel/Chaos/ChaosSlotPickerWindow.xaml` and `.xaml.cs`.
- Web protocol and boot: `ConditioningControlPanel/Resources/web/dtrh/bridge.js` and `boot.js`.
- First-attempt lessons only: `ConditioningControlPanel/CCP.Core/Services/Chaos/DtrhHostOrchestrator.cs` and `ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/Services/Chaos/DtrhGameHostService.cs`.
