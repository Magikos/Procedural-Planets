## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, invoke the `skill` tool with `skill: "graphify"` before doing anything else.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).

## Shared project memory

The committed cross-agent memory root is `.agent-memory/`.

Rules:
- Before non-trivial project work, read `.agent-memory/MEMORY.md`.
- Search `.agent-memory/claude/MEMORY.md` and `.agent-memory/codex/memory_summary.md` when the shared index points to relevant historical detail.
- Treat explicit user instructions and current code or documentation as newer than memory. Revalidate stale or checkout-specific claims before acting on them.
- When Bryan explicitly asks Codex to remember or forget project information, update `.agent-memory/` rather than the user-level Codex memory store.
- Never add credentials, access tokens, private keys, or sensitive captures to committed memory.
