public static partial class ShaderGlobalIds
{
    // Set to 1 by ScatterImpostorBaker while it renders the octahedral atlas; the scatter/foliage mesh
    // shaders then output flat unlit albedo (no directional sun/shadow) so the impostor card can be
    // relit at runtime instead of freezing one sun angle into the bake.
    public const string ImpostorAlbedoBake = "_ImpostorAlbedoBake";
}
