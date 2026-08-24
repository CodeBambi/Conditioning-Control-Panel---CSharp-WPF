// Board row shape check for client/docs/task-board.md.
//
// Every queue row is `| Pn | STATUS | title | body |` and must carry exactly five STRUCTURAL pipes.
// Two ways that breaks, both of which have happened repeatedly and neither of which any other gate
// sees:
//
//   1. A row loses its trailing pipe, and the table renders broken from there down. The
//      coordinator's own append scripts did this FOUR times.
//   2. A literal pipe inside a cell — `WS_EX_LAYERED | WS_EX_TOOLWINDOW`, `a || b` — splits the
//      cell into extra columns when rendered. That happened in EIGHT rows before anyone noticed,
//      because the file still reads fine as plain text. Write `\|` inside a cell.
//
// This is the cheapest possible check for a document that is the port's only live queue, and it
// exists because "remember to add the trailing pipe" demonstrably does not work.
import { readFileSync } from "node:fs";

const path = "client/docs/task-board.md";
const ESCAPED = /\\\|/g; // a literal backslash followed by a pipe: the escaped form, not structural.

const rows = readFileSync(path, "utf8")
  .split(/\r?\n/)
  .map((line, i) => ({ line, n: i + 1 }))
  .filter((r) => r.line.startsWith("| P") || r.line.startsWith("| Priority "));

const structuralPipes = (line) => (line.replace(ESCAPED, "").match(/\|/g) ?? []).length;
const bad = rows.filter((r) => structuralPipes(r.line) !== 5);

if (bad.length) {
  console.error(`BOARD SHAPE FAILED: ${bad.length} malformed row(s) of ${rows.length} in ${path}`);
  for (const r of bad) {
    const why = r.line.trimEnd().endsWith("|")
      ? `${structuralPipes(r.line)} structural pipes, expected 5 — an unescaped pipe inside a cell splits it; escape it`
      : "lost its trailing pipe — the table renders broken from here down";
    console.error(`  line ${r.n}: ${why}`);
    console.error(`    ${r.line.slice(0, 100)}…`);
  }
  process.exitCode = 1;
} else {
  console.log(`BOARD SHAPE OK: ${rows.length} rows in ${path}, all with 5 structural pipes.`);
}
