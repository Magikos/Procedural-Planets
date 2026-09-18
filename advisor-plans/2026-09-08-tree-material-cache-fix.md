# Tree material cache recovery

TreeInjection now checks Unity object validity on cache lookup for bark, fallback foliage, clean foliage, and conifer materials. It recreates destroyed entries and preserves surviving tuple members. Planet.RegisterWorldSettings already uses ApplyAll before world generation, so startup receives repaired references without a separate reset system.

ScatterValidation reports missing/destroyed materials and missing/unsupported shaders by prototype, species, and part. The existing injection validation invokes it in the Editor. ScatterField.Configure invokes it before scatter setup in the Editor and development builds. Release builds omit these diagnostic calls. Recovery checks remain in all builds. No per-frame scan was added.

## Evidence

- Core build: zero warnings, zero errors.
- Planet build: 18 existing warnings, zero errors.
- ScatterRenderingRegressionTests: 18 passed, zero failed. Four new cases cover destruction and reuse of each tree material cache, including preservation of a surviving tuple member.
- Two successive Unity Play sessions with both domain and scene reload disabled: each had 188 prototypes, zero invalid part materials, and zero invalid stump materials.
- Unity remains in the second Play session.
- graphify update completed.

The refresh tool initially timed out after 60 seconds; Unity completed compilation afterward and ran the tests successfully.

## Limits

The original destruction caller remains unproven. This fixes stale cache reuse; it does not intercept arbitrary destruction during an active world. Existing DTOs and draw parameters retain their original references until setup rebuilds them. Development diagnostics expose such later damage. No release player build or new visual comparison was performed.
