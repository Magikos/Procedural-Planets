using UnityEngine;
using UnityEngine.Rendering;

// World-scoped handle the URP ScatterRenderFeature resolves each frame to draw the scatter inside the
// opaque phase, so the scatter writes into the camera colour+depth (and thus _CameraDepthTexture).
// Registered by Planet.RegisterWorldServices; resolved via ServiceLocator.TryGet in the render feature.
public interface IScatterDrawRuntime
{
    bool HasDrawData { get; }
    void RecordDraws(RasterCommandBuffer cmd, Vector3 camPos);
}
