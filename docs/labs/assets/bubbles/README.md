# Bubble Pop preview assets

Real CC app assets powering the floating bubbles on the explore page
(`js/rabbit-hole/bubbles.js`). The hand-picked explicit content (flash clips,
voice lines) is opt-in and listed in `manifest.js` — nothing plays until you add it.

## What's here

- `bubble.png` — the real soap-bubble sprite (resized 1024→256).
- `spiral.webm` — real `spiral.gif`, re-encoded (8.6MB → ~300KB).
- `sfx/` — pop SFX: `Pop/Pop2/Pop3.mp3` (random pop), `Burst.mp3` (trigger),
  `GG.mp3` (combo streak), `chime1/2/3.mp3` (Lucky). These play by default.
- `flash/` — **you populate this.** Web-optimized flash clips (`.webm`).
- `voices/` — **you populate this.** Spoken voice-line `.mp3`s (opt-in only).
- `manifest.js` — the single source of truth: `FLASH_CLIPS`, `VOICES`,
  `SUBLIMINAL_WORDS`. The preview only shows/plays what's listed here.

## Hand-pick flash gifs (the ~20)

Your library lives at
`C:/Users/PC/AppData/Local/ConditioningControlPanel/assets/images/` (~112 gifs).
Pick the ones you want, then optimize them for web:

```bash
tools/optimize-flash.sh "/c/Users/PC/AppData/Local/ConditioningControlPanel/assets/images/0_bambi1.gif" ...
```

It writes small `.webm`s into `flash/` and prints the `FLASH_CLIPS = [...]` array
to paste into `manifest.js`. With clips listed, Trigger bubbles flash a real clip
instead of the built-in CSS flash.

## Hand-pick voice lines

Drop chosen `.mp3`s into `voices/` and list their filenames in `VOICES`. They only
play when the visitor flips the **🔊 Voices** toggle (off by default).
Source folders: `…/ccp-hotfix-v597/ConditioningControlPanel/Resources/sounds/flashes_audio/`
and `…/Resources/sub_audio/`.
