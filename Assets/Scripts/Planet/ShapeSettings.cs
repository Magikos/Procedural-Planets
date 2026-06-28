// Runtime terrain-shape parameters built per generation by PlanetDto.BuildShapeSettings.
// Plain class, not a ScriptableObject — it is never authored or stored as an asset, so creating
// one per generation must not allocate a leaked Unity object.
public class ShapeSettings
{
    public float PlanetRadius = 1;
    public NoiseLayer[] NoiseLayers;
    public DiagnosticTerrainLayoutDto DiagnosticTerrainLayout;

    public class NoiseLayer
    {
        public bool UseFirstLayerAsMask = false;
        public bool Enabled = true;
        public NoiseSettings NoiseSettings;
    }
}
