// SP-039 scratch verification v2: provision two worktrees THROUGH THE ENGINE'S OWN
// provisioning path (global install worktree.mjs) with projectRoot = MAIN CHECKOUT
// (production-identical; v1's worktree-of-worktree died on MAX_PATH for deep WPF
// asset filenames). Hook enabled (lane-1) vs disabled negative control (lane-2).
// Full cleanup: worktrees, branches, temp exe copy, empty scripts dir.
import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";

const ENGINE = "file:///C:/Users/Micha/.pi/agent/npm/node_modules/pi-spine/src/batch/worktree.mjs";
const { provisionLaneWorktree, runWorktreeSetupHook, removeLaneWorktree } = await import(ENGINE);

const laneRoot = process.cwd();
const projectRoot = "C:\\Code\\Conditioning-Control-Panel---CSharp-WPF";
const batchId = "scratch-sp039";
const orchBranch = "task/spine-lane-2-20260804T144454";
const tempExeDir = path.join(projectRoot, "scripts");
const tempExe = path.join(tempExeDir, "spine-worktree-setup.exe");
const createdScriptsDir = !fs.existsSync(tempExeDir);

const results = {};
try {
	fs.mkdirSync(tempExeDir, { recursive: true });
	fs.copyFileSync(path.join(laneRoot, "scripts", "spine-worktree-setup.exe"), tempExe);

	// --- Test 1: hook ENABLED ---
	const lane1 = provisionLaneWorktree({ projectRoot, batchId, laneNumber: 1, orchBranch });
	results.hook = runWorktreeSetupHook({
		projectRoot,
		worktreePath: lane1.worktreePath,
		batchId,
		laneNumber: 1,
		config: { worktreeSetupHook: "scripts/spine-worktree-setup.exe" },
	});
	const prestaged = path.join(lane1.worktreePath, ".pi", "npm", "node_modules", "pi-spine");
	results.piSpinePresent = fs.existsSync(path.join(prestaged, "package.json"));
	try {
		const v = execFileSync(process.execPath, [path.join(lane1.worktreePath, ".spine", "patches", "verify.mjs"), "--root", prestaged], { encoding: "utf-8" });
		results.verifyExit = 0;
		results.verifyTail = v.trim().split(/\r?\n/).slice(-1)[0];
	} catch (e) {
		results.verifyExit = e.status;
		results.verifyTail = String(e.stdout ?? e.message).trim().split(/\r?\n/).slice(-3);
	}
	const logPath = path.join(lane1.worktreePath, ".pi", "npm", "worktree-setup-hook.log");
	results.hookLog = fs.existsSync(logPath) ? fs.readFileSync(logPath, "utf-8").trim() : "(missing)";

	// --- Test 2: NEGATIVE CONTROL (hook disabled) ---
	const lane2 = provisionLaneWorktree({ projectRoot, batchId, laneNumber: 2, orchBranch });
	results.control = runWorktreeSetupHook({
		projectRoot,
		worktreePath: lane2.worktreePath,
		batchId,
		laneNumber: 2,
		config: {},
	});
	results.controlPiSpinePresent = fs.existsSync(path.join(lane2.worktreePath, ".pi", "npm", "node_modules", "pi-spine", "package.json"));
} finally {
	for (const n of [1, 2]) {
		try { removeLaneWorktree(projectRoot, batchId, n); } catch (e) { results[`cleanupLane${n}`] = String(e); }
	}
	try {
		const branches = execFileSync("git", ["branch", "--list", "*scratch-sp039*", "--format=%(refname:short)"], { cwd: projectRoot, encoding: "utf-8" }).trim().split(/\r?\n/).filter(Boolean);
		for (const b of branches) {
			execFileSync("git", ["branch", "-D", b], { cwd: projectRoot, stdio: "pipe" });
		}
		results.deletedBranches = branches;
	} catch (e) { results.branchCleanup = String(e); }
	try { execFileSync("git", ["worktree", "prune"], { cwd: projectRoot, stdio: "pipe" }); } catch { /* best effort */ }
	try { fs.rmSync(tempExe, { force: true }); if (createdScriptsDir) fs.rmdirSync(tempExeDir); } catch (e) { results.exeCleanup = String(e); }
	results.leftoverDir = fs.existsSync(path.join(projectRoot, ".worktrees", `spine-${batchId}`));
}
console.log(JSON.stringify(results, null, 2));
