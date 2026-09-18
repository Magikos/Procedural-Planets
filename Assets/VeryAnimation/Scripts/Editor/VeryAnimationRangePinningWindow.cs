using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

#if VERYANIMATION_ANIMATIONRIGGING
using UnityEngine.Animations.Rigging;
#endif

namespace VeryAnimation
{
    [Serializable]
    internal abstract class VeryAnimationRangePinningBaseWindow : EditorWindow
    {
        protected VeryAnimationWindow VAW => VeryAnimationWindow.instance;

        protected const string UndoChangeRangePinning = "Change Range Pinning";

        #region EditorPrefs Keys
        protected const string PrefKey_Time = "VeryAnimation_RangePinning_Time";
        protected const string PrefKey_UseEndFrame = "VeryAnimation_RangePinning_UseEndFrame";
        protected const string PrefKey_EnableTransitionDuration = "VeryAnimation_RangePinning_EnableTransitionDuration";
        protected const string PrefKey_TransitionDurationTime = "VeryAnimation_RangePinning_TransitionDurationTime";
        protected const string PrefKey_Weight = "VeryAnimation_RangePinning_Weight";
        protected const string PrefKey_TargetPositionX = "VeryAnimation_RangePinning_TargetPositionX";
        protected const string PrefKey_TargetPositionY = "VeryAnimation_RangePinning_TargetPositionY";
        protected const string PrefKey_TargetPositionZ = "VeryAnimation_RangePinning_TargetPositionZ";
        protected const string PrefKey_TargetRotationX = "VeryAnimation_RangePinning_TargetRotationX";
        protected const string PrefKey_TargetRotationY = "VeryAnimation_RangePinning_TargetRotationY";
        protected const string PrefKey_TargetRotationZ = "VeryAnimation_RangePinning_TargetRotationZ";
        protected const string PrefKey_HintPositionX = "VeryAnimation_RangePinning_HintPositionX";
        protected const string PrefKey_HintPositionY = "VeryAnimation_RangePinning_HintPositionY";
        protected const string PrefKey_HintPositionZ = "VeryAnimation_RangePinning_HintPositionZ";
        #endregion

        protected const int FlagWidth = 64;

        protected UAnimationClipEditor uAnimationClipEditor;

        public int rangeFirstFrame;
        public int rangeLastFrame;

        public bool useEndFrame;

        public bool enableTransitionDuration;
        public int transitionDurationFrame;

        public bool weight = true;
        public bool[] targetPosition = new bool[3];
        public bool[] targetRotation = new bool[3];
        public bool[] hintPosition = new bool[3];

        protected bool TargetRotationAll
        {
            get => targetRotation[0] && targetRotation[1] && targetRotation[2];
            set => targetRotation[0] = targetRotation[1] = targetRotation[2] = value;
        }

        protected virtual void OnEnable()
        {
            titleContent = new GUIContent("Range Pinning");

            var endTime = EditorPrefs.GetFloat(PrefKey_Time, 0.1f);
            useEndFrame = EditorPrefs.GetBool(PrefKey_UseEndFrame, false);
            enableTransitionDuration = EditorPrefs.GetBool(PrefKey_EnableTransitionDuration, true);
            transitionDurationFrame = VAW.VA.GetTimeFrame(EditorPrefs.GetFloat(PrefKey_TransitionDurationTime, 0.05f));
            weight = EditorPrefs.GetBool(PrefKey_Weight, true);
            targetPosition[0] = EditorPrefs.GetBool(PrefKey_TargetPositionX, true);
            targetPosition[1] = EditorPrefs.GetBool(PrefKey_TargetPositionY, true);
            targetPosition[2] = EditorPrefs.GetBool(PrefKey_TargetPositionZ, true);
            targetRotation[0] = EditorPrefs.GetBool(PrefKey_TargetRotationX, true);
            targetRotation[1] = EditorPrefs.GetBool(PrefKey_TargetRotationY, true);
            targetRotation[2] = EditorPrefs.GetBool(PrefKey_TargetRotationZ, true);
            hintPosition[0] = EditorPrefs.GetBool(PrefKey_HintPositionX, true);
            hintPosition[1] = EditorPrefs.GetBool(PrefKey_HintPositionY, true);
            hintPosition[2] = EditorPrefs.GetBool(PrefKey_HintPositionZ, true);

            uAnimationClipEditor = new UAnimationClipEditor(VAW.VA.CurrentClip, VAW.VA.UAvatarPreview);

            rangeFirstFrame = VAW.VA.GetTimeFrame(VAW.VA.CurrentTime);
            rangeLastFrame = Math.Min(rangeFirstFrame + VAW.VA.GetTimeFrame(endTime), VAW.VA.GetLastFrame());
        }
        protected virtual void OnDisable()
        {
            uAnimationClipEditor?.Dispose();
            uAnimationClipEditor = null;

            EditorPrefs.SetFloat(PrefKey_Time, VAW.VA.GetFrameTime(Math.Max(rangeLastFrame - rangeFirstFrame, 0)));
            EditorPrefs.SetBool(PrefKey_UseEndFrame, useEndFrame);
            EditorPrefs.SetBool(PrefKey_EnableTransitionDuration, enableTransitionDuration);
            EditorPrefs.SetFloat(PrefKey_TransitionDurationTime, VAW.VA.GetFrameTime(Math.Max(transitionDurationFrame, 0)));
            EditorPrefs.SetBool(PrefKey_Weight, weight);
            EditorPrefs.SetBool(PrefKey_TargetPositionX, targetPosition[0]);
            EditorPrefs.SetBool(PrefKey_TargetPositionY, targetPosition[1]);
            EditorPrefs.SetBool(PrefKey_TargetPositionZ, targetPosition[2]);
            EditorPrefs.SetBool(PrefKey_TargetRotationX, targetRotation[0]);
            EditorPrefs.SetBool(PrefKey_TargetRotationY, targetRotation[1]);
            EditorPrefs.SetBool(PrefKey_TargetRotationZ, targetRotation[2]);
            EditorPrefs.SetBool(PrefKey_HintPositionX, hintPosition[0]);
            EditorPrefs.SetBool(PrefKey_HintPositionY, hintPosition[1]);
            EditorPrefs.SetBool(PrefKey_HintPositionZ, hintPosition[2]);
        }

        protected void DrawAxisTogglesGUI(string label, bool[] flags)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            for (int i = 0; i < 3; i++)
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.ToggleLeft(EditorCommon.AxisLabels[i], flags[i], GUILayout.Width(FlagWidth));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(this, UndoChangeRangePinning);
                    flags[i] = flag;
                }
            }
            EditorGUILayout.EndHorizontal();
        }
        protected void DrawAxisAllToggleGUI(string label, bool[] flags)
        {
            EditorGUI.BeginChangeCheck();
            var flag = EditorGUILayout.Toggle(label, flags[0] && flags[1] && flags[2]);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, UndoChangeRangePinning);
                flags[0] = flags[1] = flags[2] = flag;
            }
        }

        protected void DrawClipRangeGUI()
        {
            float firstFrame = rangeFirstFrame;
            float lastFrame = rangeLastFrame;
            float additivePoseframe = 0.0f;
            uAnimationClipEditor.ClipRangeGUI(ref firstFrame, ref lastFrame, out bool changedStart, out bool changedStop, false, ref additivePoseframe, out _);
            if (changedStart)
            {
                Undo.RecordObject(this, "Change first frame");
                rangeFirstFrame = Mathf.RoundToInt(firstFrame);
            }
            if (changedStop)
            {
                Undo.RecordObject(this, "Change last frame");
                rangeLastFrame = Mathf.RoundToInt(lastFrame);
            }
        }
        protected void DrawEndFrameToggleGUI()
        {
            EditorGUI.BeginChangeCheck();
            var flag = EditorGUILayout.Toggle(Language.GetContent(Language.Help.SelectionRangePinning_UseEndFrame), useEndFrame);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, UndoChangeRangePinning);
                useEndFrame = flag;
            }
        }
        protected void DrawTransitionDurationGUI()
        {
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.Toggle(Language.GetContent(Language.Help.SelectionRangePinning_TransitionDuration), enableTransitionDuration);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(this, UndoChangeRangePinning);
                    enableTransitionDuration = flag;
                }
            }
            if (enableTransitionDuration)
            {
                EditorGUILayout.Space();
                {
                    EditorGUI.BeginChangeCheck();
                    var value = EditorGUILayout.IntField(transitionDurationFrame, GUILayout.Width(FlagWidth));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(this, UndoChangeRangePinning);
                        transitionDurationFrame = Math.Max(value, 0);
                    }
                }
                EditorGUILayout.LabelField($"Frames / Time {VAW.VA.GetFrameTime(transitionDurationFrame)}");
            }
            EditorGUILayout.EndHorizontal();
        }
        protected void DrawSetGUI()
        {
            GUILayout.FlexibleSpace();
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space();
                if (GUILayout.Button("Set"))
                {
                    Set();
                    Close();
                }
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.Space();
        }
        protected abstract void Set();
    }

    internal sealed class VeryAnimationRangePinningIKWindow : VeryAnimationRangePinningBaseWindow
    {
        protected override void OnEnable()
        {
            base.OnEnable();

            minSize = new Vector2(512, 200);
            position = new Rect(position.position, minSize);
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical(VAW.GuiStyleSkinBox);

            DrawClipRangeGUI();

            DrawEndFrameToggleGUI();

            DrawTransitionDurationGUI();

            EditorGUILayout.Space();

            DrawAxisTogglesGUI("Position", targetPosition);
            DrawAxisAllToggleGUI("Rotation", targetRotation);

            DrawSetGUI();

            EditorGUILayout.EndVertical();
        }
        protected override void Set()
        {
            VAW.VA.CallToolIKRangePinning(rangeFirstFrame, rangeLastFrame, useEndFrame, enableTransitionDuration, transitionDurationFrame,
                                                            targetPosition, TargetRotationAll);
        }
    }

#if VERYANIMATION_ANIMATIONRIGGING
    internal sealed class VeryAnimationRangePinningAnimationRiggingWindow : VeryAnimationRangePinningBaseWindow
    {
        private bool enableTargetRotation;
        private bool enableHintPosition;

        protected override void OnEnable()
        {
            base.OnEnable();

            minSize = new Vector2(512, 240);
            position = new Rect(position.position, minSize);

            if (VAW.VA.SelectionActiveGameObject.TryGetComponent<IRigConstraint>(out var constraint))
            {
                if (constraint is TwoBoneIKConstraint)
                {
                    enableTargetRotation = true;
                    enableHintPosition = true;
                }
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical(VAW.GuiStyleSkinBox);

            DrawClipRangeGUI();

            DrawEndFrameToggleGUI();

            DrawTransitionDurationGUI();

            EditorGUILayout.Space();

            {
                EditorGUI.BeginChangeCheck();
                var flag = EditorGUILayout.Toggle("Weight", weight);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(this, UndoChangeRangePinning);
                    weight = flag;
                }
            }
            DrawAxisTogglesGUI("Target Position", targetPosition);
            if (enableTargetRotation)
            {
                DrawAxisTogglesGUI("Target Rotation", targetRotation);
            }
            if (enableHintPosition)
            {
                DrawAxisTogglesGUI("Hint Position", hintPosition);
            }

            DrawSetGUI();

            EditorGUILayout.EndVertical();
        }

        protected override void Set()
        {
            VAW.VA.CallToolAnimationRiggingRangePinning(rangeFirstFrame, rangeLastFrame, useEndFrame, enableTransitionDuration, transitionDurationFrame, 
                                                            weight, targetPosition, targetRotation, hintPosition);
        }
    }
#endif
}
