---
name: feedback-audit-workflow
description: "How Bryan wants code audits produced, consolidated, and reviewed before any fixes are made"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 97829702-a6c8-47a8-a3db-f18c9ac1f8af
---

Audits live in `docs/audit/{date}.md`. When asked for a new audit: create a fresh dated file, then **consolidate it with the previous audit** — re-validate every prior finding against the *current* code, state whether it's resolved/partial/open, and say whether you agree with it (don't just restate it). Add new findings you discover. Then produce an **improvements document** (separate or appended) with forward-looking ideas to better the project.

**Why:** Bryan iterates on this project over long arcs; findings get fixed between audits, so a new audit that ignores prior state would be noise. He explicitly wants your independent agreement/disagreement, not a rubber-stamp.

**How to apply:** Audit → consolidate/validate → improvements doc → **stop and let Bryan review and give feedback** → only then work on fixing/implementing. Do not start fixing findings until he reviews. See [[project-current-focus]].
