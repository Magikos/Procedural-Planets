# Unity MCP package maintenance

- Local patch PP-MCP-001 replaces screenshot capture's recursive `EditorApplication.Step()` loop with asynchronous capture.
  Its durable patch, checked reapply script, and update validation procedure live in
  [the patch runbook](../tools/patches/unity-mcp-playerloop/README.md).
- Package Manager can discard the installed cache edit. After updating MCP, inspect upstream and reproduce before reapplying.
  If upstream fixes the defect, retire the patch. Different patch context requires review; never force it.
- As of 2026-09-05, the patch is loaded and live running, overlay, paused, explicit-camera, and multiview captures pass.
  Interruption and watchdog checks pass without retained helpers. The runbook records test limits and the paused-cleanup correction.
