scope: ProceduralPlanets cloud seam diagnosis and fix after the chunk/performance work exposed a sharp cloud boundary.
applies_to: cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets; reuse_rule=safe when a future cloud/weather seam appears, but revalidate against the current F10 Cloud Diagnostics bundle before editing.

## Cloud cube-face seam fix

- Symptom: a sharp diagonal/cube-face-shaped line appeared through the cloud layer. The new `Cloud Diagnostics` F10 capture set proved the artifact was already visible in `CloudWeather`, then propagated into `CloudDensity`, `CloudOpticalDepth`, and normal `Off`. This made it a weather texture / cube-face sampling issue, not a cloud lighting or composite issue.
- First attempted fix: `WeatherEvolution.compute` was updated to edge-snap border texels during evolution, matching initial weather generation. This was valid but only partial; a follow-up F10 still showed the wedge in `CloudWeather`.
- Root cause: the weather grid was generated with `CubeFaceToUnitSphere(face, uv)`, but shader-side `CubeFaceUv(direction)` was not its inverse. Several faces were flipped or rotated when sampled, producing large face-shaped discontinuities.
- Final fix: align cube-face UV mapping across `Assets/Graphics/Shaders/Includes/WeatherSampling.hlsl`, `Assets/Graphics/Shaders/WeatherEvolution.compute`, `Assets/Graphics/Shaders/Includes/CloudShadows.hlsl`, and the CPU query path in `Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs`.
- Verification: `dotnet build ProceduralPlanets.Planet.csproj --no-restore` passed after the shader/C# changes. Bryan then circled the planet several times and could not find another cloud seam; treat this as visually fixed for now.
- If a seam returns: start with `Cloud Diagnostics` F10 and inspect `CloudWeather` first. If the seam is present there, revisit weather cube-face sampling, evolution, or true cross-face filtering/ghost border texels. If `CloudWeather` is clean but `CloudDensity` or `Off` shows it, move downstream to cloud density/raymarch/lighting.
