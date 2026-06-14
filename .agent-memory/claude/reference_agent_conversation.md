---
name: agent-conversation
description: "Cross-agent scratchpad in docs/agent-conversation/ — append review feedback here when other agents work in parallel on the same phase"
metadata:
  type: reference
---

`docs/agent-conversation/` is a shared scratchpad for cross-agent collaboration. When Bryan has multiple agents working on the same phase in parallel (different Claude sessions, different models, etc.), they leave structured review feedback / observations / questions here so the next agent has full context.

**File convention:** `YYYY-MM-DD-<phase>.md` (one file per phase). Append new entries to the bottom under `## YYYY-MM-DD — <author> — <topic>`. Don't rewrite earlier entries — preserve the conversation. End each entry with a clear "what I'm asking the next agent to do" line if action is needed.

**When to use:** when reviewing another agent's implementation, post the assessment here in addition to chat. Before starting a new chunk of work, scan for the most recent entry in the relevant phase file.

**When NOT to use:** transient debugging notes (chat with Bryan), design proposals (`docs/design/`), trivial single-turn nits.

Established 2026-06-01 alongside Phase C grass renderer; see [README](../../docs/agent-conversation/README.md) for full convention. Related: [[project-current-focus]], [[grass-chunks-research]], [[feedback-audit-workflow]].
