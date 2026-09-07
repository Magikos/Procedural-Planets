using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// URP excludes transparent queues from object motion rendering. Grass draws after water compositing,
// so its motion pass must run explicitly after that geometry has written the scene depth.
public sealed class VegetationMotionRenderFeature : ScriptableRendererFeature
{
    static readonly int PreviousWindId = Shader.PropertyToID(ShaderGlobalIds.VegetationPreviousWind);
    static readonly int PreviousTimeId = Shader.PropertyToID(ShaderGlobalIds.VegetationPreviousTime);
    static readonly int PreviousCameraId = Shader.PropertyToID(ShaderGlobalIds.VegetationPreviousCamera);
    static readonly int WindId = Shader.PropertyToID(ShaderGlobalIds.WindDirection);
    static readonly int SpeedId = Shader.PropertyToID(ShaderGlobalIds.WindSpeedMps);
    static readonly int StrengthId = Shader.PropertyToID(ShaderGlobalIds.WindStrength01);
    static readonly int GameTimeId = Shader.PropertyToID(ShaderGlobalIds.GameTime);
    static readonly List<Draw> Draws = new();
    static int _drawFrame = -1;
    readonly Dictionary<Camera, History> _history = new();
    MotionPass _pass;

    struct Draw
    {
        public RenderParams Parameters;
        public GraphicsBuffer Arguments;
        public GraphicsBuffer Instances;
    }

    struct History
    {
        public int Frame;
        public Vector4 Wind;
        public Vector4 Time;
        public Vector3 CameraPosition;
    }

    internal static void RecordDraw(RenderParams parameters, GraphicsBuffer arguments, GraphicsBuffer instances)
    {
        if (_drawFrame != Time.frameCount)
        {
            Draws.Clear();
            _drawFrame = Time.frameCount;
        }
        Draws.Add(new Draw { Parameters = parameters, Arguments = arguments, Instances = instances });
    }

    public override void Create() => _pass = new MotionPass();

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera camera = renderingData.cameraData.camera;
        if (camera.cameraType != CameraType.Game) return;
        Vector4 wind = Shader.GetGlobalVector(WindId);
        wind.w = Shader.GetGlobalFloat(SpeedId);
        var current = new History
        {
            Frame = Time.frameCount,
            Wind = wind,
            Time = new Vector4(Time.time, Shader.GetGlobalFloat(GameTimeId), Shader.GetGlobalFloat(StrengthId), 1f),
            CameraPosition = camera.transform.position,
        };
        if (!_history.TryGetValue(camera, out History previous) || previous.Frame != Time.frameCount - 1)
        {
            previous = current;
            previous.Time.w = 0f;
        }
        Shader.SetGlobalVector(PreviousWindId, previous.Wind);
        Shader.SetGlobalVector(PreviousTimeId, previous.Time);
        Shader.SetGlobalVector(PreviousCameraId, previous.CameraPosition);
        _history[camera] = current;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _history.Clear();
        Draws.Clear();
        Shader.SetGlobalVector(PreviousTimeId, Vector4.zero);
    }

    sealed class MotionPass : ScriptableRenderPass
    {
        sealed class PassData { public List<Draw> Draws; }

        public MotionPass() => renderPassEvent = RenderPassEvent.AfterRenderingTransparents;

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (_drawFrame != Time.frameCount || !resources.motionVectorColor.IsValid()
                || !resources.activeDepthTexture.IsValid()) return;
            var draws = new List<Draw>();
            foreach (Draw draw in Draws)
                if (draw.Parameters.camera == cameraData.camera && draw.Arguments.IsValid() && draw.Instances.IsValid())
                    draws.Add(draw);
            if (draws.Count == 0) return;
            using var builder = graph.AddRasterRenderPass<PassData>("Grass motion vectors", out var data);
            data.Draws = draws;
            builder.SetRenderAttachment(resources.motionVectorColor, 0, AccessFlags.ReadWrite);
            builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
            foreach (Draw draw in draws)
            {
                builder.UseBuffer(graph.ImportBuffer(draw.Arguments), AccessFlags.Read);
                builder.UseBuffer(graph.ImportBuffer(draw.Instances), AccessFlags.Read);
            }
            builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
            {
                foreach (Draw draw in pass.Draws)
                {
                    int shaderPass = draw.Parameters.material.FindPass("MotionVectors");
                    if (shaderPass < 0) continue;
                    context.cmd.DrawProceduralIndirect(Matrix4x4.identity, draw.Parameters.material, shaderPass,
                        MeshTopology.Triangles, draw.Arguments, 0, draw.Parameters.matProps);
                }
            });
        }
    }
}
