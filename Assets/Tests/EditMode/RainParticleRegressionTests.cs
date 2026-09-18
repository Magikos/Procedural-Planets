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

        [StructLayout(LayoutKind.Sequential)]
        struct CollisionShape
        {
            public Matrix4x4 Inverse;
            public Vector4 Center;
            public Vector4 Size;
        }

        [Test]
        public void DropStopsAtThinRoofBeforeGround()
        {
            var drop = Step(new Drop { Position = new Vector3(0,1050,0), Life = 1 },
                new Vector3(0,1050,0), Vector3.forward, roof: true);
            Assert.Less(drop.Life, 0);
            Assert.That(drop.Position.y, Is.InRange(1049.09f,1049.15f));
        }

        [Test]
        public void DropStopsAtSurfaceAndDoesNotImmediatelyRespawn()
        {
            var drop = Step(new Drop { Position = new Vector3(0,1050,0), Life = 1 },
                new Vector3(0,1050,0), Vector3.forward, terrain: 1049);
            Assert.Less(drop.Life, 0);
            Assert.That(drop.Position.magnitude, Is.InRange(1049,1049.04f));
            var next = Step(drop, new Vector3(0,1050,0), Vector3.forward, terrain: 1049);
            Assert.That(Vector3.Distance(next.Position,drop.Position), Is.LessThan(.0001f));
        }

        [Test]
        public void DropStopsAtPlayerCapsule()
        {
            var drop = Step(new Drop { Position = new Vector3(0,1050,0), Life = 1 },
                new Vector3(0,1050,0), Vector3.forward, roof: true, shapeType: 1);
            Assert.Less(drop.Life, 0);
            Assert.That(drop.Position.y, Is.InRange(1049.5f,1049.65f));
        }

        [Test]
        public void ColdBirthRemainsSnowWhenSnowRenderingIsDisabled()
        {
            var drop = Step(default, new Vector3(0,1050,0), Vector3.forward, reset: true, temperature: -10);
            Assert.AreEqual(1,drop.Padding);
        }

        [Test]
        public void DropHitsLakeAboveTerrain()
        {
            var drop = Step(new Drop {Position=new Vector3(0,1050,0),Life=1},
                new Vector3(0,1050,0),Vector3.forward,terrain:1040,lake:1049);
            Assert.Less(drop.Life,0);
            Assert.AreEqual(2,drop.Padding);
            Assert.That(drop.Position.magnitude,Is.InRange(1049,1049.04f));
        }

        [Test]
        public void DropUsesUnshiftedTerrainFaceCoordinates()
        {
            var drop = Step(new Drop {Position=new Vector3(0,1051,0),Life=1},
                new Vector3(0,1051,0),Vector3.forward,gradient:true);
            Assert.Less(drop.Life,0);
            Assert.That(drop.Position.y,Is.InRange(1050,1050.06f));
        }

        [Test]
        public void DropStopsAtMeshTriangle()
        {
            var drop = Step(new Drop {Position=new Vector3(0,1050,0),Life=1},
                new Vector3(0,1050,0),Vector3.forward,roof:true,shapeType:2);
            Assert.Less(drop.Life,0);
            Assert.That(drop.Position.y,Is.InRange(1049,1049.04f));
        }

        static Drop Step(Drop initial, Vector3 camera, Vector3 forward, bool reset = false, bool roof = false, float terrain = 0f, int shapeType = 0, float temperature = 20f, float lake = 0f, bool gradient = false)
        {
            var asset = Resources.Load<ComputeShader>("RainParticleUpdate");
            Assert.IsNotNull(asset);
            var compute = Object.Instantiate(asset);
            using var buffer = new ComputeBuffer(1, Marshal.SizeOf<Drop>());
            using var triangles = new ComputeBuffer(3,12);
            using var colliderBuffer = new ComputeBuffer(1, 96);
            colliderBuffer.SetData(new[] { new CollisionShape { Inverse = Matrix4x4.Translate(new Vector3(0,-1049,0)), Center = new Vector4(0,0,0,shapeType == 2 ? 3 : shapeType), Size = shapeType == 1 ? new Vector4(.5f,.1f,0,0) : new Vector4(10,.1f,10,0) } });
            triangles.SetData(new[] { new Vector3(-10,0,-10), new Vector3(10,0,-10), new Vector3(0,0,10) });
            var atlas = new Texture2D(2,2,TextureFormat.RFloat,false);
            var water = new Texture2DArray(1,1,6,TextureFormat.RFloat,false);
            var climate = new Texture2DArray(1,1,6,TextureFormat.RGFloat,false);
            try
            {
                buffer.SetData(new[] { initial });
                int kernel = compute.FindKernel("RainUpdate");
                var low = new Color(gradient ? 1040 : terrain,0,0,0);
                var high = new Color(gradient ? 1060 : terrain,0,0,0);
                var radiusPixels = new[] {low,high,low,high};
                atlas.SetPixels(radiusPixels); atlas.Apply();
                compute.SetInt("_RainSurfaceResolution", terrain > 0 || gradient ? 2 : 0);
                for (int i = 0; i < 6; i++) compute.SetTexture(kernel, "_RainSurfaceRadius" + i, atlas);
                GrassWaterFieldBinding.Bind(compute, kernel);
                for(int face=0;face<6;face++) water.SetPixels(new[] {new Color(lake/1000f-1,0,0,0)},face);
                water.Apply();
                compute.SetTexture(kernel,"_GrassWaterLevelTex",water);
                compute.SetFloat("_GrassWaterLevelBaseRadius",1000);
                compute.SetFloat("_GrassWaterSurfaceOffset",0);
                compute.SetInt("_GrassWaterLevelRes", lake > 0 ? 1 : 0);
                compute.SetInt("_RainColliderCount", roof ? 1 : 0);
                compute.SetBuffer(kernel, "_RainColliders", colliderBuffer);
                compute.SetBuffer(kernel, "_RainTriangles", triangles);
                compute.SetTexture(kernel, "_ClimateMap", climate);
                compute.SetFloat("_ClimateMapResolution", 0);
                compute.SetVector("_ClimateTemperatureRangeCelsius", new Vector4(temperature,temperature,0,0));
                compute.SetVector("_WeatherParticlePhaseParams", new Vector4(0,2,0,0));
                compute.SetVector("_WeatherParticleSnowParams", new Vector4(0,1,.05f,2));
                compute.SetFloat("_SnowEnabled", 0);
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
                Object.DestroyImmediate(climate);
                Object.DestroyImmediate(atlas);
                Object.DestroyImmediate(water);
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
