---
name: project-cloud-weather-arc
description: 2026-07-14 cloud/weather visual arc state — what shipped, what's parked, key gotchas
metadata:
  type: project
---

Cloud + weather visual arc, worked through 2026-07-14 on branch `code-refactor`. Bryan
parked clouds ("still need visual work, but tired of clouds") after this landed. Functional,
not final.

**Shipped this session:**
- Cloud-type vertical profiles: `Assets/Graphics/Shaders/Includes/CloudDensity.hlsl`
  (`CloudVerticalProfile`) — stratus (flat)/cumulus (billow)/cumulonimbus (towering).
  Shared include called by both `Cloud.shader` (SampleCloud) and `CloudShadows.hlsl`
  (shadow proxy) so sky+ground can't drift (audit D2). Storm channel drives cumulonimbus.
- Cloud type's stratus↔cumulus axis is driven by **climate temperature** (`SampleClimate01`
  from `ClimateSampling.hlsl`), NOT moisture — because moisture-source ≡ condensation in the
  seed (RMS diff 0.01), so keying type on moisture made every visible cloud read as cumulus.
  Warm→cumulus, cold→stratus, `smoothstep(0.2,0.6,temp01)`. Type varies by latitude.
- `InitialCoverage` baked 0.48 → **0.30** (CloudSettings.cs + .asset). Gives 87% clear /
  13% cloudy / 7% storm — Earth-like. Was near-total overcast + planet-wide storms at 0.48.
- Rain-shaft / virga: below-cloud-base march accumulation in `Cloud.shader`
  (`_CloudRainShaftParams`), `cloud.rain-shaft` / `cloud.rain-shaft-length` knobs.
  **Default off** (perf/bake TBD).
- Sun aureole (low-sun fireball bloom) in `Atmosphere.shader`, baked strength 2 / size 200,
  gated by a planet-hit test (fixed "sun bleeds through planet from space").
- Sun mid-elevation glow: the "sun too big mid-morning" is the atmosphere Mie/Rayleigh
  in-scatter saturating; a `mie-mid-damp` attempt was tried and **reverted** (orange ring
  artifact). Left as-is per Bryan.

**New weather console commands (WeatherManager):** `storm-threshold`, `coverage`,
`regenerate`, `test-pattern`. Plus `weather.export-grid` (pre-existing) dumps all 6 channels
to `%USERPROFILE%/AppData/LocalLow/Magikorp/ProceduralPlanets/weather-grid-*/cells.csv`.

**Gotchas:**
- `weather.force` and `weather.test-pattern` OVERWRITE the stationary moisture-source (b)
  channel with uniform/patterned values AND freeze evolution. After either, the planet stays
  uniform until `weather.regenerate` reseeds the source. This burned a lot of debugging time.
- Storms form where `condensation > StormThreshold(0.86)` AND `source > 0.76`; condensation
  only climbs toward `source`. So storm frequency is really controlled by coverage/source
  level, not just the threshold.
- `SkyDepthMask` (Atmosphere.shader) misclassifies the planet as sky from orbit (reversed-Z
  depth below its threshold) — that's why the aureole needed an explicit planet-hit gate.

**Still-wanted polish (Bryan's words, deferred):** clouds "still need some visual work" —
not specified. Rain visuals pass was the planned next weather item (rain-shaft bake,
precipitation particles vs real storms). See [[project-current-focus]].
