# Shared Agent Memory

This directory is the committed memory source for agents working on
ProceduralPlanets.

## Layout

- `MEMORY.md`: concise cross-agent index and conflict-resolution rules.
- `claude/`: Claude Code's native auto-memory directory.
- `codex/`: imported Codex memory snapshot and detailed historical notes.

## Agent routing

Claude Code uses `.agent-memory/claude` through
`.claude/settings.json` and imports the shared `MEMORY.md` from `CLAUDE.md`.

Codex reads the shared `MEMORY.md` through `AGENTS.md`. Project
`.codex/config.toml` disables the separate user-level memory cache for this
repository so the committed memory remains authoritative. Codex does not offer
a memory-only path override; moving `CODEX_HOME` would also move credentials,
sessions, logs, and other machine-local state into the repository.

## Maintenance

- Put stable cross-agent facts in `MEMORY.md` or a linked topic file.
- Keep agent-specific generated detail under `claude/` or `codex/`.
- Date checkout-specific state and revalidate it before acting.
- Explicit user instructions and current code or documentation override memory.
- Review memory diffs before committing. Never store secrets or sensitive
  captures here.

The initial import was created on 2026-06-14 from Bryan's existing Claude Code
project memory and Codex user memory for this repository.
