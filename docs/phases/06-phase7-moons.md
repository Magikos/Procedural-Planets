# Phase 7: Moon System
*Goal: Procedural moon(s) with phases and reflected light*

## 7.1 — Moon Generation
- [ ] `MoonGenerator` reuses Planet pipeline at lower resolution (subset: mesh + simple noise, no water/biomes/spawning)
- [ ] Configurable: number of moons (1–3), size range, noise settings
- [ ] Each moon seed derived from planet seed (`planetSeed + moonIndex * 1000`)
- [ ] Simple gray/brown rocky surface material

## 7.2 — Moon Orbit
- [ ] Each moon gets `OrbitalBody` component: orbital radius, speed, inclination
- [ ] Simple elliptical orbit (configurable eccentricity, default near-circular)
- [ ] Moons orbit around planet; since planet orbit is faked, moons just orbit world origin
- [ ] Different orbital periods per moon for visual variety

## 7.3 — Moon Phases & Moonlight
- [ ] Calculate phase from sun → moon → planet angle
- [ ] Moon shader: lit hemisphere faces sun direction (same principle as day/night)
- [ ] From planet surface: crescent/half/gibbous/full based on orbital position
- [ ] Secondary directional light per moon aimed at planet
- [ ] Light intensity = base moonlight × phase factor (full = max, new = 0)
- [ ] Light color: cool white/blue tint
- [ ] Multiple moons contribute additive moonlight
