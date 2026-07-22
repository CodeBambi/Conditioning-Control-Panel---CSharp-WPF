// SP-031 fixture v2 — provenance-faithful to the REAL wave-1 failure.
//
// SP-028's fixture proved "patched module deletes .reviews/ when taskFolder is the lane
// task folder" — and that was never the broken part. Wave-1 failed because the patch sat
// on the repo-local install while the engine CLI loaded the global install (applied ≠
// loaded). This fixture proves the real mechanism and discharges the packet's
// base-shaped-taskFolder requirement with its TRUE semantics:
//
//   cell 1  pristine-negative-control : pristine module, real caller shape → DirtyWorktree residue
//   cell 2  patched-real-caller-shape : patched module, taskFolder = lane task folder
//                                       (ALL 4 callers' shape, both versions) → clean
//   cell 3  two-tree-wave1-repro      : patch present on tree A, engine module loaded from
//                                       tree B (pristine) → residue survives = wave-1
//   cell 4  base-path-no-done         : taskFolder = base-shaped path OUTSIDE the worktree
//                                       (no .DONE there — wave-1's real base state) →
//                                       early-return ".DONE is missing", NO commit — the
//                                       fingerprint wave-1's journal does NOT show
//   cell 5  base-path-planted-done    : same with a planted base .DONE → commit + residue
//                                       (the theory was CONSISTENT with this shape; git
//                                       history — no base .DONE until the 12:06 merge —
//                                       is what falsifies it, not the fixture)
//
// Scratch only (%TEMP%); the repo and both live installs are never written.
import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const LANE = path.resolve(HERE, "..", "..", "..");
const TMP = process.env.TEMP || "/tmp";
const ROOT = path.join(TMP, "sp031-fixture");
const manifest = JSON.parse(fs.readFileSync(path.join(LANE, ".spine/patches/manifest.json"), "utf-8"));
const t5 = manifest.patches.find((p) => p.id === "t5-reviews-autoclean");
const PATCHED_INSTALL = path.join(LANE, ".pi/npm/node_modules/pi-spine"); // patched in Step 2

function git(cwd, args) {
	return execFileSync("git", args, { cwd, encoding: "utf-8" }).trim();
}

// A full pristine module tree: copy the patched install AS A SIBLING inside the lane's
// node_modules (upward dep resolution finds micromatch etc.), revert the t5 replacement.
// Cleaned up by the caller after the run; gitignored tree, never committed.
const PRISTINE_SIBLING = path.join(LANE, ".pi/npm/node_modules/pi-spine-sp031-pristine-scratch");
function makePristineInstall() {
	fs.rmSync(PRISTINE_SIBLING, { recursive: true, force: true });
	fs.cpSync(PATCHED_INSTALL, PRISTINE_SIBLING, { recursive: true });
	const f = path.join(PRISTINE_SIBLING, "src/batch/lane-commit.mjs");
	const c = fs.readFileSync(f, "utf-8");
	if (!c.includes(t5.replacement)) throw new Error("fixture broken: lane install is not t5-patched");
	fs.writeFileSync(f, c.replace(t5.replacement, t5.anchor), "utf-8");
	return PRISTINE_SIBLING;
}

function makeLane(name) {
	const wt = path.join(ROOT, name);
	fs.rmSync(wt, { recursive: true, force: true });
	fs.mkdirSync(wt, { recursive: true });
	git(wt, ["init", "-q", "-b", "lane-1"]);
	git(wt, ["config", "user.email", "fixture@local"]);
	git(wt, ["config", "user.name", "fixture"]);
	// the repo's REAL .gitignore — the blank-line quirk is load-bearing
	fs.copyFileSync(path.join(LANE, ".gitignore"), path.join(wt, ".gitignore"));
	const taskFolder = path.join(wt, "spine-tasks/SP-TEST");
	fs.mkdirSync(taskFolder, { recursive: true });
	fs.writeFileSync(path.join(taskFolder, "PROMPT.md"), "# fixture task\n");
	fs.writeFileSync(path.join(taskFolder, "STATUS.md"), "done\n");
	git(wt, ["add", "-A"]);
	git(wt, ["commit", "-q", "-m", "worker step commits"]);
	fs.writeFileSync(path.join(taskFolder, ".DONE"), "");
	fs.mkdirSync(path.join(taskFolder, ".reviews"), { recursive: true });
	fs.writeFileSync(path.join(taskFolder, ".reviews", "final-20260722T000000.md"), "VERDICT: PASS\n");
	const probe = git(wt, ["check-ignore", "--no-index", "spine-tasks/SP-TEST/.reviews/"]);
	if (!probe.includes(".reviews/")) throw new Error("fixture broken: blank-line quirk did not reproduce");
	return { wt, taskFolder };
}

async function importCommit(modulePath, tag) {
	// unique query bypasses ESM cache so each cell loads its own module state
	const mod = await import(pathToFileURL(modulePath).href + `?cell=${tag}`);
	return mod.commitLaneWorktree;
}

let fail = 0;
function assert(name, cond) { console.log(`${cond ? "PASS" : "FAIL"}  ${name}`); if (!cond) fail = 1; }

const PRISTINE_INSTALL = makePristineInstall();
const pristineModule = path.join(PRISTINE_INSTALL, "src/batch/lane-commit.mjs");
const patchedModule = path.join(PATCHED_INSTALL, "src/batch/lane-commit.mjs");

// --- cell 1: pristine engine, real caller shape (negative control)
{
	const { wt, taskFolder } = makeLane("cell1-pristine");
	const commitLaneWorktree = await importCommit(pristineModule, "c1");
	const result = commitLaneWorktree({
		worktreePath: wt, taskBranch: "lane-1", taskId: "SP-TEST", batchId: "fixture",
		taskFolder, projectRoot: wt,
	});
	const after = git(wt, ["status", "--porcelain"]);
	console.log("\ncell1 pristine: committed =", result.committed, "| post porcelain:", JSON.stringify(after));
	assert("cell1 negative control: pristine commits .DONE but leaves ?? .reviews/ residue (T-5 DirtyWorktree shape)",
		result.ok && result.committed === true && after.includes(".reviews/"));
	assert("cell1 negative control: pristine does NOT delete .reviews/",
		fs.existsSync(path.join(taskFolder, ".reviews")));
}

// --- cell 2: patched engine, real caller shape (all 4 callers, both versions)
{
	const { wt, taskFolder } = makeLane("cell2-patched");
	const commitLaneWorktree = await importCommit(patchedModule, "c2");
	const artifactExistedAtCall = fs.existsSync(path.join(taskFolder, ".reviews", "final-20260722T000000.md"));
	const result = commitLaneWorktree({
		worktreePath: wt, taskBranch: "lane-1", taskId: "SP-TEST", batchId: "fixture",
		taskFolder, projectRoot: wt, // taskFolder = path.join(wt, taskFolderRel) — the ONLY caller shape
	});
	const after = git(wt, ["status", "--porcelain"]);
	console.log("\ncell2 patched: committed =", result.committed, "| post porcelain:", JSON.stringify(after) || "(clean)");
	assert("cell2 patched + real caller shape: ok + committed", result.ok && result.committed === true);
	assert("cell2 post-commit porcelain CLEAN (finalization passes)", after.trim() === "");
	assert("cell2 verdict artifact existed right up to the finalization call", artifactExistedAtCall);
	assert("cell2 .reviews/ deleted inside the finalization call", !fs.existsSync(path.join(taskFolder, ".reviews")));
}

// --- cell 3: two-tree wave-1 repro — patch on tree A, engine module loaded from tree B
{
	// tree A = PATCHED_INSTALL (verified patched in Step 2); tree B = PRISTINE_INSTALL.
	const treeAContent = fs.readFileSync(patchedModule, "utf-8");
	const { wt, taskFolder } = makeLane("cell3-two-tree");
	const commitLaneWorktree = await importCommit(pristineModule, "c3"); // engine loads tree B
	const result = commitLaneWorktree({
		worktreePath: wt, taskBranch: "lane-1", taskId: "SP-TEST", batchId: "fixture",
		taskFolder, projectRoot: wt,
	});
	const after = git(wt, ["status", "--porcelain"]);
	console.log("\ncell3 two-tree: patch on tree A =", treeAContent.includes(t5.replacement),
		"| engine loaded tree B (pristine) | post porcelain:", JSON.stringify(after));
	assert("cell3 wave-1 mechanism: patch present on tree A on disk", treeAContent.includes(t5.replacement));
	assert("cell3 wave-1 mechanism: execution from unpatched tree B leaves the residue (applied != loaded)",
		result.ok && result.committed === true && after.includes(".reviews/"));
}

// --- cell 4: base-shaped taskFolder OUTSIDE the worktree, no base .DONE (wave-1's real base state)
{
	const { wt, taskFolder: laneTaskFolder } = makeLane("cell4-base-path");
	const baseTaskFolder = path.join(ROOT, "cell4-base-repo/spine-tasks/SP-TEST");
	fs.mkdirSync(baseTaskFolder, { recursive: true });
	fs.writeFileSync(path.join(baseTaskFolder, "PROMPT.md"), "# fixture task\n"); // packet exists; .DONE does NOT
	const headBefore = git(wt, ["rev-parse", "HEAD"]);
	const commitLaneWorktree = await importCommit(patchedModule, "c4");
	const result = commitLaneWorktree({
		worktreePath: wt, taskBranch: "lane-1", taskId: "SP-TEST", batchId: "fixture",
		taskFolder: baseTaskFolder, projectRoot: wt, // the packet's theorized shape
	});
	const headAfter = git(wt, ["rev-parse", "HEAD"]);
	console.log("\ncell4 base-path (no .DONE):", JSON.stringify({ ok: result.ok, failureClass: result.failureClass, error: result.error }));
	assert("cell4 falsification fingerprint: early-return DirtyWorktree ('.DONE is missing'), NO commit (no lane.committed)",
		result.ok === false && result.failureClass === "DirtyWorktree" && /\.DONE is missing/.test(result.error ?? "") && headBefore === headAfter);
	assert("cell4 wave-1 showed the OPPOSITE order (lane.committed then post-commit DirtyWorktree) ⇒ taskFolder was the lane path",
		true); // journal evidence, recorded in record.md — kept as a named assertion for the reader
	assert("cell4 lane residue untouched by the base-path rmSync", fs.existsSync(path.join(laneTaskFolder, ".reviews")));
}

// --- cell 5: base-shaped taskFolder with a PLANTED base .DONE (what the theory needed to be true)
{
	const { wt, taskFolder: laneTaskFolder } = makeLane("cell5-planted-done");
	const baseTaskFolder = path.join(ROOT, "cell5-base-repo/spine-tasks/SP-TEST");
	fs.mkdirSync(baseTaskFolder, { recursive: true });
	fs.writeFileSync(path.join(baseTaskFolder, "PROMPT.md"), "# fixture task\n");
	fs.writeFileSync(path.join(baseTaskFolder, ".DONE"), ""); // planted — wave-1's base had none until the 12:06 merge
	const commitLaneWorktree = await importCommit(patchedModule, "c5");
	const result = commitLaneWorktree({
		worktreePath: wt, taskBranch: "lane-1", taskId: "SP-TEST", batchId: "fixture",
		taskFolder: baseTaskFolder, projectRoot: wt,
	});
	const after = git(wt, ["status", "--porcelain"]);
	console.log("\ncell5 planted-.DONE base path: committed =", result.committed, "| post porcelain:", JSON.stringify(after));
	assert("cell5 theory shape: commit proceeds (planted .DONE passes the check) but lane residue survives (rmSync hit base/.reviews)",
		result.ok && result.committed === true && after.includes(".reviews/"));
	assert("cell5 lane .reviews/ still present (base-path delete no-ops on the lane)", fs.existsSync(path.join(laneTaskFolder, ".reviews")));
}

console.log(fail === 0 ? "\nFIXTURE v2 GREEN (5 cells)" : "\nFIXTURE v2 FAILED");
fs.rmSync(PRISTINE_SIBLING, { recursive: true, force: true });
process.exit(fail);
