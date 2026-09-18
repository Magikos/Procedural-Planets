using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Serialization;

#if VERYANIMATION_ANIMATIONRIGGING
using UnityEngine.Animations.Rigging;
#endif

namespace VeryAnimation
{
    internal partial class VeryAnimation
    {
        internal enum ToolMode
        {
            Copy,
            Trim,
            Add,
            Combine,
            CreateNewClip,
            CreateNewKeyframe,
            BakeIK,
            AnimationRigging,
            HumanoidIK,
            RootMotion,
            ParameterRelatedCurves,
            RotationCurveInterpolation,
            KeyframeReduction,
            EnsureQuaternionContinuity,
            Cleanup,
            FixErrors,
            AdditiveReferencePose,
            AnimCompression,
            Export,
        }
        public ToolMode toolMode;
        public bool toolsHelp = true;
        public int toolCopy_FirstFrame;
        public int toolCopy_LastFrame;
        public int toolCopy_WriteFrame;
        public int toolTrim_FirstFrame;
        public int toolTrim_LastFrame;
        public AnimationClip toolAdd_Clip;
        public AnimationClip toolCombine_Clip;
        public enum CreateNewClipMode
        {
            Blank,
            Duplicate,
            Mirror,
            Result,
        }
        public CreateNewClipMode toolCreateNewClip_Mode;
        public int toolCreateNewClip_FirstFrame;
        public int toolCreateNewClip_LastFrame;
        public int toolCreateNewKeyframe_FirstFrame;
        public int toolCreateNewKeyframe_LastFrame;
        public int toolCreateNewKeyframe_IntervalFrame = 6;
        public bool toolCreateNewKeyframe_AnimatorRootT = true;
        public bool toolCreateNewKeyframe_AnimatorRootQ = true;
        public bool toolCreateNewKeyframe_AnimatorMuscle = true;
        public bool toolCreateNewKeyframe_AnimatorTDOF;
        public bool toolCreateNewKeyframe_TransformPosition;
        public bool toolCreateNewKeyframe_TransformRotation = true;
        public bool toolCreateNewKeyframe_TransformScale;
        public enum BakeIKMode
        {
            Simple,
            Interpolation,
        }
        public BakeIKMode toolBakeIK_Mode;
        public int toolBakeIK_FirstFrame;
        public int toolBakeIK_LastFrame;
        public int toolAnimationRigging_FirstFrame;
        public int toolAnimationRigging_LastFrame;
#pragma warning disable 0649
        public bool toolAnimationRigging_ChangeRigWeight;
        public float toolAnimationRigging_RigWeight = 1f;
        public bool toolAnimationRigging_RootMotionCancel;
        public bool toolAnimationRigging_ChangeConstraintWeight = true;
        public float toolAnimationRigging_ConstraintWeight = 1f;
#pragma warning restore 0649
        public bool toolHumanoidIK_Hand;
        public bool toolHumanoidIK_Foot = true;
        public int toolHumanoidIK_FirstFrame;
        public int toolHumanoidIK_LastFrame;
        public enum RootMotionMode
        {
            MotionCurves,
            RootCurves,
        }
        public RootMotionMode toolRootMotion_Mode;
        public bool toolCleanup_RemoveRoot;
        public bool toolCleanup_RemoveIK;
        public bool toolCleanup_RemoveTDOF;
        public bool toolCleanup_RemoveMotion;
        public bool toolCleanup_RemoveFinger;
        public bool toolCleanup_RemoveEyes;
        public bool toolCleanup_RemoveJaw;
        public bool toolCleanup_RemoveToes;
        public bool toolCleanup_RemoveTransformPosition;
        public bool toolCleanup_RemoveTransformRotation;
        public bool toolCleanup_RemoveTransformScale;
        public bool toolCleanup_RemoveBlendShape;
        public bool toolCleanup_RemoveObjectReference;
        public bool toolCleanup_RemoveEvent;
        public bool toolCleanup_RemoveMissing = true;
        public bool toolCleanup_RemoveHumanoidConflict = true;
        public bool toolCleanup_RemoveRootMotionConflict = true;
        public bool toolCleanup_RemoveUnnecessary = true;
        public bool toolCleanup_RemoveAvatarMaskDisable;
        public AvatarMask toolCleanup_RemoveAvatarMask;
        public enum RotationCurveInterpolationMode
        {
            Quaternion,
            EulerAngles,
        };
        public RotationCurveInterpolationMode toolRotationInterpolation_Mode;
        public float toolKeyframeReduction_RotationError = 0.5f;
        public float toolKeyframeReduction_PositionError = 0.5f;
        [FormerlySerializedAs("toolKeyframeReduction_ScaleAndOthersError")]
        public float toolKeyframeReduction_ScaleError = 0.5f;
        [FormerlySerializedAs("toolKeyframeReduction_EnableHumanoid")]
        public bool toolKeyframeReduction_EnableAnimator = true;
        [FormerlySerializedAs("toolKeyframeReduction_EnableHumanoidRootAndIKGoal")]
        public bool toolKeyframeReduction_EnableAnimatorRootAndIKGoal = true;
        [FormerlySerializedAs("toolKeyframeReduction_EnableGeneric")]
        public bool toolKeyframeReduction_EnableTransform = true;
        public bool toolKeyframeReduction_EnableOther = true;
        public bool toolAdditiveReferencePose_Has;
        public AnimationClip toolAdditiveReferencePose_Clip;
        public float toolAdditiveReferencePose_Time;
        public bool toolAnimCompression_Compressed;
        public bool toolAnimCompression_UseHighQualityCurve = true;
        public bool toolExport_ActiveOnly = true;
        public bool toolExport_Mesh = true;
        public enum ExportAnimationMode
        {
            None,
            CurrentClip,
            AllClips,
        };
        public ExportAnimationMode toolExport_AnimationMode = ExportAnimationMode.CurrentClip;
        public bool toolExport_BakeFootIK = true;
#pragma warning disable 0649
        public bool toolExport_BakeAnimationRigging;
#pragma warning restore 0649

#pragma warning disable 0414
        private bool toolBakeIK_AnimatorIKFoldout = true;
        private bool toolBakeIK_OriginalIKFoldout = true;
        private bool toolAnimationRigging_AnimatorIKFoldout = true;
#pragma warning restore 0414

        private class ParameterRelatedData
        {
            public string propertyName;
            public int parameterIndex;
            public bool enableAnimationCurve;
            public bool enableAnimatorParameter;
        }
        private List<ParameterRelatedData> toolParameterRelatedCurve_DataList;
        private bool toolParameterRelatedCurve_Update;
        private ReorderableList toolParameterRelatedCurve_List;

        public void ToolsGUI()
        {
            if (CurrentClip == null) return;
            var clip = CurrentClip;

            EditorGUILayout.BeginVertical(VAW.GuiStyleSkinBox);
            {
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUI.BeginChangeCheck();
                    var mode = (ToolMode)EditorGUILayout.EnumPopup(toolMode);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Tool Mode");
                        toolMode = mode;
                    }
                }
                {
                    if (GUILayout.Button(VAW.UEditorGUI.GUIContents.GetHelpIcon(), toolsHelp ? VAW.GuiStyleIconActiveButton : VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        Undo.RecordObject(VAW, "Change Tool Help");
                        toolsHelp = !toolsHelp;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel++;
            if (toolsHelp)
            {
                EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpToolsCopy + (int)toolMode), MessageType.Info);
            }
            else
            {
                EditorGUILayout.Space();
            }
            switch (toolMode)
            {
                case ToolMode.Copy: ToolsGUI_Copy(clip); break;
                case ToolMode.Trim: ToolsGUI_Trim(clip); break;
                case ToolMode.Add: ToolsGUI_Add(clip); break;
                case ToolMode.Combine: ToolsGUI_Combine(clip); break;
                case ToolMode.CreateNewClip: ToolsGUI_CreateNewClip(clip); break;
                case ToolMode.CreateNewKeyframe: ToolsGUI_CreateNewKeyframe(clip); break;
                case ToolMode.BakeIK: ToolsGUI_BakeIK(clip); break;
                case ToolMode.AnimationRigging: ToolsGUI_AnimationRigging(clip); break;
                case ToolMode.HumanoidIK: ToolsGUI_HumanoidIK(clip); break;
                case ToolMode.RootMotion: ToolsGUI_RootMotion(clip); break;
                case ToolMode.ParameterRelatedCurves: ToolsGUI_ParameterRelatedCurves(clip); break;
                case ToolMode.RotationCurveInterpolation: ToolsGUI_RotationCurveInterpolation(clip); break;
                case ToolMode.KeyframeReduction: ToolsGUI_KeyframeReduction(clip); break;
                case ToolMode.EnsureQuaternionContinuity: ToolsGUI_EnsureQuaternionContinuity(clip); break;
                case ToolMode.Cleanup: ToolsGUI_Cleanup(clip); break;
                case ToolMode.FixErrors: ToolsGUI_FixErrors(clip); break;
                case ToolMode.AdditiveReferencePose: ToolsGUI_AdditiveReferencePose(clip); break;
                case ToolMode.AnimCompression: ToolsGUI_AnimCompression(clip); break;
                case ToolMode.Export: ToolsGUI_Export(clip); break;
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();
        }

        private void ToolsGUI_Copy(AnimationClip clip)
        {
            if (UAnimationClipEditor != null)
            {
                float firstFrame = toolCopy_FirstFrame;
                float lastFrame = toolCopy_LastFrame;
                float additivePoseframe = 0.0f;
                UAnimationClipEditor.ClipRangeGUI(ref firstFrame, ref lastFrame, out bool changedStart, out bool changedStop, false, ref additivePoseframe, out _);
                if (changedStart)
                {
                    Undo.RecordObject(VAW, "Change First Frame");
                    toolCopy_FirstFrame = Mathf.RoundToInt(firstFrame);
                }
                if (changedStop)
                {
                    Undo.RecordObject(VAW, "Change Last Frame");
                    toolCopy_LastFrame = Mathf.RoundToInt(lastFrame);
                }
            }
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(Language.GetContent(Language.Help.ToolsCopyWriteFrame), GUILayout.Width(132));
                {
                    EditorGUI.BeginChangeCheck();
                    var frame = EditorGUILayout.IntField(toolCopy_WriteFrame, GUILayout.Width(64));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Write Frame");
                        toolCopy_WriteFrame = Math.Max(frame, 0);
                    }
                    if (GUILayout.Button(Styles.guiContentCurrentFrameButton, EditorStyles.miniButton, GUILayout.Width(64), GUILayout.Height(15)))
                    {
                        Undo.RecordObject(VAW, "Change Write Frame");
                        toolCopy_WriteFrame = UAw.GetCurrentFrame();
                    }
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("to", GUILayout.Width(32));
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField((toolCopy_WriteFrame + (toolCopy_LastFrame - toolCopy_FirstFrame)).ToString(), GUILayout.Width(100));
                EditorGUILayout.EndHorizontal();
            }
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space();
                if (GUILayout.Button("Copy"))
                {
                    ToolsCopy(clip);
                }
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ToolsGUI_Trim(AnimationClip clip)
        {
            if (UAnimationClipEditor != null)
            {
                float firstFrame = toolTrim_FirstFrame;
                float lastFrame = toolTrim_LastFrame;
                float additivePoseframe = 0.0f;
                UAnimationClipEditor.ClipRangeGUI(ref firstFrame, ref lastFrame, out bool changedStart, out bool changedStop, false, ref additivePoseframe, out _);
                if (changedStart)
                {
                    Undo.RecordObject(VAW, "Change First Frame");
                    toolTrim_FirstFrame = Mathf.RoundToInt(firstFrame);
                }
                if (changedStop)
                {
                    Undo.RecordObject(VAW, "Change Last Frame");
                    toolTrim_LastFrame = Mathf.RoundToInt(lastFrame);
                }
            }
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space();
                if (GUILayout.Button("Trim"))
                {
                    ToolsTrim(clip);
                }
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ToolsGUI_Add(AnimationClip clip)
        {
            {
                EditorGUI.BeginChangeCheck();
                var addClip = EditorGUILayout.ObjectField("Add Clip", toolAdd_Clip, typeof(AnimationClip), true) as AnimationClip;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Add Clip");
                    toolAdd_Clip = addClip;
                }
            }
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space();
                EditorGUI.BeginDisabledGroup(toolAdd_Clip == null);
                if (GUILayout.Button("Add"))
                {
                    ToolsAdd(clip);
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ToolsGUI_Combine(AnimationClip clip)
        {
            {
                EditorGUI.BeginChangeCheck();
                var combineClip = EditorGUILayout.ObjectField("Combine Clip", toolCombine_Clip, typeof(AnimationClip), true) as AnimationClip;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Combine Clip");
                    toolCombine_Clip = combineClip;
                }
            }
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space();
                EditorGUI.BeginDisabledGroup(toolCombine_Clip == null);
                if (GUILayout.Button("Combine"))
                {
                    ToolsCombine(clip);
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ToolsGUI_CreateNewClip(AnimationClip clip)
        {
            {
                {
                    EditorGUI.BeginChangeCheck();
                    var mode = (CreateNewClipMode)GUILayout.Toolbar((int)toolCreateNewClip_Mode, Styles.createNewClipModeStrings, EditorStyles.miniButton);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Create New Clip Mode");
                        toolCreateNewClip_Mode = mode;
                    }
                    if (toolsHelp)
                    {
                        EditorGUILayout.HelpBox(Language.GetText(Language.Help.ToolsCreateNewClipBlank + (int)toolCreateNewClip_Mode), MessageType.Info);
                    }
                    EditorGUILayout.Space();
                }
                if (UAnimationClipEditorTotal != null &&
                    toolCreateNewClip_Mode == CreateNewClipMode.Result)
                {
                    float firstFrame = toolCreateNewClip_FirstFrame;
                    float lastFrame = toolCreateNewClip_LastFrame;
                    float additivePoseframe = 0.0f;
                    UAnimationClipEditorTotal.ClipRangeGUI(ref firstFrame, ref lastFrame, out bool changedStart, out bool changedStop, false, ref additivePoseframe, out _);
                    if (changedStart)
                    {
                        Undo.RecordObject(VAW, "Change First Frame");
                        toolCreateNewClip_FirstFrame = Mathf.RoundToInt(firstFrame);
                    }
                    if (changedStop)
                    {
                        Undo.RecordObject(VAW, "Change Last Frame");
                        toolCreateNewClip_LastFrame = Mathf.RoundToInt(lastFrame);
                    }
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space();
                if (GUILayout.Button("Create"))
                {
                    var name = clip.name;
                    if (toolCreateNewClip_Mode == CreateNewClipMode.Mirror)
                        name += " (mirror)";
                    else if (toolCreateNewClip_Mode == CreateNewClipMode.Result)
                        name += " (result)";
                    var assetPath = $"{EditorCommon.GetAssetPath(clip)}/{EditorCommon.GetSafeFileName(name)}.anim";
                    var uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
                    string path = EditorCommon.SaveFilePanelInAssets("Create new animation clip", Path.GetDirectoryName(uniquePath), Path.GetFileName(uniquePath), "anim");
                    if (path != null)
                    {
                        ToolsCreateNewClip(path);
                    }
                }
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ToolsGUI_CreateNewKeyframe(AnimationClip clip)
        {
            if (UAnimationClipEditor != null)
            {
                float firstFrame = toolCreateNewKeyframe_FirstFrame;
                float lastFrame = toolCreateNewKeyframe_LastFrame;
                float additivePoseframe = 0.0f;
                UAnimationClipEditor.ClipRangeGUI(ref firstFrame, ref lastFrame, out bool changedStart, out bool changedStop, false, ref additivePoseframe, out _);
                if (changedStart)
                {
                    Undo.RecordObject(VAW, "Change First Frame");
                    toolCreateNewKeyframe_FirstFrame = Mathf.RoundToInt(firstFrame);
                }
                if (changedStop)
                {
                    Undo.RecordObject(VAW, "Change Last Frame");
                    toolCreateNewKeyframe_LastFrame = Mathf.RoundToInt(lastFrame);
                }
            }
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(Language.GetContent(Language.Help.ToolsCreateNewKeyframeIntervalFrame), GUILayout.Width(132));
                {
                    EditorGUI.BeginChangeCheck();
                    var frame = EditorGUILayout.IntField(toolCreateNewKeyframe_IntervalFrame, GUILayout.Width(64));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Interval Frame");
                        toolCreateNewKeyframe_IntervalFrame = Math.Max(frame, 1);
                    }
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"{GetFrameTime(toolCreateNewKeyframe_IntervalFrame)} Second", VAW.GuiStyleMiddleRightGreyMiniLabel);
                EditorGUILayout.EndHorizontal();
            }
            if (IsHuman)
            {
                EditorGUILayout.LabelField(Styles.guiContentAnimator);
                EditorGUI.indentLevel++;
                {
                    {
                        EditorGUI.BeginChangeCheck();
                        var flag = EditorGUILayout.ToggleLeft("RootT", toolCreateNewKeyframe_AnimatorRootT);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(VAW, "Change Flag");
                            toolCreateNewKeyframe_AnimatorRootT = flag;
                        }
                    }
                    {
                        EditorGUI.BeginChangeCheck();
                        var flag = EditorGUILayout.ToggleLeft("RootQ", toolCreateNewKeyframe_AnimatorRootQ);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(VAW, "Change Flag");
                            toolCreateNewKeyframe_AnimatorRootQ = flag;
                        }
                    }
                    {
                        EditorGUI.BeginChangeCheck();
                        var flag = EditorGUILayout.ToggleLeft("Muscle", toolCreateNewKeyframe_AnimatorMuscle);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(VAW, "Change Flag");
                            toolCreateNewKeyframe_AnimatorMuscle = flag;
                        }
                    }
                    if (HumanoidHasTDoF)
                    {
                        EditorGUI.BeginChangeCheck();
                        var flag = EditorGUILayout.ToggleLeft("TDOF", toolCreateNewKeyframe_AnimatorTDOF);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(VAW, "Change Flag");
                            toolCreateNewKeyframe_AnimatorTDOF = flag;
                        }
                    }
                }
                EditorGUI.indentLevel--;
            }
            {
                EditorGUILayout.LabelField(Styles.guiContentTransform);
                EditorGUI.indentLevel++;
                {
                    {
                        EditorGUI.BeginChangeCheck();
                        var flag = EditorGUILayout.ToggleLeft("Position", toolCreateNewKeyframe_TransformPosition);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(VAW, "Change Flag");
                            toolCreateNewKeyframe_TransformPosition = flag;
                        }
                    }
                    {
                        EditorGUI.BeginChangeCheck();
                        var flag = EditorGUILayout.ToggleLeft("Rotation", toolCreateNewKeyframe_TransformRotation);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(VAW, "Change Flag");
                            toolCreateNewKeyframe_TransformRotation = flag;
                        }
                    }
                    {
                        EditorGUI.BeginChangeCheck();
                        var flag = EditorGUILayout.ToggleLeft("Scale", toolCreateNewKeyframe_TransformScale);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(VAW, "Change Flag");
                            toolCreateNewKeyframe_TransformScale = flag;
                        }
                    }
                }
                EditorGUI.indentLevel--;
            }
            bool disable = (SelectionBones == null || SelectionBones.Count == 0);
            {
                EditorGUI.BeginDisabledGroup(disable);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space();
                if (GUILayout.Button("Apply"))
                {
                    ToolsCreateNewKeyframe(clip);
                }
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
                EditorGUI.EndDisabledGroup();
            }
        }

        private void ToolsGUI_BakeIK(AnimationClip clip)
        {
            if (UAnimationClipEditor != null)
            {
                float firstFrame = toolBakeIK_FirstFrame;
                float lastFrame = toolBakeIK_LastFrame;
                float additivePoseframe = 0.0f;
                UAnimationClipEditor.ClipRangeGUI(ref firstFrame, ref lastFrame, out bool changedStart, out bool changedStop, false, ref additivePoseframe, out _);
                if (changedStart)
                {
                    Undo.RecordObject(VAW, "Change First Frame");
                    toolBakeIK_FirstFrame = Mathf.RoundToInt(firstFrame);
                }
                if (changedStop)
                {
                    Undo.RecordObject(VAW, "Change Last Frame");
                    toolBakeIK_LastFrame = Mathf.RoundToInt(lastFrame);
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var mode = (BakeIKMode)GUILayout.Toolbar((int)toolBakeIK_Mode, Styles.bakeIKModeStrings, EditorStyles.miniButton);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Root Motion Mode");
                    toolBakeIK_Mode = mode;
                }
                if (toolsHelp)
                {
                    EditorGUILayout.HelpBox(Language.GetText(Language.Help.ToolsBakeIKSimple + (int)toolBakeIK_Mode), MessageType.Info);
                }
                EditorGUILayout.Space();
            }
            bool disable = !(animatorIK.ikData != null && animatorIK.ikData.Any(data => data.enable)) &&
                            !(originalIK.ikData != null && originalIK.ikData.Any(data => data.enable));
            {
                #region AnimatorIK
                if (IsHuman && animatorIK.ikData != null)
                {
                    toolBakeIK_AnimatorIKFoldout = EditorGUILayout.Foldout(toolBakeIK_AnimatorIKFoldout, "Animator IK", true);
                    if (toolBakeIK_AnimatorIKFoldout)
                    {
                        EditorGUI.indentLevel++;
                        for (int index = 0; index < animatorIK.ikData.Length; index++)
                        {
                            EditorGUILayout.BeginHorizontal();
                            {
                                EditorGUI.BeginChangeCheck();
                                EditorGUILayout.ToggleLeft(AnimatorIKCore.IKTargetStrings[index], animatorIK.ikData[index].enable, GUILayout.Width(160f));
                                if (EditorGUI.EndChangeCheck())
                                {
                                    animatorIK.ChangeTargetIK((AnimatorIKCore.IKTarget)index);
                                }
                            }
                            EditorGUILayout.LabelField(animatorIK.GetSynchroInfoToString((AnimatorIKCore.IKTarget)index), VAW.GuiStyleMiddleRightMiniLabel);
                            EditorGUILayout.EndHorizontal();
                        }
                        EditorGUI.indentLevel--;
                    }
                }
                #endregion
                #region OriginalIK
                if (originalIK.ikData != null)
                {
                    toolBakeIK_OriginalIKFoldout = EditorGUILayout.Foldout(toolBakeIK_OriginalIKFoldout, "Original IK", true);
                    if (toolBakeIK_OriginalIKFoldout)
                    {
                        EditorGUI.indentLevel++;
                        for (int index = 0; index < originalIK.ikData.Count; index++)
                        {
                            EditorGUILayout.BeginHorizontal();
                            {
                                EditorGUI.BeginChangeCheck();
                                EditorGUILayout.ToggleLeft(originalIK.ikData[index].name, originalIK.ikData[index].enable, GUILayout.Width(160f));
                                if (EditorGUI.EndChangeCheck())
                                {
                                    originalIK.ChangeTargetIK(index);
                                }
                            }
                            EditorGUILayout.LabelField(originalIK.GetSynchroInfoToString(index), VAW.GuiStyleMiddleRightMiniLabel);
                            EditorGUILayout.EndHorizontal();
                        }
                        EditorGUI.indentLevel--;
                    }
                }
                #endregion
            }
            EditorGUI.BeginDisabledGroup(disable);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.Space();
            if (GUILayout.Button("Apply"))
            {
                ToolsGenerateBakeIK(clip);
            }
            EditorGUILayout.Space();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
        }

        private void ToolsGUI_AnimationRigging(AnimationClip clip)
        {
#if VERYANIMATION_ANIMATIONRIGGING
            EditorGUI.BeginDisabledGroup(!AnimationRigging.IsValid);
            if (UAnimationClipEditorTotal != null)
            {
                float firstFrame = toolAnimationRigging_FirstFrame;
                float lastFrame = toolAnimationRigging_LastFrame;
                float additivePoseframe = 0.0f;
                UAnimationClipEditorTotal.ClipRangeGUI(ref firstFrame, ref lastFrame, out bool changedStart, out bool changedStop, false, ref additivePoseframe, out _);
                if (changedStart)
                {
                    Undo.RecordObject(VAW, "Change First Frame");
                    toolAnimationRigging_FirstFrame = Mathf.RoundToInt(firstFrame);
                }
                if (changedStop)
                {
                    Undo.RecordObject(VAW, "Change Last Frame");
                    toolAnimationRigging_LastFrame = Mathf.RoundToInt(lastFrame);
                }
            }
            bool constraintDisable = !(animatorIK.ikData != null && animatorIK.ikData.Any(data => data.enable && data.rigConstraint != null));
            EditorGUILayout.LabelField("Rig");
            EditorGUI.indentLevel++;
            {
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUI.BeginChangeCheck();
                    var flag = EditorGUILayout.ToggleLeft("Rig Weight", toolAnimationRigging_ChangeRigWeight);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Settings");
                        toolAnimationRigging_ChangeRigWeight = flag;
                    }
                }
                if (toolAnimationRigging_ChangeRigWeight)
                {
                    EditorGUI.BeginChangeCheck();
                    var value = EditorGUILayout.Slider(toolAnimationRigging_RigWeight, 0f, 1f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Settings");
                        toolAnimationRigging_RigWeight = value;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.LabelField("Constraint");
            EditorGUI.indentLevel++;
            EditorGUI.BeginDisabledGroup(constraintDisable);
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsAnimationRiggingRootMotionCancel), toolAnimationRigging_RootMotionCancel);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Settings");
                    toolAnimationRigging_RootMotionCancel = flag;
                }
            }
            {
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUI.BeginChangeCheck();
                    var flag = EditorGUILayout.ToggleLeft("Constraint Weight", toolAnimationRigging_ChangeConstraintWeight);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Settings");
                        toolAnimationRigging_ChangeConstraintWeight = flag;
                    }
                }
                if (toolAnimationRigging_ChangeConstraintWeight)
                {
                    EditorGUI.BeginChangeCheck();
                    var value = EditorGUILayout.Slider(toolAnimationRigging_ConstraintWeight, 0f, 1f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Settings");
                        toolAnimationRigging_ConstraintWeight = value;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.EndDisabledGroup();
            {
                #region AnimatorIK
                if (IsHuman && animatorIK.ikData != null)
                {
                    toolAnimationRigging_AnimatorIKFoldout = EditorGUILayout.Foldout(toolAnimationRigging_AnimatorIKFoldout, "Animator IK", true);
                    if (toolAnimationRigging_AnimatorIKFoldout)
                    {
                        EditorGUI.indentLevel++;
                        for (int index = 0; index < animatorIK.ikData.Length; index++)
                        {
                            var data = animatorIK.ikData[index];
                            if (data.rigConstraint == null)
                                continue;
                            EditorGUILayout.BeginHorizontal();
                            {
                                EditorGUI.BeginChangeCheck();
                                EditorGUILayout.ToggleLeft("", data.enable, GUILayout.Width(64f));
                                if (EditorGUI.EndChangeCheck())
                                {
                                    animatorIK.ChangeTargetIK((AnimatorIKCore.IKTarget)index);
                                }
                            }
                            {
                                if (GUILayout.Button(AnimatorIKCore.IKTargetStrings[index]))
                                {
                                    SelectGameObject(data.rigConstraint.gameObject);
                                    {
                                        var list = new List<EditorCurveBinding>();
                                        {
                                            list.AddRange(animatorIK.GetAnimationRiggingConstraintBindings((AnimatorIKCore.IKTarget)index));
                                            list.Add(data.rigConstraintWeight);
                                        }
                                        SetAnimationWindowSynchroSelection(list);
                                    }
                                }
                            }
                            EditorGUILayout.LabelField(animatorIK.GetSynchroInfoToString((AnimatorIKCore.IKTarget)index, true), VAW.GuiStyleMiddleRightMiniLabel);
                            EditorGUILayout.EndHorizontal();
                        }
                        EditorGUI.indentLevel--;
                    }
                }
                #endregion
            }
            EditorGUI.indentLevel--;
            EditorGUI.BeginDisabledGroup(!toolAnimationRigging_ChangeRigWeight && constraintDisable);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.Space();
            if (GUILayout.Button("Clear"))
            {
                ToolsClearAnimationRigging(clip);
            }
            EditorGUILayout.Space();
            if (GUILayout.Button("Generate"))
            {
                ToolsGenerateAnimationRigging(clip);
            }
            EditorGUILayout.Space();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
            EditorGUI.EndDisabledGroup();
#endif
        }

        private void ToolsGUI_HumanoidIK(AnimationClip clip)
        {
            EditorGUI.BeginDisabledGroup(!IsHuman || !clip.isHumanMotion);
            if (UAnimationClipEditor != null)
            {
                float firstFrame = toolHumanoidIK_FirstFrame;
                float lastFrame = toolHumanoidIK_LastFrame;
                float additivePoseframe = 0.0f;
                UAnimationClipEditor.ClipRangeGUI(ref firstFrame, ref lastFrame, out bool changedStart, out bool changedStop, false, ref additivePoseframe, out _);
                if (changedStart)
                {
                    Undo.RecordObject(VAW, "Change First Frame");
                    toolHumanoidIK_FirstFrame = Mathf.RoundToInt(firstFrame);
                }
                if (changedStop)
                {
                    Undo.RecordObject(VAW, "Change Last Frame");
                    toolHumanoidIK_LastFrame = Mathf.RoundToInt(lastFrame);
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft("Hand IK", toolHumanoidIK_Hand);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change IK Curve setting");
                    toolHumanoidIK_Hand = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft("Foot IK", toolHumanoidIK_Foot);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change IK Curve setting");
                    toolHumanoidIK_Foot = flag;
                }
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(!IsHuman || !clip.isHumanMotion || (!toolHumanoidIK_Hand && !toolHumanoidIK_Foot));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.Space();
            if (GUILayout.Button("Clear"))
            {
                ToolsClearHumanoidIK(clip);
            }
            EditorGUILayout.Space();
            if (GUILayout.Button("Generate"))
            {
                ToolsGenerateHumanoidIK(clip);
            }
            EditorGUILayout.Space();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
            if (!IsHuman || !clip.isHumanMotion)
            {
                EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpToolsHumanoidWarning), MessageType.Warning);
            }
        }

        private void ToolsGUI_RootMotion(AnimationClip clip)
        {
            {
                EditorGUI.BeginChangeCheck();
                var mode = (RootMotionMode)GUILayout.Toolbar((int)toolRootMotion_Mode, Styles.rootMotionModeStrings, EditorStyles.miniButton);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Root Motion Mode");
                    toolRootMotion_Mode = mode;
                }
            }
            if (toolsHelp)
            {
                EditorGUILayout.HelpBox(Language.GetText(Language.Help.ToolsRootMotionMotionCurves + (int)toolRootMotion_Mode), MessageType.Info);
            }
            EditorGUILayout.Space();
            if (toolRootMotion_Mode == RootMotionMode.MotionCurves)
            {
                #region MotionCurves
                var disable = VAW.Animator == null;
                EditorGUI.BeginDisabledGroup(disable);

                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.Space();
                    var index = -1;
                    if (SelectionMotionTool)
                    {
                        if (Tools.current != Tool.View)
                        {
                            if (CurrentTool() == Tool.Move) index = 0;
                            else index = 1;
                        }
                    }
                    EditorGUI.BeginChangeCheck();
                    index = GUILayout.Toolbar(index, VAW.GuiContentMoveRotateTools);
                    if (EditorGUI.EndChangeCheck())
                    {
                        LastTool = index switch
                        {
                            0 => Tool.Move,
                            _ => Tool.Rotate,
                        };
                        SelectMotionTool();
                    }
                    EditorGUILayout.Space();
                    EditorGUILayout.EndHorizontal();
                }

                GUILayout.Space(24f);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space();
                if (GUILayout.Button("Clear"))
                {
                    ToolsRootMotionMotionClear(clip);
                }
                EditorGUILayout.Space();
                if (GUILayout.Button("Generate"))
                {
                    ToolsRootMotionMotionGenerate(clip);
                }
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.Space();
                if (VAW.Animator == null)
                {
                    EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpToolsNotAnimatorWarning), MessageType.Warning);
                }
                #endregion
            }
            else if (toolRootMotion_Mode == RootMotionMode.RootCurves)
            {
                #region RootCurves
                var disable = IsHuman || RootMotionBoneIndex < 0 || VAW.Animator == null;
                EditorGUI.BeginDisabledGroup(disable);

                GUILayout.Space(24f);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space();
                if (GUILayout.Button("Clear"))
                {
                    ToolsRootMotionRootClear(clip);
                }
                EditorGUILayout.Space();
                if (GUILayout.Button("Generate"))
                {
                    ToolsRootMotionRootGenerate(clip);
                }
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.Space();
                if (disable)
                {
                    EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpToolsGenericAndRootNodeWarning), MessageType.Warning);
                }
                #endregion
            }
        }

        private void ToolsGUI_ParameterRelatedCurves(AnimationClip clip)
        {
            var e = Event.current;
            if (e.type == EventType.Layout)
            {
                ParameterRelatedCurveUpdateList();
            }
            toolParameterRelatedCurve_List?.DoLayoutList();
            if (clip.legacy || VAW.Animator == null)
            {
                EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpToolsNotAnimatorWarning), MessageType.Warning);
            }
        }

        private void ToolsGUI_RotationCurveInterpolation(AnimationClip clip)
        {
            {
                EditorGUI.BeginChangeCheck();
                var mode = (RotationCurveInterpolationMode)EditorGUILayout.EnumPopup("Interpolation", toolRotationInterpolation_Mode);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Rotation Curve Interpolation setting");
                    toolRotationInterpolation_Mode = mode;
                }
            }
            if (toolRotationInterpolation_Mode == RotationCurveInterpolationMode.EulerAngles)
            {
                EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpToolsRotationCurveInterpolationEulerAnglesWarning), MessageType.Warning);
            }
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space();
                if (GUILayout.Button("Convert"))
                {
                    ToolsRotationCurveInterpolation(clip, toolRotationInterpolation_Mode);
                }
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ToolsGUI_KeyframeReduction(AnimationClip clip)
        {
            {
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUILayout.LabelField("Rotation Error", GUILayout.Width(150f));
                        EditorGUI.BeginChangeCheck();
                        var param = EditorGUILayout.FloatField(toolKeyframeReduction_RotationError, GUILayout.Width(100f));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(VAW, "Change Keyframe Reduction setting");
                            toolKeyframeReduction_RotationError = param;
                        }
                    }
                    EditorGUILayout.Space();
                    {
                        if (GUILayout.Button("Reset"))
                        {
                            Undo.RecordObject(VAW, "Change Keyframe Reduction setting");
                            toolKeyframeReduction_RotationError = 0.5f;
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUILayout.LabelField("Position Error", GUILayout.Width(150f));
                        EditorGUI.BeginChangeCheck();
                        var param = EditorGUILayout.FloatField(toolKeyframeReduction_PositionError, GUILayout.Width(100f));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(VAW, "Change Keyframe Reduction setting");
                            toolKeyframeReduction_PositionError = param;
                        }
                    }
                    EditorGUILayout.Space();
                    {
                        if (GUILayout.Button("Reset"))
                        {
                            Undo.RecordObject(VAW, "Change Keyframe Reduction setting");
                            toolKeyframeReduction_PositionError = 0.5f;
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUILayout.LabelField("Scale Error", GUILayout.Width(150f));
                        EditorGUI.BeginChangeCheck();
                        var param = EditorGUILayout.FloatField(toolKeyframeReduction_ScaleError, GUILayout.Width(100f));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(VAW, "Change Keyframe Reduction setting");
                            toolKeyframeReduction_ScaleError = param;
                        }
                    }
                    EditorGUILayout.Space();
                    {
                        if (GUILayout.Button("Reset"))
                        {
                            Undo.RecordObject(VAW, "Change Keyframe Reduction setting");
                            toolKeyframeReduction_ScaleError = 0.5f;
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                {
                    EditorGUI.BeginChangeCheck();
                    var flag = EditorGUILayout.ToggleLeft(Styles.guiContentAnimatorCurves, toolKeyframeReduction_EnableAnimator);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Keyframe Reduction setting");
                        toolKeyframeReduction_EnableAnimator = flag;
                    }
                }
                if (toolKeyframeReduction_EnableAnimator)
                {
                    EditorGUI.indentLevel++;
                    EditorGUI.BeginChangeCheck();
                    var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsKeyframeReductionRootAndIKGoalCurves), toolKeyframeReduction_EnableAnimatorRootAndIKGoal);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Keyframe Reduction setting");
                        toolKeyframeReduction_EnableAnimatorRootAndIKGoal = flag;
                    }
                    EditorGUI.indentLevel--;
                }
                {
                    EditorGUI.BeginChangeCheck();
                    var flag = EditorGUILayout.ToggleLeft(Styles.guiContentTransformCurves, toolKeyframeReduction_EnableTransform);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Keyframe Reduction setting");
                        toolKeyframeReduction_EnableTransform = flag;
                    }
                }
                {
                    EditorGUI.BeginChangeCheck();
                    var flag = EditorGUILayout.ToggleLeft(Styles.guiContentOtherCurves, toolKeyframeReduction_EnableOther);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Keyframe Reduction setting");
                        toolKeyframeReduction_EnableOther = flag;
                    }
                }
                {
                    EditorGUI.BeginDisabledGroup(!toolKeyframeReduction_EnableAnimator && !toolKeyframeReduction_EnableTransform && !toolKeyframeReduction_EnableOther);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.Space();
                    if (GUILayout.Button("Reduction"))
                    {
                        ToolsKeyframeReduction(clip);
                    }
                    EditorGUILayout.Space();
                    EditorGUILayout.EndHorizontal();
                    EditorGUI.EndDisabledGroup();
                }
            }
        }

        private void ToolsGUI_EnsureQuaternionContinuity(AnimationClip clip)
        {
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space();
                if (GUILayout.Button("Execute"))
                {
                    ToolsEnsureQuaternionContinuity(clip);
                }
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ToolsGUI_Cleanup(AnimationClip clip)
        {
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveAnimatorRootCurves), toolCleanup_RemoveRoot);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveRoot = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveAnimatorIKGoalCurves), toolCleanup_RemoveIK);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveIK = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveAnimatorTDOFCurves), toolCleanup_RemoveTDOF);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveTDOF = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveAnimatorMotionCurves), toolCleanup_RemoveMotion);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveMotion = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveAnimatorFingerCurves), toolCleanup_RemoveFinger);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveFinger = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveAnimatorEyeCurves), toolCleanup_RemoveEyes);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveEyes = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveAnimatorJawCurve), toolCleanup_RemoveJaw);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveJaw = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveAnimatorToeCurves), toolCleanup_RemoveToes);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveToes = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveTransformPositionCurves), toolCleanup_RemoveTransformPosition);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveTransformPosition = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveTransformRotationCurves), toolCleanup_RemoveTransformRotation);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveTransformRotation = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveTransformScaleCurves), toolCleanup_RemoveTransformScale);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveTransformScale = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveBlendShapeCurves), toolCleanup_RemoveBlendShape);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveBlendShape = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveObjectReferenceCurves), toolCleanup_RemoveObjectReference);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveObjectReference = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveAnimationEvents), toolCleanup_RemoveEvent);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveEvent = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveMissingCurves), toolCleanup_RemoveMissing);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveMissing = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveHumanoidandGenericconflictCurves), toolCleanup_RemoveHumanoidConflict);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveHumanoidConflict = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveRootmotionconflictCurves), toolCleanup_RemoveRootMotionConflict);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveRootMotionConflict = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveUnnecessaryCurves), toolCleanup_RemoveUnnecessary);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveUnnecessary = flag;
                }
            }
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.ToolsCleanupRemoveAvatarMaskDisable), toolCleanup_RemoveAvatarMaskDisable);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Cleanup setting");
                    toolCleanup_RemoveAvatarMaskDisable = flag;
                }
                {
                    EditorGUI.BeginChangeCheck();
                    var mask = EditorGUILayout.ObjectField(toolCleanup_RemoveAvatarMask, typeof(AvatarMask), false) as AvatarMask;
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Cleanup setting");
                        toolCleanup_RemoveAvatarMask = mask;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space();
                EditorGUI.BeginDisabledGroup(!toolCleanup_RemoveRoot && !toolCleanup_RemoveIK && !toolCleanup_RemoveTDOF && !toolCleanup_RemoveMotion &&
                                                !toolCleanup_RemoveFinger && !toolCleanup_RemoveEyes && !toolCleanup_RemoveJaw && !toolCleanup_RemoveToes &&
                                                !toolCleanup_RemoveTransformPosition && !toolCleanup_RemoveTransformRotation && !toolCleanup_RemoveTransformScale && !toolCleanup_RemoveBlendShape &&
                                                !toolCleanup_RemoveObjectReference && !toolCleanup_RemoveEvent && !toolCleanup_RemoveMissing && !toolCleanup_RemoveHumanoidConflict && !toolCleanup_RemoveRootMotionConflict && !toolCleanup_RemoveUnnecessary && !toolCleanup_RemoveAvatarMaskDisable);
                if (GUILayout.Button("Cleanup"))
                {
                    ToolsCleanup(clip);
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ToolsGUI_FixErrors(AnimationClip clip)
        {
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space();
                if (GUILayout.Button("Fix"))
                {
                    ToolsFixErrors(clip);
                }
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ToolsGUI_AdditiveReferencePose(AnimationClip clip)
        {
            {
                {
                    EditorGUI.BeginChangeCheck();
                    var flag = EditorGUILayout.ToggleLeft("Has Additive Reference Pose", toolAdditiveReferencePose_Has);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change AdditiveReferencePose setting");
                        toolAdditiveReferencePose_Has = flag;
                    }
                }
                EditorGUI.BeginDisabledGroup(!toolAdditiveReferencePose_Has);
                EditorGUI.indentLevel++;
                {
                    EditorGUI.BeginChangeCheck();
                    var refClip = EditorGUILayout.ObjectField("Clip", toolAdditiveReferencePose_Clip, typeof(AnimationClip), true) as AnimationClip;
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change AdditiveReferencePose setting");
                        toolAdditiveReferencePose_Clip = refClip;
                    }
                }
                if (toolAdditiveReferencePose_Clip != null)
                {
                    EditorGUI.BeginChangeCheck();
                    var refTime = EditorGUILayout.Slider("Time", toolAdditiveReferencePose_Time, 0f, toolAdditiveReferencePose_Clip.length);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change AdditiveReferencePose setting");
                        toolAdditiveReferencePose_Time = refTime;
                    }
                }
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(toolAdditiveReferencePose_Has && toolAdditiveReferencePose_Clip == null);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space();
                if (GUILayout.Button("Set Current Clip"))
                {
                    ToolsAdditiveReferencePose(clip, false);
                }
                EditorGUILayout.Space();
                if (GUILayout.Button("Set All Clips"))
                {
                    ToolsAdditiveReferencePose(clip, true);
                }
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
                EditorGUI.EndDisabledGroup();
            }
        }

        private void ToolsGUI_AnimCompression(AnimationClip clip)
        {
            {
                {
                    EditorGUI.BeginChangeCheck();
                    var flag = EditorGUILayout.Toggle(Language.GetContent(Language.Help.ToolsAnimCompressionCompressed), toolAnimCompression_Compressed);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Anim Compression setting");
                        toolAnimCompression_Compressed = flag;
                        if (toolAnimCompression_Compressed)
                            toolAnimCompression_UseHighQualityCurve = true;
                    }
                }
                {
                    EditorGUI.BeginDisabledGroup(clip.legacy);
                    EditorGUI.BeginChangeCheck();
                    var flag = EditorGUILayout.Toggle(Language.GetContent(Language.Help.ToolsAnimCompressionUseHighQualityCurve), toolAnimCompression_UseHighQualityCurve);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Anim Compression setting");
                        toolAnimCompression_UseHighQualityCurve = flag;
                        if (!toolAnimCompression_UseHighQualityCurve)
                            toolAnimCompression_Compressed = false;
                    }
                    EditorGUI.EndDisabledGroup();
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space();
                if (GUILayout.Button("Set Current Clip"))
                {
                    ToolsAnimCompression(clip, false);
                }
                EditorGUILayout.Space();
                if (GUILayout.Button("Set All Clips"))
                {
                    ToolsAnimCompression(clip, true);
                }
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ToolsGUI_Export(AnimationClip clip)
        {
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.Toggle("Active Only", toolExport_ActiveOnly);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Export setting");
                    toolExport_ActiveOnly = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.Toggle("Export Mesh", toolExport_Mesh);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Export setting");
                    toolExport_Mesh = flag;
                }
            }
            {
                EditorGUI.BeginChangeCheck();
                var mode = (ExportAnimationMode)EditorGUILayout.EnumPopup("Export Animation", toolExport_AnimationMode);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, "Change Export setting");
                    toolExport_AnimationMode = mode;
                }
            }
            if (toolExport_AnimationMode != ExportAnimationMode.None)
            {
                EditorGUILayout.LabelField("Bake");
                EditorGUI.indentLevel++;
                if (IsHuman)
                {
                    EditorGUI.BeginChangeCheck();
                    var flag = EditorGUILayout.Toggle(Styles.guiContentFootIK, toolExport_BakeFootIK);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Bake Flag");
                        toolExport_BakeFootIK = flag;
                    }
                }
#if VERYANIMATION_ANIMATIONRIGGING
                {
                    bool disableBake = !AnimationRigging.IsValid;
                    EditorGUI.BeginDisabledGroup(disableBake);
                    EditorGUI.BeginChangeCheck();
                    var flag = EditorGUILayout.Toggle("Animation Rigging", toolExport_BakeAnimationRigging);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(VAW, "Change Bake Flag");
                        toolExport_BakeAnimationRigging = flag;
                    }
                    EditorGUI.EndDisabledGroup();
                }
#endif
                EditorGUI.indentLevel--;
            }
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space();
                if (GUILayout.Button("Export"))
                {
                    ToolsExport();
                }
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
            }
        }

        public void DuplicateAndReplace()
        {
            var assetPath = $"{EditorCommon.GetAssetPath(CurrentClip)}/{EditorCommon.GetSafeFileName(CurrentClip.name)}.anim";
            var uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            string path = EditorCommon.SaveFilePanelInAssets("Duplicate", Path.GetDirectoryName(uniquePath), Path.GetFileName(uniquePath), "anim");
            if (path == null)
                return;
            {
                using (new VeryAnimationWindow.CustomAssetModificationProcessor.PauseScope())
                {
                    AssetDatabase.CreateAsset(AnimationClip.Instantiate(CurrentClip), path);
                    var newClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

                    #region Extra
                    if (EditorUtility.DisplayDialog("Extra", Language.GetText(Language.Help.AnimationClipDuplicateandReplaceDialog), "Yes", "No"))
                    {
                        ToolsReductionCurve(newClip);

                        ToolsRotationCurveInterpolation(newClip, RotationCurveInterpolationMode.Quaternion);
                    }
                    #endregion

                    bool replaced = false;
                    if (UAw.GetLinkedWithTimeline())
                    {
                        #region Timeline
#if VERYANIMATION_TIMELINE
                        var timelineClip = UAw.GetTimelineAnimationClip();
                        if (timelineClip == CurrentClip)
                        {
                            UAw.SetTimelineAnimationClip(newClip, "Duplicate and Replace");
                            UAw.EditSequencerClip(UAw.GetTimelineClip());
                            replaced = true;
                        }
#else
                        Assert.IsTrue(false);
#endif
                        #endregion
                    }
                    else
                    {
                        #region Animator
                        if (VAW.Animator != null && VAW.Animator.runtimeAnimatorController != null)
                        {
                            var ac = AnimationCommon.GetAnimatorController(VAW.Animator);
                            #region AnimatorOverrideController
                            if (VAW.Animator.runtimeAnimatorController is AnimatorOverrideController owc)
                            {
                                {
                                    List<KeyValuePair<AnimationClip, AnimationClip>> srcList = new();
                                    owc.GetOverrides(srcList);
                                    List<KeyValuePair<AnimationClip, AnimationClip>> dstList = new();
                                    bool changed = false;
                                    foreach (var pair in srcList)
                                    {
                                        if (pair.Key == CurrentClip || pair.Value == CurrentClip)
                                            changed = true;
                                        dstList.Add(new KeyValuePair<AnimationClip, AnimationClip>(pair.Key != CurrentClip ? pair.Key : newClip,
                                                                                                    pair.Value != CurrentClip ? pair.Value : newClip));
                                    }
                                    if (changed)
                                    {
                                        owc.ApplyOverrides(dstList);
                                        replaced = true;
                                    }
                                }
                            }
                            #endregion
                            #region AnimatorControllerLayer
                            if (ac != null)
                            {
                                var layers = ac.layers;
                                for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
                                {
                                    if (animationMode == AnimationMode.Layers)
                                    {
                                        if (layerIndex != animationLayerIndex)
                                            continue;
                                    }

                                    void ReplaceStateMachine(AnimatorStateMachine stateMachine)
                                    {
                                        foreach (var state in stateMachine.states)
                                        {
                                            var motion = ac.GetStateEffectiveMotion(state.state, layerIndex);
                                            if (motion is UnityEditor.Animations.BlendTree)
                                            {
                                                Undo.RecordObject(state.state, "Duplicate and Replace");
                                                void ReplaceBlendTree(UnityEditor.Animations.BlendTree blendTree)
                                                {
                                                    if (blendTree.children == null) return;
                                                    Undo.RecordObject(blendTree, "Duplicate and Replace");
                                                    var children = blendTree.children;
                                                    for (int i = 0; i < children.Length; i++)
                                                    {
                                                        if (children[i].motion is UnityEditor.Animations.BlendTree)
                                                        {
                                                            ReplaceBlendTree(children[i].motion as UnityEditor.Animations.BlendTree);
                                                        }
                                                        else
                                                        {
                                                            if (children[i].motion == CurrentClip)
                                                            {
                                                                children[i].motion = newClip;
                                                                replaced = true;
                                                            }
                                                        }
                                                    }
                                                    blendTree.children = children;
                                                }

                                                ReplaceBlendTree(motion as UnityEditor.Animations.BlendTree);
                                            }
                                            else
                                            {
                                                if (motion == CurrentClip)
                                                {
                                                    ac.SetStateEffectiveMotion(state.state, newClip, layerIndex);
                                                    replaced = true;
                                                }
                                            }
                                        }
                                        foreach (var childStateMachine in stateMachine.stateMachines)
                                        {
                                            ReplaceStateMachine(childStateMachine.stateMachine);
                                        }
                                    }

                                    var stateMachine = UAnimatorController.FindEffectiveRootStateMachine(ac, layerIndex);
                                    ReplaceStateMachine(stateMachine);
                                }
                            }
                            #endregion
                        }
                        #endregion
                        #region Animation
                        if (VAW.Animation != null)
                        {
                            Undo.RecordObject(VAW.Animation, "Duplicate and Replace");
                            bool changed = false;
                            var animations = AnimationUtility.GetAnimationClips(VAW.GameObject);
                            for (int i = 0; i < animations.Length; i++)
                            {
                                if (animations[i] == CurrentClip)
                                {
                                    animations[i] = newClip;
                                    changed = true;
                                }
                            }
                            if (VAW.Animation.clip == CurrentClip)
                            {
                                VAW.Animation.clip = newClip;
                                changed = true;
                            }
                            if (changed)
                            {
                                AnimationUtility.SetAnimationClips(VAW.Animation, animations);
                                replaced = true;
                            }
                        }
                        #endregion

                        if (replaced)
                            SetCurrentClip(newClip);
                    }

                    if (!replaced)
                        Debug.LogWarningFormat(Language.GetText(Language.Help.LogAnimationClipReferenceReplaceError), newClip);

                    ClearEditorCurveCache();
                    OnHierarchyWindowChanged();
                    SetUpdateSampleAnimation(true, true);
                    UAw.ForceRefresh();
                }
            }
        }

        private void ToolsReset()
        {
            var lastFrame = GetLastFrame();
            var totalLastFrame = Mathf.RoundToInt(GetTotalClipLength() * CurrentClip.frameRate);
            toolCreateNewClip_FirstFrame = 0;
            toolCreateNewClip_LastFrame = totalLastFrame;
#if VERYANIMATION_TIMELINE
            if (UAw.GetLinkedWithTimeline())
            {
                var timelineClip = UAw.GetTimelineClip();
                if (timelineClip != null)
                {
                    var start = Mathf.RoundToInt((float)timelineClip.start * UAw.GetTimelineFrameRate());
                    var end = Mathf.RoundToInt((float)timelineClip.end * UAw.GetTimelineFrameRate());
                    toolCreateNewClip_FirstFrame = start;
                    toolCreateNewClip_LastFrame = end;
                }
            }
#endif
            toolCreateNewKeyframe_FirstFrame = 0;
            toolCreateNewKeyframe_LastFrame = lastFrame;
            toolBakeIK_FirstFrame = 0;
            toolBakeIK_LastFrame = lastFrame;
            toolHumanoidIK_FirstFrame = 0;
            toolHumanoidIK_LastFrame = lastFrame;
            toolAnimationRigging_FirstFrame = 0;
            toolAnimationRigging_LastFrame = totalLastFrame;
            toolCopy_FirstFrame = 0;
            toolCopy_LastFrame = lastFrame;
            toolCopy_WriteFrame = lastFrame + 1;
            toolTrim_FirstFrame = 0;
            toolTrim_LastFrame = lastFrame;
            if (CurrentClip != null)
            {
                var so = new SerializedObject(CurrentClip);
                {
                    var animationClipSettings = so.FindProperty("m_AnimationClipSettings");
                    var clip = animationClipSettings.FindPropertyRelative("m_AdditiveReferencePoseClip").objectReferenceValue;
                    toolAdditiveReferencePose_Clip = clip as AnimationClip;
                    toolAdditiveReferencePose_Time = animationClipSettings.FindPropertyRelative("m_AdditiveReferencePoseTime").floatValue;
                    toolAdditiveReferencePose_Has = animationClipSettings.FindPropertyRelative("m_HasAdditiveReferencePose").boolValue;
                }
                toolAnimCompression_Compressed = so.FindProperty("m_Compressed").boolValue;
                toolAnimCompression_UseHighQualityCurve = so.FindProperty("m_UseHighQualityCurve").boolValue;
            }

            ToolsParameterRelatedCurveReset();
        }
        private void ToolsParameterRelatedCurveReset()
        {
            toolParameterRelatedCurve_DataList = null;
            toolParameterRelatedCurve_Update = true;
            toolParameterRelatedCurve_List = null;
        }

        private void ParameterRelatedCurveUpdateList()
        {
            if (!toolParameterRelatedCurve_Update)
                return;

            void UpdateEnableFlagAll()
            {
                if (toolParameterRelatedCurve_DataList == null) return;
                var ac = AnimationCommon.GetAnimatorController(VAW.Animator);
                if (ac == null) return;
                var parameters = ac.parameters;
                foreach (var data in toolParameterRelatedCurve_DataList)
                {
                    var binding = AnimationCurveBindingAnimatorCustom(data.propertyName);
                    var curve = GetEditorCurveCache(binding);
                    data.enableAnimationCurve = curve != null;
                    data.parameterIndex = ArrayUtility.FindIndex(parameters, (x) => x.name == data.propertyName);
                    data.enableAnimatorParameter = data.parameterIndex >= 0 && parameters[data.parameterIndex].type == UnityEngine.AnimatorControllerParameterType.Float;
                }
                SetUpdateSampleAnimation();
                UAw.ForceRefresh();
            }
            void UpdateEnableFlag(int index)
            {
                if (toolParameterRelatedCurve_DataList == null) return;
                var ac = AnimationCommon.GetAnimatorController(VAW.Animator);
                if (ac == null) return;
                var parameters = ac.parameters;
                {
                    var data = toolParameterRelatedCurve_DataList[index];
                    var binding = AnimationCurveBindingAnimatorCustom(data.propertyName);
                    var curve = GetEditorCurveCache(binding);
                    data.enableAnimationCurve = curve != null;
                    data.parameterIndex = ArrayUtility.FindIndex(parameters, (x) => x.name == data.propertyName);
                    data.enableAnimatorParameter = data.parameterIndex >= 0 && parameters[data.parameterIndex].type == UnityEngine.AnimatorControllerParameterType.Float;
                }
                SetUpdateSampleAnimation();
                UAw.ForceRefresh();
            }

            toolParameterRelatedCurve_DataList ??= new List<ParameterRelatedData>();
            toolParameterRelatedCurve_DataList.Clear();
            {
                var ac = AnimationCommon.GetAnimatorController(VAW.Animator);
                if (ac != null)
                {
                    var parameters = ac.parameters;
                    foreach (var binding in AnimationUtility.GetCurveBindings(CurrentClip))
                    {
                        if (binding.type != typeof(Animator)) continue;
                        if (IsAnimatorReservedPropertyName(binding.propertyName)) continue;
                        bool ready = false;
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            if (parameters[i].type != UnityEngine.AnimatorControllerParameterType.Float) continue;
                            if (binding.propertyName == parameters[i].name)
                            {
                                toolParameterRelatedCurve_DataList.Add(new ParameterRelatedData()
                                {
                                    propertyName = binding.propertyName,
                                    parameterIndex = i,
                                    enableAnimationCurve = true,
                                    enableAnimatorParameter = true
                                });
                                ready = true;
                                break;
                            }
                        }
                        if (!ready)
                        {
                            toolParameterRelatedCurve_DataList.Add(new ParameterRelatedData()
                            {
                                propertyName = binding.propertyName,
                                parameterIndex = -1,
                                enableAnimationCurve = true,
                                enableAnimatorParameter = false,
                            });
                        }
                    }
                }
            }
            toolParameterRelatedCurve_List = new ReorderableList(toolParameterRelatedCurve_DataList, typeof(ParameterRelatedData), draggable: false, displayHeader: true, displayAddButton: true, displayRemoveButton: true)
            {
                drawHeaderCallback = rect =>
                {
                    float x = rect.x;
                    {
                        const float Rate = 0.4f;
                        var r = rect;
                        r.x = x;
                        r.width = rect.width * Rate;
                        x += r.width;
                        EditorGUI.LabelField(r, "Name", VAW.GuiStyleCenterAlignLabel);
                    }
                    {
                        const float Rate = 0.2f;
                        var r = rect;
                        r.x = x;
                        r.width = rect.width * Rate;
                        x += r.width;
                        EditorGUI.LabelField(r, Styles.guiContentCurve, VAW.GuiStyleCenterAlignLabel);
                    }
                    {
                        const float Rate = 0.2f;
                        var r = rect;
                        r.x = x;
                        r.width = rect.width * Rate;
                        x += r.width;
                        EditorGUI.LabelField(r, Styles.guiContentParameter, VAW.GuiStyleCenterAlignLabel);
                    }
                    {
                        const float Rate = 0.2f;
                        var r = rect;
                        r.x = x;
                        r.width = rect.width * Rate;
                        x += r.width;
                        EditorGUI.LabelField(r, "Value", VAW.GuiStyleCenterAlignLabel);
                    }
                }
            };
            toolParameterRelatedCurve_List.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                if (index >= toolParameterRelatedCurve_DataList.Count)
                    return;

                EditorGUI.BeginDisabledGroup((CurrentClip.hideFlags & HideFlags.NotEditable) != HideFlags.None);

                float x = rect.x;
                {
                    const float Rate = 0.4f;
                    var r = rect;
                    r.x = x;
                    r.y += 2;
                    r.height -= 4;
                    r.width = rect.width * Rate;
                    x += r.width;
                    if (index == toolParameterRelatedCurve_List.index)
                    {
                        EditorGUI.BeginChangeCheck();
                        var text = EditorGUI.TextField(r, toolParameterRelatedCurve_DataList[index].propertyName);
                        if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(text))
                        {
                            if (ToolsCommonBefore(CurrentClip, "Change Parameter Related Curve"))
                            {
                                var ac = AnimationCommon.GetAnimatorController(VAW.Animator);
                                if (ac != null)
                                {
                                    {
                                        var origText = text;
                                        if (IsAnimatorReservedPropertyName(text))
                                            text += " 0";
                                        text = ac.MakeUniqueParameterName(text);
                                        if (origText != text)
                                        {
                                            Debug.LogWarningFormat(Language.GetText(Language.Help.LogParameterRelatedCurveNameChanged), origText, text);
                                        }
                                    }
                                    Undo.RecordObject(VAW, "Change Parameter Related Curve");
                                    Undo.RecordObject(ac, "Change Parameter Related Curve");
                                    {
                                        var oldBinding = AnimationCurveBindingAnimatorCustom(toolParameterRelatedCurve_DataList[index].propertyName);
                                        var curve = AnimationUtility.GetEditorCurve(CurrentClip, oldBinding);
                                        if (curve != null)
                                        {
                                            var binding = AnimationCurveBindingAnimatorCustom(text);
                                            AnimationCommon.SetEditorCurves(CurrentClip, new Dictionary<EditorCurveBinding, AnimationCurve>(2)
                                            {
                                                [oldBinding] = null,
                                                [binding] = curve,
                                            });
                                            EditorApplication.delayCall += () =>
                                            {
                                                SetAnimationWindowSynchroSelection(new EditorCurveBinding[] { binding });
                                                VAW.Repaint();
                                            };
                                        }
                                    }
                                    {
                                        var parameters = ac.parameters;
                                        int paramIndex = toolParameterRelatedCurve_DataList[index].parameterIndex;
                                        if (paramIndex >= 0 && paramIndex < parameters.Length)
                                        {
                                            parameters[paramIndex].name = text;
                                            ac.parameters = parameters;
                                        }
                                    }
                                    toolParameterRelatedCurve_DataList[index].propertyName = text;
                                    UpdateEnableFlag(index);
                                }
                            }
                        }
                    }
                    else
                    {
                        EditorGUI.LabelField(r, toolParameterRelatedCurve_DataList[index].propertyName);
                    }
                }
                {
                    const float Rate = 0.2f;
                    var r = rect;
                    r.x = x;
                    r.y += 2;
                    r.height -= 4;
                    r.width = rect.width * Rate;
                    x += r.width;
                    if (toolParameterRelatedCurve_DataList[index].enableAnimationCurve)
                        EditorGUI.LabelField(r, "Ready", VAW.GuiStyleCenterAlignLabel);
                    else
                        EditorGUI.LabelField(r, "Missing", VAW.GuiStyleCenterAlignYellowLabel);
                }
                {
                    const float Rate = 0.2f;
                    var r = rect;
                    r.x = x;
                    r.y += 2;
                    r.height -= 4;
                    r.width = rect.width * Rate;
                    x += r.width;
                    if (toolParameterRelatedCurve_DataList[index].enableAnimatorParameter)
                        EditorGUI.LabelField(r, "Ready", VAW.GuiStyleCenterAlignLabel);
                    else
                        EditorGUI.LabelField(r, "Missing", VAW.GuiStyleCenterAlignYellowLabel);
                }
                {
                    const float Rate = 0.2f;
                    var r = rect;
                    r.x = x;
                    r.y += 2;
                    r.height -= 4;
                    r.width = rect.width * Rate;
                    x += r.width;
                    if (!toolParameterRelatedCurve_DataList[index].enableAnimationCurve || !toolParameterRelatedCurve_DataList[index].enableAnimatorParameter)
                    {
                        if (GUI.Button(r, "Fix"))
                        {
                            if (ToolsCommonBefore(CurrentClip, "Fix Parameter Related Curve"))
                            {
                                var ac = AnimationCommon.GetAnimatorController(VAW.Animator);
                                if (ac != null)
                                {
                                    Undo.RecordObject(VAW, "Fix Parameter Related Curve");
                                    Undo.RecordObject(ac, "Fix Parameter Related Curve");
                                    if (!toolParameterRelatedCurve_DataList[index].enableAnimationCurve)
                                    {
                                        var binding = AnimationCurveBindingAnimatorCustom(toolParameterRelatedCurve_DataList[index].propertyName);
                                        var curve = new AnimationCurve();
                                        AnimationCommon.SetKeyframeTangentModeLinear(curve, curve.AddKey(0f, 0f));
                                        AnimationCommon.SetKeyframeTangentModeLinear(curve, curve.AddKey(CurrentClip.length, 1f));
                                        AnimationCommon.SetEditorCurves(CurrentClip, new Dictionary<EditorCurveBinding, AnimationCurve>(1)
                                        {
                                            [binding] = curve,
                                        });
                                    }
                                    if (!toolParameterRelatedCurve_DataList[index].enableAnimatorParameter)
                                    {
                                        {
                                            var parameters = ac.parameters;
                                            var paramIndex = ArrayUtility.FindIndex(parameters, (d) => d.name == toolParameterRelatedCurve_DataList[index].propertyName);
                                            if (paramIndex >= 0 && paramIndex < parameters.Length)
                                            {
                                                ac.RemoveParameter(paramIndex);
                                            }
                                        }
                                        ac.AddParameter(toolParameterRelatedCurve_DataList[index].propertyName, UnityEngine.AnimatorControllerParameterType.Float);
                                    }
                                    UpdateEnableFlag(index);
                                }
                            }
                        }
                    }
                    else if (UAvatarPreview != null)
                    {
                        var binding = AnimationCurveBindingAnimatorCustom(toolParameterRelatedCurve_DataList[index].propertyName);
                        var curve = GetEditorCurveCache(binding);
                        if (curve != null)
                        {
                            var value = curve.Evaluate(UAvatarPreview.GetTime());
                            EditorGUI.LabelField(r, value.ToString("F2"), VAW.GuiStyleCenterAlignLabel);
                        }
                    }
                }

                EditorGUI.EndDisabledGroup();
            };
            toolParameterRelatedCurve_List.onSelectCallback = list =>
            {
                UpdateEnableFlagAll();
                EditorApplication.delayCall += () =>
                {
                    SetAnimationWindowSynchroSelection(new EditorCurveBinding[] { AnimationCurveBindingAnimatorCustom(toolParameterRelatedCurve_DataList[list.index].propertyName) });
                    VAW.Repaint();
                };
            };
            toolParameterRelatedCurve_List.onAddCallback = list =>
            {
                if (!ToolsCommonBefore(CurrentClip, "Add Parameter Related Curve")) return;

                var ac = AnimationCommon.GetAnimatorController(VAW.Animator);
                if (ac == null)
                    return;

                Undo.RecordObject(VAW, "Add Parameter Related Curve");
                {
                    var name = ac.MakeUniqueParameterName("New Parameter");
                    var data = new ParameterRelatedData() { propertyName = name };
                    {
                        var binding = AnimationCurveBindingAnimatorCustom(name);
                        var curve = new AnimationCurve();
                        AnimationCommon.SetKeyframeTangentModeLinear(curve, curve.AddKey(0f, 0f));
                        AnimationCommon.SetKeyframeTangentModeLinear(curve, curve.AddKey(CurrentClip.length, 1f));
                        AnimationCommon.SetEditorCurves(CurrentClip, new Dictionary<EditorCurveBinding, AnimationCurve>(1)
                        {
                            [binding] = curve,
                        });
                    }
                    {
                        ac.AddParameter(name, UnityEngine.AnimatorControllerParameterType.Float);
                    }
                    toolParameterRelatedCurve_DataList.Add(data);
                }
                toolParameterRelatedCurve_Update = true;
                EditorApplication.delayCall += () =>
                {
                    toolParameterRelatedCurve_List.index = toolParameterRelatedCurve_DataList.Count - 1;
                    SetAnimationWindowSynchroSelection(new EditorCurveBinding[] { AnimationCurveBindingAnimatorCustom(toolParameterRelatedCurve_DataList[toolParameterRelatedCurve_List.index].propertyName) });
                    VAW.Repaint();
                };

                ToolsCommonAfter();
                InternalEditorUtility.RepaintAllViews();
            };
            toolParameterRelatedCurve_List.onCanAddCallback = list =>
            {
                var ac = AnimationCommon.GetAnimatorController(VAW.Animator);
                return (ac != null);
            };
            toolParameterRelatedCurve_List.onRemoveCallback = list =>
            {
                if (!ToolsCommonBefore(CurrentClip, "Add Parameter Related Curve")) return;

                Undo.RecordObject(VAW, "Add Parameter Related Curve");
                {
                    var ac = AnimationCommon.GetAnimatorController(VAW.Animator);
                    if (ac != null)
                    {
                        var data = toolParameterRelatedCurve_DataList[list.index];
                        {
                            var binding = AnimationCurveBindingAnimatorCustom(data.propertyName);
                            AnimationCommon.SetEditorCurves(CurrentClip, new Dictionary<EditorCurveBinding, AnimationCurve>(1)
                            {
                                [binding] = null,
                            });
                        }
                        {
                            var parameters = ac.parameters;
                            data.parameterIndex = ArrayUtility.FindIndex(parameters, (x) => x.name == data.propertyName);
                            if (data.parameterIndex >= 0)
                                ac.RemoveParameter(data.parameterIndex);
                        }
                    }
                }
                toolParameterRelatedCurve_DataList.RemoveAt(list.index);
                toolParameterRelatedCurve_Update = true;

                ToolsCommonAfter();
                InternalEditorUtility.RepaintAllViews();
            };

            UpdateEnableFlagAll();
            toolParameterRelatedCurve_Update = false;
        }

        private void ToolsCurvesWasModifiedStoppedUpdateTangents(float beginTime, float endTime)
        {
            foreach (var pair in curvesWasModifiedStopped)
            {
                if (pair.Value.deleted != AnimationUtility.CurveModifiedType.CurveModified)
                    continue;
                var curve = GetEditorCurveCache(pair.Value.binding);
                if (curve == null)
                    continue;

                for (int i = 0; i < curve.length; i++)
                {
                    if (curve[i].time >= beginTime && curve[i].time <= endTime)
                        AnimationCommon.SetKeyframeTangentModeClampedAuto(curve, i);
                }
                SetEditorCurveCache(pair.Value.binding, curve);
            }
        }

        private bool ToolsCommonBefore(AnimationClip clip, string undoName)
        {
            if (!BeginChangeAnimationCurve(clip, undoName))
                return false;

            UAw.ClearKeySelections();
            ClearEditorCurveCache();
            SetOnCurveWasModifiedStop(true);

            return true;
        }
        private void ToolsCommonAfter()
        {
            ResetOnCurveWasModifiedStop();
            UpdateSyncEditorCurveClip();
            curvesWasModified?.Clear();
            ResetAnimatorRootCorrection();
            humanoidFootIK?.Clear();
            ClearEditorCurveCache();
            SetUpdateSampleAnimation(true, true);
            SetAnimationWindowSynchroSelection();
            ResetUpdateIKtargetAll();
            SetSynchroIKtargetAll();
            UAw.ForceRefresh();
        }

        private void ToolsReductionCurve(AnimationClip clip)
        {
            if (!ToolsCommonBefore(clip, "Reduction Curve")) return;

            try
            {
                bool allWriteDefaults = true;
                if (UAw.GetLinkedWithTimeline())
                {
                    allWriteDefaults = false;
                }
                else
                {
                    ActionAllAnimatorState(clip, (animatorState) =>
                    {
                        if (!animatorState.writeDefaultValues)
                            allWriteDefaults = false;
                    });
                }

                #region It is not necessary if AnimatorState.writeDefaultValues is enabled
                if (allWriteDefaults)
                {
                    const float eps = 0.0001f;
                    string[] TransformTypeNames =
                    {
                        "m_LocalPosition",
                        "m_LocalRotation",
                        "m_LocalScale",
                        "localEulerAnglesRaw",
                    };

                    var lastFrame = EditorCommon.GetLastFrame(clip.length, clip.frameRate);
                    var bindings = AnimationUtility.GetCurveBindings(clip);
                    int progressIndex = 0;
                    int progressTotal = bindings.Length;

                    bool[] doneFlags = new bool[bindings.Length];
                    for (int k = 0; k < bindings.Length; k++)
                    {
                        EditorUtility.DisplayProgressBar("Reduction Curve", AnimationCommon.GetBindingDisplayName(bindings[k]), progressIndex++ / (float)progressTotal);
                        if (doneFlags[k]) continue;
                        doneFlags[k] = true;
                        var curve = AnimationUtility.GetEditorCurve(clip, bindings[k]);
                        if (curve == null) continue;
                        var t = GetTransformFromPath(bindings[k].path);
                        if (t == null) continue;
                        if (bindings[k].type == typeof(Animator))
                        {
                            #region Animator
                            if (GetMuscleIndexFromCurveBinding(bindings[k]) >= 0)
                            {
                                bool remove = true;
                                for (int frame = 0; frame <= lastFrame; frame++)
                                {
                                    var time = EditorCommon.GetFrameTime(frame, clip.frameRate);
                                    if (Mathf.Abs(curve.Evaluate(time)) >= eps)
                                    {
                                        remove = false;
                                        break;
                                    }
                                }
                                if (remove)
                                {
                                    AnimationUtility.SetEditorCurve(clip, bindings[k], null);
                                }
                            }
                            #endregion
                        }
                        else if (bindings[k].type == typeof(Transform))
                        {
                            #region Transform
                            int type = -1;
                            for (int i = 0; i < TransformTypeNames.Length; i++)
                            {
                                if (bindings[k].propertyName.StartsWith(TransformTypeNames[i], StringComparison.Ordinal))
                                {
                                    type = i;
                                    break;
                                }
                            }
                            var boneIndex = GetBoneIndexFromCurveBinding(bindings[k]);
                            if (type >= 0 && boneIndex >= 0)
                            {
                                var save = TransformPoseSave.GetOriginalTransform(Bones[boneIndex].transform);
                                if (save != null)
                                {
                                    int dofCount = type == 1 ? 4 : 3;
                                    bool remove = true;
                                    int[] indexes = new int[dofCount];
                                    for (int dof = 0; dof < dofCount; dof++)
                                    {
                                        indexes[dof] = ArrayUtility.FindIndex(bindings, (x) => x.type == bindings[k].type && x.path == bindings[k].path &&
                                                                                                x.propertyName == TransformTypeNames[type] + AnimationCommon.PropertyName.DotDof[dof]);
                                        if (indexes[dof] >= 0)
                                            doneFlags[indexes[dof]] = true;
                                        if (remove && indexes[dof] >= 0)
                                        {
                                            curve = AnimationUtility.GetEditorCurve(clip, bindings[indexes[dof]]);
                                            if (curve != null)
                                            {
                                                float saveValue = 0f;
                                                switch (type)
                                                {
                                                    case 0: saveValue = save.localPosition[dof]; break;
                                                    case 1: saveValue = save.localRotation[dof]; break;
                                                    case 2: saveValue = save.localScale[dof]; break;
                                                    case 3: saveValue = save.localRotation.eulerAngles[dof]; break;
                                                }
                                                for (int frame = 0; frame <= lastFrame; frame++)
                                                {
                                                    var time = EditorCommon.GetFrameTime(frame, clip.frameRate);
                                                    if (Mathf.Abs(curve.Evaluate(time) - saveValue) >= eps)
                                                    {
                                                        remove = false;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    if (remove)
                                    {
                                        foreach (var index in indexes)
                                        {
                                            if (index >= 0)
                                                AnimationUtility.SetEditorCurve(clip, bindings[index], null);
                                        }
                                    }
                                }
                            }
                            #endregion
                        }
                        else if (bindings[k].type == typeof(SkinnedMeshRenderer))
                        {
                            #region SkinnedMeshRenderer
                            var renderer = t.GetComponent<SkinnedMeshRenderer>();
                            if (renderer != null && renderer.sharedMesh != null && renderer.sharedMesh.blendShapeCount > 0)
                            {
                                if (AnimationCommon.IsBlendShapePropertyName(bindings[k].propertyName))
                                {
                                    var name = AnimationCommon.PropertyName2BlendShapeName(bindings[k].propertyName);
                                    if (BlendShapeWeightSave.IsHaveOriginalWeight(renderer, name))
                                    {
                                        var weight = BlendShapeWeightSave.GetOriginalWeight(renderer, name);
                                        bool remove = true;
                                        for (int frame = 0; frame <= lastFrame; frame++)
                                        {
                                            var time = EditorCommon.GetFrameTime(frame, clip.frameRate);
                                            if (Mathf.Abs(curve.Evaluate(time) - weight) >= eps)
                                            {
                                                remove = false;
                                                break;
                                            }
                                        }
                                        if (remove)
                                        {
                                            AnimationUtility.SetEditorCurve(clip, bindings[k], null);
                                        }
                                    }
                                }
                            }
                            #endregion
                        }
                    }
                }
                #endregion

                #region Optional bone
                {
                    void RemoveMuscleCurve(HumanBodyBones hi)
                    {
                        for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                        {
                            var mi = HumanTrait.MuscleFromBone((int)hi, dofIndex);
                            if (mi < 0) continue;
                            AnimationUtility.SetEditorCurve(clip, AnimatorMuscleBindings[mi], null);
                        }
                    }
                    if (!IsHuman || !HumanoidHasLeftHand)
                    {
                        for (var hi = HumanBodyBones.LeftThumbProximal; hi <= HumanBodyBones.LeftLittleDistal; hi++)
                            RemoveMuscleCurve(hi);
                    }
                    if (!IsHuman || !HumanoidHasRightHand)
                    {
                        for (var hi = HumanBodyBones.RightThumbProximal; hi <= HumanBodyBones.RightLittleDistal; hi++)
                            RemoveMuscleCurve(hi);
                    }
                    if (!IsHuman || !HumanoidHasTDoF)
                    {
                        for (int tdofIndex = 0; tdofIndex < (int)AnimatorTDOFIndex.Total; tdofIndex++)
                        {
                            for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                                AnimationUtility.SetEditorCurve(clip, AnimatorTDOFBindings[(int)tdofIndex][dofIndex], null);
                        }
                    }
                    void RemoveNoneMuscleCurve(HumanBodyBones hi)
                    {
                        if (!IsHuman || HumanoidBones[(int)hi] == null)
                            RemoveMuscleCurve(hi);
                    }
                    RemoveNoneMuscleCurve(HumanBodyBones.LeftEye);
                    RemoveNoneMuscleCurve(HumanBodyBones.RightEye);
                    RemoveNoneMuscleCurve(HumanBodyBones.Jaw);
                    RemoveNoneMuscleCurve(HumanBodyBones.LeftToes);
                    RemoveNoneMuscleCurve(HumanBodyBones.RightToes);
                }
                #endregion

                #region GenericRootMotion
                if (!IsHuman)
                {
                    var removeCurves = new Dictionary<EditorCurveBinding, AnimationCurve>();
                    if (RootMotionBoneIndex >= 0)
                    {
                        if (IsHaveAnimationCurveTransformPosition(0))
                        {
                            for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                                removeCurves[AnimationCurveBindingTransformPosition(0, dofIndex)] = null;
                        }
                        if (GetHaveAnimationCurveTransformRotationMode(0) != URotationCurveInterpolation.Mode.Undefined)
                        {
                            for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                                removeCurves[AnimationCurveBindingTransformRotation(0, dofIndex, URotationCurveInterpolation.Mode.RawEuler)] = null;
                            for (int dofIndex = 0; dofIndex < 4; dofIndex++)
                                removeCurves[AnimationCurveBindingTransformRotation(0, dofIndex, URotationCurveInterpolation.Mode.RawQuaternions)] = null;
                        }
                    }
                    else
                    {
                        if (IsHaveAnimationCurveAnimatorRootT())
                        {
                            for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                                removeCurves[AnimationCommon.Binding.RootT[dofIndex]] = null;
                        }
                        if (IsHaveAnimationCurveAnimatorRootQ())
                        {
                            for (int dofIndex = 0; dofIndex < 4; dofIndex++)
                                removeCurves[AnimationCommon.Binding.RootQ[dofIndex]] = null;
                        }
                    }
                    AnimationCommon.SetEditorCurves(clip, removeCurves);
                }
                #endregion
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ToolsCommonAfter();
        }
        private bool ToolsFixOverRotationCurve(AnimationClip clip)
        {
            if (!ToolsCommonBefore(clip, "Fix Over Rotation Curve")) return false;

            try
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                int progressIndex = 0;
                int progressTotal = bindings.Length;
                #region CurveBindings
                foreach (var binding in bindings)
                {
                    EditorUtility.DisplayProgressBar("Fix Over Rotation Curve", AnimationCommon.GetBindingDisplayName(binding), progressIndex++ / (float)progressTotal);
                    if (!IsTransformRotationCurveBinding(binding) || URotationCurveInterpolation.GetModeFromCurveData(binding) != URotationCurveInterpolation.Mode.RawEuler) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null) continue;
                    bool update = false;
                    for (int i = 1; i < curve.length; i++)
                    {
                        var power = curve[i].value - curve[i - 1].value;
                        if (Mathf.Abs(power) < 180f) continue;
                        var time = EditorCommon.SnapToFrame(Mathf.Lerp(curve[i].time, curve[i - 1].time, 0.5f), clip.frameRate);
                        if (Mathf.Approximately(time, curve[i].time) || Mathf.Approximately(time, curve[i - 1].time)) continue;
                        AnimationCommon.AddKeyframe(curve, time, curve.Evaluate(time));
                        update = true;
                        i = 0;
                    }
                    if (update)
                    {
                        AnimationUtility.SetEditorCurve(clip, binding, curve);
                        Debug.LogWarningFormat(Language.GetText(Language.Help.LogFixOverRotationCurve), binding.path, binding.propertyName);
                    }
                }
                #endregion
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ToolsCommonAfter();

            return true;
        }

        private struct IKDataSave
        {
            public IKDataSave(AnimatorIKCore.AnimatorIKData ikData)
            {
                position = ikData.position;
                rotation = ikData.rotation;
                worldPosition = ikData.WorldPosition;
                worldRotation = ikData.WorldRotation;
                swivelRotation = ikData.swivelRotation;
                swivelPosition = ikData.swivelPosition;
            }
            public IKDataSave(OriginalIKCore.OriginalIKData ikData)
            {
                position = ikData.position;
                rotation = ikData.rotation;
                worldPosition = ikData.WorldPosition;
                worldRotation = ikData.WorldRotation;
                swivelRotation = ikData.swivel;
                swivelPosition = Vector3.zero;
            }
            public readonly void Set(AnimatorIKCore.AnimatorIKData ikData)
            {
                if (ikData.spaceType == AnimatorIKCore.AnimatorIKData.SpaceType.Parent)
                {
                    ikData.position = position;
                    ikData.rotation = rotation;
                }
                else
                {
                    ikData.WorldPosition = worldPosition;
                    ikData.WorldRotation = worldRotation;
                }
                ikData.swivelRotation = swivelRotation;
                ikData.swivelPosition = swivelPosition;
            }
            public readonly void Set(OriginalIKCore.OriginalIKData ikData)
            {
                if (ikData.spaceType == OriginalIKCore.OriginalIKData.SpaceType.Parent)
                {
                    ikData.position = position;
                    ikData.rotation = rotation;
                }
                else
                {
                    ikData.WorldPosition = worldPosition;
                    ikData.WorldRotation = worldRotation;
                }
                ikData.swivel = swivelRotation;
            }
            public void OverWrite(IKDataSave source, bool[] positionFlags, bool rotationFlag)
            {
                {
                    var tmpPosition = position;
                    var tmpWorldPosition = worldPosition;
                    var tmpSwivelPosition = swivelPosition;
                    for (int i = 0; i < 3; i++)
                    {
                        if (positionFlags[i])
                            continue;
                        tmpPosition[i] = source.position[i];
                        tmpWorldPosition[i] = source.worldPosition[i];
                        tmpSwivelPosition[i] = source.swivelPosition[i];
                    }
                    position = tmpPosition;
                    worldPosition = tmpWorldPosition;
                    swivelPosition = tmpSwivelPosition;
                }
                {
                    var tmpRotation = rotation;
                    var tmpWorldRotation = worldRotation;
                    var tmpSwivelRotation = swivelRotation;
                    if (!rotationFlag)
                    {
                        tmpRotation = source.rotation;
                        tmpWorldRotation = source.worldRotation;
                        tmpSwivelRotation = source.swivelRotation;
                    }
                    rotation = tmpRotation;
                    worldRotation = tmpWorldRotation;
                    swivelRotation = tmpSwivelRotation;
                }
            }

            public static IKDataSave Lerp(in IKDataSave a, in IKDataSave b, float t)
            {
                var ikDataSave = new IKDataSave()
                {
                    position = Vector3.Lerp(a.position, b.position, t),
                    rotation = Quaternion.Slerp(a.rotation, b.rotation, t),
                    worldPosition = Vector3.Lerp(a.worldPosition, b.worldPosition, t),
                    worldRotation = Quaternion.Slerp(a.worldRotation, b.worldRotation, t),
                    swivelPosition = Vector3.Lerp(a.swivelPosition, b.swivelPosition, t),
                };
                if (Mathf.Abs(a.swivelRotation - b.swivelRotation) > 180f)
                {
                    var aSwivel = a.swivelRotation;
                    if (aSwivel < 0f) aSwivel += 360f;
                    var bSwivel = b.swivelRotation;
                    if (bSwivel < 0f) bSwivel += 360f;
                    ikDataSave.swivelRotation = Mathf.Lerp(aSwivel, bSwivel, t);
                    while (ikDataSave.swivelRotation < -180f || ikDataSave.swivelRotation > 180f)
                    {
                        if (ikDataSave.swivelRotation > 180f)
                            ikDataSave.swivelRotation -= 360f;
                        else if (ikDataSave.swivelRotation < -180f)
                            ikDataSave.swivelRotation += 360f;
                    }
                }
                else
                {
                    ikDataSave.swivelRotation = Mathf.Lerp(a.swivelRotation, b.swivelRotation, t);
                }
                return ikDataSave;
            }

            public Vector3 position;
            public Quaternion rotation;
            public Vector3 worldPosition;
            public Quaternion worldRotation;
            public float swivelRotation;
            public Vector3 swivelPosition;
        }
        private void ToolsGenerateBakeIK(AnimationClip clip)
        {
            const string MenuTitle = "Bake IK";

            if (!ToolsCommonBefore(clip, MenuTitle)) return;

            Assert.IsTrue(clip == CurrentClip);

            var firstFrame = Mathf.Clamp(toolBakeIK_FirstFrame, 0, GetLastFrame());
            var lastFrame = Mathf.Clamp(toolBakeIK_LastFrame, firstFrame, GetLastFrame());

            var saveCurrentTime = UAw.GetCurrentTime();
            try
            {
                var beginTime = EditorCommon.SnapToFrame(firstFrame / clip.frameRate, clip.frameRate);
                var endTime = EditorCommon.SnapToFrame(lastFrame / clip.frameRate, clip.frameRate);

                #region AnimatorIK
                if (IsHuman && animatorIK.ikData != null && animatorIK.ikData.Any(data => data.enable))
                {
                    #region Save
                    IKDataSave[,] ikDataAllSave = null;
                    IKDataSave[] ikDataBeginSave = null, ikDataEndSave = null;
                    if (toolBakeIK_Mode == BakeIKMode.Simple)
                    {
                        ikDataAllSave = new IKDataSave[animatorIK.ikData.Length, lastFrame + 1];
                        for (int frame = firstFrame; frame <= lastFrame; frame++)
                        {
                            EditorUtility.DisplayProgressBar(MenuTitle, $"Animator IK {frame} / {lastFrame}", (frame - firstFrame) / (float)Mathf.Max(1, lastFrame - firstFrame));

                            var time = UAw.GetFrameTime(frame, clip);
                            SetCurrentTimeAndSampleAnimation(time);
                            for (var index = 0; index < animatorIK.ikData.Length; index++)
                            {
                                if (!animatorIK.ikData[index].enable) continue;
                                animatorIK.SynchroSet((AnimatorIKCore.IKTarget)index);
                                ikDataAllSave[index, frame] = new IKDataSave(animatorIK.ikData[index]);
                            }
                        }
                    }
                    else if (toolBakeIK_Mode == BakeIKMode.Interpolation)
                    {
                        ikDataBeginSave = new IKDataSave[animatorIK.ikData.Length];
                        ikDataEndSave = new IKDataSave[animatorIK.ikData.Length];
                        SetCurrentTimeAndSampleAnimation(beginTime);
                        for (var index = 0; index < animatorIK.ikData.Length; index++)
                        {
                            if (!animatorIK.ikData[index].enable) continue;
                            animatorIK.SynchroSet((AnimatorIKCore.IKTarget)index);
                            ikDataBeginSave[index] = new IKDataSave(animatorIK.ikData[index]);
                        }
                        SetCurrentTimeAndSampleAnimation(endTime);
                        for (var index = 0; index < animatorIK.ikData.Length; index++)
                        {
                            if (!animatorIK.ikData[index].enable) continue;
                            animatorIK.SynchroSet((AnimatorIKCore.IKTarget)index);
                            ikDataEndSave[index] = new IKDataSave(animatorIK.ikData[index]);
                        }
                    }
                    #endregion

                    ResetAnimatorRootCorrection();
                    for (int frame = firstFrame; frame <= lastFrame; frame++)
                    {
                        EditorUtility.DisplayProgressBar(MenuTitle, $"Animator IK {frame} / {lastFrame}", (frame - firstFrame) / (float)Mathf.Max(1, lastFrame - firstFrame));

                        var time = UAw.GetFrameTime(frame, clip);

                        SetCurrentTimeAndSampleAnimation(time);

                        if (toolBakeIK_Mode == BakeIKMode.Simple)
                        {
                            #region Simple
                            for (var index = 0; index < animatorIK.ikData.Length; index++)
                            {
                                if (!animatorIK.ikData[index].enable) continue;
                                ikDataAllSave[index, frame].Set(animatorIK.ikData[index]);
                                SetUpdateIKtargetAnimatorIK((AnimatorIKCore.IKTarget)index);
                            }
                            #endregion
                        }
                        else if (toolBakeIK_Mode == BakeIKMode.Interpolation)
                        {
                            #region Interpolation
                            float rate = 0f;
                            if (lastFrame - firstFrame > 0)
                                rate = (frame - firstFrame) / (float)(lastFrame - firstFrame);

                            for (var index = 0; index < animatorIK.ikData.Length; index++)
                            {
                                if (!animatorIK.ikData[index].enable) continue;
                                var ikDataSave = IKDataSave.Lerp(in ikDataBeginSave[index], in ikDataEndSave[index], rate);
                                ikDataSave.Set(animatorIK.ikData[index]);
                                SetUpdateIKtargetAnimatorIK((AnimatorIKCore.IKTarget)index);
                            }
                            #endregion
                        }

                        EnableAnimatorRootCorrection(time, time, time);
                        UpdateAnimatorRootCorrection();
                        animatorIK.UpdateIK(false);
                        ResetAnimatorRootCorrection();
                        ResetUpdateIKtargetAll();
                    }
                    for (int frame = firstFrame; frame <= lastFrame; frame++)
                    {
                        var time = UAw.GetFrameTime(frame, clip);
                        EnableAnimatorRootCorrection(time, time, time);
                        AddHumanoidFootIK(time);
                    }
                    UpdateAnimatorRootCorrection();
                    UpdateHumanoidFootIK();
                }
                #endregion
                #region OriginalIK
                if (originalIK.ikData != null && originalIK.ikData.Any(data => data.enable))
                {
                    #region Save
                    IKDataSave[,] ikDataAllSave = null;
                    IKDataSave[] ikDataBeginSave = null, ikDataEndSave = null;
                    if (toolBakeIK_Mode == BakeIKMode.Simple)
                    {
                        ikDataAllSave = new IKDataSave[originalIK.ikData.Count, lastFrame + 1];
                        for (int frame = firstFrame; frame <= lastFrame; frame++)
                        {
                            EditorUtility.DisplayProgressBar(MenuTitle, $"Original IK {frame} / {lastFrame}", (frame - firstFrame) / (float)Mathf.Max(1, lastFrame - firstFrame));

                            var time = UAw.GetFrameTime(frame, clip);
                            SetCurrentTimeAndSampleAnimation(time);
                            for (var index = 0; index < originalIK.ikData.Count; index++)
                            {
                                if (!originalIK.ikData[index].enable) continue;
                                originalIK.SynchroSet(index);
                                ikDataAllSave[index, frame] = new IKDataSave(originalIK.ikData[index]);
                            }
                        }
                    }
                    else if (toolBakeIK_Mode == BakeIKMode.Interpolation)
                    {
                        ikDataBeginSave = new IKDataSave[originalIK.ikData.Count];
                        ikDataEndSave = new IKDataSave[originalIK.ikData.Count];
                        SetCurrentTimeAndSampleAnimation(beginTime);
                        for (var index = 0; index < originalIK.ikData.Count; index++)
                        {
                            if (!originalIK.ikData[index].enable) continue;
                            originalIK.SynchroSet(index);
                            ikDataBeginSave[index] = new IKDataSave(originalIK.ikData[index]);
                        }
                        SetCurrentTimeAndSampleAnimation(endTime);
                        for (var index = 0; index < originalIK.ikData.Count; index++)
                        {
                            if (!originalIK.ikData[index].enable) continue;
                            originalIK.SynchroSet(index);
                            ikDataEndSave[index] = new IKDataSave(originalIK.ikData[index]);
                        }
                    }
                    #endregion

                    for (int frame = firstFrame; frame <= lastFrame; frame++)
                    {
                        EditorUtility.DisplayProgressBar(MenuTitle, $"Original IK {frame} / {lastFrame}", (frame - firstFrame) / (float)Mathf.Max(1, lastFrame - firstFrame));

                        var time = UAw.GetFrameTime(frame, clip);

                        SetCurrentTimeAndSampleAnimation(time);

                        if (toolBakeIK_Mode == BakeIKMode.Simple)
                        {
                            #region Simple
                            for (var index = 0; index < originalIK.ikData.Count; index++)
                            {
                                if (!originalIK.ikData[index].enable) continue;
                                ikDataAllSave[index, frame].Set(originalIK.ikData[index]);
                                SetUpdateIKtargetOriginalIK(index);
                            }
                            #endregion
                        }
                        else if (toolBakeIK_Mode == BakeIKMode.Interpolation)
                        {
                            #region Interpolation
                            float rate = 0f;
                            if (lastFrame - firstFrame > 0)
                                rate = (frame - firstFrame) / (float)(lastFrame - firstFrame);

                            for (var index = 0; index < originalIK.ikData.Count; index++)
                            {
                                if (!originalIK.ikData[index].enable) continue;
                                var ikDataSave = IKDataSave.Lerp(in ikDataBeginSave[index], in ikDataEndSave[index], rate);
                                ikDataSave.Set(originalIK.ikData[index]);
                                SetUpdateIKtargetOriginalIK(index);
                            }
                            #endregion
                        }

                        originalIK.UpdateIK();
                        ResetUpdateIKtargetAll();
                    }
                }
                #endregion

                ToolsCurvesWasModifiedStoppedUpdateTangents(beginTime, endTime);
            }
            finally
            {
                SetCurrentTime(saveCurrentTime);
                EditorUtility.ClearProgressBar();
            }

            ToolsCommonAfter();
        }
        private void ToolsGenerateAnimationRigging(AnimationClip clip)
        {
#if VERYANIMATION_ANIMATIONRIGGING
            if (!ToolsCommonBefore(clip, "AnimationRigging")) return;

            Assert.IsTrue(clip == CurrentClip);

            List<EditorCurveBinding> afterSyncBindings = new();

            var saveTime = CurrentTime;
            var saveRigLayerActive = VAW.VA.AnimationRigging.RigLayer.active;
            try
            {
                VAW.VA.AnimationRigging.RigLayer.active = false;

                SetCurrentTime(UAw.GetFrameTime(toolAnimationRigging_FirstFrame, clip));
                var firstTime = UAw.GetFrameTime(toolAnimationRigging_FirstFrame, clip);
                var lastTime = UAw.GetFrameTime(toolAnimationRigging_LastFrame, clip);

                if (toolAnimationRigging_RootMotionCancel)
                {
                    var root = VAW.GameObject.transform;

                    SetCurrentTimeAndSampleAnimation(0f);
                    root.GetPositionAndRotation(out var zeroPosition, out var zeroRotation);
                    for (int frame = toolAnimationRigging_FirstFrame; frame <= toolAnimationRigging_LastFrame; frame++)
                    {
                        EditorUtility.DisplayProgressBar("Generate AnimationRigging Curves", frame.ToString(), (frame - toolAnimationRigging_FirstFrame) / (float)Mathf.Max(1, toolAnimationRigging_LastFrame - toolAnimationRigging_FirstFrame));

                        var time = UAw.GetFrameTime(frame, clip);
                        SetCurrentTimeAndSampleAnimation(time);
                        var position = AnimationRigging.ArRig.transform.parent.worldToLocalMatrix.MultiplyPoint3x4(zeroPosition);
                        var rotation = Quaternion.Inverse(AnimationRigging.ArRig.transform.parent.rotation) * zeroRotation;

                        for (var target = 0; target < animatorIK.ikData.Length; target++)
                        {
                            var data = animatorIK.ikData[target];
                            if (!data.enable || data.rigConstraint == null)
                                continue;

                            var boneIndex = BonesIndexOf(animatorIK.ikData[target].rigConstraint.gameObject);
                            SetAnimationValueTransformPosition(boneIndex, position, time);
                            SetAnimationValueTransformRotation(boneIndex, rotation, time);
                        }
                    }
                    for (var target = 0; target < animatorIK.ikData.Length; target++)
                    {
                        var data = animatorIK.ikData[target];
                        if (!data.enable || data.rigConstraint == null)
                            continue;

                        var boneIndex = BonesIndexOf(animatorIK.ikData[target].rigConstraint.gameObject);
                        for (int dof = 0; dof < 3; dof++)
                            afterSyncBindings.Add(AnimationCurveBindingTransformPosition(boneIndex, dof));
                        for (int dof = 0; dof < 4; dof++)
                            afterSyncBindings.Add(AnimationCurveBindingTransformRotation(boneIndex, dof, URotationCurveInterpolation.Mode.RawQuaternions));
                        for (int dof = 0; dof < 3; dof++)
                            afterSyncBindings.Add(AnimationCurveBindingTransformRotation(boneIndex, dof, URotationCurveInterpolation.Mode.RawEuler));
                    }
                }

                for (int frame = toolAnimationRigging_FirstFrame; frame <= toolAnimationRigging_LastFrame; frame++)
                {
                    EditorUtility.DisplayProgressBar("Generate AnimationRigging Curves", frame.ToString(), (frame - toolAnimationRigging_FirstFrame) / (float)Mathf.Max(1, toolAnimationRigging_LastFrame - toolAnimationRigging_FirstFrame));

                    var time = UAw.GetFrameTime(frame, clip);
                    SetCurrentTimeAndSampleAnimation(time);

                    for (var target = 0; target < animatorIK.ikData.Length; target++)
                    {
                        var data = animatorIK.ikData[target];
                        if (!data.enable || data.rigConstraint == null)
                            continue;

                        var syncFlags = AnimatorIKCore.SynchroSetFlags.None;
                        {
                            if (data.defaultSyncType == AnimatorIKCore.AnimatorIKData.SyncType.SceneObject)
                            {
                                syncFlags |= AnimatorIKCore.SynchroSetFlags.SceneObject;
                            }
                            if (data.defaultSyncType == AnimatorIKCore.AnimatorIKData.SyncType.HumanoidIK)
                            {
                                syncFlags |= AnimatorIKCore.SynchroSetFlags.HumanoidIK;
                            }
                        }
                        animatorIK.SynchroSet((AnimatorIKCore.IKTarget)target, syncFlags);

                        animatorIK.WriteAnimationRiggingConstraint((AnimatorIKCore.IKTarget)target, time);
                    }
                }
                for (var target = 0; target < animatorIK.ikData.Length; target++)
                {
                    var data = animatorIK.ikData[target];
                    if (!data.enable || data.rigConstraint == null)
                        continue;

                    var bindings = animatorIK.GetAnimationRiggingConstraintBindings((AnimatorIKCore.IKTarget)target);
                    afterSyncBindings.AddRange(bindings);
                }

                ToolsCurvesWasModifiedStoppedUpdateTangents(UAw.GetFrameTime(toolAnimationRigging_FirstFrame, clip), UAw.GetFrameTime(toolAnimationRigging_LastFrame, clip));

                if (toolAnimationRigging_ChangeRigWeight)
                {
                    var path = GetGameObjectPath(AnimationRigging.ArRig.gameObject);
                    EditorCurveBinding binding = EditorCurveBinding.FloatCurve(path, AnimationRigging.ArRig.GetType(), "m_Weight");
                    var curve = GetAnimationCurveCustomProperty(binding);
                    var firstIndex = AnimationCommon.SetKeyframe(curve, firstTime, toolAnimationRigging_RigWeight);
                    var lastIndex = AnimationCommon.SetKeyframe(curve, lastTime, toolAnimationRigging_RigWeight);
                    AnimationCommon.SetKeyframeTangentFlat(curve, firstIndex);
                    AnimationCommon.SetKeyframeTangentFlat(curve, lastIndex);
                    SetAnimationCurveCustomProperty(binding, curve);
                    afterSyncBindings.Add(binding);
                }

                if (toolAnimationRigging_ChangeConstraintWeight)
                {
                    for (var target = 0; target < animatorIK.ikData.Length; target++)
                    {
                        var data = animatorIK.ikData[target];
                        if (!data.enable || data.rigConstraint == null)
                            continue;

                        var path = GetGameObjectPath(data.rigConstraint.gameObject);
                        EditorCurveBinding binding = EditorCurveBinding.FloatCurve(path, data.rigConstraint.GetType(), "m_Weight");
                        var curve = GetAnimationCurveCustomProperty(binding);
                        var firstIndex = AnimationCommon.SetKeyframe(curve, firstTime, toolAnimationRigging_ConstraintWeight);
                        var lastIndex = AnimationCommon.SetKeyframe(curve, lastTime, toolAnimationRigging_ConstraintWeight);
                        AnimationCommon.SetKeyframeTangentFlat(curve, firstIndex);
                        AnimationCommon.SetKeyframeTangentFlat(curve, lastIndex);
                        SetAnimationCurveCustomProperty(binding, curve);
                        afterSyncBindings.Add(binding);
                    }
                }
            }
            finally
            {
                VAW.VA.AnimationRigging.RigLayer.active = saveRigLayerActive;
                SetCurrentTime(saveTime);
                EditorUtility.ClearProgressBar();
            }

            EditorApplication.delayCall += () =>
            {
                SetAnimationWindowSynchroSelection(afterSyncBindings);
            };

            ToolsCommonAfter();
#endif
        }
        private void ToolsClearAnimationRigging(AnimationClip clip)
        {
#if VERYANIMATION_ANIMATIONRIGGING
            if (!ToolsCommonBefore(clip, "Clear AnimationRigging")) return;

            {
                var beginTime = EditorCommon.SnapToFrame(toolAnimationRigging_FirstFrame >= 0 ? toolAnimationRigging_FirstFrame / clip.frameRate : 0f, clip.frameRate);
                var endTime = EditorCommon.SnapToFrame(toolAnimationRigging_LastFrame >= 0 ? toolAnimationRigging_LastFrame / clip.frameRate : clip.length, clip.frameRate);
                float halfFrameTime = EditorCommon.GetHalfFrameTime(clip.frameRate);

                void Clear(EditorCurveBinding binding)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null) return;
                    for (int i = curve.length - 1; i >= 0; i--)
                    {
                        if (curve[i].time >= beginTime - halfFrameTime && curve[i].time <= endTime + halfFrameTime)
                        {
                            curve.RemoveKey(i);
                        }
                    }
                    AnimationUtility.SetEditorCurve(clip, binding, curve.length > 0 ? curve : null);
                }

                if (toolAnimationRigging_ChangeRigWeight)
                {
                    var path = GetGameObjectPath(AnimationRigging.ArRig.gameObject);
                    EditorCurveBinding binding = EditorCurveBinding.FloatCurve(path, AnimationRigging.ArRig.GetType(), "m_Weight");
                    Clear(binding);
                }

                if (toolAnimationRigging_RootMotionCancel)
                {
                    for (var target = 0; target < animatorIK.ikData.Length; target++)
                    {
                        var data = animatorIK.ikData[target];
                        if (!data.enable || data.rigConstraint == null)
                            continue;

                        var boneIndex = BonesIndexOf(data.rigConstraint.gameObject);
                        for (int dof = 0; dof < 3; dof++)
                            Clear(AnimationCurveBindingTransformPosition(boneIndex, dof));
                        for (int dof = 0; dof < 4; dof++)
                            Clear(AnimationCurveBindingTransformRotation(boneIndex, dof, URotationCurveInterpolation.Mode.RawQuaternions));
                        for (int dof = 0; dof < 3; dof++)
                            Clear(AnimationCurveBindingTransformRotation(boneIndex, dof, URotationCurveInterpolation.Mode.RawEuler));
                    }
                }
                if (toolAnimationRigging_ChangeConstraintWeight)
                {
                    for (var target = 0; target < animatorIK.ikData.Length; target++)
                    {
                        var data = animatorIK.ikData[target];
                        if (!data.enable || data.rigConstraint == null)
                            continue;

                        var path = GetGameObjectPath(data.rigConstraint.gameObject);
                        EditorCurveBinding binding = EditorCurveBinding.FloatCurve(path, data.rigConstraint.GetType(), "m_Weight");
                        Clear(binding);
                    }
                }

                for (var target = 0; target < animatorIK.ikData.Length; target++)
                {
                    var data = animatorIK.ikData[target];
                    if (!data.enable || data.rigConstraint == null)
                        continue;

                    var bindings = animatorIK.GetAnimationRiggingConstraintBindings((AnimatorIKCore.IKTarget)target);
                    foreach (var binding in bindings)
                    {
                        Clear(binding);
                    }
                }
            }

            ToolsCommonAfter();
#endif
        }
        private void ToolsClearHumanoidIK(AnimationClip clip)
        {
            if (!ToolsCommonBefore(clip, "Clear IK Keyframe")) return;

            {
                var beginTime = EditorCommon.SnapToFrame(toolHumanoidIK_FirstFrame >= 0 ? toolHumanoidIK_FirstFrame / clip.frameRate : 0f, clip.frameRate);
                var endTime = EditorCommon.SnapToFrame(toolHumanoidIK_LastFrame >= 0 ? toolHumanoidIK_LastFrame / clip.frameRate : clip.length, clip.frameRate);
                float halfFrameTime = EditorCommon.GetHalfFrameTime(clip.frameRate);
                for (var ikIndex = (AnimatorIKIndex)0; ikIndex < AnimatorIKIndex.Total; ikIndex++)
                {
                    if (ikIndex == AnimatorIKIndex.LeftHand || ikIndex == AnimatorIKIndex.RightHand)
                    {
                        if (!toolHumanoidIK_Hand) continue;
                    }
                    else if (ikIndex == AnimatorIKIndex.LeftFoot || ikIndex == AnimatorIKIndex.RightFoot)
                    {
                        if (!toolHumanoidIK_Foot) continue;
                    }
                    else
                    {
                        continue;
                    }
                    void ClearHumanoidIK(EditorCurveBinding binding)
                    {
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        if (curve == null) return;
                        for (int i = curve.length - 1; i >= 0; i--)
                        {
                            if (curve[i].time >= beginTime - halfFrameTime && curve[i].time <= endTime + halfFrameTime)
                            {
                                curve.RemoveKey(i);
                            }
                        }
                        AnimationUtility.SetEditorCurve(clip, binding, curve.length > 0 ? curve : null);
                    }
                    for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                        ClearHumanoidIK(AnimatorIkTBindings[(int)ikIndex][dofIndex]);
                    for (int dofIndex = 0; dofIndex < 4; dofIndex++)
                        ClearHumanoidIK(AnimatorIkQBindings[(int)ikIndex][dofIndex]);
                }
            }

            ToolsCommonAfter();
        }
        private void ToolsGenerateHumanoidIK(AnimationClip clip)
        {
            if (!IsHuman || !clip.isHumanMotion) return;
            if (!ToolsCommonBefore(clip, "Generate IK Keyframe")) return;

            Assert.IsTrue(clip == CurrentClip);

            HashSet<EditorCurveBinding> afterSyncBindings = new();

            var saveTime = CurrentTime;
            try
            {
                EditorUtility.DisplayProgressBar("Generate IK Keyframe", "", 0f);
                var firstFrame = Mathf.Clamp(toolHumanoidIK_FirstFrame, 0, GetLastFrame());
                var lastFrame = Mathf.Clamp(toolHumanoidIK_LastFrame, firstFrame, GetLastFrame());
                var beginTime = EditorCommon.SnapToFrame(firstFrame / clip.frameRate, clip.frameRate);
                var endTime = EditorCommon.SnapToFrame(lastFrame / clip.frameRate, clip.frameRate);

                SetCurrentTime(beginTime);

                float halfFrameTime = EditorCommon.GetHalfFrameTime(clip.frameRate);
                for (var ikIndex = (AnimatorIKIndex)0; ikIndex < AnimatorIKIndex.Total; ikIndex++)
                {
                    EditorUtility.DisplayProgressBar("Generate IK Keyframe", $"{(int)ikIndex} / {(int)AnimatorIKIndex.Total}", (int)ikIndex / (float)AnimatorIKIndex.Total);
                    if (ikIndex == AnimatorIKIndex.LeftHand || ikIndex == AnimatorIKIndex.RightHand)
                    {
                        if (!toolHumanoidIK_Hand) continue;
                    }
                    else if (ikIndex == AnimatorIKIndex.LeftFoot || ikIndex == AnimatorIKIndex.RightFoot)
                    {
                        if (!toolHumanoidIK_Foot) continue;
                    }
                    else
                    {
                        continue;
                    }

                    var frameCapacity = Mathf.Max(0, lastFrame - firstFrame + 1);
                    AnimationCurve[] ikTCurves = new AnimationCurve[3];
                    AnimationCurve[] ikQCurves = new AnimationCurve[4];
                    AnimationCommon.MiniKeyframeList[] ikTKeys = new AnimationCommon.MiniKeyframeList[3];
                    AnimationCommon.MiniKeyframeList[] ikQKeys = new AnimationCommon.MiniKeyframeList[4];
                    {
                        for (int dofIndex = 0; dofIndex < ikTCurves.Length; dofIndex++)
                        {
                            ikTKeys[dofIndex] = new AnimationCommon.MiniKeyframeList(frameCapacity);
                            ikTCurves[dofIndex] = AnimationUtility.GetEditorCurve(clip, AnimatorIkTBindings[(int)ikIndex][dofIndex]);
                            if (ikTCurves[dofIndex] == null)
                                ikTCurves[dofIndex] = new AnimationCurve();
                            else
                            {
                                for (int i = ikTCurves[dofIndex].length - 1; i >= 0; i--)
                                {
                                    if (ikTCurves[dofIndex][i].time >= beginTime - halfFrameTime && ikTCurves[dofIndex][i].time <= endTime + halfFrameTime)
                                    {
                                        ikTCurves[dofIndex].RemoveKey(i);
                                    }
                                }
                            }
                        }
                        for (int dofIndex = 0; dofIndex < ikQCurves.Length; dofIndex++)
                        {
                            ikQKeys[dofIndex] = new AnimationCommon.MiniKeyframeList(frameCapacity);
                            ikQCurves[dofIndex] = AnimationUtility.GetEditorCurve(clip, AnimatorIkQBindings[(int)ikIndex][dofIndex]);
                            if (ikQCurves[dofIndex] == null)
                                ikQCurves[dofIndex] = new AnimationCurve();
                            else
                            {
                                for (int i = ikQCurves[dofIndex].length - 1; i >= 0; i--)
                                {
                                    if (ikQCurves[dofIndex][i].time >= beginTime - halfFrameTime && ikQCurves[dofIndex][i].time <= endTime + halfFrameTime)
                                    {
                                        ikQCurves[dofIndex].RemoveKey(i);
                                    }
                                }
                            }
                        }
                    }
                    Skeleton.SetApplyIK(false);
                    Skeleton.SetTransformStart();
                    var localToWorldRotation = TransformPoseSave.StartRotation;
                    var worldToLocalMatrix = TransformPoseSave.StartMatrix.inverse;
                    var humanScale = Skeleton.Animator.humanScale;
                    var leftFeetBottomHeight = Skeleton.Animator.leftFeetBottomHeight;
                    var rightFeetBottomHeight = Skeleton.Animator.rightFeetBottomHeight;
                    var postLeftHand = GetHumanoidAvatarPostRotation(HumanBodyBones.LeftHand);
                    var postRightHand = GetHumanoidAvatarPostRotation(HumanBodyBones.RightHand);
                    var postLeftFoot = GetHumanoidAvatarPostRotation(HumanBodyBones.LeftFoot);
                    var postRightFoot = GetHumanoidAvatarPostRotation(HumanBodyBones.RightFoot);
                    var humanoidIndex = AnimatorIKIndex2HumanBodyBones[(int)ikIndex];
                    var t = Skeleton.HumanoidBones[(int)humanoidIndex].transform;
                    var positionTable = new Dictionary<float, Vector3>();
                    var rotationTable = new Dictionary<float, Quaternion>();
                    #region KeyInfoTable
                    {
                        var keyTimes = GetHumanoidKeyframeTimeList(clip, AnimatorIKIndex2HumanBodyBones[(int)ikIndex]);
                        foreach (var time in keyTimes)
                        {
                            Skeleton.SampleAnimation(clip, time);
                            positionTable.Add(time, t.position);
                            rotationTable.Add(time, t.rotation);
                        }
                    }
                    #endregion
                    for (int frame = firstFrame; frame <= lastFrame; frame++)
                    {
                        var time = UAw.GetFrameTime(frame, clip);
                        Skeleton.SampleAnimation(clip, time);
                        Vector3 position;
                        Quaternion rotation;
                        {
                            Vector3 positionL = Vector3.zero, positionR = Vector3.zero;
                            float nearL = float.MinValue, nearR = float.MaxValue;
                            foreach (var pair in positionTable)
                            {
                                if (pair.Key <= time && pair.Key > nearL)
                                {
                                    positionL = pair.Value;
                                    nearL = pair.Key;
                                }
                                if (pair.Key >= time && pair.Key < nearR)
                                {
                                    positionR = pair.Value;
                                    nearR = pair.Key;
                                }
                            }
                            var rate = nearR - nearL != 0f ? (time - nearL) / (nearR - nearL) : 0f;
                            position = Vector3.Lerp(positionL, positionR, rate);
                        }
                        {
                            Quaternion rotationL = Quaternion.identity, rotationR = Quaternion.identity;
                            float nearL = float.MinValue, nearR = float.MaxValue;
                            foreach (var pair in rotationTable)
                            {
                                if (pair.Key <= time && pair.Key > nearL)
                                {
                                    rotationL = pair.Value;
                                    nearL = pair.Key;
                                }
                                if (pair.Key >= time && pair.Key < nearR)
                                {
                                    rotationR = pair.Value;
                                    nearR = pair.Key;
                                }
                            }
                            var rate = nearR - nearL != 0f ? (time - nearL) / (nearR - nearL) : 0f;
                            rotation = Quaternion.Slerp(rotationL, rotationR, rate);
                        }

                        var rootT = GetAnimationValueAnimatorRootT(time);
                        var rootQ = GetAnimationValueAnimatorRootQ(time);

                        Vector3 ikGoalPosition = position;
                        Quaternion ikGoalRotation = rotation;
                        {
                            {
                                Quaternion postRotation = Quaternion.identity;
                                switch (ikIndex)
                                {
                                    case AnimatorIKIndex.LeftHand: postRotation = postLeftHand; break;
                                    case AnimatorIKIndex.RightHand: postRotation = postRightHand; break;
                                    case AnimatorIKIndex.LeftFoot: postRotation = postLeftFoot; break;
                                    case AnimatorIKIndex.RightFoot: postRotation = postRightFoot; break;
                                }
                                ikGoalRotation *= postRotation;
                            }
                            if (ikIndex == AnimatorIKIndex.LeftFoot || ikIndex == AnimatorIKIndex.RightFoot)
                            {
                                Vector3 footBottom = new(ikIndex == AnimatorIKIndex.LeftFoot ? leftFeetBottomHeight : rightFeetBottomHeight, 0, 0);
                                ikGoalPosition += ikGoalRotation * footBottom;
                            }
                            ikGoalPosition = worldToLocalMatrix.MultiplyPoint3x4(ikGoalPosition);
                            ikGoalRotation = Quaternion.Inverse(localToWorldRotation) * ikGoalRotation;
                            (ikGoalPosition, ikGoalRotation) = AnimationCommon.CalcAvatarIKGoal(ikGoalPosition, ikGoalRotation, rootT, rootQ, humanScale);
                        }
                        for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                            ikTKeys[dofIndex].SetKey(time, ikGoalPosition[dofIndex]);
                        for (int dofIndex = 0; dofIndex < 4; dofIndex++)
                            ikQKeys[dofIndex].SetKey(time, ikGoalRotation[dofIndex]);
                    }
                    static void AddKeys(AnimationCurve curve, AnimationCurve keys)
                    {
                        for (int i = 0; i < keys.length; i++)
                            AnimationCommon.AddKeyframe(curve, keys[i].time, keys[i].value);
                    }
                    var ikCurveDatas = new Dictionary<EditorCurveBinding, AnimationCurve>(ikTCurves.Length + ikQCurves.Length);
                    for (int dofIndex = 0; dofIndex < ikTCurves.Length; dofIndex++)
                    {
                        AddKeys(ikTCurves[dofIndex], ikTKeys[dofIndex].CreateAnimationCurve());
                        var binding = AnimatorIkTBindings[(int)ikIndex][dofIndex];
                        ikCurveDatas.Add(binding, ikTCurves[dofIndex]);
                        afterSyncBindings.Add(binding);
                    }
                    for (int dofIndex = 0; dofIndex < ikQCurves.Length; dofIndex++)
                    {
                        AddKeys(ikQCurves[dofIndex], ikQKeys[dofIndex].CreateAnimationCurve());
                        var binding = AnimatorIkQBindings[(int)ikIndex][dofIndex];
                        ikCurveDatas.Add(binding, ikQCurves[dofIndex]);
                        afterSyncBindings.Add(binding);
                    }
                    AnimationCommon.SetEditorCurves(clip, ikCurveDatas);
                }
            }
            finally
            {
                SetCurrentTime(saveTime);
                EditorUtility.ClearProgressBar();
            }

            EditorApplication.delayCall += () =>
            {
                SetAnimationWindowSynchroSelection(afterSyncBindings);
            };

            ToolsCommonAfter();
        }
        private void ToolsRootMotionMotionClear(AnimationClip clip)
        {
            if (!ToolsCommonBefore(clip, "Clear Generic Root Motion Keyframe")) return;

            {
                var removeCurveDatas = new Dictionary<EditorCurveBinding, AnimationCurve>(AnimationCommon.Binding.MotionT.Length + AnimationCommon.Binding.MotionQ.Length);
                for (int dof = 0; dof < 3; dof++)
                    removeCurveDatas.Add(AnimationCommon.Binding.MotionT[dof], null);
                for (int dof = 0; dof < 4; dof++)
                    removeCurveDatas.Add(AnimationCommon.Binding.MotionQ[dof], null);
                AnimationCommon.SetEditorCurves(clip, removeCurveDatas);
            }

            ToolsCommonAfter();
        }
        private void ToolsRootMotionMotionGenerate(AnimationClip clip)
        {
            if (VAW.Animator == null) return;
            if (!ToolsCommonBefore(clip, "Generate Generic Root Motion Keyframe")) return;

            var afterSyncBindings = new List<EditorCurveBinding>();

            if (IsHuman)
            {
                var motionCurveDatas = new Dictionary<EditorCurveBinding, AnimationCurve>(AnimationCommon.Binding.MotionT.Length + AnimationCommon.Binding.MotionQ.Length)
                {
                    [AnimationCommon.Binding.MotionT[0]] = GetAnimationCurveAnimatorRootT(0),
                    [AnimationCommon.Binding.MotionT[2]] = GetAnimationCurveAnimatorRootT(2),
                };
                {
                    var curve = GetAnimationCurveAnimatorRootT(1);
                    for (int i = 0; i < curve.length; i++)
                    {
                        var key = curve[i];
                        key.value -= 1f;
                        curve.MoveKey(i, key);
                    }
                    motionCurveDatas.Add(AnimationCommon.Binding.MotionT[1], curve);
                }
                for (int dof = 0; dof < 4; dof++)
                    motionCurveDatas.Add(AnimationCommon.Binding.MotionQ[dof], GetAnimationCurveAnimatorRootQ(dof));
                AnimationCommon.SetEditorCurves(clip, motionCurveDatas);

                afterSyncBindings.AddRange(AnimationCommon.Binding.MotionT);
                afterSyncBindings.AddRange(AnimationCommon.Binding.MotionQ);
            }
            else
            {
                try
                {
                    EditorUtility.DisplayProgressBar("Generate Generic Root Motion Keyframe", "", 0f);
                    var lastFrame = EditorCommon.GetLastFrame(clip.length, clip.frameRate);
                    AnimationCommon.MiniKeyframeList[] rootT = new AnimationCommon.MiniKeyframeList[3];
                    AnimationCommon.MiniKeyframeList[] rootQ = new AnimationCommon.MiniKeyframeList[4];
                    {
                        for (int dof = 0; dof < 3; dof++)
                            rootT[dof] = new AnimationCommon.MiniKeyframeList(lastFrame + 1);
                        for (int dof = 0; dof < 4; dof++)
                            rootQ[dof] = new AnimationCommon.MiniKeyframeList(lastFrame + 1);
                    }
                    Skeleton.SetApplyIK(false);
                    Skeleton.SetTransformOrigin();
                    var rootTransform = Skeleton.Bones[RootMotionBoneIndex >= 0 ? RootMotionBoneIndex : 0].transform;

                    Skeleton.SampleAnimation(clip, 0f);
                    rootTransform.GetPositionAndRotation(out Vector3 startPosition, out Quaternion startRotation);

                    for (int frame = 0; frame <= lastFrame; frame++)
                    {
                        var time = GetFrameTime(frame);

                        Skeleton.SampleAnimation(clip, time);

                        var position = rootTransform.position - startPosition;
                        var rotation = Quaternion.Inverse(startRotation) * rootTransform.rotation;

                        for (int dof = 0; dof < 3; dof++)
                            rootT[dof].SetKey(time, position[dof]);
                        for (int dof = 0; dof < 4; dof++)
                            rootQ[dof].SetKey(time, rotation[dof]);
                    }
                    {
                        var motionCurveDatas = new Dictionary<EditorCurveBinding, AnimationCurve>(AnimationCommon.Binding.MotionT.Length + AnimationCommon.Binding.MotionQ.Length);
                        for (int dof = 0; dof < 3; dof++)
                        {
                            var curve = rootT[dof].CreateAnimationCurve();
                            AnimationCommon.SetAnimationCurveTangent(curve, typeof(float));
                            motionCurveDatas.Add(AnimationCommon.Binding.MotionT[dof], curve);
                        }
                        for (int dof = 0; dof < 4; dof++)
                        {
                            var curve = rootQ[dof].CreateAnimationCurve();
                            AnimationCommon.SetAnimationCurveTangent(curve, typeof(float));
                            motionCurveDatas.Add(AnimationCommon.Binding.MotionQ[dof], curve);
                        }
                        AnimationCommon.SetEditorCurves(clip, motionCurveDatas);
                    }

                    afterSyncBindings.AddRange(AnimationCommon.Binding.MotionT);
                    afterSyncBindings.AddRange(AnimationCommon.Binding.MotionQ);
                }
                finally
                {
                    Skeleton.SetTransformStart();
                    EditorUtility.ClearProgressBar();
                }
            }

            SelectMotionTool();

            EditorApplication.delayCall += () =>
            {
                SetAnimationWindowSynchroSelection(afterSyncBindings);
            };

            ToolsCommonAfter();
        }
        private void ToolsRootMotionRootClear(AnimationClip clip)
        {
            if (!ToolsCommonBefore(clip, "Clear Generic Root Motion Keyframe")) return;

            {
                var removeCurveDatas = new Dictionary<EditorCurveBinding, AnimationCurve>(AnimationCommon.Binding.RootT.Length + AnimationCommon.Binding.RootQ.Length);
                for (int dof = 0; dof < 3; dof++)
                    removeCurveDatas.Add(AnimationCommon.Binding.RootT[dof], null);
                for (int dof = 0; dof < 4; dof++)
                    removeCurveDatas.Add(AnimationCommon.Binding.RootQ[dof], null);
                AnimationCommon.SetEditorCurves(clip, removeCurveDatas);
            }

            ToolsCommonAfter();
        }
        private void ToolsRootMotionRootGenerate(AnimationClip clip)
        {
            if (IsHuman || RootMotionBoneIndex < 0) return;
            if (!ToolsCommonBefore(clip, "Generate Generic Root Motion Keyframe")) return;

            var afterSyncBindings = new List<EditorCurveBinding>();

            try
            {
                EditorUtility.DisplayProgressBar("Generate Generic Root Motion Keyframe", "", 0f);
                var lastFrame = EditorCommon.GetLastFrame(clip.length, clip.frameRate);
                AnimationCommon.MiniKeyframeList[] rootT = new AnimationCommon.MiniKeyframeList[3];
                AnimationCommon.MiniKeyframeList[] rootQ = new AnimationCommon.MiniKeyframeList[4];
                {
                    for (int dof = 0; dof < 3; dof++)
                        rootT[dof] = new AnimationCommon.MiniKeyframeList(lastFrame + 1);
                    for (int dof = 0; dof < 4; dof++)
                        rootQ[dof] = new AnimationCommon.MiniKeyframeList(lastFrame + 1);
                }
                Skeleton.SetApplyIK(false);
                Skeleton.SetTransformOrigin();
                var rootNodeTransform = Skeleton.Bones[RootMotionBoneIndex >= 0 ? RootMotionBoneIndex : 0].transform;

                Skeleton.SampleAnimation(clip, 0f);

                var startRootWorldToLocalMatrix = Skeleton.GameObject.transform.worldToLocalMatrix;
                var startRootRotation = Skeleton.GameObject.transform.rotation;

                for (int frame = 0; frame <= lastFrame; frame++)
                {
                    var time = GetFrameTime(frame);

                    Skeleton.SampleAnimation(clip, time);

                    var position = startRootWorldToLocalMatrix.MultiplyPoint3x4(rootNodeTransform.position);
                    var rotation = Quaternion.Inverse(startRootRotation) * rootNodeTransform.rotation;

                    for (int dof = 0; dof < 3; dof++)
                        rootT[dof].SetKey(time, position[dof]);
                    for (int dof = 0; dof < 4; dof++)
                        rootQ[dof].SetKey(time, rotation[dof]);
                }
                {
                    var rootCurveDatas = new Dictionary<EditorCurveBinding, AnimationCurve>(AnimationCommon.Binding.RootT.Length + AnimationCommon.Binding.RootQ.Length);
                    for (int dof = 0; dof < 3; dof++)
                    {
                        var curve = rootT[dof].CreateAnimationCurve();
                        AnimationCommon.SetAnimationCurveTangent(curve, typeof(float));
                        rootCurveDatas.Add(AnimationCommon.Binding.RootT[dof], curve);
                    }
                    for (int dof = 0; dof < 4; dof++)
                    {
                        var curve = rootQ[dof].CreateAnimationCurve();
                        AnimationCommon.SetAnimationCurveTangent(curve, typeof(float));
                        rootCurveDatas.Add(AnimationCommon.Binding.RootQ[dof], curve);
                    }
                    AnimationCommon.SetEditorCurves(clip, rootCurveDatas);
                }

                afterSyncBindings.AddRange(AnimationCommon.Binding.RootT);
                afterSyncBindings.AddRange(AnimationCommon.Binding.RootQ);
            }
            finally
            {
                Skeleton.SetTransformStart();
                EditorUtility.ClearProgressBar();
            }

            SelectGameObject(null);

            EditorApplication.delayCall += () =>
            {
                SetAnimationWindowSynchroSelection(afterSyncBindings);
            };

            ToolsCommonAfter();
        }
        private void ToolsCopy(AnimationClip clip)
        {
            if (!ToolsCommonBefore(clip, "Copy Keyframe")) return;

            try
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                var rbindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                var events = AnimationUtility.GetAnimationEvents(clip);
                var beginTime = EditorCommon.SnapToFrame(toolCopy_FirstFrame / clip.frameRate, clip.frameRate);
                var endTime = EditorCommon.SnapToFrame(toolCopy_LastFrame / clip.frameRate, clip.frameRate);
                var writeBeginTime = EditorCommon.SnapToFrame(toolCopy_WriteFrame / clip.frameRate, clip.frameRate);
                var writeEndTime = writeBeginTime + (endTime - beginTime);
                float halfFrameTime = EditorCommon.GetHalfFrameTime(clip.frameRate);
                int progressIndex = 0;
                int progressTotal = 3;

                EditorUtility.DisplayProgressBar("Copy Keyframe", "Read", progressIndex++ / (float)progressTotal);
                if (writeBeginTime > clip.length)
                {
                    #region AddLastLinearKey
                    var addLastLinearCurveDatas = new Dictionary<EditorCurveBinding, AnimationCurve>(bindings.Length);
                    foreach (var binding in bindings)
                    {
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        var keyIndex = AnimationCommon.FindKeyframeAtTime(curve, clip.length);
                        if (keyIndex < 0)
                        {
                            keyIndex = AnimationCommon.AddKeyframe(curve, clip.length, curve.Evaluate(clip.length));
                            AnimationUtility.SetKeyLeftTangentMode(curve, keyIndex, AnimationUtility.TangentMode.Linear);
                            if (keyIndex > 0)
                                AnimationUtility.SetKeyRightTangentMode(curve, keyIndex - 1, AnimationUtility.TangentMode.Linear);
                            addLastLinearCurveDatas.Add(binding, curve);
                        }
                    }
                    AnimationCommon.SetEditorCurves(clip, addLastLinearCurveDatas);
                    #endregion
                }

                EditorUtility.DisplayProgressBar("Copy Keyframe", "Read", progressIndex++ / (float)progressTotal);
                #region CurveBindings
                List<Keyframe>[] curveCopyKeyframes = new List<Keyframe>[bindings.Length];
                List<int>[] markKeyIndexes = new List<int>[bindings.Length];
                var updateCurveDatas = new Dictionary<EditorCurveBinding, AnimationCurve>(bindings.Length);
                for (int i = 0; i < bindings.Length; i++)
                {
                    curveCopyKeyframes[i] = new List<Keyframe>();
                    markKeyIndexes[i] = new List<int>();
                    bool update = false;
                    var curve = AnimationUtility.GetEditorCurve(clip, bindings[i]);
                    {
                        var key = new Keyframe(writeBeginTime, curve.Evaluate(beginTime));
                        markKeyIndexes[i].Add(curveCopyKeyframes[i].Count);
                        curveCopyKeyframes[i].Add(key);
                    }
                    if (curveCopyKeyframes[i].FindIndex((x) => Mathf.Approximately(EditorCommon.SnapToFrame(x.time, clip.frameRate), EditorCommon.SnapToFrame(writeEndTime, clip.frameRate))) < 0)
                    {
                        var key = new Keyframe(writeEndTime, curve.Evaluate(endTime));
                        markKeyIndexes[i].Add(curveCopyKeyframes[i].Count);
                        curveCopyKeyframes[i].Add(key);
                    }
                    for (int j = 0; j < curve.length; j++)
                    {
                        if (curve[j].time >= beginTime - halfFrameTime && curve[j].time <= endTime + halfFrameTime)
                        {
                            var key = curve[j];
                            key.time = EditorCommon.SnapToFrame(writeBeginTime + (key.time - beginTime), clip.frameRate);
                            curveCopyKeyframes[i].Add(key);
                        }
                        if (curve[j].time > writeBeginTime + halfFrameTime && curve[j].time < writeEndTime - halfFrameTime)
                        {
                            curve.RemoveKey(j--);
                            update = true;
                        }
                    }
                    {
                        void ActionAddKeyframe(int frame)
                        {
                            var setTime = UAw.GetFrameTime(frame, clip);
                            var keyIndex = AnimationCommon.FindKeyframeAtTime(curve, setTime);
                            if (keyIndex < 0)
                            {
                                AnimationCommon.AddKeyframe(curve, setTime, curve.Evaluate(setTime));
                                update = true;
                            }
                        }
                        if (toolCopy_WriteFrame < toolCopy_LastFrame)
                        {
                            ActionAddKeyframe(toolCopy_WriteFrame);
                        }
                        if (toolCopy_WriteFrame + (toolCopy_LastFrame - toolCopy_FirstFrame) > toolCopy_FirstFrame)
                        {
                            ActionAddKeyframe(toolCopy_WriteFrame + (toolCopy_LastFrame - toolCopy_FirstFrame));
                        }
                    }
                    if (update)
                        updateCurveDatas.Add(bindings[i], curve);
                }
                AnimationCommon.SetEditorCurves(clip, updateCurveDatas);
                #endregion
                #region ObjectReferenceCurveBindings
                List<ObjectReferenceKeyframe>[] rcurveCopyKeyframes = new List<ObjectReferenceKeyframe>[rbindings.Length];
                var updateReferenceCurves = new Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]>(rbindings.Length);
                for (int i = 0; i < rbindings.Length; i++)
                {
                    rcurveCopyKeyframes[i] = new List<ObjectReferenceKeyframe>();
                    bool update = false;
                    var keys = new List<ObjectReferenceKeyframe>(AnimationUtility.GetObjectReferenceCurve(clip, rbindings[i]));
                    for (int j = 0; j < keys.Count; j++)
                    {
                        if (keys[j].time >= beginTime - halfFrameTime && keys[j].time <= endTime + halfFrameTime)
                        {
                            var key = keys[j];
                            key.time = EditorCommon.SnapToFrame(writeBeginTime + (key.time - beginTime), clip.frameRate);
                            rcurveCopyKeyframes[i].Add(key);
                        }
                        if (keys[j].time > writeBeginTime + halfFrameTime && keys[j].time < writeEndTime - halfFrameTime)
                        {
                            keys.RemoveAt(j--);
                            update = true;
                        }
                    }
                    if (update)
                        updateReferenceCurves.Add(rbindings[i], keys.ToArray());
                }
                AnimationCommon.SetObjectReferenceCurves(clip, updateReferenceCurves);
                #endregion
                #region AnimationEvents
                List<AnimationEvent> newEvents = new();
                for (int i = 0; i < events.Length; i++)
                {
                    if (events[i].time >= beginTime - halfFrameTime && events[i].time <= endTime + halfFrameTime)
                    {
                        var key = new AnimationEvent()
                        {
                            stringParameter = events[i].stringParameter,
                            floatParameter = events[i].floatParameter,
                            intParameter = events[i].intParameter,
                            objectReferenceParameter = events[i].objectReferenceParameter,
                            functionName = events[i].functionName,
                            time = writeBeginTime + (events[i].time - beginTime),
                            messageOptions = events[i].messageOptions,
                        };
                        newEvents.Add(key);
                    }
                    if (events[i].time < writeBeginTime - halfFrameTime || events[i].time > writeEndTime + halfFrameTime)
                    {
                        newEvents.Add(events[i]);
                    }
                }
                newEvents.Sort((x, y) =>
                {
                    if (x.time > y.time) return 1;
                    else if (x.time < y.time) return -1;
                    else return 0;
                });
                #endregion

                EditorUtility.DisplayProgressBar("Copy Keyframe", "Write", progressIndex++ / (float)progressTotal);
                #region CurveBindings
                var writeCurveDatas = new Dictionary<EditorCurveBinding, AnimationCurve>(bindings.Length);
                for (int i = 0; i < bindings.Length; i++)
                {
                    if (curveCopyKeyframes[i] == null || curveCopyKeyframes[i].Count <= 0) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, bindings[i]);
                    for (int j = 0; j < curveCopyKeyframes[i].Count; j++)
                    {
                        if (markKeyIndexes[i].Contains(j))
                        {
                            AnimationCommon.SetKeyframe(curve, curveCopyKeyframes[i][j].time, curveCopyKeyframes[i][j].value);
                        }
                        else
                        {
                            var index = AnimationCommon.SetKeyframe(curve, curveCopyKeyframes[i][j]);
                            curve.MoveKey(index, curveCopyKeyframes[i][j]);
                        }
                    }
                    writeCurveDatas.Add(bindings[i], curve);
                }
                AnimationCommon.SetEditorCurves(clip, writeCurveDatas);
                #endregion
                #region ObjectReferenceCurveBindings
                var writeReferenceCurves = new Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]>(rbindings.Length);
                for (int i = 0; i < rbindings.Length; i++)
                {
                    if (rcurveCopyKeyframes[i] == null || rcurveCopyKeyframes[i].Count <= 0) continue;
                    var keys = new List<ObjectReferenceKeyframe>(AnimationUtility.GetObjectReferenceCurve(clip, rbindings[i]));
                    for (int j = 0; j < rcurveCopyKeyframes[i].Count; j++)
                    {
                        var keyIndex = AnimationCommon.FindKeyframeAtTime(keys, rcurveCopyKeyframes[i][j].time);
                        if (keyIndex >= 0)
                        {
                            keys[keyIndex] = rcurveCopyKeyframes[i][j];
                        }
                        else
                        {
                            keys.Add(rcurveCopyKeyframes[i][j]);
                        }
                    }
                    writeReferenceCurves.Add(rbindings[i], keys.ToArray());
                }
                AnimationCommon.SetObjectReferenceCurves(clip, writeReferenceCurves);
                #endregion
                #region AnimationEvents
                AnimationUtility.SetAnimationEvents(clip, newEvents.ToArray());
                #endregion
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ToolsCommonAfter();
        }
        private void ToolsTrim(AnimationClip clip)
        {
            if (!ToolsCommonBefore(clip, "Trim Keyframe")) return;

            try
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                var rbindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                var events = AnimationUtility.GetAnimationEvents(clip);
                var beginTime = EditorCommon.SnapToFrame(toolTrim_FirstFrame / clip.frameRate, clip.frameRate);
                var endTime = EditorCommon.SnapToFrame(toolTrim_LastFrame / clip.frameRate, clip.frameRate);
                float halfFrameTime = EditorCommon.GetHalfFrameTime(clip.frameRate);
                int progressIndex = 0;
                int progressTotal = bindings.Length * 3 + rbindings.Length + events.Length;
                {
                    AnimationCurve[] curves = new AnimationCurve[bindings.Length];
                    var clearCurveDatas = new Dictionary<EditorCurveBinding, AnimationCurve>(bindings.Length);
                    var writeCurveDatas = new Dictionary<EditorCurveBinding, AnimationCurve>(bindings.Length);
                    for (int i = 0; i < bindings.Length; i++)
                    {
                        EditorUtility.DisplayProgressBar("Trim", AnimationCommon.GetBindingDisplayName(bindings[i]), progressIndex++ / (float)progressTotal);
                        var curve = AnimationUtility.GetEditorCurve(clip, bindings[i]);
                        {
                            var keys = new List<Keyframe>();
                            for (int j = 0; j < curve.length; j++)
                            {
                                if (curve[j].time < beginTime - halfFrameTime || curve[j].time > endTime + halfFrameTime) continue;
                                var tmp = curve[j];
                                tmp.time = EditorCommon.SnapToFrame(tmp.time - beginTime, clip.frameRate);
                                keys.Add(tmp);
                            }
                            if (keys.FindIndex((x) => Mathf.Approximately(x.time, 0f)) < 0)
                            {
                                keys.Insert(0, new Keyframe(0, curve.Evaluate(beginTime)));
                            }
                            if (keys.FindIndex((x) => Mathf.Approximately(x.time, endTime - beginTime)) < 0)
                            {
                                keys.Add(new Keyframe(endTime - beginTime, curve.Evaluate(endTime)));
                            }
                            curve.keys = keys.ToArray();
                        }
                        curves[i] = curve;
                    }
                    for (int i = 0; i < bindings.Length; i++)
                    {
                        EditorUtility.DisplayProgressBar("Trim", AnimationCommon.GetBindingDisplayName(bindings[i]), progressIndex++ / (float)progressTotal);
                        clearCurveDatas.Add(bindings[i], null);
                    }
                    AnimationCommon.SetEditorCurves(clip, clearCurveDatas);
                    for (int i = 0; i < bindings.Length; i++)
                    {
                        EditorUtility.DisplayProgressBar("Trim", AnimationCommon.GetBindingDisplayName(bindings[i]), progressIndex++ / (float)progressTotal);
                        writeCurveDatas.Add(bindings[i], curves[i]);
                    }
                    AnimationCommon.SetEditorCurves(clip, writeCurveDatas);
                }
                {
                    var referenceCurveDatas = new Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]>(rbindings.Length);
                    for (int i = 0; i < rbindings.Length; i++)
                    {
                        EditorUtility.DisplayProgressBar("Trim", AnimationCommon.GetBindingDisplayName(rbindings[i]), progressIndex++ / (float)progressTotal);
                        var rkeys = AnimationUtility.GetObjectReferenceCurve(clip, rbindings[i]);
                        var keys = new List<ObjectReferenceKeyframe>();
                        foreach (var key in rkeys)
                        {
                            if (key.time < beginTime - halfFrameTime || key.time > endTime + halfFrameTime) continue;
                            var tmp = key;
                            tmp.time = EditorCommon.SnapToFrame(tmp.time - beginTime, clip.frameRate);
                            keys.Add(tmp);
                        }
                        if (keys.FindIndex((x) => Mathf.Approximately(x.time, 0f)) < 0)
                        {
                            var nearIndex = FindBeforeNearKeyframeAtTime(rkeys, beginTime);
                            if (rkeys.Length > 0)
                            {
                                var beginKeyIndex = nearIndex >= 0 ? nearIndex : 0;
                                keys.Insert(0, new ObjectReferenceKeyframe() { time = 0f, value = rkeys[beginKeyIndex].value });
                            }
                        }
                        referenceCurveDatas.Add(rbindings[i], keys.ToArray());
                    }
                    AnimationCommon.SetObjectReferenceCurves(clip, referenceCurveDatas);
                }
                {
                    List<AnimationEvent> newEvents = new(events.Length);
                    for (int i = 0; i < events.Length; i++)
                    {
                        EditorUtility.DisplayProgressBar("Trim", events[i].functionName, progressIndex++ / (float)progressTotal);
                        if (events[i].time < beginTime - halfFrameTime || events[i].time > endTime + halfFrameTime) continue;
                        var tmp = events[i];
                        tmp.time = EditorCommon.SnapToFrame(tmp.time - beginTime, clip.frameRate);
                        newEvents.Add(tmp);
                    }
                    AnimationUtility.SetAnimationEvents(clip, newEvents.ToArray());
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ToolsCommonAfter();
        }
        private void ToolsAdd(AnimationClip clip)
        {
            if (!ToolsCommonBefore(clip, "Add Clip")) return;

            try
            {
                var addTime = clip.length + (1f / clip.frameRate);
                {
                    var curveDatas = new Dictionary<EditorCurveBinding, AnimationCurve>();
                    foreach (var binding in AnimationUtility.GetCurveBindings(toolAdd_Clip))
                    {
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        curve ??= new AnimationCurve();
                        var srcCurve = AnimationUtility.GetEditorCurve(toolAdd_Clip, binding);
                        for (int i = 0; i < srcCurve.length; i++)
                        {
                            var key = srcCurve[i];
                            key.time += addTime;
                            curve.AddKey(key);
                        }
                        curveDatas.Add(binding, curve);
                    }
                    AnimationCommon.SetEditorCurves(clip, curveDatas);
                }
                {
                    var referenceCurveDatas = new Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]>();
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(toolAdd_Clip))
                    {
                        var refKeys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                        var curve = refKeys != null ? refKeys.ToList() : new List<ObjectReferenceKeyframe>();
                        var srcCurve = AnimationUtility.GetObjectReferenceCurve(toolAdd_Clip, binding);
                        for (int i = 0; i < srcCurve.Length; i++)
                        {
                            var key = srcCurve[i];
                            key.time += addTime;
                            curve.Add(key);
                        }
                        referenceCurveDatas.Add(binding, curve.ToArray());
                    }
                    AnimationCommon.SetObjectReferenceCurves(clip, referenceCurveDatas);
                }
                {
                    var events = AnimationUtility.GetAnimationEvents(clip).ToList();
                    foreach (var ev in AnimationUtility.GetAnimationEvents(toolAdd_Clip))
                    {
                        var tmp = ev;
                        tmp.time += addTime;
                        events.Add(tmp);
                    }
                    AnimationUtility.SetAnimationEvents(clip, events.ToArray());
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ToolsCommonAfter();
        }
        private void ToolsCombine(AnimationClip clip)
        {
            if (!ToolsCommonBefore(clip, "Combine Clip")) return;

            try
            {
                {
                    var curveDatas = new Dictionary<EditorCurveBinding, AnimationCurve>();
                    foreach (var binding in AnimationUtility.GetCurveBindings(toolCombine_Clip))
                    {
                        curveDatas.Add(binding, AnimationUtility.GetEditorCurve(toolCombine_Clip, binding));
                    }
                    AnimationCommon.SetEditorCurves(clip, curveDatas);
                }
                {
                    var referenceCurveDatas = new Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]>();
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(toolCombine_Clip))
                    {
                        referenceCurveDatas.Add(binding, AnimationUtility.GetObjectReferenceCurve(toolCombine_Clip, binding));
                    }
                    AnimationCommon.SetObjectReferenceCurves(clip, referenceCurveDatas);
                }
                {
                    var events = AnimationUtility.GetAnimationEvents(clip).ToList();
                    foreach (var ev in AnimationUtility.GetAnimationEvents(toolCombine_Clip))
                    {
                        events.Add(ev);
                    }
                    events.Sort((x, y) =>
                    {
                        if (x.time > y.time) return 1;
                        else if (x.time < y.time) return -1;
                        else return 0;
                    });
                    AnimationUtility.SetAnimationEvents(clip, events.ToArray());
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ToolsCommonAfter();
        }
        private class AnimatableBindingData
        {
            public bool needWrite;
            private readonly int capacity;
            private AnimationCommon.MiniKeyframeList curve;
            private AnimationCommon.MiniObjectReferenceKeyframeList refCurve;

            public AnimatableBindingData(int capacity) => this.capacity = capacity;

            public void SetKey(float time, float value)
            {
                curve ??= new AnimationCommon.MiniKeyframeList(capacity);
                curve.SetKey(time, value);
            }

            public void SetKey(float time, UnityEngine.Object value)
            {
                refCurve ??= new AnimationCommon.MiniObjectReferenceKeyframeList(capacity);
                refCurve.SetKey(time, value);
            }

            public AnimationCurve CreateAnimationCurve() => curve?.CreateAnimationCurve();
            public ObjectReferenceKeyframe[] CreateObjectReferenceKeyframes() => refCurve?.CreateObjectReferenceKeyframes();
        }
        private void ToolsCreateNewClip(string clipPath)
        {
            AnimationClip newClip;
            using (new VeryAnimationWindow.CustomAssetModificationProcessor.PauseScope())
            {
                newClip = AnimationCommon.CreateNewClipAtPath(clipPath);
            }

            if (!ToolsCommonBefore(newClip, "Create new clip")) return;

            var saveCurrentTime = UAw.GetCurrentTime();
            var saveApplyRootMotion = VAW.Animator != null && VAW.Animator.applyRootMotion;
#if VERYANIMATION_TIMELINE
            var timelineAnimationPlayableAsset = UAw.GetTimelineAnimationPlayableAsset();
#endif

            try
            {
                int progressIndex = 0;
                int progressTotal = 1;
                EditorUtility.DisplayProgressBar("Create", clipPath, progressIndex++ / (float)progressTotal);

                var lastFrame = GetLastFrame();
                var baseClip = CurrentClip;
                if (animationMode == AnimationMode.Layers)
                {
                    var ac = AnimationCommon.GetAnimatorController(VAW.Animator);
                    if (ac != null && CurrentLayerClips != null)
                    {
                        var layers = ac.layers;
                        baseClip = null;
                        for (int i = 0; i < layers.Length; i++)
                        {
                            if (layers[i].stateMachine == null)
                                continue;
                            if (CurrentLayerClips.TryGetValue(layers[i].stateMachine, out AnimationClip lclip) && lclip != null)
                            {
                                if (baseClip == null)
                                    baseClip = lclip;
                                lastFrame = Mathf.Max(lastFrame, EditorCommon.GetLastFrame(lclip.length, lclip.frameRate));
                            }
                        }
                    }
                }

                AnimationUtility.SetAnimationClipSettings(newClip, AnimationUtility.GetAnimationClipSettings(baseClip));
                {
                    newClip.frameRate = baseClip.frameRate;
                    newClip.wrapMode = baseClip.wrapMode;
                    newClip.localBounds = baseClip.localBounds;
                    newClip.legacy = baseClip.legacy;
                }

                if (toolCreateNewClip_Mode == CreateNewClipMode.Duplicate || toolCreateNewClip_Mode == CreateNewClipMode.Mirror)
                {
                    #region Duplicate
                    var duplicateCurveDatas = new Dictionary<EditorCurveBinding, AnimationCurve>();
                    foreach (var binding in AnimationUtility.GetCurveBindings(baseClip))
                    {
                        duplicateCurveDatas.Add(binding, AnimationUtility.GetEditorCurve(baseClip, binding));
                    }
                    AnimationCommon.SetEditorCurves(newClip, duplicateCurveDatas);
                    var duplicateReferenceCurveDatas = new Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]>();
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(baseClip))
                    {
                        duplicateReferenceCurveDatas.Add(binding, AnimationUtility.GetObjectReferenceCurve(baseClip, binding));
                    }
                    AnimationCommon.SetObjectReferenceCurves(newClip, duplicateReferenceCurveDatas);
                    AnimationUtility.SetAnimationEvents(newClip, AnimationUtility.GetAnimationEvents(baseClip));
                    #endregion
                }
                if (toolCreateNewClip_Mode == CreateNewClipMode.Mirror)
                {
                    #region Mirror
                    {
                        #region SwapMirrorCurve
                        {
                            var bindings = new List<EditorCurveBinding>(AnimationUtility.GetCurveBindings(newClip));
                            int GetMirrorBindingIndex(EditorCurveBinding binding)
                            {
                                var mbinding = GetMirrorAnimationCurveBinding(binding);
                                if (!mbinding.HasValue) return -1;
                                return bindings.IndexOf(mbinding.Value);
                            }
                            #region CreateMirrorCurve
                            int createBindingCount = bindings.Count;
                            for (int bindingIndex = 0; bindingIndex < createBindingCount; bindingIndex++)
                            {
                                var binding = bindings[bindingIndex];
                                var mbinding = GetMirrorAnimationCurveBinding(binding);
                                if (!mbinding.HasValue) continue;
                                if (!bindings.Contains(mbinding.Value))
                                {
                                    var mcurve = new AnimationCurve();
                                    void AddKey(float value)
                                    {
                                        AnimationCommon.AddKeyframe(mcurve, 0, value);
                                        AnimationCommon.AddKeyframe(mcurve, newClip.length, value);
                                    }
                                    int dofIndex = AnimationCommon.GetDOFIndex(mbinding.Value);
                                    if (mbinding.Value.type == typeof(Animator))
                                    {
                                        if (GetIkQIndexFromCurveBinding(mbinding.Value) != AnimatorIKIndex.None)
                                            AddKey(dofIndex == 3 ? 1f : 0f);
                                        else
                                            AddKey(0f);
                                    }
                                    else if (mbinding.Value.type == typeof(Transform))
                                    {
                                        var boneIndex = GetBoneIndexFromCurveBinding(mbinding.Value);
                                        if (IsTransformPositionCurveBinding(mbinding.Value))
                                            AddKey(BoneSaveTransforms[boneIndex].localPosition[dofIndex]);
                                        else if (IsTransformRotationCurveBinding(mbinding.Value))
                                        {
                                            if (URotationCurveInterpolation.GetModeFromCurveData(mbinding.Value) == URotationCurveInterpolation.Mode.RawEuler)
                                                AddKey(BoneSaveTransforms[boneIndex].localRotation.eulerAngles[dofIndex]);
                                            else if (URotationCurveInterpolation.GetModeFromCurveData(mbinding.Value) == URotationCurveInterpolation.Mode.RawQuaternions)
                                                AddKey(BoneSaveTransforms[boneIndex].localRotation[dofIndex]);
                                            else
                                                Assert.IsTrue(false);
                                        }
                                        else if (IsTransformScaleCurveBinding(mbinding.Value))
                                            AddKey(BoneSaveTransforms[boneIndex].localScale[dofIndex]);
                                    }
                                    else if (IsSkinnedMeshRendererBlendShapeCurveBinding(mbinding.Value))
                                    {
                                        var boneIndex = GetBoneIndexFromCurveBinding(mbinding.Value);
                                        var renderer = Bones[boneIndex].GetComponent<SkinnedMeshRenderer>();
                                        var name = AnimationCommon.PropertyName2BlendShapeName(mbinding.Value.propertyName);
                                        AddKey(BlendShapeWeightSave.GetDefaultWeight(renderer, name));
                                    }
                                    else
                                    {
                                        Assert.IsTrue(false);
                                    }
                                    AnimationUtility.SetEditorCurve(newClip, mbinding.Value, mcurve);
                                    bindings.Add(mbinding.Value);
                                }
                            }
                            #endregion

                            #region MirrorCurve
                            {
                                void SwapCurve(int indexA, int indexB)
                                {
                                    var curveA = AnimationUtility.GetEditorCurve(newClip, bindings[indexA]);
                                    var curveB = AnimationUtility.GetEditorCurve(newClip, bindings[indexB]);
                                    AnimationUtility.SetEditorCurve(newClip, bindings[indexB], curveA);
                                    AnimationUtility.SetEditorCurve(newClip, bindings[indexA], curveB);
                                }
                                void MirrorCurve(int index)
                                {
                                    var curve = AnimationUtility.GetEditorCurve(newClip, bindings[index]);
                                    for (int i = 0; i < curve.length; i++)
                                    {
                                        var key = curve[i];
                                        key.value = -key.value;
                                        key.inTangent = -key.inTangent;
                                        key.outTangent = -key.outTangent;
                                        curve.MoveKey(i, key);
                                    }
                                    AnimationUtility.SetEditorCurve(newClip, bindings[index], curve);
                                }

                                bool[] doneFlag = new bool[bindings.Count];
                                for (int i = 0; i < bindings.Count; i++)
                                {
                                    if (doneFlag[i]) continue;
                                    doneFlag[i] = true;
                                    if (bindings[i].type == typeof(Animator))
                                    {
                                        #region Animator
                                        AnimatorIKIndex ikIndex = AnimatorIKIndex.None;
                                        AnimatorTDOFIndex tdofIndex = AnimatorTDOFIndex.None;
                                        var muscleIndex = GetMuscleIndexFromCurveBinding(bindings[i]);
                                        if (muscleIndex >= 0)
                                        {
                                            #region Muscle
                                            var mirrorMuscleIndex = GetMirrorMuscleIndex(muscleIndex);
                                            if (mirrorMuscleIndex >= 0)
                                            {
                                                var mirrorBindingIndex = GetMirrorBindingIndex(bindings[i]);
                                                if (mirrorBindingIndex >= 0)
                                                {
                                                    doneFlag[mirrorBindingIndex] = true;
                                                    SwapCurve(i, mirrorBindingIndex);
                                                }
                                            }
                                            else if (muscleIndex == HumanTrait.MuscleFromBone(HumanTrait.BoneFromMuscle(muscleIndex), 0) ||
                                                    muscleIndex == HumanTrait.MuscleFromBone(HumanTrait.BoneFromMuscle(muscleIndex), 1))
                                            {
                                                MirrorCurve(i);
                                            }
                                            #endregion
                                        }
                                        else if (bindings[i].propertyName == AnimationCommon.Binding.RootT[0].propertyName ||
                                                bindings[i].propertyName == AnimationCommon.Binding.RootQ[1].propertyName ||
                                                bindings[i].propertyName == AnimationCommon.Binding.RootQ[2].propertyName)
                                        {
                                            #region Root
                                            MirrorCurve(i);
                                            #endregion
                                        }
                                        else if ((ikIndex = GetIkTIndexFromCurveBinding(bindings[i])) != AnimatorIKIndex.None)
                                        {
                                            #region IKT
                                            var mirrorBindingIndex = GetMirrorBindingIndex(bindings[i]);
                                            if (mirrorBindingIndex >= 0)
                                            {
                                                doneFlag[mirrorBindingIndex] = true;
                                                var dofIndex = AnimationCommon.GetDOFIndex(bindings[i]);
                                                if (dofIndex == 0)
                                                {
                                                    MirrorCurve(i);
                                                }
                                                SwapCurve(i, mirrorBindingIndex);
                                                if (dofIndex == 0)
                                                {
                                                    MirrorCurve(i);
                                                }
                                            }
                                            #endregion
                                        }
                                        else if ((ikIndex = GetIkQIndexFromCurveBinding(bindings[i])) != AnimatorIKIndex.None)
                                        {
                                            #region IKQ
                                            var mirrorBindingIndex = GetMirrorBindingIndex(bindings[i]);
                                            if (mirrorBindingIndex >= 0)
                                            {
                                                doneFlag[mirrorBindingIndex] = true;
                                                SwapCurve(i, mirrorBindingIndex);
                                            }
                                            #endregion
                                        }
                                        else if ((tdofIndex = GetTDOFIndexFromCurveBinding(bindings[i])) != AnimatorTDOFIndex.None)
                                        {
                                            #region TDOF
                                            var dofIndex = AnimationCommon.GetDOFIndex(bindings[i]);
                                            var mirrortdofIndex = AnimatorTDOFMirrorIndexes[(int)tdofIndex];
                                            if (mirrortdofIndex != AnimatorTDOFIndex.None)
                                            {
                                                var mirrorBindingIndex = GetMirrorBindingIndex(bindings[i]);
                                                if (mirrorBindingIndex >= 0)
                                                {
                                                    doneFlag[mirrorBindingIndex] = true;

                                                    var mirror = HumanBonesAnimatorTDOFIndex[(int)AnimatorTDOFIndex2HumanBodyBones[(int)mirrortdofIndex]].mirror;
                                                    if (mirror[dofIndex] < 0)
                                                    {
                                                        MirrorCurve(i);
                                                    }
                                                    SwapCurve(i, mirrorBindingIndex);
                                                    if (mirror[dofIndex] < 0)
                                                    {
                                                        MirrorCurve(i);
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                if (dofIndex == 2)
                                                {
                                                    MirrorCurve(i);
                                                }
                                            }
                                            #endregion
                                        }
                                        #endregion
                                    }
                                    else if (IsSkinnedMeshRendererBlendShapeCurveBinding(bindings[i]))
                                    {
                                        #region BlendShape
                                        var boneIndex = GetBoneIndexFromCurveBinding(bindings[i]);
                                        if (boneIndex >= 0)
                                        {
                                            var mirrorBindingIndex = GetMirrorBindingIndex(bindings[i]);
                                            if (mirrorBindingIndex >= 0)
                                            {
                                                doneFlag[mirrorBindingIndex] = true;
                                                SwapCurve(i, mirrorBindingIndex);
                                            }
                                        }
                                        #endregion
                                    }
                                }
                            }
                            #endregion
                        }
                        #endregion

                        #region FullBakeKeyframe
                        {
                            var curves = new Dictionary<EditorCurveBinding, AnimationCommon.MiniKeyframeList>();
                            AnimationCommon.MiniKeyframeList GetCurve(EditorCurveBinding binding)
                            {
                                if (!curves.TryGetValue(binding, out var curve))
                                {
                                    curve = new AnimationCommon.MiniKeyframeList(lastFrame + 1);
                                    curves.Add(binding, curve);
                                }
                                return curve;
                            }

                            Quaternion[] boneWroteRotation = new Quaternion[Bones.Length];
                            Vector3[] boneWroteEuler = new Vector3[Bones.Length];
                            for (int boneIndex = 0; boneIndex < Bones.Length; boneIndex++)
                            {
                                boneWroteRotation[boneIndex] = BoneSaveOriginalTransforms[boneIndex].localRotation;
                                boneWroteEuler[boneIndex] = boneWroteRotation[boneIndex].eulerAngles;
                            }
                            for (int frame = 0; frame <= lastFrame; frame++)
                            {
                                EditorUtility.DisplayProgressBar("Frame", $"{frame} / {lastFrame}", frame / (float)lastFrame);

                                var time = GetFrameTime(frame);
                                #region Generic
                                {
                                    for (int boneIndex = 0; boneIndex < Bones.Length; boneIndex++)
                                    {
                                        if (IsHuman && HumanoidConflict[boneIndex]) continue;
                                        Vector3 position;
                                        Quaternion rotation;
                                        Vector3 scale;
                                        Vector3 positionMirrorOriginal;
                                        Quaternion rotationMirrorOriginal;
                                        Vector3 scaleMirrorOriginal;
                                        var mbi = MirrorBoneIndexes[boneIndex];
                                        if (mbi >= 0)
                                        {
                                            position = GetMirrorBoneLocalPosition(mbi, GetAnimationValueTransformPosition(mbi, time));
                                            rotation = GetMirrorBoneLocalRotation(mbi, GetAnimationValueTransformRotation(mbi, time));
                                            scale = GetMirrorBoneLocalScale(mbi, GetAnimationValueTransformScale(mbi, time));
                                            positionMirrorOriginal = GetMirrorBoneLocalPosition(mbi, BoneSaveOriginalTransforms[mbi].localPosition);
                                            rotationMirrorOriginal = GetMirrorBoneLocalRotation(mbi, BoneSaveOriginalTransforms[mbi].localRotation);
                                            scaleMirrorOriginal = GetMirrorBoneLocalScale(mbi, BoneSaveOriginalTransforms[mbi].localScale);
                                        }
                                        else
                                        {
                                            position = GetMirrorBoneLocalPosition(boneIndex, GetAnimationValueTransformPosition(boneIndex, time));
                                            rotation = GetMirrorBoneLocalRotation(boneIndex, GetAnimationValueTransformRotation(boneIndex, time));
                                            scale = GetMirrorBoneLocalScale(boneIndex, GetAnimationValueTransformScale(boneIndex, time));
                                            positionMirrorOriginal = GetMirrorBoneLocalPosition(boneIndex, BoneSaveOriginalTransforms[boneIndex].localPosition);
                                            rotationMirrorOriginal = GetMirrorBoneLocalRotation(boneIndex, BoneSaveOriginalTransforms[boneIndex].localRotation);
                                            scaleMirrorOriginal = GetMirrorBoneLocalScale(boneIndex, BoneSaveOriginalTransforms[boneIndex].localScale);
                                        }
                                        bool positionMirrorDifferent = false;
                                        bool rotationMirrorDifferent = false;
                                        bool scaleMirrorDifferent = false;
                                        {
                                            positionMirrorDifferent = Mathf.Abs(positionMirrorOriginal.x - BoneSaveOriginalTransforms[boneIndex].localPosition.x) >= TransformPositionApproximatelyThreshold ||
                                                                        Mathf.Abs(positionMirrorOriginal.y - BoneSaveOriginalTransforms[boneIndex].localPosition.y) >= TransformPositionApproximatelyThreshold ||
                                                                        Mathf.Abs(positionMirrorOriginal.z - BoneSaveOriginalTransforms[boneIndex].localPosition.z) >= TransformPositionApproximatelyThreshold;
                                            {
                                                var eulerAngles = rotationMirrorOriginal.eulerAngles;
                                                var originalEulerAngles = BoneSaveOriginalTransforms[boneIndex].localRotation.eulerAngles;
                                                rotationMirrorDifferent = Mathf.Abs(eulerAngles.x - originalEulerAngles.x) >= TransformRotationApproximatelyThreshold ||
                                                                            Mathf.Abs(eulerAngles.y - originalEulerAngles.y) >= TransformRotationApproximatelyThreshold ||
                                                                            Mathf.Abs(eulerAngles.z - originalEulerAngles.z) >= TransformRotationApproximatelyThreshold;
                                            }
                                            scaleMirrorDifferent = Mathf.Abs(scaleMirrorOriginal.x - BoneSaveOriginalTransforms[boneIndex].localScale.x) >= TransformScaleApproximatelyThreshold ||
                                                                    Mathf.Abs(scaleMirrorOriginal.y - BoneSaveOriginalTransforms[boneIndex].localScale.y) >= TransformScaleApproximatelyThreshold ||
                                                                    Mathf.Abs(scaleMirrorOriginal.z - BoneSaveOriginalTransforms[boneIndex].localScale.z) >= TransformScaleApproximatelyThreshold;
                                        }
                                        if (IsHaveAnimationCurveTransformPosition(boneIndex) || IsHaveAnimationCurveTransformPosition(mbi) || positionMirrorDifferent)
                                        {
                                            for (int dof = 0; dof < 3; dof++)
                                            {
                                                var curve = GetCurve(AnimationCurveBindingTransformPosition(boneIndex, dof));
                                                curve.SetKey(time, position[dof]);
                                            }
                                        }
                                        {
                                            var rotationMode = GetHaveAnimationCurveTransformRotationMode(boneIndex);
                                            var mrotationMode = GetHaveAnimationCurveTransformRotationMode(mbi);
                                            if (rotationMode != URotationCurveInterpolation.Mode.Undefined || mrotationMode != URotationCurveInterpolation.Mode.Undefined || rotationMirrorDifferent)
                                            {
                                                if (rotationMode == URotationCurveInterpolation.Mode.RawEuler)
                                                {
                                                    var eulerAngles = rotation.eulerAngles;
                                                    eulerAngles = AnimationCommon.FixReverseRotationEuler(eulerAngles, boneWroteEuler[boneIndex]);
                                                    boneWroteEuler[boneIndex] = eulerAngles;
                                                    for (int dof = 0; dof < 3; dof++)
                                                    {
                                                        var curve = GetCurve(AnimationCurveBindingTransformRotation(boneIndex, dof, URotationCurveInterpolation.Mode.RawEuler));
                                                        curve.SetKey(time, eulerAngles[dof]);
                                                    }
                                                }
                                                else
                                                {
                                                    rotation = AnimationCommon.FixReverseRotationQuaternion(rotation, boneWroteRotation[boneIndex]);
                                                    boneWroteRotation[boneIndex] = rotation;
                                                    for (int dof = 0; dof < 4; dof++)
                                                    {
                                                        var curve = GetCurve(AnimationCurveBindingTransformRotation(boneIndex, dof, URotationCurveInterpolation.Mode.RawQuaternions));
                                                        curve.SetKey(time, rotation[dof]);
                                                    }
                                                }
                                            }
                                        }
                                        if (IsHaveAnimationCurveTransformScale(boneIndex) || IsHaveAnimationCurveTransformScale(mbi) || scaleMirrorDifferent)
                                        {
                                            if (VAW.EditorSettings.SettingGenericMirrorScale)
                                            {
                                                for (int dof = 0; dof < 3; dof++)
                                                {
                                                    var curve = GetCurve(AnimationCurveBindingTransformScale(boneIndex, dof));
                                                    curve.SetKey(time, scale[dof]);
                                                }
                                            }
                                        }
                                    }
                                }
                                #endregion
                            }

                            #region UpdateTangents
                            foreach (var pair in curves)
                            {
                                var curve = pair.Value.CreateAnimationCurve();
                                if (curve == null || curve.length <= 0) continue;

                                for (int i = 0; i < curve.length; i++)
                                    AnimationCommon.SetKeyframeTangentModeClampedAuto(curve, i);
                                AnimationUtility.SetEditorCurve(newClip, pair.Key, curve);
                            }
                            #endregion
                        }
                        #endregion
                    }
                    #endregion
                }
                else if (toolCreateNewClip_Mode == CreateNewClipMode.Result)
                {
                    #region Result
                    ReadyDefaultPoseClip();

#if VERYANIMATION_TIMELINE
                    if (UAw.GetLinkedWithTimeline())
                    {
                        newClip.frameRate = UAw.GetTimelineFrameRate();
                    }
                    else
#endif
                    {
                        SampleAnimation(0f);
                    }

                    var animatableBindingsTable = new EditorCurveBinding[Bones.Length][];
                    for (int boneIndex = 0; boneIndex < Bones.Length; boneIndex++)
                    {
                        animatableBindingsTable[boneIndex] = AnimationUtility.GetAnimatableBindings(Bones[boneIndex], VAW.GameObject);
                    }

                    var resultFrameCapacity = Mathf.Max(0, toolCreateNewClip_LastFrame - toolCreateNewClip_FirstFrame + 1);
                    var animatableDataDic = new Dictionary<EditorCurveBinding, AnimatableBindingData>();

                    void SetNeedWrite(EditorCurveBinding[] bindings, bool value)
                    {
                        for (int dof = 0; dof < bindings.Length; dof++)
                            animatableDataDic[bindings[dof]].needWrite = value;
                    }

                    var rootT = VAW.GameObject.transform;
                    var rootNodeT = RootMotionBoneIndex >= 0 ? Bones[RootMotionBoneIndex].transform : null;
                    var previousRootQ = Quaternion.identity;

                    for (int frame = toolCreateNewClip_FirstFrame; frame <= toolCreateNewClip_LastFrame; frame++)
                    {
                        EditorUtility.DisplayProgressBar("Frame", $"{frame} / {toolCreateNewClip_LastFrame}", (frame - toolCreateNewClip_FirstFrame) / (float)Mathf.Max(1, toolCreateNewClip_LastFrame - toolCreateNewClip_FirstFrame));

                        var time = UAw.GetFrameTime(frame, newClip);
                        var writeTime = UAw.GetFrameTime(frame - toolCreateNewClip_FirstFrame, newClip);
#if VERYANIMATION_TIMELINE
                        if (UAw.GetLinkedWithTimeline())
                        {
                            UAw.SetTimelineFrame(frame);
                            SampleAnimation();
                        }
                        else
#endif
                        {
                            SetCurrentTime(time);
                            SampleAnimation();
                        }

                        #region Root
                        if (IsHuman)
                        {
                            var rootTValue = TransformPoseSave.StartMatrix.inverse.MultiplyPoint3x4(rootT.position);
                            var rootQValue = Quaternion.Inverse(TransformPoseSave.StartRotation) * rootT.rotation;
                            rootT.SetLocalPositionAndRotation(rootTValue, rootQValue);
                        }
                        else if (AnimatorApplyRootMotion && rootNodeT != null)
                        {
                            rootNodeT.GetPositionAndRotation(out var rootNodePosition, out var rootNodeRotation);
                            rootT.SetPositionAndRotation(TransformPoseSave.StartPosition, TransformPoseSave.StartRotation);
                            rootNodeT.SetPositionAndRotation(rootNodePosition, rootNodeRotation);
                        }
                        #endregion

                        for (int boneIndex = 0; boneIndex < Bones.Length; boneIndex++)
                        {
                            foreach (var binding in animatableBindingsTable[boneIndex])
                            {
                                if (!animatableDataDic.TryGetValue(binding, out var value))
                                {
                                    value = new AnimatableBindingData(resultFrameCapacity);
                                    animatableDataDic[binding] = value;
                                }

                                if (binding.isPPtrCurve)
                                {
                                    if (AnimationUtility.GetObjectReferenceValue(VAW.GameObject, binding, out var data))
                                    {
                                        if (!value.needWrite)
                                        {
                                            var defaultCurve = AnimationUtility.GetObjectReferenceCurve(defaultPoseClip, binding);
                                            if (defaultCurve != null)
                                            {
                                                foreach (var item in defaultCurve)
                                                {
                                                    if (item.value != data)
                                                    {
                                                        value.needWrite = true;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                        value.SetKey(writeTime, data);
                                    }
                                }
                                else
                                {
                                    if (AnimationUtility.GetFloatValue(VAW.GameObject, binding, out var data))
                                    {
                                        if (!value.needWrite)
                                        {
                                            var defaultCurve = AnimationUtility.GetEditorCurve(defaultPoseClip, binding);
                                            if (defaultCurve != null)
                                            {
                                                var defaultValue = defaultCurve.Evaluate(time);
                                                if (defaultValue != data)
                                                {
                                                    value.needWrite = true;
                                                }
                                            }
                                        }
                                        value.SetKey(writeTime, data);
                                    }
                                }
                            }
                        }

                        #region Humanoid
                        if (IsHuman)
                        {
                            HumanPose humanPose = new();
                            HumanPoseHandler.GetHumanPose(ref humanPose);

                            for (int dof = 0; dof < 3; dof++)
                            {
                                var binding = AnimationCommon.Binding.RootT[dof];
                                animatableDataDic[binding].SetKey(writeTime, humanPose.bodyPosition[dof]);
                            }
                            {
                                var rootQ = humanPose.bodyRotation;
                                if (frame > toolCreateNewClip_FirstFrame)
                                {
                                    rootQ = AnimationCommon.FixReverseRotationQuaternion(rootQ, previousRootQ);
                                }
                                previousRootQ = rootQ;
                                for (int dof = 0; dof < 4; dof++)
                                {
                                    var binding = AnimationCommon.Binding.RootQ[dof];
                                    animatableDataDic[binding].SetKey(writeTime, rootQ[dof]);
                                }
                            }
                            for (int muscleIndex = 0; muscleIndex < HumanTrait.MuscleCount; muscleIndex++)
                            {
                                var binding = AnimatorMuscleBindings[muscleIndex];
                                animatableDataDic[binding].SetKey(writeTime, humanPose.muscles[muscleIndex]);
                            }
                        }
                        #endregion
                        #region Generic
                        if (!IsHuman &&
                            AnimatorApplyRootMotion && rootNodeT != null)
                        {
                            var rootTValue = TransformPoseSave.StartMatrix.inverse.MultiplyPoint3x4(rootNodeT.position);
                            var rootQValue = Quaternion.Inverse(TransformPoseSave.StartRotation) * rootNodeT.rotation;

                            for (int dof = 0; dof < 3; dof++)
                            {
                                var binding = AnimationCommon.Binding.RootT[dof];
                                if (!animatableDataDic.TryGetValue(binding, out var value))
                                {
                                    value = new AnimatableBindingData(resultFrameCapacity);
                                    animatableDataDic[binding] = value;
                                }
                                animatableDataDic[binding].SetKey(writeTime, rootTValue[dof]);
                            }
                            for (int dof = 0; dof < 4; dof++)
                            {
                                var binding = AnimationCommon.Binding.RootQ[dof];
                                if (!animatableDataDic.TryGetValue(binding, out var value))
                                {
                                    value = new AnimatableBindingData(resultFrameCapacity);
                                    animatableDataDic[binding] = value;
                                }
                                animatableDataDic[binding].SetKey(writeTime, rootQValue[dof]);
                            }
                        }
                        #endregion
                    }
                    #region Humanoid
                    if (IsHuman)
                    {
                        SetNeedWrite(AnimationCommon.Binding.RootT, true);
                        SetNeedWrite(AnimationCommon.Binding.RootQ, true);
                        SetNeedWrite(AnimationCommon.Binding.MotionT, false);
                        SetNeedWrite(AnimationCommon.Binding.MotionQ, false);
                        for (int muscleIndex = 0; muscleIndex < HumanTrait.MuscleCount; muscleIndex++)
                        {
                            var binding = AnimatorMuscleBindings[muscleIndex];
                            animatableDataDic[binding].needWrite = true;
                        }
                        foreach (var pair in animatableDataDic)
                        {
                            if (!pair.Value.needWrite)
                                continue;
                            if (pair.Key.type == typeof(Transform))
                            {
                                var boneIndex = GetBoneIndexFromCurveBinding(pair.Key);
                                if (boneIndex >= 0)
                                {
                                    if (HumanoidConflict[boneIndex])
                                        pair.Value.needWrite = false;
                                }
                            }
                        }
                    }
                    #endregion
                    #region Generic
                    if (!IsHuman &&
                        AnimatorApplyRootMotion && rootNodeT != null)
                    {
                        SetNeedWrite(AnimationCommon.Binding.RootT, true);
                        SetNeedWrite(AnimationCommon.Binding.RootQ, true);

                        for (int dof = 0; dof < 3; dof++)
                        {
                            var binding = AnimationCurveBindingTransformPosition(0, dof);
                            animatableDataDic[binding].needWrite = false;
                        }
                        for (int dof = 0; dof < 4; dof++)
                        {
                            var binding = AnimationCurveBindingTransformRotation(0, dof, URotationCurveInterpolation.Mode.RawQuaternions);
                            animatableDataDic[binding].needWrite = false;
                        }
                    }
                    #endregion
                    #region Same members
                    foreach (var pair in animatableDataDic)
                    {
                        if (!pair.Value.needWrite)
                            continue;

                        var lastIndex = pair.Key.propertyName.LastIndexOf('.');
                        if (lastIndex >= 0)
                        {
                            var pName = pair.Key.propertyName[..(lastIndex + 1)];
                            foreach (var pairSub in animatableDataDic)
                            {
                                if (pairSub.Value.needWrite)
                                    continue;
                                if (pair.Key == pairSub.Key ||
                                    pair.Key.path != pairSub.Key.path)
                                    continue;
                                if (!pairSub.Key.propertyName.StartsWith(pName, StringComparison.Ordinal))
                                    continue;
                                pairSub.Value.needWrite = true;
                            }
                        }
                    }
                    #endregion

                    {
                        var rDatas = new Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]>(animatableDataDic.Count);
                        var fDatas = new Dictionary<EditorCurveBinding, AnimationCurve>(animatableDataDic.Count);
                        foreach (var pair in animatableDataDic)
                        {
                            if (!pair.Value.needWrite)
                                continue;

                            if (pair.Key.isPPtrCurve)
                            {
                                var refCurve = pair.Value.CreateObjectReferenceKeyframes();
                                if (refCurve != null && refCurve.Length > 0)
                                {
                                    rDatas.Add(pair.Key, refCurve);
                                }
                            }
                            else
                            {
                                var curve = pair.Value.CreateAnimationCurve();
                                if (curve != null && curve.length > 0)
                                {
                                    var valueType = AnimationUtility.GetEditorCurveValueType(VAW.GameObject, pair.Key);
                                    AnimationCommon.SetAnimationCurveTangent(curve, valueType);
                                    fDatas.Add(pair.Key, curve);
                                }
                            }
                        }
                        AnimationCommon.SetObjectReferenceCurves(newClip, rDatas);
                        AnimationCommon.SetEditorCurves(newClip, fDatas);
                    }

                    ResetAnimationMode();
                    #endregion
                }

                bool added = false;
                if (UAw.GetLinkedWithTimeline())
                {
                    #region Timeline
#if VERYANIMATION_TIMELINE
                    Undo.RecordObject(UAw.GetTimelineCurrentDirector(), "Create New Clip");
                    var animationTrack = UAw.GetTimelineAnimationTrack();
                    double? overrideClipStart = null;
                    if (toolCreateNewClip_Mode == CreateNewClipMode.Result)
                    {
                        var overrideTrack = UAw.CreateTimelineOverrideTrack();
                        Assert.IsNotNull(overrideTrack);
                        animationTrack = overrideTrack;
                        overrideClipStart = toolCreateNewClip_FirstFrame / (double)UAw.GetTimelineFrameRate();
                    }
                    Undo.RecordObject(animationTrack, "Create New Clip");
                    var timelineClip = animationTrack.CreateClip(newClip);
                    if (overrideClipStart.HasValue)
                        timelineClip.start = overrideClipStart.Value;
                    timelineClip.displayName = Path.GetFileNameWithoutExtension(clipPath);
                    UAw.ForceRefresh();
                    UAw.EditSequencerClip(timelineClip);
                    var animationPlayableAsset = UAw.GetTimelineAnimationPlayableAsset();
                    animationPlayableAsset.position = timelineAnimationPlayableAsset.position;
                    animationPlayableAsset.rotation = timelineAnimationPlayableAsset.rotation;
                    animationPlayableAsset.useTrackMatchFields = timelineAnimationPlayableAsset.useTrackMatchFields;
                    animationPlayableAsset.matchTargetFields = timelineAnimationPlayableAsset.matchTargetFields;
                    animationPlayableAsset.removeStartOffset = timelineAnimationPlayableAsset.removeStartOffset;
                    animationPlayableAsset.applyFootIK = timelineAnimationPlayableAsset.applyFootIK;
                    animationPlayableAsset.loop = timelineAnimationPlayableAsset.loop;
                    added = true;
                    RequestRelease(true);
#endif
                    #endregion
                }
                else
                {
                    #region Animator
                    if (VAW.Animator != null && VAW.Animator.runtimeAnimatorController != null)
                    {
                        var ac = AnimationCommon.GetAnimatorController(VAW.Animator);
                        AnimationClip virtualClip = null;
                        #region AnimatorOverrideController
                        if (VAW.Animator.runtimeAnimatorController is AnimatorOverrideController owc)
                        {
                            {
                                var srcList = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                                owc.GetOverrides(srcList);
                                foreach (var pair in srcList)
                                {
                                    if (pair.Value == baseClip)
                                    {
                                        virtualClip = pair.Key;
                                        added = true;
                                        break;
                                    }
                                }
                            }
                        }
                        #endregion
                        #region AnimatorControllerLayer
                        if (ac != null)
                        {
                            Undo.RecordObject(ac, "Create New Clip");
                            int findLayerIndex = 0;
                            AnimatorState srcState = null;
                            var layers = ac.layers;
                            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
                            {
                                var effectiveRootStateMachine = UAnimatorController.FindEffectiveRootStateMachine(ac, layerIndex);
                                var effectiveLayerIndex = ArrayUtility.FindIndex(layers, x => x.stateMachine == effectiveRootStateMachine);
                                void FindStateMachine(AnimatorStateMachine stateMachine)
                                {
                                    foreach (var state in stateMachine.states)
                                    {
                                        var motion = ac.GetStateEffectiveMotion(state.state, layerIndex);
                                        if (motion is UnityEditor.Animations.BlendTree)
                                        {
                                            void FindBlendTree(UnityEditor.Animations.BlendTree blendTree)
                                            {
                                                if (blendTree.children == null) return;
                                                var children = blendTree.children;
                                                for (int i = 0; i < children.Length; i++)
                                                {
                                                    if (children[i].motion is UnityEditor.Animations.BlendTree)
                                                    {
                                                        FindBlendTree(children[i].motion as UnityEditor.Animations.BlendTree);
                                                    }
                                                    else
                                                    {
                                                        if (children[i].motion == baseClip || (virtualClip != null && children[i].motion == virtualClip))
                                                        {
                                                            findLayerIndex = effectiveLayerIndex;
                                                            srcState = state.state;
                                                            break;
                                                        }
                                                    }
                                                }
                                            }

                                            FindBlendTree(motion as UnityEditor.Animations.BlendTree);
                                        }
                                        else
                                        {
                                            if (motion == baseClip || (virtualClip != null && motion == virtualClip))
                                            {
                                                findLayerIndex = effectiveLayerIndex;
                                                srcState = state.state;
                                                break;
                                            }
                                        }
                                    }
                                    foreach (var childStateMachine in stateMachine.stateMachines)
                                    {
                                        FindStateMachine(childStateMachine.stateMachine);
                                    }
                                }

                                FindStateMachine(effectiveRootStateMachine);
                            }
                            var animatorState = ac.AddMotion(newClip, findLayerIndex);
                            if (srcState != null)
                            {
                                animatorState.behaviours = srcState.behaviours;
                                animatorState.transitions = srcState.transitions;
                                animatorState.mirrorParameterActive = srcState.mirrorParameterActive;
                                animatorState.cycleOffsetParameterActive = srcState.cycleOffsetParameterActive;
                                animatorState.speedParameterActive = srcState.speedParameterActive;
                                animatorState.mirrorParameter = srcState.mirrorParameter;
                                animatorState.cycleOffsetParameter = srcState.cycleOffsetParameter;
                                animatorState.speedParameter = srcState.speedParameter;
                                animatorState.tag = srcState.tag;
                                animatorState.writeDefaultValues = srcState.writeDefaultValues;
                                animatorState.iKOnFeet = srcState.iKOnFeet;
                                animatorState.mirror = srcState.mirror;
                                animatorState.cycleOffset = srcState.cycleOffset;
                                animatorState.speed = srcState.speed;
                                animatorState.motion = newClip;
                                animatorState.timeParameter = srcState.timeParameter;
                                animatorState.timeParameterActive = srcState.timeParameterActive;
                                added = true;
                            }
                        }
                        #endregion
                    }
                    #endregion
                    #region Animation
                    if (VAW.Animation != null)
                    {
                        Undo.RecordObject(VAW.Animation, "Create New Clip");
                        var animations = AnimationUtility.GetAnimationClips(VAW.GameObject);
                        ArrayUtility.Add(ref animations, newClip);
                        AnimationUtility.SetAnimationClips(VAW.Animation, animations);
                        added = true;
                    }
                    #endregion

                    if (!added)
                        Debug.LogWarningFormat(Language.GetText(Language.Help.LogAnimationClipAddError), newClip);

                    UAw.ForceRefresh();
                    SetCurrentClip(newClip);

                    EditorCommon.PingObject(newClip);
                }
            }
            finally
            {
                SetCurrentTime(saveCurrentTime);
                if (VAW.Animator != null && VAW.Animator.applyRootMotion != saveApplyRootMotion)
                    VAW.Animator.applyRootMotion = saveApplyRootMotion;
                EditorUtility.ClearProgressBar();
            }

            ToolsCommonAfter();
        }
        private void ToolsCreateNewKeyframe(AnimationClip clip)
        {
            if (!ToolsCommonBefore(clip, "Create New Keyframe")) return;

            var firstFrame = Mathf.Clamp(toolCreateNewKeyframe_FirstFrame, 0, GetLastFrame());
            var lastFrame = Mathf.Clamp(toolCreateNewKeyframe_LastFrame, firstFrame, GetLastFrame());

            try
            {
                int progressIndex = 0;
                int progressTotal = 1;
                EditorUtility.DisplayProgressBar("Create New Keyframe", "", progressIndex++ / (float)progressTotal);

                bool selectRoot = SelectionBones != null && SelectionBones.Contains(0);
                var humanoidIndexes = SelectionGameObjectsHumanoidIndex();
                var boneIndexes = SelectionGameObjectsOtherHumanoidBoneIndex();
                List<float> times = new();
                {
                    int interval = 0;
                    for (int frame = firstFrame; frame <= lastFrame; frame++)
                    {
                        bool set = frame == firstFrame || frame == lastFrame || toolCreateNewKeyframe_IntervalFrame == 0;
                        if (!set)
                        {
                            set = ++interval >= toolCreateNewKeyframe_IntervalFrame;
                        }
                        if (!set) continue;
                        interval = 0;
                        var time = GetFrameTime(frame);
                        times.Add(time);
                    }
                }

                if (IsHuman)
                {
                    if (selectRoot)
                    {
                        if (toolCreateNewKeyframe_AnimatorRootT)
                        {
                            var saveValues = new Dictionary<float, Vector3>();
                            foreach (var time in times)
                                saveValues.Add(time, GetAnimationValueAnimatorRootT(time));
                            foreach (var pair in saveValues)
                                SetAnimationValueAnimatorRootT(pair.Value, pair.Key);
                        }
                        if (toolCreateNewKeyframe_AnimatorRootQ)
                        {
                            var saveValues = new Dictionary<float, Quaternion>();
                            foreach (var time in times)
                                saveValues.Add(time, GetAnimationValueAnimatorRootQ(time));
                            foreach (var pair in saveValues)
                                SetAnimationValueAnimatorRootQ(pair.Value, pair.Key);
                        }
                    }
                    foreach (var humanoidIndex in humanoidIndexes)
                    {
                        if (toolCreateNewKeyframe_AnimatorMuscle)
                        {
                            for (int dof = 0; dof < 3; dof++)
                            {
                                var muscleIndex = HumanTrait.MuscleFromBone((int)humanoidIndex, dof);
                                if (muscleIndex < 0)
                                    continue;
                                var saveValues = new Dictionary<float, float>();
                                foreach (var time in times)
                                    saveValues.Add(time, GetAnimationValueAnimatorMuscle(muscleIndex, time));
                                foreach (var pair in saveValues)
                                    SetAnimationValueAnimatorMuscle(muscleIndex, pair.Value, pair.Key);
                            }
                        }
                        if (HumanoidHasTDoF && toolCreateNewKeyframe_AnimatorTDOF)
                        {
                            if (HumanBonesAnimatorTDOFIndex[(int)humanoidIndex] != null)
                            {
                                var tdof = HumanBonesAnimatorTDOFIndex[(int)humanoidIndex].index;
                                var saveValues = new Dictionary<float, Vector3>();
                                foreach (var time in times)
                                    saveValues.Add(time, GetAnimationValueAnimatorTDOF(tdof, time));
                                foreach (var pair in saveValues)
                                    SetAnimationValueAnimatorTDOF(tdof, pair.Value, pair.Key);
                            }
                        }
                    }
                }

                foreach (var boneIndex in boneIndexes)
                {
                    if (IsConflictBone(boneIndex))
                        continue;

                    if (toolCreateNewKeyframe_TransformPosition)
                    {
                        var saveValues = new Dictionary<float, Vector3>();
                        foreach (var time in times)
                            saveValues.Add(time, GetAnimationValueTransformPosition(boneIndex, time));
                        foreach (var pair in saveValues)
                            SetAnimationValueTransformPosition(boneIndex, pair.Value, pair.Key);
                    }
                    if (toolCreateNewKeyframe_TransformRotation)
                    {
                        var saveValues = new Dictionary<float, Quaternion>();
                        foreach (var time in times)
                            saveValues.Add(time, GetAnimationValueTransformRotation(boneIndex, time));
                        foreach (var pair in saveValues)
                            SetAnimationValueTransformRotation(boneIndex, pair.Value, pair.Key);
                    }
                    if (toolCreateNewKeyframe_TransformScale)
                    {
                        var saveValues = new Dictionary<float, Vector3>();
                        foreach (var time in times)
                            saveValues.Add(time, GetAnimationValueTransformScale(boneIndex, time));
                        foreach (var pair in saveValues)
                            SetAnimationValueTransformScale(boneIndex, pair.Value, pair.Key);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ToolsCommonAfter();
        }
        private void ToolsRotationCurveInterpolation(AnimationClip clip, RotationCurveInterpolationMode rotationCurveInterpolationMode)
        {
            if (!ToolsFixOverRotationCurve(clip)) return;

            if (!ToolsCommonBefore(clip, "RotationCurveInterpolation")) return;

            try
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                int progressIndex = 0;
                int progressTotal = bindings.Length + 1;

                {
                    List<EditorCurveBinding> convertBindings = new();
                    for (int i = 0; i < bindings.Length; i++)
                    {
                        EditorUtility.DisplayProgressBar("Read", AnimationCommon.GetBindingDisplayName(bindings[i]), progressIndex++ / (float)progressTotal);
                        if (!IsTransformRotationCurveBinding(bindings[i])) continue;
                        var mode = URotationCurveInterpolation.GetModeFromCurveData(bindings[i]);
                        if (convertBindings.FindIndex((x) => x.path == bindings[i].path) < 0)
                        {
                            var boneIndex = GetBoneIndexFromCurveBinding(bindings[i]);
                            if (boneIndex >= 0)
                            {
                                switch (mode)
                                {
                                    case URotationCurveInterpolation.Mode.RawQuaternions:
                                        if (rotationCurveInterpolationMode != RotationCurveInterpolationMode.Quaternion)
                                        {
                                            for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                                                convertBindings.Add(AnimationCurveBindingTransformRotation(boneIndex, dofIndex, URotationCurveInterpolation.Mode.Baked));
                                            for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                                                convertBindings.Add(AnimationCurveBindingTransformRotation(boneIndex, dofIndex, URotationCurveInterpolation.Mode.NonBaked));
                                        }
                                        else
                                        {
                                            for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                                                convertBindings.Add(AnimationCurveBindingTransformRotation(boneIndex, dofIndex, URotationCurveInterpolation.Mode.Baked));
                                        }
                                        break;
                                    case URotationCurveInterpolation.Mode.RawEuler:
                                        if (rotationCurveInterpolationMode != RotationCurveInterpolationMode.EulerAngles)
                                        {
                                            for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                                                convertBindings.Add(AnimationCurveBindingTransformRotation(boneIndex, dofIndex, URotationCurveInterpolation.Mode.RawEuler));
                                        }
                                        break;
                                }
                            }
                        }
                    }
                    {
                        URotationCurveInterpolation.Mode mode = (URotationCurveInterpolation.Mode)(-1);
                        switch (rotationCurveInterpolationMode)
                        {
                            case RotationCurveInterpolationMode.Quaternion: mode = URotationCurveInterpolation.Mode.NonBaked; break;
                            case RotationCurveInterpolationMode.EulerAngles: mode = URotationCurveInterpolation.Mode.RawEuler; break;
                            default: Assert.IsTrue(false); break;
                        }
                        EditorUtility.DisplayProgressBar("Convert", "", progressIndex++ / (float)progressTotal);
                        if (convertBindings.Count > 0)
                            URotationCurveInterpolation.SetInterpolation(clip, convertBindings.ToArray(), mode);
                    }
                }
                #region FixReverseRotation
                if (rotationCurveInterpolationMode == RotationCurveInterpolationMode.EulerAngles)
                {
                    bindings = AnimationUtility.GetCurveBindings(clip);
                    foreach (var binding in bindings)
                    {
                        if (!IsTransformRotationCurveBinding(binding)) continue;
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        if (AnimationCommon.FixReverseRotationEuler(curve))
                            AnimationUtility.SetEditorCurve(clip, binding, curve);
                    }
                }
                #endregion
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ToolsCommonAfter();
        }
        private void ToolsKeyframeReduction(AnimationClip clip)
        {
            if (!ToolsCommonBefore(clip, "KeyframeReduction")) return;

            ToolsRotationCurveInterpolation(clip, RotationCurveInterpolationMode.Quaternion);

            AnimationClip tmpClip = null;
            GameObject tmpObject = null;

            using (new VeryAnimationWindow.CustomAssetModificationProcessor.PauseScope())
            {
                try
                {
                    var fileName = EditorCommon.GetSafeFileName($"{clip.name}_tmp");
                    var assetPath = $"{EditorCommon.GetAssetPath(clip)}/{fileName}.dae";
                    assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
                    var path = Application.dataPath + assetPath["Assets".Length..];

                    if (!TransformPoseSave.ResetPrefabTransform())
                        TransformPoseSave.ResetOriginalTransform();
                    if (!BlendShapeWeightSave.ResetPrefabWeight())
                        BlendShapeWeightSave.ResetOriginalWeight();

                    tmpClip = AnimationClip.Instantiate(clip);
                    tmpClip.hideFlags |= HideFlags.HideAndDontSave;
                    tmpObject = AnimationCommon.InstantiateForPreview(VAW.GameObject);

                    AnimationCommon.AddMissingTransforms(tmpObject, tmpClip);
                    var otherCurveDic = AnimationCommon.ConvertForKeyframeReduction(tmpObject, tmpClip);

                    DaeExporter exporter = new()
                    {
                        settings_activeOnly = false,
                        settings_exportMesh = false,
                        settings_iKOnFeet = false,
                        settings_animationRigging = false,
                        settings_animationType = IsHuman ? ModelImporterAnimationType.Human : (VAW.Animator != null ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.Legacy),
                        settings_motionNodePath = RootMotionBoneIndex >= 0 ? BonePaths[RootMotionBoneIndex] : null,
                    };
                    if (VAW.Animator != null)
                        exporter.settings_avatar = VAW.Animator.avatar;
                    var result = exporter.Export(path, tmpObject.GetComponentsInChildren<Transform>(true), new AnimationClip[] { tmpClip });
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
                                    modelImporter.animationRotationError = toolKeyframeReduction_RotationError;
                                    modelImporter.animationPositionError = toolKeyframeReduction_PositionError;
                                    modelImporter.animationScaleError = toolKeyframeReduction_ScaleError;
                                    modelImporter.SaveAndReimport();
                                }
                                reductionClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(subAssetPath);
                            }
                            if (reductionClip != null)
                            {
                                var datas = AnimationCommon.ImportByKeyframeReduction(clip, reductionClip, otherCurveDic,
                                                                                        toolKeyframeReduction_EnableAnimator,
                                                                                        toolKeyframeReduction_EnableAnimatorRootAndIKGoal,
                                                                                        toolKeyframeReduction_EnableTransform,
                                                                                        toolKeyframeReduction_EnableOther);
                                AnimationCommon.SetEditorCurves(clip, datas, true);
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
                    if (toolKeyframeReduction_EnableOther)
                    {
                        AnimationCommon.SimpleReductionKeyframe(clip, VAW.GameObject);
                    }
                }
                finally
                {
                    if (tmpClip != null)
                        AnimationClip.DestroyImmediate(tmpClip);
                    if (tmpObject != null)
                        GameObject.DestroyImmediate(tmpObject);
                }
            }

            ToolsCommonAfter();
        }
        private void ToolsEnsureQuaternionContinuity(AnimationClip clip)
        {
            if (!ToolsCommonBefore(clip, "EnsureQuaternionContinuity")) return;

            {
                clip.EnsureQuaternionContinuity();
            }

            ToolsCommonAfter();
        }
        private void ToolsCleanup(AnimationClip clip)
        {
            if (!ToolsCommonBefore(clip, "Cleanup")) return;

            try
            {
                var removeCurveDatas = new Dictionary<EditorCurveBinding, AnimationCurve>();
                var removeReferenceCurveDatas = new Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]>();
                void RemoveCurve(EditorCurveBinding binding) => removeCurveDatas[binding] = null;
                void RemoveCurves(EditorCurveBinding[] bindings)
                {
                    foreach (var binding in bindings)
                        RemoveCurve(binding);
                }
                void RemoveReferenceCurve(EditorCurveBinding binding) => removeReferenceCurveDatas[binding] = null;
                void FlushRemoveCurves()
                {
                    AnimationCommon.SetEditorCurves(clip, removeCurveDatas);
                    removeCurveDatas.Clear();
                }
                void FlushRemoveReferenceCurves()
                {
                    AnimationCommon.SetObjectReferenceCurves(clip, removeReferenceCurveDatas);
                    removeReferenceCurveDatas.Clear();
                }
                void RemoveMuscleCurve(HumanBodyBones hi)
                {
                    for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                    {
                        var mi = HumanTrait.MuscleFromBone((int)hi, dofIndex);
                        if (mi < 0) continue;
                        RemoveCurve(AnimatorMuscleBindings[mi]);
                    }
                }
                void RemoveTDofCurve(AnimatorTDOFIndex tdofIndex)
                {
                    for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                        RemoveCurve(AnimatorTDOFBindings[(int)tdofIndex][dofIndex]);
                }
                var bindings = AnimationUtility.GetCurveBindings(clip);

                int progressIndex = 0;
                int progressTotal = 17;

                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                if (toolCleanup_RemoveRoot)
                {
                    RemoveCurves(AnimationCommon.Binding.RootT);
                    RemoveCurves(AnimationCommon.Binding.RootQ);
                }
                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                if (toolCleanup_RemoveIK)
                {
                    for (int ikIndex = 0; ikIndex < (int)AnimatorIKIndex.Total; ikIndex++)
                    {
                        RemoveCurves(AnimatorIkTBindings[(int)ikIndex]);
                        RemoveCurves(AnimatorIkQBindings[(int)ikIndex]);
                    }
                }
                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                if (toolCleanup_RemoveTDOF)
                {
                    for (int tdofIndex = 0; tdofIndex < (int)AnimatorTDOFIndex.Total; tdofIndex++)
                        RemoveTDofCurve((AnimatorTDOFIndex)tdofIndex);
                }
                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                if (toolCleanup_RemoveMotion)
                {
                    RemoveCurves(AnimationCommon.Binding.MotionT);
                    RemoveCurves(AnimationCommon.Binding.MotionQ);
                }
                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                if (toolCleanup_RemoveFinger)
                {
                    for (var hi = HumanBodyBones.LeftThumbProximal; hi <= HumanBodyBones.RightLittleDistal; hi++)
                        RemoveMuscleCurve(hi);
                }
                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                if (toolCleanup_RemoveEyes)
                {
                    RemoveMuscleCurve(HumanBodyBones.LeftEye);
                    RemoveMuscleCurve(HumanBodyBones.RightEye);
                }
                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                if (toolCleanup_RemoveJaw)
                {
                    RemoveMuscleCurve(HumanBodyBones.Jaw);
                }
                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                if (toolCleanup_RemoveToes)
                {
                    RemoveMuscleCurve(HumanBodyBones.LeftToes);
                    RemoveMuscleCurve(HumanBodyBones.RightToes);
                }
                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                if (toolCleanup_RemoveTransformPosition || toolCleanup_RemoveTransformRotation || toolCleanup_RemoveTransformScale)
                {
                    foreach (var binding in bindings)
                    {
                        if (binding.type == typeof(Transform))
                        {
                            if ((toolCleanup_RemoveTransformPosition && binding.propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal)) ||
                                (toolCleanup_RemoveTransformRotation && (binding.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal) || binding.propertyName.StartsWith("localEulerAngles", StringComparison.Ordinal))) ||
                                (toolCleanup_RemoveTransformScale && binding.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal)))
                            {
                                RemoveCurve(binding);
                            }
                        }
                    }
                }
                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                if (toolCleanup_RemoveBlendShape)
                {
                    foreach (var binding in bindings)
                    {
                        if (IsSkinnedMeshRendererBlendShapeCurveBinding(binding))
                        {
                            RemoveCurve(binding);
                        }
                    }
                }
                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                FlushRemoveCurves();
                if (toolCleanup_RemoveObjectReference)
                {
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    {
                        RemoveReferenceCurve(binding);
                    }
                }
                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                if (toolCleanup_RemoveEvent)
                {
                    AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
                }
                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                FlushRemoveCurves();
                FlushRemoveReferenceCurves();
                if (toolCleanup_RemoveMissing)
                {
                    foreach (var binding in UAw.GetMissingCurveBindings())
                    {
                        if (!binding.isPPtrCurve)
                            RemoveCurve(binding);
                        else
                            RemoveReferenceCurve(binding);
                    }
                }
                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                if (toolCleanup_RemoveHumanoidConflict && IsHuman)
                {
                    HashSet<string> paths = new(StringComparer.Ordinal);
                    for (int i = 0; i < Bones.Length; i++)
                    {
                        if (HumanoidConflict[i])
                        {
                            paths.Add(BonePaths[i]);
                        }
                    }
                    foreach (var binding in bindings)
                    {
                        if (binding.type != typeof(Transform)) continue;
                        if (!paths.Contains(binding.path)) continue;
                        RemoveCurve(binding);
                    }
                }
                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                if (toolCleanup_RemoveRootMotionConflict && RootMotionBoneIndex >= 0)
                {
                    foreach (var binding in bindings)
                    {
                        if (!IsTransformPositionCurveBinding(binding) && !IsTransformRotationCurveBinding(binding)) continue;
                        var boneIndex = GetBoneIndexFromCurveBinding(binding);
                        if (boneIndex == 0)
                            RemoveCurve(binding);
                    }
                }
                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                FlushRemoveCurves();
                if (toolCleanup_RemoveUnnecessary)
                {
                    ToolsReductionCurve(clip);
                }
                EditorUtility.DisplayProgressBar("Cleanup", "", progressIndex++ / (float)progressTotal);
                if (toolCleanup_RemoveAvatarMaskDisable && toolCleanup_RemoveAvatarMask != null)
                {
                    if (!toolCleanup_RemoveAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root))
                    {
                        RemoveCurves(AnimationCommon.Binding.RootT);
                        RemoveCurves(AnimationCommon.Binding.RootQ);
                        RemoveCurves(AnimationCommon.Binding.MotionT);
                        RemoveCurves(AnimationCommon.Binding.MotionQ);
                    }
                    if (!toolCleanup_RemoveAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body))
                    {
                        RemoveMuscleCurve(HumanBodyBones.UpperChest);
                        RemoveMuscleCurve(HumanBodyBones.Chest);
                        RemoveMuscleCurve(HumanBodyBones.Spine);
                        RemoveTDofCurve(AnimatorTDOFIndex.UpperChest);
                        RemoveTDofCurve(AnimatorTDOFIndex.Chest);
                        RemoveTDofCurve(AnimatorTDOFIndex.Spine);
                    }
                    if (!toolCleanup_RemoveAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Head))
                    {
                        RemoveMuscleCurve(HumanBodyBones.Neck);
                        RemoveMuscleCurve(HumanBodyBones.Head);
                        RemoveMuscleCurve(HumanBodyBones.LeftEye);
                        RemoveMuscleCurve(HumanBodyBones.RightEye);
                        RemoveMuscleCurve(HumanBodyBones.Jaw);
                        RemoveTDofCurve(AnimatorTDOFIndex.Neck);
                        RemoveTDofCurve(AnimatorTDOFIndex.Head);
                    }
                    if (!toolCleanup_RemoveAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg))
                    {
                        RemoveMuscleCurve(HumanBodyBones.LeftUpperLeg);
                        RemoveMuscleCurve(HumanBodyBones.LeftLowerLeg);
                        RemoveMuscleCurve(HumanBodyBones.LeftFoot);
                        RemoveMuscleCurve(HumanBodyBones.LeftToes);
                        RemoveTDofCurve(AnimatorTDOFIndex.LeftUpperLeg);
                        RemoveTDofCurve(AnimatorTDOFIndex.LeftLowerLeg);
                        RemoveTDofCurve(AnimatorTDOFIndex.LeftFoot);
                        RemoveTDofCurve(AnimatorTDOFIndex.LeftToes);
                    }
                    if (!toolCleanup_RemoveAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg))
                    {
                        RemoveMuscleCurve(HumanBodyBones.RightUpperLeg);
                        RemoveMuscleCurve(HumanBodyBones.RightLowerLeg);
                        RemoveMuscleCurve(HumanBodyBones.RightFoot);
                        RemoveMuscleCurve(HumanBodyBones.RightToes);
                        RemoveTDofCurve(AnimatorTDOFIndex.RightUpperLeg);
                        RemoveTDofCurve(AnimatorTDOFIndex.RightLowerLeg);
                        RemoveTDofCurve(AnimatorTDOFIndex.RightFoot);
                        RemoveTDofCurve(AnimatorTDOFIndex.RightToes);
                    }
                    if (!toolCleanup_RemoveAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm))
                    {
                        RemoveMuscleCurve(HumanBodyBones.LeftShoulder);
                        RemoveMuscleCurve(HumanBodyBones.LeftUpperArm);
                        RemoveMuscleCurve(HumanBodyBones.LeftLowerArm);
                        RemoveMuscleCurve(HumanBodyBones.LeftHand);
                        RemoveTDofCurve(AnimatorTDOFIndex.LeftShoulder);
                        RemoveTDofCurve(AnimatorTDOFIndex.LeftUpperArm);
                        RemoveTDofCurve(AnimatorTDOFIndex.LeftLowerArm);
                        RemoveTDofCurve(AnimatorTDOFIndex.LeftHand);
                    }
                    if (!toolCleanup_RemoveAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm))
                    {
                        RemoveMuscleCurve(HumanBodyBones.RightShoulder);
                        RemoveMuscleCurve(HumanBodyBones.RightUpperArm);
                        RemoveMuscleCurve(HumanBodyBones.RightLowerArm);
                        RemoveMuscleCurve(HumanBodyBones.RightHand);
                        RemoveTDofCurve(AnimatorTDOFIndex.RightShoulder);
                        RemoveTDofCurve(AnimatorTDOFIndex.RightUpperArm);
                        RemoveTDofCurve(AnimatorTDOFIndex.RightLowerArm);
                        RemoveTDofCurve(AnimatorTDOFIndex.RightHand);
                    }
                    if (!toolCleanup_RemoveAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers))
                    {
                        for (var hi = HumanBodyBones.LeftThumbProximal; hi <= HumanBodyBones.LeftLittleDistal; hi++)
                            RemoveMuscleCurve(hi);
                    }
                    if (!toolCleanup_RemoveAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers))
                    {
                        for (var hi = HumanBodyBones.RightThumbProximal; hi <= HumanBodyBones.RightLittleDistal; hi++)
                            RemoveMuscleCurve(hi);
                    }
                    if (!toolCleanup_RemoveAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK))
                    {
                        RemoveCurves(AnimatorIkTBindings[(int)AnimatorIKIndex.LeftFoot]);
                        RemoveCurves(AnimatorIkQBindings[(int)AnimatorIKIndex.LeftFoot]);
                    }
                    if (!toolCleanup_RemoveAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK))
                    {
                        RemoveCurves(AnimatorIkTBindings[(int)AnimatorIKIndex.RightFoot]);
                        RemoveCurves(AnimatorIkQBindings[(int)AnimatorIKIndex.RightFoot]);
                    }
                    if (!toolCleanup_RemoveAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK))
                    {
                        RemoveCurves(AnimatorIkTBindings[(int)AnimatorIKIndex.LeftHand]);
                        RemoveCurves(AnimatorIkQBindings[(int)AnimatorIKIndex.LeftHand]);
                    }
                    if (!toolCleanup_RemoveAvatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK))
                    {
                        RemoveCurves(AnimatorIkTBindings[(int)AnimatorIKIndex.RightHand]);
                        RemoveCurves(AnimatorIkQBindings[(int)AnimatorIKIndex.RightHand]);
                    }
                    for (int i = 0; i < toolCleanup_RemoveAvatarMask.transformCount; i++)
                    {
                        if (!toolCleanup_RemoveAvatarMask.GetTransformActive(i))
                        {
                            var path = toolCleanup_RemoveAvatarMask.GetTransformPath(i);
                            foreach (var binding in bindings)
                            {
                                if (binding.path == path)
                                    RemoveCurve(binding);
                            }
                        }
                    }
                }
                FlushRemoveCurves();
                FlushRemoveReferenceCurves();

            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ToolsCommonAfter();
        }
        private void ToolsFixErrors(AnimationClip clip)
        {
            if (!ToolsFixOverRotationCurve(clip)) return;

            if (!ToolsCommonBefore(clip, "Fix Errors")) return;

            try
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);

                int progressIndex = 0;
                int progressTotal = bindings.Length;

                foreach (var binding in bindings)
                {
                    EditorUtility.DisplayProgressBar("Fix Errors", "", progressIndex++ / (float)progressTotal);

                    #region There must be at least two keyframes. If not, an Assert will occur.[AnimationUtility.GetEditorCurve]
                    if (IsTransformRotationCurveBinding(binding) && URotationCurveInterpolation.GetModeFromCurveData(binding) == URotationCurveInterpolation.Mode.RawQuaternions)
                    {
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        if (curve.length <= 1)
                        {
                            void ErrorAvoidance()
                            {
                                while (curve.length < 2)
                                {
                                    float addTime;
                                    if (AnimationCommon.FindKeyframeAtTime(curve, 0f) < 0) addTime = 0f;
                                    else if (clip.length != 0f) addTime = clip.length;
                                    else addTime = 1f;
                                    if (AnimationCommon.AddKeyframe(curve, addTime, curve.Evaluate(addTime)) < 0)
                                        break;
                                }
                            }
                            ErrorAvoidance();
                            AnimationUtility.SetEditorCurve(clip, binding, curve);
                            Debug.LogWarningFormat(Language.GetText(Language.Help.LogFixErrors), binding.path, binding.propertyName);
                        }
                    }
                    #endregion
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ToolsCommonAfter();
        }
        private void ToolsAdditiveReferencePose(AnimationClip clip, bool isAllClip)
        {
            if (!ToolsCommonBefore(clip, "Additive Reference Pose")) return;

            AnimationClip[] allClips;
            if (!isAllClip)
                allClips = new AnimationClip[] { clip };
            else
                allClips = AnimationCommon.GetUniqueAnimationClips(VAW.GameObject);

            try
            {
                Undo.RecordObjects(allClips, "Additive Reference Pose");

                HashSet<EditorCurveBinding> additiveReferencePoseBindingSet = null;
                if (toolAdditiveReferencePose_Has)
                    additiveReferencePoseBindingSet = new HashSet<EditorCurveBinding>(AnimationUtility.GetCurveBindings(toolAdditiveReferencePose_Clip));

                int progressIndex = 0;
                int progressTotal = allClips.Length;
                foreach (var c in allClips)
                {
                    EditorUtility.DisplayProgressBar("Additive Reference Pose", "", progressIndex++ / (float)progressTotal);

                    if ((c.hideFlags & HideFlags.NotEditable) != HideFlags.None)
                    {
                        EditorCommon.ShowNotification("Read-Only");
                        Debug.LogErrorFormat(Language.GetText(Language.Help.LogAnimationClipReadOnlyError), c.name);
                        continue;
                    }

                    if (toolAdditiveReferencePose_Has)
                    {
                        var bindings = AnimationUtility.GetCurveBindings(c);
                        var missingBindings = bindings.Where(x => !additiveReferencePoseBindingSet.Contains(x)).ToArray();
                        if (missingBindings.Length > 0)
                        {
                            if (toolAdditiveReferencePose_Clip.hideFlags.HasFlag(HideFlags.NotEditable))
                            {
                                Debug.LogFormat(Language.GetText(Language.Help.LogToolsAdditiveReferencePoseMissingCurvesError), toolAdditiveReferencePose_Clip.name);
                            }
                            else
                            {
                                Undo.RecordObject(toolAdditiveReferencePose_Clip, "Additive Reference Pose");

                                foreach (var binding in missingBindings)
                                {
                                    var baseCurve = AnimationUtility.GetEditorCurve(c, binding);
                                    var curve = new AnimationCurve(new Keyframe[] { new(0f, baseCurve.Evaluate(0f)) });
                                    AnimationUtility.SetEditorCurve(toolAdditiveReferencePose_Clip, binding, curve);
                                    additiveReferencePoseBindingSet.Add(binding);
                                }
                                Debug.LogFormat(Language.GetText(Language.Help.LogToolsAdditiveReferencePoseAddMissingCurves), toolAdditiveReferencePose_Clip.name);
                            }
                        }

                        AnimationUtility.SetAdditiveReferencePose(c, toolAdditiveReferencePose_Clip, toolAdditiveReferencePose_Time);
                    }
                    else
                    {
                        AnimationUtility.SetAdditiveReferencePose(c, null, 0f);
                    }

                    Debug.LogFormat(Language.GetText(Language.Help.LogToolsAdditiveReferencePoseChanged), c.name);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ToolsCommonAfter();
        }
        private void ToolsAnimCompression(AnimationClip clip, bool isAllClip)
        {
            if (!ToolsCommonBefore(clip, "Anim Compression")) return;

            AnimationClip[] allClips;
            if (!isAllClip)
                allClips = new AnimationClip[] { clip };
            else
                allClips = AnimationCommon.GetUniqueAnimationClips(VAW.GameObject);

            try
            {
                Undo.RecordObjects(allClips, "Anim Compression");

                int progressIndex = 0;
                int progressTotal = allClips.Length;
                foreach (var c in allClips)
                {
                    EditorUtility.DisplayProgressBar("Anim Compression", "", progressIndex++ / (float)progressTotal);

                    if ((c.hideFlags & HideFlags.NotEditable) != HideFlags.None)
                    {
                        EditorCommon.ShowNotification("Read-Only");
                        Debug.LogErrorFormat(Language.GetText(Language.Help.LogAnimationClipReadOnlyError), c.name);
                        continue;
                    }

                    bool changed = false;

                    var so = new SerializedObject(c);
                    {
                        var sp = so.FindProperty("m_Compressed");
                        if (sp.boolValue != toolAnimCompression_Compressed)
                        {
                            sp.boolValue = toolAnimCompression_Compressed;
                            changed = true;
                        }
                    }
                    if (!c.legacy)
                    {
                        var sp = so.FindProperty("m_UseHighQualityCurve");
                        if (sp.boolValue != toolAnimCompression_UseHighQualityCurve)
                        {
                            sp.boolValue = toolAnimCompression_UseHighQualityCurve;
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        so.ApplyModifiedProperties();
                        Debug.LogFormat(Language.GetText(Language.Help.LogToolsAnimCompressionChanged), c.name);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ToolsCommonAfter();
        }
        private void ToolsExport()
        {
            string path = EditorUtility.SaveFilePanel("Export",
                                                        EditorCommon.GetAssetPath(CurrentClip),
                                                        $"{EditorCommon.GetSafeFileName(VAW.GameObject.name)}.dae", "dae");
            if (string.IsNullOrEmpty(path))
                return;

            if (!TransformPoseSave.ResetPrefabTransform())
                TransformPoseSave.ResetOriginalTransform();
            if (!BlendShapeWeightSave.ResetPrefabWeight())
                BlendShapeWeightSave.ResetOriginalWeight();

            var transforms = new Transform[Bones.Length];
            for (int i = 0; i < Bones.Length; i++)
                transforms[i] = Bones[i].transform;

            AnimationClip[] clips = null;
            switch (toolExport_AnimationMode)
            {
                case ExportAnimationMode.None:
                    clips = null;
                    break;
                case ExportAnimationMode.CurrentClip:
                    clips = new AnimationClip[] { CurrentClip };
                    break;
                case ExportAnimationMode.AllClips:
                    clips = AnimationCommon.GetUniqueAnimationClips(VAW.GameObject);
                    break;
            }

            using (new VeryAnimationWindow.CustomAssetModificationProcessor.PauseScope())
            {
                DaeExporter exporter = new()
                {
                    settings_activeOnly = toolExport_ActiveOnly,
                    settings_exportMesh = toolExport_Mesh,
                    settings_iKOnFeet = toolExport_BakeFootIK,
                    settings_animationRigging = toolExport_BakeAnimationRigging,
                    settings_animationType = IsHuman ? ModelImporterAnimationType.Human : (VAW.Animator != null ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.Legacy),
                    settings_motionNodePath = RootMotionBoneIndex >= 0 ? BonePaths[RootMotionBoneIndex] : null,
                };
                if (VAW.Animator != null)
                    exporter.settings_avatar = VAW.Animator.avatar;
                exporter.Export(path, transforms, clips);
            }
            SetUpdateSampleAnimation();
            UAw.ForceRefresh();
        }

        public void CallToolIKRangePinning(int rangeFirstFrame, int rangeLastFrame, bool useEndFrame, bool enableTransitionDuration, int transitionDurationFrame,
                                                           bool[] targetPosition, bool targetRotation)
        {
            const string MenuTitle = "IK Range Pinning";
            const int DefaultLoopCount = 16;

            if (!ToolsCommonBefore(CurrentClip, MenuTitle)) return;

            var saveCurrentTime = UAw.GetCurrentTime();
            try
            {
                var beginTime = EditorCommon.SnapToFrame(rangeFirstFrame >= 0 ? rangeFirstFrame / CurrentClip.frameRate : 0f, CurrentClip.frameRate);
                var endTime = EditorCommon.SnapToFrame(rangeLastFrame >= 0 ? rangeLastFrame / CurrentClip.frameRate : CurrentClip.length, CurrentClip.frameRate);

                var startTransitionDurationTime = Mathf.Max(GetFrameTime(rangeFirstFrame - transitionDurationFrame), 0f);
                var endTransitionDurationTime = Mathf.Min(GetFrameTime(rangeLastFrame + transitionDurationFrame), CurrentClip.length);

                #region AnimatorIK
                if (IsHuman && animatorIK.ikData != null && animatorIK.ikData.Any(data => data.enable) && animatorIK.ikTargetSelect != null)
                {
                    IKDataSave[] ikDataBeginSave = null, ikDataEndSave = null;
                    {
                        ikDataBeginSave = new IKDataSave[animatorIK.ikData.Length];
                        SetCurrentTimeAndSampleAnimation(beginTime);
                        for (var index = 0; index < animatorIK.ikData.Length; index++)
                        {
                            if (!animatorIK.ikData[index].enable) continue;
                            animatorIK.SynchroSet((AnimatorIKCore.IKTarget)index);
                            ikDataBeginSave[index] = new IKDataSave(animatorIK.ikData[index]);
                        }
                        if (useEndFrame)
                        {
                            ikDataEndSave = new IKDataSave[animatorIK.ikData.Length];
                            SetCurrentTimeAndSampleAnimation(endTime);
                            for (var index = 0; index < animatorIK.ikData.Length; index++)
                            {
                                if (!animatorIK.ikData[index].enable) continue;
                                animatorIK.SynchroSet((AnimatorIKCore.IKTarget)index);
                                ikDataEndSave[index] = new IKDataSave(animatorIK.ikData[index]);
                            }
                        }
                        else
                        {
                            ikDataEndSave = ikDataBeginSave;
                        }
                    }

                    for (int frame = rangeFirstFrame; frame <= rangeLastFrame; frame++)
                    {
                        EditorUtility.DisplayProgressBar(MenuTitle, $"Animator IK {frame} / {rangeLastFrame}", (frame - rangeFirstFrame) / (float)Mathf.Max(1, rangeLastFrame - rangeFirstFrame));

                        var time = UAw.GetFrameTime(frame, CurrentClip);

                        SetCurrentTimeAndSampleAnimation(time);

                        {
                            float rate = 0f;
                            if (rangeLastFrame - rangeFirstFrame > 0)
                                rate = (frame - rangeFirstFrame) / (float)(rangeLastFrame - rangeFirstFrame);

                            foreach (var ikTarget in animatorIK.ikTargetSelect)
                            {
                                var index = (int)ikTarget;
                                if (!animatorIK.ikData[index].enable) continue;
                                animatorIK.SynchroSet((AnimatorIKCore.IKTarget)index);
                                var ikDataCurrent = new IKDataSave(animatorIK.ikData[index]);
                                var ikDataSave = IKDataSave.Lerp(in ikDataBeginSave[index], in ikDataEndSave[index], rate);
                                ikDataSave.OverWrite(ikDataCurrent, targetPosition, targetRotation);
                                ikDataSave.Set(animatorIK.ikData[index]);
                                animatorIK.SetUpdateIKtargetAnimatorIK(ikTarget, true);
                            }
                        }

                        animatorIK.UpdateIK(false, DefaultLoopCount);
                        ResetUpdateIKtargetAll();

                        AddHumanoidFootIK(time);
                    }
                    UpdateHumanoidFootIK();

                    BakeBetweenHumanoidBasicCurve(rangeFirstFrame, rangeLastFrame);
                }
                #endregion
                #region OriginalIK
                if (originalIK.ikData != null && originalIK.ikData.Any(data => data.enable) && originalIK.ikTargetSelect != null)
                {
                    IKDataSave[] ikDataBeginSave = null, ikDataEndSave = null;
                    {
                        ikDataBeginSave = new IKDataSave[originalIK.ikData.Count];
                        SetCurrentTimeAndSampleAnimation(beginTime);
                        for (var index = 0; index < originalIK.ikData.Count; index++)
                        {
                            if (!originalIK.ikData[index].enable) continue;
                            originalIK.SynchroSet(index);
                            ikDataBeginSave[index] = new IKDataSave(originalIK.ikData[index]);
                        }
                        if (useEndFrame)
                        {
                            ikDataEndSave = new IKDataSave[originalIK.ikData.Count];
                            SetCurrentTimeAndSampleAnimation(endTime);
                            for (var index = 0; index < originalIK.ikData.Count; index++)
                            {
                                if (!originalIK.ikData[index].enable) continue;
                                originalIK.SynchroSet(index);
                                ikDataEndSave[index] = new IKDataSave(originalIK.ikData[index]);
                            }
                        }
                        else
                        {
                            ikDataEndSave = ikDataBeginSave;
                        }
                    }

                    for (int frame = rangeFirstFrame; frame <= rangeLastFrame; frame++)
                    {
                        EditorUtility.DisplayProgressBar(MenuTitle, $"Original IK {frame} / {rangeLastFrame}", (frame - rangeFirstFrame) / (float)Mathf.Max(1, rangeLastFrame - rangeFirstFrame));

                        var time = UAw.GetFrameTime(frame, CurrentClip);

                        SetCurrentTimeAndSampleAnimation(time);

                        {
                            float rate = 0f;
                            if (rangeLastFrame - rangeFirstFrame > 0)
                                rate = (frame - rangeFirstFrame) / (float)(rangeLastFrame - rangeFirstFrame);

                            foreach (var ikTarget in originalIK.ikTargetSelect)
                            {
                                var index = ikTarget;
                                if (!originalIK.ikData[index].enable) continue;
                                originalIK.SynchroSet(index);
                                var ikDataCurrent = new IKDataSave(originalIK.ikData[index]);
                                var ikDataSave = IKDataSave.Lerp(in ikDataBeginSave[index], in ikDataEndSave[index], rate);
                                ikDataSave.OverWrite(ikDataCurrent, targetPosition, targetRotation);
                                ikDataSave.Set(originalIK.ikData[index]);
                                originalIK.SetUpdateIKtargetOriginalIK(ikTarget, true);
                            }
                        }

                        originalIK.UpdateIK(DefaultLoopCount);
                        ResetUpdateIKtargetAll();
                    }

                    foreach (var ikTarget in originalIK.ikTargetSelect)
                    {
                        var index = ikTarget;
                        if (!originalIK.ikData[index].enable) continue;
                        var tipIndex = originalIK.ikData[index].joints.Count > 0 ? originalIK.ikData[index].joints[0].boneIndex : -1;
                        if (tipIndex == -1) continue;
                        BakeBetweenGenericAncestorCurve(tipIndex, rangeFirstFrame, rangeLastFrame);
                    }

                    if (IsHuman)
                    {
                        BakeBetweenHumanoidBasicCurve(rangeFirstFrame, rangeLastFrame);
                    }
                }
                #endregion

                if (enableTransitionDuration)
                {
                    float halfFrameTime = EditorCommon.GetHalfFrameTime(CurrentClip.frameRate);
                    foreach (var pair in curvesWasModifiedStopped)
                    {
                        if (pair.Value.deleted != AnimationUtility.CurveModifiedType.CurveModified)
                            continue;
                        var curve = GetEditorCurveCache(pair.Value.binding);
                        if (curve == null)
                            continue;
                        if (startTransitionDurationTime != beginTime)
                        {
                            var beginValue = curve.Evaluate(beginTime);
                            AnimationCommon.SetKeyframe(curve, startTransitionDurationTime, curve.Evaluate(startTransitionDurationTime));
                            AnimationCommon.SetKeyframeTangentModeClampedAuto(curve, startTransitionDurationTime);
                            AnimationCommon.RemoveBetweenKeyframe(curve, startTransitionDurationTime + halfFrameTime, beginTime);
                            AnimationCommon.SetKeyframe(curve, beginTime, beginValue);
                        }
                        if (endTime != endTransitionDurationTime)
                        {
                            var endValue = curve.Evaluate(endTime);
                            AnimationCommon.SetKeyframe(curve, endTransitionDurationTime, curve.Evaluate(endTransitionDurationTime));
                            AnimationCommon.SetKeyframeTangentModeClampedAuto(curve, endTransitionDurationTime);
                            AnimationCommon.RemoveBetweenKeyframe(curve, endTime, endTransitionDurationTime - halfFrameTime);
                            AnimationCommon.SetKeyframe(curve, endTime, endValue);
                        }
                        SetEditorCurveCache(pair.Value.binding, curve);
                    }
                }

                ToolsCurvesWasModifiedStoppedUpdateTangents(beginTime, endTime);
            }
            finally
            {
                SetCurrentTime(saveCurrentTime);
                EditorUtility.ClearProgressBar();
            }

            ToolsCommonAfter();
        }
#if VERYANIMATION_ANIMATIONRIGGING
        public void CallToolAnimationRiggingRangePinning(int rangeFirstFrame, int rangeLastFrame, bool useEndFrame, bool enableTransitionDuration, int transitionDurationFrame,
                                                           bool weight, bool[] targetPosition, bool[] targetRotation, bool[] hintPosition)
        {
            const string MenuTitle = "Constraint Range Pinning";

            if (!ToolsCommonBefore(CurrentClip, MenuTitle)) return;

            List<EditorCurveBinding> changedBindings = new();

            var startTime = GetFrameTime(rangeFirstFrame);
            var endTime = GetFrameTime(rangeLastFrame);
            var startTransitionDurationTime = Mathf.Max(GetFrameTime(rangeFirstFrame - transitionDurationFrame), 0f);
            var endTransitionDurationTime = Mathf.Min(GetFrameTime(rangeLastFrame + transitionDurationFrame), CurrentClip.length);
            float halfFrameTime = EditorCommon.GetHalfFrameTime(CurrentClip.frameRate);

            foreach (var boneIndex in SelectionBones)
            {
                if (!Bones[boneIndex].TryGetComponent<IRigConstraint>(out var constraint))
                    continue;

                #region Weight
                if (weight)
                {
                    var binding = EditorCurveBinding.FloatCurve(BonePaths[boneIndex], constraint.GetType(), "m_Weight");
                    var curve = GetAnimationCurveCustomProperty(binding);
                    SetKeyframeWithPinning(curve, startTime, endTime, startTransitionDurationTime, endTransitionDurationTime, 1f, 1f);
                    SetAnimationCurveCustomProperty(binding, curve);
                    changedBindings.Add(binding);
                }
                #endregion

                void SetKeyframeWithPinning(AnimationCurve curve, float startTime, float endTime,
                                        float startTransitionDurationTime, float endTransitionDurationTime, float startValue, float endValue)
                {
                    if (enableTransitionDuration)
                    {
                        if (startTransitionDurationTime != startTime)
                        {
                            AnimationCommon.SetKeyframe(curve, startTransitionDurationTime, curve.Evaluate(startTransitionDurationTime));
                            AnimationCommon.SetKeyframeTangentModeClampedAuto(curve, startTransitionDurationTime);
                            AnimationCommon.RemoveBetweenKeyframe(curve, startTransitionDurationTime + halfFrameTime, startTime);
                        }
                        if (endTime != endTransitionDurationTime)
                        {
                            AnimationCommon.SetKeyframe(curve, endTransitionDurationTime, curve.Evaluate(endTransitionDurationTime));
                            AnimationCommon.SetKeyframeTangentModeClampedAuto(curve, endTransitionDurationTime);
                            AnimationCommon.RemoveBetweenKeyframe(curve, endTime, endTransitionDurationTime - halfFrameTime);
                        }
                    }
                    {
                        AnimationCommon.RemoveBetweenKeyframe(curve, startTime, endTime);
                        AnimationCommon.SetKeyframe(curve, startTime, startValue);
                        AnimationCommon.SetKeyframe(curve, endTime, endValue);
                        AnimationCommon.SetKeyframeTangentFlat(curve, startTime);
                        AnimationCommon.SetKeyframeTangentFlat(curve, endTime);
                    }
                }
                void PinPosition(int boneIndex, bool[] flags, float startTime, float endTime,
                                float startTransitionDurationTime, float endTransitionDurationTime)
                {
                    if (!(flags[0] || flags[1] || flags[2]))
                        return;
                    var startPosition = GetAnimationValueTransformPosition(boneIndex, startTime);
                    var endPosition = useEndFrame ? GetAnimationValueTransformPosition(boneIndex, endTime) : startPosition;
                    for (int dof = 0; dof < 3; dof++)
                    {
                        if (!flags[dof])
                            continue;
                        var curve = GetAnimationCurveTransformPosition(boneIndex, dof);
                        SetKeyframeWithPinning(curve, startTime, endTime, startTransitionDurationTime, endTransitionDurationTime, startPosition[dof], endPosition[dof]);
                        SetAnimationCurveTransformPosition(boneIndex, dof, curve);
                        changedBindings.Add(AnimationCurveBindingTransformPosition(boneIndex, dof));
                    }
                }
                void PinRotation(int boneIndex, bool[] flags, float startTime, float endTime,
                                            float startTransitionDurationTime, float endTransitionDurationTime)
                {
                    if (!(flags[0] || flags[1] || flags[2]))
                        return;
                    var startRotation = GetAnimationValueTransformRotation(boneIndex, startTime).eulerAngles;
                    var endRotation = useEndFrame ? GetAnimationValueTransformRotation(boneIndex, endTime).eulerAngles : startRotation;
                    var rotationMode = GetHaveAnimationCurveTransformRotationMode(boneIndex);
                    if (rotationMode != URotationCurveInterpolation.Mode.Undefined)
                    {
                        if (rotationMode != URotationCurveInterpolation.Mode.RawEuler)
                        {
                            UpdateSyncEditorCurveClip();
                            var convertBindings = new EditorCurveBinding[6];
                            for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                                convertBindings[dofIndex] = AnimationCurveBindingTransformRotation(boneIndex, dofIndex, URotationCurveInterpolation.Mode.Baked);
                            for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                                convertBindings[dofIndex + 3] = AnimationCurveBindingTransformRotation(boneIndex, dofIndex, URotationCurveInterpolation.Mode.NonBaked);
                            URotationCurveInterpolation.SetInterpolation(CurrentClip, convertBindings, URotationCurveInterpolation.Mode.RawEuler);
                            ClearEditorCurveCache();
                            rotationMode = GetHaveAnimationCurveTransformRotationMode(boneIndex);
                            #region FixReverseRotation
                            for (int dof = 0; dof < 3; dof++)
                            {
                                var curve = GetAnimationCurveTransformRotation(boneIndex, dof, rotationMode, false);
                                if (curve != null && AnimationCommon.FixReverseRotationEuler(curve))
                                    SetAnimationCurveTransformRotation(boneIndex, dof, rotationMode, curve);
                            }
                            #endregion
                        }
                    }
                    if (rotationMode == URotationCurveInterpolation.Mode.RawEuler)
                    {
                        AnimationCurve[] curves = new AnimationCurve[3];
                        for (int dof = 0; dof < 3; dof++)
                        {
                            curves[dof] = GetAnimationCurveTransformRotation(boneIndex, dof, rotationMode);

                            if (!flags[dof])
                                continue;
                            AnimationCommon.SetKeyframe(curves[dof], startTime, curves[dof].Evaluate(startTime));
                            AnimationCommon.SetKeyframe(curves[dof], endTime, curves[dof].Evaluate(endTime));
                            AnimationCommon.RemoveBetweenKeyframe(curves[dof], startTime, endTime);
                        }
                        var fixStartRotation = FixReverseRotationEuler(curves, startTime, startRotation);
                        var fixEndRotation = FixReverseRotationEuler(curves, endTime, endRotation);
                        for (int dof = 0; dof < 3; dof++)
                        {
                            if (!flags[dof])
                                continue;
                            SetKeyframeWithPinning(curves[dof], startTime, endTime, startTransitionDurationTime, endTransitionDurationTime, fixStartRotation[dof], fixEndRotation[dof]);
                            SetAnimationCurveTransformRotation(boneIndex, dof, rotationMode, curves[dof]);
                            changedBindings.Add(AnimationCurveBindingTransformRotation(boneIndex, dof, rotationMode));
                        }
                    }
                }

                if (constraint is MultiAimConstraint multiAimConstraint)
                {
                    #region MultiAimConstraint
                    for (int i = 0; i < multiAimConstraint.data.sourceObjects.Count; i++)
                    {
                        if (multiAimConstraint.data.sourceObjects[i].transform == null)
                            continue;

                        var targetBoneIndex = BonesIndexOf(multiAimConstraint.data.sourceObjects[i].transform.gameObject);
                        if (targetBoneIndex >= 0)
                        {
                            PinPosition(targetBoneIndex, targetPosition, startTime, endTime, startTransitionDurationTime, endTransitionDurationTime);
                        }
                    }
                    #endregion
                }
                else if (constraint is TwoBoneIKConstraint twoBoneIKConstraint)
                {
                    #region TwoBoneIKConstraint
                    if (twoBoneIKConstraint.data.target != null)
                    {
                        var targetBoneIndex = BonesIndexOf(twoBoneIKConstraint.data.target.gameObject);
                        if (targetBoneIndex >= 0)
                        {
                            PinPosition(targetBoneIndex, targetPosition, startTime, endTime, startTransitionDurationTime, endTransitionDurationTime);
                            PinRotation(targetBoneIndex, targetRotation, startTime, endTime, startTransitionDurationTime, endTransitionDurationTime);
                        }
                    }
                    if (twoBoneIKConstraint.data.hint != null)
                    {
                        var hintBoneIndex = BonesIndexOf(twoBoneIKConstraint.data.hint.gameObject);
                        if (hintBoneIndex >= 0)
                        {
                            PinPosition(hintBoneIndex, hintPosition, startTime, endTime, startTransitionDurationTime, endTransitionDurationTime);
                        }
                    }
                    #endregion
                }
            }

            EditorApplication.delayCall += () =>
            {
                SetAnimationWindowSynchroSelection(changedBindings);
            };

            ToolsCommonAfter();
        }
#endif
    }
}
