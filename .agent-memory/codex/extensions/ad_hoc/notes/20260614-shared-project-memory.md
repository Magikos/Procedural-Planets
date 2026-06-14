# ProceduralPlanets shared project memory

Bryan explicitly requested that Claude Code and Codex use memory committed with
the ProceduralPlanets repository.

- Canonical shared index: `.agent-memory/MEMORY.md`.
- Claude native auto-memory: `.agent-memory/claude`, configured by the tracked
  `.claude/settings.json`.
- Codex reads and updates the shared tree through `AGENTS.md`.
- Project `.codex/config.toml` disables Codex's separate user-level memory use
  and generation for this repository to prevent two divergent sources.
- Existing Claude and Codex memories were imported into `.agent-memory/`.
- Current code, project docs, captures, and explicit user instructions override
  stale memory.
- Memory diffs must be reviewed before commit and must not contain secrets.
