# Cloud Visual-Quality Research — 2026-07-04

Companion to [2026-07-04-grass-cloud-reference-recommendations.md](2026-07-04-grass-cloud-reference-recommendations.md)
(which covered what's in `local-only/`). This covers the published research our renderer
hasn't absorbed yet, ranked by expected visual payoff for **our** architecture: single-pass
fullscreen march through a spherical shell, weather-sim-driven, no temporal accumulation
(tried, reverted). Each item names the source and the exact integration point in
`Cloud.shader`.

The canonical papers, in the order they matter to us:

- **Schneider & Vos, "The Real-Time Volumetric Cloudscapes of Horizon Zero Dawn"**
  (SIGGRAPH Advances in Real-Time Rendering, 2015) and **Schneider, "Nubis: Authoring
  Real-Time Volumetric Cloudscapes"** (2017). The system every modern game cloud renderer
  descends from — including Lague's project, and therefore ours, but we inherited only the
  skeleton.
- **Högfeldt, "Convincing Cloud Rendering — Real-Time Dynamic Volumetric Clouds in
  Frostbite"** (2016).
- **Hillaire, "Physically Based Sky, Atmosphere and Cloud Rendering in Frostbite"**
  (SIGGRAPH 2016 course). Source of the multi-scattering octave approximation.
- **Wrenninge, Kulla & Lundqvist, "Oz: The Great and Volumetric"** (SIGGRAPH 2013 talk) —
  origin of that multi-scatter trick.

---

## 1. Beer-Powder term — the single biggest missing look feature (Nubis 2015)

Real clouds show **dark creases between bulges on the sun-facing side** ("powdered sugar"
look): light entering a low-density region in-scatters away before it can return to the
eye. Standard Beer-Lambert alone (what we have, `Cloud.shader:228,394`) can't produce it —
transmittance only ever brightens thin regions.

Schneider's approximation, applied where we compute `lightTransmittance`:

```hlsl
float beer   = exp(-lightDensity * _CloudLightAbsorption);
float powder = 1.0 - exp(-lightDensity * 2.0);
float lit    = _CloudDarknessThreshold
             + beer * lerp(1.0, powder, powderStrength * saturate(cosAngle))   // sun-facing only
             * (1.0 - _CloudDarknessThreshold);
```

(`lightDensity` = the accumulated density from `LightMarch` before the exp.) One new
tunable. This is the highest look-per-line change available — it's what makes cumulus read
as *carved* instead of cotton-wool.

## 2. Energy-conserving multi-scatter octaves (Oz 2013 / Frostbite 2016)

Our current multi-scatter is one ad-hoc term (`Cloud.shader:369-370`,
`pow(T,0.25)*(1-T)*0.4`). The literature replacement: evaluate the light loop as **N
octaves** with attenuation `a`, contribution `b`, and phase-eccentricity `c` falling off
per octave (typical `a=b=c=0.5`, N=2-3):

```hlsl
float3 scatter = 0;
for (int o = 0; o < 3; o++)
{
    float att = pow(0.5, o);                       // a^o
    float T_o = exp(-lightDensity * _CloudLightAbsorption * att);
    scatter += pow(0.5, o)                          // b^o (contribution)
             * T_o
             * CloudPhase(cosAngle, phaseG * pow(0.5, o));   // c^o widens phase each octave
}
```

Cost: no extra texture samples (reuses the one marched `lightDensity`), just ALU.
Payoff: thick storm cores stop going flat black while stay dark — this is the principled
version of what the silver-lining/gloom hand-tuning has been chasing. Directly relevant to
the "rain clouds look dead" thread.

## 3. Two-tone ambient gradient (Nubis / Frostbite, both)

Our ambient is a scalar (`_CloudAmbientStrength` × height factor, `Cloud.shader:363-364`).
Real clouds are lit from **above by blue sky** and **below by warm/dark ground bounce**.
Two colors, lerped by the `height01` we already compute:

```hlsl
float3 ambient = lerp(_CloudAmbientGround.rgb, _CloudAmbientSky.rgb, cloud.height01)
               * ambientStrength;
```

Sky tint can come from the atmosphere system's zenith color instead of a constant —
we already share sun state with atmosphere, this closes the loop the Harris guide (§9-12)
points at. Cheap; makes undersides read grounded instead of uniformly gray.

## 4. Vertical density profiles / cloud types (Nubis weather system)

Our vertical shape is one bottomFade × topFade envelope (`Cloud.shader:163-166`) — every
cloud is the same species. Nubis drives a **per-cell cloud-type parameter** from the
weather map that selects between stratus (low, flat), cumulus (billowing), cumulonimbus
(tall anvil) height profiles — a 1D lookup or three analytic envelopes blended:

```hlsl
float profile = lerp(StratusProfile(h), CumulusProfile(h), typeLow)
              ;  // second lerp toward cumulonimbus with storm
```

We have the perfect driver already: **storm cells should grow cumulonimbus profiles**
(tall, dense, anvil top) while calm humid cells stay stratus/cumulus. This is the change
that would make weather *legible from the sky shape* — storms visibly towering — rather
than only from darkening. Medium effort: the profile functions are trivial; tuning the
type mapping to the weather sim is the work.

## 5. Curl-noise distortion of the detail sample (Nubis 2015)

We sample detail noise at an undistorted position (`Cloud.shader:158`). Nubis warps the
detail sample position with low-frequency curl noise so eroded edges become **wispy and
sheared** instead of uniformly bumpy:

```hlsl
float3 curl = SampleCurl(advectedPos * _CloudCurlScale);   // small RGB texture or analytic
float3 detailPos = (advectedPos + curl * _CloudCurlStrength * (1.0 - cloud.height01))
                 * _CloudDetailNoiseScale;
```

Stronger at cloud base (wind shear), zero at top. One small 2D/3D texture (could be
generated by the existing `CloudNoiseGenerator`). Cheap; edges stop looking like eroded
Worley and start looking like weather.

## 6. Aerial perspective on distant clouds (Frostbite / Hillaire)

We composite clouds **after** the atmosphere pass (deliberate, to avoid terrain-depth
fogging), which means distant clouds get no atmospheric haze at all — crisp dark shapes at
the horizon where everything else fades. Fix without reordering passes: fade the cloud
contribution toward the horizon/sky color by marched distance:

```hlsl
float aerial = 1.0 - exp(-startDistance * _CloudAerialDensity);
lightEnergy   = lerp(lightEnergy, horizonColor * (1.0 - transmittance), aerial);
```

`horizonColor` from the atmosphere system's existing globals. Small change, kills the
pasted-on horizon read — likely visible in every wide screenshot.

## 7. Cone-sampled light march (Nubis 2015)

Nubis takes its 6 light samples in an **expanding cone** toward the sun (plus one
long-range sample at 3× distance) instead of along a jittered line. Softens light-march
banding and gives more plausible penumbra on billows. Our jittered-line march
(`LightMarch`, `Cloud.shader:203-231`) is the same sample count — this is a redistribution,
not new cost. Do after #1/#2 since they change what the light march feeds.

## 8. Parked: temporal / quarter-res reprojection (Frostbite, and everyone since)

The industry answer to "more steps per pixel" remains quarter-res march + temporal
reprojection. We built it, you rejected the artifacts, it's reverted — recording that the
research consensus hasn't changed, so if step budget ever becomes the bottleneck again the
Frostbite variant (4×4 pixel update pattern + reprojection, rather than our EMA blend) is
the specific recipe to try, not a re-derivation.

---

## Where the local-only PDFs fit

`Ray Tracing Gems II.pdf` and `arbeit_fleck.pdf` are compressed; I couldn't extract text
to confirm cloud-relevant chapters this session. RTG II is mostly ray-tracing-pipeline
material — likely low direct value for a raster march. Worth a manual TOC skim; if
`arbeit_fleck` is the atmosphere/scattering thesis its name suggests, it belongs to the
atmosphere backlog, not clouds.

## Suggested order

| # | Technique | Source | Effort | Look payoff |
|---|-----------|--------|--------|-------------|
| 1 | Beer-Powder | Nubis 2015 | S | carved, puffy sunlit faces — biggest single win |
| 2 | Multi-scatter octaves | Oz / Frostbite | S-M | luminous-but-dark storm cores, principled gloom |
| 3 | Two-tone ambient | Nubis/Frostbite | S | grounded undersides |
| 4 | Aerial perspective | Frostbite | S | distant clouds sit *in* the sky |
| 5 | Curl-distorted detail | Nubis 2015 | M | wispy sheared edges |
| 6 | Cloud-type profiles from storm | Nubis 2017 | M | towering storms, layered calm — weather legibility |
| 7 | Cone light march | Nubis 2015 | S | softer light banding |

1-4 are shader-only, no new assets except two color constants; 5 needs one small texture;
6 is the one that changes how weather *reads* and deserves its own tuning session. All
stack with the already-queued R1 (blue noise) and R2 (detail early-out) from the reference
doc.

---

## Addendum 2026-07-05 — iq "Clouds" Shadertoy review

Bryan supplied Inigo Quilez's 2013 volumetric-cloud Shadertoy for evaluation.
**License forbids any use of the code itself** (no products, commercial or not) — ideas
only, via iq's public articles, independently implemented. Three are worth taking:

- **A1. Distance-proportional march steps** — `dt ∝ t` (log-distributed) instead of our
  uniform `(end-start)/steps`. Concentrates samples near the camera where banding is
  visible; cheapest remaining anti-banding lever after blue noise. Fold into the cloud
  plan's Phase 3 or 5.
- **A2. Distance-LOD on detail noise** — skip the detail fetch beyond a few km (erosion is
  sub-pixel there). Complements the Phase-1 empty-air early-out.
- **A3. Directional-derivative lighting for the cheap tier** — one density sample toward
  the sun (`clamp((den(p) - den(p + k·sunDir))/k', 0, 1)`, see
  iquilezles.org/articles/derivative) replaces the light march in `CLOUD_QUALITY_LOW`
  (currently 3 marched steps → 1 sample). Not for the hero path — powder + multi-scatter
  octaves are strictly better where self-shadowing matters.

Already covered elsewhere: blue-noise dither (Phase 1, shipped), `1-exp(-k·t²)` fog toward
background (= Phase 3 aerial perspective), high-opacity early-out (have it), in-shader
value-noise fBM and color hacks (superseded by baked Worley + the Phase-2 lighting model).
