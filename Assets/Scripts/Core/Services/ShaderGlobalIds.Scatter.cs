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

    // Foliage translucency: how strongly leaves glow when the sun is behind them. A GLOBAL rather than a
    // material property because it is a look knob that gets dialled by eye — as a per-material value it could
    // not be changed without a world reload, and tuning it at runtime would mean writing to shared material
    // assets. Published by ScatterRenderer.Configure; `scatter.backlight` overrides it live.
    public const string FoliageBacklight = "_FoliageBacklight";

    // Seconds a newly gathered instance takes to dither in. A GLOBAL for the same reason as
    // FoliageBacklight: it is a feel value dialled by eye, and it must apply to every scatter material at
    // once. Zero disables the ramp (instances appear immediately, the old behaviour).
    public const string ScatterFadeInSeconds = "_ScatterFadeInSeconds";
}
