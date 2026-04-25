# Phase 10: Character Controller
*Goal: 3rd person character on spherical planet with gravity*

## 10.1 — Spherical Gravity
- [ ] `SphericalGravity` component: always pulls toward planet center
- [ ] Gravity strength configurable (surface gravity based on planet radius)
- [ ] Character aligns "up" to surface normal (away from planet center)
- [ ] Smooth rotation alignment when moving across curved surface

## 10.2 — Character Movement
- [ ] 3rd person controller: WASD movement relative to camera, jump, sprint
- [ ] Movement on sphere surface: translate along surface tangent plane
- [ ] Collision with marching cubes terrain (mesh colliders on chunks)
- [ ] Slope limits: can't walk up steep terrain
- [ ] Ground detection via raycast toward planet center
- [ ] Stamina system for sprint/jump (ties into survival mechanics later)

## 10.3 — 3rd Person Camera
- [ ] Camera follows behind character, orbits with mouse
- [ ] Camera collision with terrain (don't clip through ground)
- [ ] Smooth transitions when terrain changes
- [ ] Look up to see sky/moons/stars, look down to see ground
- [ ] Zoom in/out

## 10.4 — Flight Mode (Later)
- [ ] Toggle flight: character lifts off surface
- [ ] Flight controls: ascend/descend + directional movement
- [ ] Height limit: atmosphere ceiling
- [ ] Transition between grounded and flight gravity modes
