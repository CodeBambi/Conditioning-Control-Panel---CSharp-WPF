# Visual capture matrix

Select the applicable rows for the task. A row is complete only when the artifact manifest identifies its contract/reference and K3 verdict.

| Area | Required states | Variants |
|---|---|---|
| Dashboard | initial, enabled/disabled/locked cards, hover, keyboard focus, long labels | five themes; supported languages; min/normal/large window |
| Feature popup | short content, overflowing content at top/middle/bottom, nested list/editor, focused clipped control | owner on primary/secondary; 100/125/150% scaling; wheel/touch/thumb evidence separately |
| Dialog/window | launch, owner active/inactive, modal/modeless, resize minimum/maximum, close/error | Windows; Linux X11; Linux Wayland; native chrome where applicable |
| AvatarTube | static pose A/B, GIF frame sequence, idle emote sequence, speech/reaction, attached/detached, mod switch | five themes/mods; owner resize/minimize/restore; mixed scaling |
| Shared composition | base desktop, video first frame/live sequence, black bars, spiral, tint, representative higher effect, teardown | each monitor; negative/vertical topology; portrait/flipped; tint safety |
| Overlay | passive-only, interactive region, focus/activation, capture-included/excluded, teardown | Windows; X11; Wayland; actual desktop capture required |
| DTRH | windowed, fullscreen, loading, hub, run, native video cover, error/exit | Windows; X11; Wayland; WebView content requires real capture |
| Error/empty/loading | no assets, missing runtime, offline, invalid input, permission denied, retry | theme/language/platform relevant to task |

## Minimum artifact manifest

```json
{
  "task": "task-board row or surface",
  "tree": "commit or dirty-tree description",
  "platform": "Windows or Linux",
  "backend": "Win32, X11, Wayland plus compositor/window manager",
  "monitors": [
    { "name": "...", "boundsPx": "x,y,w,h", "scale": 1.0, "orientation": "..." }
  ],
  "theme": "...",
  "language": "...",
  "surface": "...",
  "state": "...",
  "captureMethod": "offscreen window render, target window, or virtual desktop",
  "reference": "contract section and optional reference image",
  "model": "kimi-coding/k3",
  "verdict": "PASS, FIX, or BLOCKED"
}
```

Do not include user paths, tokens, URLs with credentials, personal media names, camera content, or other sensitive values.
