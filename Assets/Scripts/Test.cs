using UnityEngine;
using System.Collections.Generic;
using Shapes;

public class Test : MonoBehaviour
{
    [SerializeField] private float radius = 1f;
    [SerializeField] private float displayRadius = 1f;
    [SerializeField] private Vector2 sampleRegionSize = new Vector2(10f, 10f);
    [SerializeField] private int maxAttempts = 30;
    private List<Vector2> _points;

    private void OnValidate()
    {
        _points = PoissonDiscSampling.GeneratePoints(radius, sampleRegionSize, maxAttempts);
    }

    private void OnDrawGizmos()
    {
        // Draw rectangle outline
        Draw.LineGeometry = LineGeometry.Flat2D;
        Draw.Thickness = 0.1f;
        Draw.Color = Color.white;
        Draw.Rectangle(
            new Vector3(sampleRegionSize.x * 0.5f, sampleRegionSize.y * 0.5f, 0),
            sampleRegionSize
        );

        if (_points != null)
        {
            Draw.Color = Color.cyan;
            foreach (Vector2 point in _points)
            {
                Draw.Disc(new Vector3(point.x, point.y, 0.01f), Vector3.forward, displayRadius);
            }
        }
    }
}