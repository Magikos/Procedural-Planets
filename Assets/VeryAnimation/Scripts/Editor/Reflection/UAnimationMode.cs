using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class UAnimationMode
    {
        readonly FieldInfo fi_onAnimationRecordingStart;
        readonly FieldInfo fi_onAnimationRecordingStop;
        private readonly Action<GameObject> dg_RevertPropertyModificationsForGameObject;

        public UAnimationMode()
        {
            var type = typeof(AnimationMode);

            Assert.IsNotNull(fi_onAnimationRecordingStart = type.GetField("onAnimationRecordingStart", BindingFlags.NonPublic | BindingFlags.Static));
            Assert.IsNotNull(fi_onAnimationRecordingStop = type.GetField("onAnimationRecordingStop", BindingFlags.NonPublic | BindingFlags.Static));
            Assert.IsNotNull(dg_RevertPropertyModificationsForGameObject = (Action<GameObject>)Delegate.CreateDelegate(typeof(Action<GameObject>), null, type.GetMethod("RevertPropertyModificationsForGameObject", BindingFlags.NonPublic | BindingFlags.Static)));
        }

        public Action GetOnAnimationRecordingStart()
        {
            return (Action)fi_onAnimationRecordingStart.GetValue(null);
        }
        public void SetOnAnimationRecordingStart(Action action)
        {
            fi_onAnimationRecordingStart.SetValue(null, action);
        }

        public Action GetOnAnimationRecordingStop()
        {
            return (Action)fi_onAnimationRecordingStop.GetValue(null);
        }
        public void SetOnAnimationRecordingStop(Action action)
        {
            fi_onAnimationRecordingStop.SetValue(null, action);
        }

        public void RevertPropertyModificationsForGameObject(GameObject gameObject)
        {
            if (gameObject == null) return;
            dg_RevertPropertyModificationsForGameObject(gameObject);
        }
    }
}
