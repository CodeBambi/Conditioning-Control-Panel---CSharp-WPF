#!/usr/bin/env node
// with-slot.mjs — a cross-platform counting semaphore for the greenfield gate.
//
// WHY THIS EXISTS
// The port runs in parallel git-worktree lanes (target 8). Every lane wants to run
//   dotnet build client/CcpClient.sln
//   node client/tests/floor/check-floor.mjs
// A built client tree is ~4.6 GB and this machine has 8 physical / 16 logical cores
// and 31.3 GB RAM. Eight concurrent builds thrash CPU, RAM, and the NuGet/MSBuild
// file locks. MODEL concurrency and BUILD concurrency are deliberately different
// numbers: all lanes may think at once, but only N of them may build or test at once.
// Wrap the heavy step:
//   node client/tools/gate/with-slot.mjs -- dotnet build client/CcpClient.sln -c Debug
//
// USAGE
//   node client/tools/gate/with-slot.mjs [options] -- <command> [args...]
//     --slots N        concurrent holders (default: $CCP_GATE_SLOTS, else 3)
//     --timeout SEC    max wait for a slot (default 3600). Exceeding it exits 75.
//     --name NAME      semaphore namespace (default ccp-gate) so unrelated gates
//                      do not contend with each other
//     --max-age SEC    hard ceiling after which a lock is reaped regardless of
//                      apparent liveness (default 14400 = 4h)
//     --quiet          suppress the wrapper's own stderr chatter
//
// MECHANISM
// One lock FILE per slot index in os.tmpdir(), created with fs.openSync(path, "wx").
// "wx" is O_CREAT|O_EXCL: the OS guarantees exactly one creator wins, so the
// acquire is atomic with no read-modify-write window. Deliberately NOT a single
// counter file (racy) and NOT a directory-rename scheme.
//
// EXIT CODE CONTRACT — THE MOST IMPORTANT LINE IN THIS FILE
// The child's exit code is propagated UNCHANGED. This wrapper sits in front of a
// GATE; swallowing, clamping, or remapping a non-zero child exit would turn a red
// gate green, which is the single worst thing this file could do. There is exactly
// one process.exit that carries a child status and it passes `code` through
// verbatim. Wrapper-only failures use codes the child never produces here:
//   75  could not acquire a slot before --timeout (EX_TEMPFAIL)
//   70  internal wrapper error (EX_SOFTWARE)
//   127 command could not be spawned (not found)
//   126 command found but not executable
//   130 interrupted while still WAITING for a slot (no child had been started)
// Once the child exists, its status wins over all of the above.
//
// PORTABILITY
// Node 20+, zero npm dependencies, no shell. Only node:child_process, node:crypto,
// node:fs, node:os, node:path are used — all core and all identical on Windows and
// Linux. No PowerShell, no cmd.exe, no Win32 API, no POSIX-only syscall. `spawn` is
// called with shell:false, so <command> must be a real executable (dotnet, node,
// pwsh, bash) and not a shell builtin or a bare .cmd/.bat script.

import { spawn } from "node:child_process";
import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const DEFAULT_SLOTS = 3;
const DEFAULT_TIMEOUT_SEC = 3600;
const DEFAULT_MAX_AGE_SEC = 4 * 60 * 60;
const DEFAULT_NAME = "ccp-gate";
const POLL_MIN_MS = 100;
const POLL_MAX_MS = 500;
// A lock file exists for a few microseconds between openSync("wx") and the write of
// its JSON body. Never treat an empty/garbage lock as stale inside this grace window
// or two acquirers can stomp the same slot.
const MALFORMED_GRACE_MS = 10_000;

const HOST = os.hostname();

// ---------------------------------------------------------------- argument parsing

function usage() {
  return [
    "usage: node client/tools/gate/with-slot.mjs [--slots N] [--timeout SEC]",
    "                                            [--name NAME] [--max-age SEC]",
    "                                            [--quiet] -- <command> [args...]",
  ].join("\n");
}

function parseArgs(argv) {
  const opts = {
    slots: Number.parseInt(process.env.CCP_GATE_SLOTS ?? "", 10),
    timeoutSec: DEFAULT_TIMEOUT_SEC,
    maxAgeSec: DEFAULT_MAX_AGE_SEC,
    name: DEFAULT_NAME,
    quiet: false,
    command: [],
  };
  if (!Number.isFinite(opts.slots) || opts.slots < 1) opts.slots = DEFAULT_SLOTS;

  const takeValue = (flag, inline, i) => {
    if (inline !== undefined) return { value: inline, next: i };
    if (i + 1 >= argv.length) die(70, `${flag} needs a value\n${usage()}`);
    return { value: argv[i + 1], next: i + 1 };
  };
  const posInt = (flag, raw, min) => {
    const n = Number.parseInt(raw, 10);
    if (!Number.isFinite(n) || n < min) die(70, `${flag} must be an integer >= ${min}, got ${JSON.stringify(raw)}`);
    return n;
  };

  let i = 0;
  for (; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === "--") {
      opts.command = argv.slice(i + 1);
      break;
    }
    const eq = arg.indexOf("=");
    const flag = arg.startsWith("--") && eq > 0 ? arg.slice(0, eq) : arg;
    const inline = arg.startsWith("--") && eq > 0 ? arg.slice(eq + 1) : undefined;

    switch (flag) {
      case "--slots": {
        const t = takeValue(flag, inline, i);
        opts.slots = posInt(flag, t.value, 1);
        i = t.next;
        break;
      }
      case "--timeout": {
        const t = takeValue(flag, inline, i);
        opts.timeoutSec = posInt(flag, t.value, 0);
        i = t.next;
        break;
      }
      case "--max-age": {
        const t = takeValue(flag, inline, i);
        opts.maxAgeSec = posInt(flag, t.value, 1);
        i = t.next;
        break;
      }
      case "--name": {
        const t = takeValue(flag, inline, i);
        if (!/^[A-Za-z0-9._-]+$/.test(t.value)) {
          die(70, `--name must match [A-Za-z0-9._-]+ (it becomes a directory name), got ${JSON.stringify(t.value)}`);
        }
        opts.name = t.value;
        i = t.next;
        break;
      }
      case "--quiet":
        opts.quiet = true;
        break;
      case "-h":
      case "--help":
        process.stdout.write(`${usage()}\n`);
        process.exit(0);
        break;
      default:
        die(70, `unknown option ${JSON.stringify(arg)}\n${usage()}`);
    }
  }

  if (opts.command.length === 0) die(70, `no command given — everything after "--" is the command\n${usage()}`);
  return opts;
}

// ---------------------------------------------------------------------- reporting

let QUIET = false;

function note(message) {
  if (!QUIET) process.stderr.write(`[with-slot] ${message}\n`);
}

function loud(message) {
  process.stderr.write(`[with-slot] !! ${message}\n`);
}

function die(code, message) {
  loud(message);
  process.exit(code);
}

// ------------------------------------------------------------------ lock plumbing

const opts = parseArgs(process.argv.slice(2));
QUIET = opts.quiet;

const LOCK_DIR = path.join(os.tmpdir(), `${opts.name}-slots`);
const TOKEN = crypto.randomUUID();

function slotPath(index) {
  return path.join(LOCK_DIR, `slot-${index}.lock`);
}

function ensureLockDir() {
  fs.mkdirSync(LOCK_DIR, { recursive: true });
}

// process.kill(pid, 0) sends no signal; it only asks "does this pid exist and may I
// signal it?". Windows and Linux both support signal 0 through libuv.
//   ESRCH -> no such process, provably dead
//   EPERM -> alive but owned by another user; MUST be treated as alive
// Anything else is unknown, and unknown means alive (fail safe: never steal a slot).
function pidAlive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch (err) {
    if (err.code === "ESRCH") return false;
    if (err.code === "EPERM") return true;
    return true;
  }
}

function ageMsOf(record, stat) {
  const stamped = record && typeof record.acquiredAtIso === "string" ? Date.parse(record.acquiredAtIso) : Number.NaN;
  const base = Number.isFinite(stamped) ? stamped : stat.mtimeMs;
  return Date.now() - base;
}

// Decide what a lock file means. Verdicts:
//   free   — the file is gone
//   held   — a live owner on this host, or an owner we are not allowed to judge
//   reap   — provably abandoned; .reason explains why, loudly where required
function inspectLock(file) {
  let raw;
  let stat;
  try {
    stat = fs.statSync(file);
    raw = fs.readFileSync(file, "utf8");
  } catch (err) {
    if (err.code === "ENOENT") return { verdict: "free" };
    throw err;
  }

  let record = null;
  try {
    record = JSON.parse(raw);
  } catch {
    record = null;
  }

  const malformed = !record || typeof record.pid !== "number" || typeof record.host !== "string";
  if (malformed) {
    const age = Date.now() - stat.mtimeMs;
    if (age > MALFORMED_GRACE_MS) {
      return {
        verdict: "reap",
        record,
        loud: true,
        reason: `lock ${path.basename(file)} has no readable {pid,host} and is ${Math.round(age / 1000)}s old — reaping it`,
      };
    }
    // Almost certainly a lock being born right now. Back off.
    return { verdict: "held", record, describe: `${path.basename(file)} (being created)` };
  }

  const age = ageMsOf(record, stat);
  const describe =
    `${path.basename(file)} pid=${record.pid} host=${record.host} ` +
    `since=${record.acquiredAtIso ?? "?"} (${Math.round(age / 1000)}s)`;

  // Hard age ceiling. Applied FIRST and on purpose: a pid can be recycled, so a
  // "live" pid is not proof the original holder still exists. This is the only
  // backstop that also covers a foreign-host lock, which the liveness path below is
  // forbidden from touching. os.tmpdir() is machine-local in every supported setup,
  // so a foreign host here already means something is wrong.
  if (age > opts.maxAgeSec * 1000) {
    return {
      verdict: "reap",
      record,
      loud: true,
      reason:
        `REAPING AGED LOCK ${path.basename(file)}: pid=${record.pid} host=${record.host} ` +
        `held ${Math.round(age / 1000)}s, over the --max-age ceiling of ${opts.maxAgeSec}s. ` +
        `If that pid is genuinely still building, raise --max-age; a pid can be recycled, ` +
        `so age is the only honest ceiling.`,
      describe,
    };
  }

  // Never judge a lock written by a different machine — its pid means nothing here.
  if (record.host !== HOST) {
    return { verdict: "held", record, describe: `${describe} [foreign host, never pid-reaped]` };
  }

  if (!pidAlive(record.pid)) {
    return {
      verdict: "reap",
      record,
      loud: false,
      reason: `reaping stale lock ${path.basename(file)} — pid ${record.pid} on this host is gone (ESRCH)`,
      describe,
    };
  }

  return { verdict: "held", record, describe };
}

function reap(file, decision) {
  const say = decision.loud ? loud : note;
  say(decision.reason);
  try {
    fs.unlinkSync(file);
  } catch (err) {
    if (err.code !== "ENOENT") note(`could not reap ${file}: ${err.message}`);
  }
}

let held = null; // {slot, token, file}

function tryAcquire(index) {
  const file = slotPath(index);
  let fd;
  try {
    fd = fs.openSync(file, "wx"); // O_CREAT|O_EXCL — atomic, exactly one winner
  } catch (err) {
    if (err.code !== "EEXIST") throw err;
    const decision = inspectLock(file);
    if (decision.verdict === "held") return null;
    if (decision.verdict === "reap") reap(file, decision);
    try {
      fd = fs.openSync(file, "wx"); // lost the re-race? then someone else owns it now
    } catch (err2) {
      if (err2.code === "EEXIST") return null;
      throw err2;
    }
  }

  const record = {
    pid: process.pid,
    host: HOST,
    name: opts.name,
    acquiredAtIso: new Date().toISOString(),
    slot: index,
    token: TOKEN, // proves the lock we delete on release is still the one we took
    command: opts.command.join(" "),
  };
  try {
    fs.writeSync(fd, JSON.stringify(record));
  } finally {
    fs.closeSync(fd);
  }
  // Publish ownership INSIDE this synchronous function. JS cannot be interrupted
  // mid-function, so there is no tick in which the lock file exists on disk but
  // `held` is still null — which would let a signal or exit handler skip the
  // release and leak the slot.
  held = { slot: index, token: TOKEN, file };
  return held;
}

function snapshotHolders() {
  const rows = [];
  for (let i = 0; i < opts.slots; i++) {
    const file = slotPath(i);
    let decision;
    try {
      decision = inspectLock(file);
    } catch (err) {
      rows.push(`  slot ${i}: unreadable (${err.message})`);
      continue;
    }
    if (decision.verdict === "free") rows.push(`  slot ${i}: free (taken between poll and report)`);
    else rows.push(`  slot ${i}: ${decision.describe ?? "held"}`);
  }
  return rows.join("\n");
}

// Release must be safe to call repeatedly and from a synchronous exit handler.
function release() {
  if (!held) return;
  const { file, token, slot } = held;
  held = null;
  try {
    const record = JSON.parse(fs.readFileSync(file, "utf8"));
    if (record.token !== token) {
      // Our lock was reaped and retaken by someone else. Deleting it would hand two
      // processes the same slot. Leave it alone.
      loud(`slot ${slot} was reaped out from under us (now pid=${record.pid}); not deleting it`);
      return;
    }
  } catch (err) {
    if (err.code === "ENOENT") return; // already gone
    // Unparseable but present: it is almost certainly our own truncated file.
  }
  try {
    fs.unlinkSync(file);
  } catch (err) {
    if (err.code !== "ENOENT") loud(`failed to release slot ${slot}: ${err.message}`);
  }
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function acquire() {
  ensureLockDir();
  const startedAt = Date.now();
  const deadline = startedAt + opts.timeoutSec * 1000;
  let announcedWait = false;

  for (;;) {
    for (let i = 0; i < opts.slots; i++) {
      if (tryAcquire(i)) return { waitedMs: Date.now() - startedAt }; // tryAcquire set `held`
    }
    if (Date.now() >= deadline) {
      loud(
        `TIMED OUT waiting ${Math.round((Date.now() - startedAt) / 1000)}s for one of ${opts.slots} ` +
          `"${opts.name}" slots (--timeout ${opts.timeoutSec}s). The command was NOT run.\n` +
          `Slots at timeout:\n${snapshotHolders()}\n` +
          `Lock dir: ${LOCK_DIR}`,
      );
      process.exit(75);
    }
    if (!announcedWait) {
      announcedWait = true;
      note(`all ${opts.slots} "${opts.name}" slots busy — waiting (timeout ${opts.timeoutSec}s)`);
    }
    // Randomised backoff so N waiters do not stampede slot 0 in lockstep.
    await sleep(POLL_MIN_MS + Math.floor(Math.random() * (POLL_MAX_MS - POLL_MIN_MS + 1)));
  }
}

// -------------------------------------------------------------------------- main

process.on("exit", release); // belt and braces: sync release on any exit path
process.on("uncaughtException", (err) => {
  release();
  die(70, `internal error: ${err?.stack ?? err}`);
});
process.on("unhandledRejection", (err) => {
  release();
  die(70, `internal error (rejection): ${err?.stack ?? err}`);
});

let child = null;
let childExited = false;

// Registered BEFORE the wait loop, not after it. A lane can be interrupted while it
// is still queueing for a slot, and an unregistered handler cannot release anything
// or stop the poll loop.
// SIGBREAK exists only on Windows; SIGHUP only on POSIX. Register what the platform
// actually has so neither branch throws on the other OS.
const signals = process.platform === "win32" ? ["SIGINT", "SIGTERM", "SIGBREAK"] : ["SIGINT", "SIGTERM", "SIGHUP"];
for (const signal of signals) {
  process.on(signal, () => {
    if (child && !childExited) {
      // Forward and let the child's own exit event release the slot and set status.
      try {
        child.kill(signal);
      } catch {
        /* already gone */
      }
      return;
    }
    release(); // no-op if no slot was taken yet
    loud(`${signal} received before the command started — slot released, command not run`);
    process.exit(130);
  });
}

const { waitedMs } = await acquire();
note(`acquired slot ${held.slot}/${opts.slots} of "${opts.name}" after ${waitedMs}ms (pid ${process.pid})`);

child = spawn(opts.command[0], opts.command.slice(1), {
  stdio: "inherit", // live pass-through, never buffered: a lane needs to see progress
  shell: false,
  windowsHide: true,
});

child.on("error", (err) => {
  childExited = true;
  release();
  const code = err.code === "ENOENT" ? 127 : 126;
  die(code, `could not spawn ${JSON.stringify(opts.command[0])}: ${err.message}`);
});

child.on("exit", (code, signal) => {
  childExited = true;
  release();
  if (signal) {
    const number = os.constants.signals[signal] ?? 0;
    loud(`command was killed by ${signal}`);
    process.exit(128 + number);
  }
  // THE PASSTHROUGH. Do not clamp, remap, or zero this. A red gate must stay red.
  process.exit(code ?? 70);
});
