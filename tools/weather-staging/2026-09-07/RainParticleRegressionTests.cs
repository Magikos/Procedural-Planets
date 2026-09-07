using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class RainParticleRegressionTests
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Drop
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Life;
            public float Padding;
        }

        static Drop Step(Drop initial, Vector3 camera, Vector3 forward, bool reset = false)
        {
            var asset = Resources.Load<ComputeShader>("RainParticleUpdate");
            Assert.IsNotNull(asset);
            var compute = Object.Instantiate(asset);
            using var buffer = new ComputeBuffer(1, Marshal.SizeOf<Drop>());
            try
            {
                buffer.SetData(new[] { initial });
                int kernel = compute.FindKernel("RainUpdate");
                compute.SetBuffer(kernel, "_RainParticles", buffer);
                compute.SetInt("_ActiveCount", 1);
                compute.SetInt("_ResetParticles", reset ? 1 : 0);
                compute.SetInt("_FrameSeed", 17);
                compute.SetVector("_PlanetCenter", Vector3.zero);
                compute.SetFloat("_SeaRadius", 1000f);
                compute.SetFloat("_CloudBottomRadius", 1400f);
                compute.SetFloat("_CloudTopRadius", 1460f);
                compute.SetVector("_CameraPosition", camera);
                compute.SetVector("_CameraForward", forward);
                compute.SetFloat("_CameraNearRadius", 100f);
                compute.SetFloat("_ForwardConeBias", 0.35f);
                compute.SetVector("_WindDirection", Vector3.right);
                compute.SetFloat("_WindSpeedMps", 9f);
                compute.SetFloat("_WindCoupling", 0.2f);
                compute.SetFloat("_FallSpeedMps", 20f);
                compute.SetFloat("_DeltaTime", 0.1f);
                compute.Dispatch(kernel, 1, 1, 1);
                var result = new Drop[1];
                buffer.GetData(result);
                return result[0];
            }
            finally
            {
                Object.DestroyImmediate(compute);
            }
        }

        [Test]
        public void ExistingDropTrajectoryDoesNotDependOnCameraMotion()
        {
            var initial = new Drop { Position = new Vector3(0f, 1050f, 0f), Life = 1f };
            Drop stationary = Step(initial, new Vector3(0f, 1050f, 0f), Vector3.forward);
            Drop moved = Step(initial, new Vector3(15f, 1050f, 10f), Vector3.left);
            Assert.Less(Vector3.Distance(stationary.Position, moved.Position), 0.0001f);
            Assert.Less(Vector3.Distance(stationary.Velocity, moved.Velocity), 0.0001f);
            Assert.Less(stationary.Position.y, initial.Position.y);
            Assert.Greater(stationary.Position.x, initial.Position.x);
        }

        [Test]
        public void ResetSeedsNearObserverEvenWhenCloudBaseIsFarAway()
        {
            Vector3 camera = new Vector3(0f, 1002f, 0f);
            Drop reset = Step(new Drop { Position = Vector3.one * 99999f, Life = 1f },
                camera, Vector3.up, true);
            Assert.Less(Vector3.Distance(reset.Position, camera), 101f);
            Assert.GreaterOrEqual(reset.Position.magnitude, 1000f);
            Assert.Greater(reset.Life, 0f);
            Assert.IsFalse(float.IsNaN(reset.Position.x));
            Assert.IsFalse(float.IsNaN(reset.Velocity.x));
        }

        [Test]
        public void MatchingCameraAndDropMotionDoesNotDragDrop()
        {
            var drop = new Drop { Position = new Vector3(0f, 1050f, 0f), Life = 1f };
            Vector3 camera = new Vector3(0f, 1050f, 10f);
            for (int i = 0; i < 10; i++)
            {
                Drop fixedCamera = Step(drop, camera, Vector3.forward);
                Drop movingCamera = Step(drop, camera + Vector3.right * 10f, Vector3.back);
                Assert.Less(Vector3.Distance(fixedCamera.Position, movingCamera.Position), 0.0001f);
                drop = movingCamera;
            }
        }
    }
}
