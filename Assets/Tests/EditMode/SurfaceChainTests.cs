using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class SurfaceChainTests
    {
        sealed class Slope : IGroundingProvider
        {
            public bool TryGround(Vector3 p, Vector3 down, float offset, out GroundResult result)
            {
                result = new GroundResult(new Vector3(p.x, p.z * .2f + offset, p.z), new Vector3(0, 1, -.2f).normalized);
                return true;
            }
        }

        [Test]
        public void ArticulatedSupportPreservesSlitherAndRestoresAuthoredPose()
        {
            var root = new GameObject("surface fixture");
            try
            {
                var bones = new Transform[4];
                for (int i = 0; i < bones.Length; i++)
                {
                    bones[i] = new GameObject("segment " + i).transform;
                    bones[i].SetParent(i == 0 ? root.transform : bones[i - 1], false);
                    bones[i].position = new Vector3(Mathf.Sin(i) * .15f, .1f, i * .3f);
                }
                var positions = new Vector3[bones.Length];
                var rotations = new Quaternion[bones.Length];
                for (int i = 0; i < bones.Length; i++) { positions[i] = bones[i].position; rotations[i] = bones[i].rotation; }
                var definition = root.AddComponent<ProceduralRigDefinition>();
                definition.SurfaceChains = new[] { new SurfaceChainDefinition { Bones = bones, Clearance = .1f, MaxCorrection = .5f } };
                var pose = new ProceduralPoseRig(root.transform, definition);
                for (int frame = 0; frame < 3; frame++)
                {
                    pose.RestoreAnimation(); pose.CaptureAnimation();
                    pose.Tick(Vector3.up, Vector3.forward, new Slope(), 0, 1, 0, .1f);
                    for (int i = 0; i < bones.Length; i++)
                    {
                        Assert.That(bones[i].position.x, Is.EqualTo(positions[i].x).Within(.0001f));
                        Assert.That(bones[i].position.z, Is.EqualTo(positions[i].z).Within(.0001f));
                        Assert.That(bones[i].position.y, Is.EqualTo(positions[i].z * .2f + .1f).Within(.0001f));
                    }
                }
                pose.RestoreAnimation();
                for (int i = 0; i < bones.Length; i++)
                {
                    Assert.That(Vector3.Distance(bones[i].position, positions[i]), Is.LessThan(.0001f));
                    Assert.That(Quaternion.Angle(bones[i].rotation, rotations[i]), Is.LessThan(.001f));
                }
                pose.SurfaceEnabled = false;
                float supportedOffset = positions[3].z * .2f;
                pose.Tick(Vector3.up, Vector3.forward, new Slope(), 0, 1, 0, 1f / 60f);
                float remaining = Vector3.Distance(bones[3].position, positions[3]);
                Assert.That(remaining, Is.GreaterThan(0f).And.LessThan(supportedOffset));
                for (int frame = 0; frame < 120; frame++)
                {
                    pose.RestoreAnimation(); pose.CaptureAnimation();
                    pose.Tick(Vector3.up, Vector3.forward, new Slope(), 0, 1, 0, 1f / 60f);
                }
                Assert.That(Vector3.Distance(bones[3].position, positions[3]), Is.LessThan(.0001f));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
