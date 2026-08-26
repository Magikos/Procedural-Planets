#!/usr/bin/env node
// PreToolUse(Bash) filter: rewrite noisy commands so only the useful lines come back.
//
// Two classes are rewritten, everything else is passed through untouched:
//   A) build / install / test runners -> errors, warnings, summary, tail. Exit code preserved.
//   B) whole-file `cat` over a line threshold -> head + a LOUD truncation marker.
//
// Rules this obeys:
//   - Never silently truncate. Every rewrite prints what it dropped and how to get the rest.
//   - Preserve the real exit code. A filtered build must still fail when the build failed.
//   - Bail out on anything ambiguous (heredocs, existing filters, redirects, subshells).
//   - node only. python3 on this machine is the Windows Store stub and cannot execute.

const path = require("path");

const MARK = '__CCQUIET__';           // idempotence guard: never rewrite our own output
const CAT_LINE_LIMIT = 400;           // cat below this is left completely alone

const NOISY = /(^|[\s;&|(])(dotnet\s+(build|test|restore|publish)|msbuild|npm\s+(i|install|ci|run\s+build|test)|pnpm\s+(install|build|test)|yarn\s+(install|build|test)|pip\s+install|cargo\s+(build|test)|gradle|mvn)\b/;


// Guards run against a copy with quoted spans blanked out, so an echoed ">>>" or "$(" in
// a message string does not look like a redirect or a substitution. Rewrites use the original.
function scrub(cmd) {
  return cmd.replace(/"(\\.|[^"\\])*"/g, '""').replace(/'[^']*'/g, "''");
}

function unsafe(cmd) {
  return cmd.includes(MARK)          // already rewritten
    || cmd.includes('<<')            // heredoc: rewriting would corrupt the body
    || /\|\s*(grep|head|tail|sed|awk|rg|Select-String)\b/.test(cmd)  // already filtered by hand
    || />\s*\S/.test(cmd)            // redirects to a file: caller wants the full stream
    || cmd.includes('$(')            // command substitution: value may be consumed downstream
    || cmd.includes('`');
}

// A) Build / install / test -> digest via summarise-build.js, keep the exit code.
function filterBuild(cmd) {
  const t = `/tmp/ccq_$$_${MARK}`;
  const sum = path.join(__dirname, 'summarise-build.js').replace(/\\/g, '/');
  // Subshell, not braces: a wrapped command that calls `exit` must not kill the reporting below.
  return `( ${cmd} ) >${t} 2>&1; __rc=$?; ` +
    `node "${sum}" ${t} $__rc ${MARK}; ` +
    `exit $__rc`;
}

// B) Whole-file cat over the threshold -> head + explicit marker naming the real tool to use.
function filterCat(cmd) {
  const m = cmd.match(/^((?:cd\s+(?:"[^"]*"|'[^']*'|\S+)\s*&&\s*)?)cat\s+(-\S+\s+)*("([^"]+)"|'([^']+)'|([^\s|;&<>]+))\s*$/);
  if (!m) return null;
  const prefix = m[1] || '';
  const file = m[4] || m[5] || m[6];
  if (!file || file.startsWith('-')) return null;
  const q = `"${file}"`;
  return `${prefix}{ __n=$(wc -l < ${q} 2>/dev/null || echo 0); ` +
    `if [ "$__n" -gt ${CAT_LINE_LIMIT} ]; then ` +
    `head -${CAT_LINE_LIMIT} ${q}; ` +
    `echo "[${MARK} TRUNCATED: showed ${CAT_LINE_LIMIT} of $__n lines of ${file}. ` +
    `This is NOT the whole file - do not edit or summarise it from this alone. ` +
    `Use the Read tool with offset/limit, or sed -n 'A,Bp', for the rest.]"; ` +
    `else cat ${q}; fi; }`;
}

let raw = '';
process.stdin.on('data', d => raw += d);
process.stdin.on('end', () => {
  let cmd = '';
  try { const j = JSON.parse(raw); cmd = (j.tool_input || j).command || ''; } catch { }
  const probe = scrub(cmd);
  if (!cmd || unsafe(probe)) return process.exit(0);

  let out = null;
  if (NOISY.test(probe)) out = filterBuild(cmd);
  else if (/(^|&&\s*)cat\s/.test(probe)) out = filterCat(cmd);
  if (!out) return process.exit(0);

  process.stdout.write(JSON.stringify({
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      updatedInput: { command: out },
    },
  }));
  process.exit(0);
});
