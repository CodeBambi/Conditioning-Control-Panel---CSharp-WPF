// SP-116 reproduction harness. ONE run = one full `dotnet test` of CcpClient.Tests, sequential,
// --no-build, TRX preserved. Every launched run is counted; nothing is ever re-run and no run is
// discarded. On red every failed test's fully-qualified NAME and MESSAGE come out of the TRX,
// because that is the artifact SP-106 lost and SP-115 did not capture.
//
//   node spine-tasks/SP-116-flake-characterisation/repeat.mjs <runs> <label> <logfile>
//
// It never touches client/tests/floor/floor.json and it never re-runs a failure.
import { spawnSync } from 'node:child_process';
import { mkdtempSync, appendFileSync, writeFileSync, readdirSync, readFileSync, existsSync } from 'node:fs';
import { resolve, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '..', '..');

const runs = Number(process.argv[2] ?? 1);
const label = process.argv[3] ?? 'RUN';
const logPath = resolve(here, process.argv[4] ?? 'repeat.log');

function trxFailures(dir) {
  if (!existsSync(dir)) return [{ name: '<no results directory>', message: '' }];
  const trx = readdirSync(dir).filter(f => f.endsWith('.trx'));
  if (trx.length === 0) return [{ name: '<no trx>', message: '' }];
  const xml = readFileSync(join(dir, trx[0]), 'utf8');
  const out = [];
  // UnitTestResult elements carry testName + outcome; the Message is nested in Output/ErrorInfo.
  const re = /<UnitTestResult\b[^>]*?testName="([^"]*)"[^>]*?outcome="([^"]*)"[\s\S]*?(?:<\/UnitTestResult>|\/>)/g;
  let m;
  while ((m = re.exec(xml)) !== null) {
    if (m[2] !== 'Failed') continue;
    const body = m[0];
    const msg = /<Message>([\s\S]*?)<\/Message>/.exec(body);
    const stack = /<StackTrace>([\s\S]*?)<\/StackTrace>/.exec(body);
    out.push({
      name: unescapeXml(m[1]),
      message: msg ? unescapeXml(msg[1]).trim() : '',
      stack: stack ? unescapeXml(stack[1]).trim().split('\n').slice(0, 4).join('\n') : '',
    });
  }
  return out.length ? out : [{ name: '<failed run, no failed result in trx>', message: '' }];
}

function unescapeXml(s) {
  return s.replaceAll('&lt;', '<').replaceAll('&gt;', '>').replaceAll('&quot;', '"')
    .replaceAll('&apos;', "'").replaceAll('&#xD;', '').replaceAll('&amp;', '&');
}

writeFileSync(logPath, `SP-116 ${label}: ${runs} run(s) of CcpClient.Tests, started ${new Date().toISOString()}\n`, { flag: 'a' });
let red = 0;
for (let i = 1; i <= runs; i++) {
  const results = mkdtempSync(join(tmpdir(), 'ccp-sp116-'));
  const started = Date.now();
  const r = spawnSync('dotnet', [
    'test', 'client/tests/CcpClient.Tests/CcpClient.Tests.csproj',
    '-c', 'Debug', '--nologo', '--no-build',
    '--logger', 'trx', '--results-directory', results,
  ], { cwd: root, encoding: 'utf8', shell: false, maxBuffer: 128 * 1024 * 1024 });
  const secs = ((Date.now() - started) / 1000).toFixed(1);
  const counters = (r.stdout ?? '').split('\n').filter(l => /Failed!|Passed!|Failed:/.test(l)).join(' ').trim();
  if (r.status === 0) {
    appendFileSync(logPath, `${label} r${i}  GREEN  ${secs}s  ${counters}\n`);
    console.log(`${label} r${i} GREEN ${secs}s`);
  } else {
    red++;
    appendFileSync(logPath, `${label} r${i}  RED    ${secs}s  ${counters}  results=${results}\n`);
    for (const f of trxFailures(results)) {
      appendFileSync(logPath, `    FAILED: ${f.name}\n      ${f.message.split('\n').join('\n      ')}\n`);
      if (f.stack) appendFileSync(logPath, `      AT: ${f.stack.split('\n').join(' | ')}\n`);
    }
    console.log(`${label} r${i} RED ${secs}s -> ${results}`);
  }
}
appendFileSync(logPath, `${label} TOTAL: ${runs} runs, ${red} red, ended ${new Date().toISOString()}\n`);
console.log(`${label} TOTAL ${runs} runs, ${red} red`);
