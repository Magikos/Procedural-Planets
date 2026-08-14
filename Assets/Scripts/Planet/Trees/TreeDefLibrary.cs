using UnityEngine;

// Code-first tree definitions (plan 006). One TreeDef per species; age is a parameter. This starts with a
// single sample broadleaf for T1 previewing; the real per-biome species set (types x ages) grows here later.
public static class TreeDefLibrary
{
    public static TreeDef SampleBroadleaf(float age = 1f) => new TreeDef
    {
        Name = "Broadleaf",
        GlobalScale = 1f,
        Age = Mathf.Clamp01(age),
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(6f, 8f), RadialSides = 6, Curve = 6f, Noise = 0.04f },
            new LevelRule
            {
                Label = "primary", ParentLevel = 0, Frequency = new Vector2(4, 6), ChildrenPerNode = 1,
                Range = new Vector2(0.35f, 0.92f), ParallelAlign = 0.5f, GravityAlign = new Vector2(0.35f, -0.12f),
                Length = new Vector2(3.2f, 1.6f), GirthScale = 0.5f, RadialSides = 4, Curve = 12f, Noise = 0.08f,
            },
            new LevelRule
            {
                Label = "secondary", ParentLevel = 1, Frequency = new Vector2(3, 5), ChildrenPerNode = 1,
                Range = new Vector2(0.3f, 0.95f), ParallelAlign = 0.55f, GravityAlign = new Vector2(0.3f, -0.05f),
                Length = new Vector2(1.5f, 0.8f), GirthScale = 0.5f, RadialSides = 3, Curve = 14f, Noise = 0.1f,
            },
            new LevelRule
            {
                Label = "leaves", ParentLevel = 2, IsLeaf = true, Frequency = new Vector2(4, 7),
                Range = new Vector2(0.4f, 1f), LeafSize = 0.6f, LeafGroup = 0,
            },
        },
    };
}
