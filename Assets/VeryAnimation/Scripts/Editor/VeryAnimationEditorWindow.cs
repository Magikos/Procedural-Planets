//#define Enable_Profiler

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
#if Enable_Profiler
using UnityEngine.Profiling;
#endif

#if VERYANIMATION_ANIMATIONRIGGING
using UnityEngine.Animations.Rigging;
#endif

namespace VeryAnimation
{
    [Serializable]
    internal sealed class VeryAnimationEditorWindow : EditorWindow
    {
        public static VeryAnimationEditorWindow instance;

        private VeryAnimationWindow VAW => VeryAnimationWindow.instance;

        #region GUIStyles
        class GUIStyles
        {
            public const int FoldoutSpace = 17;
            public const int FloatFieldWidth = 44;

            public readonly GUIContent guiContentRootT;
            public readonly GUIContent guiContentRootQ;
            public readonly GUIContent guiContentHumanoidPosition;
            public readonly GUIContent guiContentHumanoidRotation;
            public readonly GUIContent guiContentMotionT;
            public readonly GUIContent guiContentMotionQ;
            public readonly GUIContent guiContentRigWeight;
            public readonly GUIContent guiContentRigConstraintWeight;
            public readonly GUIContent guiContentAnimationRiggingHeader = new("Animation Rigging");
            public readonly GUIContent guiContentLocalRotationButton = new("Rotation", "Local Rotation");
            public readonly GUIContent guiContentTDoFButton = new("Position", "Translation DoF");
            public readonly GUIContent[] rootCorrectionModeString = new GUIContent[(int)VeryAnimation.RootCorrectionMode.Total];

            public GUIStyles()
            {
                guiContentRootT = new GUIContent("Root T", "Root Position\nRootT * Animator.humanScale = Position");
                guiContentRootQ = new GUIContent("Root Q", "Root Rotation (Quaternion)");
                guiContentHumanoidPosition = new GUIContent("Position", "Local Position");
                guiContentHumanoidRotation = new GUIContent("Rotation", "Local Rotation (Euler)");
                guiContentMotionT = new GUIContent("Position", "MotionT");
                guiContentMotionQ = new GUIContent("Rotation", "MotionQ");
                guiContentRigWeight = new GUIContent("Weight", "Rig.weight");
                guiContentRigConstraintWeight = new GUIContent("Weight", "IRigConstraint.weight");
                UpdateLanguage();
                Language.OnLanguageChanged += UpdateLanguage;
            }
            private void UpdateLanguage()
            {
                for (int i = 0; i < (int)VeryAnimation.RootCorrectionMode.Total; i++)
                {
                    rootCorrectionModeString[i] = new GUIContent(Language.GetContent(Language.Help.EditorRootCorrectionDisable + i));
                }
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

        #region Undo Strings
        private const string UndoChangeMirror = "Change Mirror";
        private const string UndoBindPose = "Bind Pose";
        private const string UndoPrefabPose = "Prefab Pose";
        private const string UndoEditStartPose = "Edit Start Pose";
        #endregion

        #region EditorPrefs Keys
        private const string PrefKey_Extra = "VeryAnimation_Editor_Extra";
        private const string PrefKey_Pose = "VeryAnimation_Editor_Pose";
        private const string PrefKey_BlendPose = "VeryAnimation_Editor_BlendPose";
        private const string PrefKey_Muscle = "VeryAnimation_Editor_Muscle";
        private const string PrefKey_HandPose = "VeryAnimation_Editor_HandPose";
        private const string PrefKey_BlendShape = "VeryAnimation_Editor_BlendShape";
        private const string PrefKey_Selection = "VeryAnimation_Editor_Selection";
        private const string PrefKey_ExtraVisible = "VeryAnimation_Editor_ExtraVisible";
        private const string PrefKey_PoseVisible = "VeryAnimation_Editor_PoseVisible";
        private const string PrefKey_BlendPoseVisible = "VeryAnimation_Editor_BlendPoseVisible";
        private const string PrefKey_MuscleVisible = "VeryAnimation_Editor_MuscleVisible";
        private const string PrefKey_HandPoseVisible = "VeryAnimation_Editor_HandPoseVisible";
        private const string PrefKey_BlendShapeVisible = "VeryAnimation_Editor_BlendShapeVisible";
        private const string PrefKey_SelectionVisible = "VeryAnimation_Editor_SelectionVisible";
        private const string PrefKey_SelectionOnScene = "VeryAnimation_Editor_Selection_OnScene";
        private const string PrefKey_ClampMuscle = "VeryAnimation_ClampMuscle";
        private const string PrefKey_AutoFootIK = "VeryAnimation_AutoFootIK";
        private const string PrefKey_MirrorEnable = "VeryAnimation_MirrorEnable";
        private const string PrefKey_RootCorrectionMode = "VeryAnimation_RootCorrectionMode";
        private const string PrefKey_ExtraCollision = "VeryAnimation_ExtraCollision";
        private const string PrefKey_ExtraSynchronizeAnimation = "VeryAnimation_ExtraSynchronizeAnimation";
        private const string PrefKey_ExtraOnionSkin = "VeryAnimation_ExtraOnionSkin";
        private const string PrefKey_ExtraRootTrail = "VeryAnimation_ExtraRootTrail";
        #endregion

        #region GUI
        private bool editorExtraFoldout = true;
        private bool editorPoseFoldout = true;
        private bool editorBlendPoseFoldout = true;
        private bool editorMuscleFoldout = true;
        private bool editorHandPoseFoldout = true;
        private bool editorBlendShapeFoldout = true;
        private bool editorSelectionFoldout = true;

        private bool editorExtraVisible = true;
        private bool editorPoseVisible = true;
        private bool editorBlendPoseVisible = true;
        private bool editorMuscleVisible = true;
        private bool editorHandPoseVisible = true;
        private bool editorBlendShapeVisible = true;
        private bool editorSelectionVisible = true;

        public bool EditorSelectionOnScene { get; private set; }

        private bool editorExtraGroupHelp;
        private bool editorPoseGroupHelp;
        private bool editorBlendPoseGroupHelp;
        private bool editorMuscleGroupHelp;
        private bool editorHandPoseGroupHelp;
        private bool editorBlendShapeGroupHelp;
        private bool editorSelectionGroupHelp;
        #endregion

        #region Core
        [SerializeField]
        private ExtraTree extraTree;
        [SerializeField]
        private PoseTree poseTree;
        [SerializeField]
        private BlendPoseTree blendPoseTree;
        [SerializeField]
        private MuscleGroupTree muscleGroupTree;
        [SerializeField]
        private HandPoseTree handPoseTree;
        [SerializeField]
        private BlendShapeTree blendShapeTree;
        #endregion

        private bool initialized;

        private Vector2 editorScrollPosition;
        private Rect rangePinningDropDownButtonRect;

        public string TemplateSaveDefaultDirectory { get; set; }

        void OnEnable()
        {
            if (VAW == null || VAW.VA == null) return;

            instance = this;

            TemplateSaveDefaultDirectory = Application.dataPath;

            VAW.VA.OnHierarchyUpdated += UpdateHierarchyTree;

            titleContent = new GUIContent("VA Editor");
        }
        void OnDisable()
        {
            if (VAW != null && VAW.VA != null)
            {
                VAW.VA.OnHierarchyUpdated -= UpdateHierarchyTree;
            }

            Release();

            instance = null;
        }

        public void Initialize()
        {
            Release();

            extraTree = new ExtraTree();
            poseTree = new PoseTree();
            blendPoseTree = new BlendPoseTree();
            muscleGroupTree = new MuscleGroupTree();
            handPoseTree = new HandPoseTree();
            blendShapeTree = new BlendShapeTree();

            #region EditorPref
            {
                editorExtraFoldout = EditorPrefs.GetBool(PrefKey_Extra, false);
                editorPoseFoldout = EditorPrefs.GetBool(PrefKey_Pose, true);
                editorBlendPoseFoldout = EditorPrefs.GetBool(PrefKey_BlendPose, false);
                editorMuscleFoldout = EditorPrefs.GetBool(PrefKey_Muscle, false);
                editorHandPoseFoldout = EditorPrefs.GetBool(PrefKey_HandPose, true);
                editorBlendShapeFoldout = EditorPrefs.GetBool(PrefKey_BlendShape, true);
                editorSelectionFoldout = EditorPrefs.GetBool(PrefKey_Selection, true);

                editorExtraVisible = EditorPrefs.GetBool(PrefKey_ExtraVisible, true);
                editorPoseVisible = EditorPrefs.GetBool(PrefKey_PoseVisible, true);
                editorBlendPoseVisible = EditorPrefs.GetBool(PrefKey_BlendPoseVisible, false);
                editorMuscleVisible = EditorPrefs.GetBool(PrefKey_MuscleVisible, false);
                editorHandPoseVisible = EditorPrefs.GetBool(PrefKey_HandPoseVisible, true);
                editorBlendShapeVisible = EditorPrefs.GetBool(PrefKey_BlendShapeVisible, true);
                editorSelectionVisible = EditorPrefs.GetBool(PrefKey_SelectionVisible, true);

                EditorSelectionOnScene = EditorPrefs.GetBool(PrefKey_SelectionOnScene, false);

                VAW.VA.optionsClampMuscle = EditorPrefs.GetBool(PrefKey_ClampMuscle, false);
                VAW.VA.optionsAutoFootIK = EditorPrefs.GetBool(PrefKey_AutoFootIK, false);
                VAW.VA.optionsMirror = EditorPrefs.GetBool(PrefKey_MirrorEnable, false);
                VAW.VA.rootCorrectionMode = (VeryAnimation.RootCorrectionMode)EditorPrefs.GetInt(PrefKey_RootCorrectionMode, (int)VeryAnimation.RootCorrectionMode.Single);
                VAW.VA.extraOptionsCollision = EditorPrefs.GetBool(PrefKey_ExtraCollision, false);
                VAW.VA.extraOptionsSynchronizeAnimation = EditorPrefs.GetBool(PrefKey_ExtraSynchronizeAnimation, false);
                VAW.VA.extraOptionsOnionSkin = EditorPrefs.GetBool(PrefKey_ExtraOnionSkin, false);
                VAW.VA.extraOptionsRootTrail = EditorPrefs.GetBool(PrefKey_ExtraRootTrail, false);

                extraTree.LoadEditorPref();
                poseTree.LoadEditorPref();
                blendPoseTree.LoadEditorPref();
                muscleGroupTree.LoadEditorPref();
                handPoseTree.LoadEditorPref();
                blendShapeTree.LoadEditorPref();
            }
            #endregion

            initialized = true;
        }
        private void Release()
        {
            if (!initialized) return;

            #region EditorPref
            {
                EditorPrefs.SetBool(PrefKey_Extra, editorExtraFoldout);
                EditorPrefs.SetBool(PrefKey_Pose, editorPoseFoldout);
                EditorPrefs.SetBool(PrefKey_BlendPose, editorBlendPoseFoldout);
                EditorPrefs.SetBool(PrefKey_Muscle, editorMuscleFoldout);
                EditorPrefs.SetBool(PrefKey_HandPose, editorHandPoseFoldout);
                EditorPrefs.SetBool(PrefKey_BlendShape, editorBlendShapeFoldout);
                EditorPrefs.SetBool(PrefKey_Selection, editorSelectionFoldout);

                EditorPrefs.SetBool(PrefKey_ExtraVisible, editorExtraVisible);
                EditorPrefs.SetBool(PrefKey_PoseVisible, editorPoseVisible);
                EditorPrefs.SetBool(PrefKey_BlendPoseVisible, editorBlendPoseVisible);
                EditorPrefs.SetBool(PrefKey_MuscleVisible, editorMuscleVisible);
                EditorPrefs.SetBool(PrefKey_HandPoseVisible, editorHandPoseVisible);
                EditorPrefs.SetBool(PrefKey_BlendShapeVisible, editorBlendShapeVisible);
                EditorPrefs.SetBool(PrefKey_SelectionVisible, editorSelectionVisible);

                EditorPrefs.SetBool(PrefKey_ClampMuscle, VAW.VA.optionsClampMuscle);
                EditorPrefs.SetBool(PrefKey_AutoFootIK, VAW.VA.optionsAutoFootIK);
                EditorPrefs.SetBool(PrefKey_MirrorEnable, VAW.VA.optionsMirror);
                EditorPrefs.SetInt(PrefKey_RootCorrectionMode, (int)VAW.VA.rootCorrectionMode);
                EditorPrefs.SetBool(PrefKey_ExtraCollision, VAW.VA.extraOptionsCollision);
                EditorPrefs.SetBool(PrefKey_ExtraSynchronizeAnimation, VAW.VA.extraOptionsSynchronizeAnimation);
                EditorPrefs.SetBool(PrefKey_ExtraOnionSkin, VAW.VA.extraOptionsOnionSkin);
                EditorPrefs.SetBool(PrefKey_ExtraRootTrail, VAW.VA.extraOptionsRootTrail);

                extraTree.SaveEditorPref();
                poseTree.SaveEditorPref();
                blendPoseTree.SaveEditorPref();
                muscleGroupTree.SaveEditorPref();
                handPoseTree.SaveEditorPref();
                blendShapeTree.SaveEditorPref();
            }
            #endregion

            extraTree = null;
            poseTree = null;
            blendPoseTree = null;
            muscleGroupTree = null;
            handPoseTree = null;
            blendShapeTree = null;

            initialized = false;
        }
        private void UpdateHierarchyTree()
        {
            Initialize();
        }

        void OnInspectorUpdate()
        {
            if (VAW == null || VAW.VA == null || !VAW.Initialized || VeryAnimationControlWindow.instance == null)
            {
                Close();
                return;
            }
        }

        void OnGUI()
        {
            if (VAW.VA == null || VAW.VA.IsEditError || !VAW.IsGuiStyleReady)
                return;

#if Enable_Profiler
            Profiler.BeginSample("****VeryAnimationEditorWindow.OnGUI");
#endif
            Event e = Event.current;

            #region Event
            switch (e.type)
            {
                case EventType.KeyDown:
                    if (focusedWindow == this)
                        VAW.VA.HotKeys();
                    break;
                case EventType.MouseUp:
                    SceneView.RepaintAll();
                    break;
            }
            VAW.VA.Commands();
            #endregion

            #region ToolBar
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                if (editorExtraVisible)
                {
                    editorExtraFoldout = GUILayout.Toggle(editorExtraFoldout, "Extra", EditorStyles.toolbarButton);
                }
                if (editorPoseVisible)
                {
                    editorPoseFoldout = GUILayout.Toggle(editorPoseFoldout, "Pose", EditorStyles.toolbarButton);
                }
                if (editorBlendPoseVisible)
                {
                    editorBlendPoseFoldout = GUILayout.Toggle(editorBlendPoseFoldout, "Blend Pose", EditorStyles.toolbarButton);
                }
                if (VAW.VA.IsHuman && editorMuscleVisible)
                {
                    editorMuscleFoldout = GUILayout.Toggle(editorMuscleFoldout, "Muscle Group", EditorStyles.toolbarButton);
                }
                if (VAW.VA.IsHuman && VAW.VA.HumanoidHasLeftHand && VAW.VA.HumanoidHasRightHand && editorHandPoseVisible)
                {
                    editorHandPoseFoldout = GUILayout.Toggle(editorHandPoseFoldout, "Hand Pose", EditorStyles.toolbarButton);
                }
                if (blendShapeTree.IsHaveBlendShapeNodes() && editorBlendShapeVisible)
                {
                    editorBlendShapeFoldout = GUILayout.Toggle(editorBlendShapeFoldout, "Blend Shape", EditorStyles.toolbarButton);
                }
                if (editorSelectionVisible)
                {
                    editorSelectionFoldout = GUILayout.Toggle(editorSelectionFoldout, "Selection", EditorStyles.toolbarButton);
                }
                {
                    if (EditorGUILayout.DropdownButton(VAW.UEditorGUI.GUIContents.GetTitleSettingsIcon(), FocusType.Passive, VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        GenericMenu menu = new();
                        menu.AddItem(new GUIContent("Extra"), editorExtraVisible, () =>
                        {
                            editorExtraVisible = !editorExtraVisible;
                            editorExtraFoldout = editorExtraVisible;
                            if (!editorExtraVisible)
                            {
                                Undo.RecordObject(VAW, "Change Extra");
                                VAW.VA.extraOptionsCollision = editorExtraVisible;
                                VAW.VA.extraOptionsSynchronizeAnimation = editorExtraVisible;
                                VAW.VA.SetSynchronizeAnimation(VAW.VA.extraOptionsSynchronizeAnimation);
                                VAW.VA.extraOptionsOnionSkin = editorExtraVisible;
                                VAW.VA.OnionSkin.Update();
                                VAW.VA.extraOptionsRootTrail = editorExtraVisible;
                                SceneView.RepaintAll();
                                VAW.Repaint();
                            }
                        });
                        menu.AddItem(new GUIContent("Pose"), editorPoseVisible, () => { editorPoseVisible = !editorPoseVisible; editorPoseFoldout = editorPoseVisible; });
                        menu.AddItem(new GUIContent("Blend Pose"), editorBlendPoseVisible, () => { editorBlendPoseVisible = !editorBlendPoseVisible; editorBlendPoseFoldout = editorBlendPoseVisible; });
                        if (VAW.VA.IsHuman)
                            menu.AddItem(new GUIContent("Muscle Group"), editorMuscleVisible, () => { editorMuscleVisible = !editorMuscleVisible; editorMuscleFoldout = editorMuscleVisible; });
                        if (VAW.VA.IsHuman && VAW.VA.HumanoidHasLeftHand && VAW.VA.HumanoidHasRightHand)
                            menu.AddItem(new GUIContent("Hand Pose"), editorHandPoseVisible, () => { editorHandPoseVisible = !editorHandPoseVisible; editorHandPoseFoldout = editorHandPoseVisible; });
                        if (blendShapeTree.IsHaveBlendShapeNodes())
                            menu.AddItem(new GUIContent("Blend Shape"), editorBlendShapeVisible, () => { editorBlendShapeVisible = !editorBlendShapeVisible; editorBlendShapeFoldout = editorBlendShapeVisible; });
                        menu.AddItem(new GUIContent("Selection"), editorSelectionVisible, () => { editorSelectionVisible = !editorSelectionVisible; editorSelectionFoldout = editorSelectionVisible; });
                        menu.ShowAsContext();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            #endregion

            if (VAW.VA.IsHuman)
                HumanoidEditorGUI();
            else
                GenericEditorGUI();

#if Enable_Profiler
            Profiler.EndSample();
#endif
        }

        private void HumanoidEditorGUI()
        {
            #region Tools
            {
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("Options", EditorStyles.miniLabel, GUILayout.Width(48f));
                    {
                        EditorGUI.BeginChangeCheck();
                        var flag = GUILayout.Toggle(VAW.VA.optionsClampMuscle, Language.GetContent(Language.Help.EditorOptionsClamp), EditorStyles.miniButton);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(VAW, "Change Clamp");
                            VAW.VA.optionsClampMuscle = flag;
                        }
                    }
                    {
                        var help = Language.Help.EditorOptionsFootIK_2018_3;
                        if (VAW.VA.UAw.GetLinkedWithTimeline())
                        {
#if VERYANIMATION_TIMELINE
                            EditorGUI.BeginDisabledGroup(true);
                            GUILayout.Toggle(VAW.VA.UAw.GetTimelineApplyFootIK(), Language.GetContent(help), EditorStyles.miniButton);
                            EditorGUI.EndDisabledGroup();
#endif
                        }
                        else
                        {
                            EditorGUI.BeginChangeCheck();
                            var flag = GUILayout.Toggle(VAW.VA.optionsAutoFootIK, Language.GetContent(help), EditorStyles.miniButton);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(VAW, "Change Foot IK");
                                VAW.VA.optionsAutoFootIK = flag;
                                VAW.VA.SetUpdateSampleAnimation(false, true);
                                VAW.VA.SetSynchroIKtargetAll();
                                VAW.VA.SetAnimationWindowSynchroSelection();
                                VAW.VA.UpdateSkeletonShowBoneList();
                            }
                        }
                    }
                    {
                        EditorGUI.BeginChangeCheck();
                        var flag = GUILayout.Toggle(VAW.VA.optionsMirror, Language.GetContent(Language.Help.EditorOptionsMirror), EditorStyles.miniButton);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(VAW, UndoChangeMirror);
                            VAW.VA.optionsMirror = flag;
                            VAW.VA.SetAnimationWindowSynchroSelection();
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUI.BeginDisabledGroup(VAW.VA.RootMotionBoneIndex >= 0 && VAW.VA.IsWriteLockBone(VAW.VA.RootMotionBoneIndex));
                    EditorGUILayout.LabelField(Language.GetContent(Language.Help.EditorRootCorrection), EditorStyles.miniLabel, GUILayout.Width(88f));
                    {
                        EditorGUI.BeginChangeCheck();
                        var mode = (VeryAnimation.RootCorrectionMode)GUILayout.Toolbar((int)VAW.VA.rootCorrectionMode, Styles.rootCorrectionModeString, EditorStyles.miniButton);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(VAW, "Change Root Correction Mode");
                            VAW.VA.rootCorrectionMode = mode;
                            VAW.VA.SetAnimationWindowSynchroSelection();
                        }
                    }
                    EditorGUI.EndDisabledGroup();
                }
                EditorGUILayout.EndHorizontal();
            }
            #endregion

            editorScrollPosition = EditorGUILayout.BeginScrollView(editorScrollPosition);

            EditorGUI_ExtraGUI();

            EditorGUI_PoseGUI();

            EditorGUI_BlendPoseGUI();

            EditorGUI_MuscleGroupGUI();

            EditorGUI_HandPoseGUI();

            EditorGUI_BlendShapeGUI();

            EditorGUI_SelectionGUI();

            EditorGUILayout.EndScrollView();
        }
        private void GenericEditorGUI()
        {
            #region Tools
            {
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("Options", GUILayout.Width(52f));
                    {
                        EditorGUI.BeginChangeCheck();
                        var flag = GUILayout.Toggle(VAW.VA.optionsMirror, Language.GetContent(Language.Help.EditorOptionsMirror), EditorStyles.miniButton);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(VAW, UndoChangeMirror);
                            VAW.VA.optionsMirror = flag;
                            VAW.VA.SetAnimationWindowSynchroSelection();
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            #endregion

            editorScrollPosition = EditorGUILayout.BeginScrollView(editorScrollPosition);

            EditorGUI_ExtraGUI();

            EditorGUI_PoseGUI();

            EditorGUI_BlendPoseGUI();

            EditorGUI_BlendShapeGUI();

            EditorGUI_SelectionGUI();

            EditorGUILayout.EndScrollView();
        }
        private void EditorGUI_ExtraGUI()
        {
            if (editorExtraFoldout && editorExtraVisible)
            {
                EditorGUILayout.BeginHorizontal();
                {
                    editorExtraFoldout = EditorGUILayout.Foldout(editorExtraFoldout, "Extra", true, VAW.GuiStyleBoldFoldout);
                }
                {
                    EditorGUILayout.Space();
                    extraTree.ExtraTreeToolbarGUI();
                    EditorGUILayout.Space();
                    if (GUILayout.Button(VAW.UEditorGUI.GUIContents.GetHelpIcon(), editorExtraGroupHelp ? VAW.GuiStyleIconActiveButton : VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        editorExtraGroupHelp = !editorExtraGroupHelp;
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (editorExtraGroupHelp)
                {
                    EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpExtra), MessageType.Info);
                }

                extraTree.ExtraTreeGUI();
            }
        }
        private void EditorGUI_PoseGUI()
        {
            if (editorPoseFoldout && editorPoseVisible)
            {
                EditorGUILayout.BeginHorizontal();
                {
                    editorPoseFoldout = EditorGUILayout.Foldout(editorPoseFoldout, "Pose", true, VAW.GuiStyleBoldFoldout);
                }
                {
                    EditorGUILayout.Space();
                    poseTree.PoseTreeToolbarGUI();
                    EditorGUILayout.Space();
                    if (GUILayout.Button(VAW.UEditorGUI.GUIContents.GetHelpIcon(), editorPoseGroupHelp ? VAW.GuiStyleIconActiveButton : VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        editorPoseGroupHelp = !editorPoseGroupHelp;
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (editorPoseGroupHelp)
                {
                    EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpPose), MessageType.Info);
                }

                poseTree.PoseTreeGUI();
            }
        }
        private void EditorGUI_BlendPoseGUI()
        {
            if (editorBlendPoseFoldout && editorBlendPoseVisible)
            {
                EditorGUILayout.BeginHorizontal();
                {
                    editorBlendPoseFoldout = EditorGUILayout.Foldout(editorBlendPoseFoldout, "Blend Pose", true, VAW.GuiStyleBoldFoldout);
                }
                {
                    EditorGUILayout.Space();
                    blendPoseTree.BlendPoseTreeToolbarGUI();
                    EditorGUILayout.Space();
                    if (GUILayout.Button(VAW.UEditorGUI.GUIContents.GetHelpIcon(), editorBlendPoseGroupHelp ? VAW.GuiStyleIconActiveButton : VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        editorBlendPoseGroupHelp = !editorBlendPoseGroupHelp;
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (editorBlendPoseGroupHelp)
                {
                    EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpBlendPose), MessageType.Info);
                }

                blendPoseTree.BlendPoseTreeGUI();
            }
        }
        private void EditorGUI_MuscleGroupGUI()
        {
            if (editorMuscleFoldout && editorMuscleVisible)
            {
                EditorGUILayout.BeginHorizontal();
                {
                    editorMuscleFoldout = EditorGUILayout.Foldout(editorMuscleFoldout, "Muscle Group", true, VAW.GuiStyleBoldFoldout);
                }
                {
                    EditorGUILayout.Space();
                    muscleGroupTree.MuscleGroupTreeToolbarGUI();
                    EditorGUILayout.Space();
                    if (GUILayout.Button(VAW.UEditorGUI.GUIContents.GetHelpIcon(), editorMuscleGroupHelp ? VAW.GuiStyleIconActiveButton : VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        editorMuscleGroupHelp = !editorMuscleGroupHelp;
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (editorMuscleGroupHelp)
                {
                    EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpMuscleGroup), MessageType.Info);
                }

                muscleGroupTree.MuscleGroupTreeGUI();
            }
        }
        private void EditorGUI_HandPoseGUI()
        {
            if (!VAW.VA.IsHuman || !VAW.VA.HumanoidHasLeftHand || !VAW.VA.HumanoidHasRightHand)
                return;

            if (editorHandPoseFoldout && editorHandPoseVisible)
            {
                EditorGUILayout.BeginHorizontal();
                {
                    editorHandPoseFoldout = EditorGUILayout.Foldout(editorHandPoseFoldout, "Hand Pose", true, VAW.GuiStyleBoldFoldout);
                }
                {
                    EditorGUILayout.Space();
                    handPoseTree.HandPoseTreeToolbarGUI();
                    EditorGUILayout.Space();
                    if (GUILayout.Button(VAW.UEditorGUI.GUIContents.GetHelpIcon(), editorHandPoseGroupHelp ? VAW.GuiStyleIconActiveButton : VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        editorHandPoseGroupHelp = !editorHandPoseGroupHelp;
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (editorHandPoseGroupHelp)
                {
                    EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpHandPose), MessageType.Info);
                }

                handPoseTree.HandPoseTreeGUI();
            }
        }
        private void EditorGUI_BlendShapeGUI()
        {
            if (!blendShapeTree.IsHaveBlendShapeNodes())
                return;

            if (editorBlendShapeFoldout && editorBlendShapeVisible)
            {
                EditorGUILayout.BeginHorizontal();
                {
                    editorBlendShapeFoldout = EditorGUILayout.Foldout(editorBlendShapeFoldout, "Blend Shape", true, VAW.GuiStyleBoldFoldout);
                }
                {
                    EditorGUILayout.Space();
                    blendShapeTree.BlendShapeTreeToolbarGUI();
                    EditorGUILayout.Space();
                    if (GUILayout.Button(VAW.UEditorGUI.GUIContents.GetHelpIcon(), editorBlendShapeGroupHelp ? VAW.GuiStyleIconActiveButton : VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        editorBlendShapeGroupHelp = !editorBlendShapeGroupHelp;
                    }
                    if (EditorGUILayout.DropdownButton(VAW.UEditorGUI.GUIContents.GetTitleSettingsIcon(), FocusType.Passive, VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        blendShapeTree.BlendShapeTreeSettingsMesh();
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (editorBlendShapeGroupHelp)
                {
                    EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpBlendShape), MessageType.Info);
                }

                blendShapeTree.BlendShapeTreeGUI();
            }
        }
        public void EditorGUI_SelectionGUI(bool onScene = false)
        {
            if (editorSelectionFoldout && editorSelectionVisible && onScene == EditorSelectionOnScene)
            {
                EditorGUILayout.BeginHorizontal();
                if (!onScene)
                {
                    editorSelectionFoldout = EditorGUILayout.Foldout(editorSelectionFoldout, "Selection", true, VAW.GuiStyleBoldFoldout);
                }
                if (VAW.VA.SelectionActiveGameObject != null)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.ObjectField(VAW.VA.SelectionActiveGameObject, typeof(GameObject), false);
                    EditorGUI.EndDisabledGroup();
                }
                else if (VAW.VA.animatorIK.IKActiveTarget != AnimatorIKCore.IKTarget.None && VAW.VA.animatorIK.ikData[(int)VAW.VA.animatorIK.IKActiveTarget].enable)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.LabelField($"Animator IK: {AnimatorIKCore.IKTargetStrings[(int)VAW.VA.animatorIK.IKActiveTarget]}");
                    EditorGUI.EndDisabledGroup();
                }
                else if (VAW.VA.originalIK.IKActiveTarget >= 0 && VAW.VA.originalIK.ikData[VAW.VA.originalIK.IKActiveTarget].enable)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.LabelField($"Original IK: {VAW.VA.originalIK.ikData[VAW.VA.originalIK.IKActiveTarget].name}");
                    EditorGUI.EndDisabledGroup();
                }
                else if (VAW.VA.SelectionHumanVirtualBones != null && VAW.VA.SelectionHumanVirtualBones.Count > 0)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.LabelField($"Virtual: {VAW.VA.SelectionHumanVirtualBones[0]}");
                    EditorGUI.EndDisabledGroup();
                }
                else if (VAW.VA.SelectionMotionTool)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.LabelField("Motion");
                    EditorGUI.EndDisabledGroup();
                }
                {
                    EditorGUILayout.Space();
                    if (GUILayout.Button(VAW.UEditorGUI.GUIContents.GetHelpIcon(), editorSelectionGroupHelp ? VAW.GuiStyleIconActiveButton : VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        editorSelectionGroupHelp = !editorSelectionGroupHelp;
                        VAW.editorWindowSelectionRect.size = Vector2.zero;
                    }
                    if (EditorGUILayout.DropdownButton(VAW.UEditorGUI.GUIContents.GetTitleSettingsIcon(), FocusType.Passive, VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        GenericMenu menu = new();
                        menu.AddItem(Language.GetContent(Language.Help.EditorMenuOnScene), EditorSelectionOnScene, () =>
                        {
                            EditorSelectionOnScene = !EditorSelectionOnScene;
                            EditorPrefs.SetBool(PrefKey_SelectionOnScene, EditorSelectionOnScene);
                            VAW.editorWindowSelectionRect.size = Vector2.zero;
                            Repaint();
                            SceneView.RepaintAll();
                        });
                        menu.ShowAsContext();
                    }
                }
                EditorGUILayout.EndHorizontal();
                {
                    if (editorSelectionGroupHelp)
                    {
                        EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpSelection), MessageType.Info);
                    }

                    EditorGUILayout.BeginVertical(VAW.GuiStyleSkinBox);
                    {
                        int RowCount = 0;
                        var humanoidIndex = VAW.VA.SelectionGameObjectHumanoidIndex();
                        var boneIndex = VAW.VA.SelectionActiveBone;
                        if (VAW.VA.IsHuman && (humanoidIndex >= 0 || boneIndex == VAW.VA.RootMotionBoneIndex))
                        {
                            #region Humanoid
                            if (humanoidIndex == HumanBodyBones.Hips)
                            {
                                EditorGUILayout.LabelField(Language.GetText(Language.Help.SelectionHip), VAW.GuiStyleCenterAlignLabel);
                            }
                            else if (humanoidIndex > HumanBodyBones.Hips || VAW.VA.SelectionActiveGameObject == VAW.GameObject)
                            {
                                EditorGUILayout.BeginHorizontal();
                                #region Mirror
                                var mirrorIndex = humanoidIndex >= 0 && VAW.VA.HumanoidIndex2boneIndex[(int)humanoidIndex] >= 0 ? VAW.VA.MirrorBoneIndexes[VAW.VA.HumanoidIndex2boneIndex[(int)humanoidIndex]] : -1;
                                if (GUILayout.Button(Language.GetContentFormat(Language.Help.SelectionMirror, (mirrorIndex >= 0 ? $"From '{VAW.VA.Bones[mirrorIndex].name}'" : "From self")), GUILayout.Width(100)))
                                {
                                    VAW.VA.SetSelectionMirror();
                                }
                                #endregion
                                EditorGUILayout.Space();
                                #region Reset
                                if (GUILayout.Button("Reset All", VAW.GuiStyleDropDown, GUILayout.Width(100)))
                                {
                                    ResetAllSelectionHumanoidMenu(true, true, true);
                                }
                                #endregion
                                EditorGUILayout.EndHorizontal();
                                EditorGUILayout.Space();
                            }
                            if (boneIndex == VAW.VA.RootMotionBoneIndex)
                            {
                                #region Root
                                {
                                    EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                                    if (GUILayout.Button(Styles.guiContentRootT, GUILayout.Width(64)))
                                    {
                                        VAW.VA.LastTool = Tool.Move;
                                        VAW.VA.SelectGameObject(VAW.GameObject);
                                    }
                                    EditorGUI.BeginChangeCheck();
                                    var rootT = EditorGUILayout.Vector3Field("", VAW.VA.GetAnimationValueAnimatorRootT());
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        VAW.VA.SetAnimationValueAnimatorRootT(rootT);
                                    }
                                    if (GUILayout.Button("Reset", VAW.GuiStyleDropDown, GUILayout.Width(64)))
                                    {
                                        ResetAllSelectionHumanoidMenu(true, false, false);
                                    }
                                    EditorGUILayout.EndHorizontal();
                                }
                                {
                                    EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                                    if (GUILayout.Button(Styles.guiContentRootQ, GUILayout.Width(64)))
                                    {
                                        VAW.VA.LastTool = Tool.Rotate;
                                        VAW.VA.SelectGameObject(VAW.GameObject);
                                    }
                                    EditorGUI.BeginChangeCheck();
                                    var quat = VAW.VA.GetAnimationValueAnimatorRootQ();
                                    var rotation = new Vector4(quat.x, quat.y, quat.z, quat.w);
                                    rotation = EditorGUILayout.Vector4Field("", rotation);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        if (rotation.sqrMagnitude > 0f)
                                        {
                                            rotation.Normalize();
                                            quat.x = rotation.x;
                                            quat.y = rotation.y;
                                            quat.z = rotation.z;
                                            quat.w = rotation.w;
                                            VAW.VA.SetAnimationValueAnimatorRootQ(quat);
                                        }
                                    }
                                    if (GUILayout.Button("Reset", VAW.GuiStyleDropDown, GUILayout.Width(64)))
                                    {
                                        ResetAllSelectionHumanoidMenu(false, true, false);
                                    }
                                    EditorGUILayout.EndHorizontal();
                                }
                                {
                                    EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                                    if (GUILayout.Button(Styles.guiContentHumanoidPosition, GUILayout.Width(64)))
                                    {
                                        VAW.VA.LastTool = Tool.Move;
                                        VAW.VA.SelectGameObject(VAW.GameObject);
                                    }
                                    EditorGUI.BeginChangeCheck();
                                    var position = VAW.VA.GetHumanWorldRootPosition();
                                    position = VAW.VA.TransformPoseSave.StartMatrix.inverse.MultiplyPoint3x4(position);
                                    position = EditorGUILayout.Vector3Field("", position);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        position = VAW.VA.TransformPoseSave.StartMatrix.MultiplyPoint3x4(position);
                                        VAW.VA.SetAnimationValueAnimatorRootT(VAW.VA.GetHumanLocalRootPosition(position));
                                    }
                                    if (GUILayout.Button("Reset", VAW.GuiStyleDropDown, GUILayout.Width(64)))
                                    {
                                        ResetAllSelectionHumanoidMenu(true, false, false);
                                    }
                                    EditorGUILayout.EndHorizontal();
                                }
                                {
                                    EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                                    if (GUILayout.Button(Styles.guiContentHumanoidRotation, GUILayout.Width(64)))
                                    {
                                        VAW.VA.LastTool = Tool.Rotate;
                                        VAW.VA.SelectGameObject(VAW.GameObject);
                                    }
                                    EditorGUI.BeginChangeCheck();
                                    var rootQ = EditorGUILayout.Vector3Field("", VAW.VA.GetAnimationValueAnimatorRootQ().eulerAngles);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        VAW.VA.SetAnimationValueAnimatorRootQ(Quaternion.Euler(rootQ));
                                    }
                                    if (GUILayout.Button("Reset", VAW.GuiStyleDropDown, GUILayout.Width(64)))
                                    {
                                        ResetAllSelectionHumanoidMenu(false, true, false);
                                    }
                                    EditorGUILayout.EndHorizontal();
                                }
                                #endregion
                            }
                            else if (humanoidIndex > HumanBodyBones.Hips)
                            {
                                #region Muscle
                                if (VAW.muscleRotationSliderIds == null || VAW.muscleRotationSliderIds.Length != 3)
                                    VAW.muscleRotationSliderIds = new int[3];
                                for (int i = 0; i < VAW.muscleRotationSliderIds.Length; i++)
                                    VAW.muscleRotationSliderIds[i] = -1;
                                for (int i = 0; i < 3; i++)
                                {
                                    var muscleIndex = HumanTrait.MuscleFromBone((int)humanoidIndex, i);
                                    if (muscleIndex < 0) continue;
                                    var muscleValue = VAW.VA.GetAnimationValueAnimatorMuscle(muscleIndex);
                                    EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                                    if (GUILayout.Button(VAW.VA.MusclePropertyName.Names[muscleIndex], GUILayout.Width(VAW.EditorSettings.SettingEditorNameFieldWidth)))
                                    {
                                        VAW.VA.LastTool = Tool.Rotate;
                                        VAW.VA.SelectHumanoidBone(humanoidIndex);
                                        VAW.VA.SetAnimationWindowSynchroSelection(new EditorCurveBinding[] { VAW.VA.AnimatorMuscleBindings[muscleIndex] });
                                    }
                                    {
                                        var mmuscleIndex = VAW.VA.GetMirrorMuscleIndex(muscleIndex);
                                        if (mmuscleIndex >= 0)
                                        {
                                            if (GUILayout.Button(VAW.VA.MusclePropertyName.Names[mmuscleIndex], VAW.GuiStyleMirrorButton, GUILayout.Width(VAW.MirrorTex.width), GUILayout.Height(VAW.MirrorTex.height)))
                                            {
                                                var mhumanoidIndex = (HumanBodyBones)HumanTrait.BoneFromMuscle(mmuscleIndex);
                                                VAW.VA.SelectHumanoidBone(mhumanoidIndex);
                                                VAW.VA.SetAnimationWindowSynchroSelection(new EditorCurveBinding[] { VAW.VA.AnimatorMuscleBindings[mmuscleIndex] });
                                            }
                                        }
                                        else
                                        {
                                            GUILayout.Space(GUIStyles.FoldoutSpace);
                                        }
                                    }
                                    {
                                        var saveBackgroundColor = GUI.backgroundColor;
                                        GUI.backgroundColor = i switch
                                        {
                                            0 => Handles.xAxisColor,
                                            1 => Handles.yAxisColor,
                                            2 => Handles.zAxisColor,
                                            _ => GUI.backgroundColor,
                                        };
                                        EditorGUI.BeginChangeCheck();
                                        muscleValue = GUILayout.HorizontalSlider(muscleValue, -1f, 1f);
                                        VAW.muscleRotationSliderIds[i] = VAW.UEditorGUIUtility.GetLastControlID();
                                        if (EditorGUI.EndChangeCheck())
                                        {
                                            foreach (var mi in VAW.VA.SelectionGameObjectsMuscleIndex(i))
                                            {
                                                VAW.VA.SetAnimationValueAnimatorMuscle(mi, muscleValue);
                                            }
                                        }
                                        GUI.backgroundColor = saveBackgroundColor;
                                    }
                                    {
                                        EditorGUI.BeginChangeCheck();
                                        var value2 = EditorGUILayout.FloatField(muscleValue, GUILayout.Width(GUIStyles.FloatFieldWidth));
                                        if (EditorGUI.EndChangeCheck())
                                        {
                                            foreach (var mi in VAW.VA.SelectionGameObjectsMuscleIndex(i))
                                            {
                                                VAW.VA.SetAnimationValueAnimatorMuscleIfNotOriginal(mi, value2);
                                            }
                                        }
                                    }
                                    EditorGUILayout.EndHorizontal();
                                }
                                #endregion

                                #region Rotation
                                {
                                    EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                                    if (GUILayout.Button(Styles.guiContentLocalRotationButton, GUILayout.Width(64)))
                                    {
                                        VAW.VA.LastTool = Tool.Rotate;
                                        VAW.VA.SelectHumanoidBone(humanoidIndex);
                                    }
                                    {
                                        var muscleIndex0 = HumanTrait.MuscleFromBone((int)humanoidIndex, 0);
                                        var muscleIndex1 = HumanTrait.MuscleFromBone((int)humanoidIndex, 1);
                                        var muscleIndex2 = HumanTrait.MuscleFromBone((int)humanoidIndex, 2);
                                        var euler = new Vector3(VAW.VA.Muscle2EulerAngle(muscleIndex0, VAW.VA.GetAnimationValueAnimatorMuscle(muscleIndex0)),
                                                                VAW.VA.Muscle2EulerAngle(muscleIndex1, VAW.VA.GetAnimationValueAnimatorMuscle(muscleIndex1)),
                                                                VAW.VA.Muscle2EulerAngle(muscleIndex2, VAW.VA.GetAnimationValueAnimatorMuscle(muscleIndex2)));
                                        EditorGUI.BeginChangeCheck();
                                        euler = EditorGUILayout.Vector3Field("", euler);
                                        if (EditorGUI.EndChangeCheck())
                                        {
                                            for (int i = 0; i < 3; i++)
                                            {
                                                var muscleValue = VAW.VA.EulerAngle2Muscle(HumanTrait.MuscleFromBone((int)humanoidIndex, i), euler[i]);
                                                foreach (var mi in VAW.VA.SelectionGameObjectsMuscleIndex(i))
                                                {
                                                    VAW.VA.SetAnimationValueAnimatorMuscle(mi, muscleValue);
                                                }
                                            }
                                        }
                                    }
                                    if (GUILayout.Button("Reset", VAW.GuiStyleDropDown, GUILayout.Width(64)))
                                    {
                                        ResetAllSelectionHumanoidMenu(false, true, false);
                                    }
                                    EditorGUILayout.EndHorizontal();
                                }
                                #endregion

                                #region Position(TDOF)
                                if (VAW.VA.HumanoidHasTDoF && VeryAnimation.HumanBonesAnimatorTDOFIndex[(int)humanoidIndex] != null)
                                {
                                    EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                                    if (GUILayout.Button(Styles.guiContentTDoFButton, GUILayout.Width(64)))
                                    {
                                        VAW.VA.LastTool = Tool.Move;
                                        VAW.VA.SelectHumanoidBone(humanoidIndex);
                                    }
                                    EditorGUI.BeginChangeCheck();
                                    var tdof = EditorGUILayout.Vector3Field("", VAW.VA.GetAnimationValueAnimatorTDOF(VeryAnimation.HumanBonesAnimatorTDOFIndex[(int)humanoidIndex].index));
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        foreach (var hi in VAW.VA.SelectionGameObjectsHumanoidIndex())
                                        {
                                            if (VeryAnimation.HumanBonesAnimatorTDOFIndex[(int)hi] == null) continue;
                                            VAW.VA.SetAnimationValueAnimatorTDOF(VeryAnimation.HumanBonesAnimatorTDOFIndex[(int)hi].index, tdof);
                                        }
                                    }
                                    if (GUILayout.Button("Reset", VAW.GuiStyleDropDown, GUILayout.Width(64)))
                                    {
                                        ResetAllSelectionHumanoidMenu(true, false, false);
                                    }
                                    EditorGUILayout.EndHorizontal();
                                }
                                #endregion

                            }
                            #endregion
                        }
                        else if (boneIndex >= 0)
                        {
                            #region Generic
                            if (VAW.VA.IsHuman && VAW.VA.HumanoidConflict[boneIndex])
                            {
                                EditorGUILayout.LabelField(Language.GetText(Language.Help.SelectionHumanoidConflict), VAW.GuiStyleCenterAlignLabel);
                            }
                            else
                            {
                                {
                                    EditorGUILayout.BeginHorizontal();
                                    #region Mirror
                                    if (GUILayout.Button(Language.GetContentFormat(Language.Help.SelectionMirror, (VAW.VA.MirrorBoneIndexes[boneIndex] >= 0 ? $"From '{VAW.VA.Bones[VAW.VA.MirrorBoneIndexes[boneIndex]].name}'" : "From self")), GUILayout.Width(100)))
                                    {
                                        VAW.VA.SetSelectionMirror();
                                    }
                                    #endregion
                                    EditorGUILayout.Space();
                                    #region Reset
                                    if (GUILayout.Button("Reset All", VAW.GuiStyleDropDown, GUILayout.Width(100)))
                                    {
                                        ResetAllSelectionGenericMenu(true, true, true);
                                    }
                                    #endregion
                                    EditorGUILayout.EndHorizontal();
                                }
                                EditorGUILayout.Space();
                                {
                                    #region Position
                                    {
                                        EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                                        if (GUILayout.Button("Position", GUILayout.Width(64)))
                                        {
                                            VAW.VA.LastTool = Tool.Move;
                                            VAW.VA.SelectGameObject(VAW.VA.Bones[boneIndex]);
                                        }
                                        EditorGUI.BeginChangeCheck();
                                        {
                                            var localPosition = EditorGUILayout.Vector3Field("", VAW.VA.GetAnimationValueTransformPosition(boneIndex));
                                            if (EditorGUI.EndChangeCheck())
                                            {
                                                foreach (var bi in VAW.VA.SelectionGameObjectsOtherHumanoidBoneIndex())
                                                {
                                                    VAW.VA.SetAnimationValueTransformPosition(bi, localPosition);
                                                }
                                            }
                                        }
                                        if (GUILayout.Button("Reset", VAW.GuiStyleDropDown, GUILayout.Width(64)))
                                        {
                                            ResetAllSelectionGenericMenu(true, false, false);
                                        }
                                        EditorGUILayout.EndHorizontal();
                                    }
                                    #endregion
                                    #region Rotation
                                    {
                                        EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                                        if (GUILayout.Button("Rotation", GUILayout.Width(64)))
                                        {
                                            VAW.VA.LastTool = Tool.Rotate;
                                            VAW.VA.SelectGameObject(VAW.VA.Bones[boneIndex]);
                                        }
                                        EditorGUI.BeginChangeCheck();
                                        {
                                            var localEulerAngles = EditorGUILayout.Vector3Field("", VAW.VA.GetAnimationValueTransformRotation(boneIndex).eulerAngles);
                                            if (EditorGUI.EndChangeCheck())
                                            {
                                                foreach (var bi in VAW.VA.SelectionGameObjectsOtherHumanoidBoneIndex())
                                                {
                                                    VAW.VA.SetAnimationValueTransformRotation(bi, Quaternion.Euler(localEulerAngles));
                                                }
                                            }
                                        }
                                        if (GUILayout.Button("Reset", VAW.GuiStyleDropDown, GUILayout.Width(64)))
                                        {
                                            ResetAllSelectionGenericMenu(false, true, false);
                                        }
                                        EditorGUILayout.EndHorizontal();
                                    }
                                    #endregion
                                    #region Scale
                                    {
                                        EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                                        if (GUILayout.Button("Scale", GUILayout.Width(64)))
                                        {
                                            VAW.VA.LastTool = Tool.Scale;
                                            VAW.VA.SelectGameObject(VAW.VA.Bones[boneIndex]);
                                        }
                                        EditorGUI.BeginChangeCheck();
                                        {
                                            var localScale = EditorGUILayout.Vector3Field("", VAW.VA.GetAnimationValueTransformScale(boneIndex));
                                            if (EditorGUI.EndChangeCheck())
                                            {
                                                foreach (var bi in VAW.VA.SelectionGameObjectsOtherHumanoidBoneIndex())
                                                {
                                                    VAW.VA.SetAnimationValueTransformScale(bi, localScale);
                                                }
                                            }
                                        }
                                        if (GUILayout.Button("Reset", VAW.GuiStyleDropDown, GUILayout.Width(64)))
                                        {
                                            ResetAllSelectionGenericMenu(false, false, true);
                                        }
                                        EditorGUILayout.EndHorizontal();
                                    }
                                    #endregion
                                }
                            }
                            #endregion
                        }
                        else if (VAW.VA.SelectionMotionTool)
                        {
                            #region Motion
                            {
                                EditorGUILayout.BeginHorizontal();
                                #region Mirror
                                if (GUILayout.Button(Language.GetContentFormat(Language.Help.SelectionMirror, "From self"), GUILayout.Width(100)))
                                {
                                    VAW.VA.SetSelectionMirror();
                                }
                                #endregion
                                EditorGUILayout.Space();
                                #region Reset
                                if (GUILayout.Button("Reset All", GUILayout.Width(100)))
                                {
                                    VAW.VA.SetSelectionEditStart(false, false, false);
                                }
                                #endregion
                                EditorGUILayout.EndHorizontal();
                                EditorGUILayout.Space();
                            }
                            {
                                EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                                if (GUILayout.Button(Styles.guiContentMotionT, GUILayout.Width(64)))
                                {
                                    VAW.VA.LastTool = Tool.Move;
                                    VAW.VA.SelectMotionTool();
                                }
                                EditorGUI.BeginChangeCheck();
                                var motionT = EditorGUILayout.Vector3Field("", VAW.VA.GetAnimationValueAnimatorMotionT());
                                if (EditorGUI.EndChangeCheck())
                                {
                                    VAW.VA.SetAnimationValueAnimatorMotionT(motionT);
                                }
                                if (GUILayout.Button("Reset", GUILayout.Width(64f)))
                                {
                                    VAW.VA.SetAnimationValueAnimatorMotionTIfNotOriginal(Vector3.zero);
                                }
                                EditorGUILayout.EndHorizontal();
                            }
                            {
                                EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                                if (GUILayout.Button(Styles.guiContentMotionQ, GUILayout.Width(64)))
                                {
                                    VAW.VA.LastTool = Tool.Rotate;
                                    VAW.VA.SelectMotionTool();
                                }
                                EditorGUI.BeginChangeCheck();
                                var motionQ = EditorGUILayout.Vector3Field("", VAW.VA.GetAnimationValueAnimatorMotionQ().eulerAngles);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    VAW.VA.SetAnimationValueAnimatorMotionQ(Quaternion.Euler(motionQ));
                                }
                                if (GUILayout.Button("Reset", GUILayout.Width(64f)))
                                {
                                    VAW.VA.SetAnimationValueAnimatorMotionQIfNotOriginal(Quaternion.identity);
                                }
                                EditorGUILayout.EndHorizontal();
                            }
                            #endregion
                        }
                        else if (VAW.VA.animatorIK.IKActiveTarget != AnimatorIKCore.IKTarget.None)
                        {
                            VAW.VA.animatorIK.SelectionGUI();
                        }
                        else if (VAW.VA.originalIK.IKActiveTarget >= 0)
                        {
                            VAW.VA.originalIK.SelectionGUI();
                        }
                        else
                        {
                            EditorGUILayout.LabelField(Language.GetText(Language.Help.SelectionNothingisselected), VAW.GuiStyleCenterAlignLabel);
                        }
#if VERYANIMATION_ANIMATIONRIGGING
                        if (VAW.VA.AnimationRigging.IsValid && boneIndex >= 0)
                        {
                            if (VAW.VA.Bones[boneIndex].TryGetComponent<Rig>(out var rig))
                            {
                                EditorGUILayout.BeginVertical();
                                {
                                    EditorGUILayout.BeginHorizontal();
                                    EditorGUILayout.Space();
                                    Styles.guiContentAnimationRiggingHeader.tooltip = rig.ToString();
                                    EditorGUILayout.LabelField(Styles.guiContentAnimationRiggingHeader, EditorStyles.centeredGreyMiniLabel);
                                    EditorGUILayout.Space();
                                    EditorGUILayout.EndHorizontal();
                                }

                                #region Weight
                                {
                                    List<EditorCurveBinding> selectionBindings = null;
                                    List<EditorCurveBinding> GetEditorCurveBindings()
                                    {
                                        if (selectionBindings != null)
                                            return selectionBindings;

                                        selectionBindings = new List<EditorCurveBinding>();
                                        foreach (var bi in VAW.VA.SelectionBones)
                                        {
                                            if (!VAW.VA.Bones[bi].TryGetComponent<Rig>(out var rig))
                                                continue;
                                            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(VAW.VA.BonePaths[bi], rig.GetType(), "m_Weight");
                                            selectionBindings.Add(binding);
                                        }
                                        return selectionBindings;
                                    }

                                    EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                                    if (GUILayout.Button(Styles.guiContentRigWeight, GUILayout.Width(128)))
                                    {
                                        VAW.VA.SetAnimationWindowSynchroSelection(GetEditorCurveBindings());
                                    }
                                    {
                                        EditorGUI.BeginChangeCheck();
                                        var weight = EditorGUILayout.Slider(rig.weight, 0f, 1f);
                                        if (EditorGUI.EndChangeCheck())
                                        {
                                            var bindings = GetEditorCurveBindings();
                                            foreach (var binding in bindings)
                                            {
                                                VAW.VA.SetAnimationValueCustomProperty(binding, weight);
                                            }
                                        }
                                    }
                                    EditorGUILayout.EndHorizontal();
                                }
                                #endregion

                                EditorGUILayout.EndVertical();
                            }
                            if (VAW.VA.Bones[boneIndex].TryGetComponent<IRigConstraint>(out var rigConstraint))
                            {
                                EditorGUILayout.BeginVertical();
                                {
                                    EditorGUILayout.BeginHorizontal();
                                    EditorGUILayout.Space();
                                    Styles.guiContentAnimationRiggingHeader.tooltip = rigConstraint.ToString();
                                    EditorGUILayout.LabelField(Styles.guiContentAnimationRiggingHeader, EditorStyles.centeredGreyMiniLabel);
                                    EditorGUILayout.Space();
                                    EditorGUILayout.EndHorizontal();
                                }

                                #region Weight
                                {
                                    List<EditorCurveBinding> selectionBindings = null;
                                    List<EditorCurveBinding> GetEditorCurveBindings()
                                    {
                                        if (selectionBindings != null)
                                            return selectionBindings;

                                        selectionBindings = new List<EditorCurveBinding>();
                                        foreach (var bi in VAW.VA.SelectionBones)
                                        {
                                            if (!VAW.VA.Bones[bi].TryGetComponent<IRigConstraint>(out var constraint))
                                                continue;
                                            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(VAW.VA.BonePaths[bi], constraint.GetType(), "m_Weight");
                                            selectionBindings.Add(binding);
                                        }
                                        return selectionBindings;
                                    }

                                    EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                                    if (GUILayout.Button(Styles.guiContentRigConstraintWeight, GUILayout.Width(128)))
                                    {
                                        VAW.VA.SetAnimationWindowSynchroSelection(GetEditorCurveBindings());
                                    }
                                    {
                                        EditorGUI.BeginChangeCheck();
                                        var weight = EditorGUILayout.Slider(rigConstraint.weight, 0f, 1f);
                                        if (EditorGUI.EndChangeCheck())
                                        {
                                            var bindings = GetEditorCurveBindings();
                                            foreach (var binding in bindings)
                                            {
                                                VAW.VA.SetAnimationValueCustomProperty(binding, weight);
                                            }
                                        }
                                    }
                                    EditorGUILayout.EndHorizontal();
                                }
                                #endregion

                                {
                                    EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                                    EditorGUILayout.Space();
                                    if (GUILayout.Button(Language.GetContent(Language.Help.SelectionRangePinning)))
                                    {
                                        var popupPosition = GUIUtility.GUIToScreenPoint(rangePinningDropDownButtonRect.center);
                                        var window = CreateInstance<VeryAnimationRangePinningAnimationRiggingWindow>();
                                        popupPosition.x -= window.position.size.x / 2f;
                                        window.ShowAsDropDown(new Rect(popupPosition, Vector2.zero), window.position.size);
                                    }
                                    if (Event.current.type == EventType.Repaint)
                                        rangePinningDropDownButtonRect = GUILayoutUtility.GetLastRect();
                                    EditorGUILayout.Space();
                                    EditorGUILayout.EndHorizontal();
                                }
                                EditorGUILayout.EndVertical();
                            }
                        }
#endif
                    }
                    EditorGUILayout.EndVertical();
                }
            }
        }


        private void ResetAllSelectionHumanoidMenu(bool position, bool rotation, bool scale)
        {
            GenericMenu menu = new();
            {
                if (VAW.VA.IsHuman)
                {
                    menu.AddItem(Language.GetContent(Language.Help.EditorPoseHumanoidReset), false, () =>
                    {
                        Undo.RecordObject(this, "Humanoid Pose");
                        VAW.VA.SetSelectionHumanoidDefault(position, rotation);
                    });
                }
                if (VAW.VA.TransformPoseSave.IsEnableHumanDescriptionTransforms())
                {
                    menu.AddItem(Language.GetContent(Language.Help.EditorPoseAvatarConfiguration), false, () =>
                    {
                        Undo.RecordObject(this, "Avatar Configuration Pose");
                        VAW.VA.SetSelectionHumanoidAvatarConfiguration(position, rotation);
                    });
                }
                if (VAW.VA.TransformPoseSave.IsEnableTPoseTransform())
                {
                    menu.AddItem(Language.GetContent(Language.Help.EditorPoseTPose), false, () =>
                    {
                        Undo.RecordObject(this, "T Pose");
                        VAW.VA.SetSelectionHumanoidTPose(position, rotation);
                    });
                }
                if (VAW.VA.TransformPoseSave.IsEnableBindTransform())
                {
                    menu.AddItem(Language.GetContent(Language.Help.EditorPoseBind), false, () =>
                    {
                        Undo.RecordObject(this, UndoBindPose);
                        VAW.VA.SetSelectionBindPose(position, rotation, scale);
                    });
                }
                if (VAW.VA.TransformPoseSave.IsEnablePrefabTransform())
                {
                    menu.AddItem(Language.GetContent(Language.Help.EditorPosePrefab), false, () =>
                    {
                        Undo.RecordObject(this, UndoPrefabPose);
                        VAW.VA.SetSelectionPrefabPose(position, rotation, scale);
                    });
                }
                {
                    menu.AddItem(Language.GetContent(Language.Help.EditorPoseStart), false, () =>
                    {
                        Undo.RecordObject(this, UndoEditStartPose);
                        VAW.VA.SetSelectionEditStart(position, rotation, scale);
                    });
                }
            }
            menu.ShowAsContext();
        }
        private void ResetAllSelectionGenericMenu(bool position, bool rotation, bool scale)
        {
            GenericMenu menu = new();
            {
                if (VAW.VA.TransformPoseSave.IsEnableBindTransform())
                {
                    menu.AddItem(Language.GetContent(Language.Help.EditorPoseBind), false, () =>
                    {
                        Undo.RecordObject(this, UndoBindPose);
                        VAW.VA.SetSelectionBindPose(position, rotation, scale);
                    });
                }
                if (VAW.VA.TransformPoseSave.IsEnablePrefabTransform())
                {
                    menu.AddItem(Language.GetContent(Language.Help.EditorPosePrefab), false, () =>
                    {
                        Undo.RecordObject(this, UndoPrefabPose);
                        VAW.VA.SetSelectionPrefabPose(position, rotation, scale);
                    });
                }
                {
                    menu.AddItem(Language.GetContent(Language.Help.EditorPoseStart), false, () =>
                    {
                        Undo.RecordObject(this, UndoEditStartPose);
                        VAW.VA.SetSelectionEditStart(position, rotation, scale);
                    });
                }
            }
            menu.ShowAsContext();
        }

        public void PoseQuickSave(int index)
        {
            poseTree.QuickSave(index);
        }
        public void PoseQuickLoad(int index)
        {
            poseTree.QuickLoad(index);
        }

        public static void ForceRepaint()
        {
            if (instance == null) return;
            instance.Repaint();
        }
    }
}
