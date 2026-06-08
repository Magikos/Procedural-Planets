# 2026-06-07 — Grass interactors (slice scope)

**Status:** Approved for implementation 2026-06-07.
**Builds on:** Existing `GrassInteractors.hlsl` stub + `SampleGrassInteractorBend` hooks in `Grass.shader` and `GrassMidField.shader` (left by Codex for slice 6).
**Applies:** [[feedback-settings-dto-pattern]] — consumer shaders read a packed GPU buffer; the C# side has DTOs between MonoBehaviours and the GPU.

## What ships

A working "objects in the world push grass aside" system, demonstrated with a debug sphere that follows the freecam at a fixed offset and snaps to the planet surface. Validates the system end-to-end without needing a character controller, spherical gravity, or 3rd-person camera (those are a separate future arc).

When the character system eventually ships, the player implements `IGrassInteractor` — same shader path, same registry, no system changes.

## Runtime architecture

```
IGrassInteractor (interface)
   │
   ├── DebugGrassInteractor (MonoBehaviour, the sphere)
   ├── (future) PlayerCharacter
   ├── (future) Projectile, Animal, MagicAOE...
   │
   ▼ self-register on enable
GrassInteractorRegistry (static)
   ├── Holds list of active interactors
   ├── Per LateUpdate: pack top-N into ComputeBuffer
   └── SetGlobalBuffer + SetGlobalInt to shader

Bootstrap MonoBehaviour
   └── LateUpdate → registry.UploadPerFrame()

Shader (Grass.shader + GrassMidField.shader)
   └── SampleGrassInteractorBend(rootWS, upWS) reads _GrassInteractors[N]
       → for each: bend vector projected to tangent plane, falloff by distance
```

Cap at **8 interactors**. Plenty for a player + a couple animals; beyond that we'd need spatial partitioning anyway.

## DTOs

```csharp
// Runtime contract: what the registry packs for the GPU. Consumers don't
// reach into IGrassInteractor; they receive these snapshots.
public readonly struct GrassInteractorSnapshot
{
    public readonly Vector3 WorldPosition;
    public readonly float Radius;
    public readonly float Strength;

    public GrassInteractorSnapshot(Vector3 worldPosition, float radius, float strength) { ... }

    public static GrassInteractorSnapshot From(IGrassInteractor source) => new(
        source.WorldPosition, source.Radius, source.Strength);
}

// GPU layout — matches `struct GrassInteractor` in HLSL exactly.
struct GrassInteractorGpu
{
    public Vector4 PositionRadius;  // xyz, w=radius
    public Vector4 StrengthType;    // x=strength, y=type(0=transient), z/w reserved
}
```

The GPU struct already matches Codex's HLSL definition — no shader rewrite needed beyond filling the stub.

## Shader implementation (replacing stub)

```hlsl
float3 SampleGrassInteractorBend(float3 rootWS, float3 upWS)
{
    float3 bend = float3(0, 0, 0);
    for (int i = 0; i < _GrassInteractorCount; i++)
    {
        float4 posRadius = _GrassInteractors[i].PositionRadius;
        float3 toRoot = rootWS - posRadius.xyz;
        float dist = length(toRoot);
        float radius = posRadius.w;
        if (dist >= radius || dist < 0.0001) continue;

        float falloff = 1.0 - smoothstep(0.0, radius, dist);
        float3 dir = toRoot / dist;
        // Project to tangent plane so blades bend sideways, not vertically.
        dir = dir - upWS * dot(dir, upWS);
        float dirLen = length(dir);
        if (dirLen < 0.0001) continue;

        float strength = _GrassInteractors[i].StrengthType.x;
        bend += (dir / dirLen) * falloff * strength * radius;
    }
    return bend;
}
```

Bend direction points AWAY from the interactor, projected to tangent plane, falloff smoothstepped by distance, magnitude scales with radius (bigger interactor → bigger push). Springback is **automatic** — once the interactor moves away, the bend goes to zero next frame without per-blade state.

Existing shader call sites (Grass.shader line 198, GrassMidField.shader line 85) get `upWS` added — both shaders already have it in scope.

## Console commands (under `grass.interactor-*`)

```
grass.interactor-spawn               # creates the debug sphere + enables camera-follow
grass.interactor-despawn             # removes
grass.interactor-radius [float]      # get/set radius (default 4m)
grass.interactor-strength [float]    # get/set strength (default 1)
grass.interactor-follow [bool]       # toggle camera-follow mode (sphere stops following but stays)
grass.interactor-status              # show count, positions, params
```

When camera-follow is OFF, the sphere stays where it was — useful for "drop a sphere here, fly around and look at the result."

## Camera-follow + surface snap

```csharp
// Each LateUpdate when follow enabled:
Vector3 target = camera.transform.position + camera.transform.forward * forwardDistance;  // default 5m
Vector3 fromCenter = target - planet.transform.position;
Vector3 dir = fromCenter.normalized;
float radius = planet.SeaLevelRadius + 0.5f;  // sit 0.5m above sea level by default
sphere.position = planet.transform.position + dir * radius;
```

Snaps to sea-level surface. Won't perfectly follow terrain elevation in mountains (the sphere may sink into hills or float above valleys), but for first-pass debugging the visual effect is clear: fly along, watch grass push aside. Terrain-accurate snap is a later polish if needed.

## Files

**New:**
- `Assets/Scripts/Core/Interfaces/IGrassInteractor.cs`
- `Assets/Scripts/Planet/Grass/GrassInteractorRegistry.cs` (static)
- `Assets/Scripts/Planet/Grass/GrassInteractorDtos.cs`
- `Assets/Scripts/Planet/Grass/DebugGrassInteractor.cs` (MonoBehaviour)
- `Assets/Scripts/Planet/Grass/GrassInteractorBootstrap.cs` (MonoBehaviour, per-frame upload + cleanup)
- `Assets/Scripts/Planet/Grass/GrassInteractorCommands.cs` (console commands)

**Modified:**
- `Assets/Graphics/Shaders/Includes/GrassInteractors.hlsl` — replace stub body
- `Assets/Graphics/Shaders/Grass.shader` — add upWS to call
- `Assets/Graphics/Shaders/GrassMidField.shader` — add upWS to call
- `ProceduralPlanets.Planet.csproj` — Compile entries

**Estimated:** ~250 lines across 6 new files + 3 modified.

## Validation guidance

1. `grass.interactor-spawn` — sphere appears in front of camera, follows as you fly.
2. Fly to a grass field, low altitude — blades should visibly part under the sphere.
3. Stop moving — blades stay parted as long as sphere is over them.
4. Fly away — blades spring back (automatic, no per-blade state).
5. `grass.interactor-radius 8` — bend zone widens; `grass.interactor-strength 0.3` — bend amount softens.
6. `grass.interactor-follow false` — sphere stops moving; fly around, the parted patch stays in place.

## Future consumers (out of scope this slice)

- Player character (when the character/gravity arc ships)
- Projectiles, animals, NPCs
- Magic AOE effects (radius scales with spell area)
- Vehicles (wheels as multiple interactors)
- All implement `IGrassInteractor`; zero shader changes required.

## Out of scope

- Spherical gravity, character controller, 3rd-person camera (separate big arc)
- Persistent path cutting (`StrengthType.y == 1`) — slice 7 in Codex's note
- Spatial partitioning for >8 interactors (overkill until needed)
- Terrain-accurate sphere height (sphere uses sea-level snap; precise terrain follow is later polish)
