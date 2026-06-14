# ProceduralPlanets Water Debug Update

[ad-hoc note] In `c:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets`, latest F10 feedback said water colors and above/under water behavior look good, with remaining thin shoreline-like lines visible from above water and somewhat underwater.

[ad-hoc note] `FreeCameraController` now has F10 capture retention via `DebugScreenshotMaxRuns = 6`, keeping newest `maxRuns * F10ModeCount * 2` PNG/TXT files under `local-only/debug-screenshots` when the next F10 capture is saved.

[ad-hoc note] `Ocean.shader` now attenuates shoreline lip foam by distance and submerged camera state, breaks up the edge lip with noise, and uses a smoothed near-sea-level underwater test for camera-medium absorption. Intent is to keep close shoreline foam but reduce distant/underwater white-line artifacts.

[ad-hoc note] `.amazonq/rules/memory-bank/water.md` was also updated with the same current state. Continue using the F10 captures, especially Off, Shore, Foam, WaterData, VolumeMask, and VolumePath, to verify whether the line is surface shoreline data or still a volume edge.

[ad-hoc note] `dotnet build ProceduralPlanets.Core.csproj` and `dotnet build ProceduralPlanets.Planet.csproj` passed after these changes. Unity shader reimport is still needed to validate `Ocean.shader`.
