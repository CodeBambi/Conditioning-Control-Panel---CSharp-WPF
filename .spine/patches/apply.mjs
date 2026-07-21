#!/usr/bin/env node
/**
 * apply.mjs — apply the checked-in pi-spine local patches (manifest.json).
 *
 * Usage:  node .spine/patches/apply.mjs [--root <pi-spine install dir>]
 *
 * Default root: <repo>/.pi/npm/node_modules/pi-spine (manifest is at .spine/patches/).
 * --root is for scratch-cycle testing against a throwaway install — the worker
 * never runs this against the repo's real .pi during a batch (orchestrator gate).
 *
 * Discipline (pre-approach consult, SP-020 record.md):
 *  - Anchor-based only; never line numbers.
 *  - Tri-state per patch, occurrence-counted:
 *      anchor×1 + replacement×0  -> apply
 *      anchor×0 + replacement×1  -> already applied (idempotent skip)
 *      anything else             -> FAIL LOUDLY naming the patch id (version drift)
 *  - All-or-nothing: every patch is validated before ANY file is written.
 *    Rollback note: if a write itself fails mid-apply (disk/permissions), the
 *    already-written patches validate as "already applied" on re-run, so
 *    re-running apply.mjs after fixing the cause converges safely.
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const manifest = JSON.parse(fs.readFileSync(path.join(HERE, "manifest.json"), "utf-8"));

function resolveRoot(argv) {
	const i = argv.indexOf("--root");
	if (i !== -1 && argv[i + 1]) return path.resolve(argv[i + 1]);
	return path.resolve(HERE, "..", "..", manifest.installRoot);
}

const root = resolveRoot(process.argv.slice(2));
const count = (haystack, needle) => haystack.split(needle).length - 1;

// Phase 1: validate EVERY patch before writing anything.
const plan = [];
let validationFailed = false;
for (const patch of manifest.patches) {
	const file = path.join(root, patch.target);
	if (!fs.existsSync(file)) {
		console.error(`FAIL ${patch.id}: target missing: ${patch.target}`);
		validationFailed = true;
		continue;
	}
	const content = fs.readFileSync(file, "utf-8");
	const anchors = count(content, patch.anchor);
	const replacements = count(content, patch.replacement);
	if (anchors === 1 && replacements === 0) {
		plan.push({ patch, file, content });
		console.log(`apply  ${patch.id}`);
	} else if (anchors === 0 && replacements === 1) {
		console.log(`skip   ${patch.id} (already applied)`);
	} else {
		console.error(
			`FAIL ${patch.id}: anchor×${anchors} replacement×${replacements} — ` +
			`version drift or partial state; inspect ${patch.target} manually (tested against: ${patch.testedVersions.join(", ")})`,
		);
		validationFailed = true;
	}
}

if (validationFailed) {
	console.error("\napply.mjs: validation failed — NO files were modified (all-or-nothing).");
	process.exit(1);
}

// Phase 2: write.
for (const { patch, file, content } of plan) {
	fs.writeFileSync(file, content.replace(patch.anchor, patch.replacement), "utf-8");
	console.log(`wrote  ${patch.target}`);
}
console.log(`\napply.mjs: OK — ${plan.length} applied, ${manifest.patches.length - plan.length} already applied. Run verify.mjs.`);
