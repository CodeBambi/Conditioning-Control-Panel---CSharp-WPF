# Main sync #4 delta ledger (2026-10-05)

Branch `avalonia-port/main-20261005`, from stack tip `avalonia-port/spoken-mantras` (4481a44c6).
Pin: `origin/main` = `4629b1f4a` (merge of release/7.0.5; releases 7.0.0-7.0.5 "Stay Tuned").
Last sync merge-base: `7bbf78c06`. Delta: 210 commits (137 non-merge), 361 files.

Every non-merge commit has one row. Status legend:

- **ported-by-merge**: the change landed in a file both heads use (a `CCP.Core` file the stack moved, the language files, or `Assets/web`, which the Avalonia `WebAssetServer` ships through the `..\Assets\web\**` wildcard).
- **needs-port (`lane`)**: WPF-only behaviour the Avalonia head lacks; the lane name is the port lane that owns it. Tick the row when that lane lands.
- **n/a**: no Avalonia counterpart by design (installer/version bump, WPF-only test, Win32 hook or Windows shortcut), reason given.

Counts: 47 ported-by-merge, 79 needs-port, 11 n/a (total 137).

Web-only rows for Piece by Piece (chess) reach Avalonia users only once the head hosts the game; that host is the existing `feat-pbp-*` gap in `avalonia-parity.md`, tracked as lane `chess-host`.

## needs-port lanes

| lane | rows |
|---|---|
| `shell-layout` | `bc8fcca56` |
| `copy-7.0` | `d9921236d` |
| `game-hosts-escape` | `784229973` |
| `side-art` | `6160ff5d5` |
| `flash-7.0.3` | `0e7b2517a`, `ec0ad296a`, `785bd5c69` |
| `leash` | `01254bde9`, `a80863a9a` |
| `startup-queue` | `8cbcbea01` |
| `chaster-tab-7.0.x` | `34cd41ba1`, `2b7d742d0`, `c419d6201`, `cd9bee118`, `61a331c1d`, `c8dead5b3`, `54d020e60` |
| `goon-host` | `f78b192ec` |
| `awareness-7.0.3` | `c9ad99c04`, `209659be8`, `7e337d085` |
| `chess-host` | `f8f16cc8b` |
| `companion-7.0.3` | `9dfccda39`, `e29358238`, `ec04483c4`, `6da86e2d2`, `0ec3119c4` |
| `browser` | `daf734417` |
| `quests` | `bfd5a2d22` |
| `presets` | `97481fc56`, `03af6e8bb` |
| `lobby` | `ce912bb12`, `20f1ea3ed`, `342123f83`, `239c1ae6e`, `53b81238c`, `b5eff6956` |
| `invites` | `6f5e76610`, `ecb8ace7e`, `9c799d758`, `9c2c5bb6b`, `1200f1723`, `e2d4e35ef`, `f1022c95d`, `8bd6402cf` |
| `vault-gate` | `fbe161de2`, `c8fff36a0`, `a39bbfc75`, `99f48bc22`, `a88a74569`, `d9a6d972f` |
| `nav-back` | `eb6ccc404` |
| `exclusives` | `2e9080399`, `bf57cecdf` |
| `remote-v2` | `71cfc4185`, `add72ae95`, `719ed9ca5`, `7b22ece8c`, `d39969827`, `5a44642cf`, `1fc87b08d`, `fba7a3531`, `bc67fe306` |
| `programs` | `608ff3181` |
| `deeper` | `04807471a`, `57bde0329` |
| `bugreport` | `bfe45f6db` |
| `brain-drain-7.0.3` | `0fc2d4faa`, `97303aff4`, `a464a897c` |
| `mercy-more-fold` | `8fea9bc0a`, `c0aa628b8`, `745daca00` |
| `lockcard` | `dda21a45a` |
| `overlay-spiral` | `7e051b926` |
| `gaze` | `6c034816d` |
| `fyp` | `c57156b49` |
| `video-7.0.3` | `17dade8e5`, `854ac954a` |

## Rows

| # | sha | subject | WPF files | Avalonia / Core counterpart | status | notes |
|---|---|---|---|---|---|---|
| 1 | `cb56a30a3` | fix(arcademy): unlock Annex at 75 percent and stop hidden camera work | assets: 7; tests: 1 | Assets/web/arcademy (WebAssetServer wildcard) | ported-by-merge | Annex JS/CSS lands in Assets/web; JS test Tests/arcademy n/a |
| 2 | `bc8fcca56` | fix(window): re-sync the panel's layout when the frame outgrows it | `MainWindow.WorkAreaFit.cs`, `MainWindow.xaml.cs`, `LayoutDrift.cs`; tests: 1 | CCP.Avalonia/Views/Windows/MainShellWindow (WorkAreaFit) | needs-port (`shell-layout`) | WPF LayoutDrift re-sync; check whether the Avalonia shell can drift the same way |
| 3 | `a56eeca62` | feat(chess): five computer levels on the menu and a thinking pause | assets: 6 | Assets/web/piecebypiece | ported-by-merge | web-only; chess host itself is the existing feat-pbp-* gap |
| 4 | `d9921236d` | copy: retire the word season from what the app says | `App.xaml.cs`, `SeasonRecapWindow.xaml`, `MainWindow.Marquee.cs`; languages; Core: `SkillTree.cs` | Core strings merged; SeasonRecap/Marquee views | needs-port (`copy-7.0`) | strings by merge; WPF App/SeasonRecapWindow/Marquee wording to mirror |
| 5 | `784229973` | fix(escape): Breakout and the race keep a first Escape as their pause, focused or not | `MainWindow.xaml.cs`, `BackRoomHostService.cs`, `CaucusHostService.cs`; Core: `PanicPolicy.cs`; assets: 6; tests: 1 | Core PanicPolicy merged; Avalonia backroom host | needs-port (`game-hosts-escape`) | PanicPolicy by merge; BackRoom/Caucus host Escape routing is WPF-only |
| 6 | `6160ff5d5` | fix(layout): side art cannot flip wide and compact in a loop (#1321) | `AdaptiveSideArt.cs`; tests: 1 | CCP.Avalonia AdaptiveSideArt users | needs-port (`side-art`) | flip-loop guard in WPF Features/AdaptiveSideArt.cs |
| 7 | `0e7b2517a` | fix(flash): shuffle bag so a 1000-file pool actually gets used | `FlashService.cs`, `ShuffleBag.cs`; tests: 1 | CCP.Avalonia/Views/Overlays/FlashOverlay.cs | ported (avalonia-port/port-fx) | ShuffleBag moved to Core; FlashOverlay.NextPath deals from it; `FlashPickTests.A_relisted_folder_is_walked_in_full_before_anything_repeats` |
| 8 | `1bde56f87` | copy: no dashes in the language files, neutral ru, one name for the Back Room wallet | `ChasterTabView.Ladder.cs`; languages; Core: `ChasterRaffle.cs`; assets: 8; tests: 1 | Core Languages + Assets/web | ported-by-merge | copy only; ChasterTabView.Ladder wording folds into chaster-tab-7.0.x |
| 9 | `caab54f4f` | fix(raffle): a credit on a day with no push counts on the day the rest lands (TAB-12) | Core: `ChasterLadder.cs`; tests: 1 | CCP.Core/Services/Chaster/ChasterLadder.cs | ported-by-merge | Core file, auto-merged |
| 10 | `01254bde9` | fix(leash): the panic key reaches the leash with panic off or under Lockdown | `MainWindow.Awareness.cs`, `MainWindow.Lab.cs`, `MainWindow.Leash.cs`, `MainWindow.Patreon.cs` +3 | none yet (Leash not ported) | needs-port (`leash`) | panic key -> leash; carry into the Leash port |
| 11 | `d4aee30f7` | fix(chess): a window closed mid-match spends the stake pick | assets: 3 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 12 | `8cbcbea01` | fix(startup): cards wait behind the launcher and never stack | `App.xaml.cs`, `StartupPresenter.cs`, `AnnouncementPopup.xaml.cs`, `FeatureIntroPopup.xaml.cs`; Core: `InboxItem.cs`, `StartupQueueCore.cs`; tests: 1 | Core StartupQueueCore/InboxItem merged; Avalonia startup presenter | needs-port (`startup-queue`) | Core part by merge; WPF StartupPresenter/popups wait behind launcher |
| 13 | `34cd41ba1` | fix(tab): Use Locktober and Lost stake fit at 1563x943 | `ChasterTabView.xaml`; languages | CCP.Avalonia ChasterTabView | needs-port (`chaster-tab-7.0.x`) | layout fit at 1563x943 + strings (strings merged) |
| 14 | `ab7f8c167` | fix(backroom): The Annex blade sign reads the right way from both sides | assets: 2 | Assets/web/backroom | ported-by-merge | web-only |
| 15 | `65b55d983` | fix(breakout): Escape on any pause card leaves, like chess (owner call) | assets: 2 | Assets/web/backroom | ported-by-merge | web-only |
| 16 | `a80863a9a` | fix(leash): panic off also stops the session the leash started | `ILeashTaskRunner.cs`, `MainWindow.Leash.cs`, `AppLeashTaskHost.cs`, `LeashTaskRunner.cs`; tests: 1 | none yet (Leash not ported) | needs-port (`leash`) | panic off stops leash session |
| 17 | `00d3a1a27` | fix(breakout): a blur that comes straight back does not pause | assets: 1 | Assets/web/backroom | ported-by-merge | web-only |
| 18 | `94f3a02d5` | release: 7.0.0 "Stay Tuned" | `ConditioningControlPanel.csproj`, `MainWindow.xaml`, `UpdateService.cs`; languages; `build-installer.bat`, `installer.iss` | - | n/a | release bump: WPF csproj version, installer, release notes; strings merged |
| 19 | `f58c3fe62` | release: 7.0.1 "Stay Tuned" | `ConditioningControlPanel.csproj`, `MainWindow.xaml`, `UpdateService.cs`; languages; `build-installer.bat`, `installer.iss` | - | n/a | release bump: WPF csproj version, installer, release notes; strings merged |
| 20 | `f78b192ec` | fix(goon): paint-stall watchdog ignores a stalled UI thread (ccp-bugs #1326) | `GoonHostService.cs`; tests: 1 | CCP.Avalonia GoonTestWindow / goon host | needs-port (`goon-host`) | paint-stall watchdog is WebView2 host logic; check Avalonia goon host |
| 21 | `3d7278b94` | fix(ai): a named video that does not resolve plays nothing (ccp-bugs #1325) | Core: `MediaCommand.cs`; tests: 1 | CCP.Core/Services/Commands/MediaCommand.cs | ported-by-merge | Core file, merged |
| 22 | `c9ad99c04` | fix(awareness): side art reads the window width, privacy card stops rebuilding every 1.5 s (#1321, #1323) | `AwarenessPrivacyRuntimeVm.cs`, `AwarenessTabView.xaml`, `SheListeningTabView.xaml`; tests: 2 | CCP.Avalonia AwarenessTabView / privacy VM | needs-port (`awareness-7.0.3`) | side art width + privacy card refill |
| 23 | `b3d1eddf3` | release: 7.0.1 notes | `UpdateService.cs` | - | n/a | WPF release notes text (UpdateService) |
| 24 | `3dfe2a744` | feat(chess): open tables - scrollable lobby with who is waiting and who is playing | assets: 8 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 25 | `501b4e685` | fix(chess): Distraction meter reads empty in Firefox, not full | assets: 1 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 26 | `ad36cc9d5` | feat(web): shared picture picker and niche manager | assets: 2 | Assets/web/backroom/shared/niches | ported-by-merge | web-only (new files placed under Assets/web) |
| 27 | `f8f16cc8b` | feat(chess): ask for pictures once at the first start, then keep the choice | `PbpMediaRules.cs`, `PieceByPieceHostService.Media.cs`, `PieceByPieceHostService.cs`; Core: `AppSettings.cs`; assets: 7; tests: 1 | Core AppSettings merged; none yet (PieceByPiece host not ported) | needs-port (`chess-host`) | PbpMediaRules / host Media: existing feat-pbp-media-friends gap |
| 28 | `68f5a6a5f` | feat(chess): smaller, shorter turn card with its own sound, and more crowd reactions | assets: 6 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 29 | `9dfccda39` | fix(companion): a switched-off companion can be turned back on inside the app | `MainWindow.Companion.cs`, `MainWindow.CompanionRoom.cs`, `MainWindow.Patreon.cs`, `AutonomyService.cs` +4; languages; tests: 2 | CCP.Avalonia companion views | needs-port (`companion-7.0.3`) | strings merged; on-switch in WPF MainWindow.Companion/AutonomyService |
| 30 | `727119895` | feat(chess): grade every local move and keep an IQ score that only drains | assets: 6 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 31 | `64db5a0df` | feat(chess): the screen climbs while you think and snaps down when you move | assets: 7 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 32 | `5003c577f` | feat(chess): show each local player's IQ on their card, and what a move cost | assets: 2 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 33 | `94c5c4fd9` | feat(chess): the fall - IQ recap on the end card, lowest IQ on the profile | assets: 5 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 34 | `7ba46ef7f` | fix(chess): the fall smoke spells its dash check in escapes, not dashes | assets: 1 | Assets/web/piecebypiece | ported-by-merge | web-only (smoke) |
| 35 | `d77f1abe9` | fix(chess): the fall reads the grader's record as it is | assets: 3 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 36 | `6bc32c2bc` | feat(chess): the fall - watch it again, save it, copy it | assets: 6 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 37 | `daf734417` | fix(browser): an avatar video link plays when the page is parsed, not fully loaded | `MainWindow.Browser.cs`, `BrowserService.cs` | CCP.Avalonia browser host | needs-port (`browser`) | avatar video link on parse |
| 38 | `2b7d742d0` | fix: a refused Chaster lock id never books time as landed; browser object messages are read | `MainWindow.Browser.cs`, `ChasterService.App.cs`, `ChasterTabView.xaml.cs`; Core: `ChasterClient.cs`, `ChasterService.cs`, `ChasterServiceTests.cs` | Core ChasterClient/ChasterService merged; ChasterTabView, browser | needs-port (`chaster-tab-7.0.x`) | Core part by merge; WPF Browser/ChasterTabView object messages |
| 39 | `56dbab326` | fix(chaster): the raffle check skips a lock id the client would refuse | Core: `ChasterService.Ladder.cs` | CCP.Core/Services/Chaster/ChasterService.Ladder.cs | ported-by-merge | Core file, merged |
| 40 | `5c4724413` | fix(chess): IQ only drains, a slower mate is no blunder, a late settle keeps its game | assets: 4 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 41 | `5f525dc49` | fix(chess): a corrected online seat moves the IQ readout to the right card | assets: 1 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 42 | `cb0bc1880` | fix(chess): the fall keeps the game's own IQ when the menu comes before the last grade | assets: 3 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 43 | `45a0ffd14` | fix(chess): Save says what the page knows, and the fall smoke says what it checks | assets: 2 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 44 | `1e9bcff21` | fix(chess): the think only runs over a dealt game, and a resync is not a move | assets: 2 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 45 | `ba13f6049` | fix(arcademy): phone landscape campus HUD no longer stacks on itself | assets: 3 | Assets/web/arcademy | ported-by-merge | web-only |
| 46 | `9c6e536e3` | fix(chaster): day limit cuts off adds minus credits; stale mouse swallow flag | `GlobalMouseHook.cs`; Core: `ChasterService.cs`, `CircesTab.cs`, `ChasterServiceTests.cs`; tests: 2 | CCP.Core Chaster (ChasterService, CircesTab) merged | ported-by-merge | stale swallow flag is in Win32 GlobalMouseHook (n/a on Linux) |
| 47 | `37220fd8f` | fix(chaster): log the status Chaster answered at Information | Core: `ChasterClient.cs` | CCP.Core/Services/Chaster/ChasterClient.cs | ported-by-merge | Core file, merged |
| 48 | `443679d35` | fix(breakout): clamp locked mouse spikes and drop the lock's first move (ccp-bugs #1337) | assets: 3 | Assets/web/backroom | ported-by-merge | web-only |
| 49 | `e5a0aa648` | fix(chaster): "Resisted the red" preview shows the hold and -1:00 | Core: `TabMenuCopy.cs`; assets: 1; tests: 1 | CCP.Core/Services/Chaster/TabMenuCopy.cs | ported-by-merge | Core file, merged |
| 50 | `bfd5a2d22` | fix(quests): Takeover quest time counts while the panel is minimised | `AchievementService.cs`, `RunningTimeCredit.cs`; tests: 1 | Core QuestService; WPF AchievementService/RunningTimeCredit | needs-port (`quests`) | minimised panel still credits Takeover time |
| 51 | `97481fc56` | fix(presets): catalogue preset drops import again (ccp-bugs #1331) | `MainWindow.PresetIO.cs`, `MainWindow.SessionIO.cs`, `LauncherHost.cs`, `PresetDropRules.cs` +2; languages; Core: `PresetFileService.cs`; tests: 1 | Core strings merged; WPF PresetDropRules | needs-port (`presets`) | catalogue preset drop import |
| 52 | `ab381fa6a` | fix(ai): a named AI video plays its closest local match, or its HypnoTube link (ccp-bugs #1330) | `VideoTitleMatcher.cs`; Core: `MediaCommand.cs`; tests: 2 | CCP.Core/Services/Commands/MediaCommand.cs + VideoTitleMatcher.cs (placed in Core) | ported-by-merge | Core files, merged |
| 53 | `7d28316b6` | test: the held row's own scene in NatashaHoldTests | tests: 1 | - | n/a | WPF-only test |
| 54 | `ce912bb12` | feat(lobby): one row model for chess, Goon and Remote tables, merged and gated | `LobbyModel.cs`; tests: 1 | CCP.Avalonia AvailableSubjectsTabView | needs-port (`lobby`) | LobbyModel |
| 55 | `20f1ea3ed` | feat(lobby): App.Lobby polls the open tables while watched, chess join and host intents | `App.xaml.cs`, `LobbyService.cs`, `PbpLobbyApi.cs`, `PieceByPieceHostService.Friends.cs`; assets: 1; tests: 1 | CCP.Avalonia AvailableSubjectsTabView | needs-port (`lobby`) | LobbyService / PbpLobbyApi polling |
| 56 | `342123f83` | feat(lobby): the Available Subjects tab becomes the Lobby | `MainWindow.Lobby.cs`, `MainWindow.RemoteControl.cs`, `MainWindow.TabNavigation.cs`, `AvailableSubjectsTabView.xaml` +2; Core: `SettingsPaletteIndex.cs` | CCP.Avalonia AvailableSubjectsTabView | needs-port (`lobby`) | Available Subjects tab becomes Lobby (SettingsPaletteIndex merged) |
| 57 | `239c1ae6e` | feat(lobby): Lobby chip on the launcher with a dropdown of open tables | `LauncherWindow.Lobby.cs`, `LauncherWindow.OpenTables.cs`; tests: 1 | CCP.Avalonia launcher | needs-port (`lobby`) | Lobby chip on the launcher |
| 58 | `e83f2d4b7` | feat(lobby): Lobby copy in all nine languages | languages | CCP.Core Languages | ported-by-merge | strings only |
| 59 | `53b81238c` | feat(lobby): polish - three columns side by side, launcher look, light game art | `MainWindow.Lobby.cs`, `LobbyModel.cs`, `AvailableSubjectsTabView.xaml`, `AvailableSubjectsTabView.xaml.cs` +2; languages; tests: 2 | CCP.Avalonia AvailableSubjectsTabView | needs-port (`lobby`) | three-column polish |
| 60 | `b5eff6956` | feat(lobby): text-free Remote thumbnail | `LobbyRowView.cs`; assets: 1 | CCP.Avalonia AvailableSubjectsTabView | needs-port (`lobby`) | Remote thumbnail (asset merged) |
| 61 | `6f5e76610` | feat(invites): invite week core - wire, rules and the dated premium grant | `InviteApi.cs`, `InviteGrantSync.cs`, `InviteRules.cs`, `ProfileSyncService.cs` +1; Core: `V2AuthService.cs`; tests: 2 | Core V2AuthService merged; none yet | needs-port (`invites`) | InviteApi/Rules/GrantSync (WPF) |
| 62 | `ecb8ace7e` | feat(invites): reward ladder - four Community badges that unlock wardrobe items | `MainWindow.AchievementsTab.cs`, `InviteRewards.cs`, `INVITE_WEEK_PRIMER.md`; languages; Core: `Achievement.cs`; assets: 9; tests: 1 | Core strings + Assets merged | needs-port (`invites`) | reward ladder, badges |
| 63 | `9c799d758` | feat(invites): invites section on the Exclusives tab | `InvitePanel.cs`, `MainWindow.Exclusives.cs`, `InviteApi.cs`, `InviteRules.cs` +1; languages; tests: 2 | CCP.Avalonia Exclusives tab | needs-port (`invites`) | invites section |
| 64 | `9c2c5bb6b` | fix(invites): review fixes - own grant field, no celebration for trials, refuse bad grants | `App.xaml.cs`, `InvitePanel.cs`, `MainWindow.CloudBackup.cs`, `MainWindow.Login.cs` +8; Core: `AppSettings.cs`; tests: 3 | Core AppSettings merged | needs-port (`invites`) | grant field, refusals |
| 65 | `fbe161de2` | feat(vault): gate card on every padlock, tier compare, invite-week last-day card | `VaultGateDialog.cs`, `MainWindow.Patreon.cs`, `MainWindow.xaml.cs`, `VaultOffer.cs` +1; languages; Core: `TierGate.cs`; tests: 1 | CCP.Avalonia Vault/padlock surfaces | needs-port (`vault-gate`) | VaultGateDialog, VaultOffer |
| 66 | `c8fff36a0` | feat(vault): the real Patreon prices, in euros or dollars, with yearly | `VaultGateDialog.cs`, `VaultOffer.cs`, `INVITE_WEEK_PRIMER.md`; languages; Core: `PatreonModels.cs`; tests: 1 | - | needs-port (`vault-gate`) | real Patreon prices |
| 67 | `1200f1723` | fix(invites,vault): whole-stack review fixes | `InvitePanel.cs`, `InviteRedeemBox.cs`, `VaultGateDialog.cs`, `MainWindow.Patreon.cs` +5; languages; Core: `Achievement.cs`, `TierGate.cs`; tests: 4 | - | needs-port (`invites`) | invites + vault review fixes |
| 68 | `a39bbfc75` | fix(invites,vault): Basic and Prime, never vault or lab; neutral copy | `VaultGateDialog.cs`, `INVITE_WEEK_PRIMER.md`; languages; Core: `Achievement.cs` | - | needs-port (`vault-gate`) | tier naming copy |
| 69 | `b7a28266f` | release: 7.0.2 "Stay Tuned" | `ConditioningControlPanel.csproj`, `MainWindow.xaml`, `UpdateService.cs`; languages; `build-installer.bat`, `installer.iss` | - | n/a | release bump: WPF csproj version, installer, release notes; strings merged |
| 70 | `99f48bc22` | fix(vault): links on the unlock card click again (DragMove ate the button-up) | `VaultGateDialog.cs` | - | needs-port (`vault-gate`) | unlock-card links (WPF DragMove); verify on the ported dialog |
| 71 | `a88a74569` | feat(vault): first-month sale prices on the unlock card | `VaultGateDialog.cs`, `VaultSale.cs`; languages; tests: 1 | - | needs-port (`vault-gate`) | VaultSale first-month prices |
| 72 | `e2d4e35ef` | feat(invites): header invite ticket + "Invite them" link in the friends drawer | `FriendsDrawer.cs`, `InvitePanel.cs`, `MainWindow.Exclusives.cs`, `MainWindow.InviteTicket.cs` +5; languages; tests: 1 | CCP.Avalonia header / friends drawer | needs-port (`invites`) | invite ticket + Invite them link |
| 73 | `7adb801a3` | fix(invites): code count line reads right for one code | languages | CCP.Core Languages | ported-by-merge | strings only |
| 74 | `71334b429` | release notes: one invite code a month, the ticket | `UpdateService.cs` | - | n/a | WPF release notes text |
| 75 | `eb6ccc404` | feat(nav): Back arrow above search returns to the previous tab; Sparkle wallet 25% smaller | `MainWindow.TabHistory.cs`, `MainWindow.xaml`; languages | CCP.Avalonia MainShellWindow.TabNavigation | needs-port (`nav-back`) | Back arrow / tab history |
| 76 | `d9a6d972f` | fix(vault): the unlock card lists what each tier really adds; four gate fixes | `VaultGateDialog.cs`, `MainWindow.Exclusives.cs`, `PatreonService.cs`, `EmiTargets.cs`; languages | - | needs-port (`vault-gate`) | tier list + gate fixes (PatreonService, EmiTargets) |
| 77 | `2e9080399` | feat(exclusives): the collection shows every paid feature, Prime first | `MainWindow.Exclusives.cs`; languages; Core: `ExclusiveFeature.cs`; assets: 1 | CCP.Avalonia Exclusives tab | needs-port (`exclusives`) | collection shows every paid feature |
| 78 | `f1022c95d` | feat(invites): real badge and wardrobe art, picture tiles for the reward ladder | `InvitePanel.cs`; languages; assets: 8; tests: 1 | Assets merged | needs-port (`invites`) | badge/wardrobe art tiles |
| 79 | `bf57cecdf` | fix(exclusives): the collection grid fills the width with as many columns as fit | `MainWindow.Exclusives.cs`, `ExclusiveShelfFit.cs`; tests: 1 | CCP.Avalonia Exclusives tab | needs-port (`exclusives`) | ExclusiveShelfFit |
| 80 | `b9af605c2` | fix(chess): the board stays readable at any heat | assets: 7 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 81 | `caf95db66` | feat(chess): Distraction takes a breath on every move, and has dials | `UpdateService.cs`; assets: 14 | Assets/web/piecebypiece | ported-by-merge | web-only; release-notes line n/a |
| 82 | `2781b217c` | feat(chess): Options in two columns, no scroll bars, and a Surrender button | assets: 3 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 83 | `e29358238` | fix(companion): a speech bubble no longer brings the app to the front | `AvatarTubeWindow.Speech.cs` | CCP.Avalonia AvatarTubeWindow | needs-port (`companion-7.0.3`) | speech bubble must not activate the app |
| 84 | `e4764063c` | fix(ai): effect-control video resolves a pool title to its HypnoTube link (ccp-bugs #1330 #1344) | Core: `MediaCommand.cs`; tests: 1 | CCP.Core/Services/Commands/MediaCommand.cs | ported-by-merge | Core file, merged |
| 85 | `2e1296805` | fix(bubbles): a red bubble press passes through the hook and is judged by the real button (ccp-bugs #1342) | `BubbleService.cs`; tests: 1 | - | n/a | Win32 low-level mouse hook pass-through (no global hook on the Linux head) |
| 86 | `ec04483c4` | feat(companion): v2 Settings gets Video links back (ccp-bugs #1343) | `MainWindow.xaml.cs`, `WorkshopRuntimeVm.cs`, `ConversationPage.xaml`, `ConversationPage.xaml.cs`; languages | CCP.Avalonia companion V2 settings | needs-port (`companion-7.0.3`) | video links back |
| 87 | `cf32a4156` | fix(chaster): a lock the model cannot read no longer reads as 'Chaster is unreachable' (ccp-bugs #1332) | Core: `ChasterClient.cs`; tests: 1 | CCP.Core/Services/Chaster/ChasterClient.cs | ported-by-merge | Core file, merged |
| 88 | `71cfc4185` | fix: controller leave releases panic/strict lock, no bubbles over game windows, game windows keep a taskbar button under a hidden panel (ccp-bugs #1340 #1345) | `AvatarTubeWindow.Speech.cs`, `ChaosWebViewHost.cs`, `RemoteControlService.cs` | CCP.Avalonia remote control | needs-port (`remote-v2`) | controller leave releases panic/strict; Chaos WebView2 part n/a |
| 89 | `355d608ad` | feat(launcher): desktop game shortcuts wear their own icon | `ConditioningControlPanel.csproj`, `LauncherShortcuts.cs`, `StartupManager.cs`; assets: 9; tests: 1 | - | n/a | Windows desktop .lnk shortcuts with .ico (installer/StartupManager); Linux .desktop is a separate concern |
| 90 | `add72ae95` | feat(remote): v2 API skeleton for the HUD (signals, Easy, controller name, last action) | `RemoteControlService.V2.cs`, `RemoteControlService.cs` | CCP.Avalonia RemoteControlTabView / Core remote | needs-port (`remote-v2`) | v2 API skeleton |
| 91 | `719ed9ca5` | feat(remote): v2 haptic patterns, hold-to-buzz, Melt, panic key stays on | `MainWindow.xaml.cs`, `RemoteActionLabels.cs`, `RemoteControlService.V2.cs`, `RemoteHapticDriver.cs` +2; tests: 1 | - | needs-port (`remote-v2`) | haptic patterns, hold-to-buzz, Melt |
| 92 | `209659be8` | feat(awareness): show the listening cue while a voiced mantra waits for the phrase (ccp-bugs #841) | `AutonomyService.cs` | Core autonomy / Avalonia awareness | needs-port (`awareness-7.0.3`) | listening cue while voiced mantra waits |
| 93 | `6da86e2d2` | feat(companion): keep the tube chat box open after Send (ccp-bugs #1279) | `AvatarTubeWindow.ChatInput.cs` | CCP.Avalonia AvatarTubeWindow | needs-port (`companion-7.0.3`) | chat box stays open after Send |
| 94 | `55eb57f21` | feat(quests): a lockdown counts for quests only after 20 minutes (ccp-bugs #705) | `App.xaml.cs`; Core: `QuestService.cs`; tests: 1 | CCP.Core/Services/Progression/QuestService.cs | ported-by-merge | Core file, merged; WPF App.xaml.cs wiring checked in the merge |
| 95 | `608ff3181` | feat(programs): list the active mod's programs first (ccp-bugs #966) | `MainWindow.ProgramsTab.cs`, `ProgramBrowseOrder.cs`; tests: 1 | CCP.Avalonia ProgramsTab | needs-port (`programs`) | active mod's programs first |
| 96 | `04807471a` | feat(deeper): show values on the overlay opacity sliders (ccp-bugs #936) | `DeeperEditorWindow.Unified.cs` | CCP.Avalonia DeeperEditor | needs-port (`deeper`) | opacity slider values |
| 97 | `bfe45f6db` | feat(bugreport): report window stays above overlays and takes focus (ccp-bugs #704) | `BugReportWindow.xaml`, `BugReportWindow.xaml.cs` | CCP.Avalonia BugReportWindow | needs-port (`bugreport`) | stays above overlays, takes focus |
| 98 | `5cac9c54d` | feat(chess): invert left/right and up/down for the camera drag (ccp-bugs #1329) | assets: 5 | Assets/web/piecebypiece | ported-by-merge | web-only |
| 99 | `0fc2d4faa` | feat(awareness): Brain Drain as a keyword visual effect (ccp-bugs #1214) | `AwarenessPresetDetailDialog.xaml.cs`, `KeywordTriggerService.cs`; Core: `KeywordTrigger.cs` | Core KeywordTrigger merged; KeywordTriggerService | needs-port (`brain-drain-7.0.3`) | Brain Drain keyword visual effect |
| 100 | `ec0ad296a` | feat(flash): Drift and Bounce speed slider (ccp-bugs #1265) | `FlashFeatureControl.xaml`, `FlashFeatureControl.xaml.cs`, `FlashService.cs`; languages; Core: `AppSettings.cs`; tests: 1 | CCP.Avalonia FlashFeatureControl / FlashOverlay | folded (feat-flash) | Avalonia has no flash motion styles yet; carry FlashDriftSpeed when they are built |
| 101 | `97303aff4` | feat(braindrain): give Brain Drain clips their own volume (ccp-bugs #1104) | `BrainDrainService.cs`, `BrainDrainFeatureControl.xaml`, `BrainDrainFeatureControl.xaml.cs`; languages; Core: `AppSettings.cs`; tests: 1 | CCP.Avalonia BrainDrain | needs-port (`brain-drain-7.0.3`) | clip volume |
| 102 | `8fea9bc0a` | feat(mercy): Mercy switch and after-N-fails picker for Bubble Count and Video (ccp-bugs #1145) | `BubbleCountFeatureControl.xaml`, `BubbleCountFeatureControl.xaml.cs`, `MercyRow.xaml`, `MercyRow.xaml.cs` +4; languages; Core: `AppSettings.cs`; tests: 1 | CCP.Avalonia BubbleCount/Video feature controls | ported (avalonia-port/port-fx) | Mercy switch + after-N picker on the Video card; Core schedulers read MercyAfterFails; Bubble Count strict warning names it; `VideoTargetMonitorTests`, Core `Mercy_comes_after_the_picked_number_of_*` |
| 103 | `785bd5c69` | feat(flash): images per flash as a random range (ccp-bugs #658) | `FlashFeatureControl.xaml`, `FlashFeatureControl.xaml.cs`, `FlashService.cs`; languages; Core: `AppSettings.cs`; tests: 1 | CCP.Avalonia FlashFeatureControl | ported (avalonia-port/port-fx) | Random image count switch + Fewest slider; FlashOverlay rolls RollFlashImageCount; `FlashPickTests.Random_image_count_switch_reveals_the_floor_and_saves` |
| 104 | `dda21a45a` | feat(lockcard): optional reset on typo wipes the line (ccp-bugs #1163) | `LockCardFeatureControl.xaml`, `LockCardFeatureControl.xaml.cs`, `LockCardWindow.xaml.cs`; languages; Core: `AppSettings.cs` | CCP.Avalonia LockCard feature/window | needs-port (`lockcard`) | reset on typo |
| 105 | `a464a897c` | feat(media-log): log Brain Drain clips as Audio entries (ccp-bugs #1098) | `BrainDrainService.cs`, `MediaHistoryService.cs`, `MediaHistoryWindow.xaml`, `MediaHistoryWindow.xaml.cs`; languages; Core: `SessionLog.cs`; tests: 1 | CCP.Avalonia MediaHistoryWindow | needs-port (`brain-drain-7.0.3`) | Brain Drain clips logged as Audio |
| 106 | `7e051b926` | feat(overlay): spiral visible at high opacity (ccp-bugs #722) | `OverlayService.cs`; tests: 1 | CCP.Avalonia spiral overlay | folded (feat-spiral) | Avalonia has no spiral overlay yet (SpiralOpacity is read only by the card); carry SpiralPaint with it |
| 107 | `7e337d085` | feat(awareness): cooldowns reach an hour, sliders walk a ladder of stops (ccp-bugs #640) | `MainWindow.Awareness.cs`, `MainWindow.KeywordTriggers.cs`, `AwarenessCooldownScale.cs`, `AwarenessTabView.xaml`; Core: `AppSettings.cs`; tests: 1 | Core AppSettings merged; Avalonia awareness tab | needs-port (`awareness-7.0.3`) | cooldown ladder sliders |
| 108 | `6c034816d` | feat(gaze): open the camera at launch when Focus Gaze was left on (ccp-bugs #1106) | `MainWindow.LabTab.cs` | CCP.Avalonia Lab tab gaze | folded (shell-lab-tab) | Avalonia has no Focus Gaze/webcam path yet (ChkFocusGaze_Changed is empty); carry the boot camera start with it |
| 109 | `57bde0329` | feat(deeper): "Don't ask again" on the enhanced-video nudge (ccp-bugs #644) | `MainWindow.DeeperTab.cs`; Core: `AppSettings.cs` | Core AppSettings merged; Avalonia Deeper tab | needs-port (`deeper`) | Don't ask again nudge |
| 110 | `c57156b49` | feat(fyp): ghost mode starts at 60% opacity (ccp-bugs #832) | `FypHostService.cs`; Core: `AppSettings.cs`; assets: 1 | Core AppSettings default + Assets merged; Avalonia FYP host | needs-port (`fyp`) | ghost 60% (default by merge) |
| 111 | `17dade8e5` | feat(video): pick the monitor mandatory videos play on (ccp-bugs #1154) | `VideoFeatureControl.xaml`, `VideoFeatureControl.xaml.cs`, `VideoService.Browser.cs`, `VideoService.cs`; Core: `AppSettings.cs`; tests: 1 | Core AppSettings merged; Avalonia VideoFeatureControl/MandatoryVideoOverlay | ported (avalonia-port/port-fx) | Video card monitor picker (CmbMonitor, Default/All/N) saves VideoTargetMonitor; MandatoryVideoOverlay.Targets resolves it via VideoTarget; `VideoTargetMonitorTests` |
| 112 | `03af6e8bb` | feat(assets): asset presets also switch the Scrolller selection and media source (ccp-bugs #1142) | `MainWindow.Assets.cs`, `LauncherMediaDialog.xaml.cs`; Core: `AssetPreset.cs`, `AssetPresetService.cs`; tests: 1 | Core AssetPreset/AssetPresetService merged; Avalonia AssetsTabView | needs-port (`presets`) | preset switches Scrolller + source |
| 113 | `0ec3119c4` | fix(companion): speech bubble re-decides when a game opens or closes | `AvatarTubeWindow.Speech.cs`, `ChaosWebViewHost.cs` | CCP.Avalonia AvatarTubeWindow | needs-port (`companion-7.0.3`) | bubble re-decides on game open/close; Chaos WebView2 part n/a |
| 114 | `7b22ece8c` | feat(remote): refuse trigger_haptic with no toy and report haptics as active (ccp-bugs #1065) | `RemoteControlService.cs` | - | needs-port (`remote-v2`) | refuse trigger_haptic with no toy |
| 115 | `c419d6201` | fix(chaster): say the ladder counts what Chaster confirms, not the card's own figure | `ChasterTabView.xaml`; languages | CCP.Avalonia ChasterTabView | needs-port (`chaster-tab-7.0.x`) | ladder copy (strings merged) |
| 116 | `5d8dfdfd9` | fix(breakout): a click launches under the mouse lock; portal drops never leave the top | assets: 5 | Assets/web/backroom | ported-by-merge | web-only |
| 117 | `ada150186` | fix(chaster): tab previews show each row's own sign; the hold reads as a hold | Core: `TabMenuCopy.cs`; assets: 1; tests: 1 | CCP.Core/Services/Chaster/TabMenuCopy.cs | ported-by-merge | Core file, merged |
| 118 | `d39969827` | feat(remote): v2 live preview, hot cadence, controller name, share toggle | `MainWindow.Browser.cs`, `MainWindow.Settings.cs`, `AudioService.cs`, `RemoteControlService.Screen.cs` +7; languages; Core: `AppSettings.cs`; tests: 1 | Core AppSettings/strings merged | needs-port (`remote-v2`) | live preview, cadence, controller name, share toggle |
| 119 | `5a44642cf` | feat(remote): v2 More / Easy / Stop signals for real | `RemoteControlService.V2.cs`, `RemoteEasy.cs`, `RemoteHapticDriver.cs`, `RemoteControlService.cs`; tests: 1 | - | needs-port (`remote-v2`) | More / Easy / Stop signals |
| 120 | `1fc87b08d` | feat(remote): HUD rules and copy for the Remote Control v2 pill | `RemoteHudRules.cs`; languages; tests: 1 | - | needs-port (`remote-v2`) | HUD rules (strings merged) |
| 121 | `fba7a3531` | feat(remote): the Remote Control v2 HUD pill | `MainWindow.RemoteControl.cs`, `MainWindow.RemoteHud.cs`, `MainWindow.WindowChrome.cs`, `RemoteHudWindow.cs` | - | needs-port (`remote-v2`) | HUD pill window |
| 122 | `bc67fe306` | fix(remote): screen preview sends real JSON nulls for a missing pattern name or word | `RemoteScreenState.cs` | - | needs-port (`remote-v2`) | screen preview JSON nulls |
| 123 | `c0aa628b8` | feat(ui): fold the new power-user rows behind More options | `BubbleCountFeatureControl.xaml`, `BubbleCountFeatureControl.xaml.cs`, `LockCardFeatureControl.xaml`, `LockCardFeatureControl.xaml.cs` +5; languages; Core: `AppSettings.cs` | Core AppSettings/strings merged | ported (avalonia-port/port-fx) | Avalonia MoreFold; Video card folds monitor + Mercy, opens when off default; LockCard half folded (feat-lock-card: Reset on typo row not ported yet) |
| 124 | `745daca00` | feat(ui): fold Brain Drain clip volume behind More options | `MoreFold.cs`, `BrainDrainFeatureControl.xaml`, `BrainDrainFeatureControl.xaml.cs`; languages | - | folded (views-studio-brain-drain-feature) | the Brain Drain volume row (`97303aff4`, brain-drain-7.0.3) does not exist on Avalonia yet; wrap it in MoreFold when it lands |
| 125 | `cd9bee118` | fix(chaster): a red bubble popped on a full tab says "day is full" | `MainWindow.Chaster.cs`, `CapNotice.cs`; languages; Core: `ChasterService.cs`; tests: 1 | Core ChasterService merged; Avalonia Chaster | needs-port (`chaster-tab-7.0.x`) | CapNotice "day is full" |
| 126 | `3658d9755` | i18n: 7.0.3 strings in all nine languages (Remote v2 HUD, Chaster cap float, video links, audio filter) | languages | CCP.Core Languages | ported-by-merge | strings only |
| 127 | `e084cd367` | release: 7.0.3 Stay Tuned | `ConditioningControlPanel.csproj`, `MainWindow.xaml`, `UpdateService.cs`; languages; `build-installer.bat`, `installer.iss` | - | n/a | release bump: WPF csproj version, installer, release notes; strings merged |
| 128 | `61a331c1d` | fix(chaster): say so when the keyholder turned off adding time | `ChasterTabView.xaml.cs`; languages; Core: `ChasterClient.cs`, `ChasterService.Lock.cs`, `ChasterService.cs` +1; tests: 1 | Core ChasterClient/Lock merged; Avalonia ChasterTabView | needs-port (`chaster-tab-7.0.x`) | adds-blocked message in the tab |
| 129 | `8bd6402cf` | fix(invites): clearer redeem refusals | `InvitePanel.cs`; languages; tests: 1 | Core strings merged | needs-port (`invites`) | redeem refusals |
| 130 | `6801ac42e` | fix(chaster): a lock running a scripted extension no longer reads as offline | Core: `ChasterClient.cs`; tests: 1 | CCP.Core/Services/Chaster/ChasterClient.cs | ported-by-merge | Core file, merged |
| 131 | `5fa7cf4ac` | fix(chaster): keep nested fields the lock model maps | Core: `ChasterClient.cs` | CCP.Core/Services/Chaster/ChasterClient.cs | ported-by-merge | Core file, merged |
| 132 | `854ac954a` | fix(video): the no-videos dialog names the length filter when it emptied the pool | `NoVideosReason.cs`, `VideoService.cs`; languages; tests: 1 | Core strings merged; Avalonia video | folded (feat-video) | Avalonia's MandatoryVideoScheduler has no duration filter yet (local library only), so the filter can never empty the pool; carry NoVideosReason with the filter |
| 133 | `c8dead5b3` | fix(chaster): the tab says so when nothing can count yet | `ChasterTabView.Fx.cs`, `ChasterTabView.xaml`, `ChasterTabView.xaml.cs`; languages; Core: `TabPageText.cs`; tests: 1 | Core TabPageText merged; Avalonia ChasterTabView | needs-port (`chaster-tab-7.0.x`) | nothing-can-count notice |
| 134 | `54d020e60` | fix(chaster): raffle card text wraps instead of overlapping | `SplitRowPanel.cs`, `ChasterTabView.xaml`; tests: 1 | CCP.Avalonia ChasterTabView | needs-port (`chaster-tab-7.0.x`) | SplitRowPanel wrap |
| 135 | `8c877e8d4` | release: 7.0.4 "Stay Tuned" | `ConditioningControlPanel.csproj`, `MainWindow.xaml`, `UpdateService.cs`; languages; `build-installer.bat`, `installer.iss` | - | n/a | release bump: WPF csproj version, installer, release notes; strings merged |
| 136 | `365d4cf5d` | fix: 7.0.5 hotfix - Circe's third arm, Lockdown quest says 20 minutes, Remote Full copy | `ConditioningControlPanel.csproj`, `spiral_overlay.png`, `MainWindow.RemoteControl.cs`, `LockdownTabView.xaml` +1; languages; Core: `CirceNeutralPackPatch.cs`; tests: 2 | Core CirceNeutralPackPatch + strings merged | ported-by-merge | hotfix; WPF RemoteControl copy folds into remote-v2; LockedMod png is WPF locked-mod resource |
| 137 | `da3ebd544` | release: 7.0.5 "Stay Tuned" | `ConditioningControlPanel.csproj`, `MainWindow.xaml`, `UpdateService.cs`; languages; `build-installer.bat`, `installer.iss` | - | n/a | release bump: WPF csproj version, installer, release notes; strings merged |

## Merge notes (12ae86b3d) and -compat

- Placed in Core by the merge: `VideoTitleMatcher.cs`, `PresetDropRules.cs`, new `CapNoticeRule` (the rule half of WPF `CapNotice`).
- New Core seams the WPF head seeds and the Avalonia head does not yet: `MediaCommand.HypnoTubeSurface` (an AI-named
  HypnoTube video is refused on Avalonia until a browser host seeds it: lane `browser`), `TierGate.ReconnectIsTheAnswerProvider`
  (lane `vault-gate`), `ExclusiveFeature.JustDrop/Arcademy/BreakoutFullProvider` (unseeded = hidden/locked; lane `exclusives`),
  `AssetPresetService.OnlineChannelsReset` (lane `presets`).
- Invite-week premium is folded into Core `ProviderSubscription.HasPremiumAccess` (both heads); `IsInviteWeekOnly` added.
- -compat: WPF test paths that pointed at moved languages/assets now go through `SourceRoots` / `Assets/`.
