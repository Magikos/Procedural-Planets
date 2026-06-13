# World Lifecycle and Settings Scope

**Date:** 2026-06-13
**Status:** Slices 1-3 implemented; slice 4 runtime boundary implemented, persistence adapter pending
**Branch:** code-refactor

## Goal

Make startup, saved-world loading, and world replacement transactional. A world is either fully initialized or unavailable; the game never continues with a partially initialized dependency chain.

## Locked Decisions

- Initialization is fail-fast. Any early or late initializer failure fails the entire world.
- Application services and world services have different lifetimes.
- `GameBootstrap` and application services initialize once and persist.
- Seed, settings, planet, weather, precipitation, grass, and world actions belong to a replaceable world scope.
- Settings are keyed by DTO type inside one `ISettingsService` per world.
- Runtime settings do not use arbitrary string keys.
- Stable string identifiers and schema versions are used only at the save-file boundary.
- Duplicate DTO registration is an error.
- Registration is frozen after world construction; runtime changes use `Update<TDto>`.
- Every required subsystem and DTO is validated before the world is marked ready.
- A failed transition leaves the loading overlay active and does not resume gameplay.

## Target Ownership

### Application Scope

- `ILogger`
- `ILoadingManager`
- input
- quality settings
- debug console and capture orchestration

### World Scope

- `ISeedProvider`
- `ISettingsService`
- `IWorldActionManager`
- planet and surface services
- weather, cloud, atmosphere, and precipitation services
- grass runtime services

A world scope is not the same thing as a Unity scene. A world may eventually use multiple additive scenes, while replacing a saved game replaces the entire world scope.

## Target Transition

1. Fade out, pause simulation, and preload the requested scene set.
2. Cancel the active world lifetime token, disable the old scene roots, and tear down old world resources.
3. Deactivate the old context and activate a fresh context before Unity activates the new scene.
4. Register and validate required services during scene activation and early initialization.
5. Run early and late initialization.
6. Unload the old world scenes and dispose the old context.
7. Raise `WorldReadyEvent` and fade in.

If any step fails after the old world is released, tear down the partial new world and remain on a fatal loading screen.

## Implementation Slices

### Slice 1: Fail-Fast Initialization

**Status:** Implemented.

- Stop the pass on the first initializer exception.
- Preserve phase and service context in the thrown exception.
- Remove progress subscriptions when initialization aborts.
- Do not rerun persistent application initializers during world transitions.
- Unload a newly loaded scene when its initialization fails.
- Keep fatal initialization failures behind the loading overlay.

### Slice 2: Application and World Scopes

**Status:** Implemented.

- Introduce an explicit world context with a lifetime cancellation token.
- Move world service registration out of individual `Awake` methods into the scene bootstrap registrar pass.
- Make the world context own registration and cancellation.
- Track world nodes as they enter initialization and tear them down once in reverse initialization order, including the node that failed partway through.
- Remove world services from the application service container.
- Replace global object searches in bootstrap code with scope-owned references.
- Refresh persistent application tooling through `WorldReadyEvent`.

### Slice 3: World-Scoped Settings

**Status:** Implemented.

- Construct one `SettingsService` per world.
- Remove `SettingsProvider` lazy fallback creation.
- Resolve settings through the active world context.
- Make duplicate registration throw. Implemented.
- Add registration freeze and required-DTO validation. Implemented.

### Slice 4: Saved-World Load Request

`WorldLoadRequest` now carries the scene, optional world seed, save identity, settings schema version, and typed DTO overrides. Overrides are applied after authoring defaults are registered and before settings validation and freeze. Stable save-key deserialization and migration remain the responsibility of the future persistence adapter; runtime consumers continue to call `GetSettings<TDto>()`.

## Validation

- Cold startup succeeds.
- A deliberate early initializer failure prevents every later initializer.
- A deliberate late initializer failure prevents world readiness.
- Failed initialization removes progress listeners.
- Persistent application services initialize once across repeated transitions.
- Same-scene reload creates a fresh world.
- Loading different seeds produces independent settings and generation state.
- Missing required services or settings fail before gameplay resumes.
