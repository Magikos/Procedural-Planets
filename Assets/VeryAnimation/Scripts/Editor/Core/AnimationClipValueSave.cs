using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class AnimationClipValueSave : IDisposable
    {
        private GameObject rootObject;

        private EditorCurveBinding[] bindings;
        private float?[] floatValues;

        private EditorCurveBinding[] refBindings;
        private UnityEngine.Object[] refValues;
        private bool[] refHasValues;

        private AnimationClip animationClip;

        public AnimationClipValueSave(GameObject gameObject, AnimationClip clip, AnimationClip[] layerClips = null)
        {
            Save(gameObject, clip, layerClips);
        }

        public void Save(GameObject gameObject, AnimationClip clip, AnimationClip[] layerClips = null)
        {
            Dispose();

            this.rootObject = gameObject;

            animationClip = new AnimationClip() { name = clip.name };
            animationClip.hideFlags |= HideFlags.HideAndDontSave;
            animationClip.legacy = clip.legacy;

            {
                HashSet<EditorCurveBinding> bindingSet = new(AnimationUtility.GetCurveBindings(clip));
                if (layerClips != null)
                {
                    foreach (var layerClip in layerClips)
                    {
                        if (layerClip == null)
                            continue;
                        foreach (var binding in AnimationUtility.GetCurveBindings(layerClip))
                        {
                            bindingSet.Add(binding);
                        }
                    }
                }
                bindings = bindingSet.ToArray();
            }
            floatValues = new float?[bindings.Length];
            {
                var fDatas = new Dictionary<EditorCurveBinding, AnimationCurve>(bindings.Length);
                for (int i = 0; i < bindings.Length; i++)
                {
                    if (AnimationUtility.GetFloatValue(rootObject, bindings[i], out float floatValue))
                    {
                        floatValues[i] = floatValue;
                        fDatas.Add(bindings[i], new AnimationCurve(new Keyframe[] { new(0f, floatValue) }));
                    }
                }
                AnimationCommon.SetEditorCurves(animationClip, fDatas);
            }

            {
                HashSet<EditorCurveBinding> bindingSet = new(AnimationUtility.GetObjectReferenceCurveBindings(clip));
                if (layerClips != null)
                {
                    foreach (var layerClip in layerClips)
                    {
                        if (layerClip == null)
                            continue;
                        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(layerClip))
                        {
                            bindingSet.Add(binding);
                        }
                    }
                }
                refBindings = bindingSet.ToArray();
            }
            refValues = new UnityEngine.Object[refBindings.Length];
            refHasValues = new bool[refBindings.Length];
            {
                var rDatas = new Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]>(refBindings.Length);
                for (int i = 0; i < refBindings.Length; i++)
                {
                    if (AnimationUtility.GetObjectReferenceValue(rootObject, refBindings[i], out UnityEngine.Object refValue))
                    {
                        refValues[i] = refValue;
                        refHasValues[i] = true;
                        rDatas.Add(refBindings[i], new ObjectReferenceKeyframe[] { new() { time = 0f, value = refValue } });
                    }
                }
                AnimationCommon.SetObjectReferenceCurves(animationClip, rDatas);
            }
        }

        public void AddBindings(AnimationClip clip)
        {
            if (rootObject == null || animationClip == null || clip == null)
                return;

            {
                var bindingSet = new HashSet<EditorCurveBinding>(bindings);
                List<EditorCurveBinding> addBindings = null;
                List<float?> addFloatValues = null;
                Dictionary<EditorCurveBinding, AnimationCurve> fDatas = null;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!bindingSet.Add(binding))
                        continue;
                    addBindings ??= new List<EditorCurveBinding>();
                    addFloatValues ??= new List<float?>();
                    addBindings.Add(binding);
                    if (AnimationUtility.GetFloatValue(rootObject, binding, out float floatValue))
                    {
                        addFloatValues.Add(floatValue);
                        fDatas ??= new Dictionary<EditorCurveBinding, AnimationCurve>();
                        fDatas.Add(binding, new AnimationCurve(new Keyframe[] { new(0f, floatValue) }));
                    }
                    else
                    {
                        addFloatValues.Add(null);
                    }
                }
                if (addBindings != null)
                {
                    bindings = bindings.Concat(addBindings).ToArray();
                    floatValues = floatValues.Concat(addFloatValues).ToArray();
                    AnimationCommon.SetEditorCurves(animationClip, fDatas);
                }
            }

            {
                var bindingSet = new HashSet<EditorCurveBinding>(refBindings);
                List<EditorCurveBinding> addBindings = null;
                List<UnityEngine.Object> addRefValues = null;
                List<bool> addRefHasValues = null;
                Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]> rDatas = null;
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    if (!bindingSet.Add(binding))
                        continue;
                    addBindings ??= new List<EditorCurveBinding>();
                    addRefValues ??= new List<UnityEngine.Object>();
                    addRefHasValues ??= new List<bool>();
                    addBindings.Add(binding);
                    if (AnimationUtility.GetObjectReferenceValue(rootObject, binding, out UnityEngine.Object refValue))
                    {
                        addRefValues.Add(refValue);
                        addRefHasValues.Add(true);
                        rDatas ??= new Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]>();
                        rDatas.Add(binding, new ObjectReferenceKeyframe[] { new() { time = 0f, value = refValue } });
                    }
                    else
                    {
                        addRefValues.Add(null);
                        addRefHasValues.Add(false);
                    }
                }
                if (addBindings != null)
                {
                    refBindings = refBindings.Concat(addBindings).ToArray();
                    refValues = refValues.Concat(addRefValues).ToArray();
                    refHasValues = refHasValues.Concat(addRefHasValues).ToArray();
                    AnimationCommon.SetObjectReferenceCurves(animationClip, rDatas);
                }
            }
        }

        public void Dispose()
        {
            if (animationClip != null)
            {
                AnimationClip.DestroyImmediate(animationClip);
                animationClip = null;
            }
        }

        public void Load()
        {
            if (rootObject == null)
                return;

            if (animationClip != null)
            {
                animationClip.SampleAnimation(rootObject, 0f);
            }
        }

        public void LoadProperty()
        {
            if (rootObject == null)
                return;

            Load();

            Transform GetBindingTransform(Dictionary<string, Transform> transformCache, string path)
            {
                if (string.IsNullOrEmpty(path))
                    return rootObject.transform;

                if (!transformCache.TryGetValue(path, out var t))
                {
                    t = rootObject.transform.Find(path);
                    transformCache.Add(path, t);
                }
                return t;
            }
            static Component GetBindingComponent(Dictionary<Transform, Dictionary<Type, Component>> componentCache, Transform transform, Type type)
            {
                if (transform == null)
                    return null;

                if (!componentCache.TryGetValue(transform, out var components))
                {
                    components = new Dictionary<Type, Component>();
                    componentCache.Add(transform, components);
                }
                if (!components.TryGetValue(type, out var component))
                {
                    if (!transform.TryGetComponent(type, out component))
                        component = null;
                    components.Add(type, component);
                }
                return component;
            }
            static SerializedObject GetSerializedObject(Dictionary<Component, SerializedObject> serializedObjectCache, Component component)
            {
                if (!serializedObjectCache.TryGetValue(component, out var so))
                {
                    so = new SerializedObject(component);
                    serializedObjectCache.Add(component, so);
                }
                return so;
            }

            var transformCache = new Dictionary<string, Transform>(StringComparer.Ordinal);
            var componentCache = new Dictionary<Transform, Dictionary<Type, Component>>();
            var serializedObjectCache = new Dictionary<Component, SerializedObject>();
            var modifiedComponents = new HashSet<Component>();

            if (bindings != null)
            {
                for (int i = 0; i < bindings.Length; i++)
                {
                    if (!floatValues[i].HasValue)
                        continue;

                    var t = GetBindingTransform(transformCache, bindings[i].path);
                    var component = GetBindingComponent(componentCache, t, bindings[i].type);
                    if (component == null)
                        continue;

                    var so = GetSerializedObject(serializedObjectCache, component);
                    var sp = so.FindProperty(bindings[i].propertyName);
                    if (sp == null)
                    {
                        //Debug.LogWarning($"<color=blue>[Very Animation]</color>Property not found: {bindings[i].propertyName} on {comp.GetType().Name}");
                        continue;
                    }

                    var type = AnimationUtility.GetEditorCurveValueType(rootObject, bindings[i]);
                    if (type == typeof(float))
                    {
                        sp.floatValue = floatValues[i].Value;
                    }
                    else if (type == typeof(int))
                    {
                        sp.intValue = (int)floatValues[i].Value;
                    }
                    else if (type == typeof(bool))
                    {
                        sp.boolValue = floatValues[i].Value != 0f;
                    }
                    else
                    {
                        Assert.IsTrue(false);
                        continue;
                    }

                    modifiedComponents.Add(component);
                }
            }

            if (refBindings != null)
            {
                for (int i = 0; i < refBindings.Length; i++)
                {
                    if (!refHasValues[i])
                        continue;

                    var t = GetBindingTransform(transformCache, refBindings[i].path);
                    var component = GetBindingComponent(componentCache, t, refBindings[i].type);
                    if (component == null)
                        continue;

                    var so = GetSerializedObject(serializedObjectCache, component);
                    var sp = so.FindProperty(refBindings[i].propertyName);
                    if (sp == null)
                    {
                        //Debug.LogWarning($"<color=blue>[Very Animation]</color>Property not found: {refBindings[i].propertyName} on {comp.GetType().Name}");
                        continue;
                    }

                    sp.objectReferenceValue = refValues[i];

                    modifiedComponents.Add(component);
                }
            }

            foreach (var component in modifiedComponents)
            {
                serializedObjectCache[component].ApplyModifiedProperties();
            }
        }
    }
}
