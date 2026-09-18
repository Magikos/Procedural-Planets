# Sun and lighting fixes

Status: S01 through S05 implemented and validated on 2026-09-09. Bryan approved implementation in this task.
Current next action: Bryan can review the archived visual comparison. No implementation or regression work remains for these findings.

1. Restore a valid atmosphere optical-depth texture on re-enable. Rebuild a missing or released texture through the existing bake path.
2. Keep a manual sun override in `CelestialManager`. Reset it through `light.direction-reset`, setting time, or unfreezing time.
3. Publish the shader direction and directional light together from the sun owner. Remove atmosphere's duplicate publication.
4. Apply the horizon fade on both sides of the tangent. Preserve visibility for outward rays.
5. Gate terrain specular by surface illumination. Use the existing safe normalization helper for opposing view and light directions.

Reuse existing console adapters, shader helpers, and NUnit tests. No dependency or separate lighting system is required.
Keep the current artistic tuning values. Shader pixel changes require matched evidence and Bryan's visual review.

Validation: focused celestial lifecycle/command tests, atmosphere re-enable tests, GPU formula tests, existing console/moon/shader regressions,
serial Core and Planet builds, fresh planet runtime checks, before/after captures, and `graphify update .`.

The audit and its validation queue retain pre-fix evidence. Final results will be appended there.

## Results

- Core and Planet builds passed. Planet reported 19 warnings in unrelated files; Core reported none.
- All 70 focused Unity tests passed, including seven new regression cases. No tests were skipped.
- Runtime checks confirmed texture restoration, persistent manual direction, explicit reset, pole fallback, and synchronized moving sunlight.
- GPU tests confirmed the continuous horizon mask, zero unlit specular, and finite opposing view/light directions.
- Before/after orbit captures use seed `1691104419`, quality index `0`, local-noon time `0.524170041`, and the recorded camera pose.
- Graphify completed successfully. The original animation review scene was restored in unpaused Play mode.

See the [validation record](../../plans/2026-09-09-sun-lighting-validation-queue.md#implemented-fixes--2026-09-09) for logs and measurements.
