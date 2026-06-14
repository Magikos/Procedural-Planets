# ProceduralPlanets grass scale F10 validation - 2026-06-02

Bryan retested the grass scale marker flow in `C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets` after the visible-mesh raycast and offset marker projection fixes.

Latest reviewed captures:

- `local-only/debug-screenshots/F10-water.00-Off-20260601-230026-092`
- `local-only/debug-screenshots/F10-water.00-Off-20260601-230048-077`
- `local-only/debug-screenshots/F10-water.00-Off-20260601-230116-837`

Durable conclusions:

- Scale markers are now validated on the visible terrain surface. Sidecars report `Markers: hasDrop=True, lastSuccess=True, status=mesh-visible-terrain, count=6` and `MarkerProjection: meshHits=5, fallbacks=0`.
- Debug marker shadows are not important for this pass. Production placed assets such as trees, rocks, and other gameplay objects should use real shadow-casting renderers later.
- Grass is visible but very sparse. The close human-reference capture shows blade scale is roughly plausible, but coverage reads as isolated thin strokes.
- F10 counts were about 79-104 visible/tracked grass chunks, 79-104 draw calls, and about 6k emitted instances. FPS was near 59 in two views and 30.1 in the close blade view.

Next-session guidance:

- Do not reopen marker placement unless future sidecars lose `mesh-visible-terrain` or `MarkerProjection: meshHits=5, fallbacks=0`.
- Before density tuning, add grass F10 rejection counters: candidate cells/lanes, density-zero rejects, biome/state-mask rejects, water rejects, slope fade/rejects, distance/cull rejects, random density-roll rejects, emitted instances, and overflow/cap rejects.
- Add a debug density multiplier or force-density mode after counters exist.
- If counters show most candidates are rejected, fix that gate first. If many instances are emitted but the view still reads sparse, improve blade representation with tuft or cross-card clusters rather than only increasing raw instance count.
