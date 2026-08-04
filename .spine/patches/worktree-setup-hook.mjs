#!/usr/bin/env node
/**
 * worktree-setup-hook.mjs — T-14 (SP-039): every fresh lane starts patched.
 *
 * Invoked by the pi-spine batch engine's worktreeSetupHook seam once per lane at
 * worktree creation, BEFORE any worker/pi run (engine.mjs provisioning loop).
 * At that point the lane has NO .pi/npm (gitignored); the lane's pi-spine would
 * otherwise be installed pristine by pi's session-start package resolution and
 * the packet's verify.mjs contract step would red mid-task (SP-035/036/037/039).
 *
 * Mechanism: copy SPINE_PROJECT_ROOT's already-patched .pi/npm into the lane.
 * pi's needsInstall gate (package-manager.js:1008) sees a satisfying pinned
 * pi-spine@2.10.0 already present and never reinstalls — patches ride in with
 * the copy. (Pinned sources are also excluded from pi's auto-update check.)
 *
 * Engine contract (worktree.mjs runWorktreeSetupHook): spawned directly (no
 * shell — wrapped by scripts/spine-worktree-setup.exe on Windows), cwd = lane
 * worktree, 120s timeout, LAST stdout line MUST be JSON {"ok":true} or the
 * engine throws and the whole batch dies at provisioning. Fail-safe first:
 * this script ALWAYS exits 0 with {"ok":true,...}; every failure degrades to
 * prestaged:false + reason (today's recoverable mid-task remediation), never a
 * blocked batch. Diagnostics go to stderr + the lane log file; the engine
 * discards both streams, so the log is the only durable evidence.
 *
 * Idempotent: a lane that already has a pi-spine install (hook re-run via the
 * engine's dirty-check repair path) is left untouched.
 */
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const VERIFY = path.join(HERE, "verify.mjs");
const PI_SPINE_REL = path.join("node_modules", "pi-spine");

const startedAt = Date.now();
/** @type {Record<string, unknown>} */
const result = { ok: true, prestaged: false, reason: "unset", sourceVersion: null, verifyExit: null };
const worktree = process.env.SPINE_WORKTREE || process.cwd();
const destNpm = path.join(worktree, ".pi", "npm");
const destPiSpine = path.join(destNpm, PI_SPINE_REL);

function log(line) {
	process.stderr.write(`[worktree-setup-hook] ${line}\n`);
}

function readVersion(piSpineDir) {
	try {
		return JSON.parse(fs.readFileSync(path.join(piSpineDir, "package.json"), "utf-8")).version ?? null;
	} catch {
		return null;
	}
}

function main() {
	const projectRoot = process.env.SPINE_PROJECT_ROOT;
	const destVersion = readVersion(destPiSpine);
	if (destVersion) {
		result.prestaged = true;
		result.reason = `already-present (pi-spine ${destVersion} in lane) — idempotent skip`;
		return;
	}
	if (!projectRoot) {
		result.reason = "SPINE_PROJECT_ROOT unset — not running under the spine batch engine";
		return;
	}
	const sourcePiSpine = path.join(projectRoot, ".pi", "npm", PI_SPINE_REL);

	result.sourceVersion = readVersion(sourcePiSpine);
	if (!result.sourceVersion) {
		result.reason = `source pi-spine missing at ${sourcePiSpine} — lane falls back to pi's registry install (today's manual-remediation path)`;
		return;
	}

	fs.cpSync(path.join(projectRoot, ".pi", "npm"), destNpm, { recursive: true, dereference: true });
	result.prestaged = true;
	result.reason = `copied .pi/npm from SPINE_PROJECT_ROOT (pi-spine ${result.sourceVersion})`;

	// Post-copy self-check (read-only, single root). Never propagated — recorded only.
	if (fs.existsSync(VERIFY)) {
		const v = spawnSync(process.execPath, [VERIFY, "--root", destPiSpine], {
			cwd: worktree,
			encoding: "utf-8",
			timeout: 30_000,
		});
		result.verifyExit = v.status;
		if (v.status !== 0) {
			log(`verify.mjs --root exited ${v.status}; tail: ${(v.stdout ?? "").trim().split(/\r?\n/).slice(-3).join(" | ")}`);
		}
	} else {
		result.reason += " (verify.mjs absent in lane — self-check skipped)";
	}
}

// Durable per-lane evidence for EVERY outcome (gitignored path: .gitignore
// ".pi/npm/"); the engine discards the hook's stdout/stderr, so this log is the
// only record when prestaging was skipped or failed.
function writeLaneLog() {
	try {
		fs.mkdirSync(destNpm, { recursive: true });
		fs.writeFileSync(
			path.join(destNpm, "worktree-setup-hook.log"),
			JSON.stringify({
				at: new Date().toISOString(),
				batchId: process.env.SPINE_BATCH_ID ?? null,
				laneNumber: process.env.SPINE_LANE_NUMBER ?? null,
				projectRoot: process.env.SPINE_PROJECT_ROOT ?? null,
				worktree,
				...result,
				durationMs: Date.now() - startedAt,
			}) + "\n",
		);
	} catch (err) {
		log(`log write failed: ${err instanceof Error ? err.message : String(err)}`);
	}
}

try {
	main();
} catch (err) {
	result.prestaged = false;
	result.reason = `hook-error: ${err instanceof Error ? err.message : String(err)}`;
	log(result.reason);
}
writeLaneLog();
result.durationMs = Date.now() - startedAt;
// Last stdout line is the engine's contract — keep it the ONLY stdout output.
process.stdout.write(JSON.stringify(result) + "\n");
process.exit(0);
