using UnityEngine;

// Drives the planet's custom shader globals in a plain scene (no planet). The scatter/foliage shaders
// light from _SunParams (not the URP main light) and read _PlanetCenter for the surface up-vector, so a
// showcase must publish those for the assets to light like they do on the planet. Flat ground => put the
// planet centre far below so planetNormal resolves to world-up.
[ExecuteAlways]
public sealed class AssetShowcaseController : MonoBehaviour
{
    public Light Sun;
    [Range(0f, 1f)] public float NightAmbientIntensity = 0.1f;

    [Header("Wind provider (the showcase's stand-in for the weather system)")]
    [Tooltip("0 = calm => assets are perfectly still, exactly like the planet with no wind. Raise to test sway.")]
    [Range(0f, 1f)] public float WindStrength = 0f;
    public Vector3 WindDirection = new Vector3(1f, 0f, 0.3f);

    static readonly int SunParamsId = Shader.PropertyToID("_SunParams");
    static readonly int PlanetCenterId = Shader.PropertyToID("_PlanetCenter");
    static readonly int NightAmbientId = Shader.PropertyToID("_NightAmbientIntensity");
    static readonly int WindDirectionId = Shader.PropertyToID("_WindDirection");
    static readonly int WindStrength01Id = Shader.PropertyToID("_WindStrength01");
    static readonly int WindSpeedMpsId = Shader.PropertyToID("_WindSpeedMps");
    static readonly int InteractorsId = Shader.PropertyToID("_GrassInteractors");
    static readonly int InteractorCountId = Shader.PropertyToID("_GrassInteractorCount");

    ComputeBuffer _dummyInteractors;

    void OnEnable()
    {
        // FoliageLit (and grass) declare the global _GrassInteractors StructuredBuffer. On the planet the
        // GrassInteractorRegistry binds it at init; here there is no registry, and an UNBOUND StructuredBuffer
        // makes the driver silently drop every FoliageLit draw (invisible foliage). Bind a 1-element dummy so
        // the SRV is always valid; count 0 means the shader loop never reads it.
        // ponytail: any future non-planet scene that renders FoliageLit needs this too. If that spreads,
        // promote to an app-scope global fallback binding instead of per-scene.
        _dummyInteractors ??= new ComputeBuffer(1, sizeof(float) * 8, ComputeBufferType.Structured);
        Shader.SetGlobalBuffer(InteractorsId, _dummyInteractors);
        Shader.SetGlobalInt(InteractorCountId, 0);
        Publish();
    }

    void OnDisable()
    {
        _dummyInteractors?.Release();
        _dummyInteractors = null;
    }

    void Update() => Publish();

    void Publish()
    {
        Vector3 sunDir = Sun != null ? -Sun.transform.forward : Vector3.up; // direction TO the sun
        Shader.SetGlobalVector(SunParamsId, sunDir.normalized);
        Shader.SetGlobalVector(PlanetCenterId, new Vector3(0f, -1_000_000f, 0f));
        Shader.SetGlobalFloat(NightAmbientId, NightAmbientIntensity);

        // Publish the same wind globals WeatherManager publishes on the planet. Default 0 => no motion.
        Vector3 dir = WindDirection.sqrMagnitude > 1e-4f ? WindDirection.normalized : Vector3.right;
        Shader.SetGlobalVector(WindDirectionId, dir);
        Shader.SetGlobalFloat(WindStrength01Id, WindStrength);
        Shader.SetGlobalFloat(WindSpeedMpsId, WindStrength * 12f);
    }
}
