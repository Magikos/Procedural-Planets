# Water reflection correction

Bryan reported reflection pop-in and background trees becoming mountain-like blobs.
The live lake viewpoint reproduced the large blobs. Evidence lives in `local-only/water-reflection-2026-09-08/`.

## Isolation

`baseline.png` and `no-probe.png` use the same paused camera and wave time.
Disabling the reflection cube removes the blobs while screen-space reflections retain tree silhouettes.
The six `cube-*.png` files show the capture contains large nearby terrain surfaces.
Restricting the cube hemisphere did not remove the distortion. Sampling the reflection direction directly did.

The previous lookup projected all geometry onto a sphere at the probe's 350 m far plane.
That assumption is invalid for nearby banks and magnified their silhouettes across the water.
Ocean now uses the cube's angular direction. Screen-space reflections continue to supply visible geometry.

## Update continuity

WaterReflectionCapture now rotates three textures: previous completed capture, current completed capture, and render target.
The shader blends completed captures over 0.35 seconds. It never samples the target while Unity renders its six faces.
The first completed capture bypasses the previous image. Dispose releases all three textures and clears their globals.
Distance fade now updates while a capture remains pending.

## Validation

Planet and Core builds passed. Unity reported no C# compilation failure before the fresh Play run.
`probe-direction.png` records the shader isolation before the runtime restart.
`after-settled.png` and four `after-angle-*.png` captures record the fresh runtime at local noon and nearby viewing angles.
The eight-second `blend-runtime.csv` trace contains 173 frame samples, including 27 intermediate blend values between zero and one.
Ocean compiled without shader errors; the existing WeatherCloudConvectivity warning remains.
The console also contains Hot Reload failures for concurrent edits to ISwimmingProvider, CreatureResidencyService, and SoundReviewStore. These are outside this reflection change.
The camera pose and time-freeze state were restored. Play mode remains running.

## Pop-in follow-up

Bryan selected pop-in as the remaining priority. Ocean now fades screen-space reflection confidence before the Fresnel, ray-angle, and camera-distance cutoffs.
Hit refinement increased from four to eight bisections. The largest Low-quality march interval is 46.875 m: four bisections leave a 2.930 m interval; eight leave 0.183 m.
This puts continuous-surface refinement error below the existing 0.3 m confidence threshold. Depth discontinuities and off-screen occlusion remain approximate.
Six before/after captures at -3, 0, and +3 degree camera offsets are in `local-only/water-pop-in-2026-09-08/`.
The before variant isolates four-step refinement and unfaded confidence; both variants retain early trace rejection. These stills do not prove removal of every temporal pop.
Unity imported the shader successfully. The camera rotation and original material were restored, with Play mode running.
The cubemap remains a position approximation. This change does not claim exact off-screen geometry or hidden-tree reconstruction.
