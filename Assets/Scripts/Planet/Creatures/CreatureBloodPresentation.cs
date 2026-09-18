using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Bounded stain cache. The host supplies accepted wound events and game time.</summary>
public sealed class CreatureBloodPresentation : IDisposable
{
    readonly Transform _root;
    readonly IGroundingProvider _ground;
    readonly Material _material;
    readonly MaterialPropertyBlock _block = new();
    readonly List<(SurfaceEditStamp stamp, GameObject view)> _stains = new();
    ulong _nextId = 1;
    public int Count => _stains.Count;

    public CreatureBloodPresentation(Transform parent, Texture2D texture, IGroundingProvider ground)
    {
        _root = new GameObject("Blood stains").transform; _root.SetParent(parent, false); _ground = ground;
        _material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        _material.SetFloat("_AlphaClip", 1f); _material.EnableKeyword("_ALPHATEST_ON");
        _material.SetFloat("_Cutoff", .12f); _material.SetFloat("_Smoothness", .05f);
        _material.SetFloat("_Cull", 0f);
        _material.color = new Color(.35f, .035f, .025f);
        if (texture != null) { _material.mainTexture = texture; _material.color = Color.white; }
    }

    public void Add(Vector3 position, float radius, double gameSeconds)
    {
        if (!_ground.TryGround(position, Vector3.down, .012f, out var ground)) return;
        if (_stains.Count >= 256) Remove(0);
        var stamp = new SurfaceEditStamp { kind = SurfaceEditStampCodec.BloodKind, direction = ground.Position,
            surfaceNormal = ground.Normal, radiusMeters = radius, strength = 1f, createdGameSeconds = gameSeconds,
            regrowSeconds = 7f * 86400f, strokeId = unchecked((int)_nextId), shape = "disc", operation = "paint" };
        // Exercise the same value record used by the world ledger; scene fixtures retain it in memory.
        SurfaceEditStampCodec.TryDecode(SurfaceEditStampCodec.Encode(_nextId++, stamp), out stamp);
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "Blood " + stamp.recordId;
        go.transform.SetParent(_root, false); UnityEngine.Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetPositionAndRotation(stamp.direction, Quaternion.LookRotation(-stamp.surfaceNormal) *
            Quaternion.AngleAxis((stamp.strokeId * 137) % 360, Vector3.forward));
        go.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
        go.GetComponent<Renderer>().sharedMaterial = _material; _stains.Add((stamp, go));
    }

    public void Tick(double gameSeconds)
    {
        for (int i = _stains.Count - 1; i >= 0; i--)
        {
            var (stamp, view) = _stains[i];
            float age = (float)Math.Max(0, gameSeconds - stamp.createdGameSeconds);
            if (age >= stamp.regrowSeconds) { Remove(i); continue; }
            _block.SetColor("_BaseColor", Color.Lerp(Color.white, new Color(.22f, .16f, .12f), Mathf.Clamp01(age / 86400f)));
            _block.SetFloat("_Cutoff", Mathf.Lerp(.12f, 1f, Mathf.InverseLerp(stamp.regrowSeconds * .75f, stamp.regrowSeconds, age)));
            view.GetComponent<Renderer>().SetPropertyBlock(_block);
        }
    }
    void Remove(int index)
    {
        var go = _stains[index].view; go.SetActive(false); UnityEngine.Object.Destroy(go); _stains.RemoveAt(index);
    }
    public void Dispose()
    {
        if (_root != null) { _root.gameObject.SetActive(false); UnityEngine.Object.Destroy(_root.gameObject); }
        UnityEngine.Object.Destroy(_material); _stains.Clear();
    }
}
