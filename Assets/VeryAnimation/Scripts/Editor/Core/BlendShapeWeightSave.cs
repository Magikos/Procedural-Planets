using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VeryAnimation
{
    internal sealed class BlendShapeWeightSave
    {
        private readonly GameObject rootObject;
        private readonly Dictionary<string, SkinnedMeshRenderer> renderers;

        private static Dictionary<string, int> CreateBlendShapeIndexTable(Mesh mesh)
        {
            var indexTable = new Dictionary<string, int>(mesh.blendShapeCount);
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                indexTable.TryAdd(mesh.GetBlendShapeName(i), i);
            }
            return indexTable;
        }

        private class SaveData
        {
            public Dictionary<string, float> values;
        }

        private readonly Dictionary<SkinnedMeshRenderer, SaveData> originalValues;
        private Dictionary<SkinnedMeshRenderer, SaveData> prefabValues;

        public BlendShapeWeightSave(GameObject gameObject)
        {
            rootObject = gameObject;

            renderers = new Dictionary<string, SkinnedMeshRenderer>();
            {
                originalValues = new Dictionary<SkinnedMeshRenderer, SaveData>();
                foreach (var renderer in rootObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.blendShapeCount == 0)
                        continue;
                    var save = new SaveData()
                    {
                        values = new Dictionary<string, float>(),
                    };
                    for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                    {
                        var name = renderer.sharedMesh.GetBlendShapeName(i);
                        save.values.TryAdd(name, renderer.GetBlendShapeWeight(i));
                    }
                    originalValues.Add(renderer, save);
                    var path = AnimationUtility.CalculateTransformPath(renderer.transform, rootObject.transform);
                    renderers.TryAdd(path, renderer);
                }
            }
        }
        public void CreateExtraValues()
        {
            {
                prefabValues = new Dictionary<SkinnedMeshRenderer, SaveData>();
                var prefab = PrefabUtility.GetCorrespondingObjectFromSource(rootObject) as GameObject;
                if (prefab != null)
                {
                    foreach (var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.blendShapeCount == 0)
                            continue;
                        var save = new SaveData()
                        {
                            values = new Dictionary<string, float>(),
                        };
                        for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                        {
                            var name = renderer.sharedMesh.GetBlendShapeName(i);
                            save.values.TryAdd(name, renderer.GetBlendShapeWeight(i));
                        }
                        var path = AnimationUtility.CalculateTransformPath(renderer.transform, prefab.transform);
                        if (renderers.TryGetValue(path, out SkinnedMeshRenderer originalRenderer))
                        {
                            prefabValues.TryAdd(originalRenderer, save);
                        }
                    }
                }
            }
        }

        public bool ResetDefaultWeight()
        {
            if (ResetPrefabWeight()) return true;
            if (ResetOriginalWeight()) return true;
            return false;
        }
        public float GetDefaultWeight(SkinnedMeshRenderer renderer, string name)
        {
            if (IsHavePrefabWeight(renderer, name))
                return GetPrefabWeight(renderer, name);
            else
                return GetOriginalWeight(renderer, name);
        }

        public bool IsEnableOriginalWeight()
        {
            return originalValues != null && originalValues.Count > 0;
        }
        public bool ResetOriginalWeight()
        {
            if (!IsEnableOriginalWeight())
                return false;
            foreach (var pair in originalValues)
            {
                if (pair.Key == null || pair.Key.sharedMesh == null) continue;
                var renderer = pair.Key;
                var mesh = renderer.sharedMesh;
                var blendShapeIndexTable = CreateBlendShapeIndexTable(mesh);
                foreach (var valuePair in pair.Value.values)
                {
                    if (!blendShapeIndexTable.TryGetValue(valuePair.Key, out int index)) continue;
                    if (renderer.GetBlendShapeWeight(index) != valuePair.Value)
                    {
                        renderer.SetBlendShapeWeight(index, valuePair.Value);
                    }
                }
            }
            return true;
        }
        public bool IsHaveOriginalWeight(SkinnedMeshRenderer renderer, string name)
        {
            if (!originalValues.TryGetValue(renderer, out var data))
                return false;
            return data.values.ContainsKey(name);
        }
        public float GetOriginalWeight(SkinnedMeshRenderer renderer, string name)
        {
            if (!originalValues.TryGetValue(renderer, out var data))
                return 0f;
            if (!data.values.TryGetValue(name, out float weight))
                return 0f;
            return weight;
        }
        public void ActionOriginalWeights(SkinnedMeshRenderer renderer, Action<string, float> action)
        {
            if (!originalValues.TryGetValue(renderer, out var data))
                return;
            foreach (var pair in data.values)
            {
                action(pair.Key, pair.Value);
            }
        }

        public bool IsEnablePrefabWeight()
        {
            return prefabValues != null && prefabValues.Count > 0;
        }
        public bool ResetPrefabWeight()
        {
            if (!IsEnablePrefabWeight())
                return false;
            foreach (var pair in prefabValues)
            {
                if (pair.Key == null || pair.Key.sharedMesh == null)
                    continue;
                var mesh = pair.Key.sharedMesh;
                var blendShapeIndexTable = CreateBlendShapeIndexTable(mesh);
                foreach (var valuePair in pair.Value.values)
                {
                    if (!blendShapeIndexTable.TryGetValue(valuePair.Key, out int index))
                        continue;
                    if (pair.Key.GetBlendShapeWeight(index) != valuePair.Value)
                    {
                        pair.Key.SetBlendShapeWeight(index, valuePair.Value);
                    }
                }
            }
            return true;
        }
        public bool IsHavePrefabWeight(SkinnedMeshRenderer renderer, string name)
        {
            if (!IsEnablePrefabWeight())
                return false;
            if (!prefabValues.TryGetValue(renderer, out var data))
                return false;
            return data.values.ContainsKey(name);
        }
        public float GetPrefabWeight(SkinnedMeshRenderer renderer, string name)
        {
            if (!IsEnablePrefabWeight())
                return 0f;
            if (!prefabValues.TryGetValue(renderer, out var data))
                return 0f;
            if (!data.values.TryGetValue(name, out float weight))
                return 0f;
            return weight;
        }
    }
}
