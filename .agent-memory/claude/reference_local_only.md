---
name: reference-local-only
description: "The local-only/ folder holds external reference projects and papers that are the source/example material for this project's key features"
metadata:
  node_type: memory
  type: reference
  originSessionId: 97829702-a6c8-47a8-a3db-f18c9ac1f8af
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
