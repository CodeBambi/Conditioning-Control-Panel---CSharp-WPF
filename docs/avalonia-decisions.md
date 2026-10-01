# Avalonia port: decisions log

Every non-trivial decision made while porting the WPF head to Avalonia 12, newest last.
The WPF head (ConditioningControlPanel/) is the reference for behaviour and looks; entries
here record where and why the port chose something, and who advised.

| Date | Question | Options | Choice | Advised by |
|---|---|---|---|---|
| 2026-09-28 | Which .NET does the stack target? | stay net8.0 (EOL 2026-11-10); net10.0 (LTS to 2028-11-14); net11 (STS, not GA) | net10.0 across every project (#1764) | user |
| 2026-09-28 | How is the live Avalonia UI driven for verification? | Avalonia DevTools MCP (paid); Keincheck embedded (MIT); none | Keincheck 0.12.0, Debug builds only, http://127.0.0.1:3001 (#1765) | user |
| 2026-09-28 | Platform verification scope | Windows + Linux; Linux only | Build and verify on Linux (X11/Wayland); Windows compile/test CI kept as a safety net, not a gate | user |
| 2026-09-28 | Fate of the WPF head | delete; keep shipping; retire but keep | Retire from shipping, keep in repo as the frozen, compiling reference | user |
| 2026-09-28 | Media backend for the Avalonia head (audio + video, Linux and Windows) | NAudio on Windows + another lib on Linux; SDL/OpenAL/miniaudio bindings; LibVLCSharp for both; WebView video | LibVLCSharp for audio and video on both OSes (one library, already used by WPF for video); NAudio stays WPF-only; seed the existing CoreAudio seam; no custom mixer (OS mixes one player per layer); ducking via WASAPI on Windows, pactl on Linux. See ~/ccp-port/briefs/oracle-media-backends.md | oracle |
| 2026-09-28 | Transparent video overlays on Wayland | emulate; drop on Wayland | Deferred to the overlay unit; will go to oracle-deep (user-visible behaviour change) | oracle (pending oracle-deep) |
| 2026-09-28 | Enable Program enrolment on the Avalonia head before a program runner exists? | (A) hold ProgramService in the shell and allow Enroll; (B) intro popup only, Enroll stays disabled | (B): an enrolled run the user cannot complete here would lapse and damage the real program ledger; Enroll waits for the program run panel + session runner | supervisor |
| 2026-09-28 | How do desktop overlays (flash, subliminal, bouncing text, corner GIF, pink tint, video, chaos) work on Linux Wayland and Windows? | native Wayland (set_input_region / layer-shell shim); XWayland via the X11 backend; degrade to in-app | Always the X11 backend (Avalonia 12.1.2 ships no Wayland backend, so Wayland sessions use XWayland). Non-focus overlays = override-redirect ARGB X window + empty XFixes input region; takeovers = managed fullscreen topmost per screen; Windows = small Win32 shim (layered/transparent/noactivate/topmost); fallback when unavailable = show in-app + tell the user. Pin the backend explicitly. Safety: takeovers/chaos on Wayland need a panic control reachable without a global hotkey (tray, on-overlay target or timeout) before they ship. First unit: SetOverrideRedirect + --overlay-check runner. | oracle-deep |
| 2026-09-28 | Manual flash trigger on the Avalonia head? | none (WPF parity); head-only "▶ Test" on the Flash card | Head-only ▶ Test (btn_test_2) firing one TriggerFlashOnce-equivalent burst, so overlays can be verified before the scheduler lands; revisit when the session scheduler ships | supervisor |
| 2026-09-28 | Ambient flashes on the Avalonia head before engine Start/Stop exists | gate on the unseeded CoreSession.IsEngineRunning (never fires, like the sibling cards); arm from the Flash card's Enable toggle | **Superseded by the engine Start/Stop row.** Arm from the Enable toggle so the feature is usable and verifiable now; re-gate on the engine when Start/Stop is ported. A saved-On flag does not arm at launch (matches WPF, where the engine starts stopped); Stop lets bursts already on screen finish | reviewer (accepted), supervisor |
| 2026-09-28 | Subliminals and bouncing text on the Avalonia head before engine Start/Stop exists | gate on the unseeded CoreSession.IsEngineRunning (never shows); arm from each card's Enable toggle | **Superseded by the engine Start/Stop row.** Same as ambient flashes: the Subliminal and Bouncing Text Enable toggles start/stop CoreSubliminal / the bouncing-text overlay directly; re-gate on the engine when Start/Stop is ported. A saved-On flag does not arm at launch | supervisor (brief overlay-text-families) |
| 2026-09-28 | How does the Avalonia head draw LibVLC video? | LibVLCSharp.Avalonia `VideoView`; LibVLC video callbacks into a `WriteableBitmap` | Video callbacks (RV32 vmem) copied into a `WriteableBitmap` shown by an `Image`: LibVLCSharp.Avalonia 3.10.1 (latest) depends on Avalonia 11.3.13 and does not support Avalonia 12, and a package built against another Avalonia minor throws at attach. Same frame copy as WPF's `InlineLoopVideo`; also works headless and under the transparent-overlay plan. One process-wide `LibVLC` (`LibVlcAudio.Shared`), created by the audio seeding; audio media opt out of video with `:no-video` instead of a global `--no-video`. First user: `MiniPlayerWindow` | supervisor |
| 2026-09-28 | How do achievements reach Core when `AchievementProgress` calls `App.*` and the WPF dispatcher? | `git mv` whole (does not compile); split the model; duplicate it per head | Split: data properties + pure statics (`DecideLoginStreakAdopt`, `ShouldBankDayRollover`, click trackers) in Core; the streak lifecycle (`UpdateDailyStreak`, `ResolveDeferredStreakBreak`, `AwardDeferredStreakBonus`, `SyncCurrentStreak`, `TryAdvanceDayRollover`) becomes head-side extension methods (`ConditioningControlPanel/Models/AchievementProgressStreak.cs`) so call sites are unchanged; `PendingStreakBreak` is `internal set`. Thresholds from `AchievementService`/`GamificationBridge` live once in Core `AchievementRules`. Serialized shape pinned by `Tests/CCP.Core.Tests/AchievementProgressShapeTests.cs`. See ~/ccp-port/briefs/oracle-achievements.md | oracle-deep |
| 2026-09-28 | Where is achievements.json on every head? | `CorePaths.UserData` (LocalAppData); `SpecialFolder.ApplicationData/ConditioningControlPanel/achievements.json` | `ApplicationData/ConditioningControlPanel/achievements.json` (Roaming on Windows, `~/.config` on Linux), same as WPF; `CorePaths.UserData` would orphan every Windows user's file. The path is a constructor argument of the future Core store so tests never touch the real file | oracle-deep |
| 2026-09-28 | Does the Avalonia head sync achievement progress to the server? | sync now; local-only until account/auth (#10) is ported | Local-only: no progression endpoints, never `ResetProgress`/`ClearProgressionData`. The WPF merge is union for unlocks and take-higher for counters, so local progress merges upward later | oracle-deep |
| 2026-09-28 | Does the Avalonia head update the login streak? | run `UpdateDailyStreak`; read-only | Read-only until the skill-tree seams (#6) exist: without shield/Oopsie access the streak would reset to 1 without spending the protection the user owns (and a copied `UnifiedId` defers, then falls back to the same reset after 120s) | oracle-deep |
| 2026-09-28 | Tray icon on the Avalonia head: when is it visible, and what does it offer beyond WPF? | WPF parity (icon only while hidden, no stop item); always visible + Stop everything | **Superseded by the engine Start/Stop row.** Always visible, with a Stop everything item that unticks Flash/Subliminal/Bouncing Text and closes their overlays: it is the no-hotkey panic control the desktop-overlays row requires. X hides to tray only while a tray host exists (Windows always; Linux: `org.kde.StatusNotifierWatcher` owned on the session bus), else X exits. | review (tray-icon round 1) |
| 2026-09-28 | Time zones and the achievements streak day | normalise LastLaunchDate to UTC; keep WPF's local-time-with-offset | Keep WPF's format (byte-compatible): LastLaunchDate is stored as local time with an offset and read back as the same instant in the reader's zone, so a time-zone change can shift the streak day by one. Existing WPF behaviour, pinned by AchievementStoreTests.LastLaunchDateKeepsTheInstantAcrossTimeZones; revisit before the Avalonia head writes the streak | oracle-deep |
| 2026-09-28 | How does the panic key reach Wayland-native focus? | A always; B not-Escape; C only while effects run; D none | C: X11/XWayland focus uses XInput2 raw key presses (non-consuming, modifier-blind = the WPF LL hook; approved deviation from XGrabKey, which would swallow Escape desktop-wide). Wayland-native focus: GlobalShortcuts portal session opened and BindShortcuts called before the first overlay appears (waits <= 30 s), closed when the last effect stops; the user's rebinding is honoured and trigger_description shown. Portal missing/refused/timeout: XI2 only, one-time notice, effects start anyway. Settings text says the key is captured desktop-wide only while effects run | oracle-deep |
| 2026-09-28 | Which app does KDE file the portal panic shortcut under? | leave KDE to infer it from the launching process; register an app id first | KDE files it under the launching app's id (e.g. org_kde_konsole when started from Konsole). Follow-up: ship a .desktop file and call org.freedesktop.host.portal.Registry.Register (xdg-desktop-portal 1.19+) before CreateSession. The inactive entry left in the user's kglobalshortcutsrc can be removed in System Settings › Shortcuts | review (panic-hotkey) |
| 2026-09-28 | What does clicking an OS notification (tray hint) do? | nothing (WPF balloon has no click handler); restore the window | Clicking the first-minimize hint restores the window (`ShowFromTray`), same as clicking the tray icon; harmless and what a user expects of a Linux notification. | review (os-notifications) |
| 2026-09-28 | Where does the app version live, so every head reports the same one? | keep `<Version>` per csproj (WPF 6.10.3, Core 6.9.0, Avalonia unset = 1.0.0); `Directory.Build.props` (applies to every project, tests included); an explicit shared props | `Version.props` at the repo root, imported by `ConditioningControlPanel.csproj`, `CCP.Core.csproj` and `CCP.Avalonia.csproj`: one `<Version>6.10.3</Version>`. Explicit import rather than `Directory.Build.props` so test/smoke assemblies are untouched. WPF's stamped versions are unchanged (File 6.10.3.0 / Product 6.10.3 / Assembly 6.10.3.0, before and after: ~/ccp-port/evidence/mods-core-prereqs/version-{before,after}.txt); Core and the Avalonia head now stamp 6.10.3, so the Avalonia head's `CoreReleaseContent.AppVersion` (read from its assembly) matches WPF's `UpdateService.AppVersion` and mod `MinAppVersion` checks agree. `UpdateService.AppVersion` stays a WPF release constant bumped alongside (ConditioningControlPanel/CLAUDE.md version table) | oracle (modservice risks), worker |
| 2026-09-28 | Does the Linux head show the mod picker before the content-pack service is ported? | diverge (show on-disk mods, picking activates); stay WPF-faithful | WPF-faithful: ShowIfNeeded runs its real guards with "no pack service", so the picker never opens on Linux; it waits for ReleaseContentService (oracle §5) rather than inventing activate-from-picker behaviour WPF does not have | supervisor (dialogs-mod-picker-wired). **Superseded** (packs-mod-picker): WPF removed the auto-open in 6.11.x (MainWindow.xaml.cs:610-611); both heads offer mods via the first-run wizard and Mod Manager. The Avalonia picker is ported against the live pack service but has no caller, like WPF's |
| 2026-09-28 | Does the Avalonia session runner move WPF `SessionEngine` into Core or port it? | move (needs MainWindow, DispatcherTimer, Window, App.*; `CornerGifAdmissionTests` read it by path); port the pure parts | Port: `CCP.Core/Services/Session/SessionRules.cs` (SessionClock, SessionXp, SessionTimeline, DeferredStartQueue), `SessionSettingsSnapshot.cs`, `PhrasePoolCustody.cs` (FoldUserPoolEdit copied verbatim). The duplication is acceptable only because WPF is frozen; WPF keeps its own engine and seeding untouched. | oracle-session-runner, oracle-deep |
| 2026-09-28 | D1: carry over WPF's mid-session mod-switch pool bugs? (WPF never re-bases the user snapshot on a switch, so after A→B an edit writes A's pools into `ByMod[B]` and A→B→A saves A's pools as B's; `RestoreSettings` reconciles the mod between the subliminal and the bouncing/lock restores, leaving A's bouncing/lock pools live) | carry over; fix | Not carried over. `PhrasePoolCustody.Reapply` first sets each user snapshot to the incoming mod's freshly restored live pool, then re-asserts the prescribed pools; `Restore` reconciles after all three pools. Proof: `ModSwitch_End_AllThreePoolsAreIncomingMods`, `ModSwitchThenEdit_IncomingBackupClean`, `DoubleSwitch_BackupOfBIsB` fail with WPF's behaviour restored. | oracle-deep |
| 2026-09-28 | D2: the prescribe condition ignores the feature's Enabled flag (a disabled feature with session phrases still counts as overriding) | carry over; require Enabled | Carried over, as WPF `RememberPrescribedPools`: after D1 the only effect is that a mid-session switch re-asserts the start-time pool of that disabled feature until the session ends; the backups stay clean and the end restore gives the incoming mod's pool. | oracle-deep |
| 2026-09-28 | D1a: a mid-session switch to a mod with no per-mod backup | leave (WPF-shared leak); custody-only patch; root-cause fix in Core ModService | D1a: a mid-session switch to a mod with no per-mod backup saved the session's prescribed phrases into that mod's persisted backup, in Core and therefore in WPF too. Fixed in Core: the no-backup fallback and the self-heal prefer the user's snapshot, the same way SaveCurrentPoolsToSettings already does (`ModService.RestorePoolsFromSettings`, one `UserPhrasePoolsWhileOverriding` read). This changes WPF runtime behaviour, as a bug fix, only during a running session. The custody re-base is now unconditional. Proof: `PhrasePoolCustodyTests.ModSwitch_ToModWithoutBackup_BackupIsUserPools` (fails with the ModService change undone). | oracle-deep |
| 2026-09-29 | Who arms flash / subliminal / bouncing text / lock cards once engine Start/Stop exists, and what do tray Stop and panic do to saved flags? | keep arming from card toggles; Start/Stop arms from saved flags (WPF StartEngine) | Core `CoreEngine` mirrors WPF StartEngine/StopEngine (MainWindow.StartStop.cs:294/:444): Start arms flash always (it gates itself), subliminal / lock-card schedule / bouncing text only when saved On, and none of the four when AudioOnlySession is on (the audio bed itself is not ported); Stop disarms all and runs the head hook (overlays, `LockCardWindow.ForceCloseAll`, pink tint). Card and wall toggles only save while stopped and apply live while running (`CoreEngine.ApplyLive`). Tray "Stop everything" and the panic key call StopEngine and no longer untick saved flags. The session feature lock reads `CoreSession.IsSessionRunning` (a session, not the plain engine), so plain Start never greys the cards. `AnyEffectRunning` includes `CoreEngine.IsRunning`, so the portal shortcut stays bound for the whole run. Replaces the two arm-from-toggle rows and the tray row. | oracle-deep (session runner plan, U1) |
| 2026-09-29 | Linux token store | libsecret; 0600 file | libsecret (P/Invoke, `*v_sync`, no schema, attributes {application, name}). No Secret Service means in-memory only plus a notice; never plaintext by default. Windows Avalonia uses WPF's DPAPI files and entropy unchanged. `CCP.Avalonia/Platform/SecretStore.cs`; proof `scripts/secrets-roundtrip.sh` (throwaway gnome-keyring in Docker, never the user's keyring). | oracle-deep (account-auth plan) |
| 2026-09-29 | Does a session restore the user's SubliminalDuration? (WPF ApplySessionSettings writes it from the session's frames, SessionEngine.cs:1377, and SaveCurrentSettings/RestoreSettings never snapshot it) | carry over the leak; restore it | Session subliminal frames no longer leak into the user's SubliminalDuration after a session (WPF bug, not carried over; the WPF head is unchanged). `SessionSettingsSnapshot` carries it (37 fields). Proof: `SessionRunnerTests.OneMinuteSession_*` settings golden fails with the field removed. | supervisor (D1 precedent, oracle-deep) |
| 2026-09-29 | What XP does the Core session runner log and bank? | log the formula's award; log the XP actually banked | `SessionLog.XPEarned` = XP actually banked: the `SessionXp` award when `CoreProgression.AddXPProvider` is seeded (banked through it as "Session"), else 0. The Avalonia head seeds it since unit 7c (Core `ProgressionBank.Add`, no multipliers), so its logs now carry the banked award, non-zero on a completed signed-in session; the formula stays tested in `SessionRulesTests`. | oracle-deep (session runner plan §3) |
| 2026-09-29 | Where OAuth and the V2 client live | head; Core | Core, with WPF delegating. Supersedes the "socket stays in the head" note in `CoreAccount` (`HttpListener`/`HttpClient` are plain .NET). Unit 2 moved `V2AuthService` (client + DTOs) to `CCP.Core/Services/Account/V2AuthService.cs` and the XP-curve maths to `CCP.Core/Services/Progression/XpCurve.cs`; WPF delegates (`ProgressionService` statics, `V2AuthServiceHead.ApplyUserDataToSettings`, `MergedRecovery` seam). No wire change: `V2AuthServiceWireTests`, `XpCurveGoldenTests`. | oracle-deep (account-auth plan) |
| 2026-09-29 | What does a session started on the Avalonia head run, and what do tray Stop / panic do during one? | port every SessionEngine feature before wiring; run only the ported subset now | The head owns one Core `SessionRunner` (`App.Sessions`), started from the Presets session rack. It runs only the ported subset: flash, subliminal, bouncing text and lock cards (whispers flag written, nothing plays). Everything else a session prescribes is not driven and its settings are not written (the `ponytail:` list in `SessionRunner.cs`). The log's XP stays the banked amount (row above). **Superseded for tray/panic by U4:** tray "Stop everything" and the panic key now pause a running session and stop the engine, as WPF's panic does (MainWindow.xaml.cs:1726, `StopEngine` → `SessionRunner.Pause()`); Resume (pause button) restarts only features whose start minute has passed. Start/stop confirms use `MessageDialog`, so the buttons read OK/Cancel while the text above them is WPF's. | oracle-session-runner (§3, U3b) |
| 2026-09-29 | When does the session log count a flash image? | per scheduled batch (WPF FlashService.cs:1607); per image as it spawns | Avalonia counts each flash image as it spawns (`FlashOverlay.cs`), while WPF records the scheduled batch (FlashService.cs:1607). A burst cut short by Stop or panic therefore logs fewer images here than WPF would. | review (session-runner-wire) |
| 2026-09-29 | OAuth redirect on Linux | loopback; custom URI scheme via `.desktop` | Loopback at WPF's exact `http://localhost:{47832\|47833\|47834}/callback/`; a URI scheme would need proxy and provider changes. One Core helper, `CCP.Core/Services/Account/LoopbackOAuth.cs` (listener, CSRF state, timeout, browser pages, `/…/token` exchange), which Patreon/Discord/SubscribeStar in WPF delegate to. Windows keeps http.sys `HttpListener`. The managed `HttpListener` binds only the first address "localhost" resolves to (verified: `::1` only on this Linux host; 127.0.0.1 refused), so off Windows both loopbacks are served by `TcpListener`s with the same Host/path rules. A busy port fails at once with an `IOException` naming the port. Proof: `LoopbackOAuthTests`. Chaster's link listener (`ChasterService.Link.cs`) is not moved. | oracle-deep (account-auth plan) |
| 2026-09-29 | Where do the Avalonia head's Serilog lines go? | none (status quo: every Log.* line was lost); stderr only; stderr + WPF's file sink | stderr (CI, kc) plus a rolling file under UserData/logs (`ccp-avalonia-*.log`, Information+, 10 MB, 14 days, 1 s flush) with Serilog.Sinks.File 5.0.0, the package and version WPF already ships. WPF's path redaction, flight recorder and per-run naming come with the bug-report port. Also: after panic -> Resume, a second panic pauses again (WPF only stops ad-hoc effects there); safer, kept. | worker-deep; reviewer (file sink) |
| 2026-09-29 | Classic QuizWindow entry point: show BtnStartQuiz on Avalonia (offline quiz) or follow WPF's hidden state? | show it with offline fallback questions; follow WPF | Follow WPF: `BtnStartQuiz` and the past-quizzes list stay hidden ("pending removal", WPF GradedIntakeTabView.xaml:140, MainWindow.Lab.cs:477); the handler keeps WPF's CoreAi login gate. Showing it would add a feature the reference is removing. Proven headless only; no Keincheck (no user path). | supervisor (quiz-window-static) |
| 2026-09-29 | Which quiz features does the Avalonia head get this wave? | port AI generation/grading too; offline only | Offline only: built-in categories, fallback questions/profiles, scoring, trends and the two data files work without network (Core `QuizStore`, WPF `QuizService` statics delegate). AI quiz generation/grading, moderation-log upload and account-keyed quiz features stay stubbed on Avalonia with `ponytail:` notes naming the blocker. Unit 1 (quiz-core-pure) moved the pure half; `quiz_history.json` / `custom_quiz_categories.json` keep WPF's names, `CorePaths.UserData` location and Newtonsoft indented shape, pinned by `Tests/CCP.Core.Tests/QuizStoreTests.cs` against pre-move goldens. | oracle (oracle-quiz), supervisor |
| 2026-09-29 | Restore-session check: WPF keeps its own copy (App.xaml.cs ValidateRestoredSessionAsync) or calls the Core one now? | move WPF now (its request would gain X-Client-Version/User-Agent headers); keep two copies until auth unit 6 | Keep WPF on its own copy for this layer; auth unit 6 moves the profile load that follows it into Core and switches WPF to `V2AuthService.ValidateRestoredSessionAsync` then (every other WPF V2 call already sends those headers since v2-client-core). Temporary duplication, one unit. | supervisor |
| 2026-09-28 | Which upstream branch is "main" for the second main sync? | GitHub main (0ff4583, unchanged since the first sync); release/6.11.3 (383 commits ahead; every PR merged since 2026-09-25 lands there) | release/6.11.3: it is where the user's current work lands, and the user asked for "main's updates" to be ported. Branch avalonia-port/main-20260928b (one merge commit) + -compat. | supervisor (user request) |
| 2026-09-29 | Lock-card voice D1: which speech engine on the Avalonia head? | Vosk 0.3.38 (WPF's); whisper.cpp | Vosk 0.3.38 with WPF's model folder, grammar JSON and `Normalize`/`Similarity`/`[unk]` scoring, so both heads match the same way. Whisper transcribes freely (no closed grammar) and would add a second model. Vosk.targets picks libvosk by build-host OS and its build assets do not flow through a ProjectReference, so each runnable project references Vosk itself; no linux-arm64 native (no VR). | oracle-deep (oracle-lockcard-voice) |
| 2026-09-29 | Lock-card voice D2: Linux capture | parec subprocess; PortAudio/OpenAL | `parec --raw --format=s16le --rate=16000 --channels=1` (pw-record fallback), devices from `pactl list short sources` minus monitors. No new dependency. Lands in unit 2 (speech-linux-capture). | oracle-deep (oracle-lockcard-voice) |
| 2026-09-29 | Lock-card voice D3: Core/head split for speech | keep SpeechService in the head; move the engine to Core | Engine in Core `CCP.Core/Services/Speech/SpeechEngine.cs` (grammar session, recognizer builder, JSON parse, Normalize, Similarity, Rms, ResolveModelDir, session guard; `Log`, `CoreSettings`) behind one seam, `IMicSource` (16 kHz mono s16 PCM callback, dispose = stop, list devices). WPF `SpeechService` derives from it with an NAudio WaveIn source and keeps its statics, type and API. Proof: `Tests/CCP.Core.Tests/SpeechEngineVoskTests.cs` (real model, WAV replayed in 50 ms chunks). | oracle-deep (oracle-lockcard-voice) |
| 2026-09-29 | Lock-card voice D4: consent and model delivery | download the model; drop-in only | As WPF: voice needs `CoreSpeech.IsAvailable`, `MicConsentGiven` and the setting; audio stays in memory. No download (WPF has none). `SpeechEngine.DefaultModelRoots` searches the install folder, then `CorePaths.UserData/Models/vosk` (AppImage/Flatpak folders are read-only); WPF keeps searching only its install folder. | oracle-deep (oracle-lockcard-voice) |
| 2026-09-29 | First-run wizard "Choose a content folder": defer the picker until the wizard closes (WPF FirstRunWizard.xaml.cs:1175) or open it at once? | defer (WPF); open at once | Open at once, owned by the wizard (user-reported: the deferred click looked like it did nothing). WPF deferred only to avoid a modal-on-modal Win32 folder browser; the portal picker is not one. Same code as the shell's picker (`MainShellWindow.PickAssetsFolder`: #1053 guard, settings write, images/videos, confirmation); the button then shows the chosen folder and the post-close pick is gone. Deliberate, user-requested improvement; WPF unchanged. Proof: `FirstRunFolderPickerTests`, evidence/avalonia-port/firstrun-folder-picker. | supervisor (user report) |
| 2026-09-29 | App-level 18+ gate (Welcomed but never accepted): the WPF body says "By clicking \"Yes\"" over Yes/No buttons; this head's MessageDialog.ConfirmAsync has OK/Cancel | keep "Yes" verbatim; change the word to "OK" | "OK", so the sentence names the button actually on screen; the rest of the body, the title and the default-to-Cancel are verbatim (`MainShellWindow.AgeGateBody`, `FirstRunGateDeadClickTests`). | supervisor review |
| 2026-09-29 | Default media folder on Linux (CustomAssetsPath empty) | UserData/assets (WPF); `~/ccp media` | Linux: `~/ccp media` (images/, videos/, audio/, wallpapers/ created on first use via `CorePaths.EnsureCustomAssetsDirectories`); both pickers start there. A profile whose UserData/assets already holds files keeps it (logged; nothing moved). Windows keeps UserData/assets. The #1053 guard accepts it. Proof: `LinuxMediaDefaultTests`. | user |
| 2026-09-29 | Avatar tube placement units: WPF saved AvatarTubeLeft/Top in DIPs; Avalonia `Window.Position` is physical px | store px (new key/version); keep WPF's DIPs and convert | Keep DIPs, no new key: save `px / DesktopScaling`, restore `dip * DesktopScaling` then clamp half-on-screen (WPF ClampAvatarPosition), so a WPF-written file reads unchanged. Docking itself is px end to end, like WPF's GetWindowRect route. Restore waits for the real scaling (KWin maps first at DesktopScaling 1; `ScalingChanged` redoes it). Mixed-DPI monitors convert with the tube's current scaling, not the target monitor's. AvatarTubeScale is neither read nor written (no Ctrl+scroll zoom yet). | worker |
| 2026-09-29 | Shell Viewbox stretch when the window aspect differs from the 1585:901 design canvas | Fill always (WPF); Uniform always; Fill within a tolerance band | Shell Viewbox: Fill (WPF parity) while the window's aspect is within ±15% of the 1585:901 canvas; outside that, Uniform, top-aligned and centred horizontally, with bands in DarkerBgBrush. Reason: a user-reported portrait maximize (1113×1979 logical at 1.79 scaling) stretched everything about 2.2× vertically. WPF has the same defect; this is a deliberate improvement. Every landscape screen from 16:10 to 16:9 is unchanged from WPF. Proof: `ShellResizeMaximizeTests`. | oracle-deep |
| 2026-09-29 | Avalonia sync body | full WPF body; loaded-or-known only | Keys absent unless loaded or known. Starts as `unified_id`/`xp`/`level`/`descent_epoch`/`achievements`; fields are added per ported feature, each citing the server's merge rule. Shape is Core `SyncBody` (WPF's exact order; WPF sets `Field.All`, bytes unchanged, `SyncBodyGoldenTests`). See ~/ccp-port/briefs/oracle-sync-push.md | oracle-deep |
| 2026-09-29 | Unknown sync field | send null; omit | Omit (a `ShouldSerializeX` per `SyncBody.Known` flag). Null means "no change" only for `cosmetics`. | oracle-deep |
| 2026-09-29 | Heartbeat body | Core's `unified_id` only; WPF's four fields | WPF's four (`unified_id`, `is_active`, `in_session`, `app_version`) through Core `SyncBody.Heartbeat`; the old one-field `V2AuthServiceWireTests` golden was wrong and is fixed. | oracle-deep |
| 2026-09-29 | Where does release-content (pack download) live, and when do the Linux mod surfaces use it? | port a second copy in the Avalonia head; move to Core | Move ReleaseContentService to Core (git mv + seams: CoreSettings, Log, CoreReleaseContent.UiInvoke/AppVersion, injectable base URL/handler); WPF delegates. Packs install at CorePaths.UserData/content as WPF; WPF's rules unchanged (3× free space, ranged resume, 10 attempts, sha256 with one clean retry, journaled merge and rollback); Linux free space measured on the mount holding content/. Surfaces in order: Mod Manager rows → first-run mod step → ModPicker (HasPackService=true), superseding row 31. ContentPackService (Patreon) out of scope. `CCP_CONTENT_BASE_URL` is honoured only for a loopback http(s) host. Proof: `Tests/CCP.Core.Content.Tests` (loopback fake server). | oracle |
| 2026-09-29 | Avalonia exe name on Windows | rename `AssemblyName`; keep | Inno `DestName` renames the exe to `ConditioningControlPanel.exe` (keeps `avares://CCP.Avalonia`) | oracle-deep |
| 2026-09-29 | Upgrade in place from WPF | new AppId; same | Same AppId and folder; five-point handoff contract (tag, Setup asset name, AppId, exe name, exit 0) | oracle-deep |
| 2026-09-29 | Linux channels | AppImage; tarball | Tarball (`Properties/PublishProfiles/linux-x64.pubxml`, self-contained, not single-file) feeds Flatpak and AUR; Linux updater notifies only | user brief |
| 2026-09-29 | Updater home | Avalonia copy; Core | Core logic, head does process and download work | oracle-deep |
| 2026-09-29 | Where publish settings live | csproj; publish profiles | `CCP.Avalonia/Properties/PublishProfiles/{win-x64,linux-x64}.pubxml`; the csproj stays RID-less so test projects referencing it avoid NETSDK1151/1191 | oracle-deep |
| 2026-09-29 | Second-instance signal on Avalonia | WPF named events (Windows only); named pipe on both OSes | Same mutex name as WPF; show/ack over a named pipe, since named EventWaitHandles are unsupported on Unix | worker |
| 2026-09-29 | Linux package shape | AppImage; Flatpak bundles WPE; tarball + Flatpak + AUR | Tarball `ConditioningControlPanel/` with the Vosk model, `.desktop` and icon; app id `io.github.CodeBambi.ConditioningControlPanel`; Flatpak on GNOME 50 (has webkit2gtk-4.1, libsecret) bundles libVLC only - WebHost needs the GTK3 NativeControlHost engine, not WPE. CONFLICT: docs/avalonia-linux-install.md asks to bundle wpewebkit/wpebackend-fdo/libwpe; the Flatpak row stays blocked until the first flatpak-builder run shows whether any web surface needs WPE, then bundle it or amend the brief with the user; WM_CLASS = app id (X11PlatformOptions.WmClass) matching StartupWMClass; AUR `-bin` depends on system vlc/webkit/WPE | worker |
| 2026-09-30 | MemorySignalWriter head sources | Core writer reads App.* via one seam per service; new Core events per feature; one SourcesHook the head seeds | `SourcesHook`/`DeferredSourcesHook` (WPF seeds its original bodies verbatim); settings mirror in Core over CoreSettings; Avalonia raises `App.FeatureUsed` from its flash/subliminal/bubble surfaces and Core `MandatoryVideoScheduler.VideoStarted` (mirrors WPF VideoService.VideoStarted) | worker |

## 2026-09-29: flashing line after moving the window on KDE
- Question: the user saw a thin purple line flash where the window's top edge had been after moving it. Can the app fix it?
- Options: (a) work around it in the shell; (b) run as a native Wayland window; (c) log it as a platform limit.
- Finding: a bare 20-line undecorated Avalonia 12 window (no CCP code) shows the same line on KWin 6.7.5 over XWayland at 1.79x scaling.
- Choice: (c). Native Wayland would conflict with the X11 override-redirect overlays, so it is out of scope for this port.
- Advisor: supervisor, with the user's own reproduction.

## 2026-09-30: Entitlement lapse: startup write deferred
- Question: WPF's EnforceEntitlementLapse (MainWindow.Patreon.cs:92) runs at startup through UpdatePatreonUI and saves. `HapticSettings.Enabled` defaults to true, so every free user's first launch writes settings.json. On this head that breaks the "startup does not write settings" contract (`Tests/CCP.Avalonia.Language.Tests/LanguageSelectorTests.cs:117`).
- Options: (a) lapse only on entitlement events; (b) full parity, and change the Language test; (c) clear in memory at startup and on navigation, write on the next real save or on an entitlement event.
- Choice: (c). The flag pass (`CCP.Core/Services/EntitlementLapse.cs`) runs from the first frame, so a free user can never run a lapsed premium feature. `MainShellWindow.RefreshEntitlementVeils(persist)` saves only on a tier change, a day change or a sign-in/out, as WPF does. The effective state matches WPF; only the startup write is deferred. Proof: `Tests/CCP.Avalonia.Tests/PremiumGatesTests.cs` `LapsePass_ClearsInMemory_AndOnlyAnEntitlementEventWrites`, fail-proven both ways.
- Advisor: supervisor.

## 2026-09-29: profile cosmetics save skipped by the sync cooldown
- Question: WPF drops a cosmetics push that lands inside the 30 s sync cooldown. Keep that?
- Options: (a) drop like WPF; (b) keep it pending and send it with the next push.
- Choice: (b). The user saved on purpose; losing it silently is a WPF bug, and the pending push is cleared on logout.
- Advisor: reviewer (profile-wardrobe-live), supervisor.

## 2026-09-29: awareness consent guard tests after the lapse move to Core
- Question: moving the flag half of EnforceEntitlementLapse into Core (#1917) tripped the WPF consent guard tests, which pin the
  lapse door to MainWindow.Patreon.cs and say a move must fail loudly for re-review. Re-reviewed: is the new door still safe?
- Finding: Core EntitlementLapse is the only new writer and only ever writes false; WPF still stops the awareness engine for the
  reported key and logs it; MainWindow.Patreon.cs no longer writes the flag at all.
- Choice: retarget the three tests to the new location with the same assertions; drop MainWindow.Patreon.cs from the allow list
  (narrower than before).
- Advisor: supervisor (reviewer on #1917 accepted the Core move).

## 2026-09-29: main sync #3 from release/6.11.5
- Question: which branch does main sync #3 take? GitHub's `main` is still 0ff4583 (already merged in #1762).
- Finding: all of the user's work now lands on `release/6.11.5`, where 6.11.4 and 6.11.5 ("Locktober") landed: 185 commits ahead.
- Choice: sync from `origin/release/6.11.5` (the newest release branch). `avalonia-port/main-20260929` is one merge commit with the
  conflict resolutions and the minimal compile fixes; `avalonia-port/main-20260929-compat` ports the release's behaviour changes to
  code the stack had moved to Core: ServerClock + SyncFailureBackoff moved to Core; SyncBody signs with the server clock; SyncPush
  learns the clock, retries once on clock_skew and backs off after failures; DiscordAccount and ProviderSubscription (Patreon)
  honour DeadRefreshTokens. New WPF surfaces are ledger rows under "main-sync-3 additions".
- Advisor: supervisor.

## 2026-09-30: mandatory video keeps scheduling after an empty library
- Question: WPF's scheduled tick that finds no video returns (VideoService.ContinueTriggerVideo :2424) without ScheduleNext, so
  mandatory videos stop for the rest of the engine run. Keep that?
- Options: (a) die like WPF; (b) re-arm the normal interval.
- Choice: (b). A clip added mid-session still plays; the cost is one directory walk per interval. Core MandatoryVideoScheduler.Tick,
  test `Empty_library_starts_nothing_and_keeps_the_schedule_alive`.
- Advisor: reviewer (video-playback-core).

## 2026-09-30: strict mandatory video falls open without a live panic listener
- Question: strict mode swallows the panic key in the video window because WPF's global hook stops the video. On this head the
  X11 panic listener can be down (no X display, native Wayland, key with no keycode). Keep swallowing?
- Choice: no. As LockCardWindow #875: while the listener is not live, the panic key and Esc force-stop a strict video (no
  reschedule). With the listener live, strict swallows them and the global path stops it.
- Advisor: reviewer (video-playback-core).

## 2026-09-30: mandatory video output heal and grace card
- Question: WPF heals a white screen (no vout 8 s after Play, or a vout lost for 5 s mid-clip) by retiring the shared
  LibVLC, quarantining the player and replaying the clip once. This head decodes through video callbacks (no vout).
- Choice: the signal is frame arrival (none 8 s after Play, none for 5 s after the first frame; a clip with no video
  track plays out, as WPF). Replay the same clip once with the same strictness, on the SAME shared LibVLC (no
  retire/quarantine/4-per-session breaker), without the 1.3 s pre-roll; then end it like a dismiss. One heal budget
  per clip covers both cases (WPF: one start retry and one mid-play heal). Core `Guard`/`GuardHeals`,
  `MandatoryVideoOverlay.GuardTick`, tests `ClipGuards`, `MandatoryVideoOverlayTests`.
- The grace pause card is drawn inside each (topmost, full-screen) video window, not a separate no-activate topmost
  window with a 300 ms re-assert.
- Bubbles (Core `CoreBubbles.Pause/Resume` from `MandatoryVideoScheduler`) are held from the clip's show until the run
  ends, through verdict messages and replays, as WPF. Deviation: a ForceCleanup while the engine still runs (the
  strict fall-open panic, the Test button's force reset) resumes them too; WPF leaves them held because its panic
  always stops the engine as well.
- Advisor: reviewer (video-grace-watchdog).

## 2026-09-30: Lockdown / Emergency Exit (avalonia-port/lockdown-core)
- Question: the WPF Emergency Exit opens WebView2 exit games (EmergencyExitHostService), not portable yet. What does the slab do on Avalonia?
- Options: (A) keep the slab, fire the tripwire + Chaster hold, show the phrase steps; (B) hide the slab.
- Choice (A), rule verbatim:
  "Lockdown / Emergency Exit (Avalonia, until a native or WebView host for the exit games exists): the Emergency Exit slab stays visible and keeps the Possession off-limits name BtnEmergencyExit. Clicking it only works while a lockdown is active. It fires NotifyEscapeAttempt(EscapeKinds.EmergencyExit) and Chaster.NoteSafetyExit(), then shows a notice that states the exact secret-phrase steps (5 taps on the timer digits, then type "let me out") and the time remaining. It must never call RestartTimer() or Deactivate(). The global panic key stays ignored under Lockdown, as on WPF (LockdownDisablePanicKey). This is allowed because on WPF the timer and the phrase are the only guaranteed exits, and the Emergency Exit is a gamble. Lockdown must not ship in Avalonia unless (a) the phrase path works under StrictLock and every Lockdown overlay, (b) keys are ignored only at the global-hotkey layer, never in window text input, and (c) the recovery file restores PanicKeyEnabled and StrictLockEnabled after a kill. When the exit games are ported, bring back the WPF verdict rules exactly: the verdict is rolled on the host, a sendback restarts the full timer, and closing the window changes nothing."
- Escape paths on this head under Lockdown: timer expiry; the secret phrase; ending the process (lockdown_recovery.json restores the panic key and Strict Lock on the next start); minimize; OS shutdown (only the user's window close is refused). Refused like WPF: global panic key (MainWindow.xaml.cs:888, whatever LockdownDisablePanicKey says), Stop button and tray Stop everything (StartStop.cs:45), Exit (Launcher.cs:184), window close (WindowChrome.cs:131), session pause but never resume (LockdownPauseRule). No system-key hook exists here, so the consent dialog does not promise one. Blockers (a)-(c): `Tests/CCP.Avalonia.Tests/LockdownTests.cs`, fail-proven.
- Advisor: oracle-deep.

## 2026-09-30: Lockdown clock is UTC (deliberate improvement over WPF, both heads)
- Question: WPF LockdownService measured Remaining / ElapsedFraction / LastActiveDuration against DateTime.Now, so a DST fall-back or a clock set back lengthened a running lockdown (an hour, on the fall-back night).
- Choice: the Core LockdownService reads an injectable UTC clock (`UtcNow`, default DateTime.UtcNow), so both heads get it; a safety fix, not a parity divergence to undo. Tripwire timestamps (EscapeAttempt.At) stay local.
- Also under this rule: the Emergency Exit notice always states the real time left, even with HideLockdownTimer (the notice is the way out; hiding its clock would hide the exit).
- Tests: `LockdownTests.RemainingFollowsAMonotonicUtcClockNotTheWallClock`, `TheSlabTripsTheWireHoldsChasterAndShowsTheRealTimeEvenWhenTheTimerIsHidden`, `TheTimerRunningOutEndsTheLockdown` (fail-proven); WPF source pins in LockdownEmergencyExitTests updated.
- Advisor: reviewer (lockdown-core review).

## 2026-09-30: Awareness legacy observer applies the privacy rules on Avalonia only
- Question: WPF's legacy title observer (WindowAwarenessService, now in Core) never applied the deny list or the incognito drop; only Awareness v2 (AwarenessObserverPolicy) does, and v2 is not on this head.
- Choice: Avalonia applies AwarenessPrivacyRules (deny list incl. seeded groups + incognito) in the poll through the service's optional `allowTitle` filter (`WindowAwarenessService.PassesPrivacyRules`), before anything reads the title (logs included). A dropped window reads Unknown, raises no event and keeps nothing. WPF's legacy observer does not, and is left unchanged (constructed with no filter). Privacy-tightening deviation.
- Also: this head has no AI service, so the reaction is always the preset line WPF says with AI off; no title, app name or reaction leaves the machine.
- Tests: `Tests/CCP.Core.Tests/WindowAwarenessServiceTests.cs` (denied/incognito produce no event, fail-proven); `AwarenessConsentTests.WpfBuildsTheLegacyObserverWithoutAPrivacyFilter` (WPF source pin).
- Advisor: supervisor.

## 2026-09-30: Remote Control – escape integrity (avalonia-port/remote-control-core)
Remote Control – escape integrity. A remote controller may never remove the subject's last means of escape. 1. disable_panic is always refused and reported to the controller as refused; the panic key can be disabled only locally, through Lockdown; the consent waiver must not list 'disable panic key'. Deviation from WPF (RemoteControlService disable_panic): WPF saves the change, it outlives disconnect and restart, and the client doesn't enforce the tier. 2. While Lockdown is active, enable_strict_lock is refused; commands that only reduce restraint (stop_session, pause, trigger_panic, enable_panic, disable_strict_lock) run exactly as on WPF, don't end the Lockdown timer, and Lockdown restores the user's earlier settings when it ends.
- Advisor: oracle-deep.

## 2026-09-30: She's Listening – the mic opens with its consumer (avalonia-port/voice-commands; replaces "voice arm without consumer")
- Question: with WPF AutonomyService.VoiceCommands now in Core (`CCP.Core/Services/Speech/VoiceCommands.cs`, WPF delegating), what does arming do on Avalonia?
- Choice: exactly WPF. The wake loop / push-to-talk (`CCP.Avalonia/Views/Windows/MainShellWindow.VoiceCommands.cs`) open the existing PulseMicSource only when `VoiceInputRules.ModesToRun` allows it (consent + an armed mode + premium or the "voice" free day + an available engine), re-read at every She's Listening repaint, so Stop, revoke and an entitlement lapse close it. The hero is WPF's green "She's listening / The mic is open". Actions are limited to what this head can run (intents without a seam are left out of the grammar); confirmations are the Core text packs in the tube (no bark voice lines on this head); no spoken-mantra fallback (MantraVoiceService not ported, the Test button says `sl_mantras_not_on_this_build`). Nothing leaves the machine: WPF sends no voice text anywhere, and neither does this head.
- Test: `Tests/CCP.Avalonia.Tests/VoiceCommandsTests.cs`, `Tests/CCP.Avalonia.Tests/SheListeningTests.cs`, `Tests/CCP.Core.Tests/VoiceCommandsTests.cs` (fail-proven).
- Advisor: supervisor.

## 2026-09-30: Panic ↔ mic (avalonia-port/voice-commands)
Panic ↔ mic: panic aborts in-flight speech capture/command chain (SpeechEngine cancel) but leaves the wake-word loop and PTT armed; mic teardown stays with She's Listening, the privacy pill and shutdown. Rationale: under Lockdown the spoken 'panic' is the only repeatable in-app exit; disarming the mic would violate escape integrity. Voice 'panic' stays unguarded by Lockdown/StrictLock (WPF parity, VoiceCommands.cs:108-109); only the global panic hotkey is suppressed under Lockdown.
- Test: `Tests/CCP.Avalonia.Tests/VoiceCommandsTests.cs` (PanicAbortsTheCaptureButTheLoopStaysArmed, TheSpokenSafeWordWorksUnderLockdown; fail-proven).
- Advisor: oracle-deep.

## 2026-09-30: Panic vs Takeover (avalonia-port/autonomy-core)
Panic / tray 'Stop everything' vs Takeover: Panic stops Takeover (autonomy stays off until the user starts it again). The saved AutonomyModeEnabled switch is not changed. This matches WPF's real behaviour: every WPF panic path runs App.KillAllAudio() -> Autonomy.Stop(), and does so before RunPanicStopTail samples autonomyWasRunning, so its 'restart autonomy after skipping current action' branch never runs. Lockdown still ignores panic.
- Test: `Tests/CCP.Avalonia.Tests/TakeoverTests.cs`.
- Advisor: oracle-deep.

## 2026-09-30: webcam capture and face tracking on Avalonia
- Question: which capture stack for webcam tracking on the Avalonia head (Linux-first, also Windows)?
- Options: (a) LibVLC v4l2 callbacks (already bundled) + OnnxRuntime without OpenCV; (b) OpenCvSharp4 on both heads.
- Choice: (b). OpenCvSharp4 4.9.x + OpenCvSharp4.official.runtime.linux-x64 (V4L2) on Linux, OpenCvSharp4.runtime.win on Windows;
  Microsoft.ML.OnnxRuntime 1.20.x CPU natives on both. Frames stay in memory, never on disk, never on the network. Flatpak uses
  --device=all for now (the PipeWire camera portal is deferred: it needs a GStreamer pipewiresrc source). Native floor glibc >= 2.35
  (Ubuntu 22.04 build); if OpenCV fails to load, the feature is disabled with a message, never a crash. The LibVLC v4l2 source is a
  documented fallback behind an IFrameSource seam. Check ldd of libOpenCvSharpExtern.so before shipping.
- Advisor: oracle.

## 2026-09-30: what drives the Avalonia Inbox
- Question: the brief said the title-bar Inbox is driven by the friends Core service (slice 1). It is not: WPF's badge and
  InboxFlyout bind App.StartupLadder (Services/Startup/StartupPresenter.cs), whose rows are parked by the quiet-window rule.
- Options: (a) move the whole StartupPresenter (modal ladder, pump, quiet watch) to Core; (b) move only InboxItem + StartupQueueCore
  (pure) and extract the Inbox half into Core StartupInbox, WPF's presenter delegating; the head gets a small passive-route presenter.
- Choice: (b) (supervisor). Platform.StartupLadder routes through StartupQueueCore.Route with session/tour/first-launch inputs;
  programs intro and wardrobe toasts post to it. The modal ladder and the other WPF posters stay out; listed on shell-inbox.
## 2026-09-30: Blink Trainer session on Avalonia (webcam slice 3)
- Question: how much of BlinkTrainerService/calibration fits this slice, and where does the session live?
- Choice: the session is head code (`Views/Overlays/BlinkTrainerSession.cs`, overlay windows are head-only); the pure parts
  moved to Core (`BlinkTrainerAssetPool` by git mv, `BlinkTrainerState.TileGrid`, WPF delegates). Overlays use the pink-filter
  refusals (click-through + transparency or nothing). The start goes through `StartEffect` so the Wayland panic shortcut is
  bound first; the session `Generation` is read before the tracker start is awaited, so a Stop/panic during the tracker start
  or the pending bind cancels it. Panic, the shell's Closing (so a close cancelled to the tray too, as WPF LabTab.cs:1000),
  app exit and consent revoke stop the session. Calibration, quick recal and tracker test are NOT in this slice: all three need the gaze-projection feed
  (WPF WebcamCalibrationWindow ~2.1k LOC + gaze maths) that WebcamTracker does not emit; their buttons stay disabled with a
  reason. `CoreWebcam.IsAvailable` is seeded true, and revoke keeps all four promises in one place (`WebcamTracker.RevokeConsent`: stop, delete the calibration file via
  `WebcamCalibrationData.DeleteIfExists`, clear consent, turn the webcam features off).
  Deviations: GIF/animated webp show their first frame; mix mode buckets only already-seen images; no explicit tracking-monitor
  pick (placement = DualMonitorEnabled ? all : primary); no stage video preview; no SeasonRecap credit.
- Advisor: supervisor (progress update), worker.
## 2026-09-30: one network guard for every CCP_USERDATA_DIR sandbox
- Question: ~15 clients (BugReportService, DescentCountdownService, QuestDefinitionService, LeaderboardClient, V2AuthService,
  ProviderSubscription, SyncPush, LoginDialog, FriendsApi, ServerClock, Marquee, AnnouncementPopup, UsernamePicker, hypnotube
  HtMetadataFetcher/EnhancementFetcher, the app.cclabs.app spiral embed) had no sandbox rule, so tests/kc/render-all could reach
  production. Per-client rules (Catalogue, Remote, AI, AppUpdater, Chaster, DailyFree, ReleaseContent) do not scale.
- Choice: Core `SandboxNet` (CCP.Core/Services/SandboxNet.cs), installed by `CorePaths` whenever it honours CCP_USERDATA_DIR
  (outside any catch: a failed guard fails loudly). It sets `HttpClient.DefaultProxy` (and `WebRequest.DefaultWebProxy`) to a proxy
  at 127.0.0.1:0 (nothing can listen on port 0; a bound-but-idle port could be taken over via SO_REUSEADDR/SO_REUSEPORT);
  only literal loopback IPs and "localhost" as typed bypass it (not .NET's rewritten "loopback"). Every HttpClient / HttpClientHandler /
  SocketsHttpHandler (incl. UrlSafety's ConnectCallback handler, ServerClockHandler) and ClientWebSocket therefore gets
  "connection refused" before any DNS or connect to the real host (UrlSafety's DNS pre-flight is skipped in a sandbox); honoured LoopbackUrl overrides still work. Re-grep found no
  product code that sets UseProxy or its own Proxy; `SandboxNetTests.NoProductCodeOptsOutOfTheDefaultProxy` fails if one appears.
  Non-HTTP egress asks `SandboxNet.Allows(uri)` (loopback, non-UNC file:, about:, data: only in a sandbox; ExternalOpener also refuses UNC paths there and leaves production launches to the shell as before): Avalonia `WebHost` (source and
  every navigation), and every link/file/folder launch in the head goes through `Platform/ExternalOpener` (refusals logged;
  HyperlinkButton NavigateUri via `Controls/SafeHyperlinkButton`); `SandboxNetTests.EveryLaunchGoesThroughExternalOpener` fails on a bypass. LibVLC plays FromPath only (no network MRLs). Production
  (no CCP_USERDATA_DIR) never installs it.
- Not covered: the WPF head's own launch sites; WebView subresources of an allowed local page.
- Advisor: worker (brief sandbox-net-guard).

## 2026-09-30: panic and the camera (Avalonia only, deliberate WPF deviation)
- Question: should a panic press stop webcam tracking? WPF leaves the camera running.
- Choice (C): every panic press, including Lock Card presses that do not advance the exit ladder and a press consumed as a video
  grace pause, stops tracking after the audio and overlay teardown, fire-and-forget (Stop can block up to 5 s, so never awaited
  on the panic path). Consent, calibration, device choice and settings are kept; status chips follow the tracker's StateChanged.
  The notice "Camera stopped. Start tracking to resume." (`panic_camera_stopped`, all languages) shows when a camera was on or
  starting. The Blink Trainer session stops with it, and the tracker's stop generation keeps an in-flight Start from publishing
  its camera afterwards. A palette-claimed Escape is not a panic (PanicPolicy.DismissSettingsPalette) and does not stop it.
- Rationale: Panic is the get-me-out control; a camera left running is the most visible privacy leak; attention checks skip when
  tracking is off (AttentionCheckService.cs:247), session/autonomy don't depend on the webcam, no StrictLock/Lockdown escape rule
  uses gaze; cost is a manual restart, made expected by the notice.
- Advisor: oracle-deep.
## 2026-09-30: Chaster booked figure on Avalonia
- Question: WPF shows a booked price first as ChasterBookedPop (a topmost window at the cause or cursor) and only falls back to
  the rail-padlock adorner. Which does the Avalonia head show?
- Options: (a) port the pop window (desktop-wide topmost, cursor from Win32; Bucket E, not permitted on Wayland); (b) always the
  rail adorner, WPF's own fallback.
- Choice: (b) for now; the pop stays `missing` in the ledger. BookedFlashPlan moved to Core with colours as 0xAARRGGBB `uint`
  (WPF converts with `BookedFlashColour.Wpf()`), so both heads draw one plan.
- Advisor: none (worker, per branch brief avalonia-port/chaster-bill).
## 2026-09-30: Mantra Lab audio and opener (avalonia-port/mantra-service)
MantraService is in Core (git mv; App.Progression/App.Quests/App.Chaster -> `CoreProgression.AddXP("Mantra")`, new `CoreProgression.TrackMantraCompletedProvider`, `MantraService.ChasterNote`, seeded by both heads). WPF's NAudio SignalGenerators become synthesised 16-bit WAVs (`CCP.Avalonia/Platform/ToneWav.cs`, per-process temp dir): tones through `CoreAudio.PlayOneShot` at WPF's 0.15 gain; the drone (90 Hz + 0.4 x 180 Hz, 10 s of whole cycles) as a looping `LayeredAudio.VlcLayerPlayer` whose volume follows WPF's gain ramp x MantraDroneVolume. No NAudio on Linux. `MainShellWindow.StartMantraSession` is ported with no caller, exactly as WPF (MainWindow.PlayTab.cs:~264; the Mantras card left the Play page 2026-08-12); where the game lives is still an owner call. Its WPF failure MessageBox is dropped on this head (logged only). The drone player starts muted and unmutes once its first volume sticks (no full-volume blip); exit closes the window and deletes the temp WAVs.
- Test: `Tests/CCP.Core.Tests/MantraServiceTests.cs`, `Tests/CCP.Avalonia.Tests/MantraWindowSessionTests.cs` (fail-proven x7).
- Advisor: none (worker).
## 2026-10-01: WebHost live URL and the catalogue lookup trigger
- Question: where does the Avalonia head fire the HT catalogue lookup, and with which URL?
- Choice: WebHost gains CurrentUrl + NavigationCompleted (from NativeWebView.NavigationCompleted, raised for failed
  completions too, like WPF BrowserService.cs:1435-1443); the shell's OnBrowserNavigationCompleted sets the status line and
  fires the lookup with the live URL (Uri.AbsoluteUri, escaped like CoreWebView2.Source). The lookup no longer fires from
  NavigateBrowser with the requested URL, so with no web engine nothing is looked up, as on WPF with no browser.
  Headless tests drive the internal WebHost.OnNavigationCompleted seam. IsBrowserShowingKnownSite stays unwritten until
  its caller SyncSiteRadiosToActiveMod is ported. DashboardFold reads no URL, so it is untouched.

## 2026-10-01: sandbox rule for the catalogue lookup; clients still outside it (known gap)
- CatalogueLookup (by-ht-url lookup and bundle download) now resolves its base URL through CatalogueClient.ResolveBaseUrl:
  loopback CCP_CATALOGUE_BASE_URL only; a CCP_USERDATA_DIR sandbox without one sends nothing; under a loopback override a
  non-loopback bundle FileUrl is refused (`CatalogueLookupTests.SandboxWithoutAnOverrideSendsNothingAndALoopbackOverrideIsWhereItGoes`).
- Known gap, a separate branch will add a shared HTTP-layer guard. These still reach real servers from a sandbox:
  CCP.Core/Services/BugReportService.cs:24; CCP.Core/Services/Descent/DescentCountdownService.cs:101;
  CCP.Core/Services/Progression/QuestDefinitionService.cs:20; CCP.Core/Services/Progression/LeaderboardClient.cs:17;
  CCP.Core/Services/Account/V2AuthService.cs:30; CCP.Core/Services/Account/ProviderSubscription.cs:23;
  CCP.Core/Services/Account/SyncPush.cs:30; CCP.Core/Services/Account/DiscordAccount.cs:53 (ProviderSubscription.ProxyBaseUrl);
  CCP.Avalonia/Views/Dialogs/LoginDialog.axaml.cs:58; CCP.Avalonia/Views/Dialogs/UsernamePickerDialog.axaml.cs:49;
  web views loading fixed hosts (SpiralTabView.axaml.cs:85 embed, MainShellWindow.Browser.cs:77-78 site homes,
  MainShellWindow.TabNavigation.cs:346).
  Not audited (HTTP with a caller-supplied URL, so the rule depends on the caller): FriendsApi, ServerClock, HtMetadataFetcher,
  EnhancementFetcher, GoonContracts, ChasterLadderApi, AnnouncementPopup, MainShellWindow.Marquee, EnhancementPlayerWindow.
  Already under the rule: CatalogueClient, CatalogueLookup, RemoteRelay, AiService, ReleaseContentService, DailyFreeService,
  AppUpdater, ChasterHead.
## 2026-09-30: EmiDesk ring/codex/book slice (emidesk-ring)
- Question: which of ring, codex, book, options, summon count/placement fits one layer honestly?
- Choice: the pure data half of WPF `EmiCodex` (chapter models, fail-soft `Read(dir)`, bookmark) moved to Core
  `EmiCodexChapters` (WPF delegates; the WebView2 host stays head-side); the Avalonia plain reader drops its placeholder
  chapters and reads the shipped `Assets/web/codex/chapters` (linked as Content). Summon counts through `EmiState.NoteSummon`
  and placement round-trips through EmiState; the monitor key is `Screen.DisplayName`, or the screen's bounds when the
  platform names none (headless, some X11), since WinForms DeviceName has no Avalonia twin.
- Not in this slice: ring opener (needs EmiTargets openers + EmiSuggester composition, WPF head), codex opener (only the
  bookOffer moment opens it in WPF), book demos/cards, options' global click-away, `RefreshOutfit` (Arcademy outfit store is
  head-only) and `StopPresentation` (no presentation mode on this head). Ledger rows stay stub.
- Advisor: worker.
## 2026-09-30: Launcher slice 1 (avalonia-port/launcher-core)
- Question: the WPF launcher's cards are games whose hosts (Back Room, Breakout, Piece by Piece, Racing, DtRH, Arcademy, Goon)
  do not exist on Avalonia. Draw them, or not?
- Choice: the card table (ids, order, art, glyph, hue, account/new flags) moved to Core `LauncherCards`, with the close/Play rules
  in `LauncherRules`; WPF `LauncherCatalogue`/`LauncherHost` build and act on them unchanged. The Avalonia `LauncherWindow` draws
  only cards with a destination on this head (`LauncherWindow.Destinations`: today only Graded Intake, a panel tab); a game card
  appears when its host lands. Nothing is faked. The launcher is reached from the shell's title-bar "CC Labs" door (WPF
  `BtnBackToLauncher`); the boot surface, skip-to-panel box and tray row are slice 2 (~/ccp-port/briefs/launcher-plan.md).
- Deviation: WPF hides the launcher behind the panel's tray icon when something still runs. A Linux desktop may have no tray host,
  so when the panel is hidden and no StatusNotifierWatcher is present the close button minimizes instead of hiding, keeping a way
  back. A user close is routed through the rule; a `Close()` from code (shutdown, tests) goes through.
- Closing the shell closes the launcher (LauncherWindow.BackToLauncher hooks the panel's Closed), so a hidden launcher never keeps a trayless process alive.
- Test: `Tests/CCP.Core.Tests/LauncherRulesTests.cs`, `Tests/CCP.Avalonia.Tests/LauncherWindowTests.cs` (each behaviour fail-proven by a deliberate break).
- WindowControlButton/WindowCloseButton hoisted from MainShellWindow.axaml into Theme/Styles.xaml (its ponytail note: second caller).
- Advisor: none (worker, per branch brief).

## graded-intake: IntakePassService in Core takes its entitlement hooks from the head
`IntakePassService` moved to Core unchanged except for one seam: `AttachEntitlementSources()` read
`App.Patreon`/`App.SubscribeStar` directly, so it now takes `attach`/`detach` delegates and each head
passes its two providers' `TierChanged` (WPF App.xaml.cs; Avalonia `AccountSeed.Seed()`, the point where its
providers exist). A second call re-points the hook at the new providers instead of being a no-op.
Behaviour (refund, re-raise, Dispose detaching) is unchanged. The Avalonia gate lives on
`GradedIntakeTabView` rather than the shell, because this head's shell cannot reach x:Name fields.
## 2026-10-01: webcam gaze on Avalonia (webcam slice 4)
- Question: how does the gaze half of WebcamTrackingService reach the Avalonia tracker without a second copy, and what fits one slice?
- Choice: the stateful frame-free chain moved to Core `GazeEngine` (one instance per tracker, capture thread only) and WPF's
  ProcessFrame/EmitGazeEvents/HandleNoFace delegate to it, keeping WPF-only stages (gaze lock-on, long stare, mouth/tongue,
  pre-lock snapshot) in place and the call order unchanged. solvePnP stays in each head: Core compiles against OpenCvSharp 4.9
  (WPF's version) and 4.13 on the Avalonia head renamed `SolvePnPFlags` to `SolvePnPMethod`, so a Core call throws
  MissingMethodException there (found by `WebcamGazeTests.HeadPose_TracksAYawTurn`); Core owns the model points, Euler
  extraction and smoothing. Calibration is WPF's profile file (`WebcamCalibrationData.FilePath`), read once on first use;
  revoke deletes it and drops the in-memory copy. Tracker Test and Quick Recal are ported; the 16-point calibration window
  (WPF ~2.1k LOC plus mouth/tongue validation and the bubble test) does not fit the line cap and is the next slice, so
  Calibrate stays disabled with a reason.
- Advisor: worker (supervisor informed).

## 2026-10-01: Graded Intake host slice 1 (avalonia-port/intake-host)
- Question: WPF `IntakeHostService` runs the intake page in WebView2 with a `ccp.game` virtual host and the
  `chrome.webview` bridge. Avalonia's `NativeWebView` (WebKitGTK here) runs scripts but has neither, and the page's
  `web-shim.js` knows only `chrome.webview` and `ReactNativeWebView`.
- Choice: the run's rules (result/walk-out latches, heartbeat timeout, grade + 90% top marks, XP/mantra caps, pass
  spent ONLY by a parsed quiz-result and before the draft, collision-safe session path, PNG check, same-document
  check) moved to Core `IntakeRun`; WPF `IntakeHostService` delegates (latches, heartbeat stamp + 20 s silence rule,
  grading, completion; its DispatcherTimer and its own ChaosWebViewHost.SameDocument stay head-side). A failed XP grant
  still skips the mantra credit: `CoreProgression.AddXP` now returns false when the provider throws.
  The Avalonia host drops page messages unless the web view's last completed navigation is the document it loaded
  (`NativeWebView`'s message args carry no source, unlike WebView2's). Bridge seam: page -> host over the engine-injected
  `window.invokeCSharpAction(string)`, which `web-shim.js` now takes as a third string carrier, only when
  `chrome.webview` is absent (the shim's text changed; WPF behaviour did not:
  `IntakeRunTests.WebShimKeepsWebView2FirstAndGatesTheAvaloniaCarrier`, node probe in
  ~/ccp-port/evidence/avalonia-port/intake-host/web-shim-carrier-probe.txt); host -> page via `InvokeScriptAsync`
  calling the shim's existing `window.__ccpRnPush(json)`. `WebHost` passes `WebMessageReceived` through as `WebMessage`.
- Deferred (supervisor): Begin Intake does NOT open `IntakeHostWindow` yet - nothing serves the page on this head, so
  the window would be dead UI. Serving (loopback static server or WebResourceRequested) and shipping Assets/web in
  CCP.Avalonia.csproj are slice 2 and get their own oracle decision, since that serving is shared with Chaos/DtRH/Spiral.
  Plan: ~/ccp-port/briefs/intake-plan.md.
- Advisor: supervisor (need_decision).

## 2026-10-01: Launcher slice 2 (avalonia-port/launcher-boot)
- Boot surface: WPF builds the panel cloaked (DWM) so its Loaded work runs, then hides it. X11 has no cloak; the Avalonia panel is
  shown with ShowActivated=false and ShowInTaskbar=false and hidden from its Opened handler, before a frame is presented, then
  `LauncherWindow.RouteBoot` opens the launcher (or a game's destination; a game with no host here falls back to the tiles).
  Welcomed is read before the shell's constructor claims it, which equals WPF's `Welcomed && !FirstRunClaimedThisLaunch`.
- Second-instance handoff: WPF writes "surface\n<payload>" to its "Open with CCP" handoff file before signalling. This head has
  no handoff file, so the payload rides the existing single-instance pipe (length byte + UTF-8). A bare relaunch now follows WPF
  LauncherHost.OnBareRelaunch (panel visible -> raise it; else skip-to-panel ? panel : launcher) instead of always raising the panel.
- Known limit: .NET named mutexes on Unix are scoped to the login session, so a second launch from another session (setsid,
  another TTY) runs as its own instance. Pre-existing; found while proving the handoff live.

## 2026-10-02: EmiDesk ring, slice 2 (avalonia-port/emidesk-ring-2)
- Catalogue split: the door table (id, art, hue, order) is Core `EmiDoors`; each head's `EmiTargets` answers only "available,
  locked, open" per id through `EmiDoors.Build`. A null answer hides the door (no surface on this head), never a fake opener.
  `EmiTarget` moved to Core with `Hue` as 0xRRGGBB bits (heads convert at the brush) and without the unread `Gate` field.
- `EmiSuggester` is a git mv into Core; `Compose` now takes the head's catalogue. WPF passes `EmiTargets.All`, behaviour unchanged.
- Options click-away/Escape: polled (XQueryPointer/XQueryKeymap, 30 ms) rather than an X pointer grab, because a grab would
  swallow the click and WPF's hook let it through. Wayland-native windows stay invisible to it.
- Not taken: the Codex opener (needs the bookOffer moment bus), the book's demos/tours, scoring tab/rack opens outside the ring.
