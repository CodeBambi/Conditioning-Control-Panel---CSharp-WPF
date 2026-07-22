#!/usr/bin/env node
/**
 * apply.mjs — apply the checked-in pi-spine local patches (manifest.json).
 *
 * Usage:  node .spine/patches/apply.mjs [--root <pi-spine install dir>]
 *
 * Roots (SP-031, two-root model):
 *  - Default: installRoot (the project tree pi sessions load — repo/lane .pi/npm,
 *    resolved relative to this script) PLUS manifest.engineRoot (the GLOBAL CLI
 *    install the batch engine actually executes — process-cmdline-proven;
 *    spine.mjs / spine-worker-runner.mjs run from there). Engine-flagged patches
 *    ("engine": true) apply to BOTH roots; the rest are project-tree-only.
 *  - --root: legacy single-root scratch mode — ALL patches against that dir only.
 *
 * Discipline (pre-approach consult, SP-020 record.md):
 *  - Anchor-based only; never line numbers.
 *  - Tri-state per patch, occurrence-counted:
 *      anchor×1 + replacement×0  -> apply
 *      anchor×0 + replacement×1  -> already applied (idempotent skip)
 *      anything else             -> FAIL LOUDLY naming the patch id (version drift)
 *  - All-or-nothing across ALL roots: every patch on every root is validated
 *    before ANY file is written.
 *  - Rollback note: if a write itself fails mid-apply (disk/permissions), the
 *    already-written patches validate as "already applied" on re-run, so
 *    re-running apply.mjs after fixing the cause converges safely.
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const manifest = JSON.parse(fs.readFileSync(path.join(HERE, "manifest.json"), "utf-8"));

function resolveRoots(argv) {
	const i = argv.indexOf("--root");
	if (i !== -1 && argv[i + 1]) {
		return [{ root: path.resolve(argv[i + 1]), patches: manifest.patches, label: "explicit --root" }];
	}
	const projectRootDir = path.resolve(HERE, "..", "..", manifest.installRoot);
	const roots = [{ root: projectRootDir, patches: manifest.patches, label: "project" }];
	if (manifest.engineRoot) {
		const engineRootDir = path.resolve(manifest.engineRoot);
		if (engineRootDir.toLowerCase() !== projectRootDir.toLowerCase()) {
			roots.push({
				root: engineRootDir,
				patches: manifest.patches.filter((p) => p.engine === true),
				label: "engine",
			});
		}
	}
	return roots;
}

const roots = resolveRoots(process.argv.slice(2));
const count = (haystack, needle) => haystack.split(needle).length - 1;

// Phase 0: every root must exist. A missing engine root means the batch engine's
// install moved or was never installed here — patching the project tree alone
// leaves the engine unpatched (the SP-031 wave-1 failure class: applied != loaded).
for (const { root, label } of roots) {
	if (!fs.existsSync(root)) {
		console.error(
			`FAIL ${label} root missing: ${root}\n` +
			`Remedy: reinstall pi-spine there, or update manifest engineRoot/installRoot to the install the spine CLI actually loads.`,
		);
		process.exit(1);
	}
}

// Phase 1: validate EVERY patch on EVERY root before writing anything.
const plan = [];
let validationFailed = false;
for (const { root, patches, label } of roots) {
	for (const patch of patches) {
		const file = path.join(root, patch.target);
		if (!fs.existsSync(file)) {
			console.error(`FAIL ${patch.id} [${label}]: target missing: ${patch.target}`);
			validationFailed = true;
			continue;
		}
		const content = fs.readFileSync(file, "utf-8");
		const anchors = count(content, patch.anchor);
		const replacements = count(content, patch.replacement);
		if (anchors === 1 && replacements === 0) {
			plan.push({ patch, file, content, label });
			console.log(`apply  ${patch.id} [${label}]`);
		} else if (anchors === 0 && replacements === 1) {
			console.log(`skip   ${patch.id} [${label}] (already applied)`);
		} else {
			console.error(
				`FAIL ${patch.id} [${label}]: anchor×${anchors} replacement×${replacements} — ` +
				`version drift or partial state; inspect ${patch.target} manually (tested against: ${patch.testedVersions.join(", ")})`,
			);
			validationFailed = true;
		}
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
console.log(`\napply.mjs: OK — ${plan.length} applied across ${roots.length} root(s). Run verify.mjs.`);
