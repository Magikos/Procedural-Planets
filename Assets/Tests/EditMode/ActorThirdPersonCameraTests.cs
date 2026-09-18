using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorThirdPersonCameraTests
    {
        sealed class Floor : IGravityProvider, IGroundingProvider
        {
            public Vector3 Up;
            public bool TryGetGravity(Vector3 p, out Vector3 acceleration) { acceleration = -Up * 9.81f; return true; }
            public bool TryGround(Vector3 p, Vector3 down, float offset, out GroundResult result)
            { result = new GroundResult(p - Up * Vector3.Dot(p, Up), Up); return true; }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LookSteersFacingWhileStrafeAndReverseKeepFacing(bool sideways)
        {
            var floor = new Floor { Up = sideways ? Vector3.right : Vector3.up };
            var seed = new CharacterPose(Vector3.zero, floor.Up, Vector3.forward);
            var camera = new ActorThirdPersonCamera(0); camera.Reset(seed);
            camera.Look(new Vector2(90f, 0f), floor.Up, 1f);
            var motor = new SurfaceCharacterController(floor, floor, 0f, seed);
            var pose = motor.Tick(Vector2.right, camera.Forward, 2f, .5f);
            Assert.Less(Vector3.Distance(Vector3.Cross(floor.Up, camera.Forward), pose.Position), .001f);
            Assert.Less(Vector3.Angle(camera.Forward, pose.Forward), .01f);
            Vector3 start = pose.Position;
            pose = motor.Tick(Vector2.down, camera.Forward, 2f, .5f);
            Assert.Less(Vector3.Distance(start - camera.Forward, pose.Position), .001f);
            Assert.Less(Vector3.Angle(camera.Forward, pose.Forward), .01f);
            camera.Look(new Vector2(0f, 10000f), floor.Up, 1f);
            Assert.AreEqual(-60f, camera.Pitch);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CameraCanOrbitAnActorWhoseTraversalFacingIsFixed(bool sideways)
        {
            var root = new GameObject("Traversal camera fixture");
            using var follow = new ActorThirdPersonCamera(0);
            try
            {
                var camera = root.AddComponent<Camera>();
                var pose = new CharacterPose(Vector3.one * 1000f, sideways ? Vector3.right : Vector3.up, Vector3.forward);
                follow.Reset(pose);
                follow.Look(new Vector2(90f, 0f), pose.Up, 1f);
                for (int i = 0; i < 100; i++) follow.Follow(camera, pose, 1.3f, 4f, .02f);
                Vector3 offset = Vector3.ProjectOnPlane(camera.transform.position - pose.Position, pose.Up).normalized;
                Assert.Less(Vector3.Angle(offset, -follow.Forward), .1f);
                Assert.Greater(Vector3.Angle(offset, -pose.Forward), 89f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CameraStaysBehindActorAndRetractsBeforeWallThenRecovers(bool sideways)
        {
            var root = new GameObject("Camera fixture");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            using var follow = new ActorThirdPersonCamera(1 << 0);
            try
            {
                var camera = root.AddComponent<Camera>(); camera.nearClipPlane = .05f;
                var pose = new CharacterPose(new Vector3(1000f, 1000f, 1000f), sideways ? Vector3.right : Vector3.up, Vector3.forward);
                follow.Reset(pose);
                follow.Follow(camera, pose, 1.3f, 4f, .02f);
                Vector3 focus = pose.Position + pose.Up * 1.3f;
                Assert.AreEqual(4f, Vector3.Distance(focus, camera.transform.position), .001f);
                Assert.Less(Vector3.Dot(camera.transform.position - focus, pose.Forward), 0f);
                Assert.Greater(Vector3.Dot(camera.transform.up, pose.Up), .95f);
                wall.transform.position = focus - Vector3.forward * 2f;
                wall.transform.rotation = Quaternion.LookRotation(pose.Forward, pose.Up);
                wall.transform.localScale = new Vector3(6f, 6f, .2f); Physics.SyncTransforms();
                follow.Follow(camera, pose, 1.3f, 4f, .02f);
                float blocked = Vector3.Distance(focus, camera.transform.position);
                Assert.Less(blocked, 2f);
                Object.DestroyImmediate(wall); Physics.SyncTransforms();
                follow.Follow(camera, pose, 1.3f, 4f, .02f);
                Assert.Greater(Vector3.Distance(focus, camera.transform.position), blocked);
                Assert.Less(Vector3.Distance(focus, camera.transform.position), 4f);
                for (int i = 0; i < 100; i++) follow.Follow(camera, pose, 1.3f, 4f, .02f);
                Assert.AreEqual(4f, Vector3.Distance(focus, camera.transform.position), .002f);
                follow.SetActive(false);
                Assert.IsFalse(((Behaviour)camera.GetComponent("CinemachineBrain")).enabled);
                follow.SetActive(true);
                Assert.IsTrue(((Behaviour)camera.GetComponent("CinemachineBrain")).enabled);
                follow.Dispose();
                Assert.IsNull(camera.GetComponent("CinemachineBrain"));
            }
            finally { Object.DestroyImmediate(root); if (wall != null) Object.DestroyImmediate(wall); }
        }
    }
}
