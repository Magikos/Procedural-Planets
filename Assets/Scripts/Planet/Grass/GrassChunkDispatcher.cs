using System.Collections.Generic;
using UnityEngine;

sealed class GrassChunkDispatcher : System.IDisposable
{
    const int LaneResolution = PlanetChunkTextures.BiomeMapResolution;
    const int ThreadGroupSize = 8;
    const float LaneJitterMagnitude = 1.1f; // > 1 = blades from adjacent lanes overlap visually
    const string PlacementComputeResource = "BiomeGrassPlace";

    static readonly int BladeInstancesId = Shader.PropertyToID("_GrassBladeInstances");
    static readonly int GrassDrawArgsId = Shader.PropertyToID("_GrassDrawArgs");
    static readonly int BiomeIdsId = Shader.PropertyToID("_BiomeIds");
    static readonly int BiomeWeightsId = Shader.PropertyToID("_BiomeWeights");
    static readonly int SurfaceStateMaskId = Shader.PropertyToID("_SurfaceStateMask");
    static readonly int GrassSurfaceRadiusId = Shader.PropertyToID("_GrassSurfaceRadius");
    static readonly int GrassSurfaceNormalId = Shader.PropertyToID("_GrassSurfaceNormal");
    static readonly int BiomeGrassParamsId = Shader.PropertyToID(ShaderGlobalIds.BiomeGrassParams);
    static readonly int BiomeGrassParamCountId = Shader.PropertyToID(ShaderGlobalIds.BiomeGrassParamCount);
    static readonly int BiomeAtlasResolutionId = Shader.PropertyToID("_BiomeAtlasResolution");
    static readonly int GrassSurfaceAtlasResolutionId = Shader.PropertyToID(ShaderGlobalIds.GrassSurfaceAtlasResolution);
    static readonly int LaneResolutionId = Shader.PropertyToID("_LaneResolution");
    static readonly int MaxBladeInstancesId = Shader.PropertyToID("_MaxBladeInstances");
    static readonly int MaxBladesPerLaneId = Shader.PropertyToID("_MaxBladesPerLane");
    static readonly int GrassDensityMultiplierId = Shader.PropertyToID("_GrassDensityMultiplier");
    static readonly int FaceIndexId = Shader.PropertyToID("_FaceIndex");
    static readonly int ChunkHashId = Shader.PropertyToID("_ChunkHash");
    static readonly int ChunkUvScaleOffsetId = Shader.PropertyToID("_ChunkUvScaleOffset");
    static readonly int PlanetLocalToWorldId = Shader.PropertyToID("_PlanetLocalToWorld");
    static readonly int PlanetWorldScaleId = Shader.PropertyToID("_PlanetWorldScale");
    static readonly int WaterRadiusId = Shader.PropertyToID("_WaterRadius");
    static readonly int SeedId = Shader.PropertyToID("_Seed");
    static readonly int CameraPositionWsId = Shader.PropertyToID("_CameraPositionWs");
    static readonly int MaxRenderDistanceId = Shader.PropertyToID("_MaxRenderDistance");
    static readonly int DistanceFadeStartId = Shader.PropertyToID("_DistanceFadeStart");
    static readonly int CullDistanceJitter01Id = Shader.PropertyToID("_CullDistanceJitter01");
    static readonly int LaneJitterMagnitudeId = Shader.PropertyToID("_LaneJitterMagnitude");
    static readonly int ChunkInnerFadeStartId = Shader.PropertyToID("_ChunkInnerFadeStart");
    static readonly int ChunkInnerFadeEndId = Shader.PropertyToID("_ChunkInnerFadeEnd");
    static readonly int FrustumCullEnabledId = Shader.PropertyToID("_FrustumCullEnabled");
    static readonly int GrassStatsId = Shader.PropertyToID("_GrassStats");
    static readonly int ClimateMapId = Shader.PropertyToID(ShaderGlobalIds.ClimateMap);
    static readonly int ClimateMapResolutionId = Shader.PropertyToID(ShaderGlobalIds.ClimateMapResolution);

    readonly ChunkedSurfaceProvider _surfaceProvider;
    readonly Transform _planetTransform;
    readonly ComputeBuffer _grassParamsBuffer;
    readonly int _grassParamCount;
    readonly int _maxBladesPerLane;
    readonly int _maxBladeInstancesPerChunk;
    readonly float _grassDensityMultiplier;
    readonly float _maxRenderDistance;
    readonly float _distanceFadeStart;
    readonly float _cullDistanceJitter01;
    readonly float _waterRadius;
    readonly int _seed;
    readonly ComputeShader _placementCompute;
    readonly int _placeKernel;
    readonly GrassBladeBufferPool _bladePool;

    Texture2DArray _neutralClimateMap;
    Camera _tickCamera;
    bool _disposed;

    public bool IsAvailable { get; }
    public int MaxBladeInstancesPerChunk => _maxBladeInstancesPerChunk;

    public GrassChunkDispatcher(
        ChunkedSurfaceProvider surfaceProvider,
        Transform planetTransform,
        ComputeBuffer grassParamsBuffer,
        int grassParamCount,
        int maxBladesPerLane,
        int maxBladeInstancesPerChunk,
        float grassDensityMultiplier,
        float maxRenderDistance,
        float distanceFadeStart,
        float cullDistanceJitter01,
        float waterRadius,
        int seed,
        ILogger logger)
    {
        _surfaceProvider = surfaceProvider;
        _planetTransform = planetTransform;
        _grassParamsBuffer = grassParamsBuffer;
        _grassParamCount = Mathf.Max(grassParamCount, 0);
        _maxBladesPerLane = maxBladesPerLane;
        _maxBladeInstancesPerChunk = maxBladeInstancesPerChunk;
        _grassDensityMultiplier = grassDensityMultiplier;
        _maxRenderDistance = maxRenderDistance;
        _distanceFadeStart = distanceFadeStart;
        _cullDistanceJitter01 = cullDistanceJitter01;
        _waterRadius = waterRadius;
        _seed = seed;

        _bladePool = new GrassBladeBufferPool(maxBladeInstancesPerChunk, GrassChunkRuntime.BladeStride);

        if (!SystemInfo.supportsComputeShaders)
        {
            logger.Log(LogLevel.Info, "Grass", "BiomeGrassPlace compute shader was not available; no production grass will be placed.");
            return;
        }

        _placementCompute = Resources.Load<ComputeShader>(PlacementComputeResource);
        if (_placementCompute == null)
        {
            logger.Log(LogLevel.Info, "Grass", "BiomeGrassPlace compute shader was not available; no production grass will be placed.");
            return;
        }

        try
        {
            _placeKernel = _placementCompute.FindKernel("PlaceAndCull");
        }
        catch (System.Exception ex)
        {
            logger.Log(LogLevel.Warning, "Grass", $"BiomeGrassPlace.compute is missing PlaceAndCull: {ex.Message}");
            return;
        }

        if (grassParamsBuffer == null || _grassParamCount <= 0)
        {
            logger.Log(LogLevel.Warning, "Grass", "Biome grass parameter buffer was not available; no production grass will be placed.");
            return;
        }

        IsAvailable = true;
    }

    public void SetTickCamera(Camera camera) => _tickCamera = camera;

    public GrassChunkRuntime CreateAndDispatch(PlanetChunk chunk, Bounds worldBounds, float innerFadeStart, float innerFadeEnd)
    {
        if (!_surfaceProvider.TryGetFaceBiomeAtlases(chunk.FaceIndex,
                out _, out Texture2D biomeIds, out Texture2D biomeWeights))
            return null;
        if (!_surfaceProvider.GrassSurfaceAtlases.TryGetFace(chunk.FaceIndex,
                out Texture2D surfaceRadius, out Texture2D surfaceNormal))
            return null;
        if (chunk.SurfaceStateTexture == null)
            return null;

        var runtime = GrassChunkRuntime.Create(_bladePool, _maxBladeInstancesPerChunk, GrassChunkRuntime.BladeVertexCount,
            BladeInstancesId, GrassChunkRuntime.StatsCount, worldBounds);
        if (runtime == null)
            return null;

        DispatchSingle(chunk, runtime, biomeIds, biomeWeights, surfaceRadius, surfaceNormal, innerFadeStart, innerFadeEnd);
        runtime.RequestReadbacks();
        return runtime;
    }

    public int RedispatchAll(Dictionary<PlanetChunk, GrassChunkRuntime> chunks, float innerFadeStart, float innerFadeEnd)
    {
        int count = 0;
        foreach (var pair in chunks)
        {
            PlanetChunk chunk = pair.Key;
            GrassChunkRuntime runtime = pair.Value;
            if (runtime == null || !runtime.IsValid) continue;
            if (!_surfaceProvider.TryGetFaceBiomeAtlases(chunk.FaceIndex,
                    out _, out Texture2D biomeIds, out Texture2D biomeWeights)) continue;
            if (!_surfaceProvider.GrassSurfaceAtlases.TryGetFace(chunk.FaceIndex,
                    out Texture2D surfaceRadius, out Texture2D surfaceNormal)) continue;
            if (chunk.SurfaceStateTexture == null) continue;
            DispatchSingle(chunk, runtime, biomeIds, biomeWeights, surfaceRadius, surfaceNormal, innerFadeStart, innerFadeEnd);
            count++;
            runtime.RequestReadbacks();
        }
        return count;
    }

    void DispatchSingle(PlanetChunk chunk, GrassChunkRuntime runtime,
        Texture2D biomeIds, Texture2D biomeWeights, Texture2D surfaceRadius, Texture2D surfaceNormal,
        float innerFadeStart, float innerFadeEnd)
    {
        runtime.ResetArgsAndStats(GrassChunkRuntime.BladeVertexCount);

        _placementCompute.SetTexture(_placeKernel, BiomeIdsId, biomeIds);
        _placementCompute.SetTexture(_placeKernel, BiomeWeightsId, biomeWeights);
        _placementCompute.SetTexture(_placeKernel, SurfaceStateMaskId, chunk.SurfaceStateTexture);
        _placementCompute.SetTexture(_placeKernel, GrassSurfaceRadiusId, surfaceRadius);
        _placementCompute.SetTexture(_placeKernel, GrassSurfaceNormalId, surfaceNormal);
        _placementCompute.SetBuffer(_placeKernel, BladeInstancesId, runtime.BladeBuffer);
        _placementCompute.SetBuffer(_placeKernel, GrassDrawArgsId, runtime.ArgsBuffer);
        _placementCompute.SetBuffer(_placeKernel, GrassStatsId, runtime.StatsBuffer);

        _placementCompute.SetInt(BiomeGrassParamCountId, _grassParamCount);
        _placementCompute.SetBuffer(_placeKernel, BiomeGrassParamsId, _grassParamsBuffer);
        _placementCompute.SetInt(BiomeAtlasResolutionId, Mathf.Max(biomeIds.width, 1));
        _placementCompute.SetInt(GrassSurfaceAtlasResolutionId, Mathf.Max(surfaceRadius.width, 1));
        _placementCompute.SetInt(LaneResolutionId, LaneResolution);
        _placementCompute.SetInt(MaxBladeInstancesId, runtime.Capacity);
        _placementCompute.SetInt(MaxBladesPerLaneId, _maxBladesPerLane);
        _placementCompute.SetFloat(GrassDensityMultiplierId, _grassDensityMultiplier);
        _placementCompute.SetInt(FaceIndexId, chunk.FaceIndex);
        _placementCompute.SetInt(ChunkHashId, unchecked((int)chunk.HashValue));
        _placementCompute.SetVector(ChunkUvScaleOffsetId, _surfaceProvider.GrassSurfaceAtlases.GetUvScaleOffset(chunk));
        _placementCompute.SetMatrix(PlanetLocalToWorldId, _planetTransform.localToWorldMatrix);
        _placementCompute.SetFloat(PlanetWorldScaleId, GetUniformWorldScale(_planetTransform));
        _placementCompute.SetFloat(WaterRadiusId, _waterRadius);
        _placementCompute.SetInt(SeedId, _seed);
        // Camera position falls back to planet origin so initial placement before first Tick produces blades.
        Vector3 cameraPos = _tickCamera != null ? _tickCamera.transform.position : _planetTransform.position;
        _placementCompute.SetVector(CameraPositionWsId, cameraPos);
        _placementCompute.SetFloat(MaxRenderDistanceId, _maxRenderDistance);
        _placementCompute.SetFloat(DistanceFadeStartId, _distanceFadeStart);
        _placementCompute.SetFloat(CullDistanceJitter01Id, _cullDistanceJitter01);
        _placementCompute.SetFloat(LaneJitterMagnitudeId, LaneJitterMagnitude);
        _placementCompute.SetFloat(ChunkInnerFadeStartId, innerFadeStart);
        _placementCompute.SetFloat(ChunkInnerFadeEndId, innerFadeEnd);
        // Placement buffers persist while the camera turns, so baking the current
        // view frustum into them leaves square holes until the next movement-driven
        // redispatch. Rendering still culls each chunk by its world bounds.
        _placementCompute.SetInt(FrustumCullEnabledId, 0);

        Texture2DArray climateMap = GetClimateMap();
        _placementCompute.SetTexture(_placeKernel, ClimateMapId, climateMap);
        _placementCompute.SetInt(ClimateMapResolutionId, climateMap.width);

        int groups = Mathf.CeilToInt(LaneResolution / (float)ThreadGroupSize);
        _placementCompute.Dispatch(_placeKernel, groups, groups, 1);
    }

    Texture2DArray GetClimateMap()
    {
        if (Shader.GetGlobalTexture(ClimateMapId) is Texture2DArray map)
            return map;
        if (_neutralClimateMap == null)
        {
            _neutralClimateMap = new Texture2DArray(1, 1, 6, TextureFormat.RGHalf, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "NeutralClimateMapFallback",
            };
            var neutral = new Color(0f, 0.5f, 0f, 1f);
            for (int f = 0; f < 6; f++)
                _neutralClimateMap.SetPixels(new[] { neutral }, f);
            _neutralClimateMap.Apply(false, true);
        }
        return _neutralClimateMap;
    }

    static float GetUniformWorldScale(Transform transform)
    {
        if (transform == null) return 1f;
        Vector3 scale = transform.lossyScale;
        return Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bladePool.Dispose();
        if (_neutralClimateMap != null)
        {
            if (Application.isPlaying) Object.Destroy(_neutralClimateMap);
            else Object.DestroyImmediate(_neutralClimateMap);
            _neutralClimateMap = null;
        }
    }
}
