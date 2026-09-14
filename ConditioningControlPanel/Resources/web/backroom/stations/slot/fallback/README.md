# Fallback loops

Dealt by `Services/BackRoom/BackRoomMedia.cs` when the user's flash pool has fewer than four local
animated files (CONTRACT section 5). Slot `gN` falls back to `gifN.webp`. All four are cut from
existing repo art, `Resources/web/dtrh/assets/bubbles/effects/spirals/`, as 180x180 animated webp,
10 fps, 2 s: `gif0` = sp1, `gif1` = sp3, `gif2` = sp6, `gif3` = sp7.
