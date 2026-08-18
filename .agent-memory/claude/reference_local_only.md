---
name: reference-local-only
description: "The local-only/ folder holds external reference projects and papers that are the source/example material for this project's key features"
metadata:
  node_type: memory
  type: reference
  originSessionId: 97829702-a6c8-47a8-a3db-f18c9ac1f8af
  modified: 2026-08-16T16:51:02.915Z
---

`local-only/` (excluded from build & not this project's code) is the reference library behind the major features. Consult the matching reference before reinventing a system:

- **Procedural terrain:** `Procedural Planet E01`–`E07`, `Procedural Planet Noise` (Sebastian Lague series — the foundation).
- **Atmosphere:** `URP-Atmosphere-main`, `Geographical-Adventures-main`, `atmospheric_scattering_shader_unity_guide.md`.
- **Clouds:** `Clouds-master`, `cloud_rendering_unity_guide.md`.
- **Water/ocean:** `FFT-Ocean-main`, `Fluid-Planet-main`, `GDWaterKart-main`, plus PDFs/guides (`...HowToBuildAWaterShader_80Level.pdf`, `effective_water_simulation...`, `looking_through_water...`, `ocean_wave_foam_halftoning...`, `rendering_water_caustics...`, `fastcaustics.pdf`, `ocean water.pdf`, `waves.pdf`).
- **Planet LOD:** `LOD-Planets-in-Unity-master` (Phase 13 reference).
- **Celestial:** `Solar-System-Development`.
- **SDF/MSDF text** (`Core/Text/*`): `SIGGRAPH2007_AlphaTestedMagnification.pdf`, `publications-2018-sloup-cgf-msdf-paper.pdf`.
- **EventBus:** `EventBus/` — library the project's event system was adapted from.
- **Art direction:** `desired overall look.png`. **Debug captures:** `debug-screenshots/` (F10 water sets).

These are third-party (varying licenses); keep them in `local-only/`, out of the build, and exempt from this project's coding conventions.

## ⚠️ The `*_unity_guide.md` files are NOT papers (verified 2026-08-16)

The seven `*_unity_guide.md` files are **AI-written derivative summaries generated for this project**, not the sources they name — each carries a bespoke "spherical planet adaptation" section no real paper would have. **Never cite them as literature.** Three materially misrepresent their source:

- `ati_real_time_synthesis_rendering_ocean_water_unity_guide.md` — contains none of Mitchell's actual FFT/mip-LOD content.
- `rendering_water_caustics_unity_guide.md` — describes scrolling textures; GPU Gems 1 ch.2 is actually an **area-ratio** method. Its own shipped `ComputeCaustics()` contradicts its own advice.
- `foam_splash_rippling_spectrum_ocean_unity_guide.md` — credits "Brian T. Tessendorf" and **omits the Jacobian**, the one formula the subject rests on.
- `effective_water_simulation_physical_models_unity_guide.md` — drops GPU Gems 1's steepness normalisation `Q_i = Q/(w_i·A_i·N)` (the load-bearing part of Gerstner) and replaces the analytic normal with a non-functional `cross()`.

Faithful and substantive: `looking_through_water_unity_guide.md` (Catlike Coding) and `realtime_caustics_webgl_unity_guide.md` (Evan Wallace). **The PDFs are genuine** — `ocean water.pdf` (Tessendorf) is the citation of record for wave generation. See [[project-water-tech-research]].
