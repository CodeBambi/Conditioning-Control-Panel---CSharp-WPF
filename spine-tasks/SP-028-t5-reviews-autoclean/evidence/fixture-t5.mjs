// SP-028 fixture: patched vs pristine commitLaneWorktree against a scratch lane with
// .reviews/ residue. Proves: (1) pristine = T-5 negative control (DirtyWorktree residue),
// (2) patched = finalization passes, (3) .reviews/ deletion happens inside the finalization
// call (after the verdict artifact existed), never earlier, (4) journal stand-in outside
// the lane is untouched. Scratch only; the repo is never touched.
import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(HERE, "..", "..", "..");
const ROOT = path.join(process.env.TEMP || "/tmp", "sp028-fixture");
const PATCHED = path.join(REPO, ".pi/npm/node_modules/pi-spine/src/batch/lane-commit.mjs");
const PRISTINE = path.join(process.env.TEMP || "/tmp", "sp028-scratch/pristine-2.10.0/src/batch/lane-commit.mjs");

function git(cwd, args) {
	return execFileSync("git", args, { cwd, encoding: "utf-8" }).trim();
}

function makeLane(name) {
	const wt = path.join(ROOT, name);
	fs.rmSync(wt, { recursive: true, force: true });
	fs.mkdirSync(wt, { recursive: true });
	git(wt, ["init", "-q", "-b", "lane-1"]);
	git(wt, ["config", "user.email", "fixture@local"]);
	git(wt, ["config", "user.name", "fixture"]);
	// the repo's REAL .gitignore — blank line 179 is load-bearing for the git 2.49 quirk
	fs.copyFileSync(path.join(REPO, ".gitignore"), path.join(wt, ".gitignore"));
	const taskFolder = path.join(wt, "spine-tasks/SP-TEST");
	fs.mkdirSync(taskFolder, { recursive: true });
	fs.writeFileSync(path.join(taskFolder, "PROMPT.md"), "# fixture task\n");
	fs.writeFileSync(path.join(taskFolder, "STATUS.md"), "done\n");
	git(wt, ["add", "-A"]);
	git(wt, ["commit", "-q", "-m", "worker step commits"]);
	// worker finishes: .DONE left uncommitted (matches every real lane auto-commit)
	fs.writeFileSync(path.join(taskFolder, ".DONE"), "");
	// engine review stage: verdict artifact written AFTER the worker's last commit;
	// verdict journal lives OUTSIDE the lane (projectRoot .spine/runtime stand-in)
	fs.mkdirSync(path.join(taskFolder, ".reviews"), { recursive: true });
	fs.writeFileSync(path.join(taskFolder, ".reviews", "final-20260722T000000.md"), "VERDICT: PASS\n");
	const journal = path.join(ROOT, `${name}-journal.jsonl`);
	fs.writeFileSync(journal, JSON.stringify({ type: "review.completed", verdict: "PASS" }) + "\n");
	// sanity: the quirk reproduces in this scratch tree
	const probe = git(wt, ["check-ignore", "--no-index", "spine-tasks/SP-TEST/.reviews/"]);
	if (!probe.includes(".reviews/")) throw new Error("fixture broken: blank-line quirk did not reproduce");
	return { wt, taskFolder, journal };
}

async function run(label, modulePath) {
	const { wt, taskFolder, journal } = makeLane(label);
	const before = git(wt, ["status", "--porcelain"]);
	const artifactExistedAtCall = fs.existsSync(path.join(taskFolder, ".reviews", "final-20260722T000000.md"));
	const { commitLaneWorktree } = await import(pathToFileURL(modulePath).href);
	const result = commitLaneWorktree({
		worktreePath: wt,
		taskBranch: "lane-1",
		taskId: "SP-TEST",
		batchId: "fixture",
		taskFolder,
		projectRoot: wt,
	});
	const after = git(wt, ["status", "--porcelain"]);
	const reviewsGone = !fs.existsSync(path.join(taskFolder, ".reviews"));
	const journalIntact = fs.readFileSync(journal, "utf-8").includes('"PASS"');
	console.log(`\n=== ${label} (${path.basename(path.dirname(modulePath)) === "batch" ? modulePath.includes("pristine") ? "PRISTINE" : "PATCHED" : "?"})`);
	console.log("pre-call porcelain:", JSON.stringify(before));
	console.log("verdict artifact existed at finalization call:", artifactExistedAtCall);
	console.log("commitLaneWorktree ->", JSON.stringify({ ok: result.ok, committed: result.committed, failureClass: result.failureClass }));
	console.log("post-call porcelain:", JSON.stringify(after) || "(clean)");
	console.log(".reviews/ deleted:", reviewsGone, "| journal stand-in intact:", journalIntact);
	return { result, after, reviewsGone, journalIntact, artifactExistedAtCall };
}

const pristine = await run("pristine-lane", PRISTINE);
const patched = await run("patched-lane", PATCHED);

let fail = 0;
function assert(name, cond) { console.log(`${cond ? "PASS" : "FAIL"}  ${name}`); if (!cond) fail = 1; }

console.log("\n--- assertions");
assert("negative control: pristine commits but leaves ?? .reviews/ residue (T-5 DirtyWorktree shape)",
	pristine.result.ok && pristine.after.includes(".reviews/"));
assert("negative control: pristine does NOT delete .reviews/", !pristine.reviewsGone);
assert("patched: commitLaneWorktree ok + committed", patched.result.ok && patched.result.committed === true);
assert("patched: post-commit porcelain CLEAN (finalization passes)", patched.after.trim() === "");
assert("event order: verdict artifact existed right up to the finalization call", patched.artifactExistedAtCall);
assert("patched: .reviews/ deleted inside the finalization call (not earlier)", patched.reviewsGone);
assert("verdicts journal-durable: journal stand-in outside the lane untouched", patched.journalIntact && pristine.journalIntact);

console.log(fail === 0 ? "\nFIXTURE GREEN" : "\nFIXTURE FAILED");
process.exit(fail);
