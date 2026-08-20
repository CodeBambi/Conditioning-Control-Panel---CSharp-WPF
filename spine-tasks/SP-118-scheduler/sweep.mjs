#!/usr/bin/env node
// SP-118 mutation sweep.
//
// Lives inside this packet's folder and writes ONLY inside it (SP-112's rule, after a previous
// wave's driver wrote three levels above its own root into the shared checkout).
//
// Line endings: the working tree is CRLF and the needles below are LF, so every needle is
// normalised for MATCHING and the mutant is written back in the file's OWN endings. SP-112 lost 27
// of its hardest cases to exactly this and a sweep that silently skips is worse than no sweep.
//
// The false-clean channels, named because SP-117's record obliges it. `runSuite` decides CAUGHT
// from a NON-ZERO EXIT CODE, and `dotnet test` exits non-zero for reasons that are not a failing
// assertion: a mutant that does not COMPILE, a `--filter` that matches no test, a crashed host, or
// the timeout. `compiles()` closes the first by building the product project BEFORE the suite runs
// and reporting NOT COMPILED as its own outcome. The others are unclosed and are named in
// record.md; every round's log shows a non-zero passing count from the same filters, so no filter
// here matched zero tests.
//
// Usage: node spine-tasks/SP-118-scheduler/sweep.mjs [--only M-a,M-b] [--round N]

import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, "..", "..");

const SRC = "client/src/CcpClient.Desktop";
const F = {
  window: `${SRC}/Scheduling/ScheduleWindow.cs`,
  doc: `${SRC}/Scheduling/SchedulerPresetDocument.cs`,
  sched: `${SRC}/Scheduling/SessionScheduler.cs`,
  part: `${SRC}/Scheduling/SchedulerParticipant.cs`,
  clock: `${SRC}/Scheduling/ScheduleClock.cs`,
  shell: `${SRC}/Views/MainWindow.axaml.cs`,
  page: `${SRC}/Views/Pages/StudioPage.axaml.cs`,
  root: `${SRC}/Lifecycle/CompositionRoot.cs`,
};

// This packet's own facts plus every landed suite that consumes a symbol it touched (the
// composition root's participant list and the real-root integration proof).
const UNIT =
  "FullyQualifiedName~SchedulerWindowTests|" +
  "FullyQualifiedName~SchedulerModuleTests|" +
  "FullyQualifiedName~SystemScheduleClockTests|" +
  "FullyQualifiedName~CompositionRootValidationTests|" +
  "FullyQualifiedName~IntegrationProofTests";

const HEADLESS =
  "FullyQualifiedName~SchedulerRowHeadlessTests|FullyQualifiedName~StudioRackHeadlessTests";

/** @type {{id:string,file:string,find:string,replace:string,what:string,suite:"unit"|"headless"}[]} */
const MUTATIONS = [
  // ---- the predicate: the boundaries -------------------------------------------------------
  { id: "M-a", file: F.window, what: "same-day START boundary exclusive instead of inclusive", suite: "unit",
    find: `            : timeOfDay >= start && timeOfDay < end;`,
    replace: `            : timeOfDay > start && timeOfDay < end;` },
  { id: "M-b", file: F.window, what: "same-day END boundary OPEN instead of closed", suite: "unit",
    find: `            : timeOfDay >= start && timeOfDay < end;`,
    replace: `            : timeOfDay >= start && timeOfDay <= end;` },
  { id: "M-c", file: F.window, what: "overnight START boundary exclusive", suite: "unit",
    find: `            ? timeOfDay >= start || timeOfDay < end`,
    replace: `            ? timeOfDay > start || timeOfDay < end` },
  { id: "M-d", file: F.window, what: "overnight END boundary OPEN — the wrap's closed end", suite: "unit",
    find: `            ? timeOfDay >= start || timeOfDay < end`,
    replace: `            ? timeOfDay >= start || timeOfDay <= end` },
  { id: "M-e", file: F.window, what: "equal start/end read as OVERNIGHT (an all-day window)", suite: "unit",
    find: `        var overnight = end < start;`,
    replace: `        var overnight = end <= start;` },
  { id: "M-f", file: F.window, what: "overnight arm conjoins instead of disjoining", suite: "unit",
    find: `            ? timeOfDay >= start || timeOfDay < end`,
    replace: `            ? timeOfDay >= start && timeOfDay < end` },
  { id: "M-g", file: F.window, what: "same-day arm disjoins instead of conjoining", suite: "unit",
    find: `            : timeOfDay >= start && timeOfDay < end;`,
    replace: `            : timeOfDay >= start || timeOfDay < end;` },
  { id: "M-h", file: F.window, what: "the DAY GATE dropped from the verdict", suite: "unit",
    find: `            InWindow: dayActive && withinTimes);`,
    replace: `            InWindow: withinTimes);` },
  { id: "M-i", file: F.window, what: "start fallback is midnight, not WPF's 16:00", suite: "unit",
    find: `    public static readonly TimeSpan StartFallback = new(16, 0, 0);`,
    replace: `    public static readonly TimeSpan StartFallback = new(0, 0, 0);` },
  { id: "M-j", file: F.window, what: "end fallback is 23:59, not WPF's 22:00", suite: "unit",
    find: `    public static readonly TimeSpan EndFallback = new(22, 0, 0);`,
    replace: `    public static readonly TimeSpan EndFallback = new(23, 59, 0);` },
  { id: "M-k", file: F.window, what: "a fallback is not reported as one (the panel lies)", suite: "unit",
    find: `        fellBack = true;
        return fallback;`,
    replace: `        fellBack = false;
        return fallback;` },
  { id: "M-l", file: F.window, what: "Monday's arm reads Tuesday's box", suite: "unit",
    find: `            DayOfWeek.Monday => preset.Monday,`,
    replace: `            DayOfWeek.Monday => preset.Tuesday,` },
  { id: "M-m", file: F.window, what: "the unreachable default arm says YES", suite: "unit",
    find: `            _ => false,
        };`,
    replace: `            _ => true,
        };` },
  { id: "M-n", file: F.window, what: "the reading uses midnight instead of the real time of day", suite: "unit",
    find: `        var timeOfDay = localNow.TimeOfDay;`,
    replace: `        var timeOfDay = TimeSpan.Zero;` },
  { id: "M-o", file: F.window, what: "Empty never reported", suite: "unit",
    find: `    public bool Empty => Start == End;`,
    replace: `    public bool Empty => false;` },

  // ---- the document: WPF's defaults and its null-coalesce ----------------------------------
  { id: "M-p", file: F.doc, what: "the scheduler SHIPS ARMED", suite: "unit",
    find: `    public const bool DefaultEnabled = false;`,
    replace: `    public const bool DefaultEnabled = true;` },
  { id: "M-q", file: F.doc, what: "the default start is the FALLBACK value", suite: "unit",
    find: `    public const string DefaultStartTime = "00:00";`,
    replace: `    public const string DefaultStartTime = "16:00";` },
  { id: "M-r", file: F.doc, what: "a null start time reaches the parser", suite: "unit",
    find: `        set => _startTime = value ?? DefaultStartTime;`,
    replace: `        set => _startTime = value!;` },
  { id: "M-s", file: F.doc, what: "Monday ships unticked", suite: "unit",
    find: `    public bool Monday { get; set; } = true;`,
    replace: `    public bool Monday { get; set; } = false;` },

  // ---- the tick: the five clauses --------------------------------------------------------
  { id: "M-t", file: F.sched, what: "the ENABLE gate no longer short-circuits the tick", suite: "unit",
    find: `            if (!_store.Current.Enabled)
            {
                outcome = Record(new SchedulerTickOutcome(
                    SchedulerAction.Disabled, SchedulerReasonCodes.SchedulerDisabled,
                    "the scheduler is switched off, so this tick read no clock and changed nothing",
                    Reading: null));`,
    replace: `            if (!_store.Current.Enabled && false)
            {
                outcome = Record(new SchedulerTickOutcome(
                    SchedulerAction.Disabled, SchedulerReasonCodes.SchedulerDisabled,
                    "the scheduler is switched off, so this tick read no clock and changed nothing",
                    Reading: null));` },
  { id: "M-u", file: F.sched, what: "START drops the in-window clause", suite: "unit",
    find: `                if (reading.InWindow && !_engine.Running && !_autoStarted && !_manuallyStopped)`,
    replace: `                if (!_engine.Running && !_autoStarted && !_manuallyStopped)` },
  { id: "M-v", file: F.sched, what: "START drops the already-running clause", suite: "unit",
    find: `                if (reading.InWindow && !_engine.Running && !_autoStarted && !_manuallyStopped)`,
    replace: `                if (reading.InWindow && !_autoStarted && !_manuallyStopped)` },
  { id: "M-w", file: F.sched, what: "START drops the one-per-opening clause", suite: "unit",
    find: `                if (reading.InWindow && !_engine.Running && !_autoStarted && !_manuallyStopped)`,
    replace: `                if (reading.InWindow && !_engine.Running && !_manuallyStopped)` },
  { id: "M-x", file: F.sched, what: "START drops the HAND-STOP clause — the headline harm", suite: "unit",
    find: `                if (reading.InWindow && !_engine.Running && !_autoStarted && !_manuallyStopped)`,
    replace: `                if (reading.InWindow && !_engine.Running && !_autoStarted)` },
  { id: "M-y", file: F.sched, what: "STOP drops the it-was-ours clause", suite: "unit",
    find: `                else if (!reading.InWindow && _engine.Running && _autoStarted)`,
    replace: `                else if (!reading.InWindow && _engine.Running)` },
  { id: "M-z", file: F.sched, what: "STOP drops the outside-the-window clause", suite: "unit",
    find: `                else if (!reading.InWindow && _engine.Running && _autoStarted)`,
    replace: `                else if (_engine.Running && _autoStarted)` },
  { id: "M-aa", file: F.sched, what: "a refused start is still marked auto-started", suite: "unit",
    find: `                    if (_engine.Start())`,
    replace: `                    if (_engine.Start() || true)` },
  { id: "M-ab", file: F.sched, what: "START never sets the auto-started flag", suite: "unit",
    find: `                        _autoStarted = true;
                        _starts++;`,
    replace: `                        _starts++;` },
  { id: "M-ac", file: F.sched, what: "STOP never clears the auto-started flag", suite: "unit",
    find: `                    _engine.Stop();
                    _autoStarted = false;`,
    replace: `                    _engine.Stop();` },
  { id: "M-ad", file: F.sched, what: "the reset branch never clears the hand-stop latch", suite: "unit",
    find: `                    _autoStarted = false;
                    _manuallyStopped = false;`,
    replace: `                    _autoStarted = false;` },
  { id: "M-ae", file: F.sched, what: "the reset branch never clears the auto-started flag", suite: "unit",
    find: `                    _autoStarted = false;
                    _manuallyStopped = false;`,
    replace: `                    _manuallyStopped = false;` },
  { id: "M-af", file: F.sched, what: "the STOP branch does not stop the engine", suite: "unit",
    find: `                    _engine.Stop();
                    _autoStarted = false;`,
    replace: `                    _autoStarted = false;` },

  // ---- the manual toggle: the latch nothing else writes ------------------------------------
  { id: "M-ag", file: F.sched, what: "a hand stop latches even with the scheduler OFF", suite: "unit",
    find: `                if (_store.Current.Enabled && ScheduleWindow.Read(_store.Current, _clock.LocalNow).InWindow)`,
    replace: `                if (ScheduleWindow.Read(_store.Current, _clock.LocalNow).InWindow)` },
  { id: "M-ah", file: F.sched, what: "a hand stop latches OUTSIDE the window too", suite: "unit",
    find: `                if (_store.Current.Enabled && ScheduleWindow.Read(_store.Current, _clock.LocalNow).InWindow)`,
    replace: `                if (_store.Current.Enabled)` },
  { id: "M-ai", file: F.sched, what: "a manual START no longer clears the latch", suite: "unit",
    find: `                // :106-107 — a manual START is the user overriding their own earlier stop.
                _manuallyStopped = false;`,
    replace: `                // :106-107 — a manual START is the user overriding their own earlier stop.` },
  { id: "M-aj", file: F.sched, what: "the two manual branches swapped", suite: "unit",
    find: `            if (sessionWasRunning)
            {
                // :98-101`,
    replace: `            if (!sessionWasRunning)
            {
                // :98-101` },

  // ---- the start-up check ------------------------------------------------------------------
  { id: "M-ak", file: F.sched, what: "the start-up check ignores the enable", suite: "unit",
    find: `            // WPF :568 — the enable, first and alone.
            if (!_store.Current.Enabled)`,
    replace: `            // WPF :568 — the enable, first and alone.
            if (!_store.Current.Enabled && false)` },
  { id: "M-al", file: F.sched, what: "the start-up check ignores the window", suite: "unit",
    find: `                if (!reading.InWindow)
                {
                    outcome = Record(new SchedulerTickOutcome(
                        SchedulerAction.Held, SchedulerReasonCodes.SchedulerHeld,
                        $"the app started outside the scheduled window ({Describe(reading)}), so nothing was started",
                        reading));`,
    replace: `                if (!reading.InWindow && false)
                {
                    outcome = Record(new SchedulerTickOutcome(
                        SchedulerAction.Held, SchedulerReasonCodes.SchedulerHeld,
                        $"the app started outside the scheduled window ({Describe(reading)}), so nothing was started",
                        reading));` },
  { id: "M-am", file: F.sched, what: "the start-up check marks a refused start as ours", suite: "unit",
    find: `                else if (_engine.Start())
                {
                    _autoStarted = true;`,
    replace: `                else if (_engine.Start() || true)
                {
                    _autoStarted = true;` },

  // ---- the projection and the dot ----------------------------------------------------------
  { id: "M-an", file: F.sched, what: "the auto-start never asks the shell to get out of the way", suite: "unit",
    find: `            _signal.Post(() => AutoStarted?.Invoke());`,
    replace: `            _ = AutoStarted;` },
  { id: "M-ao", file: F.sched, what: "the dot ignores the enable", suite: "unit",
    find: `            if (!Enabled)
            {
                return EffectDotState.Off;
            }`,
    replace: `            if (!Enabled && false)
            {
                return EffectDotState.Off;
            }` },
  { id: "M-ap", file: F.sched, what: "the dot claims a tick that is not on the clock", suite: "unit",
    find: `            if (!Polling)
            {
                return EffectDotState.Off;
            }`,
    replace: `            if (!Polling && false)
            {
                return EffectDotState.Off;
            }` },
  { id: "M-aq", file: F.sched, what: "the dot never goes Live", suite: "unit",
    find: `            return Reading.InWindow ? EffectDotState.Live : EffectDotState.Armed;`,
    replace: `            return EffectDotState.Armed;` },
  { id: "M-ar", file: F.sched, what: "polling is asserted rather than earned", suite: "unit",
    find: `            _polling = polling;`,
    replace: `            _polling = true;` },
  { id: "M-as", file: F.sched, what: "the poll is five minutes, not WPF's thirty seconds", suite: "unit",
    find: `    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);`,
    replace: `    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(300);` },
  { id: "M-at", file: F.sched, what: "the start-up grace is gone", suite: "unit",
    find: `    public static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(60);`,
    replace: `    public static readonly TimeSpan StartupGrace = TimeSpan.Zero;` },

  // ---- the participant: the timer, the grace, the liveness ---------------------------------
  { id: "M-au", file: F.part, what: "phase 3 arms the POLL, skipping the grace", suite: "unit",
    find: `        Arm(SessionScheduler.StartupGrace);`,
    replace: `        Arm(SessionScheduler.PollInterval);` },
  { id: "M-av", file: F.part, what: "the due callback drops its liveness re-check", suite: "unit",
    find: `        if (!_running || !_owner.IsLive(_generation))
        {
            Scheduler.SetPolling(false);
            return;
        }`,
    replace: `        if (!_running && false)
        {
            Scheduler.SetPolling(false);
            return;
        }` },
  { id: "M-aw", file: F.part, what: "the spent tick clears the slot whatever is in it", suite: "unit",
    find: `        if (Interlocked.CompareExchange(ref _pending, null, token) != token)
        {
            return;
        }`,
    replace: `        if (Interlocked.Exchange(ref _pending, null) is null)
        {
            return;
        }` },
  { id: "M-ax", file: F.part, what: "the poll never re-arms", suite: "unit",
    find: `        Arm(SessionScheduler.PollInterval);
`,
    replace: `` },
  { id: "M-ay", file: F.part, what: "stop leaves the pending one-shot alive", suite: "unit",
    find: `        Interlocked.Exchange(ref _pending, null)?.Dispose();
        Scheduler.SetPolling(false);
        Volatile.Write(ref _gracePassed, false);`,
    replace: `        Scheduler.SetPolling(false);
        Volatile.Write(ref _gracePassed, false);` },
  { id: "M-az", file: F.part, what: "stop never cancels the generation", suite: "unit",
    find: `        _owner.Cancel();
        return _store.StopAsync();`,
    replace: `        return _store.StopAsync();` },
  { id: "M-ba", file: F.part, what: "every tick re-runs the start-up check", suite: "unit",
    find: `        var firstTick = !Volatile.Read(ref _gracePassed);`,
    replace: `        var firstTick = true;` },
  { id: "M-bb", file: F.part, what: "the settings store is never started, so it never LOADS", suite: "unit",
    find: `        await _store.StartAsync(cancellationToken).ConfigureAwait(false);`,
    replace: `        await Task.CompletedTask.ConfigureAwait(false);` },
  { id: "M-bc", file: F.part, what: "the participant is never marked running", suite: "unit",
    find: `        _running = true;
        _generation = _owner.Begin();`,
    replace: `        _generation = _owner.Begin();` },

  // ---- the clock seam ----------------------------------------------------------------------
  // Round 4 (the code review): rounds 1-3 mutated ONE line of the class that ships on every
  // product path, which contradicted this packet's own claim to have mutated every line it added.
  // The three below are the rest of it.
  { id: "M-bd", file: F.clock, what: "the real clock reads UTC instead of local", suite: "unit",
    find: `    public DateTime LocalNow => DateTime.Now;`,
    replace: `    public DateTime LocalNow => DateTime.UtcNow;` },
  { id: "M-bq", file: F.clock, what: "a faulting callback is caught SILENTLY \— the worse half", suite: "unit",
    find: `            onCallbackFault?.Invoke(ex);`,
    replace: `            _ = ex;` },
  { id: "M-br", file: F.clock, what: "NO containment at all \— the escaping exception kills the process", suite: "unit",
    find: `        try
        {
            fire();
        }
        catch (Exception ex)
        {
            // Reported, not swallowed. And deliberately not re-thrown: there is nothing above this
            // frame to catch it, so re-throwing is exactly the process kill this exists to prevent.
            onCallbackFault?.Invoke(ex);
        }`,
    replace: `        fire();` },
  { id: "M-bs", file: F.clock, what: "the negative-delay clamp dropped (Timer throws on the caller)", suite: "unit",
    find: `        var ms = Math.Max(0, (long)due.TotalMilliseconds);`,
    replace: `        var ms = (long)due.TotalMilliseconds;` },

  // ---- the participant's two teardown windows (round 4, same review) -----------------------
  { id: "M-bt", file: F.part, what: "Arm leaves the new one-shot up when the generation died mid-schedule", suite: "unit",
    find: `        if (!_running || !_owner.IsLive(_generation))
        {
            Interlocked.Exchange(ref _pending, null)?.Dispose();
            Scheduler.SetPolling(false);
        }
    }`,
    replace: `        if (!_running && false)
        {
            Interlocked.Exchange(ref _pending, null)?.Dispose();
            Scheduler.SetPolling(false);
        }
    }` },
  { id: "M-bu", file: F.part, what: "the decision runs even though teardown began during Arm", suite: "unit",
    find: `        var live = _running && _owner.IsLive(_generation);
        Scheduler.SetPolling(live);
        if (!live)
        {
            return;
        }`,
    replace: `        var live = _running && _owner.IsLive(_generation);
        Scheduler.SetPolling(live);
        if (!live && false)
        {
            return;
        }` },

  // ---- the shell ---------------------------------------------------------------------------
  { id: "M-be", file: F.shell, what: "the START/STOP button never tells the scheduler", suite: "headless",
    find: `            Scheduler.NoteManualToggle(Session.Engine.Running);
            Session.Engine.Toggle();`,
    replace: `            Session.Engine.Toggle();` },
  { id: "M-bf", file: F.shell, what: "the button tells the scheduler AFTER the toggle", suite: "headless",
    find: `            Scheduler.NoteManualToggle(Session.Engine.Running);
            Session.Engine.Toggle();`,
    replace: `            Session.Engine.Toggle();
            Scheduler.NoteManualToggle(Session.Engine.Running);` },
  { id: "M-bg", file: F.shell, what: "the shell never ducks on a scheduled start", suite: "headless",
    find: `        Scheduler.AutoStarted += () => ShellTray.Duck();`,
    replace: `        _ = Scheduler;` },

  // ---- the page ----------------------------------------------------------------------------
  { id: "M-bh", file: F.page, what: "the row's right-click flips nothing", suite: "headless",
    find: `        _scheduler.SetEnabled(!_scheduler.Enabled);`,
    replace: `        _scheduler.SetEnabled(_scheduler.Enabled);` },
  { id: "M-bi", file: F.page, what: "the time boxes are loaded from a constant, not the document", suite: "headless",
    find: `            SchedulerStartTimeBox.Text = scheduler.StartTime;`,
    replace: `            SchedulerStartTimeBox.Text = SchedulerPresetDocument.DefaultStartTime;` },
  { id: "M-bj", file: F.page, what: "committing the boxes writes the start into both", suite: "headless",
    find: `        _scheduler.SetTimes(start, end);`,
    replace: `        _scheduler.SetTimes(start, start);` },
  { id: "M-bk", file: F.page, what: "the panel never opens", suite: "headless",
    find: `        SchedulerModulePanel.IsVisible = schedulerOpen;`,
    replace: `        SchedulerModulePanel.IsVisible = false;` },
  { id: "M-bl", file: F.page, what: "every day box writes Monday", suite: "headless",
    find: `            _scheduler.SetDay(day, target);`,
    replace: `            _scheduler.SetDay(DayOfWeek.Monday, target);` },
  { id: "M-bm", file: F.page, what: "the row's dot never paints Live", suite: "headless",
    find: `    private static EffectDotState PaintSchedulerDot(Shape dot, EffectDotState state)
    {
        dot.Classes.Set("armed", state == EffectDotState.Armed);
        dot.Classes.Set("live", state == EffectDotState.Live);`,
    replace: `    private static EffectDotState PaintSchedulerDot(Shape dot, EffectDotState state)
    {
        dot.Classes.Set("armed", state == EffectDotState.Armed);
        dot.Classes.Set("live", false);` },
  { id: "M-bn", file: F.page, what: "the panel's live line is composed from a constant dot", suite: "headless",
    find: `            RenderedSchedulerDot, _scheduler.Enabled, _scheduler.Polling, schedulerReading,`,
    replace: `            RenderedSchedulerDot, _scheduler.Enabled, true, schedulerReading,` },

  // ---- the composition root -----------------------------------------------------------------
  { id: "M-bo", file: F.root, what: "the scheduler's settings never flush at teardown", suite: "unit",
    find: `                    if (scheduler is not null) await scheduler.FlushAsync(DefaultFlushTimeout).ConfigureAwait(false);`,
    replace: `                    if (scheduler is null) await scheduler!.FlushAsync(DefaultFlushTimeout).ConfigureAwait(false);` },
  { id: "M-bp", file: F.root, what: "the injected schedule clock is ignored", suite: "headless",
    find: `                infra, Path.GetDirectoryName(SettingsPathFactory())!, session.Engine,
                ScheduleClockFactory?.Invoke()),`,
    replace: `                infra, Path.GetDirectoryName(SettingsPathFactory())!, session.Engine,
                null),` },
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

/** A mutant the compiler rejects is one no test was ever asked about. */
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
    const out = execFileSync(
      "dotnet",
      ["test", project, "-c", "Debug", "--nologo", "-v", "q", "--filter", filter],
      { cwd: repo, stdio: "pipe", encoding: "utf8", timeout: 15 * 60 * 1000 },
    );
    return { verdict: "SURVIVED", tail: lastLine(out) };
  } catch (err) {
    return { verdict: "CAUGHT", tail: lastLine(String(err.stdout ?? "")) };
  }
}

function lastLine(text) {
  const lines = text.split(/\r?\n/).filter((l) => l.includes("Failed:") && l.includes("Passed:"));
  return lines.length > 0 ? lines[lines.length - 1].trim() : "<no result line>";
}

const args = process.argv.slice(2);
const onlyArg = args.indexOf("--only");
const only = onlyArg >= 0 ? new Set(args[onlyArg + 1].split(",")) : null;
const roundArg = args.indexOf("--round");
const round = roundArg >= 0 ? args[roundArg + 1] : "1";
// Needle check only: apply and restore every mutation WITHOUT building or running anything, so a
// needle that no longer matches is found in seconds instead of an hour into a round. It reports
// NOT PATCHED exactly as a real round does and it writes no log — it is an instrument for the
// driver, never evidence about the code.
const matchOnly = args.includes("--match-only");

if (matchOnly) {
  let bad = 0;
  for (const m of MUTATIONS) {
    const applied = applyMutation(m.file, m.find, m.replace);
    if (!applied.ok) {
      bad++;
      console.log(`${m.id}  NOT PATCHED (${applied.hits} match(es))  ${m.file}  ${m.what}`);
    } else {
      write(m.file, applied.original);
    }
  }

  const dirty = execFileSync("git", ["status", "--porcelain", "client/src"], {
    cwd: repo,
    encoding: "utf8",
  });
  console.log(`match check: ${MUTATIONS.length - bad}/${MUTATIONS.length} needles matched exactly once; ` +
    `tree clean: ${dirty.trim() === "" ? "YES" : "NO — " + dirty}`);
  process.exit(bad === 0 && dirty.trim() === "" ? 0 : 1);
}

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

  let verdict = "NOT COMPILED";
  let tail = "<not built>";
  try {
    if (compiles()) {
      const run = runSuite(m.suite);
      verdict = run.verdict;
      tail = run.tail;
    }
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

  const line = `${m.id}  ${verdict}  [${m.suite}]  ${m.what}  ||  ${tail}`;
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
