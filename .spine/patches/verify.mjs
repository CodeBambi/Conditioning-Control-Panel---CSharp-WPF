#!/usr/bin/env node
/**
 * verify.mjs — report per-patch applied/missing/drifted for the pi-spine install.
 *
 * Usage:  node .spine/patches/verify.mjs [--root <pi-spine install dir>]
 *
 * Run after ANY pi-spine install/update and before any batch.
 * Exit 0 only when every manifest patch is applied; exit 1 otherwise.
 *
 * States (occurrence-counted):
 *   applied  — replacement present exactly once, anchor absent
 *   missing  — anchor present exactly once, replacement absent (reinstall killed it)
 *   drifted  — anything else (both present, neither present, duplicates):
 *              a version bump changed the target; the anchor must be re-based
 *              deliberately — never force-apply.
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

let pkgVersion = "unknown";
try {
	pkgVersion = JSON.parse(fs.readFileSync(path.join(root, "package.json"), "utf-8")).version;
} catch { /* root may not exist */ }

console.log(`verify.mjs: pi-spine ${pkgVersion} at ${root}`);
console.log(`verify.mjs: patches tested against ${manifest.testedVersions.join(", ")}`);
if (pkgVersion !== "unknown" && !manifest.testedVersions.includes(pkgVersion)) {
	console.log(`verify.mjs: WARNING — installed version not in testedVersions; expect drift on changed files`);
}

let allApplied = true;
for (const patch of manifest.patches) {
	const file = path.join(root, patch.target);
	if (!fs.existsSync(file)) {
		console.log(`drifted ${patch.id}: target missing: ${patch.target}`);
		allApplied = false;
		continue;
	}
	const content = fs.readFileSync(file, "utf-8");
	const anchors = count(content, patch.anchor);
	const replacements = count(content, patch.replacement);
	if (replacements === 1 && anchors === 0) {
		console.log(`applied  ${patch.id}`);
	} else if (anchors === 1 && replacements === 0) {
		console.log(`missing  ${patch.id} (reinstall removed it — run apply.mjs)`);
		allApplied = false;
	} else {
		console.log(`drifted ${patch.id}: anchor×${anchors} replacement×${replacements} — version bump changed ${patch.target}; re-base the anchor deliberately`);
		allApplied = false;
	}
}

if (!allApplied) {
	console.error("\nverify.mjs: FAIL — patches not all applied.");
	process.exit(1);
}
console.log("\nverify.mjs: OK — all patches applied.");
