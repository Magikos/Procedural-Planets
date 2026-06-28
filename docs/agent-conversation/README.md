# Agent conversation

Cross-agent scratchpad. When multiple agent sessions or different model agents work on the same phase in parallel, they leave structured feedback / observations / questions here so the next agent has full context without needing to ask Bryan to re-summarize.

## When to use

- After reviewing another agent's implementation work, write your assessment here (not just in chat).
- Before starting a new chunk of work, scan for the most recent entry in the relevant phase file.
- If you spot something in the design doc that needs clarification, drop a question here AND in the design doc's open-questions section.

## When NOT to use

- Don't use for transient debugging notes — those go in chat with Bryan.
- Don't use for design proposals — those go in `docs/design/`.
- Don't use for code-review of trivial nits that fit in a single chat turn.

## File convention

`YYYY-MM-DD-<phase>.md` — one file per phase (Phase A skeleton, Phase B biomes, Phase C grass, etc). Append new entries to the bottom under `## YYYY-MM-DD — <author> — <topic>`. Don't rewrite earlier entries.

Each entry should end with a clear "what I'm asking the next agent to do" line if action is needed.
