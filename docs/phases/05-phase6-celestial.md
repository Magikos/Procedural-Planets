# Phase 6: Celestial System — Sun, Orbit, Day/Night, Sky
*Goal: Full day/night cycle with dynamic sky*

## 6.1 — Sun
- [ ] Create `CelestialBody` base class with position, radius, light settings
- [ ] Create `Sun` subclass: directional light, emissive visual (billboard with glow shader)
- [ ] Position sun at fixed distance from planet (faked orbit — sun "moves" around planet)
- [ ] Configure URP directional light intensity and color based on sun angle

## 6.2 — Planet Day/Night Rotation
- [ ] Add `RotationSpeed` and `AxialTilt` settings to Planet
- [ ] Rotate sun position around planet (faked — equivalent to planet rotating)
- [ ] Day length configurable in real-time minutes (e.g., 20 min day / 10 min night)
- [ ] Expose `TimeOfDay` (0–1) property for other systems to query

## 6.3 — Dynamic Sky System
- [ ] Procedural skybox shader: gradient based on sun angle (blue day → orange sunset → dark night)
- [ ] Star field visible at night (noise-based or texture-based star pattern)
- [ ] Sun disc visible in sky, tracks with directional light
- [ ] Sunrise/sunset color transitions
- [ ] Ambient light: warm during day, cool blue at night, orange at dawn/dusk

## 6.4 — Atmosphere
- [ ] Atmospheric scattering shader (Rayleigh + Mie approximation)
- [ ] Visible from surface (sky color) and from height (thin rim around planet)
- [ ] Color shifts at sunrise/sunset angles
- [ ] Configurable: thickness, color, density falloff
