using UnityEngine;
using System.Collections.Generic;

public class Test : MonoBehaviour
{
    [SerializeField] private float radius = 1f;
    [SerializeField] private float displayRadius = 0.3f;
    [SerializeField] private Vector2 sampleRegionSize = new Vector2(10f, 10f);
    [SerializeField] private int maxAttempts = 30;
    [SerializeField] private int seed = 12345;
    private List<Vector2> _points;

    public void Generate()
    {
        _points = PoissonDiscSampling.GeneratePoints(radius, sampleRegionSize, maxAttempts, seed);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(
            new Vector3(sampleRegionSize.x * 0.5f, sampleRegionSize.y * 0.5f, 0),
            new Vector3(sampleRegionSize.x, sampleRegionSize.y, 0));

        if (_points != null)
        {
            Gizmos.color = Color.cyan;
            foreach (Vector2 point in _points)
            {
                Gizmos.DrawSphere(new Vector3(point.x, point.y, 0), displayRadius);
            }
        }
    }
}
