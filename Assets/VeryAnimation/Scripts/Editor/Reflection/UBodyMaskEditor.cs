using System;
using System.Reflection;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class UBodyMaskEditor
    {
        private readonly Func<object, Color[]> dg_get_m_MaskBodyPartPicker;

        public UBodyMaskEditor()
        {
            var bodyMaskEditorType = ReflectionCommon.GetUnityEditorType("UnityEditor.BodyMaskEditor");
            {
                var fi_m_MaskBodyPartPicker = bodyMaskEditorType.GetField("m_MaskBodyPartPicker", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(dg_get_m_MaskBodyPartPicker = ReflectionCommon.CreateGetFieldDelegate<Color[]>(fi_m_MaskBodyPartPicker));
            }
        }

        public Color[] GetMaskBodyPartPicker()
        {
            return dg_get_m_MaskBodyPartPicker(null);
        }
    }
}
