# ProceduralPlanets Debugging Rule: Hard Isolation Before Tuning

When debugging ProceduralPlanets water rendering, do not keep tweaking values without first proving which system owns the fault. Bryan explicitly wants hard diagnostic lines: isolate the cause first, then fix that cause.

Standing workflow:
- Use binary/extreme tests to prove or eliminate a hypothesis. Example: if alpha is suspected, force alpha to an extreme or bypass blending/composite instead of adjusting by tiny increments.
- If an extreme test does not change the artifact, treat that branch as likely disproven and move to another subsystem.
- Separate fault-finding from tuning. Tuning is only appropriate after the responsible system/path has been identified.
- Prefer hard debug modes, bypasses, forced colors, forced opacity, disabled passes, and side-by-side F10 evidence over incremental knob changes.
- If multiple F10 runs show no visible progress, stop that line of work and redesign the diagnostic approach before continuing.

This applies especially to the underwater shoreline gaps and water surface effects. The goal is to find the fault first, then make targeted changes.
