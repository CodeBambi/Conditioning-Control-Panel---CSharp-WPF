// SP-116 A/B arm. STRICTLY ALTERNATING base and lane runs in ONE window, because that is the only
// design that separates "the tree changed" from "the desktop drifted" (SP-112's lesson). Each run
// checks the six touched test files out of the chosen commit, rebuilds, and runs the full unit
// suite exactly once with TRX preserved. Every launched run is counted; nothing is re-run and no
// run is discarded. The tree is restored to the lane commit at the end.
//
//   node spine-tasks/SP-116-flake-characterisation/ab.mjs <pairs> <baseSha> <laneSha>
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdtempSync, appendFileSync, writeFileSync, readdirSync, readFileSync, existsSync } from 'node:fs';
import { resolve, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '..', '..');
const logPath = resolve(here, 'ab-arm.log');

const pairs = Number(process.argv[2] ?? 30);
const baseSha = process.argv[3];
const laneSha = process.argv[4];
if (!baseSha || !laneSha) {
  console.error('usage: node ab.mjs <pairs> <baseSha> <laneSha>');
  process.exit(2);
}

const FILES = [
  'client/tests/CcpClient.Tests/FlashPixelProbe.cs',
  'client/tests/CcpClient.Tests/FlashDrawObservations.cs',
  'client/tests/CcpClient.Tests/FlashEndToEndObservations.cs',
  'client/tests/CcpClient.Tests/FlashDrawTests.cs',
  'client/tests/CcpClient.Tests/SpiralOverlayEffectTests.cs',
  'client/tests/CcpClient.Tests/MovingEffectSpineTests.cs',
];

function checkout(sha) {
  execFileSync('git', ['checkout', sha, '--', ...FILES], { cwd: root });
}

function unescapeXml(s) {
  return s.replaceAll('&lt;', '<').replaceAll('&gt;', '>').replaceAll('&quot;', '"')
    .replaceAll('&apos;', "'").replaceAll('&#xD;', '').replaceAll('&amp;', '&');
}

function trxFailures(dir) {
  if (!existsSync(dir)) return [{ name: '<no results directory>', message: '' }];
  const trx = readdirSync(dir).filter(f => f.endsWith('.trx'));
  if (trx.length === 0) return [{ name: '<no trx>', message: '' }];
  const xml = readFileSync(join(dir, trx[0]), 'utf8');
  const out = [];
  const re = /<UnitTestResult\b[^>]*?testName="([^"]*)"[^>]*?outcome="([^"]*)"[\s\S]*?(?:<\/UnitTestResult>|\/>)/g;
  let m;
  while ((m = re.exec(xml)) !== null) {
    if (m[2] !== 'Failed') continue;
    const msg = /<Message>([\s\S]*?)<\/Message>/.exec(m[0]);
    out.push({ name: unescapeXml(m[1]), message: msg ? unescapeXml(msg[1]).trim() : '' });
  }
  return out.length ? out : [{ name: '<failed run, no failed result in trx>', message: '' }];
}

function runOnce(label, index) {
  const build = spawnSync('dotnet', [
    'build', 'client/tests/CcpClient.Tests/CcpClient.Tests.csproj', '-c', 'Debug', '--nologo',
  ], { cwd: root, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (build.status !== 0) {
    appendFileSync(logPath, `${label} r${index}  BUILD-FAILED\n${(build.stdout ?? '').slice(-2000)}\n`);
    return true;
  }

  const results = mkdtempSync(join(tmpdir(), 'ccp-sp116ab-'));
  const started = Date.now();
  const r = spawnSync('dotnet', [
    'test', 'client/tests/CcpClient.Tests/CcpClient.Tests.csproj', '-c', 'Debug', '--nologo',
    '--no-build', '--logger', 'trx', '--results-directory', results,
  ], { cwd: root, encoding: 'utf8', maxBuffer: 128 * 1024 * 1024 });
  const secs = ((Date.now() - started) / 1000).toFixed(1);
  const counters = (r.stdout ?? '').split('\n').filter(l => /Failed!|Passed!/.test(l)).join(' ').trim();
  if (r.status === 0) {
    appendFileSync(logPath, `${label} r${index}  GREEN  ${secs}s  ${counters}\n`);
    console.log(`${label} r${index} GREEN`);
    return false;
  }

  appendFileSync(logPath, `${label} r${index}  RED    ${secs}s  ${counters}  results=${results}\n`);
  for (const f of trxFailures(results)) {
    appendFileSync(logPath, `    FAILED: ${f.name}\n      ${f.message.split('\n').join('\n      ')}\n`);
  }
  console.log(`${label} r${index} RED -> ${results}`);
  return true;
}

writeFileSync(logPath, `SP-116 A/B arm: ${pairs} pair(s), base ${baseSha} vs lane ${laneSha}, started ${new Date().toISOString()}\n`, { flag: 'a' });
let baseRed = 0;
let laneRed = 0;
try {
  for (let i = 1; i <= pairs; i++) {
    checkout(baseSha);
    if (runOnce('BASE', i)) baseRed++;
    checkout(laneSha);
    if (runOnce('LANE', i)) laneRed++;
  }
} finally {
  checkout(laneSha);
}

appendFileSync(logPath, `A/B TOTAL: base ${baseRed} red in ${pairs}, lane ${laneRed} red in ${pairs}, ended ${new Date().toISOString()}\n`);
console.log(`A/B TOTAL base ${baseRed}/${pairs}, lane ${laneRed}/${pairs}`);
