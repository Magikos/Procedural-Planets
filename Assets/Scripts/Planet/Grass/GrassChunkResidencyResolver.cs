using System.Collections.Generic;
using UnityEngine;

sealed class GrassChunkResidencyResolver
{
    readonly ChunkedSurfaceProvider _surfaceProvider;
    readonly float _paddingDegrees;
    readonly int _minDepth;

    readonly List<PlanetChunk> _scratch = new(128);
    readonly HashSet<PlanetChunk> _chunks = new();

    public HashSet<PlanetChunk> Chunks => _chunks;
    public int VisibleChunks { get; private set; }
    public int BufferedResidencyChunks { get; private set; }

    public GrassChunkResidencyResolver(ChunkedSurfaceProvider surfaceProvider, int minDepth, float paddingDegrees)
    {
        _surfaceProvider = surfaceProvider;
        _minDepth = minDepth;
        _paddingDegrees = paddingDegrees;
    }

    public void Refresh(Camera camera)
    {
        _surfaceProvider.GetGrassResidencyChunks(camera, _paddingDegrees, _scratch);
        _chunks.Clear();
        BufferedResidencyChunks = 0;
        for (int i = 0; i < _scratch.Count; i++)
        {
            PlanetChunk chunk = _scratch[i];
            if (chunk == null || chunk.DetailLevel < _minDepth)
                continue;
            _chunks.Add(chunk);
            if (!_surfaceProvider.IsChunkTerrainVisible(chunk))
                BufferedResidencyChunks++;
        }
        VisibleChunks = _chunks.Count - BufferedResidencyChunks;
    }
}
