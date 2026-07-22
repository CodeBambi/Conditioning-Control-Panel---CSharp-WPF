// SP-031 Step 2 — live-tree migration (atomic single write per file; consult condition:
// no restore→apply two-step window while a live engine could resume-handoff mid-migration).
// After this script: repo tree carries the new canonical dotnet + t5-comment texts;
// global tree carries canonical fsync comments + tail block; t5 on global is left for
// apply.mjs (anchor present). Every replacement asserts an exact occurrence count and
// FAILS LOUD without writing on any mismatch.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const LANE = path.resolve(HERE, "..", "..", "..");
const manifest = JSON.parse(fs.readFileSync(path.join(LANE, ".spine/patches/manifest.json"), "utf-8"));
const REPO = "E:/Code/Conditioning-Control-Panel";
const GLOBAL = manifest.engineRoot;
const patch = (id) => manifest.patches.find((p) => p.id === id);

const count = (h, n) => h.split(n).length - 1;
function swap(file, oldText, newText, expect = 1) {
	const raw = fs.readFileSync(file, "utf-8");
	const eol = raw.includes("\r\n") ? "\r\n" : "\n";
	const o = oldText.replace(/\n/g, eol);
	const n = newText.replace(/\n/g, eol);
	const hits = count(raw, o);
	if (hits !== expect) {
		console.error(`FAIL ${file}: expected ${expect} occurrence(s), found ${hits} — NO WRITE`);
		process.exit(1);
	}
	fs.writeFileSync(file, raw.replace(o, n), "utf-8");
	console.log(`migrated ${file} (${oldText.length}B -> ${newText.length}B, eol=${eol === "\r\n" ? "CRLF" : "LF"})`);
}

// 1. Repo tree: dotnet allowlist canonical text gains "dotnet.exe" (one line).
swap(
	path.join(REPO, ".pi/npm/node_modules/pi-spine/src/batch/evidence-command.mjs"),
	`new Set(["npm", "node", "pnpm", "yarn", "npx", "dotnet"]);`,
	`new Set(["npm", "node", "pnpm", "yarn", "npx", "dotnet", "dotnet.exe"]);`,
);

// 2. Repo tree: t5 ponytail comment block -> corrected comment (wave-1 root cause).
swap(
	path.join(REPO, ".pi/npm/node_modules/pi-spine/src/batch/lane-commit.mjs"),
	"\t// ponytail: T-5 - engine review phases write .reviews/ into the lane task folder AFTER\n" +
	"\t// the worker's last commit; git 2.49 check-ignore --no-index false-matches the\n" +
	"\t// trailing-slash untracked dir against a blank .gitignore line, so filterGitignoredPaths\n" +
	"\t// mis-skips it and the post-commit porcelain re-check fails DirtyWorktree (17 journal\n" +
	"\t// occurrences, 12 consecutive Level-2 lands SP-012..SP-027, 4 on the resume paths this\n" +
	"\t// function also serves). Verdicts are journal-durable (every land reads them from the\n" +
	"\t// journal), so deleting here mirrors the 15x-proven manual recovery step. All 4 callers\n" +
	"\t// are post-review lane finalization; verdict recording always precedes this delete.\n",
	"\t// ponytail: T-5 - engine review phases write .reviews/ into the lane task folder AFTER\n" +
	"\t// the worker's last commit; git 2.49 check-ignore --no-index false-matches the\n" +
	"\t// trailing-slash untracked dir against a blank .gitignore line, so filterGitignoredPaths\n" +
	"\t// mis-skips it and the post-commit porcelain re-check fails DirtyWorktree (18 journal\n" +
	"\t// occurrences incl. wave 20260722T101444). Verdicts are journal-durable, so deleting here\n" +
	"\t// mirrors the 15x-proven manual recovery. taskFolder IS the lane task folder at all 4\n" +
	"\t// finalization callers (SP-031) — wave-1's gate failure was this patch sitting on the\n" +
	"\t// repo-local install while the engine CLI loads the global install; keep BOTH roots patched.\n",
);

// 3+4. Global tree: fsync hand-variants lack only the ponytail comment (behaviorally identical).
for (const target of ["src/batch/abort.mjs", "src/batch/lifecycle-archive.mjs"]) {
	swap(
		path.join(GLOBAL, target),
		`\tconst fd = fs.openSync(archivePath, "r+");\n\ttry {\n\t\tfs.fsyncSync(fd);`,
		`\tconst fd = fs.openSync(archivePath, "r+"); // ponytail: Windows EPERMs fsync on read-only handles\n\ttry {\n\t\tfs.fsyncSync(fd);`,
	);
}

// 5. Global tree: tail hand-variant block -> canonical manifest text (equivalent, proven in
// SP-020's stub cycle; region located programmatically to avoid transcription drift).
{
	const file = path.join(GLOBAL, "bin/spine-worker-runner.mjs");
	const raw = fs.readFileSync(file, "utf-8");
	const eol = raw.includes("\r\n") ? "\r\n" : "\n";
	const startMark = "\t// ponytail(local patch, 2026-07-18):";
	const endMark = "\treturn piArgs;";
	const start = raw.indexOf(startMark);
	const end = raw.indexOf(endMark, start);
	if (start === -1 || end === -1 || count(raw, startMark) !== 1) {
		console.error(`FAIL ${file}: hand tail block not uniquely located — NO WRITE`);
		process.exit(1);
	}
	const canonical = patch("worker-tail-at-file").replacement
		.replace(/^\t\}\);\n/, "") // strip the shared anchor context line
		.replace(/\n/g, eol);
	const oldBlock = raw.slice(start, end + endMark.length);
	fs.writeFileSync(file, raw.replace(oldBlock, canonical), "utf-8");
	console.log(`migrated ${file} (tail hand-variant ${oldBlock.length}B -> canonical ${canonical.length}B)`);
	// Note: `import os from "node:os"` (line 21) becomes unused after this swap — harmless, kept.
}

console.log("migrate-trees: OK — 5 single-write migrations complete; t5 on the engine root is left to apply.mjs.");
