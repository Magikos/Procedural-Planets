# Development Guidelines — Procedural Planets

## Naming Conventions

### Fields & Properties (5/5 project files follow this)
- **Public fields**: PascalCase — `public int Seed`, `public bool AutoUpdate`, `public float PlanetRadius`
- **Private/protected fields**: underscore-prefixed camelCase — `_shapeSettings`, `_noiseFilters`, `_mesh`, `_resolution`
- **Properties**: PascalCase — `public float Min { get; private set; }`, `public ShapeGenerator ShapeGenerator => _shapeGenerator;`
- **Local variables**: camelCase — `float elevation`, `int triIndex`, `Vector3 pointOnUnitSphere`
- **Constants**: camelCase in project code (`textureResolution = 50`), though reference code uses PascalCase (`RandomSize`)
- **Parameters**: camelCase — `float unscaledElevation`, `Vector3 point`, `int seed`

### Types (5/5 files)
- **Classes**: PascalCase — `Planet`, `ShapeGenerator`, `TerrainFace`, `ColorGenerator`
- **Interfaces**: I-prefix PascalCase — `INoiseFilter`
- **Enums**: PascalCase type and values — `FilterType { Simple, Rigid }`
- **Nested classes**: PascalCase — `NoiseLayer`, `BiomeColorSettings`, `Biome`
- **Structs**: PascalCase — `SpawnLocation`

### Methods (5/5 files)
- PascalCase for all methods — `GeneratePlanet()`, `CalculateUnscaledElevation()`, `ConstructMesh()`
- Private methods also PascalCase — `Initialize()`, `GenerateMesh()`, `IsValid()`

## Code Organization Patterns

### File Structure (observed across all project files)
```csharp
using UnityEngine;           // Unity usings first
using System.Collections;    // System usings after

public class ClassName : MonoBehaviour  // No namespace for project code
{
    // Serialized/public fields first
    // Private fields next
    // Unity lifecycle methods (OnValidate, Awake)
    // Public methods
    // Private methods
}
```

### Namespaces in Project Code
- Project scripts mostly live in the global namespace today; treat this as current legacy state, not a permanent architecture rule
- Only plugin/reference code consistently uses namespaces today (`Shapes`, `Seb.GPUSorting`)
- If project namespaces are introduced, do it deliberately by subsystem and update assembly/rule guidance at the same time

### ScriptableObject Settings Pattern (2/2 settings files)
Settings are defined as ScriptableObjects with `CreateAssetMenu`:
```csharp
[CreateAssetMenu(menuName = "Planet/Settings/Shape Settings")]
public class ShapeSettings : ScriptableObject
{
    [Range(1, 100)]
    public float PlanetRadius = 1;
    public NoiseLayer[] NoiseLayers;
}
```
- Menu path convention: `"Planet/Settings/{Name}"`
- Nested `[System.Serializable]` classes for grouped data
- Public fields with `[Range]` attributes for inspector constraints

### Factory Pattern for Noise Filters
```csharp
public static INoiseFilter CreateNoiseFilter(NoiseSettings settings, int seed = 0)
{
    switch (settings.Filter)
    {
        case NoiseSettings.FilterType.Simple:
            return new SimpleNoiseFilter(settings, seed);
        case NoiseSettings.FilterType.Rigid:
            return new RigidNoiseFilter(settings, seed);
        default:
            throw new System.ArgumentException("Unknown filter type: " + settings.Filter);
    }
}
```

### Inheritance for Noise Variants
```csharp
public class SimpleNoiseFilter : INoiseFilter
{
    protected Noise _noise;
    protected NoiseSettings _noiseSettings;
    public virtual float Evaluate(Vector3 point) { ... }
}

public class RigidNoiseFilter : SimpleNoiseFilter
{
    public override float Evaluate(Vector3 point) { ... }
}
```
- Base class uses `protected` fields and `virtual` methods
- Derived class overrides only what differs

## Unity-Specific Patterns

### OnValidate for Live Preview (3/3 MonoBehaviours)
All MonoBehaviours with configurable parameters use `OnValidate()` for editor live updates:
```csharp
void OnValidate()
{
    GeneratePlanet();
}
```

### Custom Editor Pattern
```csharp
[CustomEditor(typeof(Planet))]
public class PlanetEditor : Editor
{
    Planet _planet;
    void OnEnable() { _planet = (Planet)target; }
    
    public override void OnInspectorGUI()
    {
        using (var check = new EditorGUI.ChangeCheckScope())
        {
            base.OnInspectorGUI();
            if (check.changed) _planet.GeneratePlanet();
        }
    }
}
```
- Uses `EditorGUI.ChangeCheckScope` for change detection
- `CreateCachedEditor` for inline sub-editors
- Foldout state stored as `[SerializeField, HideInInspector]` on the target

### Serialization Attributes
- `[SerializeField, HideInInspector]` — for editor state that shouldn't show in default inspector
- `[Header("...")]` — for grouping fields in inspector
- `[Range(min, max)]` — for constrained numeric fields
- `[System.Serializable]` — for nested data classes

### Mesh Generation Pattern
```csharp
_mesh.Clear();
_mesh.vertices = vertices;
_mesh.triangles = triangles;
_mesh.RecalculateNormals();
_mesh.uv = uvCache;
```
- Always `Clear()` before reassigning mesh data
- `RecalculateNormals()` after setting vertices and triangles
- UV cache reuse when array length matches

## Deterministic Generation

### Seed Propagation Chain
```
Planet.Seed → ShapeGenerator.UpdateSettings(settings, seed)
    → NoiseFilterFactory.CreateNoiseFilter(settings, seed + i)
        → new Noise(seed)
```
- Seed is offset per noise layer (`seed + i`) for variation
- `System.Random` used for Poisson-disc sampling
- Noise class uses seed-based permutation table shuffling

### Poisson-Disc Sampling (Sphere Variant)
```csharp
public static List<SpawnLocation> GeneratePoints(
    float minimumSpacing, int maxAttempts,
    ShapeGenerator shapeGenerator, int seed,
    System.Func<Vector3, int> biomeSelector = null)
```
- Returns `SpawnLocation` structs with position, elevation, normal, biomeIndex
- Uses `System.Random(seed)` for determinism
- Biome selection via optional `Func<Vector3, int>` delegate

## Code Style

### Bracing
- Opening brace on same line as declaration for single-line checks: `if (x) { ... }`
- Opening brace on new line for method/class bodies (standard C# style)
- Single-line bodies sometimes use inline braces: `if (_meshFilters[i] == null) { ... }`

### Minimal Comments
- Code is self-documenting through descriptive naming
- Comments used sparingly for non-obvious math or algorithm steps
- XML doc comments only in the Noise class for complex math operations

### Static Utility Classes
- `PoissonDiscSampling` and `NoiseFilterFactory` are static classes
- Used for stateless operations that don't need instance state

### Expression-Bodied Members
- Used for simple properties: `public ShapeGenerator ShapeGenerator => _shapeGenerator;`
- Not overused — multi-line methods use block bodies

## Performance Considerations
- UV cache reuse in TerrainFace to avoid allocation when resolution unchanged
- MinMax tracking during generation avoids second pass over vertices
- Noise permutation table doubled (`RandomSize * 2`) to avoid modulo in lookups
- Planned: Unity Jobs/Burst for mesh generation, compute shaders for noise
