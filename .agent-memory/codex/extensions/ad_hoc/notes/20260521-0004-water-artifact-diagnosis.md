# ProceduralPlanets Water Artifact Diagnosis

[ad-hoc note] Bryan ran the targeted F10 water artifact capture set successfully. Latest sets: `20260520-235748` above sea level (`DistanceToCenter=5034.60`) and `20260520-235803` just below sea level (`DistanceToCenter=4998.30`).

[ad-hoc note] Diagnosis from the new modes: above-water thin far-shore line appears in `VolumeBoundary`/`VolumeOptical`, so it is primarily a water-volume coverage/edge artifact. Underwater/near-waterline dotted shoreline marks remain visible in surface-isolated modes, so that is a separate surface shoreline/alpha artifact.

[ad-hoc note] `WaterVolume.shader` now uses an eroded `volumeWaterMask = waterMask * smoothstep(0.030, 0.115, waterMaskBasis)` for volume contribution while still exposing raw water mask in debug red. This should prevent subpixel shoreline fringe pixels from receiving the volume composite.

[ad-hoc note] `Ocean.shader` now suppresses submerged shore foam harder and multiplies shoreline-edge surface alpha/foam by `UnderwaterShoreEdgeVisibility`, targeting low-`shore01` edges only when the camera is near/under the sea surface.
