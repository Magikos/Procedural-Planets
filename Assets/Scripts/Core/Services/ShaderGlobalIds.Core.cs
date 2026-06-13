// Single source of truth for shader-global string names. Partial class split one file per domain
// (see ShaderGlobalIds.Atmosphere.cs, .Water.cs, .Cloud.cs, .Grass.cs, .Precipitation.cs,
// .Terrain.cs, .Celestial.cs). A global name is any name passed to Shader.SetGlobal*/GetGlobal*;
// modules cache their own Shader.PropertyToID(ShaderGlobalIds.X) locally. Material/compute property
// names are shader-scoped, not globals, and do not belong here.
public static partial class ShaderGlobalIds
{
    public const string GameTime = "_GameTime";
    public const string PlanetCenter = "_PlanetCenter";
    public const string SeaLevelRadius = "_SeaLevelRadius";
}
