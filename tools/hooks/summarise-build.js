#!/usr/bin/env node
// Digest a build/install/test log down to what actually decides the next action.
//   errors    -> every one, in full (capped)
//   warnings  -> collapsed to "CODE xN" plus one example each; never listed line by line
//   summary   -> succeeded/failed, counts, elapsed, test totals
// Falls back to the tail if nothing matched, so a log in an unexpected format is never swallowed.
//
// argv: <logPath> <exitCode> <marker>

const fs = require('fs');
const [, , log, rc = '?', MARK = '__CCQUIET__'] = process.argv;

const MAX_LINE = 220;
const MAX_ERRORS = 40;
const cut = s => (s.length > MAX_LINE ? s.slice(0, MAX_LINE) + ' …' : s);

let lines = [];
try { lines = fs.readFileSync(log, 'utf8').split(/\r?\n/); } catch { }
const total = lines.length;

const isWarn = l => /\bwarning\s+([A-Za-z]+\d+)/i.test(l);
// "0 Error(s)" / "23 Warning(s)" are summary tallies, not errors. Without this they
// land in the error list and a clean build reads as a failing one.
const isTally = l => /^\s*\d+\s+(Error|Warning)\(s\)/i.test(l.trim());
const isErr = l => /(\berror\b|\bException\b|Build FAILED|\bFailed!|\bFAILED\b)/i.test(l) && !isWarn(l) && !isTally(l);
const isSummary = l => /(Build succeeded|Build FAILED|\d+\s+Warning\(s\)|\d+\s+Error\(s\)|Time Elapsed|Passed!|Failed!|Total tests|added \d+ packages|Successfully installed)/i.test(l);

const errors = lines.filter(isErr);
const summary = lines.filter(isSummary);

// Collapse warnings by their code.
const warn = new Map();
for (const l of lines) {
  const m = l.match(/\bwarning\s+([A-Za-z]+\d+)/i);
  if (!m) continue;
  const k = m[1].toUpperCase();
  if (!warn.has(k)) warn.set(k, { n: 0, eg: l.trim() });
  warn.get(k).n++;
}

const out = [];
if (errors.length) {
  out.push(`--- errors (${errors.length}) ---`);
  errors.slice(0, MAX_ERRORS).forEach(l => out.push(cut(l.trim())));
  if (errors.length > MAX_ERRORS) out.push(`… ${errors.length - MAX_ERRORS} more errors in the log`);
}
if (warn.size) {
  const tally = [...warn.entries()].sort((a, b) => b[1].n - a[1].n);
  out.push(`--- warnings: ${tally.map(([k, v]) => k + ' x' + v.n).join(', ')} ---`);
  tally.slice(0, 5).forEach(([k, v]) => out.push(cut('  eg ' + v.eg)));
}
if (summary.length) {
  out.push('--- summary ---');
  summary.forEach(l => out.push(cut(l.trim())));
}
if (!out.length) {
  out.push('--- no errors/warnings/summary matched; last 15 lines ---');
  lines.filter(l => l.trim()).slice(-15).forEach(l => out.push(cut(l)));
}
out.push(`[${MARK} ${total} log lines -> ${out.length + 1} shown, exit=${rc}. Full log kept at ${log}]`);
process.stdout.write(out.join('\n') + '\n');
