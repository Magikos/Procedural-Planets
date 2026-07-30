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

    static readonly int SunParamsId = Shader.PropertyToID("_SunParams");
    static readonly int PlanetCenterId = Shader.PropertyToID("_PlanetCenter");
    static readonly int NightAmbientId = Shader.PropertyToID("_NightAmbientIntensity");

    void OnEnable() => Publish();
    void Update() => Publish();

    void Publish()
    {
        Vector3 sunDir = Sun != null ? -Sun.transform.forward : Vector3.up; // direction TO the sun
        Shader.SetGlobalVector(SunParamsId, sunDir.normalized);
        Shader.SetGlobalVector(PlanetCenterId, new Vector3(0f, -1_000_000f, 0f));
        Shader.SetGlobalFloat(NightAmbientId, NightAmbientIntensity);
    }
}
