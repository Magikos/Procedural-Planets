using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A debug view for finding wildlife: the world goes dull blue and every creature is ringed in a hot colour
/// that ignores depth. Answers "I flew around and never saw one" directly, and keeps answering it when a
/// species is a 45 cm capsule in tall grass.
/// </summary>
/// <remarks>
/// <para>
/// Drawn from <see cref="RenderPipelineManager.endCameraRendering"/> with a command buffer, the same way the
/// console and loading overlays already draw. That deliberately avoids a renderer feature: adding one means
/// editing the URP renderer asset, which is shared state and currently carries unrelated uncommitted work.
/// </para>
/// <para>
/// A view, never authority. It reads the poses the residency service publishes and touches nothing else.
/// </para>
/// </remarks>
public sealed class CreaturePredatorVision : System.IDisposable
{
    // Multiplied over the scene, so these are the fractions of each channel that survive. Red is crushed
    // hardest, which is what leaves the hot markers with nothing to compete against.
    static readonly Color WorldTint = new(0.10f, 0.17f, 0.38f, 1f);

    // Hot, high-contrast, and distinguishable from each other. Index by species, wrapping.
    static readonly Color[] SpeciesColors =
    {
        new(1.00f, 0.16f, 0.10f, 1f),   // red
        new(1.00f, 0.85f, 0.10f, 1f),   // yellow
        new(1.00f, 0.45f, 0.00f, 1f),   // orange
        new(1.00f, 0.20f, 0.80f, 1f),   // magenta
    };

    const float MarkerPixels = 26f;

    static readonly int TintColorId = Shader.PropertyToID("_TintColor");
    static readonly int MarkerColorId = Shader.PropertyToID("_MarkerColor");
    static readonly int MarkerPixelsId = Shader.PropertyToID("_MarkerPixels");

    readonly List<(Vector3 position, Color color)> _markers = new();
    readonly MaterialPropertyBlock _block = new();

    Material _material;
    Mesh _quad;
    bool _enabled;
    bool _hooked;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (_enabled) Hook();
            else Unhook();
        }
    }

    /// <summary>
    /// Take a copy of where the creatures are. Called from the host's update, because the render callback
    /// must not reach back into the residency service while a frame is being drawn.
    /// </summary>
    public void Sync(IReadOnlyList<CreatureResidencyService.LiveCreature> live, CreatureLibraryDto library)
    {
        if (!_enabled) return;

        _markers.Clear();
        if (live == null) return;

        for (int i = 0; i < live.Count; i++)
        {
            CreatureResidencyService.LiveCreature c = live[i];
            float height = library?.At(c.SpeciesIndex)?.BodyHeightMeters ?? 1f;

            // Ring the middle of the animal rather than its feet, so the marker reads as being ON it.
            _markers.Add((c.Position + c.Up * height * 0.5f, SpeciesColors[c.SpeciesIndex % SpeciesColors.Length]));
        }
    }

    void Hook()
    {
        if (_hooked) return;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        _hooked = true;
    }

    void Unhook()
    {
        if (!_hooked) return;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        _hooked = false;
        _markers.Clear();
    }

    bool EnsureResources()
    {
        if (_material == null)
        {
            Shader shader = Shader.Find("Hidden/PredatorVision");
            if (shader == null) return false;
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        if (_quad == null)
        {
            // Corner offsets only - the shader pins the marker to the object origin and expands it in screen
            // space, so the mesh's own size never reaches the screen.
            _quad = new Mesh { name = "PredatorMarker", hideFlags = HideFlags.HideAndDontSave };
            _quad.SetVertices(new List<Vector3>
            {
                new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(0.5f, 0.5f, 0f), new(-0.5f, 0.5f, 0f),
            });
            _quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            _quad.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);   // never frustum-culled
        }
        return true;
    }

    void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (!_enabled || cam != Camera.main) return;
        if (!EnsureResources()) return;

        var cmd = new CommandBuffer { name = "PredatorVision" };
        try
        {
            _material.SetColor(TintColorId, WorldTint);
            cmd.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);

            _material.SetFloat(MarkerPixelsId, MarkerPixels);
            for (int i = 0; i < _markers.Count; i++)
            {
                _block.SetColor(MarkerColorId, _markers[i].color);
                cmd.DrawMesh(_quad, Matrix4x4.Translate(_markers[i].position), _material, 0, 1, _block);
            }

            ctx.ExecuteCommandBuffer(cmd);
            ctx.Submit();
        }
        finally
        {
            cmd.Release();
        }
    }

    public void Dispose()
    {
        Unhook();
        if (_material != null) Object.Destroy(_material);
        _material = null;
        if (_quad != null) Object.Destroy(_quad);
        _quad = null;
    }
}
