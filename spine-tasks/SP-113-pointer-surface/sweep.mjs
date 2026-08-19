#!/usr/bin/env node
// SP-113's mutation sweep driver.
//
// It lives INSIDE this packet's folder and writes only inside it — the rule SP-112 established
// after a previous wave's driver wrote three levels above its own root into the shared checkout.
//
// One mutation at a time: patch a single product file, run a bounded filter, restore the file
// byte-identically, record caught/survived. A mutation that does not patch (its needle no longer
// matches) is reported as NOT-PATCHED rather than silently counted as caught.
//
// Usage:
//   node spine-tasks/SP-113-pointer-surface/sweep.mjs --check          (needles only, no tests)
//   node spine-tasks/SP-113-pointer-surface/sweep.mjs [--only M-a,M-b] [--log name.log]

import { execFileSync, spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(HERE, "..", "..");
const SRC = path.join(REPO, "client", "src", "CcpClient.Desktop");

// Built from char codes so this file itself can be stored with either line ending.
const LF = String.fromCharCode(10);
const CRLF = String.fromCharCode(13, 10);

const FILTER = [
  "FullyQualifiedName~BubblePopModuleTests",
  "FullyQualifiedName~PointerCapabilityTests",
  "FullyQualifiedName~PointerCoexistenceTests",
  "FullyQualifiedName~SecondEffectSpineTests",
  "FullyQualifiedName~ContinuousEffectSpineTests",
  "FullyQualifiedName~AudioModuleSpineTests",
].join("|");

const F = {
  iface: path.join(SRC, "Pointer", "IPointerSurface.cs"),
  surface: path.join(SRC, "Pointer", "Win32PointerSurface.cs"),
  factory: path.join(SRC, "Pointer", "PointerSurfaceFactory.cs"),
  field: path.join(SRC, "Effects", "BubblePopField.cs"),
  presenter: path.join(SRC, "Effects", "BubblePopSurfacePresenter.cs"),
  effect: path.join(SRC, "Effects", "BubblePopEffect.cs"),
  notices: path.join(SRC, "Views", "Pages", "PointerPanelNotices.cs"),
  document: path.join(SRC, "Session", "BubblePopPresetDocument.cs"),
};

// id, file, needle, replacement, what it breaks
const MUTATIONS = [
  // ---- the observation record: every conjunct of Confirmed, one at a time ----
  ["M-a", F.iface, "        Asked && Window != 0 && WindowExists && WindowVisible", "        Window != 0 && WindowExists && WindowVisible", "Confirmed drops Asked"],
  ["M-b", F.iface, "        Asked && Window != 0 && WindowExists && WindowVisible", "        Asked && WindowExists && WindowVisible", "Confirmed drops the non-zero window"],
  ["M-c", F.iface, "        Asked && Window != 0 && WindowExists && WindowVisible", "        Asked && Window != 0 && WindowVisible", "Confirmed drops WindowExists"],
  ["M-d", F.iface, "        Asked && Window != 0 && WindowExists && WindowVisible", "        Asked && Window != 0 && WindowExists", "Confirmed drops WindowVisible"],
  ["M-e", F.iface, "        && NonActivatingStyleHeld && !ClickThroughStyleHeld", "        && !ClickThroughStyleHeld", "Confirmed drops WS_EX_NOACTIVATE"],
  ["M-f", F.iface, "        && NonActivatingStyleHeld && !ClickThroughStyleHeld", "        && NonActivatingStyleHeld", "Confirmed drops the WS_EX_TRANSPARENT ban"],
  ["M-g", F.iface, "        && AboveEveryOrdinaryWindow && HitTestRoutesHere && ForegroundUndisturbed;", "        && HitTestRoutesHere && ForegroundUndisturbed;", "Confirmed drops the z-order"],
  ["M-h", F.iface, "        && AboveEveryOrdinaryWindow && HitTestRoutesHere && ForegroundUndisturbed;", "        && AboveEveryOrdinaryWindow && ForegroundUndisturbed;", "Confirmed drops the routing answer"],
  ["M-i", F.iface, "        && AboveEveryOrdinaryWindow && HitTestRoutesHere && ForegroundUndisturbed;", "        && AboveEveryOrdinaryWindow && HitTestRoutesHere;", "Confirmed drops the non-activation claim"],
  ["M-j", F.iface, "    public bool HitTestRoutesHere => Window != 0 && HitTestWinner == Window;", "    public bool HitTestRoutesHere => HitTestWinner == Window;", "the routing answer's non-zero guard"],
  ["M-k", F.iface, "        ForegroundBefore == ForegroundAfter && ForegroundAfter != Window;", "        ForegroundBefore == ForegroundAfter;", "the 'and it is not us' half"],
  ["M-l", F.iface, "        ForegroundBefore == ForegroundAfter && ForegroundAfter != Window;", "        ForegroundAfter != Window;", "the 'and it did not move' half"],
  ["M-m", F.iface, "    public bool Inked => BackgroundHeld && InkedPixels > 0;", "    public bool Inked => InkedPixels > 0;", "the ink differential's control point"],
  ["M-n", F.iface, "    public bool Inked => BackgroundHeld && InkedPixels > 0;", "    public bool Inked => BackgroundHeld;", "the ink count itself"],
  ["M-o", F.iface, "    public bool Confirmed => Asked && WindowStationVisible && DisplayCount >= 1 && DesktopReachable;", "    public bool Confirmed => Asked && DisplayCount >= 1 && DesktopReachable;", "the station's WSF_VISIBLE"],
  ["M-p", F.iface, "    public bool Confirmed => Asked && WindowStationVisible && DisplayCount >= 1 && DesktopReachable;", "    public bool Confirmed => Asked && WindowStationVisible && DesktopReachable;", "the station's display count"],
  ["M-q", F.iface, "    public bool Confirmed => Asked && WindowStationVisible && DisplayCount >= 1 && DesktopReachable;", "    public bool Confirmed => Asked && WindowStationVisible && DisplayCount >= 1;", "the station's desktop"],
  ["M-r", F.iface, "        if (bounds.Width < MinimumSide || bounds.Height < MinimumSide)", "        if (bounds.Width < 1 || bounds.Height < 1)", "the clickable floor on a request"],
  ["M-s", F.iface, "        if ((fill & 0x00FFFFFF) == (ink & 0x00FFFFFF))", "        if (false)", "the ink-differs-from-fill rule"],
  ["M-t", F.iface, "    public int Radius => Math.Min(Width, Height) / 2;", "    public int Radius => Math.Max(Width, Height) / 2;", "the radius taking the SHORTER side"],

  // ---- the backend's own gates ----
  ["M-u", F.surface, "        if (_targets.Count >= MaxConcurrentTargets)", "        if (false)", "the concurrent-target cap"],
  ["M-v", F.surface, "        var station = ObserveStation();\n        if (!station.Confirmed)", "        var station = ObserveStation();\n        if (false)", "Open's station gate"],
  ["M-w", F.surface, "        if (bounds.Width != entry.Bounds.Width || bounds.Height != entry.Bounds.Height)", "        if (false)", "Move's resize refusal"],
  ["M-x", F.surface, "        if (!_targets.TryGetValue(target, out var entry))\n        {\n            return LastPlacement = UnknownTarget(target);\n        }", "        if (!_targets.TryGetValue(target, out var entry))\n        {\n            return LastPlacement = Available(\"moved\");\n        }", "Move's unknown-handle refusal"],
  ["M-y", F.surface, "        if (!_targets.TryGetValue(target, out var entry))\n        {\n            return UnknownTarget(target);\n        }", "        if (!_targets.TryGetValue(target, out var entry))\n        {\n            return Available(\"closed\");\n        }", "Close's unknown-handle refusal"],
  ["M-z", F.surface, "            return LastPlacement = Unavailable(PointerReasonCodes.PointerSurfaceDisposed,\n                \"this pointer surface was disposed; its windows are gone and it will never place a target again\");", "            return LastPlacement = Available(\"disposed and still claiming\");", "Open's disposed refusal"],
  ["M-aa", F.surface, "                Win32PointerInterop.SwpShowwindow | Win32PointerInterop.SwpNoactivate))", "                Win32PointerInterop.SwpShowwindow))", "SWP_NOACTIVATE on every placement"],
  ["M-ab", F.surface, "        PaintNow(entry);", "        _ = entry;", "painting the target at all"],
  ["M-ac", F.surface, "        var foregroundBefore = Win32PointerInterop.GetForegroundWindow();\n\n        // SWP_NOACTIVATE is upstream's own flag", "        var foregroundBefore = (nint)0;\n\n        // SWP_NOACTIVATE is upstream's own flag", "sampling the foreground BEFORE the placement"],
  ["M-ad", F.surface, "        if (!observation.WindowExists || !observation.WindowVisible)", "        if (false)", "Classify's on-screen gate"],
  ["M-ae", F.surface, "        if (observation.HeldBounds != asked)", "        if (false)", "Classify's exact-rectangle gate"],
  ["M-af", F.surface, "        if (!observation.ForegroundUndisturbed)", "        if (false)", "Classify's non-activation gate"],
  ["M-ag", F.surface, "        if (!observation.NonActivatingStyleHeld || observation.ClickThroughStyleHeld)", "        if (false)", "Classify's style gate"],
  ["M-ah", F.surface, "        if (!observation.AboveEveryOrdinaryWindow)", "        if (false)", "Classify's z-order gate"],
  ["M-ai", F.surface, "        if (!observation.HitTestRoutesHere)", "        if (false)", "Classify's routing gate"],
  ["M-aj", F.surface, "        if (!observation.Inked)", "        if (false)", "the blank-target Degraded"],
  ["M-ak", F.surface, "            Win32PointerInterop.WsExNoactivate | Win32PointerInterop.WsExToolwindow,", "            Win32PointerInterop.WsExToolwindow,", "creating the window WS_EX_NOACTIVATE"],
  ["M-al", F.surface, "            Win32PointerInterop.WsExNoactivate | Win32PointerInterop.WsExToolwindow,", "            Win32PointerInterop.WsExNoactivate | Win32PointerInterop.WsExToolwindow\n                | Win32PointerInterop.WsExTransparent,", "leaving the target click-THROUGH"],
  ["M-am", F.surface, "                var answer = Win32PointerInterop.MaNoactivate;", "                var answer = (nint)1;", "answering MA_NOACTIVATE rather than MA_ACTIVATE"],
  ["M-an", F.surface, "            case Win32PointerInterop.WmLbuttondown:\n                Raise(window, PointerPressKind.Down, lParam);\n                return 0;", "            case Win32PointerInterop.WmLbuttondown:\n                return 0;", "raising the DOWN half of a click"],
  ["M-ao", F.surface, "            case Win32PointerInterop.WmLbuttonup:\n                Raise(window, PointerPressKind.Up, lParam);\n                return 0;", "            case Win32PointerInterop.WmLbuttonup:\n                return 0;", "raising the UP half of a click"],
  ["M-ap", F.surface, "            winner = Win32PointerInterop.WindowFromPoint(point);\n            if (winner == window)", "            winner = Win32PointerInterop.WindowFromPoint(point);\n            if (true)", "the raise loop's own re-ask"],
  ["M-aq", F.surface, "                && (Win32PointerInterop.GetPixel(dc, width - ControlMargin, ControlMargin - 1) & 0x00FFFFFF) == fill\n                && (Win32PointerInterop.GetPixel(dc, ControlMargin - 1, height - ControlMargin) & 0x00FFFFFF) == fill\n                && (Win32PointerInterop.GetPixel(dc, width - ControlMargin, height - ControlMargin) & 0x00FFFFFF) == fill;", "                ;", "three of the four ink control points"],
  ["M-ar", F.surface, "        Win32PointerInterop.ShowWindow(entry.Window, Win32PointerInterop.SwHide);", "        _ = entry;", "hiding the window on Close"],
  ["M-as", F.surface, "        if (stillVisible || stillRoutes)", "        if (false)", "Close's own confirmation"],
  ["M-at", F.surface, "        if (foregroundBefore != foregroundAfter)", "        if (false)", "Close's non-activation check"],
  ["M-au", F.surface, "        var inset = Math.Max(ControlMargin + 1, Math.Min(width, height) / 10);", "        var inset = 0;", "the disc's inset away from the control points"],
  ["M-av", F.surface, "        var listener = OnPress;\n        if (listener is null)\n        {\n            Interlocked.Increment(ref _pressesDropped);\n            return;\n        }", "        var listener = OnPress;\n        if (listener is null)\n        {\n            return;\n        }", "counting a press nobody listened for"],
  ["M-aw", F.surface, "        if (!_byWindow.TryGetValue(window, out var handle))\n        {\n            return;\n        }", "        var handle = 0;\n        if (false)\n        {\n            return;\n        }", "naming WHICH target the OS chose"],

  // ---- the field's arithmetic ----
  ["M-ax", F.field, "        TimeSpan.FromMilliseconds(60000.0 / Math.Clamp(perMinute, MinPerMinute, MaxPerMinute));", "        TimeSpan.FromMilliseconds(60000.0 / Math.Max(1, perMinute));", "the spawn dial's upper clamp"],
  ["M-ay", F.field, "            (int)Math.Round(baseDip * Math.Clamp(userPercent, MinSizePercent, MaxSizePercent) / 100.0),\n            MinSize,\n            MaxSize);", "            (int)Math.Round(baseDip * Math.Clamp(userPercent, MinSizePercent, MaxSizePercent) / 100.0),\n            1,\n            MaxSize);", "the clickable floor on the drawn size"],
  ["M-az", F.field, "            MinSize,\n            MaxSize);", "            MinSize,\n            int.MaxValue);", "the playfield ceiling"],
  ["M-ba", F.field, "        if (_bubbles.Count >= MaxConcurrent)", "        if (false)", "the field's concurrent cap"],
  ["M-bb", F.field, "            speed *= 1.0 + (boost / 100.0);", "            speed *= 1.0;", "the speed boost"],
  ["M-bc", F.field, "        var boost = Math.Clamp(speedBoostPercent, MinSpeedBoostPercent, MaxSpeedBoostPercent);", "        var boost = speedBoostPercent;", "the boost's own clamp"],
  ["M-bd", F.field, "            var y = bubble.Y - bubble.Speed;", "            var y = bubble.Y + bubble.Speed;", "floating UP rather than down"],
  ["M-be", F.field, "            var x = bubble.StartX + Wobble(bubble.AnimType, timeAlive);", "            var x = bubble.StartX;", "the horizontal wobble"],
  ["M-bf", F.field, "            if (y < _playY - bubble.Size - ExitMargin)", "            if (false)", "the exit at the top of the play area"],
  ["M-bg", F.field, "                var fade = bubble.FadeAlpha - PopFadePerStep;", "                var fade = bubble.FadeAlpha - 1.0;", "the pop animation's own length"],
  ["M-bh", F.field, "            if (_bubbles[i].Popping)\n            {\n                return false;\n            }", "            if (false)\n            {\n                return false;\n            }", "a second hit scoring twice"],
  ["M-bi", F.field, "        var dropped = _bubbles.ToArray();\n        _bubbles.Clear();\n        return dropped;", "        var dropped = _bubbles.ToArray();\n        Popped += dropped.Length;\n        _bubbles.Clear();\n        return dropped;", "Clear counting neither a pop nor a miss"],
  ["M-bj", F.field, "        0 => Math.Sin(timeAlive * 6) * 25,", "        0 => Math.Sin(timeAlive * 6) * 30,", "wobble 0's amplitude"],
  ["M-bk", F.field, "        1 => Math.Sin(timeAlive * 7.5) * 30,", "        1 => Math.Sin(timeAlive * 6.5) * 30,", "wobble 1's rate — the one that bounds the race"],
  ["M-bl", F.field, "        2 => Math.Cos(timeAlive * 5.4) * 25,", "        2 => Math.Sin(timeAlive * 5.4) * 25,", "wobble 2 being a COSINE"],
  ["M-bm", F.field, "    public const double TimeAlivePerStep = 0.02;", "    public const double TimeAlivePerStep = 0.04;", "the logical time increment"],
  ["M-bn", F.field, "    public static bool OneStepNeverCarriesABubbleOffItsOwnCentre => MaxStepDisplacement < MinSize / 2.0;", "    public static bool OneStepNeverCarriesABubbleOffItsOwnCentre => true;", "the race bound being an inequality at all"],
  ["M-bo", F.field, "        var startX = _playX + _random.Next(50, span);", "        var startX = _playX;", "the horizontal spawn band"],

  // ---- the presenter ----
  ["M-bp", F.presenter, "            if (!_surface.CanReachAPointer)", "            if (false)", "Engage's pointer-channel gate"],
  ["M-bq", F.presenter, "            if (area is null)", "            if (false)", "Engage's display gate"],
  ["M-br", F.presenter, "            SpawnOnceLocked();\n            return Placed();", "            return Placed();", "the immediate first spawn"],
  ["M-bs", F.presenter, "            _surface.Pump(PumpBudget);", "            _ = PumpBudget;", "pumping presses before the field moves"],
  ["M-bt", F.presenter, "                var state = _surface.Move(target, new PointerBounds(x, y, size, size));\n                if (state is CapabilityState.Available)\n                {\n                    routable++;\n                }", "                _surface.Move(target, new PointerBounds(x, y, size, size));\n                routable++;", "counting only the targets the OS routes to"],
  ["M-bu", F.presenter, "                return _engaged && (_targetByBubble.Count == 0 || _routable > 0);", "                return _engaged;", "the dot's routing clause"],
  ["M-bv", F.presenter, "                return _engaged && (_targetByBubble.Count == 0 || _routable > 0);", "                return _engaged && _routable > 0;", "the empty-field disjunction"],
  ["M-bw", F.presenter, "        if (press.Kind != PointerPressKind.Down)\n        {", "        if (false)\n        {", "popping on the DOWN only"],
  ["M-bx", F.presenter, "                    _surface.Close(goneTarget);", "                    _ = goneTarget;", "closing a target whose bubble left"],
  ["M-by", F.presenter, "                RestartSpawnTimerLocked();\n                return Placed();", "                return Placed();", "re-timing a live field when the dial moves"],
  ["M-bz", F.presenter, "                foreach (var target in _targetByBubble.Values.ToArray())\n                {\n                    _surface.Close(target);\n                }", "                foreach (var target in Array.Empty<int>())\n                {\n                    _surface.Close(target);\n                }", "Withdraw taking every target down"],
  ["M-ca", F.presenter, "            _spawnTimer?.Dispose();\n            _spawnTimer = null;\n            _stepTimer?.Dispose();\n            _stepTimer = null;", "            _spawnTimer = null;\n            _stepTimer = null;", "Withdraw stopping both cadences"],

  // ---- the module ----
  ["M-cb", F.effect, "        if (!Signal.IsBound)", "        if (false)", "the unbound-UI-thread refusal"],
  ["M-cc", F.effect, "        if (_surface is null)\n        {\n            return new CapabilityState.Unavailable(new CapabilityReason(\n                EffectReasonCodes.EffectNoSurface,", "        if (false)\n        {\n            return new CapabilityState.Unavailable(new CapabilityReason(\n                EffectReasonCodes.EffectNoSurface,", "the no-surface refusal"],
  ["M-cd", F.effect, "        if (up > 0 && routable == 0)", "        if (false)", "the unplayable-field Degraded"],
  ["M-ce", F.effect, "        if (up > 0 && routable == 0)", "        if (routable == 0)", "the up>0 half of it"],
  ["M-cf", F.effect, "        if (engaged is CapabilityState.Unavailable channel\n            && channel.Reason.Code == EffectReasonCodes.PointerSurfaceUnavailable)", "        if (engaged is CapabilityState.Unavailable)", "narrowing only the CHANNEL refusal"],
  ["M-cg", F.effect, "    protected override bool WorkIsRunning => _surface is { Running: true };", "    protected override bool WorkIsRunning => _surface is { Showing: true };", "the dot reading Running rather than Showing"],
  ["M-ch", F.effect, "        if (_surface is not { Showing: true })\n        {\n            return;\n        }\n\n        Signal.Post(_surface.Withdraw);", "        Signal.Post(_surface.Withdraw);", "the showing guard in front of the withdraw"],
  ["M-ci", F.effect, "        var clamped = Math.Clamp(perMinute, BubblePopField.MinPerMinute, BubblePopField.MaxPerMinute);", "        var clamped = perMinute;", "the frequency setter's clamp"],
  ["M-cj", F.effect, "        _preset.Mutate(p => p.PerMinute = perMinute);\n        Refresh();", "        _preset.Mutate(p => p.PerMinute = perMinute);", "the re-pace after the frequency dial"],
  ["M-ck", F.effect, "    public const string EffectId = \"bubbles\";", "    public const string EffectId = \"bubblepop\";", "the rack key being upstream's"],

  // ---- the document ----
  ["M-cl", F.document, "        set => _perMinute = Math.Clamp(value, BubblePopField.MinPerMinute, BubblePopField.MaxPerMinute);", "        set => _perMinute = value;", "the document's frequency clamp"],
  ["M-cm", F.document, "        set => _sizePercent = Math.Clamp(value, BubblePopField.MinSizePercent, BubblePopField.MaxSizePercent);", "        set => _sizePercent = value;", "the document's size clamp"],
  ["M-cn", F.document, "        set => _speedBoostPercent = Math.Clamp(\n            value, BubblePopField.MinSpeedBoostPercent, BubblePopField.MaxSpeedBoostPercent);", "        set => _speedBoostPercent = value;", "the document's speed clamp"],

  // ---- the panel ----
  ["M-co", F.notices, "        if (routable == 0)\n        {\n            return $\"Bubble Pop is running and you CANNOT hit it:", "        if (false)\n        {\n            return $\"Bubble Pop is running and you CANNOT hit it:", "the unplayable-field sentence"],
  ["M-cp", F.notices, "        if (targetsUp == 0)\n        {\n            return \"Bubble Pop is running. No bubble is on screen at this instant;", "        if (false)\n        {\n            return \"Bubble Pop is running. No bubble is on screen at this instant;", "the between-spawns sentence"],
  ["M-cq", F.notices, "        if (!canReachAPointer)", "        if (false)", "the no-channel sentence"],
  ["M-cr", F.notices, "        if (presses == 0)\n        {\n            return \"No click has been delivered to a bubble yet.", "        if (false)\n        {\n            return \"No click has been delivered to a bubble yet.", "the delivery line reading as evidence"],

  // ---- the factory ----
  ["M-cs", F.factory, "        PointerHostPlatform.Linux => new UnsupportedPointerSurface(", "        PointerHostPlatform.Linux => new Win32PointerSurface(),\n        (PointerHostPlatform)999 => new UnsupportedPointerSurface(", "Linux refusing rather than claiming the Win32 backend"],
  ["M-ct", F.factory, "        PointerHostPlatform.MacOs => new UnsupportedPointerSurface(", "        PointerHostPlatform.MacOs => new Win32PointerSurface(),\n        (PointerHostPlatform)998 => new UnsupportedPointerSurface(", "macOS refusing"],
  ["M-cu", F.factory, "            + \"the gate below runs, a caller must treat its clickable half as absent and say so — never put a \"\n            + \"target on a desktop whose focus behaviour nobody has measured. \" + LinuxManualGate),", "            + \"the gate below runs, a caller must treat its clickable half as absent and say so — never put a \"\n            + \"target on a desktop whose focus behaviour nobody has measured. \"),", "the manual gate travelling WITH the refusal"],
];

const args = process.argv.slice(2);
const only = args.includes("--only") ? args[args.indexOf("--only") + 1].split(",") : null;
const checkOnly = args.includes("--check");
const logName = args.includes("--log") ? args[args.indexOf("--log") + 1] : "sweep.log";
const logPath = path.join(HERE, logName);

function normalise(text) {
  return text.includes(CRLF) ? text.split(CRLF).join(LF) : text;
}

function check() {
  let missing = 0;
  const seen = new Set();
  for (const [id, file, needle] of MUTATIONS) {
    if (seen.has(id)) {
      console.error(`${id}  DUPLICATE ID`);
      missing++;
    }

    seen.add(id);
    const normalised = normalise(fs.readFileSync(file, "utf8"));
    if (!normalised.includes(needle)) {
      console.error(`${id}  NEEDLE NOT FOUND in ${path.basename(file)}`);
      missing++;
    }
  }

  console.log(`${MUTATIONS.length} mutations, ${missing} needle problem(s)`);
  process.exitCode = missing === 0 ? 0 : 1;
}

function run() {
  const started = Date.now();
  const lines = [];
  let caught = 0;
  let survived = 0;
  let notPatched = 0;

  for (const [id, file, needle, replacement, what] of MUTATIONS) {
    if (only && !only.includes(id)) {
      continue;
    }

    // The working tree is CRLF on this platform (git's autocrlf) while these needles are written
    // with LF, so matching is done on a normalised copy and the mutant is written back in the
    // file's OWN line endings — SP-112's round-1 defect, not repeated.
    const original = fs.readFileSync(file, "utf8");
    const crlf = original.includes(CRLF);
    const normalised = crlf ? original.split(CRLF).join(LF) : original;
    if (!normalised.includes(needle)) {
      notPatched++;
      lines.push(`${id}  NOT-PATCHED  ${what}  (needle no longer matches ${path.basename(file)})`);
      console.log(lines[lines.length - 1]);
      continue;
    }

    const mutated = normalised.replace(needle, replacement);
    fs.writeFileSync(file, crlf ? mutated.split(LF).join(CRLF) : mutated, "utf8");
    let verdict;
    try {
      const result = spawnSync(
        "node",
        [
          path.join(REPO, "client", "tools", "gate", "with-slot.mjs"),
          "--slots", "3", "--",
          "dotnet", "test",
          path.join(REPO, "client", "tests", "CcpClient.Tests", "CcpClient.Tests.csproj"),
          "-c", "Debug", "--nologo", "--filter", FILTER,
        ],
        { encoding: "utf8", timeout: 420000, shell: false });

      const out = `${result.stdout ?? ""}${result.stderr ?? ""}`;
      if (result.error && result.error.code === "ETIMEDOUT") {
        verdict = "CAUGHT (wedged)";
      } else if (/error CS\d+/.test(out)) {
        verdict = "CAUGHT (does not compile)";
      } else if (result.status === 0) {
        verdict = "SURVIVED";
      } else {
        verdict = "CAUGHT";
      }
    } finally {
      fs.writeFileSync(file, original, "utf8");
    }

    if (verdict === "SURVIVED") {
      survived++;
    } else {
      caught++;
    }

    lines.push(`${id}  ${verdict}  ${what}`);
    console.log(lines[lines.length - 1]);
  }

  const summary = [
    `SP-113 mutation sweep — ${new Date().toISOString()}`,
    `filter: ${FILTER}`,
    "",
    ...lines,
    "",
    `caught=${caught} survived=${survived} not-patched=${notPatched} total=${caught + survived + notPatched}`,
    `elapsed=${Math.round((Date.now() - started) / 1000)}s`,
    "",
  ].join("\n");
  fs.writeFileSync(logPath, summary, "utf8");
  console.log(summary.split("\n").slice(-4).join("\n"));

  // The tree must be byte-identical afterwards; a sweep that leaves a mutation behind is worse
  // than no sweep at all.
  const dirty = execFileSync("git", ["status", "--porcelain", "client/src"], { cwd: REPO, encoding: "utf8" });
  if (dirty.trim().length > 0) {
    console.error(`SWEEP LEFT THE TREE DIRTY:\n${dirty}`);
    process.exitCode = 1;
  } else {
    console.log("tree restored byte-identically (git status --porcelain client/src is empty)");
  }
}

if (checkOnly) {
  check();
} else {
  run();
}
