#!/usr/bin/env node
// SP-112's mutation sweep driver.
//
// It lives INSIDE this packet's folder and writes only inside it: a previous wave's driver wrote
// three levels above its own root into the shared checkout, where a parallel lane's `git add -A`
// would have swept it into an unrelated commit.
//
// One mutation at a time: patch a single product file, run a bounded filter, restore the file
// byte-identically, record caught/survived. A mutation that does not patch (its needle no longer
// matches) is reported as NOT-PATCHED rather than silently counted as caught.
//
// Usage: node spine-tasks/SP-112-second-consumer/sweep.mjs [--only M-a,M-b] [--log name.log]

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
  "FullyQualifiedName~BubbleCountModuleTests",
  "FullyQualifiedName~BubbleCountCapabilityTests",
  "FullyQualifiedName~StudioSurfaceNoticeTests",
  "FullyQualifiedName~VideoSurfacePresenterTests",
  "FullyQualifiedName~MandatoryVideoModuleTests",
  "FullyQualifiedName~LockCardModuleTests",
  "FullyQualifiedName~SecondEffectSpineTests",
  "FullyQualifiedName~ContinuousEffectSpineTests",
  "FullyQualifiedName~AudioModuleSpineTests",
].join("|");

const F = {
  schedule: path.join(SRC, "Effects", "BubbleCountSchedule.cs"),
  game: path.join(SRC, "Effects", "BubbleCountGame.cs"),
  answer: path.join(SRC, "Effects", "BubbleCountAnswer.cs"),
  effect: path.join(SRC, "Effects", "BubbleCountEffect.cs"),
  presenter: path.join(SRC, "Effects", "VideoSurfacePresenter.cs"),
  placement: path.join(SRC, "Effects", "PrimaryDisplayPlacement.cs"),
  notices: path.join(SRC, "Views", "Pages", "BubbleCountPanelNotices.cs"),
  document: path.join(SRC, "Session", "BubbleCountPresetDocument.cs"),
};

// id, file, needle, replacement, what it breaks
const MUTATIONS = [
  // ---- the pacing law ----
  ["M-a", F.schedule, "var effective = Math.Clamp(perHour, MinPerHour, MaxPerHour);", "var effective = perHour;", "the dial's clamp"],
  ["M-b", F.schedule, "var variance = baseInterval * JitterFraction;", "var variance = 0.0;", "the +/-20% jitter"],
  ["M-c", F.schedule, "return TimeSpan.FromSeconds(Math.Max(MinimumInterval.TotalSeconds, seconds));", "return TimeSpan.FromSeconds(seconds);", "the sixty-second floor"],
  ["M-d", F.schedule, "public const int MaxPerHour = 10;", "public const int MaxPerHour = 20;", "the ten-per-hour ceiling (the video module's twenty)"],

  // ---- the counting arithmetic ----
  ["M-e", F.game, "BubbleCountDifficulty.Easy => 3,", "BubbleCountDifficulty.Easy => 5,", "Easy's base rate"],
  ["M-f", F.game, "BubbleCountDifficulty.Hard => 8,", "BubbleCountDifficulty.Hard => 5,", "Hard's base rate"],
  ["M-g", F.game, "return Math.Max(MinimumTarget, target);", "return target;", "the three-bubble floor"],
  ["M-h", F.game, "var variance = scaled * TargetJitterFraction;", "var variance = 0.0;", "the target's +/-20%"],
  ["M-i", F.game, "var seconds = duration > TimeSpan.Zero ? duration.TotalSeconds : FallbackDuration.TotalSeconds;\n        var scaled", "var seconds = duration.TotalSeconds;\n        var scaled", "the 30 s fallback in Target"],
  ["M-j", F.game, "var even = seconds * 1000.0 / Math.Max(1, target);\n        return TimeSpan.FromMilliseconds(Math.Max(1.0, even * SpawnIntervalFactor));", "var even = seconds * 1000.0 / Math.Max(1, target);\n        return TimeSpan.FromMilliseconds(Math.Max(1.0, even));", "the 0.7 spacing factor"],
  ["M-k", F.game, "roll < SpawnChance || shown < target / 2;", "roll < SpawnChance;", "the below-half-target clause"],
  ["M-l", F.game, "roll < SpawnChance || shown < target / 2;", "shown < target / 2;", "the 0.7 probability clause"],

  // ---- the run / painter ----
  ["M-m", F.game, "if (isLeadInTick\n                || BubbleCountArithmetic.TickSpawns(_bubbles.Count, Target, _random.NextDouble()))", "if (BubbleCountArithmetic.TickSpawns(_bubbles.Count, Target, _random.NextDouble()))", "the unconditional lead-in spawn"],
  ["M-n", F.game, "while (elapsed >= _nextTickAt && _bubbles.Count < Target)", "while (elapsed >= _nextTickAt)", "the target cap on spawning"],
  ["M-o", F.game, "_nextTickAt = BubbleCountArithmetic.SpawnLeadIn;", "_nextTickAt = TimeSpan.Zero;", "the 1500 ms lead-in"],
  ["M-p", F.game, "if (elapsed < bubble.BornAt || elapsed >= bubble.GoneAt)", "if (elapsed < bubble.BornAt)", "the bubble's END of life"],
  ["M-q", F.game, "if (elapsed < bubble.BornAt || elapsed >= bubble.GoneAt)", "if (false)", "both ends of a bubble's life"],
  ["M-r", F.game, "        Draw(frame, bubble, elapsed);", "        _ = bubble;", "the paint itself"],
  ["M-s", F.game, "if (radius < 1.0)\n        {\n            return;\n        }", "if (radius < 0.0)\n        {\n            return;\n        }", "the sub-pixel guard"],
  ["M-t", F.game, "var left = Math.Max(0, (int)Math.Floor(centreX - radius));", "var left = (int)Math.Floor(centreX - radius);", "the left clamp into the picture"],
  ["M-u", F.game, "Target = BubbleCountArithmetic.Target(Difficulty, Duration, _targetRoll);", "Target = BubbleCountArithmetic.Target(Difficulty, Duration, _random.NextDouble());", "the reused jitter roll"],
  ["M-v", F.game, "Duration = duration > TimeSpan.Zero ? duration : BubbleCountArithmetic.FallbackDuration;", "Duration = duration;", "Recompute's own fallback"],
  ["M-w", F.game, "        Recompute(clip.Duration);", "        _ = clip;", "Opening's re-derivation"],
  ["M-x", F.game, "(_random.NextDouble() * PlacementXSpan) + PlacementXOffset,", "0.5,", "the horizontal placement"],
  ["M-y", F.game, "MinLifetime + (LifetimeSpan * _random.NextDouble())", "MinLifetime", "the lifetime's random span"],
  ["M-z", F.game, "return (1.0 + (PopScaleGrowth * popped), 1.0 - popped);", "return (1.0, 1.0);", "the pop fade"],

  // ---- the answer machine ----
  ["M-aa", F.answer, "if (!char.IsAsciiDigit(character))", "if (false)", "the digits-only rule"],
  ["M-ab", F.answer, "return virtualKey == SubmitVirtualKey ? Submit() : BubbleCountStep.Ignored;", "return BubbleCountStep.Ignored;", "Enter as the submit key"],
  ["M-ac", F.answer, "public const int SubmitVirtualKey = 0x0D;", "public const int SubmitVirtualKey = 0x0E;", "the submit key's code"],
  ["M-ad", F.answer, "            Feedback = NotANumberMessage;\n            _typed = string.Empty;\n            return BubbleCountStep.NotANumber;", "            Feedback = NotANumberMessage;\n            _typed = string.Empty;\n            AttemptsRemaining--;\n            return BubbleCountStep.NotANumber;", "an empty Enter costing nothing"],
  ["M-ae", F.answer, "        AttemptsRemaining--;\n        _typed = string.Empty;", "        _typed = string.Empty;", "the attempt counter"],
  ["M-af", F.answer, "if (AttemptsRemaining <= 0)", "if (AttemptsRemaining <= -99)", "the exhausted ending"],
  ["M-ag", F.answer, "Feedback = guess < _correct ? TooLowMessage : TooHighMessage;", "Feedback = guess < _correct ? TooHighMessage : TooLowMessage;", "the hint's direction"],
  ["M-ah", F.answer, "            _typed = _typed[..^1];\n            Feedback = string.Empty;\n            return BubbleCountStep.Typed;", "            Feedback = string.Empty;\n            return BubbleCountStep.Typed;", "backspace editing"],
  ["M-ai", F.answer, "        if (isCancel)\n        {\n            return BubbleCountStep.GaveUp;\n        }", "        if (isCancel)\n        {\n            return BubbleCountStep.Ignored;\n        }", "Escape ending the card"],
  ["M-aj", F.answer, "    public string Progress => Feedback.Length == 0\n        ? $\"Attempts remaining: {AttemptsRemaining}\"\n        : $\"{Feedback}   Attempts remaining: {AttemptsRemaining}\";", "    public string Progress => $\"Attempts remaining: {AttemptsRemaining}\";", "the folded feedback line"],
  ["M-ak", F.answer, "        if (guess == _correct)", "        if (guess != _correct)", "the correct answer"],

  // ---- the module ----
  ["M-al", F.effect, "        if (_surface.Showing)\n        {\n            return null;\n        }", "        if (false)\n        {\n            return null;\n        }", "Compose's already-showing guard"],
  ["M-am", F.effect, "        if (_presence.IsPrompting)\n        {\n            return null;\n        }", "        if (false)\n        {\n            return null;\n        }", "Compose's already-prompting guard"],
  ["M-an", F.effect, "        if (!_surface.CanReachADisplay || !_presence.CanReachAUser)\n        {\n            return null;\n        }", "        if (!_presence.CanReachAUser)\n        {\n            return null;\n        }", "Compose's display guard"],
  ["M-ao", F.effect, "        if (!_surface.CanReachADisplay || !_presence.CanReachAUser)\n        {\n            return null;\n        }", "        if (!_surface.CanReachADisplay)\n        {\n            return null;\n        }", "Compose's desktop guard"],
  ["M-ap", F.effect, "            Resolve(run, BubbleCountResolution.Abandoned);\n            RaiseChanged();\n            return;", "            RaiseChanged();\n            return;", "the refused clip's resolution"],
  ["M-aq", F.effect, "        ArmSafety(run.Duration + BubbleCountArithmetic.SafetyMargin);", "        _ = run;", "the safety end"],
  ["M-ar", F.effect, "    private void OnClipEnded()\n    {\n        CancelSafety();", "    private void OnClipEnded()\n    {\n        _ = 0;", "cancelling the safety on a clean end"],
  ["M-as", F.effect, "        if (_surface.LastPlacement is not CapabilityState.Available)\n        {\n            Resolve(run, BubbleCountResolution.Abandoned);\n            ReSchedule();\n            return;\n        }", "        if (false)\n        {\n            Resolve(run, BubbleCountResolution.Abandoned);\n            ReSchedule();\n            return;\n        }", "abandoning a clip the surface stopped holding"],
  ["M-at", F.effect, "        if (run.BubblesShown == 0)", "        if (false)", "abandoning a clip that showed no bubble"],
  ["M-au", F.effect, "        if (_presence.IsPrompting)\n        {\n            Resolve(run, BubbleCountResolution.Abandoned);\n            ReSchedule();\n            return;\n        }", "        if (false)\n        {\n            Resolve(run, BubbleCountResolution.Abandoned);\n            ReSchedule();\n            return;\n        }", "standing down rather than stealing another card"],
  ["M-av", F.effect, "            _presence.Dismiss();\n            Resolve(run, BubbleCountResolution.Refused);", "            Resolve(run, BubbleCountResolution.Refused);", "the load-bearing dismiss on a refusal"],
  ["M-aw", F.effect, "            if (!ReferenceEquals(_answer, answer))\n            {\n                return;\n            }\n\n            run = _run;", "            run = _run;", "the keystroke identity guard"],
  ["M-ax", F.effect, "        if (playing)\n        {\n            _surface.End();\n        }", "        _surface.End();", "the guarded End on disarm"],
  ["M-ay", F.effect, "        if (answer is not null)\n        {\n            _presence.Dismiss();\n        }", "        _presence.Dismiss();", "the guarded Dismiss on disarm"],
  ["M-az", F.effect, "        && (!ClipIsUp || _surface.Running)", "        && true", "the dot's MOTION clause"],
  ["M-ba", F.effect, "        && (!CardIsUp || _presence.HoldsTheInput);", "        && true;", "the dot's DEMAND clause"],
  ["M-bb", F.effect, "        && _surface.CanReachADisplay\n        && _presence.CanReachAUser", "        && _presence.CanReachAUser", "the dot's display channel"],
  ["M-bc", F.effect, "        && _surface.CanReachADisplay\n        && _presence.CanReachAUser", "        && _surface.CanReachADisplay", "the dot's desktop channel"],
  ["M-bd", F.effect, "        ScheduleArmed\n        && _surface.CanReachADisplay", "        true\n        && _surface.CanReachADisplay", "the dot's CLOCK clause"],
  ["M-be", F.effect, "                return _playing && _surface.Showing;", "                return _surface.Showing;", "the of-MINE qualifier on the clip"],
  ["M-bf", F.effect, "                return _answer is not null && _presence.IsPrompting;", "                return _presence.IsPrompting;", "the of-MINE qualifier on the card"],
  ["M-bg", F.effect, "                noDisplay ? EffectReasonCodes.VideoSurfaceUnavailable : EffectReasonCodes.InputCaptureUnavailable,", "EffectReasonCodes.VideoSurfaceUnavailable,", "the two-channel refusal codes"],
  ["M-bh", F.effect, "        if (_pool.ActiveCount == 0)", "        if (false)", "the empty-pool Degraded"],
  ["M-bi", F.effect, "        BubbleCountSchedule.Interval(_preset.Current.PerHour, _random.NextDouble());", "TimeSpan.FromHours(1);", "the module's own pacing"],
  ["M-bj", F.effect, "        RefreshSchedule();\n        RaiseChanged();", "        RaiseChanged();", "the re-pace from the END of a game"],

  // ---- the shared presenter's new seam ----
  ["M-bk", F.presenter, "            if (_clip is not null)\n            {\n                return Record(new CapabilityState.Unavailable(new CapabilityReason(\n                    VideoReasonCodes.VideoAlreadyPlaying,", "            if (false)\n            {\n                return Record(new CapabilityState.Unavailable(new CapabilityReason(\n                    VideoReasonCodes.VideoAlreadyPlaying,", "the already-playing refusal"],
  ["M-bl", F.presenter, "        painter?.Opening(clip.Info);", "        _ = clip.Info;", "telling the painter about the clip"],
  ["M-bm", F.presenter, "        painter?.Paint(first, TimeSpan.Zero);", "        _ = first;", "painting the FIRST picture"],
  ["M-bn", F.presenter, "        painter?.Paint(frame, elapsed);", "        _ = elapsed;", "painting every later picture"],

  // ---- the shared placement ----
  ["M-bo", F.placement, "        var width = Math.Max(1, (int)(display.Width * widthFraction));", "        var width = (int)(display.Width * widthFraction);", "the one-pixel floor"],
  ["M-bp", F.placement, "            if (displays[i].IsPrimary)", "            if (false)", "picking the PRIMARY display"],
  ["M-bq", F.placement, "            display.X + ((display.Width - width) / 2),", "            display.X,", "the centring"],

  // ---- the panel ----
  ["M-br", F.notices, "        if (dot == EffectDotState.Armed && playing)", "        if (false)", "the frozen-picture sentence"],
  ["M-bs", F.notices, "        if (dot == EffectDotState.Armed && asking)", "        if (false)", "the keyboard-elsewhere sentence"],
  ["M-bt", F.notices, "            BubbleCountResolution.Abandoned =>\n                \" The last clip could not really be watched, so no question was asked and nothing was \"\n                + \"scored.\",", "            BubbleCountResolution.Abandoned =>\n                \" You used up every try on the last one.\",", "abandoned not being reported as failed"],
  ["M-bu", F.notices, "        return $\"{video} {input}\";", "        return video;", "quoting the input capability at all"],

  // ---- the document ----
  ["M-bv", F.document, "        set => _difficulty = Enum.IsDefined(value) ? value : BubbleCountDifficulty.Medium;", "        set => _difficulty = value;", "the difficulty's fall-through default"],
  ["M-bw", F.document, "        set => _perHour = Math.Clamp(value, BubbleCountSchedule.MinPerHour, BubbleCountSchedule.MaxPerHour);", "        set => _perHour = value;", "the document's own clamp"],
];

const args = process.argv.slice(2);
const only = args.includes("--only") ? args[args.indexOf("--only") + 1].split(",") : null;
const logName = args.includes("--log") ? args[args.indexOf("--log") + 1] : "sweep.log";
const logPath = path.join(HERE, logName);

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
    // file's OWN line endings. A sweep whose multi-line needles silently never match would report
    // "not patched" for every interesting predicate — which is exactly what round 1 did.
    const original = fs.readFileSync(file, "utf8");
    const crlf = original.includes(CRLF);
    const normalised = crlf ? original.split(CRLF).join(LF) : original;
    if (!normalised.includes(needle)) {
      notPatched++;
      lines.push(`${id}  NOT-PATCHED  ${what}  (needle no longer matches ${path.basename(file)})`);
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
        { encoding: "utf8", timeout: 300000, shell: false });

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
    `SP-112 mutation sweep — ${new Date().toISOString()}`,
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

run();
