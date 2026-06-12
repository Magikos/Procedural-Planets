using UnityEngine;

// Multiple debug modules drive the same _OceanDebugMode shader global (water shader
// interprets it, but biome/terrain visualizations ride the same uniform). The registry's
// mode-applier loop calls every applier — owner applies, all others clear — so naive Clear
// implementations would overwrite the owning Apply in the same dispatch. This writer
// tracks the current owner so Clear is a no-op for non-owners.
public static class OceanDebugModeWriter
{
    static readonly int OceanDebugModeId = Shader.PropertyToID(ShaderGlobalIds.OceanDebugMode);
    static DebugModuleId _activeOwner;

    public static void Set(DebugModuleId owner, int modeLocalId)
    {
        _activeOwner = owner;
        Shader.SetGlobalInt(OceanDebugModeId, modeLocalId);
    }

    public static void Clear(DebugModuleId owner)
    {
        if (_activeOwner == owner)
        {
            _activeOwner = default;
            Shader.SetGlobalInt(OceanDebugModeId, DebugModeConstants.Off);
        }
    }
}
