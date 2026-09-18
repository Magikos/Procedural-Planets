using System;

namespace VeryAnimation
{
    internal sealed class ULayerSettingsWindow
    {
        public Type WindowType { get; private set; }

        public ULayerSettingsWindow()
        {
            WindowType = ReflectionCommon.GetUnityEditorType("UnityEditor.Graphs.LayerSettingsWindow");
        }
    }
}
