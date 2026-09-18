using UnityEngine;
using UnityEngine.Assertions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Compilation;
using UnityEditor.ShortcutManagement;
using UnityEditorInternal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#if VERYANIMATION_FBX
using UnityEditor.Formats.Fbx.Exporter;
#endif

namespace VeryAnimation
{
    [Serializable]
    internal sealed class VeryAnimationRecorderWindow : EditorWindow
    {
        public static VeryAnimationRecorderWindow Instance { get; private set; }

        [MenuItem("Window/Very Animation/Recorder", priority = 1002)]
        public static void Open()
        {
            GetWindow<VeryAnimationRecorderWindow>();
        }

        [Shortcut("Very Animation/Recorder Toggle")]
        public static void RecordToggle()
        {
            if (Instance != null && Instance.rootObject != null &&
                Instance.playModeStateChange != PlayModeStateChange.ExitingEditMode &&
                Instance.playModeStateChange != PlayModeStateChange.ExitingPlayMode)
            {
                if (!Instance.recording)
                {
                    Instance.RecordStart();
                }
                else
                {
                    Instance.RecordEnd();
                }
            }
        }

        #region EditorPrefs Keys
        private const string PrefKey_AnimationCompression = "VeryAnimation_Recorder_AnimationCompression";
        private const string PrefKey_KeyframeReduction_RotationError = "VeryAnimation_Recorder_KeyframeReduction_RotationError";
        private const string PrefKey_KeyframeReduction_PositionError = "VeryAnimation_Recorder_KeyframeReduction_PositionError";
        private const string PrefKey_KeyframeReduction_ScaleError = "VeryAnimation_Recorder_KeyframeReduction_ScaleError";
        private const string PrefKey_KeyframeReduction_EnableAnimator = "VeryAnimation_Recorder_KeyframeReduction_EnableAnimator";
        private const string PrefKey_KeyframeReduction_EnableAnimatorRootAndIKGoal = "VeryAnimation_Recorder_KeyframeReduction_EnableAnimatorRootAndIKGoal";
        private const string PrefKey_KeyframeReduction_EnableTransform = "VeryAnimation_Recorder_KeyframeReduction_EnableTransform";
        private const string PrefKey_KeyframeReduction_EnableOther = "VeryAnimation_Recorder_KeyframeReduction_EnableOther";
        private const string PrefKey_FBX_ExportFormat = "VeryAnimation_Recorder_FBX_ExportFormat";
        private const string PrefKey_FBX_ModelAnimIncludeOption = "VeryAnimation_Recorder_FBX_ModelAnimIncludeOption";
        private const string PrefKey_FBX_LODExportType = "VeryAnimation_Recorder_FBX_LODExportType";
        private const string PrefKey_FBX_ObjectPosition = "VeryAnimation_Recorder_FBX_ObjectPosition";
        private const string PrefKey_FBX_AnimateSkinnedMesh = "VeryAnimation_Recorder_FBX_AnimateSkinnedMesh";
        private const string PrefKey_FBX_UseMayaCompatibleNames = "VeryAnimation_Recorder_FBX_UseMayaCompatibleNames";
        private const string PrefKey_FBX_ExportUnrendered = "VeryAnimation_Recorder_FBX_ExportUnrendered";
        private const string PrefKey_FBX_PreserveImportSettings = "VeryAnimation_Recorder_FBX_PreserveImportSettings";
        private const string PrefKey_FBX_KeepInstances = "VeryAnimation_Recorder_FBX_KeepInstances";
        private const string PrefKey_FBX_EmbedTextures = "VeryAnimation_Recorder_FBX_EmbedTextures";
        #endregion

        #region RecordSettings
        [SerializeField]
        private GameObject rootObject;
        [SerializeField]
        private int clipFrameRate = 30;
        [SerializeField]
        private string[] recordTargets = new string[] 
        {
            typeof(GameObject).FullName, 
            typeof(Transform).FullName, 
        };
        [SerializeField]
        private AvatarMask recordAvatarMask;
        [SerializeField]
        private bool unchangingCurves;
        [SerializeField]
        private bool useDuration;
        [SerializeField]
        private float durationTime = 3f;
        #endregion

        #region ExportSettings
        private enum AnimationCompression
        {
            Off,
            KeyframeReduction,
            KeyframeReductionAndCompression,
        }
        [SerializeField]
        private AnimationCompression animationCompression = AnimationCompression.KeyframeReduction;
        [SerializeField]
        private float keyframeReduction_RotationError = 0.5f;
        [SerializeField]
        private float keyframeReduction_PositionError = 0.5f;
        [SerializeField]
        private float keyframeReduction_ScaleError = 0.5f;
        [SerializeField]
        private bool keyframeReduction_EnableAnimator = true;
        [SerializeField]
        private bool keyframeReduction_EnableAnimatorRootAndIKGoal = true;
        [SerializeField]
        private bool keyframeReduction_EnableTransform = true;
        [SerializeField]
        private bool keyframeReduction_EnableOther = true;

#if VERYANIMATION_FBX_5
        [SerializeField]
        private ExportModelOptions exportModelOptions = new();
#endif
        #endregion

        #region GUIStyles
        class GUIStyles
        {
            public readonly GUIContent guiContentRecord;
            public readonly GUIContent guiContentSettings;
            public readonly GUIContent guiContentSamplesFPS = new("Samples (FPS)", "Frame rate");

            public GUIStyles()
            {
                guiContentRecord = EditorGUIUtility.TrIconContent("Animation.Record", "");
                UEditorGUI uEditorGUI = new();
                guiContentSettings = uEditorGUI.GUIContents.GetTitleSettingsIcon();
            }
        }
        private static GUIStyles s_GUIStyles;
        private static GUIStyles Styles
        {
            get
            {
                s_GUIStyles ??= new GUIStyles();
                return s_GUIStyles;
            }
        }
        #endregion

        private bool settingsFoldout = true;

        private Animator animator;

        private GameObject lastRootObject;

        private bool settingsMode;

        private bool recording;
        private float recordTime;
        private int recordCount;
        private int recordFrameRate;
        private int lastTimeFrameCount;

        private double lastFrameEditorTime;
        private int frameDropCount;
        private AnimationClip recordClip;

        private UAnimationClipEditor uAnimationClipEditor;
        private UAvatarPreview uAvatarPreview;

#pragma warning disable IDE0044     // It should not be set to readonly because it will be reset upon reloading.
        private List<AnimationClip> historyClip = new();
#pragma warning restore IDE0044
        private string[] historyClipStrings = Array.Empty<string>();
        private int selectHistoryIndex = -1;

        private PlayModeStateChange playModeStateChange = PlayModeStateChange.EnteredEditMode;

        private readonly HashSet<GameObject> addedChangesPublished = new();
        private readonly HashSet<string> recordTargetSet = new(StringComparer.Ordinal);
        private readonly HashSet<EditorCurveBinding> animatableBindingsRefSet = new();
        private readonly HashSet<EditorCurveBinding> animatableBindingsFloatSet = new();

        private GameObject cachedRecordTargetComponentsRootObject;
        private class RecordTargetComponent
        {
            public Type type;
            public int animatableBindingCount;
            public GUIContent content;
        }
        private List<RecordTargetComponent> cachedRecordTargetComponents;
        private string cachedOptimizedAnimatorNames;
        private string cachedRecordTargetSamePathNames;
        private string cachedCulledAnimatorNames;
        private bool recordTargetComponentsDirty = true;

        private GameObject cachedRecordAvatarMaskRootObject;
        private AvatarMask cachedRecordAvatarMask;
        private Animator cachedRecordAvatarMaskAnimator;
        private HashSet<string> cachedInactiveRecordAvatarMaskPaths;

        private bool recordTargetsFoldout;
        private Vector2 recordTargetsScrollPosition;
        private List<string> recordTargetsSelected;
        private Vector2 settingsScrollPosition;

        private class AnimatableBindingRefData
        {
            public EditorCurveBinding binding;
            public bool needWrite;
            public UnityEngine.Object beforeValue;

            private readonly AnimationCommon.MiniObjectReferenceKeyframeList miniKeyframeList;

            public AnimatableBindingRefData(int capacity) => miniKeyframeList = new(capacity);

            public void SetKey(float time, UnityEngine.Object value) => miniKeyframeList.SetKey(time, value);

            public ObjectReferenceKeyframe[] CreateObjectReferenceKeyframes() => miniKeyframeList.CreateObjectReferenceKeyframes();
        }
        private List<AnimatableBindingRefData> animatableBindingsRefData;

        private class AnimatableBindingFloatData
        {
            public EditorCurveBinding binding;
            public bool needWrite;
            public Type valueType = typeof(float);
            public float beforeValue;

            private readonly AnimationCommon.MiniKeyframeList miniKeyframeList;

            public AnimatableBindingFloatData(int capacity) => miniKeyframeList = new(capacity);

            public void SetKey(float time, float value) => miniKeyframeList.SetKey(time, value);

            public AnimationCurve CreateAnimationCurve() => miniKeyframeList.CreateAnimationCurve();
        }
        private List<AnimatableBindingFloatData> animatableBindingsFloatData;

        private float trimFirstFrame;
        private float trimLastFrame;

        private bool? saveApplicationRunInBackground;

        private static readonly System.Random s_initialCapacityRandom = new();

        private bool ApplyRootMotion => animator != null && animator.applyRootMotion;

        private const string UndoVARecorderSettings = "Change VA Recorder Settings";

        private void OnEnable()
        {
            Instance = this;

            EditorSettings.SetGlobalSetting();

            titleContent = new GUIContent("VA Recorder");
            minSize = new Vector2(320, minSize.y);

            #region Settings
            {
                animationCompression = (AnimationCompression)EditorPrefs.GetInt(PrefKey_AnimationCompression, (int)AnimationCompression.KeyframeReduction);
                keyframeReduction_RotationError = EditorPrefs.GetFloat(PrefKey_KeyframeReduction_RotationError, 0.5f);
                keyframeReduction_PositionError = EditorPrefs.GetFloat(PrefKey_KeyframeReduction_PositionError, 0.5f);
                keyframeReduction_ScaleError = EditorPrefs.GetFloat(PrefKey_KeyframeReduction_ScaleError, 0.5f);
                keyframeReduction_EnableAnimator = EditorPrefs.GetBool(PrefKey_KeyframeReduction_EnableAnimator, true);
                keyframeReduction_EnableAnimatorRootAndIKGoal = EditorPrefs.GetBool(PrefKey_KeyframeReduction_EnableAnimatorRootAndIKGoal, true);
                keyframeReduction_EnableTransform = EditorPrefs.GetBool(PrefKey_KeyframeReduction_EnableTransform, true);
                keyframeReduction_EnableOther = EditorPrefs.GetBool(PrefKey_KeyframeReduction_EnableOther, true);

#if VERYANIMATION_FBX_5
                exportModelOptions.ExportFormat = (ExportFormat)EditorPrefs.GetInt(PrefKey_FBX_ExportFormat, (int)ExportFormat.ASCII);
                exportModelOptions.ModelAnimIncludeOption = (Include)EditorPrefs.GetInt(PrefKey_FBX_ModelAnimIncludeOption, (int)Include.ModelAndAnim);
                exportModelOptions.LODExportType = (LODExportType)EditorPrefs.GetInt(PrefKey_FBX_LODExportType, (int)LODExportType.All);
                exportModelOptions.ObjectPosition = (ObjectPosition)EditorPrefs.GetInt(PrefKey_FBX_ObjectPosition, (int)ObjectPosition.LocalCentered);
                exportModelOptions.AnimateSkinnedMesh = EditorPrefs.GetBool(PrefKey_FBX_AnimateSkinnedMesh, false);
                exportModelOptions.UseMayaCompatibleNames = EditorPrefs.GetBool(PrefKey_FBX_UseMayaCompatibleNames, true);
                exportModelOptions.ExportUnrendered = EditorPrefs.GetBool(PrefKey_FBX_ExportUnrendered, true);
                exportModelOptions.PreserveImportSettings = EditorPrefs.GetBool(PrefKey_FBX_PreserveImportSettings, false);
                exportModelOptions.KeepInstances = EditorPrefs.GetBool(PrefKey_FBX_KeepInstances, true);
                exportModelOptions.EmbedTextures = EditorPrefs.GetBool(PrefKey_FBX_EmbedTextures, false);
#endif
            }
            #endregion

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;

            lastRootObject = rootObject;
            RefreshRecordTargetSet();
            InvalidateRecordTargetComponentsCache();
            InvalidateRecordAvatarMaskCache();

            InternalEditorUtility.RepaintAllViews();
        }
        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;

            Release();

            #region Settings
            {
                EditorPrefs.SetInt(PrefKey_AnimationCompression, (int)animationCompression);
                EditorPrefs.SetFloat(PrefKey_KeyframeReduction_RotationError, keyframeReduction_RotationError);
                EditorPrefs.SetFloat(PrefKey_KeyframeReduction_PositionError, keyframeReduction_PositionError);
                EditorPrefs.SetFloat(PrefKey_KeyframeReduction_ScaleError, keyframeReduction_ScaleError);
                EditorPrefs.SetBool(PrefKey_KeyframeReduction_EnableAnimator, keyframeReduction_EnableAnimator);
                EditorPrefs.SetBool(PrefKey_KeyframeReduction_EnableAnimatorRootAndIKGoal, keyframeReduction_EnableAnimatorRootAndIKGoal);
                EditorPrefs.SetBool(PrefKey_KeyframeReduction_EnableTransform, keyframeReduction_EnableTransform);
                EditorPrefs.SetBool(PrefKey_KeyframeReduction_EnableOther, keyframeReduction_EnableOther);

#if VERYANIMATION_FBX_5
                EditorPrefs.SetInt(PrefKey_FBX_ExportFormat, (int)exportModelOptions.ExportFormat);
                EditorPrefs.SetInt(PrefKey_FBX_ModelAnimIncludeOption, (int)exportModelOptions.ModelAnimIncludeOption);
                EditorPrefs.SetInt(PrefKey_FBX_LODExportType, (int)exportModelOptions.LODExportType);
                EditorPrefs.SetInt(PrefKey_FBX_ObjectPosition, (int)exportModelOptions.ObjectPosition);
                EditorPrefs.SetBool(PrefKey_FBX_AnimateSkinnedMesh, exportModelOptions.AnimateSkinnedMesh);
                EditorPrefs.SetBool(PrefKey_FBX_UseMayaCompatibleNames, exportModelOptions.UseMayaCompatibleNames);
                EditorPrefs.SetBool(PrefKey_FBX_ExportUnrendered, exportModelOptions.ExportUnrendered);
                EditorPrefs.SetBool(PrefKey_FBX_PreserveImportSettings, exportModelOptions.PreserveImportSettings);
                EditorPrefs.SetBool(PrefKey_FBX_KeepInstances, exportModelOptions.KeepInstances);
                EditorPrefs.SetBool(PrefKey_FBX_EmbedTextures, exportModelOptions.EmbedTextures);
#endif
            }
            #endregion

            Instance = null;
        }
        private void OnDestroy()
        {
            ClearResource();
        }

        [InitializeOnLoad]
        internal class CustomCompilationListener
        {
            static CustomCompilationListener()
            {
                CompilationPipeline.compilationStarted += OnCompilationStarted;
            }

            private static void OnCompilationStarted(object context)
            {
                foreach (var w in Resources.FindObjectsOfTypeAll<VeryAnimationRecorderWindow>())
                {
                    if (w.recording)
                    {
                        w.Release();
                        Debug.Log("<color=blue>[Very Animation]</color>Recording ended : CompilationPipeline.compilationStarted");
                    }
                }
            }
        }
        private void OnPlayModeStateChanged(PlayModeStateChange mode)
        {
            playModeStateChange = mode;
            Release();
        }
        private void OnHierarchyChanged()
        {
            InvalidateRecordTargetComponentsCache();
            InvalidateRecordAvatarMaskCache();
        }
        private void OnUndoRedoPerformed()
        {
            if (lastRootObject != rootObject)
            {
                lastRootObject = rootObject;
                if (recording)
                    RecordEnd(true);
                ClearResource();
            }
            if (!recording)
                RefreshRecordTargetSet();
            InvalidateRecordTargetComponentsCache();
            InvalidateRecordAvatarMaskCache();
            Repaint();
        }

        private void Release()
        {
            if (recording)
            {
                RecordEnd(true);
            }
            ReleaseResource();
        }

        private void RefreshRecordTargetSet()
        {
            recordTargetSet.Clear();
            if (recordTargets == null)
                return;

            foreach (var target in recordTargets)
            {
                if (!string.IsNullOrEmpty(target))
                    recordTargetSet.Add(target);
            }
        }
        private void InvalidateRecordTargetComponentsCache()
        {
            recordTargetComponentsDirty = true;
        }
        private static bool IsRecordExcludedHideFlags(GameObject obj)
        {
            return obj.hideFlags != HideFlags.None;
        }
        private bool IsRecordExcludedObject(GameObject obj)
        {
            var rootTransform = rootObject.transform;
            var t = obj.transform;
            while (t != null && t != rootTransform)
            {
                if (IsRecordExcludedHideFlags(t.gameObject))
                    return true;
                t = t.parent;
            }
            return false;
        }
        private void EnsureRecordTargetComponentsCache()
        {
            if (rootObject == null)
            {
                InvalidateRecordTargetComponentsCache();
                return;
            }
            if (!recordTargetComponentsDirty &&
                cachedRecordTargetComponentsRootObject == rootObject &&
                cachedRecordTargetComponents != null &&
                cachedOptimizedAnimatorNames != null &&
                cachedRecordTargetSamePathNames != null &&
                cachedCulledAnimatorNames != null)
            {
                return;
            }

            var transforms = rootObject.GetComponentsInChildren<Transform>(true);

            cachedOptimizedAnimatorNames = string.Join(", ", rootObject.GetComponentsInChildren<Animator>(true).Where((x) => x != null && !IsRecordExcludedObject(x.gameObject) && !x.hasTransformHierarchy).Select((x) => x.name));

            {
                var rootTransform = rootObject.transform;
                var pathSet = new HashSet<string>(transforms.Length, StringComparer.Ordinal);
                var samePaths = new List<string>();
                for (int i = 0; i < transforms.Length; i++)
                {
                    var t = transforms[i];
                    if (t == null)
                        continue;
                    if (IsRecordExcludedObject(t.gameObject))
                        continue;

                    var path = AnimationUtility.CalculateTransformPath(t, rootTransform);
                    if (!pathSet.Add(path))
                        samePaths.Add(path);
                }
                cachedRecordTargetSamePathNames = string.Join(", ", samePaths);
            }

            {
                var culled = rootObject.GetComponentsInChildren<Animator>(true).Where((x) => x != null && !IsRecordExcludedObject(x.gameObject) && x.cullingMode != AnimatorCullingMode.AlwaysAnimate).Select((x) => x.name).ToList();
                culled.AddRange(rootObject.GetComponentsInChildren<Animation>(true).Where((x) => x != null && !IsRecordExcludedObject(x.gameObject) && x.cullingType != AnimationCullingType.AlwaysAnimate).Select((x) => x.name));
                cachedCulledAnimatorNames = string.Join(", ", culled);
            }

            cachedRecordTargetComponentsRootObject = rootObject;
            cachedRecordTargetComponents ??= new List<RecordTargetComponent>();
            cachedRecordTargetComponents.Clear();

            Dictionary<GameObject, EditorCurveBinding[]> animatableBindingsCache = new(transforms.Length);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null)
                    continue;
                if (IsRecordExcludedObject(t.gameObject))
                    continue;

                animatableBindingsCache.Add(t.gameObject, AnimationUtility.GetAnimatableBindings(t.gameObject, rootObject));
            }

            void AddComponent(Component component)
            {
                if (IsRecordExcludedObject(component.gameObject))
                    return;

                var type = component.GetType();

                int animatableBindingCount = 0;
                {
                    var cache = animatableBindingsCache[component.gameObject];
                    for (int i = 0; i < cache.Length; i++)
                    {
                        if (cache[i].type == type)
                            animatableBindingCount++;
                    }
                }

                var index = cachedRecordTargetComponents.FindIndex((x) => x.type == type);
                if (index >= 0)
                {
                    cachedRecordTargetComponents[index].animatableBindingCount += animatableBindingCount;
                    cachedRecordTargetComponents[index].content = new GUIContent($"{type.FullName} ({cachedRecordTargetComponents[index].animatableBindingCount})");
                    return;
                }

                cachedRecordTargetComponents.Add(new RecordTargetComponent()
                {
                    type = type,
                    content = new GUIContent($"{type.FullName} ({animatableBindingCount})"),
                    animatableBindingCount = animatableBindingCount,
                });
            }

            foreach (var component in rootObject.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    continue;

                AddComponent(component);
            }

            cachedRecordTargetComponents.Sort((a, b) => string.Compare(a.type.FullName, b.type.FullName, StringComparison.Ordinal));
            //GameObject.Active
            {
                var type = typeof(GameObject);
                var animatableBindingCount = animatableBindingsCache.Count - 1;    //self is not animatable
                cachedRecordTargetComponents.Insert(0, new RecordTargetComponent()
                {
                    type = type,
                    content = new GUIContent($"{type.FullName} ({animatableBindingCount})"),
                    animatableBindingCount = animatableBindingCount,
                });
            }

            recordTargetComponentsDirty = false;
        }
        private void InvalidateRecordAvatarMaskCache()
        {
            cachedRecordAvatarMaskRootObject = null;
            cachedRecordAvatarMask = null;
            cachedRecordAvatarMaskAnimator = null;
            cachedInactiveRecordAvatarMaskPaths = null;
        }
        private void EnsureRecordAvatarMaskCache()
        {
            if (rootObject == null || recordAvatarMask == null)
            {
                InvalidateRecordAvatarMaskCache();
                return;
            }
            if (cachedRecordAvatarMaskRootObject == rootObject &&
                cachedRecordAvatarMask == recordAvatarMask &&
                cachedRecordAvatarMaskAnimator == animator &&
                cachedInactiveRecordAvatarMaskPaths != null)
            {
                return;
            }

            var inactivePaths = new HashSet<string>(StringComparer.Ordinal);

            void AddTransformPath(Transform t)
            {
                if (t == null)
                    return;

                inactivePaths.Add(AnimationUtility.CalculateTransformPath(t, rootObject.transform));
            }
            void AddHumanoidPath(HumanBodyBones hi)
            {
                if (animator == null || !animator.isHuman)
                    return;

                AddTransformPath(animator.GetBoneTransform(hi));
            }

            if (animator != null && animator.isHuman)
            {
                if (!recordAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root))
                {
                    inactivePaths.Add(string.Empty);

                    var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                    if (hips != null)
                    {
                        var t = hips.parent;
                        while (t != null)
                        {
                            AddTransformPath(t);
                            if (t == rootObject.transform)
                                break;
                            t = t.parent;
                        }
                    }
                }
                if (!recordAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body))
                {
                    AddHumanoidPath(HumanBodyBones.UpperChest);
                    AddHumanoidPath(HumanBodyBones.Chest);
                    AddHumanoidPath(HumanBodyBones.Spine);
                    AddHumanoidPath(HumanBodyBones.Hips);
                }
                if (!recordAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Head))
                {
                    AddHumanoidPath(HumanBodyBones.Neck);
                    AddHumanoidPath(HumanBodyBones.Head);
                }
                if (!recordAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg))
                {
                    AddHumanoidPath(HumanBodyBones.LeftUpperLeg);
                    AddHumanoidPath(HumanBodyBones.LeftLowerLeg);
                    AddHumanoidPath(HumanBodyBones.LeftFoot);
                    AddHumanoidPath(HumanBodyBones.LeftToes);
                }
                if (!recordAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg))
                {
                    AddHumanoidPath(HumanBodyBones.RightUpperLeg);
                    AddHumanoidPath(HumanBodyBones.RightLowerLeg);
                    AddHumanoidPath(HumanBodyBones.RightFoot);
                    AddHumanoidPath(HumanBodyBones.RightToes);
                }
                if (!recordAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm))
                {
                    AddHumanoidPath(HumanBodyBones.LeftShoulder);
                    AddHumanoidPath(HumanBodyBones.LeftUpperArm);
                    AddHumanoidPath(HumanBodyBones.LeftLowerArm);
                    AddHumanoidPath(HumanBodyBones.LeftHand);
                }
                if (!recordAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm))
                {
                    AddHumanoidPath(HumanBodyBones.RightShoulder);
                    AddHumanoidPath(HumanBodyBones.RightUpperArm);
                    AddHumanoidPath(HumanBodyBones.RightLowerArm);
                    AddHumanoidPath(HumanBodyBones.RightHand);
                }
                if (!recordAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers))
                {
                    for (var hi = HumanBodyBones.LeftThumbProximal; hi <= HumanBodyBones.LeftLittleDistal; hi++)
                        AddHumanoidPath(hi);
                }
                if (!recordAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers))
                {
                    for (var hi = HumanBodyBones.RightThumbProximal; hi <= HumanBodyBones.RightLittleDistal; hi++)
                        AddHumanoidPath(hi);
                }
            }

            for (int i = 0; i < recordAvatarMask.transformCount; i++)
            {
                if (!recordAvatarMask.GetTransformActive(i))
                    inactivePaths.Add(recordAvatarMask.GetTransformPath(i));
            }

            cachedRecordAvatarMaskRootObject = rootObject;
            cachedRecordAvatarMask = recordAvatarMask;
            cachedRecordAvatarMaskAnimator = animator;
            cachedInactiveRecordAvatarMaskPaths = inactivePaths;
        }

        private void OnGUI()
        {
            if (playModeStateChange == PlayModeStateChange.ExitingEditMode ||
                playModeStateChange == PlayModeStateChange.ExitingPlayMode)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"PlayModeStateChange -> {playModeStateChange}", EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
                Repaint();
                return;
            }

            var e = Event.current;

            if (e.type == EventType.Layout)
            {
                if (selectHistoryIndex < 0 || selectHistoryIndex >= historyClip.Count)
                    selectHistoryIndex = historyClip.Count - 1;
                if (recordClip == null &&
                    historyClip.Count > 0 && historyClip[selectHistoryIndex] != null)
                {
                    recordClip = historyClip[selectHistoryIndex];
                }
                if (recordClip != null && uAnimationClipEditor == null)
                {
                    RefreshPreview();
                    Repaint();
                }
            }

            bool isRecorded = rootObject != null && !recording && recordClip != null;

            {
                EditorGUI.BeginDisabledGroup(recording || isRecorded);
                EditorGUI.BeginChangeCheck();
                var obj = EditorGUILayout.ObjectField("Root Object", rootObject, typeof(GameObject), true) as GameObject;
                if (EditorGUI.EndChangeCheck())
                {
                    if (obj != null &&
                        !(obj.scene.IsValid() && obj.scene.name != null))
                    {
                        obj = null;
                    }
                    Undo.RecordObject(this, UndoVARecorderSettings);
                    rootObject = obj;
                    lastRootObject = obj;
                    ClearResource();
                    InvalidateRecordTargetComponentsCache();
                    InvalidateRecordAvatarMaskCache();
                }
                EditorGUI.EndDisabledGroup();
            }
            if (rootObject == null)
            {
                EditorGUILayout.HelpBox(Language.GetText(Language.Help.RecorderWindowSetRootObject), MessageType.Info);
            }
            else
            {
                settingsFoldout = EditorGUILayout.Foldout(settingsFoldout, "Record Settings", true);
                if (settingsFoldout)
                {
                    EditorGUI.indentLevel++;
                    EditorGUI.BeginDisabledGroup(recording);
                    {
                        EditorGUI.BeginChangeCheck();
                        int value = EditorGUILayout.IntField(Styles.guiContentSamplesFPS, clipFrameRate);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(this, UndoVARecorderSettings);
                            clipFrameRate = Math.Max(value, 1);
                        }
                    }
                    {
                        recordTargetsFoldout = EditorGUILayout.Foldout(recordTargetsFoldout, Language.GetContent(Language.Help.RecorderWindowTargets), true);
                        if (recordTargetsFoldout)
                        {
                            if (e.type == EventType.Layout || cachedRecordTargetComponents == null)
                                EnsureRecordTargetComponentsCache();

                            EditorGUI.BeginChangeCheck();
                            recordTargetsSelected ??= new List<string>();
                            recordTargetsSelected.Clear();
                            var minScrollHeight = EditorGUIUtility.singleLineHeight + 6f;
                            var scrollHeight = Mathf.Max(Mathf.Min(position.height - 200f, Mathf.Max(minScrollHeight, cachedRecordTargetComponents.Count * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) + 6f)), minScrollHeight);
                            recordTargetsScrollPosition = EditorGUILayout.BeginScrollView(recordTargetsScrollPosition, GUILayout.Height(scrollHeight));
                            EditorGUI.indentLevel++;
                            for (int i = 0; i < cachedRecordTargetComponents.Count; ++i)
                            {
                                var fullName = cachedRecordTargetComponents[i].type.FullName;
                                var selected = !string.IsNullOrEmpty(fullName) && recordTargetSet.Contains(fullName);
                                var newSelected = EditorGUILayout.ToggleLeft(cachedRecordTargetComponents[i].content, selected);
                                if (newSelected && !string.IsNullOrEmpty(fullName))
                                    recordTargetsSelected.Add(fullName);
                            }
                            EditorGUI.indentLevel--;
                            EditorGUILayout.EndScrollView();
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, UndoVARecorderSettings);
                                recordTargets = recordTargetsSelected.ToArray();
                                RefreshRecordTargetSet();
                            }
                        }
                    }
                    {
                        EditorGUI.BeginChangeCheck();
                        var value = EditorGUILayout.ObjectField(Language.GetContent(Language.Help.RecorderWindowAvatarMask), recordAvatarMask, typeof(AvatarMask), true) as AvatarMask;
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(this, UndoVARecorderSettings);
                            recordAvatarMask = value;
                            InvalidateRecordAvatarMaskCache();
                        }
                    }
                    {
                        EditorGUI.BeginChangeCheck();
                        var value = EditorGUILayout.Toggle(Language.GetContent(Language.Help.RecorderWindowUnchangingCurves), unchangingCurves);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(this, UndoVARecorderSettings);
                            unchangingCurves = value;
                        }
                    }
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUI.BeginChangeCheck();
                            var value = EditorGUILayout.Toggle(Language.GetContent(Language.Help.RecorderWindowUseDuration), useDuration);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, UndoVARecorderSettings);
                                useDuration = value;
                            }
                        }
                        if (useDuration)
                        {
                            EditorGUI.BeginChangeCheck();
                            var value = EditorGUILayout.FloatField(durationTime);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, UndoVARecorderSettings);
                                durationTime = Mathf.Max(value, 0f);
                            }
                            EditorGUILayout.LabelField($"Seconds / {EditorCommon.GetTimeFrameFloor(durationTime, clipFrameRate)} Frames");
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUI.EndDisabledGroup();
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space();

                EditorGUILayout.BeginHorizontal();
                {
                    const float ButtonHeight = 32f;
                    {
                        var saveColor = GUI.color;
                        if (recording)
                        {
                            var recordedColor = AnimationMode.recordedPropertyColor;
                            recordedColor.a *= GUI.color.a;
                            GUI.color = recordedColor;
                        }
                        EditorGUI.BeginChangeCheck();
                        var flag = GUILayout.Toggle(recording, Styles.guiContentRecord, GUI.skin.button, GUILayout.Height(ButtonHeight));
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (flag)
                            {
                                RecordStart();
                            }
                            else
                            {
                                RecordEnd();
                            }
                        }
                        GUI.color = saveColor;
                    }

                    {
                        EditorGUI.BeginDisabledGroup(recording);
                        EditorGUI.BeginChangeCheck();
                        var settings = GUILayout.Toggle(settingsMode, Styles.guiContentSettings, GUI.skin.button, GUILayout.Width(32f), GUILayout.Height(ButtonHeight));
                        if (EditorGUI.EndChangeCheck())
                        {
                            settingsMode = settings;
                        }
                        EditorGUI.EndDisabledGroup();
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (recording)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    if (recordTime >= 0f)
                    {
                        EditorGUILayout.LabelField("Record Time", recordTime.ToString("0.00"), EditorStyles.boldLabel);
                        EditorGUILayout.LabelField("Record Frame", EditorCommon.GetTimeFrameFloor(recordTime, recordFrameRate).ToString(), EditorStyles.boldLabel);
                        EditorGUILayout.LabelField("Frame Drop", frameDropCount.ToString(), EditorStyles.boldLabel);
                    }
                    EditorGUILayout.EndVertical();

                    Repaint();
                }
                else if (settingsMode)
                {
                    EditorGUILayout.LabelField("Export Settings", EditorStyles.centeredGreyMiniLabel);
                    settingsScrollPosition = EditorGUILayout.BeginScrollView(settingsScrollPosition);
                    {
                        {
                            EditorGUI.BeginChangeCheck();
                            var value = (AnimationCompression)EditorGUILayout.EnumPopup(Language.GetContent(Language.Help.RecorderWindowAnimCompression), animationCompression);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, UndoVARecorderSettings);
                                animationCompression = value;
                            }
                        }
                        EditorGUILayout.Space();
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("Keyframe Reduction", EditorStyles.largeLabel);
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("Reset"))
                            {
                                Undo.RecordObject(this, UndoVARecorderSettings);
                                keyframeReduction_RotationError = 0.5f;
                                keyframeReduction_PositionError = 0.5f;
                                keyframeReduction_ScaleError = 0.5f;
                                keyframeReduction_EnableAnimator = true;
                                keyframeReduction_EnableAnimatorRootAndIKGoal = true;
                                keyframeReduction_EnableTransform = true;
                                keyframeReduction_EnableOther = true;
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                        EditorGUI.indentLevel++;
                        {
                            {
                                EditorGUI.BeginChangeCheck();
                                var param = EditorGUILayout.FloatField("Rotation Error", keyframeReduction_RotationError);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    Undo.RecordObject(this, UndoVARecorderSettings);
                                    keyframeReduction_RotationError = param;
                                }
                            }
                            {
                                EditorGUI.BeginChangeCheck();
                                var param = EditorGUILayout.FloatField("Position Error", keyframeReduction_PositionError);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    Undo.RecordObject(this, UndoVARecorderSettings);
                                    keyframeReduction_PositionError = param;
                                }
                            }
                            {
                                EditorGUI.BeginChangeCheck();
                                var param = EditorGUILayout.FloatField("Scale Error", keyframeReduction_ScaleError);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    Undo.RecordObject(this, UndoVARecorderSettings);
                                    keyframeReduction_ScaleError = param;
                                }
                            }
                            {
                                EditorGUI.BeginChangeCheck();
                                var param = EditorGUILayout.ToggleLeft("Animator Curves", keyframeReduction_EnableAnimator);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    Undo.RecordObject(this, UndoVARecorderSettings);
                                    keyframeReduction_EnableAnimator = param;
                                }
                            }
                            if (keyframeReduction_EnableAnimator)
                            {
                                EditorGUI.indentLevel++;
                                EditorGUI.BeginChangeCheck();
                                var param = EditorGUILayout.ToggleLeft("Root and IK Goal Curves", keyframeReduction_EnableAnimatorRootAndIKGoal);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    Undo.RecordObject(this, UndoVARecorderSettings);
                                    keyframeReduction_EnableAnimatorRootAndIKGoal = param;
                                }
                                EditorGUI.indentLevel--;
                            }
                            {
                                EditorGUI.BeginChangeCheck();
                                var param = EditorGUILayout.ToggleLeft("Transform Curves", keyframeReduction_EnableTransform);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    Undo.RecordObject(this, UndoVARecorderSettings);
                                    keyframeReduction_EnableTransform = param;
                                }
                            }
                            {
                                EditorGUI.BeginChangeCheck();
                                var param = EditorGUILayout.ToggleLeft("Other Curves", keyframeReduction_EnableOther);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    Undo.RecordObject(this, UndoVARecorderSettings);
                                    keyframeReduction_EnableOther = param;
                                }
                            }
                        }
                        EditorGUI.indentLevel--;
                    }
#if VERYANIMATION_FBX
                    EditorGUILayout.Space();
#endif
#if VERYANIMATION_FBX_5
                    {
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("Fbx Export", EditorStyles.largeLabel);
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("Reset"))
                            {
                                Undo.RecordObject(this, UndoVARecorderSettings);
                                exportModelOptions = new();
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                        EditorGUI.indentLevel++;
                        {
                            EditorGUI.BeginChangeCheck();
                            var exportFormat = (ExportFormat)EditorGUILayout.EnumPopup("Export Format", exportModelOptions.ExportFormat);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, UndoVARecorderSettings);
                                exportModelOptions.ExportFormat = exportFormat;
                            }
                        }
                        {
                            EditorGUI.BeginChangeCheck();
                            var modelAnimIncludeOption = (Include)EditorGUILayout.EnumPopup("Include", exportModelOptions.ModelAnimIncludeOption);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, UndoVARecorderSettings);
                                exportModelOptions.ModelAnimIncludeOption = modelAnimIncludeOption;
                            }
                        }
                        {
                            EditorGUI.BeginChangeCheck();
                            var LODExportType = (LODExportType)EditorGUILayout.EnumPopup("LOD level", exportModelOptions.LODExportType);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, UndoVARecorderSettings);
                                exportModelOptions.LODExportType = LODExportType;
                            }
                        }
                        {
                            EditorGUI.BeginChangeCheck();
                            var objectPosition = (ObjectPosition)EditorGUILayout.EnumPopup("Object(s) Position", exportModelOptions.ObjectPosition);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, UndoVARecorderSettings);
                                exportModelOptions.ObjectPosition = objectPosition;
                            }
                        }
                        {
                            EditorGUI.BeginChangeCheck();
                            var animateSkinnedMesh = EditorGUILayout.ToggleLeft("Animated Skinned Mesh", exportModelOptions.AnimateSkinnedMesh);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, UndoVARecorderSettings);
                                exportModelOptions.AnimateSkinnedMesh = animateSkinnedMesh;
                            }
                        }
                        {
                            EditorGUI.BeginChangeCheck();
                            var useMayaCompatibleNames = EditorGUILayout.ToggleLeft("Compatible Naming", exportModelOptions.UseMayaCompatibleNames);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, UndoVARecorderSettings);
                                exportModelOptions.UseMayaCompatibleNames = useMayaCompatibleNames;
                            }
                        }
                        {
                            EditorGUI.BeginChangeCheck();
                            var exportUnrendered = EditorGUILayout.ToggleLeft("Export Unrendered", exportModelOptions.ExportUnrendered);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, UndoVARecorderSettings);
                                exportModelOptions.ExportUnrendered = exportUnrendered;
                            }
                        }
                        {
                            EditorGUI.BeginChangeCheck();
                            var preserveImportSettings = EditorGUILayout.ToggleLeft("Preserve Import Settings", exportModelOptions.PreserveImportSettings);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, UndoVARecorderSettings);
                                exportModelOptions.PreserveImportSettings = preserveImportSettings;
                            }
                        }
                        {
                            EditorGUI.BeginChangeCheck();
                            var keepInstances = EditorGUILayout.ToggleLeft("Keep Instances", exportModelOptions.KeepInstances);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, UndoVARecorderSettings);
                                exportModelOptions.KeepInstances = keepInstances;
                            }
                        }
                        {
                            EditorGUI.BeginChangeCheck();
                            var embedTextures = EditorGUILayout.ToggleLeft("Embed Textures", exportModelOptions.EmbedTextures);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, UndoVARecorderSettings);
                                exportModelOptions.EmbedTextures = embedTextures;
                            }
                        }
                        EditorGUI.indentLevel--;
                    }
#elif VERYANIMATION_FBX
                    {
                        EditorGUILayout.LabelField("Fbx Export", EditorStyles.largeLabel);
                        EditorGUI.indentLevel++;
                        {
                            EditorGUILayout.LabelField(Language.GetText(Language.Help.RecorderWindowFBXVersion4Warning), EditorStyles.centeredGreyMiniLabel);
                        }
                        EditorGUI.indentLevel--;
                    }
#endif
                    EditorGUILayout.EndScrollView();
                }
                else
                {
                    const float BottomMenuHeight = 100f;

                    if (uAvatarPreview != null)
                    {
                        EditorGUILayout.Space();

                        if (historyClip.Count > 0)
                        {
                            EditorGUILayout.BeginHorizontal();
                            {
                                EditorGUI.BeginChangeCheck();
                                var index = GUILayout.Toolbar(selectHistoryIndex, historyClipStrings);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    selectHistoryIndex = index;
                                    recordClip = historyClip[selectHistoryIndex];
                                    RefreshPreview(true);
                                }
                            }
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button(Language.GetContent(Language.Help.RecorderWindowHistoryClear)))
                            {
                                if (EditorUtility.DisplayDialog("Clear", Language.GetText(Language.Help.RecorderWindowHistoryClearDialog), "Yes", "No"))
                                {
                                    EditorApplication.delayCall += () =>
                                    {
                                        ClearResource();
                                    };
                                }
                            }
                            EditorGUILayout.EndHorizontal();
                        }

                        {
                            EditorGUILayout.BeginHorizontal("preToolbar", GUILayout.Height(17f));
                            GUILayout.FlexibleSpace();
                            Rect lastRect = GUILayoutUtility.GetLastRect();
                            if (recordClip != null)
                                GUI.Label(lastRect, recordClip.name, "preToolbar2");
                            uAvatarPreview.OnPreviewSettings();
                            EditorGUILayout.EndHorizontal();
                        }
                        if (uAvatarPreview.Playing)
                        {
                            Repaint();
                        }
                        else
                        {
                            if (e.type == EventType.Repaint)
                                uAvatarPreview.ForceUpdate();
                        }

                        {
                            var rect = EditorGUILayout.GetControlRect(false, 0);
                            rect.height = Math.Max(position.height - rect.y - BottomMenuHeight, 0);
                            uAvatarPreview.OnGUI(rect, "preBackground");
                        }
                    }

                    if (recordClip != null)
                    {
                        GUILayout.FlexibleSpace();

                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        if (uAnimationClipEditor != null)
                        {
                            float firstFrame = trimFirstFrame;
                            float lastFrame = trimLastFrame;
                            float additivePoseframe = 0.0f;
                            uAnimationClipEditor.ClipRangeGUI(ref firstFrame, ref lastFrame, out bool changedStart, out bool changedStop, false, ref additivePoseframe, out _);
                            if (changedStart)
                            {
                                trimFirstFrame = Mathf.RoundToInt(firstFrame);
                                if (!uAvatarPreview.Playing)
                                {
                                    var time = EditorCommon.GetFrameTime((int)trimFirstFrame, recordClip.frameRate);
                                    uAvatarPreview.SetTime(time);
                                }
                            }
                            if (changedStop)
                            {
                                trimLastFrame = Mathf.RoundToInt(lastFrame);
                                if (!uAvatarPreview.Playing)
                                {
                                    var time = EditorCommon.GetFrameTime((int)trimLastFrame, recordClip.frameRate);
                                    uAvatarPreview.SetTime(time);
                                }
                            }

                            if (e.type == EventType.Repaint)
                            {
                                if (uAvatarPreview.Playing)
                                {
                                    var frame = EditorCommon.GetTimeFrameRound(uAvatarPreview.GetTime(), recordClip.frameRate);
                                    if (frame < (int)trimFirstFrame || frame > (int)trimLastFrame)
                                    {
                                        var time = EditorCommon.GetFrameTime((int)trimFirstFrame, recordClip.frameRate);
                                        uAvatarPreview.SetTime(time);
                                    }
                                }
                            }
                        }

                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Export");

                            if (VeryAnimationWindow.instance != null && VeryAnimationWindow.instance.Initialized)
                            {
                                EditorGUILayout.HelpBox(Language.GetText(Language.Help.RecorderWindowVAEditing), MessageType.Warning);
                            }
                            else
                            {
                                if (GUILayout.Button(Language.GetContent(Language.Help.RecorderWindowLegacyClip)))
                                {
                                    SaveAnimationClip(ModelImporterAnimationType.Legacy);
                                }
                                if (GUILayout.Button(Language.GetContent(Language.Help.RecorderWindowGenericClip)))
                                {
                                    SaveAnimationClip(ModelImporterAnimationType.Generic);
                                }
                                {
                                    rootObject.TryGetComponent<Animator>(out var animator);
                                    EditorGUI.BeginDisabledGroup(animator == null || !animator.isHuman);
                                    if (GUILayout.Button(Language.GetContent(Language.Help.RecorderWindowHumanoidClip)))
                                    {
                                        SaveAnimationClip(ModelImporterAnimationType.Human);
                                    }
                                    EditorGUI.EndDisabledGroup();
                                }
                                {
#if !VERYANIMATION_FBX
                                    EditorGUI.BeginDisabledGroup(true);
#endif
                                    if (GUILayout.Button(Language.GetContent(Language.Help.RecorderWindowFBX)))
                                    {
                                        SaveFBX();
                                    }
#if !VERYANIMATION_FBX
                                    EditorGUI.EndDisabledGroup();
#endif
                                }
                            }

                            EditorGUILayout.EndHorizontal();
                        }
                        EditorGUILayout.EndVertical();
                    }
                    else
                    {
                        if (e.type == EventType.Layout || cachedRecordTargetComponents == null)
                            EnsureRecordTargetComponentsCache();

                        if (!string.IsNullOrEmpty(cachedOptimizedAnimatorNames))
                        {
                            EditorGUILayout.HelpBox(string.Format(Language.GetText(Language.Help.RecorderWindowOptimizedAnimators), cachedOptimizedAnimatorNames), MessageType.Warning);
                        }
                        if (!string.IsNullOrEmpty(cachedRecordTargetSamePathNames))
                        {
                            EditorGUILayout.HelpBox(string.Format(Language.GetText(Language.Help.RecorderWindowWarningMultipleGameObjectsWithSameName), cachedRecordTargetSamePathNames), MessageType.Warning);
                        }
                        if (!string.IsNullOrEmpty(cachedCulledAnimatorNames))
                        {
                            EditorGUILayout.HelpBox(string.Format(Language.GetText(Language.Help.RecorderWindowCulledAnimators), cachedCulledAnimatorNames), MessageType.Info);
                        }
                    }
                }
            }
        }

        private void RecordStart()
        {
            Assert.IsNotNull(rootObject);
            Assert.IsFalse(recording);
            recording = true;

            if (!Application.runInBackground)
            {
                saveApplicationRunInBackground = false;
                Application.runInBackground = true;
            }

            EditorCommon.ShowNotification("Recording Start");

            ReleaseResource();

            rootObject.TryGetComponent<Animator>(out animator);
            RefreshRecordTargetSet();
            InvalidateRecordAvatarMaskCache();
            EnsureRecordAvatarMaskCache();

            animatableBindingsRefData ??= new();
            animatableBindingsRefData.Clear();
            animatableBindingsRefSet.Clear();
            animatableBindingsFloatData ??= new();
            animatableBindingsFloatData.Clear();
            animatableBindingsFloatSet.Clear();

            recordFrameRate = clipFrameRate;
            recordTime = -EditorCommon.GetFrameTime(1, recordFrameRate);
            recordCount = 0;
            lastFrameEditorTime = EditorApplication.timeSinceStartup;
            lastTimeFrameCount = -1;
            frameDropCount = 0;

            AddGameObjectResource(rootObject, false);

            settingsMode = false;
            recordTargetsFoldout = false;

            ObjectChangeEvents.changesPublished += ChangesPublished;
            EditorApplication.update += RecordUpdate;

            Repaint();
        }
        private void RecordEnd(bool forcedTermination = false)
        {
            Assert.IsTrue(recording);
            recording = false;

            try
            {
                if (saveApplicationRunInBackground.HasValue)
                {
                    Application.runInBackground = saveApplicationRunInBackground.Value;
                    saveApplicationRunInBackground = default;
                }

                if (!forcedTermination)
                {
                    EditorCommon.ShowNotification("Recording End");

                    try
                    {
                        recordClip = new AnimationClip()
                        {
                            name = DateTime.Now.ToString("HH-mm-ss"),
                            hideFlags = HideFlags.HideAndDontSave,
                            frameRate = recordFrameRate,
                        };

                        EditorUtility.DisplayProgressBar("Create animation clip", recordClip.name, 0f);

                        {
                            var settings = AnimationUtility.GetAnimationClipSettings(recordClip);
                            settings.keepOriginalPositionXZ = true;
                            settings.keepOriginalPositionY = true;
                            settings.keepOriginalOrientation = true;
                            settings.loopBlendOrientation = !ApplyRootMotion;
                            settings.loopBlendPositionXZ = !ApplyRootMotion;
                            settings.loopBlendPositionY = !ApplyRootMotion;
                            settings.mirror = false;
                            settings.loopBlend = false;
                            settings.cycleOffset = 0;
                            settings.level = 0;
                            settings.orientationOffsetY = 0;
                            settings.loopTime = false;
                            AnimationUtility.SetAnimationClipSettings(recordClip, settings);
                        }

                        #region NeedWrite
                        if (!unchangingCurves)
                        {
                            for (int i = 0; i < animatableBindingsFloatData.Count; i++)
                            {
                                var objData = animatableBindingsFloatData[i];
                                //root motion
                                if (ApplyRootMotion &&
                                    objData.binding.path == string.Empty &&
                                    objData.binding.type == typeof(Transform))
                                {
                                    objData.needWrite = true;
                                }
                                //Same members
                                if (objData.needWrite)
                                {
                                    var lastIndex = objData.binding.propertyName.LastIndexOf('.');
                                    if (lastIndex >= 0)
                                    {
                                        var pName = objData.binding.propertyName[..(lastIndex + 1)];

                                        for (int j = i - 1; j >= 0; j--)
                                        {
                                            var befData = animatableBindingsFloatData[j];
                                            if (befData.binding.path != objData.binding.path ||
                                                befData.binding.type != objData.binding.type)
                                                break;
                                            if (!befData.binding.propertyName.StartsWith(pName, StringComparison.Ordinal))
                                                break;
                                            befData.needWrite = true;
                                        }
                                        for (int j = i + 1; j < animatableBindingsFloatData.Count; j++, i++)
                                        {
                                            var befData = animatableBindingsFloatData[j];
                                            if (befData.binding.path != objData.binding.path ||
                                                befData.binding.type != objData.binding.type)
                                                break;
                                            if (!befData.binding.propertyName.StartsWith(pName, StringComparison.Ordinal))
                                                break;
                                            befData.needWrite = true;
                                        }
                                    }
                                }
                            }
                        }
                        #endregion

                        {
                            Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]> rDatas = new(animatableBindingsRefData.Count);
                            for (int index = 0; index < animatableBindingsRefData.Count; index++)
                            {
                                var objData = animatableBindingsRefData[index];

                                EditorUtility.DisplayProgressBar("Create animation clip - Reference curves", AnimationCommon.GetBindingDisplayName(objData.binding), index / (float)animatableBindingsRefData.Count);

                                if (objData.needWrite || unchangingCurves)
                                {
                                    rDatas.Add(objData.binding, objData.CreateObjectReferenceKeyframes());
                                }
                            }
                            AnimationCommon.SetObjectReferenceCurves(recordClip, rDatas);
                        }
                        {
                            Dictionary<EditorCurveBinding, AnimationCurve> fDatas = new(animatableBindingsFloatData.Count);
                            for (int index = 0; index < animatableBindingsFloatData.Count; index++)
                            {
                                var objData = animatableBindingsFloatData[index];

                                EditorUtility.DisplayProgressBar("Create animation clip - Float curves", AnimationCommon.GetBindingDisplayName(objData.binding), index / (float)animatableBindingsFloatData.Count);

                                if (objData.needWrite || unchangingCurves)
                                {
                                    fDatas.Add(objData.binding, objData.CreateAnimationCurve());
                                }
                            }
                            AnimationCommon.SetEditorCurves(recordClip, fDatas);
                        }

                        recordClip.EnsureQuaternionContinuity();

                    }
                    finally
                    {
                        EditorUtility.ClearProgressBar();
                    }

                    if (recordClip != null)
                    {
                        historyClip.Add(recordClip);
                        historyClipStrings = historyClip.Select(x => x != null ? x.name : string.Empty).ToArray();
                        selectHistoryIndex = historyClip.Count - 1;
                    }
                    RefreshPreview(true);
                }
                else
                {
                    ReleaseResource();
                }
            }
            finally
            {
                settingsMode = false;

                ObjectChangeEvents.changesPublished -= ChangesPublished;
                EditorApplication.update -= RecordUpdate;

                RefreshRecordTargetSet();
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }
        private void RecordUpdate()
        {
            if (!recording)
                return;
            if (rootObject == null)
            {
                RecordEnd(true);
                return;
            }

            float deltaTime;
            if (EditorApplication.isPlaying)
            {
                if (Time.frameCount == lastTimeFrameCount)
                    return;
                lastTimeFrameCount = Time.frameCount;
                deltaTime = Time.deltaTime;
            }
            else
            {
                var timeSinceStartup = EditorApplication.timeSinceStartup;
                deltaTime = (float)(timeSinceStartup - lastFrameEditorTime);
                lastFrameEditorTime = timeSinceStartup;
            }

            int beforeRecordFrame, recordFrame;
            float recordFrameTime;
            float timeWeight;
            {
                var beforeRecordTime = recordTime;
                beforeRecordFrame = EditorCommon.GetTimeFrameFloor(beforeRecordTime, recordFrameRate);

                recordTime += deltaTime;

                recordFrame = EditorCommon.GetTimeFrameFloor(recordTime, recordFrameRate);
                recordFrameTime = EditorCommon.GetFrameTime(recordFrame, recordFrameRate);
                timeWeight = Mathf.InverseLerp(beforeRecordTime, recordTime, recordFrameTime);
            }

            if (recordCount == 0)
            {
                beforeRecordFrame = -1;
                recordTime = 0f;
                recordFrame = 0;
                recordFrameTime = 0f;
                timeWeight = 1f;
            }

            if (recordCount > 0)
            {
                foreach (var addObj in addedChangesPublished)
                {
                    if (addObj == null)
                        continue;
                    AddGameObjectResource(addObj, true);
                }
                addedChangesPublished.Clear();
            }

            foreach (var objData in animatableBindingsRefData)
            {
                var result = AnimationUtility.GetObjectReferenceValue(rootObject, objData.binding, out var value);
                if (result)
                {
                    objData.needWrite |= objData.beforeValue != value;
                    if (beforeRecordFrame != recordFrame)
                    {
                        objData.SetKey(recordFrameTime, value);
                    }
                    objData.beforeValue = value;
                }
            }
            foreach (var objData in animatableBindingsFloatData)
            {
                var result = AnimationUtility.GetFloatValue(rootObject, objData.binding, out var value);
                if (!result)
                {
                    if (AnimationCommon.IsActiveBinding(objData.binding))
                    {
                        value = 0f;
                        result = true;
                    }
                }
                if (result)
                {
                    objData.needWrite |= objData.beforeValue != value;
                    if (beforeRecordFrame != recordFrame)
                    {
                        var calcValue = Mathf.Lerp(objData.beforeValue, value, timeWeight);
                        if (objData.valueType == typeof(int) || objData.valueType == typeof(bool))
                            calcValue = Mathf.RoundToInt(calcValue);
                        objData.SetKey(recordFrameTime, calcValue);
                    }
                    objData.beforeValue = value;
                }
            }

            if (beforeRecordFrame != recordFrame)
            {
                //Debug.Log($"recordFrame: {beforeRecordFrame} - {recordFrame}, recordTime: {EditorCommon.GetFrameTime(beforeRecordFrame, clipFrameRate)} < {recordFrameTime} < {recordTime}, timeWeight: {timeWeight}, ");
                frameDropCount += (recordFrame - beforeRecordFrame) - 1;
                recordCount++;
            }

            if (useDuration &&
                recordTime >= durationTime)
            {
                RecordEnd();
                return;
            }
        }

        private void ReleaseResource()
        {
            ReleasePreview();
            if (recordClip != null)
            {
                if (!historyClip.Contains(recordClip))
                    AnimationClip.DestroyImmediate(recordClip);
                recordClip = null;
            }
            animatableBindingsRefData?.Clear();
            animatableBindingsFloatData?.Clear();
            animatableBindingsRefSet.Clear();
            animatableBindingsFloatSet.Clear();

            addedChangesPublished.Clear();
        }
        private void ReleasePreview()
        {
            uAnimationClipEditor?.Dispose();
            uAnimationClipEditor = null;
            uAvatarPreview?.Dispose();
            uAvatarPreview = null;
        }
        private void ClearResource()
        {
            ReleaseResource();

            foreach (var clip in historyClip)
            {
                if (clip != null)
                {
                    AnimationClip.DestroyImmediate(clip);
                }
            }
            historyClip.Clear();
            historyClipStrings = Array.Empty<string>();
            selectHistoryIndex = -1;
        }
        private void AddGameObjectResource(GameObject obj, bool isChangesPublished)
        {
            EnsureRecordAvatarMaskCache();

            var oneMinusFrame = Math.Max(0, EditorCommon.GetTimeFrameFloor(recordTime - EditorCommon.GetFrameTime(1, recordFrameRate), recordFrameRate));
            var oneMinusTime = EditorCommon.GetFrameTime(oneMinusFrame, recordFrameRate);

            var bindings = AnimationUtility.GetAnimatableBindings(obj, rootObject);
            for (int i = 0; i < bindings.Length; i++)
            {
                var binding = bindings[i];
                var bindingTypeName = binding.type.FullName;
                if (string.IsNullOrEmpty(bindingTypeName) ||
                    !recordTargetSet.Contains(bindingTypeName))
                {
                    continue;
                }
                if (cachedInactiveRecordAvatarMaskPaths != null &&
                    cachedInactiveRecordAvatarMaskPaths.Contains(binding.path))
                {
                    continue;
                }

                if (binding.isPPtrCurve)
                {
                    if (!animatableBindingsRefSet.Add(binding))
                        continue;

                    AnimationUtility.GetObjectReferenceValue(rootObject, binding, out var value);

                    int GetDataInitialRefCapacity()
                    {
                        const int InitialCapacity = 4;
                        int value;
                        if (useDuration)
                        {
                            value = EditorCommon.CeilToPowerOfTwo(EditorCommon.GetTimeFrameFloor(durationTime, recordFrameRate));
                        }
                        else
                        {
                            value = s_initialCapacityRandom.Next(InitialCapacity, InitialCapacity * 2);    //Distribute the timing of resize
                        }
                        return value;
                    }

                    var data = new AnimatableBindingRefData(GetDataInitialRefCapacity())
                    {
                        binding = binding,
                        beforeValue = value,
                    };

                    if (isChangesPublished)
                    {
                        data.SetKey(0f, data.beforeValue);
                        if (oneMinusTime > 0f)
                            data.SetKey(oneMinusTime, data.beforeValue);
                    }

                    animatableBindingsRefData.Add(data);
                }
                else
                {
                    if (!animatableBindingsFloatSet.Add(binding))
                        continue;

                    AnimationUtility.GetFloatValue(rootObject, binding, out var value);

                    var needWrite = false;
                    if (isChangesPublished &&
                        AnimationCommon.IsActiveBinding(binding))
                    {
                        value = 0f;
                        needWrite = true;
                    }
                    
                    int GetDataInitialFloatCapacity()
                    {
                        const int InitialCapacity = 32;
                        int value;
                        if (useDuration)
                        {
                            value = EditorCommon.CeilToPowerOfTwo(EditorCommon.GetTimeFrameFloor(durationTime, recordFrameRate));
                        }
                        else
                        {
                            value = s_initialCapacityRandom.Next(InitialCapacity, InitialCapacity * 2);    //Distribute the timing of resize
                        }
                        return value;
                    }

                    var data = new AnimatableBindingFloatData(GetDataInitialFloatCapacity())
                    {
                        binding = binding,
                        beforeValue = value,
                        valueType = AnimationUtility.GetEditorCurveValueType(rootObject, binding),
                        needWrite = needWrite,
                    };

                    if (isChangesPublished)
                    {
                        data.SetKey(0f, data.beforeValue);
                        if (oneMinusTime > 0f)
                            data.SetKey(oneMinusTime, data.beforeValue);
                    }

                    animatableBindingsFloatData.Add(data);
                }
            }

            for (int i = 0; i < obj.transform.childCount; i++)
            {
                var child = obj.transform.GetChild(i).gameObject;
                if (IsRecordExcludedHideFlags(child))
                    continue;

                AddGameObjectResource(child, isChangesPublished);
            }
        }
        private void ChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (!recording || rootObject == null)
                return;

            var rootTransform = rootObject.transform;

#if UNITY_6000_4_OR_NEWER
            static UnityEngine.Object ToObject(EntityId id) => EditorUtility.EntityIdToObject(id);
#elif UNITY_6000_3_OR_NEWER
            static UnityEngine.Object ToObject(int id) => EditorUtility.EntityIdToObject(id);
#else
            static UnityEngine.Object ToObject(int id) => EditorUtility.InstanceIDToObject(id);
#endif
            void AddChangedObject(UnityEngine.Object changedObject)
            {
                if (changedObject is GameObject gameObj)
                {
                    if (gameObj.transform.IsChildOf(rootTransform) && !IsRecordExcludedObject(gameObj))
                        addedChangesPublished.Add(gameObj);
                }
                else if (changedObject is Component gameComp)
                {
                    if (gameComp.transform.IsChildOf(rootTransform) && !IsRecordExcludedObject(gameComp.gameObject))
                        addedChangesPublished.Add(gameComp.gameObject);
                }
            }

            for (int i = 0; i < stream.length; i++)
            {
                var evType = stream.GetEventType(i);
                switch (evType)
                {
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                        {
                            stream.GetCreateGameObjectHierarchyEvent(i, out var data);
#if UNITY_6000_4_OR_NEWER
                            AddChangedObject(ToObject(data.entityId));
#else
                            AddChangedObject(ToObject(data.instanceId));
#endif
                        }
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        {
                            stream.GetChangeGameObjectStructureHierarchyEvent(i, out var data);
#if UNITY_6000_4_OR_NEWER
                            AddChangedObject(ToObject(data.entityId));
#else
                            AddChangedObject(ToObject(data.instanceId));
#endif
                        }
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructure:
                        {
                            stream.GetChangeGameObjectStructureEvent(i, out var data);
#if UNITY_6000_4_OR_NEWER
                            AddChangedObject(ToObject(data.entityId));
#else
                            AddChangedObject(ToObject(data.instanceId));
#endif
                        }
                        break;
                    case ObjectChangeKind.ChangeGameObjectParent:
                        {
                            stream.GetChangeGameObjectParentEvent(i, out var data);
#if UNITY_6000_4_OR_NEWER
                            AddChangedObject(ToObject(data.entityId));
#else
                            AddChangedObject(ToObject(data.instanceId));
#endif
                        }
                        break;
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        {
                            stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var data);
#if UNITY_6000_4_OR_NEWER
                            AddChangedObject(ToObject(data.entityId));
#else
                            AddChangedObject(ToObject(data.instanceId));
#endif
                        }
                        break;
                    case ObjectChangeKind.UpdatePrefabInstances:
                        {
                            stream.GetUpdatePrefabInstancesEvent(i, out var data);
#if UNITY_6000_4_OR_NEWER
                            for (int j = 0; j < data.entityIds.Length; j++)
                                AddChangedObject(ToObject(data.entityIds[j]));
#else
                            for (int j = 0; j < data.instanceIds.Length; j++)
                                AddChangedObject(ToObject(data.instanceIds[j]));
#endif
                        }
                        break;
                }
            }
        }

        private void RefreshPreview(bool updateAnimationWindowSelection = false)
        {
            ReleasePreview();

            trimFirstFrame = 0;
            trimLastFrame = recordClip != null ? EditorCommon.GetLastFrame(recordClip.length, recordClip.frameRate) : 0;

            if (rootObject != null)
            {
                var previewObject = AnimationCommon.InstantiateForPreviewHasAnimator(rootObject);
                try
                {
                    if (previewObject.TryGetComponent<Animator>(out var tmpAnimator))
                    {
                        tmpAnimator.applyRootMotion = false;
                        tmpAnimator.avatar = null;
                    }
                    uAvatarPreview = new UAvatarPreview(recordClip, previewObject);
                }
                finally
                {
                    GameObject.DestroyImmediate(previewObject);
                }

                uAnimationClipEditor = new UAnimationClipEditor(recordClip, uAvatarPreview);

                uAvatarPreview.SetApplyRootMotion(false);
                uAvatarPreview.Playing = true;

                if (updateAnimationWindowSelection)
                {
                    Selection.activeObject = recordClip;
                }
            }
        }

        private void SaveAnimationClip(ModelImporterAnimationType animationType)
        {
            var fileName = EditorCommon.GetSafeFileName($"{rootObject.name}_{recordClip.name}_{animationType}");
            var assetPath = $"Assets/{fileName}.anim";
            var uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            string path = EditorCommon.SaveFilePanelInAssets("Export new animation clip", Path.GetDirectoryName(uniquePath), Path.GetFileName(uniquePath), "anim");
            if (path == null)
                return;

            try
            {
                var beginTime = EditorCommon.SnapToFrame(trimFirstFrame / recordClip.frameRate, recordClip.frameRate);
                var endTime = EditorCommon.SnapToFrame(trimLastFrame / recordClip.frameRate, recordClip.frameRate);
                var newClip = AnimationCommon.CreateNewTrimClip(path, recordClip, beginTime, endTime);

                if (ApplyRootMotion)
                {
                    AnimationCommon.RemoveStartOffset(newClip);
                }

                string genericRootMotionBonePath = null;
                if (animationType == ModelImporterAnimationType.Human)
                {
                    AnimationCommon.ConvertToHumanoidClip(newClip, rootObject);
                }
                else if (animationType == ModelImporterAnimationType.Generic)
                {
                    #region Generic
                    if (ApplyRootMotion && animator.avatar != null)
                    {
                        UAvatar uAvatar = new();
                        genericRootMotionBonePath = uAvatar.GetGenericRootMotionBonePath(animator.avatar);
                        if (animator.isHuman)
                        {
                            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                            if (hips != null)
                                genericRootMotionBonePath = AnimationUtility.CalculateTransformPath(hips, rootObject.transform);
                        }
                        if (!string.IsNullOrEmpty(genericRootMotionBonePath))
                        {
                            var rootActiveBinding = AnimationCommon.Binding.Active(genericRootMotionBonePath);
                            var rootMotionBone = AnimationUtility.GetAnimatedObject(rootObject, rootActiveBinding) as GameObject;
                            if (rootMotionBone != null)
                            {
                                AnimationCommon.TransferRootMotionToRootNodeTransform(newClip, rootObject, rootMotionBone);
                            }
                        }
                    }
                    #endregion
                }
                else if (animationType == ModelImporterAnimationType.Legacy)
                {
                    newClip.legacy = true;
                }

                var bindingValueTypes = new Dictionary<EditorCurveBinding, Type>(animatableBindingsFloatData != null ? animatableBindingsFloatData.Count : 0);
                if (animatableBindingsFloatData != null)
                {
                    foreach (var objData in animatableBindingsFloatData)
                        bindingValueTypes[objData.binding] = objData.valueType;
                }

                Type GetBindingFloatValueType(EditorCurveBinding binding)
                {
                    if (bindingValueTypes.TryGetValue(binding, out var valueType))
                        return valueType;
                    return AnimationUtility.GetEditorCurveValueType(rootObject, binding);
                }

                bool keyframeReductionSuccess = false;
                if (animationCompression == AnimationCompression.KeyframeReduction ||
                    animationCompression == AnimationCompression.KeyframeReductionAndCompression)
                {
                    #region KeyframeReduction
                    EditorUtility.DisplayProgressBar("Export animation clip - Keyframe Reduction", "", 0f);

                    AssetDatabase.Refresh();

                    AnimationClip tmpClip = null;
                    GameObject tmpObject = null;

                    try
                    {
                        var tmpFileName = EditorCommon.GetSafeFileName($"{newClip.name}_tmp");
                        var tmpAssetPath = $"{EditorCommon.GetAssetPath(newClip)}/{tmpFileName}.dae";
                        tmpAssetPath = AssetDatabase.GenerateUniqueAssetPath(tmpAssetPath);
                        var tmpPath = Application.dataPath + tmpAssetPath["Assets".Length..];

                        tmpClip = AnimationClip.Instantiate(newClip);
                        tmpClip.hideFlags |= HideFlags.HideAndDontSave;
                        tmpObject = AnimationCommon.InstantiateForPreview(rootObject);

                        if (animationType == ModelImporterAnimationType.Legacy)
                        {
                            if (tmpObject.TryGetComponent<Animator>(out var tmpAnimator))
                                Animator.DestroyImmediate(tmpAnimator);
                            if (!tmpObject.TryGetComponent<Animation>(out var _))
                                tmpObject.AddComponent<Animation>();
                        }
                        else
                        {
                            if (tmpObject.TryGetComponent<Animation>(out var tmpAnimation))
                                Animation.DestroyImmediate(tmpAnimation);
                            if (!tmpObject.TryGetComponent<Animator>(out var _))
                                tmpObject.AddComponent<Animator>();
                        }

                        AnimationCommon.AddMissingTransforms(tmpObject, tmpClip);
                        var otherCurveDic = AnimationCommon.ConvertForKeyframeReduction(tmpObject, tmpClip);

                        DaeExporter exporter = new()
                        {
                            settings_activeOnly = false,
                            settings_exportMesh = false,
                            settings_iKOnFeet = false,
                            settings_animationRigging = false,
                            settings_animationType = animationType,
                            settings_motionNodePath = genericRootMotionBonePath,
                        };
                        {
                            if (tmpObject.TryGetComponent<Animator>(out var tmpAnimator))
                                exporter.settings_avatar = tmpAnimator.avatar;
                        }
                        var result = exporter.Export(tmpPath, tmpObject.GetComponentsInChildren<Transform>(true), new AnimationClip[] { tmpClip });
                        if (result)
                        {
                            try
                            {
                                AnimationClip reductionClip = null;
                                if (exporter.exportedFiles.Count >= 2)
                                {
                                    var subAssetPath = FileUtil.GetProjectRelativePath(exporter.exportedFiles[1]);
                                    var importer = AssetImporter.GetAtPath(subAssetPath);
                                    if (importer is ModelImporter modelImporter)
                                    {
                                        modelImporter.importAnimation = true;
                                        modelImporter.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
                                        modelImporter.animationRotationError = keyframeReduction_RotationError;
                                        modelImporter.animationPositionError = keyframeReduction_PositionError;
                                        modelImporter.animationScaleError = keyframeReduction_ScaleError;
                                        modelImporter.SaveAndReimport();
                                    }
                                    reductionClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(subAssetPath);
                                }
                                if (reductionClip != null)
                                {
                                    var bindings = AnimationUtility.GetCurveBindings(newClip);
                                    var datas = AnimationCommon.ImportByKeyframeReduction(newClip, reductionClip, otherCurveDic, 
                                                                                            keyframeReduction_EnableAnimator, 
                                                                                            keyframeReduction_EnableAnimatorRootAndIKGoal,
                                                                                            keyframeReduction_EnableTransform,
                                                                                            keyframeReduction_EnableOther);
                                    #region UpdateTangents
                                    {
                                        for (int i = 0; i < bindings.Length; i++)
                                        {
                                            var binding = bindings[i];

                                            EditorUtility.DisplayProgressBar("Export animation clip - Update Tangents", AnimationCommon.GetBindingDisplayName(binding), (float)i / bindings.Length);

                                            if (datas.ContainsKey(binding))
                                                continue;
                                            var curve = AnimationUtility.GetEditorCurve(newClip, binding);
                                            var valueType = GetBindingFloatValueType(binding);
                                            AnimationCommon.SetAnimationCurveTangent(curve, valueType);
                                            datas.Add(binding, curve);
                                        }
                                    }
                                    #endregion
                                    AnimationCommon.SetEditorCurves(newClip, datas, true);
                                    keyframeReductionSuccess = true;
                                }
                            }
                            finally
                            {
                                foreach (var p in exporter.exportedFiles)
                                {
                                    var pTmp = FileUtil.GetProjectRelativePath(p);
                                    AssetDatabase.DeleteAsset(pTmp);
                                }
                                AssetDatabase.Refresh();
                            }
                        }
                        if (keyframeReduction_EnableOther)
                        {
                            AnimationCommon.SimpleReductionKeyframe(newClip, rootObject);
                        }
                    }
                    finally
                    {
                        if (tmpClip != null)
                            AnimationClip.DestroyImmediate(tmpClip);
                        if (tmpObject != null)
                            GameObject.DestroyImmediate(tmpObject);
                    }
                    #endregion
                }
                if (!keyframeReductionSuccess)
                {
                    #region UpdateTangents
                    var bindings = AnimationUtility.GetCurveBindings(newClip);
                    var datas = new Dictionary<EditorCurveBinding, AnimationCurve>(bindings.Length);
                    for (int i = 0; i < bindings.Length; i++)
                    {
                        var binding = bindings[i];

                        EditorUtility.DisplayProgressBar("Export animation clip - Update Tangents", AnimationCommon.GetBindingDisplayName(binding), (float)i / bindings.Length);

                        var curve = AnimationUtility.GetEditorCurve(newClip, binding);
                        var valueType = GetBindingFloatValueType(binding);
                        AnimationCommon.SetAnimationCurveTangent(curve, valueType);
                        datas.Add(binding, curve);
                    }
                    AnimationCommon.SetEditorCurves(newClip, datas);
                    #endregion
                }

                newClip.EnsureQuaternionContinuity();

                #region Compression 
                if (animationCompression == AnimationCompression.KeyframeReductionAndCompression)
                {
                    AnimationCommon.ConvertToRawEuler(newClip);

                    AssetDatabase.Refresh();

                    var so = new SerializedObject(newClip);
                    {
                        var sp = so.FindProperty("m_UseHighQualityCurve");
                        sp.boolValue = animationType == ModelImporterAnimationType.Legacy;
                    }
                    so.ApplyModifiedProperties();
                }
                #endregion

                AssetDatabase.SaveAssetIfDirty(newClip);
                AssetDatabase.Refresh();

                EditorCommon.PingObject(newClip);
                Selection.activeObject = newClip;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
        private void SaveFBX()
        {
#if VERYANIMATION_FBX
            var name = rootObject.name;
            var fileName = EditorCommon.GetSafeFileName($"{name}_{recordClip.name}");
            var uniquePath = AssetDatabase.GenerateUniqueAssetPath($"Assets/{fileName}.fbx");
            string path = EditorUtility.SaveFilePanel("Create new FBX", Path.GetDirectoryName(uniquePath), Path.GetFileName(uniquePath), "fbx");
            if (!string.IsNullOrEmpty(path))
            {
                AnimationClip newClip = null;
                UnityEditor.Animations.AnimatorController controller = null;
                GameObject tmpObject = null;
                try
                {
                    var beginTime = EditorCommon.SnapToFrame(trimFirstFrame / recordClip.frameRate, recordClip.frameRate);
                    var endTime = EditorCommon.SnapToFrame(trimLastFrame / recordClip.frameRate, recordClip.frameRate);
                    newClip = AnimationCommon.CreateNewTrimClip(null, recordClip, beginTime, endTime);
                    newClip.name = name;
                    newClip.hideFlags |= HideFlags.HideAndDontSave;

                    controller = new UnityEditor.Animations.AnimatorController()
                    {
                        name = name,
                    };
                    controller.hideFlags |= HideFlags.HideAndDontSave;
                    {
                        var layer = new UnityEditor.Animations.AnimatorControllerLayer
                        {
                            name = "Base Layer"
                        };
                        {
                            layer.stateMachine = new AnimatorStateMachine
                            {
                                name = layer.name
                            };
                            layer.stateMachine.hideFlags |= HideFlags.HideAndDontSave;
                            {
                                var state = new AnimatorState();
                                state.hideFlags |= HideFlags.HideAndDontSave;
                                state.name = "Animation";
                                state.motion = newClip;
                                layer.stateMachine.states = new ChildAnimatorState[]
                                {
                                    new()
                                    {
                                        state = state,
                                    },
                                };
                            }
                        }
                        controller.layers = new UnityEditor.Animations.AnimatorControllerLayer[] { layer };
                    }

                    EditorUtility.DisplayProgressBar("Save FBX", path, 0f);

                    tmpObject = AnimationCommon.InstantiateForPreviewHasAnimator(rootObject);
                    {
                        var animators = tmpObject.GetComponentsInChildren<Animator>(true);
                        foreach (Animator animator in animators)
                        {
                            if (animator.gameObject == tmpObject)
                            {
                                animator.applyRootMotion = false;
                                animator.avatar = null;
                                UnityEditor.Animations.AnimatorController.SetAnimatorController(animator, controller);
                            }
                            else
                            {
                                Animator.DestroyImmediate(animator);
                            }
                        }
                        var animations = tmpObject.GetComponentsInChildren<Animation>(true);
                        foreach (Animation animation in animations)
                        {
                            Animation.DestroyImmediate(animation);
                        }
                    }

                    if (ApplyRootMotion)
                    {
                        AnimationCommon.RemoveStartOffset(newClip);
                    }

                    EditorUtility.DisplayProgressBar("Save FBX", path, 0.5f);

                    //This is a workaround—not normally required—to address the issue where exported tangents can end up in an abnormal state.
                    AnimationCommon.BakeKeyframesForFbxExport(newClip);

                    EditorUtility.DisplayProgressBar("Save FBX", path, 1f);

#if VERYANIMATION_FBX_5
                    ModelExporter.ExportObject(path, tmpObject, exportModelOptions);
#else
                    ModelExporter.ExportObject(path, tmpObject);
#endif
                    AssetDatabase.Refresh();

                    #region ImporterSettings
                    if (path.StartsWith(Application.dataPath, StringComparison.Ordinal) &&
                        File.Exists(path))
                    {
                        var assetPath = FileUtil.GetProjectRelativePath(path);
                        AssetDatabase.ImportAsset(assetPath);
                        var importer = AssetImporter.GetAtPath(assetPath);
                        if (importer is ModelImporter modelImporter)
                        {
                            modelImporter.animationType = animator != null && animator.isHuman ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
                            switch (animationCompression)
                            {
                                case AnimationCompression.Off:
                                    modelImporter.animationCompression = ModelImporterAnimationCompression.Off;
                                    break;
                                case AnimationCompression.KeyframeReduction:
                                    modelImporter.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
                                    break;
                                case AnimationCompression.KeyframeReductionAndCompression:
                                    modelImporter.animationCompression = ModelImporterAnimationCompression.Optimal;
                                    break;
                            }
                            modelImporter.animationPositionError = keyframeReduction_PositionError;
                            modelImporter.animationRotationError = keyframeReduction_RotationError;
                            modelImporter.animationScaleError = keyframeReduction_ScaleError;
                            if (modelImporter.defaultClipAnimations != null)
                            {
                                var setClips = modelImporter.defaultClipAnimations;
                                foreach (var setClip in setClips)
                                {
                                    setClip.keepOriginalOrientation = true;
                                    setClip.keepOriginalPositionY = true;
                                    setClip.keepOriginalPositionXZ = true;
                                    setClip.lockRootHeightY = !ApplyRootMotion;
                                    setClip.lockRootPositionXZ = !ApplyRootMotion;
                                    setClip.lockRootRotation = !ApplyRootMotion;
                                }
                                modelImporter.clipAnimations = setClips;
                            }
                            modelImporter.SaveAndReimport();
                        }

                        AssetDatabase.Refresh();

                        {
                            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
                            if (clip != null && clip.frameRate != newClip.frameRate)
                            {
                                Debug.LogWarningFormat("<color=blue>[Very Animation]</color>{0}", Language.GetText(Language.Help.RecorderWindowFBXFrameRateWarning));
                            }
                        }
                        {
                            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                            if (asset != null)
                            {
                                EditorCommon.PingObject(asset);
                                Selection.activeObject = asset;
                            }
                        }
                    }
                    #endregion
                }
                finally
                {
                    if (controller != null)
                    {
                        foreach (var layer in controller.layers)
                        {
                            var stateMachine = layer.stateMachine;
                            if (stateMachine == null)
                                continue;
                            foreach (var childState in stateMachine.states)
                            {
                                if (childState.state != null)
                                    AnimatorState.DestroyImmediate(childState.state);
                            }
                            AnimatorStateMachine.DestroyImmediate(stateMachine);
                        }
                        UnityEditor.Animations.AnimatorController.DestroyImmediate(controller);
                    }

                    if (tmpObject != null)
                        GameObject.DestroyImmediate(tmpObject);
                    if (newClip != null)
                        AnimationClip.DestroyImmediate(newClip);
                    EditorUtility.ClearProgressBar();
                }
            }
#endif
        }
    }
}
