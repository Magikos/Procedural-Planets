using System;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class UAnimationClipEditor : IDisposable
    {
        public Editor Instance { get; private set; }

        private readonly MethodInfo mi_ClipRangeGUI;
        private readonly MethodInfo mi_InitClipTime;
        private readonly FieldInfo fi_m_AvatarPreview;
        private readonly FieldInfo fi_m_TimeArea;

        private AnimationClip dummyClip;
        private UAvatarPreview uDummyAvatarPreview;

        private static readonly EditorCurveBinding DummyRangeBinding = EditorCurveBinding.FloatCurve("", typeof(Animator), "");

        public UAnimationClipEditor(AnimationClip clip, UAvatarPreview avatarPreview)
        {
            var animationClipEditorType = ReflectionCommon.GetUnityEditorType("UnityEditor.AnimationClipEditor");
            Assert.IsNotNull(mi_ClipRangeGUI = animationClipEditorType.GetMethod("ClipRangeGUI"));
            Assert.IsNotNull(mi_InitClipTime = animationClipEditorType.GetMethod("InitClipTime"));
            Assert.IsNotNull(fi_m_AvatarPreview = animationClipEditorType.GetField("m_AvatarPreview", BindingFlags.NonPublic | BindingFlags.Instance));
            Assert.IsNotNull(fi_m_TimeArea = animationClipEditorType.GetField("m_TimeArea", BindingFlags.NonPublic | BindingFlags.Instance));

            Instance = Editor.CreateEditor(clip, animationClipEditorType);
            fi_m_AvatarPreview.SetValue(Instance, avatarPreview.Instance);
            mi_InitClipTime.Invoke(Instance, null);
        }
        public UAnimationClipEditor(float lastTime, float frameRate)
        {
            var animationClipEditorType = ReflectionCommon.GetUnityEditorType("UnityEditor.AnimationClipEditor");
            Assert.IsNotNull(mi_ClipRangeGUI = animationClipEditorType.GetMethod("ClipRangeGUI"));
            Assert.IsNotNull(mi_InitClipTime = animationClipEditorType.GetMethod("InitClipTime"));
            Assert.IsNotNull(fi_m_AvatarPreview = animationClipEditorType.GetField("m_AvatarPreview", BindingFlags.NonPublic | BindingFlags.Instance));
            Assert.IsNotNull(fi_m_TimeArea = animationClipEditorType.GetField("m_TimeArea", BindingFlags.NonPublic | BindingFlags.Instance));

            dummyClip = new AnimationClip
            {
                frameRate = frameRate
            };
            dummyClip.hideFlags |= HideFlags.HideAndDontSave;
            Instance = Editor.CreateEditor(dummyClip, animationClipEditorType);
            {
                uDummyAvatarPreview = new UAvatarPreview(dummyClip, null, null);
                fi_m_AvatarPreview.SetValue(Instance, uDummyAvatarPreview.Instance);
            }
            SetDummyClipRange(lastTime, frameRate);
        }
        public void Dispose()
        {
            if (Instance == null) return;
            uDummyAvatarPreview?.Dispose();
            uDummyAvatarPreview = null;
            fi_m_AvatarPreview.SetValue(Instance, null);
            Editor.DestroyImmediate(Instance);
            Instance = null;
            if (dummyClip != null)
            {
                AnimationClip.DestroyImmediate(dummyClip);
                dummyClip = null;
            }
        }

        public void ClipRangeGUI(ref float startFrame, ref float stopFrame, out bool changedStart, out bool changedStop, bool showAdditivePoseFrame, ref float additivePoseframe, out bool changedAdditivePoseframe)
        {
            changedStart = false;
            changedStop = false;
            changedAdditivePoseframe = false;
            var objects = new object[] { startFrame, stopFrame, changedStart, changedStop, showAdditivePoseFrame, additivePoseframe, changedAdditivePoseframe };
            mi_ClipRangeGUI.Invoke(Instance, objects);
            startFrame = (float)objects[0];
            stopFrame = (float)objects[1];
            changedStart = (bool)objects[2];
            changedStop = (bool)objects[3];
            additivePoseframe = (float)objects[5];
            changedAdditivePoseframe = (bool)objects[6];
        }

        public void SetDummyCursorTime(float time, float frameRate)
        {
            Assert.IsNotNull(dummyClip);
            dummyClip.frameRate = frameRate;
            uDummyAvatarPreview.SetCurrentTimeOnly(time);
        }
        public void RefreshClipTime()
        {
            if (Instance == null) return;

            fi_m_TimeArea.SetValue(Instance, null);

            mi_InitClipTime.Invoke(Instance, null);
        }
        public void SetDummyClipRange(float lastTime, float frameRate)
        {
            Assert.IsNotNull(dummyClip);

            dummyClip.frameRate = frameRate;
            dummyClip.ClearCurves();
            AnimationUtility.SetEditorCurve(dummyClip,
                                            DummyRangeBinding,
                                            new AnimationCurve(new Keyframe[] { new(0f, 0f), new(lastTime, 1f) }));

            uDummyAvatarPreview?.SetCurrentTimeOnly(Mathf.Min(uDummyAvatarPreview.GetTime(), lastTime));

            RefreshClipTime();
        }
    }
}
