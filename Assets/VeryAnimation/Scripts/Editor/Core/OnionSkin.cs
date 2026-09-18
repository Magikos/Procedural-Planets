using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VeryAnimation
{
    internal sealed class OnionSkin : IDisposable
    {
        private VeryAnimationWindow VAW => VeryAnimationWindow.instance;

        private class OnionSkinObject : IDisposable
        {
            public DummyObject dummyObject;

            public bool active;

            public OnionSkinObject(GameObject go)
            {
                dummyObject = new DummyObject();
                dummyObject.Initialize(go);
                dummyObject.ChangeTransparent();
                active = false;
            }
            public void Dispose()
            {
                dummyObject?.Dispose();
                dummyObject = null;
            }

            public void SetRenderQueue(int renderQueue)
            {
                dummyObject.SetTransparentRenderQueue(renderQueue);
            }

            public void SetColor(Color color)
            {
                dummyObject.SetColor(color);
            }
        }
        private readonly Dictionary<int, OnionSkinObject> onionSkinObjects;
        private float[] nextTimesBuffer;
        private float[] prevTimesBuffer;
        private AnimationClip lastClip;
        private float lastTime;
        private float lastClipLength;
        private float lastClipFrameRate;
        private bool lastEnabled;
        private bool lastShow;
        private EditorSettings.OnionSkinMode lastMode;
        private int lastFrameIncrement;
        private int lastNextCount;
        private int lastPrevCount;
        private Color lastNextColor;
        private Color lastPrevColor;
        private float lastNextMinAlpha;
        private float lastPrevMinAlpha;
        private bool dirty;

        private bool IsShow => VAW.IsShowSceneGizmo();

        public OnionSkin()
        {
            onionSkinObjects = new Dictionary<int, OnionSkinObject>();
            lastTime = float.NaN;
            dirty = true;
            AnimationUtility.onCurveWasModified += OnCurveWasModified;
        }
        public void Dispose()
        {
            AnimationUtility.onCurveWasModified -= OnCurveWasModified;
            Clear();
        }
        public void Clear()
        {
            if (onionSkinObjects.Count > 0)
            {
                foreach (var pair in onionSkinObjects)
                {
                    pair.Value.Dispose();
                }
                onionSkinObjects.Clear();
            }
            dirty = true;
        }

        private void OnCurveWasModified(AnimationClip clip, EditorCurveBinding binding, AnimationUtility.CurveModifiedType deleted)
        {
            var vaw = VeryAnimationWindow.instance;
            if (clip != null &&
                (clip == lastClip ||
                 (vaw != null && vaw.VA != null && clip == vaw.VA.CurrentClip)))
            {
                dirty = true;
            }
        }
        private bool HasActiveObjects()
        {
            foreach (var pair in onionSkinObjects)
            {
                if (pair.Value.dummyObject.GameObject.activeSelf)
                    return true;
            }
            return false;
        }
        private void DeactivateAllObjects()
        {
            foreach (var pair in onionSkinObjects)
            {
                pair.Value.active = false;
                if (pair.Value.dummyObject.GameObject.activeSelf)
                    pair.Value.dummyObject.GameObject.SetActive(false);
            }
        }
        private float[] EnsureTimeBuffer(ref float[] buffer, int count)
        {
            if (count <= 0)
                return null;

            if (buffer == null || buffer.Length != count)
                buffer = new float[count];

            return buffer;
        }
        private bool IsSameState(AnimationClip clip, float time, bool enabled, bool show)
        {
            return IsSamePoseState(clip, time, enabled, show) && IsSameAppearanceState();
        }
        private bool IsSamePoseState(AnimationClip clip, float time, bool enabled, bool show)
        {
            return !dirty &&
                   clip == lastClip &&
                   Mathf.Approximately(time, lastTime) &&
                   Mathf.Approximately(clip != null ? clip.length : 0f, lastClipLength) &&
                   Mathf.Approximately(clip != null ? clip.frameRate : 0f, lastClipFrameRate) &&
                   enabled == lastEnabled &&
                   show == lastShow &&
                   VAW.EditorSettings.SettingExtraOnionSkinMode == lastMode &&
                   VAW.EditorSettings.SettingExtraOnionSkinFrameIncrement == lastFrameIncrement &&
                   VAW.EditorSettings.SettingExtraOnionSkinNextCount == lastNextCount &&
                   VAW.EditorSettings.SettingExtraOnionSkinPrevCount == lastPrevCount;
        }
        private bool IsSameAppearanceState()
        {
            return VAW.EditorSettings.SettingExtraOnionSkinNextColor == lastNextColor &&
                   VAW.EditorSettings.SettingExtraOnionSkinPrevColor == lastPrevColor &&
                   Mathf.Approximately(VAW.EditorSettings.SettingExtraOnionSkinNextMinAlpha, lastNextMinAlpha) &&
                   Mathf.Approximately(VAW.EditorSettings.SettingExtraOnionSkinPrevMinAlpha, lastPrevMinAlpha);
        }
        private void SaveState(AnimationClip clip, float time, bool enabled, bool show)
        {
            lastClip = clip;
            lastTime = time;
            lastClipLength = clip != null ? clip.length : 0f;
            lastClipFrameRate = clip != null ? clip.frameRate : 0f;
            lastEnabled = enabled;
            lastShow = show;
            lastMode = VAW.EditorSettings.SettingExtraOnionSkinMode;
            lastFrameIncrement = VAW.EditorSettings.SettingExtraOnionSkinFrameIncrement;
            lastNextCount = VAW.EditorSettings.SettingExtraOnionSkinNextCount;
            lastPrevCount = VAW.EditorSettings.SettingExtraOnionSkinPrevCount;
            lastNextColor = VAW.EditorSettings.SettingExtraOnionSkinNextColor;
            lastPrevColor = VAW.EditorSettings.SettingExtraOnionSkinPrevColor;
            lastNextMinAlpha = VAW.EditorSettings.SettingExtraOnionSkinNextMinAlpha;
            lastPrevMinAlpha = VAW.EditorSettings.SettingExtraOnionSkinPrevMinAlpha;
            dirty = false;
        }
        private Color GetSlotColor(int slot)
        {
            Color color;
            float minAlpha;
            int count;
            if (slot > 0)
            {
                color = VAW.EditorSettings.SettingExtraOnionSkinNextColor;
                minAlpha = VAW.EditorSettings.SettingExtraOnionSkinNextMinAlpha;
                count = VAW.EditorSettings.SettingExtraOnionSkinNextCount;
            }
            else
            {
                color = VAW.EditorSettings.SettingExtraOnionSkinPrevColor;
                minAlpha = VAW.EditorSettings.SettingExtraOnionSkinPrevMinAlpha;
                count = VAW.EditorSettings.SettingExtraOnionSkinPrevCount;
            }
            var rate = count > 1 ? (Math.Abs(slot) - 1) / (float)(count - 1) : 0f;
            color.a = Mathf.Lerp(color.a, minAlpha, rate);
            return color;
        }
        private void UpdateActiveColors()
        {
            foreach (var pair in onionSkinObjects)
            {
                if (!pair.Value.active)
                    continue;
                pair.Value.SetColor(GetSlotColor(pair.Key));
            }
        }
        public void Update()
        {
            var enabled = VAW.VA.extraOptionsOnionSkin;
            var show = IsShow;
            var clip = VAW.VA.CurrentClip;
            var time = VAW.VA.CurrentTime;
            var hasVisibleFrames = VAW.EditorSettings.SettingExtraOnionSkinNextCount > 0 || VAW.EditorSettings.SettingExtraOnionSkinPrevCount > 0;

            if (!enabled)
            {
                if (lastEnabled || onionSkinObjects.Count > 0)
                    Clear();
                SaveState(clip, time, false, show);
                return;
            }

            if (!show || clip == null || !hasVisibleFrames)
            {
                if (!IsSameState(clip, time, enabled, show) || HasActiveObjects())
                    DeactivateAllObjects();
                SaveState(clip, time, enabled, show);
                return;
            }

            if (IsSamePoseState(clip, time, enabled, show))
            {
                if (!IsSameAppearanceState())
                {
                    UpdateActiveColors();
                    SaveState(clip, time, enabled, show);
                }
                return;
            }

            foreach (var pair in onionSkinObjects)
            {
                pair.Value.active = false;
            }

            var lastFrame = VAW.VA.GetLastFrame();

            if (VAW.EditorSettings.SettingExtraOnionSkinMode == EditorSettings.OnionSkinMode.Keyframes)
            {
                #region Keyframes
                var nextTimes = EnsureTimeBuffer(ref nextTimesBuffer, VAW.EditorSettings.SettingExtraOnionSkinNextCount);
                var prevTimes = EnsureTimeBuffer(ref prevTimesBuffer, VAW.EditorSettings.SettingExtraOnionSkinPrevCount);
                VAW.VA.UAw.GetNearKeyframeTimes(nextTimes, prevTimes);
                #region Next
                if (nextTimes != null)
                {
                    var beforeFrame = VAW.VA.GetTimeFrame(VAW.VA.CurrentTime);
                    var slot = 0;
                    for (int i = 0; i < VAW.EditorSettings.SettingExtraOnionSkinNextCount; i++)
                    {
                        if (Mathf.Approximately(VAW.VA.CurrentTime, nextTimes[i])) break;
                        var frame = VAW.VA.GetTimeFrame(nextTimes[i]);
                        if (frame < 0 || frame > lastFrame) break;
                        if (frame == beforeFrame) continue;
                        beforeFrame = frame;
                        slot++;
                        var oso = SetFrame(slot, VAW.VA.GetFrameTime(frame));
                        oso.SetColor(GetSlotColor(slot));
                    }
                }
                #endregion
                #region Prev
                if (prevTimes != null)
                {
                    var beforeFrame = VAW.VA.GetTimeFrame(VAW.VA.CurrentTime);
                    var slot = 0;
                    for (int i = 0; i < VAW.EditorSettings.SettingExtraOnionSkinPrevCount; i++)
                    {
                        if (Mathf.Approximately(VAW.VA.CurrentTime, prevTimes[i])) break;
                        var frame = VAW.VA.GetTimeFrame(prevTimes[i]);
                        if (frame < 0 || frame > lastFrame) break;
                        if (frame == beforeFrame) continue;
                        beforeFrame = frame;
                        slot++;
                        var oso = SetFrame(-slot, VAW.VA.GetFrameTime(frame));
                        oso.SetColor(GetSlotColor(-slot));
                    }
                }
                #endregion
                #endregion
            }
            else if (VAW.EditorSettings.SettingExtraOnionSkinMode == EditorSettings.OnionSkinMode.Frames)
            {
                #region Frames
                #region Next
                {
                    var frame = VAW.VA.UAw.GetCurrentFrame();
                    for (int i = 0; i < VAW.EditorSettings.SettingExtraOnionSkinNextCount; i++)
                    {
                        frame += VAW.EditorSettings.SettingExtraOnionSkinFrameIncrement;
                        if (frame < 0 || frame > lastFrame) break;
                        var oso = SetFrame((i + 1), VAW.VA.GetFrameTime(frame));
                        oso.SetColor(GetSlotColor(i + 1));
                    }
                }
                #endregion
                #region Prev
                {
                    var frame = VAW.VA.UAw.GetCurrentFrame();
                    for (int i = 0; i < VAW.EditorSettings.SettingExtraOnionSkinPrevCount; i++)
                    {
                        frame -= VAW.EditorSettings.SettingExtraOnionSkinFrameIncrement;
                        if (frame < 0 || frame > lastFrame) break;
                        var oso = SetFrame(-(i + 1), VAW.VA.GetFrameTime(frame));
                        oso.SetColor(GetSlotColor(-(i + 1)));
                    }
                }
                #endregion
                #endregion
            }

            foreach (var pair in onionSkinObjects)
            {
                if (pair.Value.active)
                    continue;
                pair.Value.dummyObject.GameObject.SetActive(false);
            }

            SaveState(clip, time, enabled, show);
        }

        private OnionSkinObject SetFrame(int frame, float time)
        {
            if (!onionSkinObjects.TryGetValue(frame, out OnionSkinObject oso))
            {
                oso = new OnionSkinObject(VAW.GameObject);
                {
                    const int QueueOffset = 300;
                    var offset = Math.Abs(frame);
                    offset = offset * 2 + (frame > 0 ? 1 : 0);
                    oso.SetRenderQueue((int)RenderQueue.Transparent - QueueOffset + offset);
                }
                onionSkinObjects.Add(frame, oso);
            }

            oso.active = true;
            if (!oso.dummyObject.GameObject.activeSelf)
                oso.dummyObject.GameObject.SetActive(true);
            oso.dummyObject.UpdateState();
            oso.dummyObject.SetTransformStart();
            oso.dummyObject.SampleAnimation(VAW.VA.CurrentClip, time);
            oso.dummyObject.UpdateTransparentDepth();

            if (EditorApplication.isPlaying && EditorApplication.isPaused) //Is there a bug that will not be updated while pausing? Therefore, it forcibly updates it.
                oso.dummyObject.RendererForceUpdate();

            return oso;
        }
    }
}
