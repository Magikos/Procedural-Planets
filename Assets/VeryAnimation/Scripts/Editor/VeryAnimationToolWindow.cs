using UnityEngine;
using UnityEngine.Assertions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEditorInternal;
using System;
using System.Collections.Generic;

#if VERYANIMATION_TIMELINE
using UnityEngine.Timeline;
#endif

namespace VeryAnimation
{
    [Serializable]
    internal sealed class VeryAnimationToolWindow : EditorWindow
    {
        public static VeryAnimationToolWindow instance;

        [MenuItem("Window/Very Animation/Tools", priority = 1001)]
        public static void Open()
        {
            GetWindow<VeryAnimationToolWindow>();
        }

        #region Reflection
        public UEditorGUI UEditorGUI { get; private set; }
        public UAnimatorController UAnimatorController { get; private set; }
        #endregion

        #region GUIStyles
        class GUIStyles
        {
            public GUIStyle guiStyleBoldButton;
            public GUIStyle guiStyleIconButton;
            public GUIStyle guiStyleIconActiveButton;

            public GUIStyles()
            {
                Assert.IsNotNull(Event.current, "GUIStyles must be created during OnGUI (GUI.skin requires event context)");
                guiStyleBoldButton = new GUIStyle(GUI.skin.button)
                {
                    fontStyle = FontStyle.Bold
                };
                guiStyleIconButton = new GUIStyle("IconButton");
                guiStyleIconActiveButton = new GUIStyle(GUI.skin.button)
                {
                    padding = new RectOffset(0, 0, 0, 0)
                };
                guiStyleIconActiveButton.normal = guiStyleIconActiveButton.active;
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

        internal enum ToolMode
        {
            ResetPose,
            TemplatePose,
            RemoveSaveSettings,
            ReplaceReference,
            AnimationRigging,
        }
        public ToolMode toolMode;
        public bool toolsHelp = true;
        public PoseTemplate toolPoseTemplate;
        public AnimationClip toolReplaceReference_OldClip;
        public AnimationClip toolReplaceReference_NewClip;

#if VERYANIMATION_ANIMATIONRIGGING
        internal enum AnimationRiggingMode
        {
            Humanoid,
            CopyFromOtherSource,
        }
        private static readonly string[] AnimationRiggingModeStrings =
        {
            "Humanoid",
            "Copy From Other Source",
        };
        public AnimationRiggingMode toolAnimationRigging_Mode;
        public bool[] toolAnimationRigging_HumanoidTargets;
        public VeryAnimationSaveSettings toolAnimationRigging_Source;
        public bool toolAnimationRigging_VARigSetScale;
#endif

        private GameObject activeRootObject;

        private void OnEnable()
        {
            instance = this;

            EditorSettings.SetGlobalSetting();

            UEditorGUI = new UEditorGUI();
            UAnimatorController = new UAnimatorController();

            titleContent = new GUIContent("VA Tools");
            minSize = new Vector2(320, minSize.y);

            #region Initialize
            {
#if VERYANIMATION_ANIMATIONRIGGING
                toolAnimationRigging_HumanoidTargets = new bool[(int)AnimatorIKCore.IKTarget.Total];
#endif
            }
            #endregion

            OnSelectionChange();

            InternalEditorUtility.RepaintAllViews();
        }
        private void OnDestroy()
        {
            instance = null;
        }

        private void OnSelectionChange()
        {
            UpdateSelection();
            Repaint();
        }

        private void OnFocus()
        {
            UpdateSelection();
            Repaint();
        }

        private void OnHierarchyChange()
        {
            UpdateSelection();
            Repaint();
        }

        private void OnGUI()
        {
            if (VeryAnimationWindow.instance != null && VeryAnimationWindow.instance.Initialized)
            {
                EditorGUILayout.HelpBox(Language.GetText(Language.Help.ToolsWindowVAEditing), MessageType.Warning);
            }
            else
            {
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUI.BeginChangeCheck();
                        var mode = (ToolMode)EditorGUILayout.EnumPopup(toolMode);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(this, "Change Tool Mode");
                            toolMode = mode;
                        }
                    }
                    {
                        if (GUILayout.Button(UEditorGUI.GUIContents.GetHelpIcon(), toolsHelp ? Styles.guiStyleIconActiveButton : Styles.guiStyleIconButton, GUILayout.Width(19)))
                        {
                            Undo.RecordObject(this, "Change Tool Help");
                            toolsHelp = !toolsHelp;
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel++;
                if (toolsHelp)
                {
                    EditorGUILayout.HelpBox(Language.GetText(Language.Help.ToolsWindowHelpResetPose + (int)toolMode), MessageType.Info);
                }
                if (toolMode == ToolMode.ResetPose)
                {
                    #region ResetPose
                    var activePrefab = Selection.activeGameObject != null ? PrefabUtility.GetCorrespondingObjectFromSource(Selection.activeGameObject) as GameObject : null;
                    bool disable = Selection.activeGameObject == null || activePrefab == null;
                    {
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.ObjectField("Game Object", Selection.activeGameObject, typeof(GameObject), false);
                        EditorGUI.EndDisabledGroup();
                    }
                    {
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.ObjectField("Prefab", activePrefab, typeof(GameObject), false);
                        EditorGUI.EndDisabledGroup();
                    }
                    if (activeRootObject != null)
                    {
                        var animator = activeRootObject.GetComponent<Animator>();
                        if (animator != null && !animator.hasTransformHierarchy)
                        {
                            EditorGUILayout.HelpBox(Language.GetText(Language.Help.Editingonoptimizedtransformhierarchyisnotsupported), MessageType.Error);
                            disable = true;
                        }
                    }
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.Space();
                        EditorGUI.BeginDisabledGroup(disable);
                        if (GUILayout.Button("Reset Pose"))
                        {
                            ToolsResetPose(Selection.activeGameObject, activePrefab);
                        }
                        EditorGUI.EndDisabledGroup();
                        EditorGUILayout.Space();
                        EditorGUILayout.EndHorizontal();
                    }
                    #endregion
                }
                else if (toolMode == ToolMode.TemplatePose)
                {
                    #region TemplatePose
                    bool disable = activeRootObject == null || toolPoseTemplate == null;
                    Animator animator = activeRootObject != null ? activeRootObject.GetComponent<Animator>() : null;
                    Animation animation = activeRootObject != null ? activeRootObject.GetComponent<Animation>() : null;
                    {
                        EditorGUI.BeginDisabledGroup(true);
                        if (animation != null)
                            EditorGUILayout.ObjectField("Animation", animation, typeof(Animation), false);
                        else
                            EditorGUILayout.ObjectField("Animator", animator, typeof(Animator), false);
                        EditorGUI.EndDisabledGroup();
                    }
                    {
                        EditorGUI.BeginChangeCheck();
                        var poseTemplate = EditorGUILayout.ObjectField("Template", toolPoseTemplate, typeof(PoseTemplate), false) as PoseTemplate;
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(this, "Change Template");
                            toolPoseTemplate = poseTemplate;
                        }
                    }
                    if (activeRootObject != null)
                    {
                        if (animator != null && !animator.hasTransformHierarchy)
                        {
                            EditorGUILayout.HelpBox(Language.GetText(Language.Help.Editingonoptimizedtransformhierarchyisnotsupported), MessageType.Error);
                            disable = true;
                        }
                    }
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.Space();
                        EditorGUI.BeginDisabledGroup(disable);
                        if (GUILayout.Button("Set Pose"))
                        {
                            ToolsTemplatePose();
                        }
                        EditorGUI.EndDisabledGroup();
                        EditorGUILayout.Space();
                        EditorGUILayout.EndHorizontal();
                    }
                    #endregion
                }
                else if (toolMode == ToolMode.RemoveSaveSettings)
                {
                    #region RemoveSaveSettings
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.Space();
                        if (GUILayout.Button("Remove All"))
                        {
                            if (EditorUtility.DisplayDialog(Language.GetText(Language.Help.DisplayDialogAnimationRemoveSaveSettings),
                                                            Language.GetTooltip(Language.Help.DisplayDialogAnimationRemoveSaveSettings), "ok", "cancel"))
                            {
                                EditorApplication.delayCall += () => ToolsRemoveSaveSettings();
                            }
                        }
                        EditorGUILayout.Space();
                        EditorGUILayout.EndHorizontal();
                    }
                    #endregion
                }
                else if (toolMode == ToolMode.ReplaceReference)
                {
                    #region ReplaceReference
                    {
                        EditorGUI.BeginChangeCheck();
                        var oldClip = (AnimationClip)EditorGUILayout.ObjectField("Old Animation Clip", toolReplaceReference_OldClip, typeof(AnimationClip), false);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(this, "Change Old Clip");
                            toolReplaceReference_OldClip = oldClip;
                        }
                    }
                    {
                        EditorGUI.BeginChangeCheck();
                        var newClip = (AnimationClip)EditorGUILayout.ObjectField("New Animation Clip", toolReplaceReference_NewClip, typeof(AnimationClip), false);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(this, "Change New Clip");
                            toolReplaceReference_NewClip = newClip;
                        }
                    }
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.Space();
                        EditorGUI.BeginDisabledGroup(toolReplaceReference_OldClip == null || toolReplaceReference_NewClip == null || toolReplaceReference_OldClip == toolReplaceReference_NewClip);
                        if (GUILayout.Button("Replace Reference"))
                        {
                            if (EditorUtility.DisplayDialog(Language.GetText(Language.Help.DisplayDialogAnimationReplaceReference),
                                                            Language.GetTooltip(Language.Help.DisplayDialogAnimationReplaceReference), "ok", "cancel"))
                            {
                                EditorApplication.delayCall += () => ToolsReplaceReference();
                            }
                        }
                        EditorGUI.EndDisabledGroup();
                        EditorGUILayout.Space();
                        EditorGUILayout.EndHorizontal();
                    }
                    #endregion
                }
                else if (toolMode == ToolMode.AnimationRigging)
                {
                    #region AnimationRigging
#if VERYANIMATION_ANIMATIONRIGGING
                    Animator animator = activeRootObject != null ? activeRootObject.GetComponent<Animator>() : null;
                    bool disableHierarchy = false;
                    {
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.ObjectField("Animator", animator, typeof(Animator), false);
                        EditorGUI.EndDisabledGroup();
                    }
                    if (animator != null && !animator.hasTransformHierarchy)
                    {
                        EditorGUILayout.HelpBox(Language.GetText(Language.Help.Editingonoptimizedtransformhierarchyisnotsupported), MessageType.Error);
                        disableHierarchy = true;
                    }
                    {
                        EditorGUI.BeginChangeCheck();
                        var mode = (AnimationRiggingMode)GUILayout.Toolbar((int)toolAnimationRigging_Mode, AnimationRiggingModeStrings, EditorStyles.miniButton);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(this, "Change Mode");
                            toolAnimationRigging_Mode = mode;
                        }
                    }
                    if (toolAnimationRigging_Mode == AnimationRiggingMode.Humanoid)
                    {
                        bool disableSelect = true;
                        bool isHuman = animator != null && animator.isHuman;
                        EditorGUI.BeginDisabledGroup(!isHuman);
                        for (int i = 0; i < toolAnimationRigging_HumanoidTargets.Length; i++)
                        {
                            EditorGUI.BeginChangeCheck();
                            var flag = EditorGUILayout.Toggle(AnimatorIKCore.IKTargetStrings[i], toolAnimationRigging_HumanoidTargets[i]);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, "Change Flag");
                                toolAnimationRigging_HumanoidTargets[i] = flag;
                            }
                            if (toolAnimationRigging_HumanoidTargets[i])
                                disableSelect = false;
                        }
                        EditorGUI.EndDisabledGroup();
                        {
                            EditorGUI.BeginDisabledGroup(animator == null);
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.Space();
                            if (GUILayout.Button("Delete All"))
                            {
                                ToolsAnimationRiggingDeleteAll();
                            }
                            EditorGUILayout.Space();
                            EditorGUI.BeginDisabledGroup(disableHierarchy | disableSelect | !isHuman);
                            if (GUILayout.Button("Create"))
                            {
                                ToolsAnimationRiggingHumanoidCreate();
                            }
                            EditorGUI.EndDisabledGroup();
                            EditorGUILayout.Space();
                            EditorGUILayout.EndHorizontal();
                            EditorGUI.EndDisabledGroup();
                        }
                    }
                    else if (toolAnimationRigging_Mode == AnimationRiggingMode.CopyFromOtherSource)
                    {
                        bool disableSelect = true;
                        {
                            EditorGUI.BeginChangeCheck();
                            var source = EditorGUILayout.ObjectField("Source", toolAnimationRigging_Source, typeof(VeryAnimationSaveSettings), true);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, "Change Source");
                                toolAnimationRigging_Source = source as VeryAnimationSaveSettings;
                            }
                            if (toolAnimationRigging_Source != null)
                                disableSelect = false;
                        }
                        {
                            EditorGUI.BeginChangeCheck();
                            var flag = EditorGUILayout.Toggle("Set VARig Scale", toolAnimationRigging_VARigSetScale);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(this, "Change Flag");
                                toolAnimationRigging_VARigSetScale = flag;
                            }
                        }
                        {
                            EditorGUI.BeginDisabledGroup(animator == null);
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.Space();
                            if (GUILayout.Button("Delete All"))
                            {
                                ToolsAnimationRiggingDeleteAll();
                            }
                            EditorGUILayout.Space();
                            EditorGUI.BeginDisabledGroup(disableHierarchy | disableSelect);
                            if (GUILayout.Button("Create"))
                            {
                                ToolsAnimationRiggingSourceCreate();
                            }
                            EditorGUI.EndDisabledGroup();
                            EditorGUILayout.Space();
                            EditorGUILayout.EndHorizontal();
                            EditorGUI.EndDisabledGroup();
                        }
                    }
#else
                    if (!toolsHelp)
                    {
                        EditorGUILayout.HelpBox(Language.GetText(Language.Help.ToolsWindowHelpAnimationRigging), MessageType.Warning);
                    }
#endif
                    #endregion
                }
                EditorGUI.indentLevel--;
            }
        }

        private void UpdateSelection()
        {
            if (Selection.activeGameObject != null)
            {
                #region Animator
                {
                    activeRootObject = Selection.activeGameObject;
                    var animator = activeRootObject.GetComponentInParent<Animator>();
                    if (animator == null)
                        activeRootObject = null;
                    else
                        activeRootObject = animator.gameObject;
                }
                #endregion
                #region Animation
                if (activeRootObject == null)
                {
                    activeRootObject = Selection.activeGameObject;
                    var animation = activeRootObject.GetComponentInParent<Animation>();
                    if (animation == null)
                        activeRootObject = null;
                    else
                        activeRootObject = animation.gameObject;
                }
                #endregion
            }
            else
            {
                activeRootObject = null;
            }
        }

        private void ToolsResetPose(GameObject activeGameObject, GameObject activePrefab)
        {
            var go = AnimationCommon.InstantiateForPreview(activePrefab);
            try
            {
                var srcTransforms = go.GetComponentsInChildren<Transform>(true);
                var srcList = new Dictionary<string, Transform>(srcTransforms.Length);
                foreach (var t in srcTransforms)
                    srcList.TryAdd(AnimationUtility.CalculateTransformPath(t, go.transform), t);

                foreach (var dstT in activeGameObject.GetComponentsInChildren<Transform>(true))
                {
                    if (dstT == activeGameObject.transform)
                        continue;

                    var path = AnimationUtility.CalculateTransformPath(dstT, activeGameObject.transform);
                    if (!srcList.TryGetValue(path, out Transform srcT))
                        continue;

                    Undo.RecordObject(dstT, "Reset Pose");
                    dstT.SetLocalPositionAndRotation(srcT.localPosition, srcT.localRotation);
                    dstT.localScale = srcT.localScale;
                    var dstR = dstT.GetComponent<SkinnedMeshRenderer>();
                    var srcR = srcT.GetComponent<SkinnedMeshRenderer>();
                    if (dstR != null && dstR.sharedMesh != null && dstR.sharedMesh.blendShapeCount > 0 &&
                        srcR != null && srcR.sharedMesh != null && srcR.sharedMesh.blendShapeCount > 0)
                    {
                        Undo.RecordObject(dstR, "Reset Pose");
                        for (int i = 0; i < dstR.sharedMesh.blendShapeCount; i++)
                        {
                            var dstName = dstR.sharedMesh.GetBlendShapeName(i);
                            var weight = 0f;
                            for (int j = 0; j < srcR.sharedMesh.blendShapeCount; j++)
                            {
                                if (dstName == srcR.sharedMesh.GetBlendShapeName(j))
                                {
                                    weight = srcR.GetBlendShapeWeight(j);
                                    break;
                                }
                            }
                            dstR.SetBlendShapeWeight(i, weight);
                        }
                    }
                }
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }
        private void ToolsTemplatePose()
        {
            var transforms = activeRootObject.GetComponentsInChildren<Transform>(true);
            Undo.RecordObjects(transforms, "Template Pose");
            var animator = activeRootObject.GetComponent<Animator>();
            if (animator != null && !animator.isInitialized)
                animator.Rebind();
            var save = new TransformPoseSave.SaveData(activeRootObject.transform);
            var pathIndices = new Dictionary<string, int>(transforms.Length);
            for (int i = 0; i < transforms.Length; i++)
                pathIndices.TryAdd(AnimationUtility.CalculateTransformPath(transforms[i], activeRootObject.transform), i);
            #region Human
            if (animator != null && animator.isHuman)
            {
                using var humanPoseHandler = new HumanPoseHandler(animator.avatar, animator.avatarRoot);
                var humanPose = new HumanPose();
                humanPoseHandler.GetHumanPose(ref humanPose);
                {
                    var musclePropertyName = new MusclePropertyName();
                    if (toolPoseTemplate.haveRootT)
                    {
                        humanPose.bodyPosition = toolPoseTemplate.rootT;
                    }
                    if (toolPoseTemplate.haveRootQ)
                    {
                        humanPose.bodyRotation = toolPoseTemplate.rootQ;
                    }
                    if (toolPoseTemplate.musclePropertyNames != null && toolPoseTemplate.muscleValues != null)
                    {
                        Assert.IsTrue(toolPoseTemplate.musclePropertyNames.Length == toolPoseTemplate.muscleValues.Length);
                        for (int i = 0; i < toolPoseTemplate.musclePropertyNames.Length; i++)
                        {
                            if (!musclePropertyName.PropertyNameDic.TryGetValue(toolPoseTemplate.musclePropertyNames[i], out var muscleIndex)) continue;
                            humanPose.muscles[muscleIndex] = toolPoseTemplate.muscleValues[i];
                        }
                    }
                    if (toolPoseTemplate.tdofIndices != null && toolPoseTemplate.tdofValues != null)
                    {
                        //not support
                    }
                }
                humanPoseHandler.SetHumanPose(ref humanPose);
            }
            #endregion
            #region Generic
            if (toolPoseTemplate.transformPaths != null && toolPoseTemplate.transformValues != null)
            {
                Assert.IsTrue(toolPoseTemplate.transformPaths.Length == toolPoseTemplate.transformValues.Length);
                for (int i = 0; i < toolPoseTemplate.transformPaths.Length; i++)
                {
                    if (!pathIndices.TryGetValue(toolPoseTemplate.transformPaths[i], out int index)) continue;
                    transforms[index].SetLocalPositionAndRotation(toolPoseTemplate.transformValues[i].position, toolPoseTemplate.transformValues[i].rotation);
                    transforms[index].localScale = toolPoseTemplate.transformValues[i].scale;
                }
            }
            #endregion
            #region BlendShape
            if (toolPoseTemplate.blendShapePaths != null && toolPoseTemplate.blendShapeValues != null)
            {
                foreach (var renderer in activeRootObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.blendShapeCount <= 0) continue;
                    Undo.RecordObject(renderer, "Template Pose");
                    var path = AnimationUtility.CalculateTransformPath(renderer.transform, activeRootObject.transform);
                    Assert.IsTrue(toolPoseTemplate.blendShapePaths.Length == toolPoseTemplate.blendShapeValues.Length);
                    var index = ArrayUtility.IndexOf(toolPoseTemplate.blendShapePaths, path);
                    if (index < 0 || index >= toolPoseTemplate.blendShapeValues.Length) continue;
                    var nameIndices = new Dictionary<string, int>(renderer.sharedMesh.blendShapeCount);
                    for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                    {
                        nameIndices.TryAdd(renderer.sharedMesh.GetBlendShapeName(i), i);
                    }
                    var names = toolPoseTemplate.blendShapeValues[index].names;
                    var weights = toolPoseTemplate.blendShapeValues[index].weights;
                    if (names == null || weights == null) continue;
                    Assert.IsTrue(names.Length == weights.Length);
                    var count = Mathf.Min(names.Length, weights.Length);
                    for (int i = 0; i < count; i++)
                    {
                        if (!nameIndices.TryGetValue(names[i], out int nameindex)) continue;
                        renderer.SetBlendShapeWeight(nameindex, weights[i]);
                    }
                }
            }
            #endregion
            save.LoadLocal(activeRootObject.transform);
        }
        private void ToolsRemoveSaveSettings()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            SaveActiveScenes(out string activeScenePath, out string[] addScenesPath);

            var searchInFolders = new[] { "Assets" };
            var prefabAssets = AssetDatabase.FindAssets("t:Prefab", searchInFolders);
            var sceneAssets = AssetDatabase.FindAssets("t:SceneAsset", searchInFolders);
            int progressIndex = 0;
            int progressTotal = prefabAssets.Length + sceneAssets.Length;
            bool cancelled = false;
            bool UpdateProgress(string title, string info)
            {
                cancelled |= EditorUtility.DisplayCancelableProgressBar(title, info, progressIndex++ / (float)progressTotal);
                return cancelled;
            }
            try
            {
                #region Prefab
                {
                    for (int i = 0; !cancelled && i < prefabAssets.Length; i++)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(prefabAssets[i]);
                        if (UpdateProgress("Prefab", path))
                            break;
                        try
                        {
                            var gameObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                            if (gameObject == null) continue;
                            var saveSettings = gameObject.GetComponentsInChildren<VeryAnimationSaveSettings>(true);
                            foreach (var cp in saveSettings)
                            {
                                if (cp == null)
                                    continue;
                                if (PrefabUtility.GetCorrespondingObjectFromSource(cp) != null)
                                    continue;
                                Undo.DestroyObjectImmediate(cp);
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                    AssetDatabase.SaveAssets();
                }
                #endregion
                #region Scene
                {
                    for (int i = 0; !cancelled && i < sceneAssets.Length; i++)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(sceneAssets[i]);
                        if (UpdateProgress("Scene", path))
                            break;
                        try
                        {
                            EditorSceneManager.OpenScene(path);
                            bool updated = false;
                            var scene = EditorSceneManager.GetActiveScene();
                            if (scene.isLoaded)
                            {
                                foreach (var go in scene.GetRootGameObjects())
                                {
                                    var saveSettings = go.GetComponentsInChildren<VeryAnimationSaveSettings>(true);
                                    foreach (var cp in saveSettings)
                                    {
                                        if (cp == null)
                                            continue;
                                        Undo.DestroyObjectImmediate(cp);
                                        updated = true;
                                    }
                                }
                            }
                            if (updated)
                            {
                                EditorSceneManager.MarkAllScenesDirty();
                                EditorSceneManager.SaveOpenScenes();
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                }
                #endregion
            }
            finally
            {
                LoadActiveScenes(activeScenePath, addScenesPath);
                Resources.UnloadUnusedAssets();
                EditorUtility.ClearProgressBar();
                InternalEditorUtility.RepaintAllViews();
            }
        }
        private void ToolsReplaceReference()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            SaveActiveScenes(out string activeScenePath, out string[] addScenesPath);

            var searchInFolders = new[] { "Assets" };
            var controllers = AssetDatabase.FindAssets("t:AnimatorController", searchInFolders);
            var ocontrollers = AssetDatabase.FindAssets("t:AnimatorOverrideController", searchInFolders);
#if VERYANIMATION_TIMELINE
            var timelines = AssetDatabase.FindAssets("t:TimelineAsset", searchInFolders);
#endif
            var prefabAssets = AssetDatabase.FindAssets("t:Prefab", searchInFolders);
            var sceneAssets = AssetDatabase.FindAssets("t:SceneAsset", searchInFolders);
            int progressIndex = 0;
            int progressTotal = controllers.Length + ocontrollers.Length + prefabAssets.Length + sceneAssets.Length;
#if VERYANIMATION_TIMELINE
            progressTotal += timelines.Length;
#endif
            bool cancelled = false;
            bool UpdateProgress(string title, string info)
            {
                cancelled |= EditorUtility.DisplayCancelableProgressBar(title, info, progressIndex++ / (float)progressTotal);
                return cancelled;
            }
            bool ReplaceAnimation(Animation animation)
            {
                if ((animation.hideFlags & HideFlags.NotEditable) != 0) return false;
                Undo.RecordObject(animation, "Replace Reference");
                bool changed = false;
                var anims = AnimationUtility.GetAnimationClips(animation.gameObject);
                for (int j = 0; j < anims.Length; j++)
                {
                    if (anims[j] == toolReplaceReference_OldClip)
                    {
                        anims[j] = toolReplaceReference_NewClip;
                        changed = true;
                    }
                }
                if (animation.clip == toolReplaceReference_OldClip)
                {
                    animation.clip = toolReplaceReference_NewClip;
                    changed = true;
                }
                if (changed)
                {
                    Debug.LogFormat("<color=blue>[Very Animation]</color>Replace Animation '{0}'", animation.name);
                    AnimationUtility.SetAnimationClips(animation, anims);
                }
                return changed;
            }

            try
            {
                #region AnimatorController
                for (int i = 0; !cancelled && i < controllers.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(controllers[i]);
                    if (UpdateProgress("Replace", path))
                        break;
                    var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
                    if (controller == null) continue;
                    if ((controller.hideFlags & HideFlags.NotEditable) != 0) continue;
                    var layers = controller.layers;
                    for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
                    {
                        void ReplaceStateMachine(AnimatorStateMachine stateMachine)
                        {
                            foreach (var state in stateMachine.states)
                            {
                                var motion = controller.GetStateEffectiveMotion(state.state, layerIndex);
                                if (motion is UnityEditor.Animations.BlendTree)
                                {
                                    Undo.RecordObject(state.state, "Duplicate and Replace");
                                    void ReplaceBlendTree(UnityEditor.Animations.BlendTree blendTree)
                                    {
                                        if (blendTree.children == null) return;
                                        Undo.RecordObject(blendTree, "Replace Reference");
                                        var children = blendTree.children;
                                        for (int j = 0; j < children.Length; j++)
                                        {
                                            if (children[j].motion is UnityEditor.Animations.BlendTree)
                                            {
                                                ReplaceBlendTree(children[j].motion as UnityEditor.Animations.BlendTree);
                                            }
                                            else
                                            {
                                                if (children[j].motion == toolReplaceReference_OldClip)
                                                {
                                                    children[j].motion = toolReplaceReference_NewClip;
                                                    Debug.LogFormat("<color=blue>[Very Animation]</color>Replace AnimatorController '{0} - {1}'", controller.name, state.state.name);
                                                }
                                            }
                                        }
                                        blendTree.children = children;
                                    }

                                    ReplaceBlendTree(motion as UnityEditor.Animations.BlendTree);
                                }
                                else
                                {
                                    if (motion == toolReplaceReference_OldClip)
                                    {
                                        controller.SetStateEffectiveMotion(state.state, toolReplaceReference_NewClip, layerIndex);
                                        Debug.LogFormat("<color=blue>[Very Animation]</color>Replace AnimatorController '{0} - {1}'", controller.name, state.state.name);
                                    }
                                }
                            }
                            foreach (var childStateMachine in stateMachine.stateMachines)
                            {
                                ReplaceStateMachine(childStateMachine.stateMachine);
                            }
                        }

                        var stateMachine = UAnimatorController.FindEffectiveRootStateMachine(controller, layerIndex);
                        ReplaceStateMachine(stateMachine);
                    }
                }
                #endregion
                #region AnimatorOverrideController
                for (int i = 0; !cancelled && i < ocontrollers.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(ocontrollers[i]);
                    if (UpdateProgress("Replace", path))
                        break;
                    var ocontroller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
                    if (ocontroller == null) continue;
                    if ((ocontroller.hideFlags & HideFlags.NotEditable) != 0) continue;
                    Undo.RecordObject(ocontroller, "Replace Reference");
                    List<KeyValuePair<AnimationClip, AnimationClip>> srcList = new();
                    ocontroller.GetOverrides(srcList);
                    List<KeyValuePair<AnimationClip, AnimationClip>> dstList = new();
                    bool changed = false;
                    foreach (var pair in srcList)
                    {
                        if (pair.Key == toolReplaceReference_OldClip || pair.Value == toolReplaceReference_OldClip)
                            changed = true;
                        dstList.Add(new KeyValuePair<AnimationClip, AnimationClip>(pair.Key != toolReplaceReference_OldClip ? pair.Key : toolReplaceReference_NewClip,
                                                                                    pair.Value != toolReplaceReference_OldClip ? pair.Value : toolReplaceReference_NewClip));
                    }
                    if (changed)
                    {
                        Debug.LogFormat("<color=blue>[Very Animation]</color>Replace AnimatorOverrideController '{0}'", ocontroller.name);
                        ocontroller.ApplyOverrides(dstList);
                    }
                }
                #endregion
                #region TimelineAsset
                {
#if VERYANIMATION_TIMELINE
                    void ReplaceAnimationTrack(TimelineAsset timeline, TrackAsset trackAsset)
                    {
                        var animationTrack = trackAsset as AnimationTrack;
                        if (animationTrack != null)
                        {
                            foreach (var timelineClip in animationTrack.GetClips())
                            {
                                var animationPlayableAsset = timelineClip.asset as AnimationPlayableAsset;
                                if (animationPlayableAsset == null) continue;
                                if (animationPlayableAsset.clip == toolReplaceReference_OldClip)
                                {
                                    Undo.RecordObject(animationPlayableAsset, "Replace Reference");
                                    Debug.LogFormat("<color=blue>[Very Animation]</color>Replace TimelineAsset '{0}/{1}'", timeline.name, animationTrack.name);
                                    animationPlayableAsset.clip = toolReplaceReference_NewClip;
                                }
                            }
                        }
                        foreach (var cTrackAsset in trackAsset.GetChildTracks())
                        {
                            ReplaceAnimationTrack(timeline, cTrackAsset);
                        }
                    }

                    for (int i = 0; !cancelled && i < timelines.Length; i++)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(timelines[i]);
                        if (UpdateProgress("Replace", path))
                            break;
                        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
                        if (timeline == null) continue;
                        if ((timeline.hideFlags & HideFlags.NotEditable) != 0) continue;

                        foreach (var trackAsset in timeline.GetRootTracks())
                        {
                            ReplaceAnimationTrack(timeline, trackAsset);
                        }
                    }
#endif
                }
                #endregion
                #region Prefab
                {
                    for (int i = 0; !cancelled && i < prefabAssets.Length; i++)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(prefabAssets[i]);
                        if (UpdateProgress("Prefab", path))
                            break;
                        var gameObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (gameObject == null) continue;
                        var animations = gameObject.GetComponentsInChildren<Animation>(true);
                        foreach (var animation in animations)
                        {
                            if (animation == null)
                                continue;
                            ReplaceAnimation(animation);
                        }
                    }
                    AssetDatabase.SaveAssets();
                }
                #endregion
                #region Scene
                {
                    for (int i = 0; !cancelled && i < sceneAssets.Length; i++)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(sceneAssets[i]);
                        if (UpdateProgress("Scene", path))
                            break;
                        try
                        {
                            EditorSceneManager.OpenScene(path);
                            bool updated = false;
                            var scene = EditorSceneManager.GetActiveScene();
                            if (scene.isLoaded)
                            {
                                foreach (var go in scene.GetRootGameObjects())
                                {
                                    var animations = go.GetComponentsInChildren<Animation>(true);
                                    foreach (var animation in animations)
                                    {
                                        if (animation == null)
                                            continue;
                                        updated |= ReplaceAnimation(animation);
                                    }
                                }
                            }
                            if (updated)
                            {
                                EditorSceneManager.MarkAllScenesDirty();
                                EditorSceneManager.SaveOpenScenes();
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                }
                #endregion
            }
            finally
            {
                LoadActiveScenes(activeScenePath, addScenesPath);
                Resources.UnloadUnusedAssets();
                EditorUtility.ClearProgressBar();
                InternalEditorUtility.RepaintAllViews();
            }
        }
#if VERYANIMATION_ANIMATIONRIGGING
        private void ToolsAnimationRiggingHumanoidCreate()
        {
            AnimationRigging.Create(activeRootObject);

            var newObjects = new List<GameObject>();
            for (int i = 0; i < toolAnimationRigging_HumanoidTargets.Length; i++)
            {
                if (!toolAnimationRigging_HumanoidTargets[i])
                    continue;
                var target = (AnimatorIKCore.IKTarget)i;
                var go = AnimatorIKCore.GetAnimationRiggingConstraint(activeRootObject, target);
                if (go != null)
                    continue;
                go = AnimatorIKCore.AddAnimationRiggingConstraint(activeRootObject, target);
                if (go != null)
                    newObjects.Add(go);
            }
            if (newObjects.Count > 0)
            {
                Selection.objects = newObjects.ToArray();
            }
        }
        private void ToolsAnimationRiggingSourceCreate()
        {
            if (toolAnimationRigging_Source == null)
                return;

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Animation Rigging");

            ToolsAnimationRiggingDeleteAll();
            AnimationRigging.Create(activeRootObject);

            var newObjects = new List<GameObject>();
            for (int i = 0; i < (int)AnimatorIKCore.IKTarget.Total; i++)
            {
                var target = (AnimatorIKCore.IKTarget)i;
                var go = AnimatorIKCore.GetAnimationRiggingConstraint(toolAnimationRigging_Source.gameObject, target);
                if (go == null)
                    continue;
                go = AnimatorIKCore.AddAnimationRiggingConstraint(activeRootObject, target);
                if (go != null)
                    newObjects.Add(go);
            }
            if (toolAnimationRigging_VARigSetScale)
            {
                var vaRig = activeRootObject.GetComponentInChildren<VeryAnimationRig>();
                var currentAnimator = activeRootObject.GetComponent<Animator>();
                var sourceAnimator = toolAnimationRigging_Source.GetComponent<Animator>();
                if (vaRig != null && currentAnimator != null && sourceAnimator != null)
                {
                    Undo.RecordObject(vaRig, "Change Transform Scale");
                    Undo.RecordObject(vaRig.transform, "Change Transform Scale");
                    vaRig.sourceHumanScale = sourceAnimator.humanScale;
                    vaRig.SetProperAdjustmentScale();
                }
            }
            if (newObjects.Count > 0)
            {
                Selection.objects = newObjects.ToArray();
            }
            Undo.CollapseUndoOperations(undoGroup);
        }
        private void ToolsAnimationRiggingDeleteAll()
        {
            var selectionObjects = Selection.objects;
            AnimationRigging.Delete(activeRootObject);
            Selection.objects = selectionObjects;
        }
#endif

        private void SaveActiveScenes(out string activeScenePath, out string[] addScenesPath)
        {
            activeScenePath = EditorSceneManager.GetActiveScene().path;
            addScenesPath = new String[EditorSceneManager.sceneCount];
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                addScenesPath[i] = EditorSceneManager.GetSceneAt(i).path;
            }
        }
        private void LoadActiveScenes(string activeScenePath, string[] addScenesPath)
        {
            if (string.IsNullOrEmpty(activeScenePath))
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);
            else
                EditorSceneManager.OpenScene(activeScenePath);
            foreach (var path in addScenesPath)
            {
                if (!string.IsNullOrEmpty(path) && path != activeScenePath)
                {
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                }
            }
        }
    }
}
