using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VeryAnimation
{
    internal sealed class TransformPoseSave
    {
        public GameObject RootObject { get; private set; }
        public Vector3 StartPosition { get; private set; }
        public Quaternion StartRotation { get; private set; }
        public Vector3 StartScale { get; private set; }
        public Vector3 StartLocalPosition { get; private set; }
        public Quaternion StartLocalRotation { get; private set; }
        public Vector3 StartLocalScale { get; private set; }
        public Vector3 OriginalPosition { get; private set; }
        public Quaternion OriginalRotation { get; private set; }
        public Vector3 OriginalScale { get; private set; }
        public Vector3 OriginalLocalPosition { get; private set; }
        public Quaternion OriginalLocalRotation { get; private set; }
        public Vector3 OriginalLocalScale { get; private set; }

        public Matrix4x4 StartMatrix => Matrix4x4.TRS(StartPosition, StartRotation, StartScale);
        public Matrix4x4 OriginalMatrix => Matrix4x4.TRS(OriginalPosition, OriginalRotation, OriginalScale);

        public class SaveData
        {
            public SaveData()
            {
            }
            public SaveData(Transform t)
            {
                Save(t);
            }
            public void Save(Transform t)
            {
                localPosition = t.localPosition;
                localRotation = t.localRotation;
                localScale = t.localScale;
                position = t.position;
                rotation = t.rotation;
                scale = t.lossyScale;
            }
            public void LoadLocal(Transform t)
            {
                if (t.localPosition != localPosition ||
                    t.localRotation != localRotation)
                {
                    t.SetLocalPositionAndRotation(localPosition, localRotation);
                }
                if (t.localScale != localScale)
                    t.localScale = localScale;
            }
            public void LoadWorld(Transform t)
            {
                t.SetPositionAndRotation(position, rotation);
            }
            public Matrix4x4 LocalMatrix => Matrix4x4.TRS(localPosition, localRotation, localScale);
            public Matrix4x4 Matrix => Matrix4x4.TRS(position, rotation, scale);

            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }
        private readonly Dictionary<Transform, SaveData> originalTransforms;
        private Dictionary<Transform, SaveData> bindTransforms;
        private Dictionary<Transform, SaveData> tposeTransforms;
        private Dictionary<Transform, SaveData> prefabTransforms;
        private Dictionary<Transform, SaveData> humanDescriptionTransforms;

        public TransformPoseSave(GameObject gameObject)
        {
            RootObject = gameObject;
            StartPosition = OriginalPosition = gameObject.transform.position;
            StartRotation = OriginalRotation = gameObject.transform.rotation;
            StartScale = OriginalScale = gameObject.transform.lossyScale;
            StartLocalPosition = OriginalLocalPosition = gameObject.transform.localPosition;
            StartLocalRotation = OriginalLocalRotation = gameObject.transform.localRotation;
            StartLocalScale = OriginalLocalScale = gameObject.transform.localScale;
            #region originalTransforms
            {
                originalTransforms = new Dictionary<Transform, SaveData>();
                void SaveTransform(Transform t, Transform root)
                {
                    originalTransforms.TryAdd(t, new SaveData(t));
                    for (int i = 0; i < t.childCount; i++)
                        SaveTransform(t.GetChild(i), root);
                }

                SaveTransform(gameObject.transform, gameObject.transform);
            }
            #endregion
        }
        public void CreateExtraTransforms()
        {
            #region saveTransforms
            {
                var bindPathTransforms = new Dictionary<string, SaveData>();
                var tposePathTransforms = new Dictionary<string, SaveData>();
                var prefabPathTransforms = new Dictionary<string, SaveData>();
                var humanDescriptionPathTransforms = new Dictionary<string, SaveData>();
                {
                    var uAvatarSetupTool = new UAvatarSetupTool();

                    static void SaveTransform(Dictionary<string, SaveData> transforms, Transform t, Transform root)
                    {
                        var path = AnimationUtility.CalculateTransformPath(t, root);
                        if (!transforms.ContainsKey(path))
                            transforms.Add(path, new SaveData(t));
                        for (int i = 0; i < t.childCount; i++)
                            SaveTransform(transforms, t.GetChild(i), root);
                    }

                    {
                        #region BindPose
                        if (RootObject.GetComponentInChildren<SkinnedMeshRenderer>() != null)
                        {
                            var goTmp = AnimationCommon.InstantiateForPreview(RootObject);
                            try
                            {
                                if (uAvatarSetupTool.SampleBindPose(goTmp))
                                {
                                    var rootT = goTmp.transform;
                                    #region Root
                                    rootT.SetLocalPositionAndRotation(RootObject.transform.localPosition, RootObject.transform.localRotation);
                                    rootT.localScale = RootObject.transform.localScale;
                                    #endregion
                                    SaveTransform(bindPathTransforms, rootT, rootT);
                                }
                            }
                            finally
                            {
                                GameObject.DestroyImmediate(goTmp);
                            }
                        }
                        #endregion
                        #region TPose
                        {
                            var animator = RootObject.GetComponent<Animator>();
                            if (animator != null && animator.isHuman && animator.avatar != null)
                            {
                                var goTmp = AnimationCommon.InstantiateForPreview(RootObject);
                                try
                                {
                                    if (uAvatarSetupTool.SampleBindPose(goTmp) &&   //Reset
                                        uAvatarSetupTool.SampleTPose(goTmp))
                                    {
                                        var rootT = goTmp.transform;
                                        #region Root
                                        rootT.SetLocalPositionAndRotation(RootObject.transform.localPosition, RootObject.transform.localRotation);
                                        rootT.localScale = RootObject.transform.localScale;
                                        #endregion
                                        SaveTransform(tposePathTransforms, rootT, rootT);
                                    }
                                    if (uAvatarSetupTool.SampleBindPose(goTmp))   //Reset
                                    {
                                        var hd = animator.avatar.humanDescription;
                                        var transforms = goTmp.GetComponentsInChildren<Transform>(true);
                                        var transformNameTable = new Dictionary<string, Transform>(transforms.Length);
                                        foreach (var transform in transforms)
                                        {
                                            if (transform == null)
                                                continue;
                                            transformNameTable.TryAdd(transform.name, transform);
                                        }
                                        for (int i = 0; i < hd.skeleton.Length; i++)
                                        {
                                            if (!transformNameTable.TryGetValue(hd.skeleton[i].name, out var t))
                                                continue;
                                            t.SetLocalPositionAndRotation(hd.skeleton[i].position, hd.skeleton[i].rotation);
                                            t.localScale = hd.skeleton[i].scale;
                                        }
                                        var rootT = goTmp.transform;
                                        #region Root
                                        rootT.SetLocalPositionAndRotation(RootObject.transform.localPosition, RootObject.transform.localRotation);
                                        rootT.localScale = RootObject.transform.localScale;
                                        #endregion
                                        SaveTransform(humanDescriptionPathTransforms, rootT, rootT);
                                    }
                                }
                                finally
                                {
                                    GameObject.DestroyImmediate(goTmp);
                                }
                            }
                        }
                        #endregion
                        #region Prefab
                        {
                            var prefab = PrefabUtility.GetCorrespondingObjectFromSource(RootObject) as GameObject;
                            if (prefab != null)
                            {
                                var goTmp = AnimationCommon.InstantiateForPreview(prefab);
                                try
                                {
                                    #region PrefabPose
                                    {  //Root
                                        goTmp.transform.SetLocalPositionAndRotation(RootObject.transform.localPosition, RootObject.transform.localRotation);
                                        goTmp.transform.localScale = RootObject.transform.localScale;
                                    }
                                    SaveTransform(prefabPathTransforms, goTmp.transform, goTmp.transform);
                                    #endregion
                                }
                                finally
                                {
                                    GameObject.DestroyImmediate(goTmp);
                                }
                            }
                        }
                        #endregion
                    }
                }
                bindTransforms = Paths2Transforms(bindPathTransforms, RootObject.transform);
                tposeTransforms = Paths2Transforms(tposePathTransforms, RootObject.transform);
                prefabTransforms = Paths2Transforms(prefabPathTransforms, RootObject.transform);
                humanDescriptionTransforms = Paths2Transforms(humanDescriptionPathTransforms, RootObject.transform);
            }
            #endregion
        }

        public void ChangeStartTransform()
        {
            var transform = RootObject.transform;
            StartPosition = transform.position;
            StartRotation = transform.rotation;
            StartScale = transform.lossyScale;
            StartLocalPosition = transform.localPosition;
            StartLocalRotation = transform.localRotation;
            StartLocalScale = transform.localScale;
            ChangeTransform(transform);
        }
        public void ChangeTransform(Transform transform)
        {
            static void SetTransform(Dictionary<Transform, SaveData> list, Transform t)
            {
                if (list == null)
                    return;
                if (!list.TryGetValue(t, out SaveData save))
                    return;
                save.Save(t);
            }
            SetTransform(originalTransforms, transform);
        }
        public void ChangeTransformReference(GameObject gameObject)
        {
            var transformPathMap = new Dictionary<string, Transform>(originalTransforms.Count);
            foreach (var pair in originalTransforms)
            {
                var path = AnimationUtility.CalculateTransformPath(pair.Key, RootObject.transform);
                transformPathMap.TryAdd(path, pair.Key);
            }

            void SaveTransform(Transform t, Transform root)
            {
                var path = AnimationUtility.CalculateTransformPath(t, root);
                if (transformPathMap.TryGetValue(path, out var oldTransform))
                {
                    static void ChangeTransform(Dictionary<Transform, SaveData> list, Transform oldT, Transform newT)
                    {
                        if (list != null && list.Count > 0)
                        {
                            if (list.TryGetValue(oldT, out SaveData saveData))
                            {
                                list.Remove(oldT);
                                list.Add(newT, saveData);
                            }
                        }
                    }
                    ChangeTransform(originalTransforms, oldTransform, t);
                    ChangeTransform(bindTransforms, oldTransform, t);
                    ChangeTransform(tposeTransforms, oldTransform, t);
                    ChangeTransform(prefabTransforms, oldTransform, t);
                    ChangeTransform(humanDescriptionTransforms, oldTransform, t);
                }
                for (int i = 0; i < t.childCount; i++)
                    SaveTransform(t.GetChild(i), root);
            }

            SaveTransform(gameObject.transform, gameObject.transform);
            RootObject = gameObject;
        }

        public bool IsRootStartTransform()
        {
            if (RootObject != null)
            {
                var t = RootObject.transform;
                if (t.position == StartPosition &&
                    t.rotation == StartRotation)
                {
                    return true;
                }
            }
            return false;
        }
        public void ResetRootStartTransform()
        {
            if (RootObject != null)
            {
                RootObject.transform.SetPositionAndRotation(StartPosition, StartRotation);
            }
        }
        public void ResetRootOriginalTransform()
        {
            if (RootObject != null)
            {
                RootObject.transform.SetPositionAndRotation(OriginalPosition, OriginalRotation);
            }
        }

        public bool ResetDefaultTransform()
        {
            if (ResetBindTransform()) return true;
            if (ResetPrefabTransform()) return true;
            if (ResetOriginalTransform()) return true;
            return false;
        }

        private static bool IsEnableTransforms(Dictionary<Transform, SaveData> transforms)
        {
            return transforms != null && transforms.Count > 0;
        }
        private static bool ResetTransforms(Dictionary<Transform, SaveData> transforms)
        {
            if (!IsEnableTransforms(transforms))
                return false;
            foreach (var trans in transforms)
            {
                if (trans.Key != null)
                    trans.Value.LoadLocal(trans.Key);
            }
            return true;
        }
        private static SaveData GetTransformSaveData(Dictionary<Transform, SaveData> transforms, Transform t)
        {
            if (!IsEnableTransforms(transforms))
                return null;
            if (transforms.TryGetValue(t, out SaveData data))
                return data;
            return null;
        }

        public bool IsEnableOriginalTransform() => IsEnableTransforms(originalTransforms);
        public bool ResetOriginalTransform() => ResetTransforms(originalTransforms);
        public SaveData GetOriginalTransform(Transform t) => GetTransformSaveData(originalTransforms, t);

        public bool IsEnableBindTransform() => IsEnableTransforms(bindTransforms);
        public bool ResetBindTransform() => ResetTransforms(bindTransforms);
        public SaveData GetBindTransform(Transform t) => GetTransformSaveData(bindTransforms, t);

        public bool IsEnableTPoseTransform() => IsEnableTransforms(tposeTransforms);
        public bool ResetTPoseTransform() => ResetTransforms(tposeTransforms);
        public SaveData GetTPoseTransform(Transform t) => GetTransformSaveData(tposeTransforms, t);

        public bool IsEnablePrefabTransform() => IsEnableTransforms(prefabTransforms);
        public bool ResetPrefabTransform() => ResetTransforms(prefabTransforms);
        public SaveData GetPrefabTransform(Transform t) => GetTransformSaveData(prefabTransforms, t);

        public bool IsEnableHumanDescriptionTransforms() => IsEnableTransforms(humanDescriptionTransforms);
        public bool ResetHumanDescriptionTransforms() => ResetTransforms(humanDescriptionTransforms);
        public SaveData GetHumanDescriptionTransforms(Transform t) => GetTransformSaveData(humanDescriptionTransforms, t);

        private Dictionary<Transform, SaveData> Paths2Transforms(Dictionary<string, SaveData> src, Transform transform)
        {
            var dst = new Dictionary<Transform, SaveData>(src.Count);
            void SaveTransform(Transform t, Transform root)
            {
                var path = AnimationUtility.CalculateTransformPath(t, root);
                if (src.TryGetValue(path, out var saveData))
                    dst.Add(t, saveData);
                for (int i = 0; i < t.childCount; i++)
                    SaveTransform(t.GetChild(i), root);
            }

            SaveTransform(transform, transform);
            return dst;
        }
    }
}
