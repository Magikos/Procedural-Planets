#if VERYANIMATION_TIMELINE
using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEditor.Animations;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    [CustomEditor(typeof(VAAnimationTrack)), CanEditMultipleObjects]
    sealed class VAAnimationTrackInspector : Editor
    {
        private VAAnimationTrack targetObj;

        [SerializeField]
        private Editor animationTrackInspector;

        private Func<bool> IsTrackLocked;

        private void OnEnable()
        {
            if (animationTrackInspector != null)
            {
                Editor.DestroyImmediate(animationTrackInspector);
                animationTrackInspector = null;
            }

            if (target == null)
                return;

            targetObj = target as VAAnimationTrack;

            var typeAnimationTrackInspector = ReflectionCommon.GetUnityEditorType("UnityEditor.Timeline.AnimationTrackInspector");
            Assert.IsNotNull(typeAnimationTrackInspector);

            animationTrackInspector = Editor.CreateEditor(targets, typeAnimationTrackInspector);

            var mi_IsTrackLocked = typeAnimationTrackInspector.GetMethod("IsTrackLocked", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mi_IsTrackLocked);
            IsTrackLocked = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), animationTrackInspector, mi_IsTrackLocked);
        }
        private void OnDisable()
        {
            if (animationTrackInspector != null)
                Editor.DestroyImmediate(animationTrackInspector);
            animationTrackInspector = null;
        }

        public override void OnInspectorGUI()
        {
            if (animationTrackInspector == null)
                return;

            animationTrackInspector.OnInspectorGUI();

            using (new EditorGUI.DisabledScope(IsTrackLocked()))
            {
                EditorGUI.BeginDisabledGroup(!targetObj.isSubTrack);
                {
                    AnimatorLayerBlendingMode blendingMode = targetObj.blendingAdditive ? AnimatorLayerBlendingMode.Additive : AnimatorLayerBlendingMode.Override;
                    EditorGUI.BeginChangeCheck();
                    blendingMode = (AnimatorLayerBlendingMode)EditorGUILayout.EnumPopup("Blending", blendingMode);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObjects(targets, "Change Blending Mode");
                        foreach (var t in targets)
                        {
                            var track = t as VAAnimationTrack;
                            track.blendingAdditive = blendingMode == AnimatorLayerBlendingMode.Additive;

                            EditorUtility.SetDirty(track);
                        }
                        TimelineEditor.Refresh(RefreshReason.ContentsModified);
                    }
                }
                EditorGUI.EndDisabledGroup();
            }
        }
    }
}
#endif