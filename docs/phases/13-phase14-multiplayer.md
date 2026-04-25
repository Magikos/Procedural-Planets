# Phase 14: Multiplayer Foundation
*Goal: Co-op multiplayer with Mirror, host/client + dedicated server*

## 14.1 — Mirror Integration
- [ ] Add Mirror package to project
- [ ] Set up NetworkManager, transport layer
- [ ] Player spawning and connection handling
- [ ] Host/client mode and dedicated server mode

## 14.2 — World State Synchronization
- [ ] Convert `IWorldAction` commands to network RPCs
- [ ] Server authoritative: all terrain/building modifications validated by server
- [ ] Chunk data streaming: server sends chunk deltas to connecting clients (async streaming via Awaitable)
- [ ] Client prediction for responsive feel, server reconciliation

## 14.3 — Player Synchronization
- [ ] Sync player position, rotation, animation state
- [ ] Sync building placement (ghost preview local, placement server-authoritative)
- [ ] Sync terrain deformation
- [ ] Sync inventory and crafting
- [ ] Interest management: only sync nearby players and chunks
