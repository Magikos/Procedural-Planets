using UnityEngine;

[CreateAssetMenu(menuName = "Planet/LOD Settings")]
public class LODSettings : ScriptableObject
{
    public int maxLOD = 5; // Levels: 0 (lowest res) to max
    public float[] lodDistances = { 0f, 0.5f, 1f, 2f, 4f, 8f }; // Distance thresholds per level (normalize by planet radius)
}