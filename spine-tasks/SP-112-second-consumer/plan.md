# SP-112 — plan checkpoint (written BEFORE the first product edit)

Branch `lane/SP-112-second-consumer`, base `7a35f6d8`, worktree
`.claude/worktrees/agent-a5d23672cec330ff9`. Pin **1742 unit / 107 headless**.

---

## 1. WHICH MODULE — **Bubble Count**, and Bubble Pop is refused with evidence rather than with "too big"

The packet says to choose by which module stresses the capability hardest, and names Bubble Pop's
per-bubble hit tests on MOVING windows as the harder question. **I read the capability before
choosing, and the answer is not "hard" — it is "absent".**

### Bubble Pop cannot consume `IInputPresence` at all, and here is the census

| what Bubble Pop needs | what `Input/**` has |
|---|---|
| a MOUSE click delivered from a window | `Win32InputPresence.WindowProc` (`:818-877`) handles `WM_PAINT`, `WM_KEYDOWN`, `WM_SYSKEYDOWN`, `WM_CHAR`, `WM_CLOSE` and nothing else. `Win32InputInterop` (`:54-58`) declares those five message constants and no mouse message at all. The only `Mouse`-shaped token in the whole folder is `WndClassExW.hCursor` |
| a hit test per bubble | `InputCaptureObservation.HitTestRoutesHere` is asked at ONE point — the single card's centre (`InputBounds.Centre`) |
| MANY windows at once | `Win32InputPresence` owns exactly one `nint _window`, one `_content`, one `_onKeystroke` |
| windows that MOVE while visible | `Prompt` places once; there is no move seam |
| windows that must NOT take the foreground (upstream's bubbles are `ShowActivated=false`, `IsHitTestVisible=_isClickable`, `WM_MOUSEACTIVATE -> MA_NOACTIVATE`: `BubbleService.cs:2960/2988/3103`, `BubbleCountWindow.xaml.cs:1822-1830`) | `Prompt` ESCALATES for the foreground (`AttachThreadInput` + `SetForegroundWindow`, D115) and `Confirmed` **requires** `IsForegroundWindow && SystemKeyboardFocusIsThisWindow` |

Consuming the input capability for Bubble Pop would mean adding a pointer message route, a
multi-window collection, a non-activating window class, a move-while-visible seam, and a SECOND
definition of `Available` in which the foreground clause is inverted. **That is not a second
consumer of a capability; it is a second capability inside `Input/**`** — precisely the shape the
packet forbids ("a capability that grows a method per caller is not a capability"). The finding is
the answer, and it costs one reading to state instead of one packet to discover.

### Bubble Count stresses BOTH landed capabilities where they claim to be general

1. It is the **first consumer that hands the video capability a picture this process COMPOSED**
   rather than the decoder's own output. `IVideoPresence.Show(VideoFrame)` claims to be a seam; this
   is the first caller that finds out.
2. It is the **first consumer that asks the input capability for something other than a phrase** — a
   NUMBER, with a submit key, an attempt budget and per-attempt feedback. `InputPromptContent`'s four
   slots and `InputKeystrokeKind`'s four shapes were written for one caller; this is the second.
3. It is the **first module that consumes two capabilities in one firing**, in an order SP-111's
   coexistence run never took: video surface UP, video surface DOWN, then a card that must take the
   foreground. SP-111 measured card-then-video (its §5 leg 5); this is video-then-card.
4. Its OUTCOME depends on the capability's own answer: the number the user is asked for is only
   askable because the operating system confirmed it was holding the frames the bubbles were in.

Upstream evidence read for the choice, in full: `Services/BubbleCountService.cs` (749 lines —
scheduling `:83-114`, the trigger and its drop/queue rules `:118-270`, the retry/mercy machine
`:296-436`, the video draw `:518-590`), `Windows/BubbleCountWindow.xaml.cs` (target `:1137-1150`,
spawn `:1199-1290`, the end `:1348-1391`, `CountBubble` `:1641-1810`),
`Windows/BubbleCountResultWindow.xaml.cs` (the answer machine `:159-247`, three attempts `:24`, the
#633 watchdog `:28-32`). **The packet's citation is exact: `BubbleCountService.cs:30` is
`private string _videosPath = "";` and `:29` is `private bool _isBusy;`.** Not re-broken.

---

## 2. PREDICTIONS — what I expect the capabilities to be missing (each checked in `record.md`)

| # | prediction |
|---|---|
| **P1** | `IVideoSurface.Begin` has **no per-frame seam**; nothing can be drawn ON a clip. I will need one, and the justification it must pass is that the FIRST consumer wants it too — upstream's Mandatory Video draws over its own video (blurred-background composite `VideoService.cs:3436`, attention checks `:4846`), both already listed as unported in `VideoSurfacePresenter`'s own remarks |
| **P2** | `InputPromptContent` has FOUR slots and the counting card has FIVE lines (question, instruction, answer, feedback, attempts). I predict I must **fold, not grow** |
| **P3** | `InputKeystrokeKind` has no SUBMIT. Enter will arrive as `Key` with `VirtualKey == 0x0D` and I will consume it **without adding a kind** |
| **P4** | The primary-display placement will be a **third copy** of the same twenty lines (`LockCardEffect.DefaultPlacement`, `VideoSurfacePresenter.DefaultPlacement`) — the SP-101 shape, and the answer is to share it |
| **P5** | Both shared capabilities are **SINGLE-TENANT**: neither `IInputPresence` nor `IVideoSurface` knows WHOSE card or WHOSE clip is up, so the second consumer must guard every teardown call with bookkeeping the first consumer never needed. SP-109's audio presence solved exactly this with per-module keyed slots; these two did not |
| **P6** | `IVideoPresence.FramesHeld` is a PRESENCE-lifetime counter, not a per-clip one, so it cannot answer "how many of MY frames did the OS hold" |

---

## 3. WHAT THE MODULE IS, in outcome terms

Upstream: a clip plays, bubbles appear over it and pop; then the user is asked for the total, with
three attempts and a too-low/too-high hint. The port keeps that outcome and changes the MECHANISM
only where the port's capabilities differ:

* **the bubbles are composed INTO the frames** rather than drawn as one topmost window per bubble.
  Upstream's own no-asset fallback bubble is a radial pink disc with a white rim
  (`BubbleCountWindow.xaml.cs:1688-1698`) and that is what is drawn. The gain is not aesthetic: a
  bubble in the picture is a bubble the video capability's OWN read-back proves the operating system
  is holding, at no new evidence cost.
* **arithmetic is upstream's, verbatim**: target `max(3, round(baseRate/30 * durationSeconds ±20%))`
  with baseRate 3/5/8 per difficulty (`:1137-1150`); spawn interval `duration*1000/target*0.7` ms
  with the 1500 ms lead-in and the `roll < 0.7 || count < target/2` rule (`:1199-1245`); position
  `relX = roll*0.7+0.15`, `relY = roll*0.5+0.25` (`:1252-1253`); lifetime `1000 + roll*500` ms
  (`:1734`); the safety end at `duration + 5 s` (`:1180`); the 30 s fallback duration (`:98`).
* **the question is the input capability's card**: `How Many Bubbles?` (`en.json:2443-2446`), three
  attempts (`BubbleCountResultWindow.xaml.cs:24`), digits only (`:98-101`), Enter submits
  (`:166-170`), `Too low! Try higher.` / `Too high! Try lower.` (`:247`), `Please enter a number!`
  (`:192`).
* **a game that could not be watched is never scored.** If the surface stopped holding the picture,
  or no bubble ever reached a held frame, the game is ABANDONED rather than asked — upstream's own
  answer to a game it could not really show (`BubbleCountService.cs:224-233`: skip outright, because
  a failed count starts the retry bounce for a game the user never saw).

**Not ported, declared rather than stubbed:** the strict/retry/mercy machine and its mercy LOCK CARD
(`BubbleCountService.cs:296-436`, `BubbleCountResultWindow.xaml.cs:296-340`), XP/achievements/level
gate, multi-monitor, content packs and the browser engine, the LibVLC poison cooldown, and the #633
inactivity watchdog — the port's answer to a stranded user is the Escape D112 already guarantees, so
the watchdog's cause does not exist here. **Strict mode is therefore ABSENT rather than an inert
dial** (D93's rule): with no retry apparatus and Escape always live, a strict switch would move
nothing.

## 4. THE DOT — no eighth meaning is owed, and that is the finding

```
Live  =  a firing is on the clock
      && the OS says this process can put a video surface on a display      (channel 1)
      && the OS says this process can put a window in front of a user       (channel 2)
      && ( nothing is running
           OR  a clip is up      -> the OS's copy of the surface ADVANCED       (MOTION, SP-111)
           OR  a question is up  -> the OS says the card holds foreground+focus (DEMAND, SP-110) )
```

The seven meanings are properties of the CAPABILITIES, not of the modules. A module that consumes
two capabilities inherits two meanings and needs no new one; the third clause is a phase switch
between them, not a new fact about the world.

## 5. FILES I EXPECT TO TOUCH (all inside File Scope)

New: `Effects/BubbleCountSchedule.cs`, `Effects/BubbleCountGame.cs`, `Effects/BubbleCountAnswer.cs`,
`Effects/BubbleCountEffect.cs`, `Effects/PrimaryDisplayPlacement.cs`,
`Session/BubbleCountPresetDocument.cs`, tests `BubbleCountModuleTests.cs` and a real-desktop
`BubbleCountObservations.cs` + `BubbleCountCapabilityTests.cs`.

Changed: `Effects/VideoSurfacePresenter.cs` (the painter seam and a busy refusal),
`Effects/LockCardEffect.cs` / `Effects/MandatoryVideoEffect.cs` only where the shared placement
extraction reaches them, `Session/EffectReasonCodes.cs`, `Session/SessionParticipant.cs`,
`Views/Pages/StudioPage.axaml(.cs)`, the four spine lists, `StudioRackHeadlessTests.cs`,
`StudioSurfaceNoticeTests.cs`, `RealDesktopCollectionGuardTests.cs`,
`client/docs/wpf-surface-reachability.md` (D133+).

**`Input/**` and `Video/**`: my working intention is to add NOTHING to either.** Every addition that
turns out to be unavoidable is reported in `record.md` against the first-consumer justification
test, and any I cannot justify is reported as a finding instead of shipped.
