---
name: project-scatter-shape-seed
description: Renaming a scatter prototype re-rolls its generated geometry and orphans its baked impostor card unless ShapeSeed is set.
metadata:
  type: project
---

A scatter prototype's `DisplayName` is not a label. The plant, tree and rock injectors seed the
generator from it, so renaming a prototype regrows it as a different plant, tree or rock, and the new
geometry no longer matches its baked impostor atlas.

**Why:** `ImpostorKey => DisplayName` and `ImpostorSourceHash = AppearanceHash(LOD0 meshes, tints)`.
The key survives a rename through the `.asset` GUID, but the hash does not, because the geometry
itself moved. `GeneratedImpostorManifest.TryGet` needs both, so the prop silently loses its far-field
card. This is how the 2026-09-18 vendor-prefix retirement (pass 2e) produced
`[ScatterCheck] 20 impostor prototype(s) have no baked card`.

**How to apply:** Before renaming a generated prototype, set `ShapeSeed` in its `.asset` to the signed
FNV-1a of the OLD name. `ScatterPrototypeDto.ShapeSeedBase(fallback)` returns that value when
`ShapeSeed != 0` and hashes `DisplayName` when it is zero, so the shape stays byte-identical and the
rename needs no capture-diff approval. The 17 renamed prototypes in
`Assets/Resources/Settings/Scatter/` already carry their pinned seeds. Verify with
`Tools > ProceduralPlanets > Impostors > Validate`: every prototype must read a baked card. Never use
`string.GetHashCode` or `HashCode.Combine` to seed world content - .NET randomises string hashing per
process, so each session would grow a different plant from the same world seed.
Related: [[project_all_generated_props]], [[project_scatter_lod_impostor]].
