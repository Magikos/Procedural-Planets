using System;
using System.Reflection;
using UnityEditorInternal;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class UTimeControl
    {
        public object Instance { get; private set; }

        private readonly Func<object, float> dg_get_currentTime;
        private readonly Action<object, float> dg_set_currentTime;
        private readonly Action<float> dg_set_nextCurrentTime;
        private readonly Func<object, float> dg_get_startTime;
        private readonly Action<object, float> dg_set_startTime;
        private readonly Func<object, float> dg_get_stopTime;
        private readonly Action<object, float> dg_set_stopTime;
        private readonly Func<object, bool> dg_get_loop;
        private readonly Action<object, bool> dg_set_loop;
        private readonly Action dg_Update;
        private readonly Func<float> dg_get_deltaTime;
        private readonly Action<float> dg_set_deltaTime;
        private readonly Func<object, bool> dg_get_m_DeltaTimeSet;
        private readonly Func<bool> dg_get_playing;
        private readonly Action<bool> dg_set_playing;

        public UTimeControl(object instance)
        {
            this.Instance = instance;

            var timeControlType = ReflectionCommon.GetUnityEditorType("UnityEditor.TimeControl");
            Assert.IsNotNull(timeControlType);

            {
                var fi_currentTime = timeControlType.GetField("currentTime");
                Assert.IsNotNull(fi_currentTime);
                dg_get_currentTime = ReflectionCommon.CreateGetFieldDelegate<float>(fi_currentTime);
                dg_set_currentTime = ReflectionCommon.CreateSetFieldDelegate<float>(fi_currentTime);
            }
            {
                var pi_nextCurrentTime = timeControlType.GetProperty("nextCurrentTime");
                Assert.IsNotNull(pi_nextCurrentTime);
                dg_set_nextCurrentTime = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), instance, pi_nextCurrentTime.GetSetMethod());
            }
            {
                var fi_startTime = timeControlType.GetField("startTime");
                Assert.IsNotNull(fi_startTime);
                dg_get_startTime = ReflectionCommon.CreateGetFieldDelegate<float>(fi_startTime);
                dg_set_startTime = ReflectionCommon.CreateSetFieldDelegate<float>(fi_startTime);
            }
            {
                var fi_stopTime = timeControlType.GetField("stopTime");
                Assert.IsNotNull(fi_stopTime);
                dg_get_stopTime = ReflectionCommon.CreateGetFieldDelegate<float>(fi_stopTime);
                dg_set_stopTime = ReflectionCommon.CreateSetFieldDelegate<float>(fi_stopTime);
            }
            {
                var fi_loop = timeControlType.GetField("loop");
                Assert.IsNotNull(fi_loop);
                dg_get_loop = ReflectionCommon.CreateGetFieldDelegate<bool>(fi_loop);
                dg_set_loop = ReflectionCommon.CreateSetFieldDelegate<bool>(fi_loop);
            }

            {
                var mi_Update = timeControlType.GetMethod("Update");
                Assert.IsNotNull(mi_Update);
                dg_Update = (Action)Delegate.CreateDelegate(typeof(Action), instance, mi_Update);
            }
            {
                var pi_deltaTime = timeControlType.GetProperty("deltaTime");
                Assert.IsNotNull(pi_deltaTime);
                dg_get_deltaTime = (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), instance, pi_deltaTime.GetGetMethod());
                dg_set_deltaTime = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), instance, pi_deltaTime.GetSetMethod());
            }
            {
                var fi_m_DeltaTimeSet = timeControlType.GetField("m_DeltaTimeSet", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(fi_m_DeltaTimeSet);
                dg_get_m_DeltaTimeSet = ReflectionCommon.CreateGetFieldDelegate<bool>(fi_m_DeltaTimeSet);
            }
            {
                var pi_playing = timeControlType.GetProperty("playing");
                Assert.IsNotNull(pi_playing);
                dg_get_playing = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), instance, pi_playing.GetGetMethod());
                dg_set_playing = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), instance, pi_playing.GetSetMethod());
            }
        }

        public void Update()
        {
            dg_Update();
        }

        public float CurrentTime
        {
            get
            {
                return dg_get_currentTime(Instance);
            }
            set
            {
                dg_set_currentTime(Instance, value);
            }
        }
        public float NextCurrentTime
        {
            set
            {
                dg_set_nextCurrentTime(value);
            }
        }
        public float StartTime
        {
            get
            {
                return dg_get_startTime(Instance);
            }
            set
            {
                dg_set_startTime(Instance, value);
            }
        }
        public float StopTime
        {
            get
            {
                return dg_get_stopTime(Instance);
            }
            set
            {
                dg_set_stopTime(Instance, value);
            }
        }
        public bool IsLoop
        {
            get
            {
                return dg_get_loop(Instance);
            }
            set
            {
                dg_set_loop(Instance, value);
            }
        }
        public float DeltaTime
        {
            get
            {
                return dg_get_deltaTime();
            }
            set
            {
                dg_set_deltaTime(value);
            }
        }
        public bool GetDeltaTimeSet()
        {
            return dg_get_m_DeltaTimeSet(Instance);
        }
        public bool IsPlaying
        {
            get
            {
                return dg_get_playing();
            }
            set
            {
                dg_set_playing(value);
            }
        }
    }
}
