using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Scatter Library", fileName = "ScatterLibrary")]
public sealed class ScatterLibrary : ScriptableObject
{
    public ScatterPrototype[] Prototypes = System.Array.Empty<ScatterPrototype>();
}
