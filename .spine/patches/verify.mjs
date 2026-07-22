#!/usr/bin/env node
/**
 * verify.mjs — report per-patch applied/missing/drifted for the pi-spine installs.
 *
 * Usage:  node .spine/patches/verify.mjs [--root <pi-spine install dir>]
 *
 * Run after ANY pi-spine install/update and before any batch.
 * Exit 0 only when every manifest patch is applied on its root(s); exit 1 otherwise.
 *
 * Roots (SP-031, two-root model): default checks installRoot (project tree — all
 * patches) AND manifest.engineRoot (the global CLI install the batch engine
 * executes — engine-flagged patches only). The engine root is the check that
 * proves applied == loaded for engine-behavior patches; skipping it is how
 * SP-028's t5 patch verified green while the engine ran unpatched (wave-1 gate
 * failure). --root keeps legacy single-root all-patches semantics for scratch.
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

console.log(`verify.mjs: patches tested against ${manifest.testedVersions.join(", ")}`);

let allApplied = true;
for (const { root, patches, label } of roots) {
	let pkgVersion = "unknown";
	try {
		pkgVersion = JSON.parse(fs.readFileSync(path.join(root, "package.json"), "utf-8")).version;
	} catch { /* root may not exist */ }
	if (pkgVersion === "unknown") {
		console.error(
			`FAIL ${label} root missing: ${root}\n` +
			`Remedy: reinstall pi-spine there, or update manifest engineRoot/installRoot to the install the spine CLI actually loads.`,
		);
		allApplied = false;
		continue;
	}
	console.log(`verify.mjs: [${label}] pi-spine ${pkgVersion} at ${root} (${patches.length} patches)`);
	if (!manifest.testedVersions.includes(pkgVersion)) {
		console.log(`verify.mjs: WARNING — [${label}] installed version not in testedVersions; expect drift on changed files`);
	}
	for (const patch of patches) {
		const file = path.join(root, patch.target);
		if (!fs.existsSync(file)) {
			console.log(`drifted ${patch.id} [${label}]: target missing: ${patch.target}`);
			allApplied = false;
			continue;
		}
		const content = fs.readFileSync(file, "utf-8");
		const anchors = count(content, patch.anchor);
		const replacements = count(content, patch.replacement);
		if (replacements === 1 && anchors === 0) {
			console.log(`applied  ${patch.id} [${label}]`);
		} else if (anchors === 1 && replacements === 0) {
			console.log(`missing  ${patch.id} [${label}] (reinstall removed it — run apply.mjs)`);
			allApplied = false;
		} else {
			console.log(`drifted ${patch.id} [${label}]: anchor×${anchors} replacement×${replacements} — version bump changed ${patch.target}; re-base the anchor deliberately`);
			allApplied = false;
		}
	}
}

if (!allApplied) {
	console.error("\nverify.mjs: FAIL — patches not all applied.");
	process.exit(1);
}
console.log("\nverify.mjs: OK — all patches applied on all roots.");
