#!/usr/bin/env node
// SP-117 mutation sweep.
//
// Lives inside this packet's folder and writes ONLY inside it (SP-112's rule, after a previous
// wave's driver wrote three levels above its own root into the shared checkout).
//
// The line-ending discipline is SP-112 round 1's, paid for once already: the working tree is CRLF
// and needles are written LF, so every needle is normalised for MATCHING and the mutant is written
// back in the file's OWN endings. A sweep that silently skips its hardest cases is worse than no
// sweep.
//
// THE SECOND FALSE-CLEAN CHANNEL, named here rather than left implicit, because this packet's own
// record argues that a sweep which can manufacture a SURVIVOR can manufacture a false CLEAN — and
// leaving this one unstated would be that standard applied unevenly.
//
// `runSuite` decides CAUGHT from a NON-ZERO EXIT CODE, and `dotnet test` exits non-zero for reasons
// that are not a failing assertion: a mutant that does not COMPILE, a `--filter` that matches no
// test, a crashed test host, or the timeout below. **Every one of those registers as a catch**, so
// the exit code alone cannot distinguish "a fact noticed" from "nothing was ever measured".
//
// `compiles()` closes the largest of those channels by building the product project BEFORE the
// suite runs and reporting NOT COMPILED as its own outcome, the way NOT PATCHED already is: a
// mutation the compiler rejected is a mutation no test was ever asked about.
//
// **It was added after round 3 and therefore did NOT gate rounds 1-4 of SP-117's own sweep.** The
// bound that makes those rounds sound anyway is stated rather than assumed, and was measured:
// all 60 mutations are type-preserving, and no project under `client/` sets
// `TreatWarningsAsErrors` or `WarningsAsErrors` — so the one mutation that could plausibly have
// failed to build, M-x (removing the sole `Changed?.Invoke()` and orphaning the event), was applied
// by hand and built: `CS0067` at `FlashDraw.cs:131` is a WARNING, `Build succeeded`, 0 errors. Its
// CAUGHT is a real assertion failure. The remaining channels (empty filter, crashed host, timeout)
// are unclosed and are named in `record.md` §6.
//
// Usage: node spine-tasks/SP-117-last-three/sweep.mjs [--only M-a,M-b] [--round N]

import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, "..", "..");

const SRC = "client/src/CcpClient.Desktop";
const F = {
  draw: `${SRC}/Effects/FlashDraw.cs`,
  doc: `${SRC}/Session/VisualsPresetDocument.cs`,
  presenter: `${SRC}/Effects/FlashSurfacePresenter.cs`,
  dial: `${SRC}/Effects/IntensityDial.cs`,
  ramp: `${SRC}/Effects/IntensityRampEffect.cs`,
  rampNotices: `${SRC}/Views/Pages/RampPanelNotices.cs`,
  participant: `${SRC}/Session/SessionParticipant.cs`,
  notices: `${SRC}/Views/Pages/VisualsPanelNotices.cs`,
  page: `${SRC}/Views/Pages/StudioPage.axaml.cs`,
};

// The unit filter: this packet's own facts plus every landed suite that consumes a symbol it
// mutated (FlashSurfacePresenter, IntensityDial, IntensityRampEffect, SessionParticipant).
const UNIT =
  "FullyQualifiedName~VisualsModuleTests|" +
  "FullyQualifiedName~FlashSurfacePresenterTests|" +
  "FullyQualifiedName~FlashEndToEndObservations|" +
  "FullyQualifiedName~NonDrawingEffectSpineTests|" +
  "FullyQualifiedName~IntensityRampEffectTests|" +
  "FullyQualifiedName~StudioSurfaceNoticeTests|" +
  "FullyQualifiedName~SessionSpineTests|" +
  "FullyQualifiedName~SecondEffectSpineTests";

const HEADLESS = "FullyQualifiedName~StudioRackHeadlessTests";

/** @type {{id:string,file:string,find:string,replace:string,what:string,suite:"unit"|"headless"}[]} */
const MUTATIONS = [
  // ---- FlashDraw: the validating boundary --------------------------------------------------
  { id: "M-a", file: F.draw, what: "scale lower bound not checked", suite: "unit",
    find: `        ArgumentOutOfRangeException.ThrowIfLessThan(
            scalePercent, VisualsPresetDocument.MinImageScalePercent, nameof(scalePercent));\n`,
    replace: `` },
  { id: "M-b", file: F.draw, what: "scale upper bound not checked", suite: "unit",
    find: `        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            scalePercent, VisualsPresetDocument.MaxImageScalePercent, nameof(scalePercent));\n`,
    replace: `` },
  { id: "M-c", file: F.draw, what: "opacity lower bound not checked", suite: "unit",
    find: `        ArgumentOutOfRangeException.ThrowIfLessThan(
            opacityPercent, VisualsPresetDocument.MinFlashOpacityPercent, nameof(opacityPercent));\n`,
    replace: `` },
  { id: "M-d", file: F.draw, what: "opacity upper bound not checked", suite: "unit",
    find: `        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            opacityPercent, VisualsPresetDocument.MaxFlashOpacityPercent, nameof(opacityPercent));\n`,
    replace: `` },
  { id: "M-e", file: F.draw, what: "duration lower bound not checked", suite: "unit",
    find: `        ArgumentOutOfRangeException.ThrowIfLessThan(
            durationSeconds, VisualsPresetDocument.MinFlashDurationSeconds, nameof(durationSeconds));\n`,
    replace: `` },
  { id: "M-f", file: F.draw, what: "duration upper bound not checked", suite: "unit",
    find: `        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            durationSeconds, VisualsPresetDocument.MaxFlashDurationSeconds, nameof(durationSeconds));\n`,
    replace: `` },
  { id: "M-g", file: F.draw, what: "opacity fraction over 255 not 100", suite: "unit",
    find: `public double Opacity => OpacityPercent / 100.0;`,
    replace: `public double Opacity => OpacityPercent / 255.0;` },
  { id: "M-h", file: F.draw, what: "the one-second lifetime grace dropped", suite: "unit",
    find: `        TimeSpan.FromSeconds(DurationSeconds)
        + TimeSpan.FromMilliseconds(FlashSurfacePresenter.LifetimeGraceMilliseconds);`,
    replace: `        TimeSpan.FromSeconds(DurationSeconds);` },
  { id: "M-i", file: F.draw, what: "the grace added as SECONDS", suite: "unit",
    find: `        + TimeSpan.FromMilliseconds(FlashSurfacePresenter.LifetimeGraceMilliseconds);`,
    replace: `        + TimeSpan.FromSeconds(FlashSurfacePresenter.LifetimeGraceMilliseconds);` },
  { id: "M-j", file: F.draw, what: "the default scale is not WPF's", suite: "unit",
    find: `        VisualsPresetDocument.DefaultImageScalePercent,
        VisualsPresetDocument.DefaultFlashOpacityPercent,`,
    replace: `        VisualsPresetDocument.MaxImageScalePercent,
        VisualsPresetDocument.DefaultFlashOpacityPercent,` },
  { id: "M-k", file: F.draw, what: "the reading ignores the document's scale", suite: "unit",
    find: `            document.ImageScalePercent, document.FlashOpacityPercent, document.FlashDurationSeconds);`,
    replace: `            VisualsPresetDocument.DefaultImageScalePercent, document.FlashOpacityPercent, document.FlashDurationSeconds);` },

  // ---- The document: WPF's clamps ----------------------------------------------------------
  { id: "M-l", file: F.doc, what: "scale floor removed", suite: "unit",
    find: `    public const int MinImageScalePercent = 50;`,
    replace: `    public const int MinImageScalePercent = 0;` },
  { id: "M-m", file: F.doc, what: "scale ceiling widened", suite: "unit",
    find: `    public const int MaxImageScalePercent = 250;`,
    replace: `    public const int MaxImageScalePercent = 1000;` },
  { id: "M-n", file: F.doc, what: "opacity floor removed — the invisible-surface route", suite: "unit",
    find: `    public const int MinFlashOpacityPercent = 10;`,
    replace: `    public const int MinFlashOpacityPercent = 0;` },
  { id: "M-o", file: F.doc, what: "opacity ceiling widened", suite: "unit",
    find: `    public const int MaxFlashOpacityPercent = 100;`,
    replace: `    public const int MaxFlashOpacityPercent = 255;` },
  { id: "M-p", file: F.doc, what: "duration floor removed", suite: "unit",
    find: `    public const int MinFlashDurationSeconds = 1;`,
    replace: `    public const int MinFlashDurationSeconds = 0;` },
  { id: "M-q", file: F.doc, what: "duration ceiling widened", suite: "unit",
    find: `    public const int MaxFlashDurationSeconds = 30;`,
    replace: `    public const int MaxFlashDurationSeconds = 300;` },
  { id: "M-r", file: F.doc, what: "the scale default is not WPF's", suite: "unit",
    find: `    public const int DefaultImageScalePercent = 100;`,
    replace: `    public const int DefaultImageScalePercent = 150;` },
  { id: "M-s", file: F.doc, what: "the opacity default is not WPF's", suite: "unit",
    find: `    public const int DefaultFlashOpacityPercent = 100;`,
    replace: `    public const int DefaultFlashOpacityPercent = 80;` },
  { id: "M-t", file: F.doc, what: "the duration default is not WPF's", suite: "unit",
    find: `    public const int DefaultFlashDurationSeconds = 5;`,
    replace: `    public const int DefaultFlashDurationSeconds = 8;` },
  { id: "M-u", file: F.doc, what: "the scale setter stops clamping", suite: "unit",
    find: `        set => _imageScalePercent = Math.Clamp(value, MinImageScalePercent, MaxImageScalePercent);`,
    replace: `        set => _imageScalePercent = value;` },
  { id: "M-v", file: F.doc, what: "the opacity setter stops clamping", suite: "unit",
    find: `        set => _flashOpacityPercent = Math.Clamp(value, MinFlashOpacityPercent, MaxFlashOpacityPercent);`,
    replace: `        set => _flashOpacityPercent = value;` },
  { id: "M-w", file: F.doc, what: "the duration setter stops clamping", suite: "unit",
    find: `        set => _flashDurationSeconds = Math.Clamp(value, MinFlashDurationSeconds, MaxFlashDurationSeconds);`,
    replace: `        set => _flashDurationSeconds = value;` },

  // ---- VisualsDials -------------------------------------------------------------------------
  { id: "M-x", file: F.draw, what: "a dial move raises no Changed", suite: "unit",
    find: `        _preset.Mutate(mutation);
        Changed?.Invoke();`,
    replace: `        _preset.Mutate(mutation);` },
  { id: "M-y", file: F.draw, what: "the opacity setter writes the SCALE", suite: "unit",
    find: `    public void SetFlashOpacityPercent(int percent) => Move(p => p.FlashOpacityPercent = percent);`,
    replace: `    public void SetFlashOpacityPercent(int percent) => Move(p => p.ImageScalePercent = percent);` },
  { id: "M-z", file: F.draw, what: "the duration setter writes the SCALE", suite: "unit",
    find: `    public void SetFlashDurationSeconds(int seconds) => Move(p => p.FlashDurationSeconds = seconds);`,
    replace: `    public void SetFlashDurationSeconds(int seconds) => Move(p => p.ImageScalePercent = seconds);` },
  { id: "M-aa", file: F.draw, what: "Draw() ignores the document entirely", suite: "unit",
    find: `    public FlashDraw Draw() => FlashDraw.From(_preset.Current);`,
    replace: `    public FlashDraw Draw() => FlashDraw.Defaults;` },

  // ---- The presenter ------------------------------------------------------------------------
  { id: "M-ab", file: F.presenter, what: "the reading is taken PER SURFACE, not per flash", suite: "unit",
    find: `        var draw = _draw();
        LastDraw = draw;`,
    replace: `        var draw = FlashDraw.Defaults;
        LastDraw = _draw();` },
  { id: "M-ac", file: F.presenter, what: "the size dial is ignored", suite: "unit",
    find: `                FlashGeometry.Size(sourceWidth, sourceHeight, monitor.Width, monitor.Height, draw.ScalePercent));`,
    replace: `                FlashGeometry.Size(sourceWidth, sourceHeight, monitor.Width, monitor.Height, ImageScalePercent));` },
  { id: "M-ad", file: F.presenter, what: "the opacity dial is ignored", suite: "unit",
    find: `        var request = new OverlaySurfaceRequest(placement, draw.Opacity, ClickThrough: true);`,
    replace: `        var request = new OverlaySurfaceRequest(placement, OpacityPercent / 100.0, ClickThrough: true);` },
  { id: "M-ae", file: F.presenter, what: "the duration dial is ignored", suite: "unit",
    find: `        _surfaces.Place(slot, request, frame, draw.Lifetime);`,
    replace: `        _surfaces.Place(slot, request, frame, SurfaceLifetime);` },
  { id: "M-af", file: F.presenter, what: "the last reading is never recorded", suite: "unit",
    find: `        var draw = _draw();
        LastDraw = draw;`,
    replace: `        var draw = _draw();` },
  { id: "M-ag", file: F.presenter, what: "a supplied dial source is dropped for the defaults", suite: "unit",
    find: `        _draw = draw ?? (static () => FlashDraw.Defaults);`,
    replace: `        _draw = static () => FlashDraw.Defaults;` },
  { id: "M-ah", file: F.presenter, what: "a flash with NO paths still reads the dials", suite: "unit",
    find: `        if (_surfaces.Disposed || paths.Count == 0)`,
    replace: `        if (_surfaces.Disposed)` },

  // ---- The ramp's third dial -----------------------------------------------------------------
  { id: "M-ai", file: F.dial, what: "the flash dial is keyed to the PANEL not the module", suite: "unit",
    find: `    public string Id => FlashImagesEffect.EffectId;

    /// <inheritdoc/>
    public string Label => "Flash Images opacity";`,
    replace: `    public string Id => VisualsDials.RowId;

    /// <inheritdoc/>
    public string Label => "Flash Images opacity";` },
  { id: "M-aj", file: F.dial, what: "the flash dial's ceiling is the FLOOR", suite: "unit",
    find: `    public int Ceiling => VisualsPresetDocument.MaxFlashOpacityPercent;`,
    replace: `    public int Ceiling => VisualsPresetDocument.MinFlashOpacityPercent;` },
  { id: "M-ak", file: F.dial, what: "the flash dial reads the SCALE", suite: "unit",
    find: `    public int Read() => _dials.Preset.FlashOpacityPercent;`,
    replace: `    public int Read() => _dials.Preset.ImageScalePercent;` },
  { id: "M-al", file: F.dial, what: "the flash dial's write is a no-op", suite: "unit",
    find: `    public void Write(int percent) => _dials.SetFlashOpacityPercent(percent);`,
    replace: `    public void Write(int percent) { }` },
  { id: "M-am", file: F.ramp, what: "the flash link is never recognised", suite: "unit",
    find: `        FlashImagesEffect.EffectId => preset.LinkFlashOpacity,`,
    replace: `        FlashImagesEffect.EffectId => false,` },
  { id: "M-an", file: F.ramp, what: "the flash link switch writes the SPIRAL link", suite: "headless",
    find: `        MutateAndReapply(p => p.LinkFlashOpacity = linked);`,
    replace: `        MutateAndReapply(p => p.LinkSpiralOpacity = linked);` },
  { id: "M-ao", file: F.rampNotices, what: "the flash link does not count as a link", suite: "unit",
    find: `        var anyLink = preset.LinkSpiralOpacity || preset.LinkPinkFilterOpacity || preset.LinkFlashOpacity;`,
    replace: `        var anyLink = preset.LinkSpiralOpacity || preset.LinkPinkFilterOpacity;` },

  // ---- The composition root -------------------------------------------------------------------
  { id: "M-ap", file: F.participant, what: "the Visuals store is never loaded", suite: "unit",
    find: `        await _visualsPreset.StartAsync(cancellationToken).ConfigureAwait(false);\n`,
    replace: `` },
  { id: "M-aq", file: F.participant, what: "the Bouncing Text store is never loaded (SP-115's defect, restored)", suite: "unit",
    find: `        await _bouncingTextPreset.StartAsync(cancellationToken).ConfigureAwait(false);\n`,
    replace: `` },
  { id: "M-ar", file: F.participant, what: "the Visuals store is never flushed", suite: "unit",
    find: `            _visualsPreset.FlushAsync(boundedWait));`,
    replace: `            Task.CompletedTask);` },
  { id: "M-as", file: F.participant, what: "the Bouncing Text store is never flushed", suite: "unit",
    find: `            _bouncingTextPreset.FlushAsync(boundedWait),`,
    replace: `` },
  // Routed to UNIT, corrected after round 2. The composition root's wiring is proved by
  // THECOMPOSITIONROOTReallyHandsTheDialsToTheProductPresenter, which lives in the pure-logic
  // project because it needs the REAL product presenter and the headless boot supplies a double.
  // Round 2 reported this as a survivor purely because the driver ran the wrong suite against it —
  // recorded rather than quietly re-run, on SP-112's rule about a sweep that skips its own cases.
  { id: "M-at", file: F.participant, what: "the presenter is built without the dials", suite: "unit",
    find: `        _surface = surface ?? FlashSurfacePresenter.Product(sessionClock, Dispatch, draw: Visuals.Draw);`,
    replace: `        _surface = surface ?? FlashSurfacePresenter.Product(sessionClock, Dispatch);` },
  { id: "M-au", file: F.participant, what: "the ramp is built without the third dial", suite: "headless",
    find: `                new FlashOpacityDial(Visuals),\n`,
    replace: `` },

  // ---- The panel's sentences --------------------------------------------------------------------
  { id: "M-av", file: F.notices, what: "the ownership line never says the owner is off", suite: "unit",
    find: `        flashEnabled
            ? "These are the Flash Images module's own dials: how big its pictures are, how solid "`,
    replace: `        true
            ? "These are the Flash Images module's own dials: how big its pictures are, how solid "` },
  { id: "M-aw", file: F.notices, what: "the dial line stops saying WHEN a change lands", suite: "headless",
    find: `        + $"{Seconds(draw.DurationSeconds)}. A change here reaches the NEXT flash, not one that is "
        + "already up.";`,
    replace: `        + $"{Seconds(draw.DurationSeconds)}.";` },
  { id: "M-ax", file: F.notices, what: "the absence line drops the fade half", suite: "unit",
    find: `        "Two controls the Windows app shows here are deliberately not here. Its “Fade” slider is "
        + "wired to nothing in that build either — the fade speed is a fixed constant — so a slider "
        + "for it would move nothing. Its “Link to audio” switch makes a flash last as long as its "
        + "sound, and flashes are silent in this build.";`,
    replace: `        "One control the Windows app shows here is deliberately not here. Its “Link to audio” "
        + "switch makes a flash last as long as its sound, and flashes are silent in this build.";` },
  { id: "M-ay", file: F.notices, what: "the seconds are always plural", suite: "unit",
    find: `        value == 1 ? "1 second" : value.ToString(CultureInfo.CurrentCulture) + " seconds";`,
    replace: `        value.ToString(CultureInfo.CurrentCulture) + " seconds";` },
  { id: "M-az", file: F.notices, what: "an unattempted surface claims a placement", suite: "unit",
    find: `        null => "No flash has been drawn yet this run, so the screen has not been asked for "
            + "anything and there is nothing to report.",`,
    replace: `        null => "The operating system confirmed the last flash was placed at "
            + "the size and opacity above.",` },
  { id: "M-ba", file: F.notices, what: "a refusal is not quoted", suite: "unit",
    find: `        CapabilityState.Unavailable u =>
            $"These dials describe pictures this build cannot put on a screen: {u.Reason.Detail}",`,
    replace: `        CapabilityState.Unavailable =>
            "These dials describe pictures this build cannot put on a screen.",` },

  // ---- The page ----------------------------------------------------------------------------------
  { id: "M-bb", file: F.page, what: "the size readout shows the opacity", suite: "headless",
    find: `        VisualsScaleValue.Text = draw.ScalePercent.ToString(System.Globalization.CultureInfo.CurrentCulture);`,
    replace: `        VisualsScaleValue.Text = draw.OpacityPercent.ToString(System.Globalization.CultureInfo.CurrentCulture);` },
  { id: "M-bc", file: F.page, what: "the opacity slider writes the SIZE", suite: "headless",
    find: `        _visuals.SetFlashOpacityPercent((int)Math.Round(VisualsOpacitySlider.Value));`,
    replace: `        _visuals.SetImageScalePercent((int)Math.Round(VisualsOpacitySlider.Value));` },
  { id: "M-bd", file: F.page, what: "the duration slider writes nothing", suite: "headless",
    find: `        _visuals.SetFlashDurationSeconds((int)Math.Round(VisualsDurationSlider.Value));`,
    replace: `        _ = VisualsDurationSlider.Value;` },
  { id: "M-be", file: F.page, what: "the panel never opens", suite: "headless",
    find: `        VisualsModulePanel.IsVisible = visualsOpen;`,
    replace: `        VisualsModulePanel.IsVisible = false;` },
  { id: "M-bf", file: F.page, what: "the rack hint stays up over the Visuals panel", suite: "headless",
    find: `            && !bubblePopOpen && !bouncingTextOpen && !visualsOpen;`,
    replace: `            && !bubblePopOpen && !bouncingTextOpen;` },
  { id: "M-bg", file: F.page, what: "the dials do not load from the document", suite: "headless",
    find: `            VisualsScaleSlider.Value = visuals.ImageScalePercent;`,
    replace: `            VisualsScaleSlider.Value = VisualsPresetDocument.DefaultImageScalePercent;` },
  { id: "M-bh", file: F.page, what: "the ownership line ignores the owner's enable", suite: "headless",
    find: `        VisualsOwnershipState.Text = VisualsPanelNotices.DescribeOwnership(_flash.Enabled);`,
    replace: `        VisualsOwnershipState.Text = VisualsPanelNotices.DescribeOwnership(true);` },
];

function read(rel) {
  return fs.readFileSync(path.join(repo, rel), "utf8");
}

function write(rel, text) {
  fs.writeFileSync(path.join(repo, rel), text);
}

/** Normalise for matching; write back in the file's OWN endings. */
function applyMutation(rel, find, replace) {
  const original = read(rel);
  const crlf = original.includes("\r\n");
  const flat = crlf ? original.replaceAll("\r\n", "\n") : original;
  if (flat.split(find).length - 1 !== 1) {
    return { ok: false, original, hits: flat.split(find).length - 1 };
  }
  const mutated = flat.replace(find, replace);
  write(rel, crlf ? mutated.replaceAll("\n", "\r\n") : mutated);
  return { ok: true, original };
}

/**
 * Build the product project alone, before any test runs. A mutant the compiler rejects is reported
 * as NOT COMPILED rather than counted as a catch, because nothing was measured about it.
 */
function compiles() {
  try {
    execFileSync(
      "dotnet",
      ["build", `${SRC}/CcpClient.Desktop.csproj`, "-c", "Debug", "--nologo", "-v", "q"],
      { cwd: repo, stdio: "pipe", encoding: "utf8", timeout: 10 * 60 * 1000 },
    );
    return true;
  } catch {
    return false;
  }
}

function runSuite(suite) {
  const project =
    suite === "headless"
      ? "client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj"
      : "client/tests/CcpClient.Tests/CcpClient.Tests.csproj";
  const filter = suite === "headless" ? HEADLESS : UNIT;
  try {
    execFileSync(
      "dotnet",
      ["test", project, "-c", "Debug", "--nologo", "-v", "q", "--filter", filter],
      { cwd: repo, stdio: "pipe", encoding: "utf8", timeout: 15 * 60 * 1000 },
    );
    return "SURVIVED";
  } catch {
    return "CAUGHT";
  }
}

const args = process.argv.slice(2);
const onlyArg = args.indexOf("--only");
const only = onlyArg >= 0 ? new Set(args[onlyArg + 1].split(",")) : null;
const roundArg = args.indexOf("--round");
const round = roundArg >= 0 ? args[roundArg + 1] : "1";

const log = [];
let caught = 0;
let survived = 0;
let skipped = 0;
let notCompiled = 0;

for (const m of MUTATIONS) {
  if (only && !only.has(m.id)) {
    continue;
  }

  const applied = applyMutation(m.file, m.find, m.replace);
  if (!applied.ok) {
    skipped++;
    const line = `${m.id}  NOT PATCHED (${applied.hits} match(es))  ${m.file}  ${m.what}`;
    console.log(line);
    log.push(line);
    continue;
  }

  let verdict;
  try {
    verdict = compiles() ? runSuite(m.suite) : "NOT COMPILED";
  } finally {
    write(m.file, applied.original);
  }

  if (verdict === "NOT COMPILED") {
    notCompiled++;
  } else if (verdict === "CAUGHT") {
    caught++;
  } else {
    survived++;
  }

  const line = `${m.id}  ${verdict}  [${m.suite}]  ${m.what}`;
  console.log(line);
  log.push(line);
}

const status = execFileSync("git", ["status", "--porcelain", "client/src"], {
  cwd: repo,
  encoding: "utf8",
});
const clean = status.trim() === "";
const summary =
  `\nround ${round}: ${caught} caught, ${survived} survived, ${skipped} not patched, ` +
  `${notCompiled} not compiled ` +
  `(${caught + survived + skipped + notCompiled} attempted)\n` +
  `tree restored byte-identically: ${clean ? "YES" : "NO — " + status}`;
console.log(summary);
log.push(summary);

fs.writeFileSync(path.join(here, `sweep-round${round}.log`), log.join("\n") + "\n");

if (!clean) {
  process.exitCode = 1;
}
