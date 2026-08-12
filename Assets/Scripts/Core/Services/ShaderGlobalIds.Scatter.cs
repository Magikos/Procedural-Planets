public static partial class ShaderGlobalIds
{
    // Set to 1 by ScatterImpostorBaker while it renders the octahedral atlas; the scatter/foliage mesh
    // shaders then output flat unlit albedo (no directional sun/shadow) so the impostor card can be
    // relit at runtime instead of freezing one sun angle into the bake.
    public const string ImpostorAlbedoBake = "_ImpostorAlbedoBake";

    // Set to 1 by ScatterImpostorBaker's normal pass; the scatter/foliage mesh shaders then output the
    // view-space surface normal (encoded 0..1) instead of albedo, so the impostor can be relit with the
    // real surface normal at runtime rather than a synthesized hemisphere.
    public const string ImpostorNormalBake = "_ImpostorNormalBake";
}
