---
name: reference-state-machine-project
description: "External prior-art char controller at C:\\Users\\Bryan\\Source\\Repos\\Magikorp\\State Machine — a WIP skeleton (motor UNFINISHED/empty, all physics-collider based). Harvest = PATTERNS not code. Its generic hierarchical FSM is the PP Phase-10 graduation backbone; harvest documented in plans/001 Appendix."
metadata:
  type: reference
---

Bryan's unfinished character-controller experiment: `C:\Users\Bryan\Source\Repos\Magikorp\State Machine\Assets\Scripts` (~95 .cs files).

**Reality:** WIP skeleton. The MOTOR is unfinished — `CharacterMotor.ApplyMotion` empty stub; `CharacterMotorRefactored`, `PlayerCharacterControllerRefactored`, `SlopeMotorBehavior`, `StepUpMotorBehavior`, `StepDetectionSensor`, `CharacterMotionContext`, all `ICharacter*Context` interfaces are 0-byte. Entirely Unity-physics/collider based (Rigidbody + CapsuleCollider + Physics.Raycast). So: **harvest PATTERNS, not code** — PP has no terrain colliders (analytic `IPlanetSurfaceSampler`).

**The prize = the generic hierarchical FSM** (`AdaptiveStateMachine<TContext>`): context injected per-update (FSM holds no context), Type-cached singleton states, hierarchical composites (`CompositeState`), context-adaptive `ResolveTo(from,ctx)=>Type`, `EvaluateExit` self-exit, `WithNullDefault`/`ErrorState` fallback + `BlockTimeout` watchdog, fluent builder + declarative transition TABLE. **PP fit = LATER (Phase 10 graduation backbone)** when flight then build/dig exist — NOT the MVP (one behavior, zero transitions; folding it in = speculative infra PP forbids). Port as `AdaptiveStateMachine<PpCtx>` with PpCtx a PP-owned **readonly struct** (radial up, sampler results, input DTO, **dt** — never static Time), composite Grounded/Airborne.

**VALIDATES the [[project-character-terrain-plans]] MVP design:** producer/consumer split (states write Intent, motor sole applier) ≡ PP's pure `CharacterMotor.Step` + adapter; and the flat-world traps everywhere (camForward.y=0, LookRotation w/o up, Vector3.up slope, velocity.y) are exactly what PP's `Step(up,…)` + `LookRotation(dir,up)` + tangent projection avoid. MVP fold adopted: **prime-ground-once-at-spawn**.

**SKIP:** their EventBus (dupe), Singleton (PP forbids → WorldContext DI), Logwin (→ ILogger), PlayerInputProvider (dupe input svc), all physics motor/sensor code, empty interfaces, LocomotionSettings-static + CharacterStats-POCO (→ one PP SO→DTO). Rules predicate DSL = optional/lean-skip (only ~6 combinators if transitions grow; prefer lambdas; class-per-predicate is the anti-pattern).

Full catalog (MVP/LATER/SKIP per idea, file:line) in `plans/001-character-controller-mvp.md` Appendix, harvested 2026-08-08 via a 6-agent parallel read.
