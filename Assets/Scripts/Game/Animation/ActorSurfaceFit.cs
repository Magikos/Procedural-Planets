using UnityEngine;

/// <summary>One-time skin clearance after a pose freezes. Never runs as a per-frame skin bake.</summary>
public static class ActorSurfaceFit
{
    public static void ClearMeshPenetration(Transform frame, Transform visualBody, IGroundingProvider ground, Vector3 up)
    {
        if (frame == null || visualBody == null || ground == null || !CharacterMath.IsFinite(up) || up.sqrMagnitude < .5f) return;
        up.Normalize();
        float lift = float.NegativeInfinity;
        var mesh = new Mesh();
        var vertices = new System.Collections.Generic.List<Vector3>();
        try
        {
            foreach (var renderer in frame.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.BakeMesh(mesh, true);
                mesh.GetVertices(vertices);
                // Bound analytic queries per renderer. Rig contacts handle joints; these samples cover flesh thickness.
                int stride = Mathf.Max(1, Mathf.CeilToInt(vertices.Count / 128f));
                Vector3 lowest = default;
                float lowestHeight = float.PositiveInfinity;
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 point = renderer.transform.TransformPoint(vertex);
                    float height = Vector3.Dot(point, up);
                    if (height < lowestHeight) { lowestHeight = height; lowest = point; }
                }
                // Always include the skin's true lowest point, even when it falls between bounded samples.
                if (float.IsFinite(lowestHeight) && ground.TryGround(lowest, -up, .005f, out var lowestHit) && CharacterMath.IsFinite(lowestHit.Position))
                    lift = Mathf.Max(lift, Vector3.Dot(lowestHit.Position - lowest, up));
                for (int i = 0; i < vertices.Count; i += stride)
                {
                    Vector3 point = renderer.transform.TransformPoint(vertices[i]);
                    if (ground.TryGround(point, -up, .005f, out var hit) && CharacterMath.IsFinite(hit.Position))
                        lift = Mathf.Max(lift, Vector3.Dot(hit.Position - point, up));
                }
            }
            if (float.IsFinite(lift)) visualBody.position += up * lift;
        }
        finally
        {
            if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh);
        }
    }
}
