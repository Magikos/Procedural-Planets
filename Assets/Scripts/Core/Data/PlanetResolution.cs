// Selects which planet surface generator a Planet uses. `Low` is the per-face mesh path
// (one mesh per cube face, fixed vertex resolution). `High` is the chunked quadtree LOD
// path. See docs/design/2026-05-30-chunk-skeleton.md for the contract.
public enum PlanetResolution
{
    Low,
    High
}
