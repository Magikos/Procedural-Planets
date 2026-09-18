using System;
using UnityEditor;
using UnityEngine;

namespace VeryAnimation
{
    internal class RootTrail
    {
        private VeryAnimationWindow VAW => VeryAnimationWindow.instance;

        private readonly AnimationCurve[] curves;

        public RootTrail()
        {
            curves = new AnimationCurve[3];
        }

        public void Draw()
        {
            var va = VAW.VA;
            if (!va.IsHuman) return;

            #region CurveReady
            for (int dof = 0; dof < 3; dof++)
            {
                curves[dof] = va.GetAnimationCurveAnimatorRootT(dof, false);
                if (curves[dof] == null) return;
            }
            #endregion

            var lastFrame = va.GetLastFrame();
            var matrix = va.TransformPoseSave.StartMatrix;
            var humanScale = va.Skeleton.Animator.humanScale;
            var frameRate = va.CurrentClip.frameRate;

            VAW.UHandleUtility.ApplyWireMaterial();
            GL.PushMatrix();
            GL.MultMatrix(Handles.matrix);
            GL.Begin(GL.LINE_STRIP);
            GL.Color(VAW.EditorSettings.SettingExtraRootTrailColor);
            {
                var beforeTime = 0f;
                var beforePos = Vector3.zero;
                for (int frame = 0; frame <= lastFrame; frame++)
                {
                    var time = EditorCommon.GetFrameTime(frame, frameRate);
                    var pos = matrix.MultiplyPoint3x4(AnimationCommon.EvaluateVector3(curves, time) * humanScale);

                    if (frame > 0)
                    {
                        const float Granularity = 0.04f;
                        const int MaxCount = 64;
                        var screenLength = Vector2.Distance(HandleUtility.WorldToGUIPoint(beforePos), HandleUtility.WorldToGUIPoint(pos));
                        int count = Math.Min(Mathf.RoundToInt(screenLength * Granularity), MaxCount);
                        var step = 1f / (count + 1f);
                        for (int i = 0; i < count; i++)
                        {
                            var rate = step * (i + 1);
                            var stepTime = Mathf.Lerp(beforeTime, time, rate);
                            var stepPos = matrix.MultiplyPoint3x4(AnimationCommon.EvaluateVector3(curves, stepTime) * humanScale);
                            GL.Vertex(stepPos);
                        }
                    }
                    GL.Vertex(pos);

                    beforeTime = time;
                    beforePos = pos;
                }
            }
            GL.End();
            GL.PopMatrix();
        }
    }
}
